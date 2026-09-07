namespace HR.Domain.Errors;

/// <summary>Base type for all domain errors. Serialised to API problem responses.</summary>
public abstract class DomainException : Exception
{
    protected DomainException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }

    public virtual int HttpStatus => 400;
}

public sealed class DocumentNotFoundError : DomainException
{
    public DocumentNotFoundError(string id) : base("document_not_found", $"Document '{id}' was not found.") { }
    public override int HttpStatus => 404;
}

public sealed class RunNotFoundError : DomainException
{
    public RunNotFoundError(string id) : base("run_not_found", $"Run '{id}' was not found.") { }
    public override int HttpStatus => 404;
}

public sealed class ApprovalNotFoundError : DomainException
{
    public ApprovalNotFoundError(string id) : base("approval_not_found", $"Approval request '{id}' was not found.") { }
    public override int HttpStatus => 404;
}

public sealed class SessionNotFoundError : DomainException
{
    public SessionNotFoundError(string id) : base("session_not_found", $"Session '{id}' was not found.") { }
    public override int HttpStatus => 404;
}

public sealed class InvalidStateError : DomainException
{
    public InvalidStateError(string message) : base("invalid_state", message) { }
}

public sealed class ValidationError : DomainException
{
    public ValidationError(string message) : base("validation_error", message) { }
}

public sealed class ApprovalRequiredError : DomainException
{
    public ApprovalRequiredError(string message) : base("approval_required", message) { }
    public override int HttpStatus => 409;
}

public sealed class LowEvidenceError : DomainException
{
    public LowEvidenceError(string message) : base("low_evidence", message) { }
}

public sealed class ProviderUnavailableError : DomainException
{
    public ProviderUnavailableError(string message) : base("provider_unavailable", message) { }
    public override int HttpStatus => 503;
}

public sealed class NotSupportedError : DomainException
{
    public NotSupportedError(string message) : base("not_supported", message) { }
    public override int HttpStatus => 501;
}

public sealed class PolicyViolationError : DomainException
{
    public PolicyViolationError(string message) : base("policy_violation", message) { }
    public override int HttpStatus => 403;
}

public sealed class UnauthorizedError : DomainException
{
    public UnauthorizedError(string message) : base("unauthorized", message) { }
    public override int HttpStatus => 401;
}

public sealed class RateLimitedError : DomainException
{
    public RateLimitedError(string message) : base("rate_limited", message) { }
    public override int HttpStatus => 429;
}

public sealed class ContentHashMismatchError : DomainException
{
    public ContentHashMismatchError(string message) : base("content_hash_mismatch", message) { }
}
