using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Responses;

namespace AdminDesk.SharedKernel.Exceptions;

// Base for typed errors; carries the error code and HTTP status the exception middleware writes out.
public class AppException : Exception
{
    public string Code { get; }
    public int HttpStatus { get; }

    public AppException(string code, string message, int httpStatus) : base(message)
    {
        Code = code;
        HttpStatus = httpStatus;
    }
}

public class ValidationException : AppException
{
    public IReadOnlyList<FieldError> FieldErrors { get; }

    public ValidationException(
        IEnumerable<FieldError> fieldErrors, string message = "One or more fields are invalid.",
        string code = ErrorCodes.VALIDATION_FAILED)
        : base(code, message, 400)
    {
        FieldErrors = fieldErrors.ToList();
    }

    public ValidationException(string field, string message, string code = ErrorCodes.VALIDATION_FAILED)
        : this(new[] { new FieldError { Field = field, Message = message } }, message, code)
    {
    }
}

public class NotFoundException : AppException
{
    public NotFoundException(string message, string code = ErrorCodes.NOT_FOUND)
        : base(code, message, 404)
    {
    }
}

public class ForbiddenException : AppException
{
    public ForbiddenException(string message, string code = ErrorCodes.FORBIDDEN)
        : base(code, message, 403)
    {
    }
}

public class ConflictException : AppException
{
    public ConflictException(string message, string code = ErrorCodes.STATE_CONFLICT)
        : base(code, message, 409)
    {
    }
}

public class DomainRuleException : AppException
{
    public DomainRuleException(string code, string message)
        : base(code, message, 422)
    {
    }
}

// The message names the file and the problem.
public class DefinitionLoadException : AppException
{
    public DefinitionLoadException(string message)
        : base(ErrorCodes.DEFINITION_INVALID, message, 500)
    {
    }
}
