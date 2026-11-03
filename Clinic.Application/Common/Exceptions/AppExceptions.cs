namespace Clinic.Application.Common.Exceptions;

/// 404
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
    public NotFoundException(string entity, object key) : base($"{entity} ({key}) was not found.") { }
}

/// 401 (بيانات دخول غلط أو مفيش هوية)
public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message = "Authentication is required.") : base(message) { }
}

/// 403
public class ForbiddenException : Exception
{
    public ForbiddenException(string message = "You do not have access to this resource.") : base(message) { }
}

/// 409 (حجز مكرر، كوبون خلص)
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}

/// 400
public class AppValidationException : Exception
{
    public IDictionary<string, string[]> Errors { get; }

    public AppValidationException(string message) : base(message)
    {
        Errors = new Dictionary<string, string[]>();
    }

    public AppValidationException(IDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }
}