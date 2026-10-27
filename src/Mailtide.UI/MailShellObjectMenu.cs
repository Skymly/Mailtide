namespace Mailtide.UI;

public enum ObjectMenuSurface
{
    ThreadList = 0,
    Reading = 1,
}

public enum ObjectMenuVerb
{
    Reply = 0,
    ReplyAll,
    Forward,
    ForwardAsAttachment,
    EditAsNew,
    SaveAsEml,
    ShowOriginal,
    OpenAttachment,
    CopySubject,
    CopyAddress,
    SearchFromSender,
    SearchToRecipient,
    CopyHeaders,
    CopyMessageId,
    ToggleRead,
    ToggleFlag,
    Move,
    Copy,
    RevealInMailbox,
    FindInConversation,
    ExpandConversation,
    CollapseConversation,
    ConversationOrder,
    Archive,
    MoveToJunk,
    NotJunk,
    Delete,
    Restore,
    RetryOutbox,
    DiscardOutbox,
}

public sealed record ObjectMenuEntry(ObjectMenuVerb Verb, string Header, bool IsVisible);

public sealed record ObjectMenuState
{
    public bool ShowingOutbox { get; init; }

    public bool UnifiedInbox { get; init; }

    public bool TrashScope { get; init; }

    public bool JunkScope { get; init; }

    public bool ArchiveScope { get; init; }

    public bool CanExpandConversation { get; init; }

    public bool ConversationExpanded { get; init; }

    public bool ConversationNewestFirst { get; init; }

    public bool AnyUnread { get; init; }

    public bool AnyFlagged { get; init; }

    public bool MessageHasAttachments { get; init; }
}

/// <summary>
/// Single builder for the object-verb list shown by the Thread-list context menu
/// and the reading-pane "..." overflow. Same items, same order, same visibility.
/// </summary>
public static class MailShellObjectMenu
{
    public static IReadOnlyList<ObjectMenuEntry> Build(ObjectMenuSurface surface, ObjectMenuState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var message = !state.ShowingOutbox;
        var entries = new List<ObjectMenuEntry>
        {
            new(ObjectMenuVerb.Reply, "Reply", message),
            new(ObjectMenuVerb.ReplyAll, "Reply All", message),
            new(ObjectMenuVerb.Forward, "Forward", message),
            new(ObjectMenuVerb.ForwardAsAttachment, "Forward as Attachment", message),
            new(ObjectMenuVerb.EditAsNew, "Edit as New", message),
            new(ObjectMenuVerb.SaveAsEml, "Save as .eml", message),
            new(ObjectMenuVerb.ShowOriginal, "Show original", message),
            new(ObjectMenuVerb.OpenAttachment, "Open Attachment", message && state.MessageHasAttachments),
            new(ObjectMenuVerb.CopySubject, "Copy subject", message),
            new(ObjectMenuVerb.CopyAddress, "Copy address", message),
            new(ObjectMenuVerb.SearchFromSender, "Search from sender", message),
            new(ObjectMenuVerb.SearchToRecipient, "Search to recipient", message),
            new(ObjectMenuVerb.CopyHeaders, "Copy headers", message),
            new(ObjectMenuVerb.CopyMessageId, "Copy Message-ID", message),
            new(ObjectMenuVerb.ToggleRead, state.AnyUnread ? "Mark Read" : "Mark Unread", true),
            new(ObjectMenuVerb.ToggleFlag, state.AnyFlagged ? "Unflag" : "Flag", true),
            new(ObjectMenuVerb.Move, "Move", message),
            new(ObjectMenuVerb.Copy, "Copy", message),
            new(ObjectMenuVerb.RevealInMailbox, "Show in Mailbox", message && state.UnifiedInbox),
        };
        if (surface == ObjectMenuSurface.Reading)
        {
            entries.Add(new(ObjectMenuVerb.FindInConversation, "Find in conversation", message));
            entries.Add(new(
                ObjectMenuVerb.ExpandConversation,
                "Expand all",
                state.CanExpandConversation && !state.ConversationExpanded));
            entries.Add(new(
                ObjectMenuVerb.CollapseConversation,
                "Collapse all",
                state.CanExpandConversation && state.ConversationExpanded));
            entries.Add(new(
                ObjectMenuVerb.ConversationOrder,
                state.ConversationNewestFirst ? "Oldest on top" : "Newest on top",
                state.CanExpandConversation));
        }

        entries.Add(new(ObjectMenuVerb.Archive, "Archive", message && !state.ArchiveScope));
        entries.Add(new(ObjectMenuVerb.MoveToJunk, "Move to Junk", message && !state.JunkScope));
        entries.Add(new(ObjectMenuVerb.NotJunk, "Not Junk", state.JunkScope));
        entries.Add(new(ObjectMenuVerb.Delete, "Delete", message));
        entries.Add(new(ObjectMenuVerb.Restore, "Restore", state.TrashScope));
        if (surface == ObjectMenuSurface.ThreadList)
        {
            entries.Add(new(ObjectMenuVerb.RetryOutbox, "Retry", state.ShowingOutbox));
            entries.Add(new(ObjectMenuVerb.DiscardOutbox, "Discard", state.ShowingOutbox));
        }

        return entries;
    }
}
