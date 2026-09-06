# Bug and security scan — Mailtide

Scan of Core, Desktop, Android hosts, and tests. High-confidence issues below were
reproduced with failing tests and fixed on this branch. Medium/low items were
reviewed in code but not changed.

## Fixed (high confidence)

### 1. High — IMAP MOVE kept the source UID (unique-constraint crash)

- **Location:** `IImapClient.MoveAsync`, `MailKitImapClient`, `MailtideApp.RelocateMessageAsync`
- **Impact:** IMAP UIDs are per-Mailbox. Moving a Message whose `RemoteId` already
  existed in the destination Mailbox (`INBOX` UID 1 → Archive UID 1 is common)
  hit `UNIQUE (AccountId, MailboxId, RemoteId)` and threw `DbUpdateException`.
  Flag/move operations after a successful MOVE also targeted the wrong UID.
- **Fix:** MOVE returns the destination UID (MailKit `MoveToAsync` / COPYUID).
  Core stores that `RemoteId`. If the server omits COPYUID and a collision
  remains, Core assigns a unique local sentinel so SaveChanges cannot crash.

### 2. High — OAuth refresh Credentials were discarded (Microsoft rotation)

- **Location:** `OAuthAccessTokenResult`, `AccountCredentialAuth.GetAccessTokenAsync`,
  Desktop/Android `*OidcOAuthClient.RefreshAsync`
- **Impact:** Hosts dropped `refresh_token` from the token response. Microsoft
  consumer OAuth rotates refresh tokens; reusing the retired Credential yields
  `invalid_grant` and forces re-sign-in. IDLE + command sync also refreshed the
  same secret concurrently, which can trigger reuse detection.
- **Fix:** Persist a rotated refresh Credential. Serialize refresh per
  `CredentialHandle` and re-read the current secret under that gate so concurrent
  IDLE/sync/send cannot replay a retired token.

### 3. Medium — Outbox attachment blobs leaked after send/discard

- **Location:** `MailtideApp.SendNowAsync`, `DiscardOutboxItemAsync`, `RemoveAccountAsync`
- **Impact:** After SMTP accepted a Message (or the Person discarded an Outbox
  item), attachment bytes stayed under `accounts/{id}/blobs/` and
  `OutboxAttachments` rows were left behind. Sent/discarded payloads remained on
  disk until Account removal.
- **Fix:** Delete Outbox attachment rows and blob files on successful submit and
  on discard. RemoveAccount also deletes `OutboxAttachments` rows.

## Not fixed (medium / low confidence)

These were inspected; no code change because they are design tradeoffs, depend
on host WebView behavior, or were not reproduced against a live IdP.

| Severity | Topic | Notes |
| --- | --- | --- |
| Medium | HTML `data:` / `blob:` navigations | `HtmlRemoteContentPolicy.IsAllowed` permits `data:` and `blob:` for NativeWebView navigation. CSP wrapping applies to `NavigateToString` content; a click-through `data:text/html,...` load is a new document without that CSP. Tests currently require `data:text/html` to be allowed (likely for `NavigateToString`). Tightening this needs a WebView-specific allowlist so HTML viewing does not break. |
| Medium | CID inliner `ContentType` | `HtmlCidInliner` concatenates `part.ContentType` into a `data:` URL. MimeKit normally emits `type/subtype` only; a quote/newline in a stored ContentType could break out of an `src` attribute. CSP `default-src 'none'` should still block script. |
| Medium | Flag ops vs sync race | `ApplyMessageImapFlagAsync` writes local flags first and does not take `AccountWorkGate`. A concurrent `SyncNow` can overwrite flags from IMAP summaries. Optimistic UI; not a crash. |
| Medium | `RemoveAccount` during send | `RemoveAccountAsync` is intentionally not serialized on `AccountWorkGate` so a sync in IMAP can be abandoned (`ParallelSyncTests`). SendNow can therefore read blobs while the Account partition is deleted. |
| Low | OIDC `ValidateEndpoints = false` | Required because Google/Microsoft token hosts sit outside the discovery base URL. Production still requires HTTPS. |
| Low | Android `MainActivity` exported for `mailtide://oauth/callback` | Another app can deliver a callback Intent; Duende OidcClient + PKCE should reject a mismatched `state` / code. |
| Low | Loopback OAuth first HTTP request wins | Inherent to loopback redirects; PKCE mitigates code theft. |
| Low | No installer/APK signature check in the updater | Desktop opens the GitHub Release URL in the system browser rather than downloading a payload in-process. |
| Low | `MessageFts` rows on RemoveAccount | FTS rows for deleted Messages are not explicitly cleared; search is scoped to remaining Message rows so results are not leaked. |
| Low | UIDVALIDITY 0 | When STATUS UIDVALIDITY is unavailable, Core stores 0 and does not invalidate. Matches ADR-0005’s “leave at 0” behavior. |

## Checked with no high-confidence issue

- Credential storage: Windows DPAPI, Linux libsecret (no plaintext fallback), Android Keystore AES-GCM
- IMAP/SMTP TLS: non-loopback hosts use `SslOnConnect` (993/465) or required STARTTLS; no opportunistic cleartext (ADR-0005)
- FTS/SQL: parameterized commands; FTS MATCH tokens are quoted
- Account Credential handles hashed for DPAPI/Keystore filenames
- OAuth public clients: no client secret in the app; refresh Credential in OS-backed storage
- Attachment open path: filename sanitized, Linux `xdg-open` uses `ArgumentList`
- Custom-scheme OAuth on Android uses PKCE via Duende OidcClient

## Verification

- `dotnet test tests/Mailtide.Core.Tests` — 173 passed
- `dotnet test tests/Mailtide.Desktop.Tests` (OIDC, HTML policy, update, guards) — 43 passed
- Desktop libsecret / BrowseShell tests were not relied on here (Secret Service / UI host)
