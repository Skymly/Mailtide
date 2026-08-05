# Credential model for Accounts

Part of: [Mailtide v1 — decision map](../map.md)

Type: grilling  
Status: resolved

## Question

What does an Account store for auth — OAuth refresh token, password, or both? Who owns token refresh (sync engine vs separate auth module)? How do credentials relate to the Person’s device? Decide the credential **model**; platform secret APIs stay for a later ticket after research 04.

## Answer

1. **One primary credential per Account** — either OAuth (refresh token + necessary client/tenant metadata; access tokens are short-lived and disposable) **or** a password / app password for manual IMAP. Never both on the same Account.
2. **Auth module owns token lifecycle** (obtain usable access token, refresh, mark invalid). The sync engine only consumes a usable access token before network I/O; it does not talk to the IdP or write refresh tokens. Auth failures surface as Account error / re-login needed (feeds the sync UI error state from ticket 06).
3. **Device-bound secrets**: credentials are bound to this install on this device — no cloud roaming of secrets, no cross-device sharing of refresh tokens. Non-secret Account config may live in the store; secrets are accessed only through Auth via platform secure storage (concrete APIs still deferred).

Concrete per-platform secret APIs remain a later ticket.
