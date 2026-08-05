# Candidate dependency AOT surface (IMAP, OAuth, local store, secrets)

Part of: [Mailtide v1 — decision map](../map.md)

Type: research  
Status: resolved

## Question

For libraries commonly used from .NET for **IMAP/SMTP**, **OAuth2**, **local persistence**, and **OS secret storage**, what do primary sources say about Native AOT / trimming compatibility (annotations, known failures, recommended alternatives)? Output an inventory tagged **usable / caution / avoid** for AOT — do not pick Mailtide’s concrete packages in this ticket.

## Answer

AOT inventory (not final picks): MailKitLite/MimeKitLite and Duende OidcClient look **usable**; full MailKit/MimeKit, vanilla Dapper, LiteDB, and System.Net.Mail.SmtpClient lean **avoid**; MSAL, Google.Apis.Auth, Microsoft.Data.Sqlite/SQLitePCLRaw, and most secret-store NuGet wrappers are **caution**. Thin P/Invoke keyring wrappers are the more AOT-friendly secrets class.

Full cited inventory: [research/03-dependency-aot-surface.md](../research/03-dependency-aot-surface.md)
