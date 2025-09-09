
// Async Operation Error Handling Template
public async Task<Result<T>> ProcessAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
{
    try
    {
        var result = await operation();
        return Result<T>.Success(result);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        _logger.LogInformation("Operation was cancelled");
        return Result<T>.Cancelled();
    }
    catch (ArgumentException ex)
    {
        _logger.LogWarning(ex, "Invalid argument provided to operation");
        return Result<T>.Failure("Invalid input parameters");
    }
    catch (InvalidOperationException ex)
    {
        _logger.LogWarning(ex, "Operation not valid in current state");
        return Result<T>.Failure("Operation not currently available");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Unexpected error during async operation");
        return Result<T>.Failure("An unexpected error occurred");
    }
}

public class Result<T>
{
    public bool IsSuccess { get; private set; }
    public bool IsCancelled { get; private set; }
    public T Value { get; private set; }
    public string ErrorMessage { get; private set; }
    
    public static Result<T> Success(T value) => new Result<T> { IsSuccess = true, Value = value };
    public static Result<T> Failure(string error) => new Result<T> { ErrorMessage = error };
    public static Result<T> Cancelled() => new Result<T> { IsCancelled = true };
}
