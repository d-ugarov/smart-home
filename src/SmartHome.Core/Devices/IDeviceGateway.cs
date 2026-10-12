using SmartHome.Core.Common;
using SmartHome.Core.Entities;

namespace SmartHome.Core.Devices;

/// <summary> Hardware behind the entities, implemented by an integration. Methods do not throw. </summary>
public interface IDeviceGateway
{
    /// <summary>
    /// Reads the current values from the devices, one request per device. Entities of a device that did not answer
    /// are missing from the result.
    /// </summary>
    Task<IReadOnlyDictionary<string, EntityValue>> ReadAsync(IReadOnlyCollection<Entity> entities, CancellationToken cancellationToken);

    Task<OperationResult> SwitchAsync(Entity entity, SwitchCommand command, CancellationToken cancellationToken);

    /// <summary> Starts the open or close pulse of a valve. </summary>
    Task<OperationResult> RunValveAsync(Entity valve, ValveCommand command, CancellationToken cancellationToken);

    /// <summary> The sensor whose input closes the valve by a scenario of the controller (leak protection); null - none. </summary>
    Entity? LeakSensorOf(Entity valve);

    /// <summary> The counter of the input the meter is bound to; null - not bound. Its count is read as <see cref="CounterValue"/>. </summary>
    PulseCounter? CounterOf(Entity meter);
}

/// <param name="Modulus"> The counter goes round: after <c>Modulus - 1</c> comes 0. </param>
/// <param name="CountsPerPulse"> Steps of the counter per pulse of the meter: an input that counts both edges of the contact makes 2. </param>
public sealed record PulseCounter(int Modulus, int CountsPerPulse);
