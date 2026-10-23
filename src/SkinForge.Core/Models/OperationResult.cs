namespace SkinForge.Core.Models;

public class OperationResult
{
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }
    public string? ErrorCode { get; }
    public ApplicationError? Error { get; }

    protected OperationResult(bool isSuccess, string? errorMessage = null, string? errorCode = null, ApplicationError? error = null)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage ?? error?.Message;
        ErrorCode = errorCode ?? error?.Code;
        Error = error;
    }

    public static OperationResult Success() => new(true);
    public static OperationResult Failure(string errorMessage, string? errorCode = null) => new(false, errorMessage, errorCode);
    public static OperationResult Failure(ApplicationError error) => new(false, error.Message, error.Code, error);

    public static OperationResult<T> Success<T>(T data) => OperationResult<T>.Success(data);
    public static OperationResult<T> Failure<T>(string errorMessage, string? errorCode = null) => OperationResult<T>.Failure(errorMessage, errorCode);
    public static OperationResult<T> Failure<T>(ApplicationError error) => OperationResult<T>.Failure(error);
}

public class OperationResult<T> : OperationResult
{
    public T? Data { get; }

    private OperationResult(bool isSuccess, T? data = default, string? errorMessage = null, string? errorCode = null, ApplicationError? error = null)
        : base(isSuccess, errorMessage, errorCode, error)
    {
        Data = data;
    }

    public static OperationResult<T> Success(T data) => new(true, data);
    public new static OperationResult<T> Failure(string errorMessage, string? errorCode = null) => new(false, default, errorMessage, errorCode);
    public new static OperationResult<T> Failure(ApplicationError error) => new(false, default, error.Message, error.Code, error);
}
