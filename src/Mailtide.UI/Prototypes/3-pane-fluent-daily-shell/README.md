# PROTOTYPE — 3-pane Fluent daily shell

Throwaway. Not product UI. Do not merge to `main`.

**Question:** Does a compact Fluent 3-pane daily shell feel like a product rather than the current 4-column tool chrome?

Three chrome placements, switchable via `?variant=`:

| Key | Name | What changes |
| --- | --- | --- |
| `A` | Window CommandBar | Compose + Sync span the whole window (canonical lock from #209) |
| `B` | Content CommandBar | Left nav is independent; commands sit above list + reading |
| `C` | List-header CommandBar | Commands sit in the Thread-list header next to search |

Zero-Accounts empty state: `?empty=1` (or the switcher toggle). Compose replaces the reading pane.

## Run

Open `index.html` in a browser, or from the repo root:

```bash
python -m http.server 8765 --directory src/Mailtide.UI/Prototypes/3-pane-fluent-daily-shell
```

Then http://localhost:8765/?variant=A
