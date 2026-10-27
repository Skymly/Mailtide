using Mailtide.UI;

namespace Mailtide.Desktop.Tests;

[TestClass]
public sealed class ObjectMenuBuilderTests
{
    private static readonly string[] SharedMessageHeaders =
    [
        "Reply",
        "Reply All",
        "Forward",
        "Forward as Attachment",
        "Edit as New",
        "Save as .eml",
        "Show original",
        "Copy subject",
        "Copy address",
        "Search from sender",
        "Search to recipient",
        "Copy headers",
        "Copy Message-ID",
    ];

    private static IReadOnlyList<string> Visible(
        ObjectMenuSurface surface,
        ObjectMenuState state) =>
        MailShellObjectMenu.Build(surface, state)
            .Where(entry => entry.IsVisible)
            .Select(entry => entry.Header)
            .ToList();

    private static ObjectMenuState State(
        bool outbox = false,
        bool unified = false,
        bool trash = false,
        bool junk = false,
        bool archive = false,
        bool canExpand = false,
        bool expanded = false,
        bool newestFirst = true,
        bool unread = false,
        bool flagged = false,
        bool attachments = false) =>
        new()
        {
            ShowingOutbox = outbox,
            UnifiedInbox = unified,
            TrashScope = trash,
            JunkScope = junk,
            ArchiveScope = archive,
            CanExpandConversation = canExpand,
            ConversationExpanded = expanded,
            ConversationNewestFirst = newestFirst,
            AnyUnread = unread,
            AnyFlagged = flagged,
            MessageHasAttachments = attachments,
        };

    [TestMethod]
    public void Thread_menu_in_a_mailbox_lists_the_message_verb_set()
    {
        CollectionAssert.AreEqual(
            SharedMessageHeaders
                .Concat(["Mark Unread", "Flag", "Move", "Copy", "Archive", "Move to Junk", "Delete"])
                .ToArray(),
            Visible(ObjectMenuSurface.ThreadList, State()).ToArray());
    }

    [TestMethod]
    public void Reading_flyout_in_a_mailbox_matches_the_Thread_verbs_plus_Find()
    {
        CollectionAssert.AreEqual(
            SharedMessageHeaders
                .Concat(
                [
                    "Mark Unread",
                    "Flag",
                    "Move",
                    "Copy",
                    "Find in conversation",
                    "Archive",
                    "Move to Junk",
                    "Delete",
                ])
                .ToArray(),
            Visible(ObjectMenuSurface.Reading, State()).ToArray());
    }

    [TestMethod]
    public void Mailbox_scope_states_toggle_the_scope_verbs_on_both_surfaces()
    {
        foreach (var surface in new[] { ObjectMenuSurface.ThreadList, ObjectMenuSurface.Reading })
        {
            Assert.IsTrue(
                Visible(surface, State(unified: true)).Contains("Show in Mailbox"),
                surface.ToString());
            Assert.IsTrue(
                Visible(surface, State(trash: true)).Contains("Restore"),
                surface.ToString());
            var junk = Visible(surface, State(junk: true));
            Assert.IsTrue(junk.Contains("Not Junk"), surface.ToString());
            Assert.IsFalse(junk.Contains("Move to Junk"), surface.ToString());
            Assert.IsFalse(
                Visible(surface, State(archive: true)).Contains("Archive"),
                surface.ToString());
            Assert.IsFalse(
                Visible(surface, State(unified: true, outbox: true)).Contains("Show in Mailbox"),
                surface.ToString());
        }
    }

    [TestMethod]
    public void Outbox_state_echoes_retry_discard_on_the_list_menu_only()
    {
        CollectionAssert.AreEqual(
            new[] { "Mark Unread", "Flag", "Retry", "Discard" },
            Visible(ObjectMenuSurface.ThreadList, State(outbox: true)).ToArray());
        CollectionAssert.AreEqual(
            new[] { "Mark Unread", "Flag" },
            Visible(ObjectMenuSurface.Reading, State(outbox: true)).ToArray());
    }

    [TestMethod]
    public void Message_card_state_adds_open_attachment_after_show_original()
    {
        var withFile = Visible(ObjectMenuSurface.Reading, State(attachments: true));
        Assert.IsTrue(withFile.Contains("Open Attachment"));
        Assert.AreEqual(
            "Open Attachment",
            withFile[withFile.ToList().IndexOf("Show original") + 1]);

        Assert.IsFalse(
            Visible(ObjectMenuSurface.Reading, State()).Contains("Open Attachment"));
        Assert.IsFalse(
            Visible(ObjectMenuSurface.Reading, State(outbox: true, attachments: true))
                .Contains("Open Attachment"));
    }

    [TestMethod]
    public void Read_and_flag_labels_follow_selection_state()
    {
        var unread = Visible(ObjectMenuSurface.ThreadList, State(unread: true));
        Assert.IsTrue(unread.Contains("Mark Read"));
        Assert.IsFalse(unread.Contains("Mark Unread"));
        var flagged = Visible(ObjectMenuSurface.ThreadList, State(flagged: true));
        Assert.IsTrue(flagged.Contains("Unflag"));
        Assert.IsFalse(flagged.Contains("Flag"));
    }

    [TestMethod]
    public void Conversation_verbs_follow_expand_state()
    {
        var collapsed = Visible(
            ObjectMenuSurface.Reading,
            State(canExpand: true, expanded: false, newestFirst: true));
        Assert.IsTrue(collapsed.Contains("Expand all"));
        Assert.IsFalse(collapsed.Contains("Collapse all"));
        Assert.IsTrue(collapsed.Contains("Oldest on top"));

        var expanded = Visible(
            ObjectMenuSurface.Reading,
            State(canExpand: true, expanded: true, newestFirst: false));
        Assert.IsTrue(expanded.Contains("Collapse all"));
        Assert.IsFalse(expanded.Contains("Expand all"));
        Assert.IsTrue(expanded.Contains("Newest on top"));

        var single = Visible(ObjectMenuSurface.Reading, State(canExpand: false));
        Assert.IsFalse(single.Contains("Expand all"));
        Assert.IsFalse(single.Contains("Collapse all"));
        Assert.IsFalse(single.Contains("Newest on top"));
        Assert.IsFalse(single.Contains("Oldest on top"));
    }

    [TestMethod]
    public void Build_is_deterministic_and_preserves_shared_order()
    {
        var state = State(unified: true, canExpand: true, unread: true, flagged: true);
        var thread = MailShellObjectMenu.Build(ObjectMenuSurface.ThreadList, state)
            .Select(entry => entry.Verb)
            .ToList();
        var reading = MailShellObjectMenu.Build(ObjectMenuSurface.Reading, state)
            .Select(entry => entry.Verb)
            .ToList();
        var sharedPrefix = reading.TakeWhile(verb => verb != ObjectMenuVerb.FindInConversation).ToList();
        CollectionAssert.AreEqual(sharedPrefix, thread.Take(sharedPrefix.Count).ToList());
        CollectionAssert.AreEqual(
            MailShellObjectMenu.Build(ObjectMenuSurface.Reading, state).Select(e => (e.Verb, e.Header, e.IsVisible)).ToList(),
            MailShellObjectMenu.Build(ObjectMenuSurface.Reading, state).Select(e => (e.Verb, e.Header, e.IsVisible)).ToList());
    }
}
