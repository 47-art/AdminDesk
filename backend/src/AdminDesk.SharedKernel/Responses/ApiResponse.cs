namespace AdminDesk.SharedKernel.Responses;

public class FieldError
{
    public string Field { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class ApiError
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<FieldError> FieldErrors { get; set; } = new();
    public string? CorrelationId { get; set; }
}

public class ApiResponse
{
    public bool Success { get; set; }
    public ApiError? Error { get; set; }

    public static ApiResponse Ok() => new() { Success = true };

    public static ApiResponse Fail(string code, string message, IEnumerable<FieldError>? fieldErrors = null, string? correlationId = null) =>
        new()
        {
            Success = false,
            Error = new ApiError
            {
                Code = code,
                Message = message,
                FieldErrors = fieldErrors?.ToList() ?? new List<FieldError>(),
                CorrelationId = correlationId
            }
        };
}

public class ApiResponse<T> : ApiResponse
{
    public T? Data { get; set; }

    public static ApiResponse<T> Ok(T data) => new() { Success = true, Data = data };

    public new static ApiResponse<T> Fail(string code, string message, IEnumerable<FieldError>? fieldErrors = null, string? correlationId = null) =>
        new()
        {
            Success = false,
            Error = new ApiError
            {
                Code = code,
                Message = message,
                FieldErrors = fieldErrors?.ToList() ?? new List<FieldError>(),
                CorrelationId = correlationId
            }
        };
}
