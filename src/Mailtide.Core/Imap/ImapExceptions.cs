namespace Mailtide.Core.Imap;

/// <summary>
/// Authentication rejected by the mail server. Core maps this to a human-readable Account error.
/// </summary>
public sealed class ImapAuthenticationException : Exception
{
    public ImapAuthenticationException()
        : base("IMAP authentication failed.")
    {
    }

    public ImapAuthenticationException(string message)
        : base(message)
    {
    }

    public ImapAuthenticationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Protocol-level failure while talking to the mail server. Core maps this without leaking wire details.
/// </summary>
public sealed class ImapProtocolException : Exception
{
    public ImapProtocolException()
        : base("IMAP protocol failure.")
    {
    }

    public ImapProtocolException(string message)
        : base(message)
    {
    }

    public ImapProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
