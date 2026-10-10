using SmartHome.Core.Configuration;
using SmartHome.Core.Entities;

namespace SmartHome.Core.Rules;

public sealed record InputRule(
    Entity Button,
    IReadOnlySet<PressKind> Presses,
    RuleCondition? Condition,
    IReadOnlyList<RuleAction> Actions,
    RuleResponse Response,
    bool IsFinal);

public sealed record RuleAction(Entity Target, SwitchCommand Command, TimeSpan DelayBefore, TimeSpan DelayAfter);

public sealed record RuleCondition(ConditionType Type, OnOff? State, IReadOnlyList<Entity> Switches)
{
    /// <summary>
    /// Checked on the states that could be read: a switch of an unreachable controller is left out, as in <c>master</c>
    /// (so "all are" holds and "any is" fails when nothing could be read).
    /// </summary>
    public bool IsMet(IReadOnlyDictionary<string, EntityValue> snapshot)
    {
        var states = Switches.Select(x => snapshot.GetValueOrDefault(x.Id))
                             .OfType<OnOffValue>()
                             .Select(x => x.State)
                             .ToList();

        return Type switch
        {
            ConditionType.AllAre => states.All(x => x == State),
            ConditionType.AnyIs => states.Any(x => x == State),
            ConditionType.NoneIs => states.All(x => x != State),
            ConditionType.AllSame => states.Distinct().Count() == 1,
            ConditionType.Mixed => states.Distinct().Count() > 1,
            _ => throw new NotSupportedException($"Condition {Type}"),
        };
    }

    public override string ToString() => State is { } state
        ? $"{Type} {state}: {string.Join(", ", Switches.Select(x => x.Id))}"
        : $"{Type}: {string.Join(", ", Switches.Select(x => x.Id))}";
}
