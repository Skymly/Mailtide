# IMAP TLS, UIDVALIDITY, and session reuse

IMAP (and SMTP) connections to non-loopback hosts always use TLS: implicit SSL on 993/465, otherwise STARTTLS **required**. Opportunistic STARTTLS and cleartext are not used on the network. Loopback remains plaintext so protocol contract tests can run without certificates.

Each Mailbox stores IMAP **UIDVALIDITY**. When it changes, Core discards that Mailbox’s local Message/RemoteId mapping and refetches bodies — UIDs are not reused across validity epochs.

Core reuses one command IMAP session and one IDLE session per endpoint (`ImapSessionPool`). A failed session is dropped so the next use reconnects. Hosts still pass `IImapClientFactory`; the pool is an internal Core module, not a new public port.
