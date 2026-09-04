# PROTOTYPE — Empty and unavailable states in the 3-pane shell

Throwaway. Not product UI. Do not merge to `main`.

**Question:** How should **empty and unavailable** states look and behave in the locked compact Fluent 3-pane daily shell?

Locked chrome from [#212](https://github.com/Skymly/Mailtide/issues/212) / [#213](https://github.com/Skymly/Mailtide/issues/213) / [#214](https://github.com/Skymly/Mailtide/issues/214): Window CommandBar (Compose + Sync), 3-pane TreeView | ListBox | reading, compact sender-first Thread rows, conversation reading pane. Zero-Accounts is already locked (full-page empty + Add Account) — not in this prototype.

**Lock (#215):** variant **D** — **A** empty list + **C** blank reading when the list is empty.

Once Accounts exist, the **3-pane stays drawn**. Empty Mailbox / Unified Inbox / Outbox: quiet centered copy in the **list pane only**; the reading pane is **blank** (nothing to select). No extra Compose/Sync CTA — chrome already has them. List has Threads but none selected: quiet **Select a Thread**. Body unavailable / offline copy: Thread chrome and other Messages stay; the affected Message keeps headers and a body card with Retry / Get body. Do not collapse list+reading, do not take over the reading pane, do not use a status strip or sticky InfoBar.

Rejected: **B** (collapsing the well makes the shell jump and duplicates chrome CTAs; takeover hides locked Thread chrome). **C** strip/InfoBar (empty is not a warning; Avalonia has no InfoBar). **A**'s second empty in the reading pane when the list is already empty.

Four variants, switchable via `?variant=`. Six scenes via `?scene=`:

| Key | Name | 3-pane when list is empty | Empty copy | Unavailable / offline |
| --- | --- | --- | --- | --- |
| `A` | Pane-local quiet | Stays drawn | Centered in the pane that is empty (list vs reading) | Thread chrome + Message headers stay; card in the body with Retry / Get body |
| `B` | Content-well collapse | Nav stays; list + reading **merge** into one well | Illustration + one CTA (Compose on empty Mailbox, Sync on empty Unified Inbox, none on empty Outbox) | Reading pane takeover — no Thread chrome |
| `C` | Status strip / blank / inline | Stays drawn | Compact strip under the list header; reading pane is **blank** | Sticky InfoBar / in-Message chip; other local Messages in the Thread still render |
| `D` | Lock: A empty + blank reading | Stays drawn | A in the list; reading blank while the list is empty | Same as A |

Scenes: `mailbox` · `unified` · `outbox` · `noselect` · `unavailable` · `offline`.

## Run

Open `index.html` in a browser, or from the repo root:

```bash
python -m http.server 8780 --directory src/Mailtide.UI/Prototypes/empty-unavailable-states
```

Then http://localhost:8780/?variant=D&scene=mailbox
