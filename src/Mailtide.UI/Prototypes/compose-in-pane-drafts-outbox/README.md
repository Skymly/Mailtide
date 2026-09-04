# PROTOTYPE — Compose-in-pane, local Drafts, and Outbox nav

Throwaway. Not product UI. Do not merge to `main`.

**Question:** When compose **replaces the reading pane**, how do **local Draft switching**, Send/discard, and the per-Account **Outbox** nav item work without becoming a button farm again?

Locked chrome from [#212](https://github.com/Skymly/Mailtide/issues/212) / [#213](https://github.com/Skymly/Mailtide/issues/213): Window CommandBar (Compose + Sync), 3-pane TreeView | ListBox | reading, compact sender-first Thread rows, conversation reading pane. This prototype only varies **compose-in-pane**, **local Draft switching**, and **Outbox**.

Standing constraints:

- Nav + Thread list stay put; the right pane becomes compose
- Local **Draft** switcher lives **inside** the compose surface (not a fourth column, not a left-nav Drafts item)
- Do not unify Draft with the Drafts-role Mailbox
- Outbox is the per-Account left-nav item (not a Mailbox); Retry/Discard are not top chrome
- Chrome **Compose** always starts a new local Draft; autosave; no New Draft / Save Draft
- **Send** is on the compose surface; Outbox flushes with **Sync**

Three variants, switchable via `?variant=`:

| Key | Name | Local Draft switcher | Send / Discard | Outbox Retry / Discard |
| --- | --- | --- | --- | --- |
| `A` | Header menu / footer pair | Compose-header **Local Drafts ▾** menu | **Send** + **Discard** in the compose footer | List context + reading `...` (no row buttons) |
| `B` | In-pane rail / hover verbs | Narrow **rail inside the reading pane** (not a fourth shell column) | **Send** in the form footer; **Discard** is the rail-card **X** | Hover **Retry** / **Discard** on the Outbox row; reading pane is a status card |
| `C` | Chip tabs / context only | Browser-tab **chips** across the compose top | **Send** in the header (Ctrl+Enter); Discard is chip **X** / Esc | Context menu only; reading pane is a queue inspector |

**Lock (#214):** variant **A**.

Empty untitled Drafts auto-discard when switching away. Resume an existing Draft: chrome Compose (new) → switcher → pick the old one.

Rejected: **B** (in-pane rail reads as a fourth column; hover Retry/Discard is a button farm and keyboard-invisible); **C** chip tabs (not Fluent desktop mail, scales poorly). Outbox stays context + `...` (A already matches C there).

## Run

Open `index.html` in a browser, or from the repo root:

```bash
python -m http.server 8779 --directory src/Mailtide.UI/Prototypes/compose-in-pane-drafts-outbox
```

Then http://localhost:8779/?variant=A
