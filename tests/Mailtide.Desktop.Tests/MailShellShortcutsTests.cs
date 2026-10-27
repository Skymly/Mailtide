using Avalonia.Input;
using Mailtide.UI;

namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class MailShellShortcutsTests
{
    [TestMethod]
    public void FromKey_maps_the_locked_chord_table()
    {
        Assert.AreEqual(MailShellShortcut.NewDraft, MailShellShortcuts.FromKey(Key.N, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.Sync, MailShellShortcuts.FromKey(Key.F9, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.FocusSearch, MailShellShortcuts.FromKey(Key.F, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.Reply, MailShellShortcuts.FromKey(Key.R, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.ReplyAll, MailShellShortcuts.FromKey(Key.R, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.Forward, MailShellShortcuts.FromKey(Key.F, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.Delete, MailShellShortcuts.FromKey(Key.Delete, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.MarkUnread, MailShellShortcuts.FromKey(Key.U, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.Flag, MailShellShortcuts.FromKey(Key.G, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.Move, MailShellShortcuts.FromKey(Key.V, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.NextUnread, MailShellShortcuts.FromKey(Key.OemPeriod, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.PreviousUnread, MailShellShortcuts.FromKey(Key.OemComma, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.Send, MailShellShortcuts.FromKey(Key.Enter, KeyModifiers.Control, true, true));
        Assert.AreEqual(MailShellShortcut.Send, MailShellShortcuts.FromKey(Key.Enter, KeyModifiers.Control, false, true));
        Assert.AreEqual(MailShellShortcut.DiscardDraft, MailShellShortcuts.FromKey(Key.Escape, KeyModifiers.None, true, true));
        Assert.AreEqual(MailShellShortcut.DiscardDraft, MailShellShortcuts.FromKey(Key.Escape, KeyModifiers.None, false, true));
    }

    [TestMethod]
    public void FromKey_returns_None_for_removed_navigation_and_paging_gestures()
    {
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.F5, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.F6, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.F6, KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.F3, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.F3, KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.F2, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Insert, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Space, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Space, KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.PageDown, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.PageUp, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.PageDown, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.PageUp, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Down, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Up, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Home, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.End, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Down, KeyModifiers.Alt, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Up, KeyModifiers.Alt, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Home, KeyModifiers.Alt, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.End, KeyModifiers.Alt, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.OemBackslash, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.OemPipe, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Escape, KeyModifiers.None, false, false));
    }

    [TestMethod]
    public void FromKey_returns_None_for_removed_command_chords()
    {
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.O, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Q, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.E, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Z, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Y, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.S, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.S, KeyModifiers.Alt, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.S, KeyModifiers.Alt, true, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Delete, KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.F, KeyModifiers.Control | KeyModifiers.Alt, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.D1, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.D0, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.NumPad3, KeyModifiers.Control, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.OemPeriod, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.OemComma, KeyModifiers.Control | KeyModifiers.Shift, false, false));
    }

    [TestMethod]
    public void FromKey_returns_None_for_removed_ctrl_shift_mailbox_and_object_chords()
    {
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.C, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.A, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.I, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.O, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.S, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.D, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.T, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.J, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.E, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.N, KeyModifiers.Control | KeyModifiers.Shift, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.U, KeyModifiers.Control | KeyModifiers.Shift, false, false));
    }

    [TestMethod]
    public void FromKey_keeps_the_compose_surface_closed_to_send_and_discard()
    {
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.S, KeyModifiers.Control, true, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.S, KeyModifiers.Control, false, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.A, KeyModifiers.Control | KeyModifiers.Shift, true, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.A, KeyModifiers.Control | KeyModifiers.Shift, false, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.C, KeyModifiers.Control | KeyModifiers.Shift, true, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.B, KeyModifiers.Control | KeyModifiers.Shift, true, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.H, KeyModifiers.Control | KeyModifiers.Shift, true, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.S, KeyModifiers.Alt, true, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Delete, KeyModifiers.None, false, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.F9, KeyModifiers.None, false, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.N, KeyModifiers.Control, false, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.R, KeyModifiers.Control, false, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Enter, KeyModifiers.Control, true, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Escape, KeyModifiers.None, true, false));
    }

    [TestMethod]
    public void FromKey_drops_gmail_single_keys_and_plain_navigation()
    {
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.R, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.C, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.N, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Down, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Up, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Home, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.End, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Enter, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Left, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Right, KeyModifiers.None, false, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Tab, KeyModifiers.None, false, false));
    }

    [TestMethod]
    public void FromKey_is_inert_while_typing_in_a_text_input()
    {
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.R, KeyModifiers.Control, true, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Delete, KeyModifiers.None, true, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Delete, KeyModifiers.Shift, true, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.N, KeyModifiers.Control, true, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.F, KeyModifiers.Control, true, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.U, KeyModifiers.Control, true, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.G, KeyModifiers.Control | KeyModifiers.Shift, true, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.F9, KeyModifiers.None, true, false));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.OemPeriod, KeyModifiers.Control, true, false));
    }

    [TestMethod]
    public void TypeaheadChar_maps_letters_and_digits()
    {
        Assert.AreEqual('b', MailShellShortcuts.TypeaheadChar(Key.B));
        Assert.AreEqual('3', MailShellShortcuts.TypeaheadChar(Key.D3));
        Assert.AreEqual('7', MailShellShortcuts.TypeaheadChar(Key.NumPad7));
    }
}
