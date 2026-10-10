using SmartHome.Core.Devices;
using SmartHome.Core.Events;
using SmartHome.Core.State;

namespace SmartHome.Integrations.MegaD;

/// <summary>
/// After a restart the outputs of the controller are whatever its settings say: the state is read again. The light is
/// not restored, as in <c>master</c>.
/// </summary>
internal sealed class MegaDRestartHandler(MegaDBindings bindings, IDeviceGateway gateway, HomeState state) : IHomeEventHandler
{
    public static string DeviceId(MegaDController controller) => $"megad:{controller.Id}";

    public async Task HandleAsync(HomeEvent homeEvent, CancellationToken cancellationToken)
    {
        if (homeEvent is not DeviceRestarted restarted ||
            bindings.Controllers.Values.FirstOrDefault(x => DeviceId(x) == restarted.DeviceId) is not { } controller)
            return;

        var entities = bindings.EntitiesOf(controller).ToList();
        state.Forget(entities.Select(x => x.Id));
        state.SetAll(await gateway.ReadAsync(entities, cancellationToken), StateSource.Poll);
    }
}
