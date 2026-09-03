# PROTOTYPE — Thread row and conversation reading pane

Throwaway. Not product UI. Do not merge to `main`.

**Question:** How should a compact Thread row and the conversation reading pane look and behave?

Locked chrome from [#212](https://github.com/Skymly/Mailtide/issues/212): Window CommandBar (Compose + Sync), 3-pane TreeView | ListBox | reading, search in the list header, no persistent reading-header buttons. This prototype only varies **Thread-row anatomy** and **reading-pane presentation**.

**Lock (#213):** variant **D** — **A** Thread rows + **C** reading pane.

Four variants, switchable via `?variant=`:

| Key | Name | Row | Reading | Commands |
| --- | --- | --- | --- | --- |
| `A` | Sender-first triple / chips | Unread pip + sender + Account badge + date / subject / preview + flag + clip | Sticky Thread subject; Messages stacked oldest→newest; attachment **chips** in each Message | One pane `...` (Thread) + list context + Message context |
| `B` | Two-line collapse / thumbs | Account initial + sender + flag/clip + date / subject — preview | No pane header; older Messages collapsed; latest expanded; attachment **thumbnail cards** | Per-Message `...` + list context |
| `C` | Subject-first / attach strip | Subject + date / sender · preview; trailing unread/flag/clip/Account | Sticky Thread chrome + **Thread-level attachment strip**; avatar timeline | One header `...` + list context + Message context; strip click opens attachment |
| `D` | Lock: A row + C pane | Same as A | Same as C | Same as C |

Rejected: B (truncation, collapsed conversation, per-Message `...`, Account initials); C rows (subject-first); A per-Message chips.

Unified Inbox vs a single Mailbox: `?unified=0` (or the switcher toggle). Account badge/initial/caption only when the scope is Unified Inbox.

## Run

Open `index.html` in a browser, or from the repo root:

```bash
python -m http.server 8778 --directory src/Mailtide.UI/Prototypes/thread-row-conversation-pane
```

Then http://localhost:8778/?variant=D
