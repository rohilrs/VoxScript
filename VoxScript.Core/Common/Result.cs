namespace VoxScript.Core.Common;

public readonly record struct Result<T>
{
    public T? Value { get; }
    public string? Error { get; }
    public bool IsSuccess { get; }

    private Result(T value) { Value = value; IsSuccess = true; }
    private Result(string error) { Error = error; IsSuccess = false; }

    public static Result<T> Ok(T value) => new(value);
    public static Result<T> Fail(string error) => new(error);

    public Result<TOut> Map<TOut>(Func<T, TOut> f) =>
        IsSuccess ? Result<TOut>.Ok(f(Value!)) : Result<TOut>.Fail(Error!);
}

public readonly record struct Result
{
    public string? Error { get; }
    public bool IsSuccess { get; }

    private Result(bool success, string? error) { IsSuccess = success; Error = error; }

    public static Result Ok() => new(true, null);
    public static Result Fail(string error) => new(false, error);
}
