namespace RixelPOS.Domain.Common;

public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }

    public DomainException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>
/// A business rule failure with a stable machine-readable code and arguments,
/// so the UI can present a friendly, localized explanation.
/// </summary>
public sealed class BusinessRuleException : DomainException
{
    public BusinessRuleException(string code, string message, params object[] arguments)
        : base(message)
    {
        Code = code;
        Arguments = arguments;
    }

    public string Code { get; }

    public object[] Arguments { get; }
}
