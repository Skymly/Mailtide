namespace Mailtide.Core.Smtp;

/// <summary>
/// Authentication rejected by the mail server. Core maps this to a human-readable error.
/// </summary>
public sealed class SmtpAuthenticationException : Exception
{
    public SmtpAuthenticationException()
        : base("SMTP authentication failed.")
    {
    }

    public SmtpAuthenticationException(string message)
        : base(message)
    {
    }

    public SmtpAuthenticationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Protocol-level failure while talking to the mail server. Core maps this without leaking wire details.
/// </summary>
public sealed class SmtpProtocolException : Exception
{
    public SmtpProtocolException()
        : base("SMTP protocol failure.")
    {
    }

    public SmtpProtocolException(string message)
        : base(message)
    {
    }

    public SmtpProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
