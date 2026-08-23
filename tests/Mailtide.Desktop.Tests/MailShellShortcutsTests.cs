using Avalonia.Input;
using Mailtide.UI;

namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class MailShellShortcutsTests
{
    [TestMethod]
    public void FromKey_maps_everyday_actions_when_not_typing()
    {
        Assert.AreEqual(MailShellShortcut.Reply, MailShellShortcuts.FromKey(Key.R, false));
        Assert.AreEqual(MailShellShortcut.ReplyAll, MailShellShortcuts.FromKey(Key.A, false));
        Assert.AreEqual(MailShellShortcut.Forward, MailShellShortcuts.FromKey(Key.F, false));
        Assert.AreEqual(MailShellShortcut.MarkUnread, MailShellShortcuts.FromKey(Key.U, false));
        Assert.AreEqual(MailShellShortcut.Flag, MailShellShortcuts.FromKey(Key.S, false));
        Assert.AreEqual(MailShellShortcut.Delete, MailShellShortcuts.FromKey(Key.Delete, false));
        Assert.AreEqual(MailShellShortcut.FocusSearch, MailShellShortcuts.FromKey(Key.Oem2, false));
        Assert.AreEqual(MailShellShortcut.NewDraft, MailShellShortcuts.FromKey(Key.C, false));
    }

    [TestMethod]
    public void FromKey_is_inert_while_typing_in_a_text_input()
    {
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.R, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Delete, true));
        Assert.AreEqual(MailShellShortcut.None, MailShellShortcuts.FromKey(Key.Oem2, true));
    }
}
