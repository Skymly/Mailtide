using Avalonia.Input;

namespace Mailtide.UI;

public enum MailShellShortcut
{
    None,
    Reply,
    ReplyAll,
    Forward,
    MarkUnread,
    Flag,
    Delete,
    FocusSearch,
    NewDraft,
    NextUnread,
    PreviousUnread,
    Sync,
    Move,
    Send,
    DiscardDraft,
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

        if (composeSurface)
        {
            if (key == Key.Escape && modifiers == KeyModifiers.None)
            {
                return MailShellShortcut.DiscardDraft;
            }

            if (key == Key.Enter && onlyCtrl)
            {
                return MailShellShortcut.Send;
            }

            return MailShellShortcut.None;
        }

        if (textInputFocused)
        {
            return MailShellShortcut.None;
        }

        if (key == Key.F9 && modifiers == KeyModifiers.None)
        {
            return MailShellShortcut.Sync;
        }

        if (key == Key.Delete && modifiers == KeyModifiers.None)
        {
            return MailShellShortcut.Delete;
        }

        if (onlyCtrl)
        {
            return key switch
            {
                Key.N => MailShellShortcut.NewDraft,
                Key.F => MailShellShortcut.FocusSearch,
                Key.R => MailShellShortcut.Reply,
                Key.U => MailShellShortcut.MarkUnread,
                Key.OemPeriod => MailShellShortcut.NextUnread,
                Key.OemComma => MailShellShortcut.PreviousUnread,
                _ => MailShellShortcut.None,
            };
        }

        if (ctrlShift)
        {
            return key switch
            {
                Key.R => MailShellShortcut.ReplyAll,
                Key.F => MailShellShortcut.Forward,
                Key.G => MailShellShortcut.Flag,
                Key.V => MailShellShortcut.Move,
                _ => MailShellShortcut.None,
            };
        }

        return MailShellShortcut.None;
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
