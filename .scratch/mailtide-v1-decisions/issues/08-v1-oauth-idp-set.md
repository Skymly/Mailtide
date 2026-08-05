# v1 OAuth identity-provider set

Part of: [Mailtide v1 — decision map](../map.md)

Type: grilling  
Status: resolved

## Question

Which OAuth IdPs are first-class in v1? Working recommendation from charting: **Gmail** and **Outlook.com (Microsoft consumer)**; everything else is manual IMAP + password / app password or deferred. Lock the v1 allow-list only — no OAuth implementation design in this ticket.

## Answer

**OAuth first-class IdPs (v1)**
- **Google** (Gmail; personal IMAP OAuth path — not enterprise Google admin scope)
- **Microsoft consumer** (Outlook.com / Hotmail / Live) — **not** Entra work/school

**First-class preset provider (not OAuth)**
- **QQ Mail**: preset IMAP/SMTP endpoints; Credential is QQ’s **授权码** (app-password style), per QQ’s third-party client model — not an OAuth IdP

**Always available**
- Manual IMAP + SMTP + password / app password for any other provider

Not first-class OAuth in v1: Fastmail, Yahoo, Entra work accounts, generic OIDC, etc. (manual IMAP or a later ticket).

No OAuth implementation design in this ticket.
