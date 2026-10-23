namespace TenderSync.Domain.Common;

/// <summary>Raised when a domain rule is violated. Mapped to HTTP 400/409 at the API edge.</summary>
public class DomainException : Exception
{
    public DomainException()
    {
    }

    public DomainException(string message)
        : base(message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
