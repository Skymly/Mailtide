using Avalonia.Input;

namespace Mailtide.UI;

public enum MailShellShortcut
{
    None,
    Reply,
    ReplyAll,
    Forward,
    ForwardAsAttachment,
    MarkUnread,
    MarkRead,
    Flag,
    Delete,
    PermanentlyDelete,
    FocusSearch,
    NewDraft,
    NextUnread,
    PreviousUnread,
    NextFlagged,
    PreviousFlagged,
    NextThread,
    PreviousThread,
    NextInConversation,
    PreviousInConversation,
    FirstThread,
    LastThread,
    FirstInConversation,
    LastInConversation,
    NewMailbox,
    RenameMailbox,
    Sync,
    Move,
    CopyToMailbox,
    Archive,
    Send,
    DiscardDraft,
    UndoTrash,
    Attach,
    FocusCc,
    FocusBcc,
    FocusHtml,
    FindInMessage,
    FindPreviousInMessage,
    PageReading,
    PageReadingUp,
    PageThread,
    PageThreadUp,
    PageViewport,
    PageViewportUp,
    NextMailbox,
    PreviousMailbox,
    FocusNextPane,
    FocusPreviousPane,
    FocusReading,
    FocusList,
    ExpandConversation,
    CollapseConversation,
    ToggleQuoted,
    GoToMailbox,
    GoToInbox,
    GoToOutbox,
    GoToSent,
    GoToDrafts,
    GoToTrash,
    GoToJunk,
    SaveDraft,
    SaveMessage,
}

public static class MailShellShortcuts
{
    public static MailShellShortcut FromKey(
        Key key,
        KeyModifiers modifiers,
        bool textInputFocused,
        bool composeSurface)
    {
        var ctrl = modifiers.HasFlag(KeyModifiers.Control);
        var shift = modifiers.HasFlag(KeyModifiers.Shift);
        var alt = modifiers.HasFlag(KeyModifiers.Alt);
        var onlyCtrl = ctrl && !shift && !alt;
        var ctrlShift = ctrl && shift && !alt;
        var none = modifiers == KeyModifiers.None;

        if (composeSurface && key == Key.Escape && none)
        {
            return MailShellShortcut.DiscardDraft;
        }

        if (composeSurface && key == Key.Enter && onlyCtrl)
        {
            return MailShellShortcut.Send;
        }

        if (composeSurface && key == Key.A && ctrlShift)
        {
            return MailShellShortcut.Attach;
        }

        if (composeSurface && key == Key.C && ctrlShift)
        {
            return MailShellShortcut.FocusCc;
        }

        if (composeSurface && key == Key.B && ctrlShift)
        {
            return MailShellShortcut.FocusBcc;
        }

        if (composeSurface && key == Key.H && ctrlShift)
        {
            return MailShellShortcut.FocusHtml;
        }

        if (composeSurface && key == Key.S && onlyCtrl)
        {
            return MailShellShortcut.SaveDraft;
        }

        if (composeSurface && key == Key.S && alt && !ctrl && !shift)
        {
            return MailShellShortcut.Send;
        }

        if ((key == Key.F9 || key == Key.F5) && none)
        {
            return MailShellShortcut.Sync;
        }

        if (key == Key.F6 && !ctrl && !alt)
        {
            return shift ? MailShellShortcut.FocusPreviousPane : MailShellShortcut.FocusNextPane;
        }

        if (!composeSurface && key == Key.F3 && !ctrl && !alt)
        {
            return shift ? MailShellShortcut.FindPreviousInMessage : MailShellShortcut.FindInMessage;
        }

        if (!composeSurface
            && none
            && !textInputFocused
            && key is Key.OemBackslash or Key.OemPipe)
        {
            return MailShellShortcut.ToggleQuoted;
        }

        if (textInputFocused)
        {
            return MailShellShortcut.None;
        }

        if (key == Key.Delete && none)
        {
            return MailShellShortcut.Delete;
        }

        if (key == Key.Delete && shift && !ctrl && !alt)
        {
            return MailShellShortcut.PermanentlyDelete;
        }

        if (key == Key.F2 && none && !composeSurface)
        {
            return MailShellShortcut.RenameMailbox;
        }

        if (key == Key.Space && !ctrl && !alt && !composeSurface)
        {
            return shift ? MailShellShortcut.PageReadingUp : MailShellShortcut.PageReading;
        }

        if (none && !composeSurface)
        {
            if (key == Key.PageDown)
            {
                return MailShellShortcut.PageViewport;
            }

            if (key == Key.PageUp)
            {
                return MailShellShortcut.PageViewportUp;
            }
        }

        if (key == Key.Insert && none && !composeSurface)
        {
            return MailShellShortcut.Flag;
        }

        if (onlyCtrl)
        {
            return key switch
            {
                Key.N => MailShellShortcut.NewDraft,
                Key.O => composeSurface ? MailShellShortcut.None : MailShellShortcut.FocusReading,
                Key.F => MailShellShortcut.FocusSearch,
                Key.E => MailShellShortcut.FocusSearch,
                Key.R => MailShellShortcut.Reply,
                Key.U => MailShellShortcut.MarkUnread,
                Key.Q => MailShellShortcut.MarkRead,
                Key.OemPeriod => MailShellShortcut.NextUnread,
                Key.OemComma => MailShellShortcut.PreviousUnread,
                Key.Down => composeSurface ? MailShellShortcut.None : MailShellShortcut.NextThread,
                Key.Up => composeSurface ? MailShellShortcut.None : MailShellShortcut.PreviousThread,
                Key.Home => composeSurface ? MailShellShortcut.None : MailShellShortcut.FirstThread,
                Key.End => composeSurface ? MailShellShortcut.None : MailShellShortcut.LastThread,
                Key.Z => MailShellShortcut.UndoTrash,
                Key.Y => MailShellShortcut.GoToMailbox,
                Key.S => MailShellShortcut.SaveMessage,
                Key.PageDown => composeSurface ? MailShellShortcut.None : MailShellShortcut.NextMailbox,
                Key.PageUp => composeSurface ? MailShellShortcut.None : MailShellShortcut.PreviousMailbox,
                _ => MailShellShortcut.None,
            };
        }

        if (alt && !ctrl && !shift && !composeSurface)
        {
            return key switch
            {
                Key.Down => MailShellShortcut.NextInConversation,
                Key.Up => MailShellShortcut.PreviousInConversation,
                Key.Home => MailShellShortcut.FirstInConversation,
                Key.End => MailShellShortcut.LastInConversation,
                _ => MailShellShortcut.None,
            };
        }

        if (ctrl && alt && !shift)
        {
            return key == Key.F && !composeSurface
                ? MailShellShortcut.ForwardAsAttachment
                : MailShellShortcut.None;
        }

        if (ctrlShift)
        {
            return key switch
            {
                Key.R => MailShellShortcut.ReplyAll,
                Key.F => MailShellShortcut.Forward,
                Key.G => MailShellShortcut.Flag,
                Key.V => MailShellShortcut.Move,
                Key.C => composeSurface ? MailShellShortcut.None : MailShellShortcut.CopyToMailbox,
                Key.A => composeSurface ? MailShellShortcut.None : MailShellShortcut.Archive,
                Key.I => MailShellShortcut.GoToInbox,
                Key.O => MailShellShortcut.GoToOutbox,
                Key.S => composeSurface ? MailShellShortcut.None : MailShellShortcut.GoToSent,
                Key.D => composeSurface ? MailShellShortcut.None : MailShellShortcut.GoToDrafts,
                Key.T => composeSurface ? MailShellShortcut.None : MailShellShortcut.GoToTrash,
                Key.J => composeSurface ? MailShellShortcut.None : MailShellShortcut.GoToJunk,
                Key.E => composeSurface ? MailShellShortcut.None : MailShellShortcut.NewMailbox,
                Key.OemPeriod => composeSurface ? MailShellShortcut.None : MailShellShortcut.NextFlagged,
                Key.OemComma => composeSurface ? MailShellShortcut.None : MailShellShortcut.PreviousFlagged,
                _ => MailShellShortcut.None,
            };
        }

        return MailShellShortcut.None;
    }

    public static MailShellShortcut ListBoundaryShortcut(
        Key key,
        KeyModifiers modifiers,
        bool listFocused,
        bool textInputFocused,
        bool composeSurface)
    {
        if (!listFocused || textInputFocused || composeSurface || modifiers != KeyModifiers.None)
        {
            return MailShellShortcut.None;
        }

        return key switch
        {
            Key.Home => MailShellShortcut.FirstThread,
            Key.End => MailShellShortcut.LastThread,
            Key.Down => MailShellShortcut.NextThread,
            Key.Up => MailShellShortcut.PreviousThread,
            Key.PageDown => MailShellShortcut.PageThread,
            Key.PageUp => MailShellShortcut.PageThreadUp,
            Key.Enter => MailShellShortcut.FocusReading,
            Key.Right => MailShellShortcut.ExpandConversation,
            Key.Left => MailShellShortcut.CollapseConversation,
            _ => MailShellShortcut.None,
        };
    }

    public static int? AccountShortcutIndex(
        Key key,
        KeyModifiers modifiers,
        bool textInputFocused,
        bool composeSurface)
    {
        if (textInputFocused || composeSurface || modifiers != KeyModifiers.Control)
        {
            return null;
        }

        if (key is Key.D0 or Key.NumPad0)
        {
            return -1;
        }

        if (key is >= Key.D1 and <= Key.D9)
        {
            return key - Key.D1;
        }

        if (key is >= Key.NumPad1 and <= Key.NumPad9)
        {
            return key - Key.NumPad1;
        }

        return null;
    }

    public static char? TypeaheadChar(Key key)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            return (char)('a' + (key - Key.A));
        }

        if (key >= Key.D0 && key <= Key.D9)
        {
            return (char)('0' + (key - Key.D0));
        }

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            return (char)('0' + (key - Key.NumPad0));
        }

        return null;
    }
}
