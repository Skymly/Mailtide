# Mailtide

A personal, multi-account, offline-first email client for Windows, Linux, and Android.

## Language

**Account**:
A mail account the person configures (identity + IMAP/SMTP endpoints + credentials/OAuth). One installation manages many Accounts.
_Avoid_: Profile, mailbox (for the account itself), user

**Person**:
The human using the app on a device. Not a server-side user record.
_Avoid_: User, customer, account holder

**Message**:
A single email as the person sees and stores it locally (headers, body, sync state).
_Avoid_: Mail, email, item

**Mailbox**:
An IMAP mailbox (folder) under an Account that holds Messages.
_Avoid_: Folder (unless speaking UI copy), label, directory

**Outbox**:
The per-Account queue of Messages the Person has asked to send that are not yet successfully submitted via SMTP. Held in the local store; consumed only by the sync engine.
_Avoid_: Send queue (as a synonym in domain prose), pending send, draft
