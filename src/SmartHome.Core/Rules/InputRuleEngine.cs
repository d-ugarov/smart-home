using Microsoft.Extensions.Logging;
using SmartHome.Core.Devices;
using SmartHome.Core.Entities;
using SmartHome.Core.Events;
using SmartHome.Core.Home;
using SmartHome.Core.State;
using SmartHome.Core.Configuration;

namespace SmartHome.Core.Rules;

/// <summary>
/// A press of a button, with the semantics of <c>master</c> (contract K1-K6):
/// <list type="number">
/// <item>the devices of all rules of the button are read once, fresh - all rules are checked on this reading,
/// commands of one rule do not change the conditions of the next one;</item>
/// <item>rules go in order; a rule fires when the press kind matches and its condition holds;</item>
/// <item>commands go one by one with their pauses; On/Off is skipped when the switch is already in that state,
/// Toggle is sent as is;</item>
/// <item><see cref="RuleResponse"/> of the last fired rule is the answer; a final rule stops the evaluation.</item>
/// </list>
/// The answer goes to the controller after the commands: if the server dies in the middle, the controller gets no
/// answer and runs its own scenario.
/// </summary>
public sealed class InputRuleEngine(
    HomeModel home,
    IDeviceGateway gateway,
    HomeState state,
    HomeEventBus events,
    TimeProvider timeProvider,
    ILogger<InputRuleEngine> logger)
{
    /// <returns> true when the press is handled by the server (200), false - the controller acts itself (204). </returns>
    public async Task<bool> HandleAsync(Entity button, PressKind press, CancellationToken cancellationToken)
    {
        var rules = home.RulesOf(button.Id);
        if (rules.Count == 0)
            return false;

        var snapshot = await gateway.ReadAsync(home.EntitiesReadOnPress(button.Id), cancellationToken);
        state.SetAll(snapshot, StateSource.Poll);

        var handled = false;

        foreach (var rule in rules)
        {
            if (!rule.Presses.Contains(press) || rule.Condition?.IsMet(snapshot) == false)
                continue;

            logger.LogDebug("{Button} {Press}: rule fired ({Condition})", button.Id, press, rule.Condition?.ToString() ?? "no condition");

            foreach (var action in rule.Actions)
            {
                if (action.DelayBefore > TimeSpan.Zero)
                    await Task.Delay(action.DelayBefore, timeProvider, cancellationToken);

                await ExecuteAsync(button, action, snapshot, cancellationToken);

                if (action.DelayAfter > TimeSpan.Zero)
                    await Task.Delay(action.DelayAfter, timeProvider, cancellationToken);
            }

            handled = rule.Response == RuleResponse.Handled;

            if (rule.IsFinal)
                break;
        }

        return handled;
    }

    private async Task ExecuteAsync(Entity button, RuleAction action, IReadOnlyDictionary<string, EntityValue> snapshot, CancellationToken cancellationToken)
    {
        var known = snapshot.GetValueOrDefault(action.Target.Id) as OnOffValue;

        if (known != null && IsAlready(known.State, action.Command))
        {
            logger.LogInformation("Rule of {Button}: {Command} {Target} skipped, already {State}", button.Id, action.Command, action.Target.Id, known.State);
            events.Publish(new RuleCommandExecuted(button, action.Target, action.Command, new CommandOutcome.Skipped(known.State)));
            return;
        }

        var result = await gateway.SwitchAsync(action.Target, action.Command, cancellationToken);
        logger.LogInformation("Rule of {Button}: {Command} {Target}: {Result}", button.Id, action.Command, action.Target.Id, result.Report());
        events.Publish(new RuleCommandExecuted(button, action.Target, action.Command, new CommandOutcome.Sent(result)));

        if (result.IsSuccess && Expected(known?.State, action.Command) is { } expected)
            state.Set(action.Target.Id, new OnOffValue(expected), StateSource.Rule);
    }

    private static bool IsAlready(OnOff current, SwitchCommand command) =>
        (current, command) is (OnOff.On, SwitchCommand.On) or (OnOff.Off, SwitchCommand.Off);

    internal static OnOff? Expected(OnOff? before, SwitchCommand command) => command switch
    {
        SwitchCommand.On => OnOff.On,
        SwitchCommand.Off => OnOff.Off,
        SwitchCommand.Toggle when before is { } b => b == OnOff.On ? OnOff.Off : OnOff.On,
        _ => null,
    };
}
