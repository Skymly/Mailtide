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
}

public static class MailShellShortcuts
{
    public static MailShellShortcut FromKey(Key key, bool textInputFocused)
    {
        if (textInputFocused)
        {
            return MailShellShortcut.None;
        }

        return key switch
        {
            Key.R => MailShellShortcut.Reply,
            Key.A => MailShellShortcut.ReplyAll,
            Key.F => MailShellShortcut.Forward,
            Key.U => MailShellShortcut.MarkUnread,
            Key.S => MailShellShortcut.Flag,
            Key.Delete => MailShellShortcut.Delete,
            Key.Oem2 or Key.OemQuestion or Key.Divide => MailShellShortcut.FocusSearch,
            Key.C => MailShellShortcut.NewDraft,
            Key.N => MailShellShortcut.NextUnread,
            Key.P => MailShellShortcut.PreviousUnread,
            _ => MailShellShortcut.None,
        };
    }
}
