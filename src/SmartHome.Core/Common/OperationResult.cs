namespace SmartHome.Core.Common;

/// <summary> Outcome of an operation that does not throw: success, or an error message with the exception behind it. </summary>
public class OperationResult
{
    protected OperationResult(bool isSuccess, string? error, Exception? exception)
    {
        IsSuccess = isSuccess;
        Error = error;
        Exception = exception;
    }

    public bool IsSuccess { get; }
    public string? Error { get; }
    public Exception? Exception { get; }

    public static OperationResult Ok() => new(true, null, null);

    public static OperationResult Failed(string error, Exception? exception = null) => new(false, error, exception);

    public static OperationResult Failed(Exception exception) => new(false, exception.Message, exception);

    public string Report() => IsSuccess ? "OK" : $"Failed ({Error})";

    public override string ToString() => Report();
}

public sealed class OperationResult<T> : OperationResult
{
    private OperationResult(bool isSuccess, T? data, string? error, Exception? exception) : base(isSuccess, error, exception)
    {
        Data = data;
    }

    /// <summary> Set when <see cref="OperationResult.IsSuccess"/>. </summary>
    public T? Data { get; }

    public static OperationResult<T> Ok(T data) => new(true, data, null, null);

    public new static OperationResult<T> Failed(string error, Exception? exception = null) => new(false, default, error, exception);

    public new static OperationResult<T> Failed(Exception exception) => new(false, default, exception.Message, exception);
}
