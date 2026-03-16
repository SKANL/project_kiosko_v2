namespace Nodus.Admin.Domain.Common;

/// <summary>
/// Discriminated union for error handling — no exceptions on expected failures.
/// Usage: return Result<T>.Ok(value) or Result<T>.Fail("reason")
/// </summary>
public sealed record Result<T>
{
    public T?     Value   { get; }
    public string? Error  { get; }
    public bool   IsOk    => Error is null;
    public bool   IsFail  => Error is not null;

    private Result(T? value, string? error) { Value = value; Error = error; }

    public static Result<T> Ok(T value)        => new(value, null);
    public static Result<T> Fail(string error) => new(default, error);

    public Result<TOut> Map<TOut>(Func<T, TOut> mapper) =>
        IsOk ? Result<TOut>.Ok(mapper(Value!)) : Result<TOut>.Fail(Error!);

    public override string ToString() =>
        IsOk ? $"Ok({Value})" : $"Fail({Error})";
}

/// <summary>Non-generic Result for operations that return no value.</summary>
public sealed record Result
{
    public string? Error => _error;
    public bool    IsOk  => _error is null;
    public bool    IsFail => _error is not null;

    private readonly string? _error;
    private Result(string? error) { _error = error; }

    public static Result Ok()              => new((string?)null);
    public static Result Fail(string err)  => new(err);

    public override string ToString() => IsOk ? "Ok" : $"Fail({_error})";
}
