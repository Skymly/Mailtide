namespace Mailtide.Core;

public sealed record MessageThreadInfo(
    MessageInfo Latest,
    IReadOnlyList<MessageInfo> Messages);
