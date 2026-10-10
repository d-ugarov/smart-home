using Microsoft.Extensions.Logging;

namespace SmartHome.Core.Common;

/// <summary>
/// Runs an operation and turns any exception into a failed <see cref="OperationResult"/>: callers check the
/// result instead of catching. The failure is logged here, once.
/// </summary>
public static class InvokeOperations
{
    public static async Task<OperationResult> InvokeAsync(ILogger logger, string operation, Func<Task> action)
    {
        try
        {
            await action();
            return OperationResult.Ok();
        }
        catch (Exception e)
        {
            Log(logger, operation, e);
            return OperationResult.Failed(e);
        }
    }

    public static async Task<OperationResult<T>> InvokeAsync<T>(ILogger logger, string operation, Func<Task<T>> action)
    {
        try
        {
            return OperationResult<T>.Ok(await action());
        }
        catch (Exception e)
        {
            Log(logger, operation, e);
            return OperationResult<T>.Failed(e);
        }
    }

    private static void Log(ILogger logger, string operation, Exception e)
    {
        if (e is OperationCanceledException)
            logger.LogDebug("{Operation}: cancelled", operation);
        else
            logger.LogError(e, "{Operation}: {Error}", operation, e.Message);
    }
}
