using Avalonia.Input;
using Mailtide.UI;

namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class MailShellShortcutsTests
{
    [TestMethod]
    public void FromKey_maps_new_Outlook_chords_when_not_typing()
    {
        Assert.AreEqual(MailShellShortcut.NewDraft, MailShellShortcuts.FromKey(Key.N, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.FocusReading, MailShellShortcuts.FromKey(Key.O, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.O, KeyModifiers.Control, true, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.O, KeyModifiers.Control, false, true));
        Assert.AreEqual(MailShellShortcut.Sync, MailShellShortcuts.FromKey(Key.F9, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.Sync, MailShellShortcuts.FromKey(Key.F5, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.Sync, MailShellShortcuts.FromKey(Key.F5, KeyModifiers.None, true, false));
        Assert.AreEqual(MailShellShortcut.FocusNextPane, MailShellShortcuts.FromKey(Key.F6, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.FocusPreviousPane, MailShellShortcuts.FromKey(Key.F6, KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.FocusNextPane, MailShellShortcuts.FromKey(Key.F6, KeyModifiers.None, true, false));
        Assert.AreEqual(MailShellShortcut.FocusNextPane, MailShellShortcuts.FromKey(Key.F6, KeyModifiers.None, true, true));
        Assert.AreEqual(MailShellShortcut.FocusSearch, MailShellShortcuts.FromKey(Key.F, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.FocusSearch, MailShellShortcuts.FromKey(Key.E, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.E, KeyModifiers.Control, true, false));
        Assert.AreEqual(MailShellShortcut.Reply, MailShellShortcuts.FromKey(Key.R, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.ReplyAll, MailShellShortcuts.FromKey(Key.R, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.Forward, MailShellShortcuts.FromKey(Key.F, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.ForwardAsAttachment, MailShellShortcuts.FromKey(Key.F, KeyModifiers.Control | KeyModifiers.Alt, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.F, KeyModifiers.Control | KeyModifiers.Alt, false, true));
        Assert.AreEqual(MailShellShortcut.Delete, MailShellShortcuts.FromKey(Key.Delete, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.PermanentlyDelete, MailShellShortcuts.FromKey(Key.Delete, KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.MarkUnread, MailShellShortcuts.FromKey(Key.U, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.MarkRead, MailShellShortcuts.FromKey(Key.Q, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.Flag, MailShellShortcuts.FromKey(Key.Insert, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Insert, KeyModifiers.None, true, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Insert, KeyModifiers.None, false, true));
        Assert.AreEqual(MailShellShortcut.PageReading, MailShellShortcuts.FromKey(Key.Space, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.PageReadingUp, MailShellShortcuts.FromKey(Key.Space, KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Space, KeyModifiers.None, true, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Space, KeyModifiers.None, false, true));
        Assert.AreEqual(MailShellShortcut.PageViewport, MailShellShortcuts.FromKey(Key.PageDown, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.PageViewportUp, MailShellShortcuts.FromKey(Key.PageUp, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.PageDown, KeyModifiers.None, true, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.PageDown, KeyModifiers.None, false, true));
        Assert.AreEqual(MailShellShortcut.NextMailbox, MailShellShortcuts.FromKey(Key.PageDown, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.PreviousMailbox, MailShellShortcuts.FromKey(Key.PageUp, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.PageDown, KeyModifiers.Control, true, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.PageDown, KeyModifiers.Control, false, true));
        Assert.AreEqual(MailShellShortcut.ToggleQuoted, MailShellShortcuts.FromKey(Key.OemBackslash, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.ToggleQuoted, MailShellShortcuts.FromKey(Key.OemPipe, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.OemBackslash, KeyModifiers.None, true, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.OemBackslash, KeyModifiers.None, false, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Escape, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Escape, KeyModifiers.None, true, false));
        Assert.AreEqual(MailShellShortcut.DiscardDraft, MailShellShortcuts.FromKey(Key.Escape, KeyModifiers.None, true, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Q, KeyModifiers.Control, true, false));
        Assert.AreEqual(MailShellShortcut.Flag, MailShellShortcuts.FromKey(Key.G, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.Move, MailShellShortcuts.FromKey(Key.V, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.GoToInbox, MailShellShortcuts.FromKey(Key.I, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.GoToOutbox, MailShellShortcuts.FromKey(Key.O, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.GoToSent, MailShellShortcuts.FromKey(Key.S, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.GoToDrafts, MailShellShortcuts.FromKey(Key.D, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.GoToTrash, MailShellShortcuts.FromKey(Key.T, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.GoToJunk, MailShellShortcuts.FromKey(Key.J, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.NewMailbox, MailShellShortcuts.FromKey(Key.E, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.E, KeyModifiers.Control | KeyModifiers.Shift, false, true));
        Assert.AreEqual(MailShellShortcut.RenameMailbox, MailShellShortcuts.FromKey(Key.F2, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.F2, KeyModifiers.None, true, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.F2, KeyModifiers.None, false, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.S, KeyModifiers.Control | KeyModifiers.Shift, false, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.D, KeyModifiers.Control | KeyModifiers.Shift, false, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.T, KeyModifiers.Control | KeyModifiers.Shift, false, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.J, KeyModifiers.Control | KeyModifiers.Shift, false, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.S, KeyModifiers.Control | KeyModifiers.Shift, true, true));
        Assert.AreEqual(MailShellShortcut.NextUnread, MailShellShortcuts.FromKey(Key.OemPeriod, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.NextFlagged, MailShellShortcuts.FromKey(Key.OemPeriod, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.PreviousFlagged, MailShellShortcuts.FromKey(Key.OemComma, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.OemPeriod, KeyModifiers.Control | KeyModifiers.Shift, false, true));
        Assert.AreEqual(MailShellShortcut.PreviousUnread, MailShellShortcuts.FromKey(Key.OemComma, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.NextThread, MailShellShortcuts.FromKey(Key.Down, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.PreviousThread, MailShellShortcuts.FromKey(Key.Up, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.FirstThread, MailShellShortcuts.FromKey(Key.Home, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.LastThread, MailShellShortcuts.FromKey(Key.End, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Home, KeyModifiers.Control, false, true));
        Assert.AreEqual(MailShellShortcut.NextInConversation, MailShellShortcuts.FromKey(Key.Down, KeyModifiers.Alt, false, false));
        Assert.AreEqual(MailShellShortcut.PreviousInConversation, MailShellShortcuts.FromKey(Key.Up, KeyModifiers.Alt, false, false));
        Assert.AreEqual(MailShellShortcut.FirstInConversation, MailShellShortcuts.FromKey(Key.Home, KeyModifiers.Alt, false, false));
        Assert.AreEqual(MailShellShortcut.LastInConversation, MailShellShortcuts.FromKey(Key.End, KeyModifiers.Alt, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Home, KeyModifiers.Alt, false, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Down, KeyModifiers.Alt, false, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Down, KeyModifiers.Alt, true, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Down, KeyModifiers.Control, false, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Down, KeyModifiers.Control, true, false));
        Assert.AreEqual(MailShellShortcut.UndoTrash, MailShellShortcuts.FromKey(Key.Z, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.GoToMailbox, MailShellShortcuts.FromKey(Key.Y, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.SaveMessage, MailShellShortcuts.FromKey(Key.S, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.S, KeyModifiers.Control, true, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Y, KeyModifiers.Control, true, false));
        Assert.AreEqual(MailShellShortcut.FindInMessage, MailShellShortcuts.FromKey(Key.F3, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.FindPreviousInMessage, MailShellShortcuts.FromKey(Key.F3, KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.FindInMessage, MailShellShortcuts.FromKey(Key.F3, KeyModifiers.None, true, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.F3, KeyModifiers.None, true, true));
    }

    [TestMethod]
    public void FromKey_drops_gmail_single_keys()
    {
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.R, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.C, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.N, KeyModifiers.None, false, false));
    }

    [TestMethod]
    public void FromKey_is_inert_while_typing_in_a_text_input()
    {
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.R, KeyModifiers.Control, true, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Delete, KeyModifiers.None, true, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Delete, KeyModifiers.Shift, true, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.N, KeyModifiers.Control, true, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Z, KeyModifiers.Control, true, false));
        Assert.AreEqual(MailShellShortcut.Sync, MailShellShortcuts.FromKey(Key.F9, KeyModifiers.None, true, false));
    }

    [TestMethod]
    public void FromKey_maps_compose_scoped_chords()
    {
        Assert.AreEqual(MailShellShortcut.Send, MailShellShortcuts.FromKey(Key.Enter, KeyModifiers.Control, true, true));
        Assert.AreEqual(MailShellShortcut.DiscardDraft, MailShellShortcuts.FromKey(Key.Escape, KeyModifiers.None, true, true));
        Assert.AreEqual(MailShellShortcut.Attach, MailShellShortcuts.FromKey(Key.A, KeyModifiers.Control | KeyModifiers.Shift, true, true));
        Assert.AreEqual(MailShellShortcut.Attach, MailShellShortcuts.FromKey(Key.A, KeyModifiers.Control | KeyModifiers.Shift, false, true));
        Assert.AreEqual(MailShellShortcut.FocusCc, MailShellShortcuts.FromKey(Key.C, KeyModifiers.Control | KeyModifiers.Shift, true, true));
        Assert.AreEqual(MailShellShortcut.FocusBcc, MailShellShortcuts.FromKey(Key.B, KeyModifiers.Control | KeyModifiers.Shift, true, true));
        Assert.AreEqual(MailShellShortcut.FocusHtml, MailShellShortcuts.FromKey(Key.H, KeyModifiers.Control | KeyModifiers.Shift, true, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.H, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.CopyToMailbox, MailShellShortcuts.FromKey(Key.C, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.FocusCc, MailShellShortcuts.FromKey(Key.C, KeyModifiers.Control | KeyModifiers.Shift, false, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Enter, KeyModifiers.Control, true, false));
        Assert.AreEqual(MailShellShortcut.Archive, MailShellShortcuts.FromKey(Key.A, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.A, KeyModifiers.Control, true, true));
        Assert.AreEqual(MailShellShortcut.Send, MailShellShortcuts.FromKey(Key.S, KeyModifiers.Alt, true, true));
        Assert.AreEqual(MailShellShortcut.SaveDraft, MailShellShortcuts.FromKey(Key.S, KeyModifiers.Control, true, true));
        Assert.AreEqual(MailShellShortcut.SaveDraft, MailShellShortcuts.FromKey(Key.S, KeyModifiers.Control, false, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.S, KeyModifiers.Control, true, false));
    }

    [TestMethod]
    public void ListBoundaryShortcut_maps_Home_End_when_the_list_is_focused()
    {
        Assert.AreEqual(MailShellShortcut.FirstThread, MailShellShortcuts.ListBoundaryShortcut(Key.Home, KeyModifiers.None, true, false, false));
        Assert.AreEqual(MailShellShortcut.LastThread, MailShellShortcuts.ListBoundaryShortcut(Key.End, KeyModifiers.None, true, false, false));
        Assert.AreEqual(MailShellShortcut.NextThread, MailShellShortcuts.ListBoundaryShortcut(Key.Down, KeyModifiers.None, true, false, false));
        Assert.AreEqual(MailShellShortcut.PreviousThread, MailShellShortcuts.ListBoundaryShortcut(Key.Up, KeyModifiers.None, true, false, false));
        Assert.AreEqual(MailShellShortcut.PageThread, MailShellShortcuts.ListBoundaryShortcut(Key.PageDown, KeyModifiers.None, true, false, false));
        Assert.AreEqual(MailShellShortcut.PageThreadUp, MailShellShortcuts.ListBoundaryShortcut(Key.PageUp, KeyModifiers.None, true, false, false));
        Assert.AreEqual(MailShellShortcut.FocusReading, MailShellShortcuts.ListBoundaryShortcut(Key.Enter, KeyModifiers.None, true, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.ListBoundaryShortcut(Key.Enter, KeyModifiers.None, false, false, false));
        Assert.AreEqual(MailShellShortcut.ExpandConversation, MailShellShortcuts.ListBoundaryShortcut(Key.Right, KeyModifiers.None, true, false, false));
        Assert.AreEqual(MailShellShortcut.CollapseConversation, MailShellShortcuts.ListBoundaryShortcut(Key.Left, KeyModifiers.None, true, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.ListBoundaryShortcut(Key.Right, KeyModifiers.None, false, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.ListBoundaryShortcut(Key.PageDown, KeyModifiers.None, false, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.ListBoundaryShortcut(Key.PageDown, KeyModifiers.Control, true, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.ListBoundaryShortcut(Key.Down, KeyModifiers.None, false, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.ListBoundaryShortcut(Key.Home, KeyModifiers.None, false, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.ListBoundaryShortcut(Key.Home, KeyModifiers.None, true, true, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.ListBoundaryShortcut(Key.Home, KeyModifiers.None, true, false, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.ListBoundaryShortcut(Key.Home, KeyModifiers.Control, true, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.ListBoundaryShortcut(Key.A, KeyModifiers.None, true, false, false));
    }

    [TestMethod]
    public void AccountShortcutIndex_maps_Ctrl_digits()
    {
        Assert.AreEqual(-1, MailShellShortcuts.AccountShortcutIndex(Key.D0, KeyModifiers.Control, false, false));
        Assert.AreEqual(0, MailShellShortcuts.AccountShortcutIndex(Key.D1, KeyModifiers.Control, false, false));
        Assert.AreEqual(8, MailShellShortcuts.AccountShortcutIndex(Key.D9, KeyModifiers.Control, false, false));
        Assert.AreEqual(2, MailShellShortcuts.AccountShortcutIndex(Key.NumPad3, KeyModifiers.Control, false, false));
        Assert.IsNull(MailShellShortcuts.AccountShortcutIndex(Key.D1, KeyModifiers.Control, true, false));
        Assert.IsNull(MailShellShortcuts.AccountShortcutIndex(Key.D1, KeyModifiers.Control, false, true));
        Assert.IsNull(MailShellShortcuts.AccountShortcutIndex(Key.D1, KeyModifiers.None, false, false));
        Assert.IsNull(MailShellShortcuts.AccountShortcutIndex(Key.D1, KeyModifiers.Control | KeyModifiers.Shift, false, false));
    }

    [TestMethod]
    public void TypeaheadChar_maps_letters_and_digits()
    {
        Assert.AreEqual('b', MailShellShortcuts.TypeaheadChar(Key.B));
        Assert.AreEqual('3', MailShellShortcuts.TypeaheadChar(Key.D3));
        Assert.AreEqual('7', MailShellShortcuts.TypeaheadChar(Key.NumPad7));
        Assert.IsNull(MailShellShortcuts.TypeaheadChar(Key.Enter));
    }
}
