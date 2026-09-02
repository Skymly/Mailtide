# Fluent desktop mail chrome patterns

Ticket: [#209](https://github.com/Skymly/Mailtide/issues/209) (child of [#208](https://github.com/Skymly/Mailtide/issues/208)).

**Question:** What chrome patterns from Windows 11 Mail, Outlook for Windows, and Avalonia Fluent can Mailtide copy for a compact 3-pane daily shell without inventing a design system?

**Answer:** Copy Avalonia 12.1 `FluentTheme` Compact tokens plus the Windows mail **list/details** silhouette (left Account/Mailbox tree, compact Thread list, reading pane on the right). Do **not** use WinUI `NavigationView` — it does not exist in Avalonia 12 Fluent. Host the three panes in a `Grid` + `GridSplitter`. Put Compose and Sync on a two-button `CommandBar` with labels to the right of icons. Everything else is `ContextMenu`, compose surface, or Ctrl+. Windows 11 Mail itself is retired; new Outlook for Windows is the current first-party desktop mail chrome, and it still uses that same three-pane mail layout.

This note locks surfaces for the 3-pane prototype. Field-level Thread-row / reading-header / attachment chrome stays on the map fog.

## Prototype lock

| Region | Copy | Avalonia 12.1 Fluent surface | Do not use |
| --- | --- | --- | --- |
| Density | Compact only | `<FluentTheme DensityStyle="Compact" />` | Dual density, custom palettes |
| Left nav | Always-expanded tree at desktop width: Unified Inbox, then each Account with nested Mailboxes, per-Account Outbox (not a Mailbox), Add Account pinned at the bottom | `TreeView` / `TreeViewItem` + `DockPanel` footer `Button`; `ContextMenu` on nodes | WinUI `NavigationView` (missing); Avalonia `NavigationPage` (stack, not a shell); hamburger / `LeftCompact` collapse |
| Thread list | Compact selectable rows; search lives in this pane header; selection drives the reading pane | `ListBox` + `ItemTemplate`; constrain height so virtualization stays on | `DataGrid` / column-list view as the daily default |
| Reading pane | Right of the list at desktop width; stacks the selected Thread's Messages; attachments stay here; compose **replaces this pane** | `ContentControl` / `ScrollViewer` in the third `Grid` column | Fourth attachment column; Gmail overlay compose; pop-out compose as the default |
| Two-action chrome | Always-visible Compose + Sync only | `CommandBar` + two `CommandBarButton`s, `DefaultLabelPosition="Right"`, overflow hidden | Ribbon; Office app rail; putting Reply/Delete/Send in chrome |
| Empty (zero Accounts) | Full-page illustration + title + Add Account CTA; do not draw the three panes | `StackPanel` + `PathIcon` + `TextBlock` + accent `Button` | Empty `NavigationView` / empty 3-pane skeleton |
| Outbox count | Numeric badge on the Outbox row | Compose in the `TreeDataTemplate` (`Border` + caption `TextBlock`) | WinUI `InfoBadge` (missing) |

Shell host: a three-column `Grid` with two `GridSplitter`s (Person drags pane borders, as in Outlook). `SplitView` is a **two**-region control (pane + content); do not stretch it into a fake 3-pane.

Suggested starting widths (not a brand system — just enough to paint the prototype): left ~240–280px (`SplitViewOpenPaneThemeLength` in Fluent is 320px and is too wide for a mail folder tree), list ~320–360px, reading `*`.

## 1. What Avalonia 12 Fluent actually ships

Mailtide already references `Avalonia` / `Avalonia.Themes.Fluent` **12.1.1**. Fluent is the official theme inspired by Microsoft Fluent; Compact is a first-party density, not a custom theme.

Sources: [Fluent / Simple themes](https://docs.avaloniaui.net/docs/styling/themes), [src/Mailtide.UI/Mailtide.UI.csproj](../../src/Mailtide.UI/Mailtide.UI.csproj), [FluentTheme.xaml (tag 12.1.1)](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Avalonia.Themes.Fluent/FluentTheme.xaml), [Compact.xaml (tag 12.1.1)](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Avalonia.Themes.Fluent/DensityStyles/Compact.xaml).

### Exists and maps onto the shell

Confirmed as exported types in `Avalonia.Controls` 12.1.1 **and** merged by [FluentControls.xaml (tag 12.1.1)](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Avalonia.Themes.Fluent/Controls/FluentControls.xaml):

| Need | Control | Why it is the Fluent stand-in |
| --- | --- | --- |
| Left Account/Mailbox tree | `TreeView`, `TreeViewItem` | Hierarchical expand/collapse, 16px indent, chevrons. Compact sets `TreeViewItemMinHeight` **24** (Fluent default is **32**). |
| Compact Thread rows | `ListBox`, `ListBoxItem` | Avalonia's equivalent of WinUI `ListView`. Compact sets `ListBoxItemPadding` to **4,2** (Fluent default **12,9,12,12**). |
| 3-pane resize | `Grid`, `GridSplitter` | Redistributes column width; Outlook's documented "drag the pane border" behavior. |
| Two-action command area | `CommandBar`, `CommandBarButton`, `CommandBarSeparator` | Primary commands inline; secondary in overflow. Avalonia docs: add commands in importance order. |
| Overflow / context actions | `ContextMenu`, `MenuItem`, `Flyout` | Map: everything except Compose + Sync. |
| Icons | `PathIcon` | Themed; used by CommandBar and TreeView chevrons. |
| Reading / compose host | `ContentControl`, `ScrollViewer`, `TransitioningContentControl` | Swap reading vs compose in the third column. |
| Footer CTA / empty CTA | `Button` | Add Account. |

`SplitView` **does** exist (open pane 320px, compact pane 48px, `DisplayMode` Inline / CompactInline / Overlay). Avalonia's own WinUI migration table and SplitView how-to use it as **sidebar + one content region**, often with a `ListBox` of icon+label rows. That is the NavigationView *substitute*, not a mail 3-pane. Use it only if a later ticket wants a collapsible left pane — the map does not.

`NavigationPage` / `ContentPage` also exist. They are a **stack** with a back button and optional `TopCommandBar` (mobile/wizard). Do not host the daily shell in them.

### Does not exist in Avalonia 12 Fluent

Avalonia's WinUI migration table is explicit:

| WinUI | Avalonia 12.1 |
| --- | --- |
| `NavigationView` | **No direct equivalent** — `SplitView` + `ListBox`, or third-party |
| `InfoBadge` / `InfoBar` / `TeachingTip` | Missing (`Border` / `Popup`) |
| `ListView` | `ListBox` |
| `CommandBar` (WinUI) | Avalonia now **does** ship `CommandBar` (the migration table still says "Menu or ToolBar"; prefer the live control docs and 12.1.1 theme) |

Do not pull FluentAvalonia / PleasantUI `NavigationView` — that would be a second design system.

Compact resource leftovers such as `NavigationViewItemOnLeftMinHeight` in Compact.xaml are unused WinUI names; there is still no `NavigationView` type.

Sources: [WinUI migration — controls](https://docs.avaloniaui.net/docs/migration/winui/), [CommandBar](https://docs.avaloniaui.net/controls/navigation/commandbar), [SplitView](https://docs.avaloniaui.net/controls/layout/containers/splitview), [How to: navigate between views](https://docs.avaloniaui.net/docs/how-to/navigation-how-to), [How to: TreeView](https://docs.avaloniaui.net/docs/how-to/treeview-how-to), [How to: ListBox](https://docs.avaloniaui.net/docs/how-to/listbox-how-to), [How to: menus / ContextMenu](https://docs.avaloniaui.net/docs/how-to/menu-how-to), [GridSplitter API](https://docs.avaloniaui.net/api/avalonia/controls/gridsplitter), [TreeViewItem.xaml](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Avalonia.Themes.Fluent/Controls/TreeViewItem.xaml), [ListBoxItem.xaml](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Avalonia.Themes.Fluent/Controls/ListBoxItem.xaml), [SplitView.xaml](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Avalonia.Themes.Fluent/Controls/SplitView.xaml), [CommandBar.xaml](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Avalonia.Themes.Fluent/Controls/CommandBar.xaml), [FluentControlResources.xaml](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Avalonia.Themes.Fluent/Accents/FluentControlResources.xaml).

### CommandBar settings for two actions

From Avalonia CommandBar docs (mirrors WinUI):

- Put Compose then Sync in `PrimaryCommands` (importance order).
- `DefaultLabelPosition="Right"` — WinUI: on larger windows, labels to the **right** of icons stay visible without opening the bar. Bottom labels are the touch default and waste vertical space in compact mail.
- `OverflowButtonVisibility="Collapsed"` until a later ticket actually has secondary chrome. Map overflow is `ContextMenu`, not `...`.
- `IsDynamicOverflowEnabled` can stay false with only two buttons.
- Single-word labels. `PathIcon` for each button.

WinUI also: command bars on large screens sit **near the top** of the window; bottom bars are for handheld reach. Desktop Mailtide: top of the shell, not a Gmail-style floating overlay.

Sources: [Avalonia CommandBar](https://docs.avaloniaui.net/controls/navigation/commandbar), [WinUI Command bar](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/command-bar).

### Empty state

There is no Fluent `EmptyState` control. WinUI content-basics: Title / Subtitle / Body with 12epx spacing; caption on command buttons. Map already: zero Accounts is a **full-page** empty + Add Account, not an empty three-pane.

## 2. Windows 11 Mail / Outlook for Windows at compact desktop width

### Mail is retired; copy the silhouette, not the app

Microsoft ended support for Windows Mail, Calendar, and People on **31 December 2024**. Windows 11 now ships **new Outlook for Windows** as the default mailbox app. Mail is still a valid *pattern* source because Microsoft's own comparison table lists Mail as having the same **conversation settings** (message list vs grouped conversation, reading pane) as new Outlook.

Sources: [Windows Mail, Calendar and People are becoming new Outlook](https://support.microsoft.com/en-us/outlook/windows-mail-calendar-and-people-are-becoming-new-outlook), [Outlook for Windows: the future of Mail on Windows 11](https://support.microsoft.com/en-us/outlook/outlook-for-windows-the-future-of-mail-calendar-and-people-on-windows-11), [Getting started with new Outlook](https://support.microsoft.com/en-us/office/getting-started-with-the-new-outlook-for-windows-66b195df-08b9-48bd-b464-d6edf95813e4).

### Three panes, reading pane on the right

WinUI **list/details** is the documented email pattern: list pane + details pane; at **641 epx or wider** use **side-by-side** (stacked only at 320–640, which the map excludes). The list keeps a selection visual; selecting a row updates details.

New Outlook names the same regions:

- **Folder pane** (Microsoft UI copy; Mailtide domain word is Mailbox, nested under Account) on the left. Show/hide; Person resizes by dragging the border between panes. New Outlook does **not** offer classic Outlook's minimized icon-strip folder pane.
- **Message list** in the middle. Layout: Settings → Mail → Layout. **Message list format:** Sender name first or Subject first. **Text size and spacing:** Small / Medium / Large (Small is the compact end). Classic Outlook's default Inbox view is **Compact**, messages grouped by conversation.
- **Reading pane** default **Right**. Also Bottom / Fill screen / Popout only — those are options, not the desktop daily default. Reading pane can show **all messages in a conversation** or only the selected message.

Add Account sits **at the bottom of the folder list** (Microsoft: "Select **Add account** at the bottom of your list of folders").

Sync in new Outlook replaced the Send/Receive ribbon tab: **View → Sync** (F9). Status appears at the bottom of the message list while syncing.

Sources: [List/details pattern](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/list-details), [Change how the message list is displayed](https://support.microsoft.com/en-us/outlook/mail/change-how-the-message-list-is-displayed-in-outlook), [Use and configure the Reading Pane](https://support.microsoft.com/en-us/outlook/use-and-configure-the-reading-pane-to-preview-messages-in-outlook), [Change the appearance of the Folder Pane](https://support.microsoft.com/en-us/outlook/change-the-appearance-of-the-folder-pane) (classic minimize — **do not copy** for new Outlook / Mailtide), [Getting started — Add account / ribbon / nav bar](https://support.microsoft.com/en-us/office/getting-started-with-the-new-outlook-for-windows-66b195df-08b9-48bd-b464-d6edf95813e4), [Sync in new Outlook](https://support.microsoft.com/en-us/outlook/mail/sync-to-manually-check-for-new-mail-and-to-send-messages-in-outlook), [Change font / text size and spacing](https://support.microsoft.com/en-us/office/change-the-font-or-font-size-in-the-message-list-57bd24a6-1f85-45ac-a657-fba877d3fe00), [Outlook.com density Compact](https://support.microsoft.com/en-us/office/change-the-look-of-your-mailbox-in-outlook-com-and-outlook-on-the-web-b41c2ecb-f23c-42b3-b7f8-659646d5e58c).

### Left tree vs NavigationView

WinUI `NavigationView` **Left** is for 5–10 equally important top-level *app* categories (Settings-style). Hierarchical mailbox trees are the documented job of **Tree view** ("folder structure or nested relationships"). InfoBadge-on-NavigationViewItem is Microsoft's mail *unread count* example — copy the **badge-on-row** idea onto `TreeViewItem`, not the control.

At desktop width, NavigationView Auto would collapse to `LeftCompact` (icons only) below 1008px. Mailtide's left nav cannot be icon-only: Mailboxes are named, Outbox is named, Unified Inbox is named. Keep the tree expanded (`PaneDisplayMode` equivalent: always Left / Inline).

Footer items: WinUI puts extra actions in `FooterMenuItems` / `PaneFooter` — that is the pattern for **Add Account**, not a fake Mailbox node.

Sources: [NavigationView](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/navigationview), [Tree view](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/tree-view), [Info badge](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/info-badge), [App silhouettes — left navigation](https://learn.microsoft.com/en-us/windows/apps/design/basics/app-silhouette).

### List rows

WinUI list item templates (current): single-line (44px), double-line (64px), triple-line (84px) with Base + Caption type ramp. Compact density exists specifically "for information-rich UI" and pointer input.

Older first-party **inbox** item template (Win8 XAML, still the mail row anatomy Microsoft published): From, Subject, preview, timestamp — not a spreadsheet of columns.

Fluent content-basics: multi-line lists use Body + Caption; 12epx between content areas. Nested actionable controls inside a row are a special case (WinUI nested-UI); daily Thread rows should stay **one primary click** (open Thread). Secondary actions: context menu.

Field-level Thread-row spec is map fog — prototype can use a compact double/triple-line `ItemTemplate` without inventing tokens.

Sources: [Item templates for list view](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/item-templates-listview), [Image and text list (inbox) template (XAML)](https://learn.microsoft.com/en-us/previous-versions/windows/apps/hh780639(v=win.10)), [Spacing — Compact sizing](https://learn.microsoft.com/en-us/windows/apps/design/style/spacing), [Content layout and spacing](https://learn.microsoft.com/en-us/windows/apps/design/basics/content-basics), [Nested UI in list items](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/nested-ui).

### Reading pane and compose

Map already: compose **replaces the reading pane**; nav + list stay. That is **not** Gmail's overlay compose, and it is **not** new Outlook's default pop-out window (`Ctrl+N` opens a "New Outlook window" in screen-reader docs; Voice Access has an explicit "Open in new window"). Copy Outlook's **inline reading column**, not its compose window.

Attachments: new Outlook can show thumbnails in the message **and** a separate attachment strip; map already puts attachments **in the reading pane** (no fourth column).

Sources: [Create an email in Outlook](https://support.microsoft.com/en-us/office/create-an-email-message-in-outlook-147208af-ca8e-4cdf-b71f-77ba81a54069), [Screen reader — new Outlook window](https://support.microsoft.com/en-us/office/basic-tasks-using-a-screen-reader-with-email-in-outlook-3fe74ea4-b512-490f-bb42-95fdeb722b9e).

## 3. Do not copy

Standing map decisions, with the first-party chrome they reject:

| Pattern | Where it lives | Why not |
| --- | --- | --- |
| Ribbon (classic or new Outlook simplified/classic) | New Outlook getting-started still tells people to use the ribbon | Visible chrome is Compose + Sync only |
| Office module rail / My Day | New Outlook left nav bar: Calendar, Contacts, other M365 apps; My Day top-right | Mail-only shell; no new capabilities |
| Copilot, Loop, Snooze, Pin, Schedule send, Focused Inbox, categories-as-chrome | New Outlook "what should I check out" + feature table | Out of scope / Office-only |
| Gmail overlay compose | Gmail, not Fluent | Compose replaces the reading pane |
| NavigationView Auto / LeftCompact / LeftMinimal / Top | WinUI NavigationView | Desktop 3-pane stays up; names need text |
| Avalonia `NavigationPage` shell | Avalonia navigation controls | Stack + back button, not mail chrome |
| Third-party FluentAvalonia NavigationView | Not Avalonia.Themes.Fluent | Would invent a second system |
| Classic Outlook minimized folder strip | Classic Folder Pane | New Outlook dropped it; our tree is always labeled |
| Reading pane Bottom / Hide as default | Outlook Layout options | Map: three panes, reading on the right |
| Fourth column / attachment well beside reading | Some new Outlook layouts | Attachments in the reading pane |
| Custom `ColorPaletteResources` / brand | FluentTheme palettes API | No custom brand system |
| Dual density | Fluent DensityStyle Normal vs Compact; Outlook Roomy/Cozy/Compact | Compact only |
| WinUI `InfoBadge` / `InfoBar` / `TeachingTip` as dependencies | WinUI | Missing in Avalonia; compose `Border` / banner we already have |

## Sources (primary only)

- Avalonia 12.1.1 theme and controls: GitHub tag `12.1.1` under `src/Avalonia.Themes.Fluent/`, plus [docs.avaloniaui.net](https://docs.avaloniaui.net/).
- WinUI / Windows app design: [learn.microsoft.com/windows/apps](https://learn.microsoft.com/en-us/windows/apps/design/).
- Outlook / Mail product UI: [support.microsoft.com](https://support.microsoft.com/) first-party articles cited above.
