using Mailtide.Core;

namespace Mailtide.UI;

/// <summary>
/// UI-framework-agnostic compose / Outbox surface. Issues Core intents only.
/// </summary>
public sealed class ComposeOutboxShell
{
    private readonly MailtideApp _app;

    public ComposeOutboxShell(MailtideApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        _app = app;
    }

    public Guid? SelectedAccountId { get; private set; }

    public Guid? SelectedDraftId { get; private set; }

    public IReadOnlyList<DraftInfo> Drafts { get; private set; } = [];

    public IReadOnlyList<OutboxItemInfo> OutboxItems { get; private set; } = [];

    public IReadOnlyList<DraftAttachmentInfo> DraftAttachments { get; private set; } = [];

    public async Task SelectAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        if (SelectedAccountId != accountId)
        {
            SelectedDraftId = null;
        }

        SelectedAccountId = accountId;
        await RefreshListsAsync(accountId, cancellationToken).ConfigureAwait(false);
    }

    public void ClearSelection()
    {
        SelectedAccountId = null;
        SelectedDraftId = null;
        Drafts = [];
        OutboxItems = [];
        DraftAttachments = [];
    }

    public void StartNewDraft()
    {
        _ = RequireSelectedAccount();
        SelectedDraftId = null;
        DraftAttachments = [];
    }

    public async Task SaveDraftAsync(
        string toAddresses,
        string subject,
        string bodyText,
        string ccAddresses = "",
        string bccAddresses = "",
        CancellationToken cancellationToken = default)
    {
        var accountId = RequireSelectedAccount();
        var addresses = ParseAddresses(toAddresses);
        var cc = ParseAddresses(ccAddresses);
        var bcc = ParseAddresses(bccAddresses);
        var saved = await _app
            .SaveDraftAsync(
                accountId,
                new DraftContent(addresses, subject, bodyText) { CcAddresses = cc, BccAddresses = bcc },
                SelectedDraftId,
                cancellationToken)
            .ConfigureAwait(false);
        SelectedDraftId = saved.Id;
        await RefreshListsAsync(accountId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DraftInfo> SelectDraftAsync(
        Guid draftId,
        CancellationToken cancellationToken = default)
    {
        var accountId = RequireSelectedAccount();
        var draft = Drafts.FirstOrDefault(item => item.Id == draftId);
        if (draft is null)
        {
            Drafts = await _app.ListDraftsAsync(accountId, cancellationToken).ConfigureAwait(false);
            draft = Drafts.FirstOrDefault(item => item.Id == draftId);
        }

        if (draft is null)
        {
            throw new InvalidOperationException($"Draft '{draftId}' was not found.");
        }

        SelectedDraftId = draft.Id;
        DraftAttachments = await _app
            .ListDraftAttachmentsAsync(accountId, draft.Id, cancellationToken)
            .ConfigureAwait(false);
        return draft;
    }

    public async Task<DraftInfo> StartForwardAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var draft = await _app
            .StartForwardAsync(accountId, messageId, cancellationToken)
            .ConfigureAwait(false);
        SelectedAccountId = accountId;
        SelectedDraftId = draft.Id;
        await RefreshListsAsync(accountId, cancellationToken).ConfigureAwait(false);
        return draft;
    }
    public async Task<DraftInfo> StartReplyAllAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var draft = await _app
            .StartReplyAllAsync(accountId, messageId, cancellationToken)
            .ConfigureAwait(false);
        SelectedAccountId = accountId;
        SelectedDraftId = draft.Id;
        await RefreshListsAsync(accountId, cancellationToken).ConfigureAwait(false);
        return draft;
    }

    public async Task<DraftInfo> StartReplyAsync(
        Guid accountId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var draft = await _app
            .StartReplyAsync(accountId, messageId, cancellationToken)
            .ConfigureAwait(false);
        SelectedAccountId = accountId;
        SelectedDraftId = draft.Id;
        await RefreshListsAsync(accountId, cancellationToken).ConfigureAwait(false);
        return draft;
    }

    public async Task AddDraftAttachmentAsync(
        string fileName,
        string contentType,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        var accountId = RequireSelectedAccount();
        if (SelectedDraftId is not { } draftId)
        {
            throw new InvalidOperationException("Save the Draft before attaching a file.");
        }

        await _app
            .AddDraftAttachmentAsync(accountId, draftId, fileName, contentType, content, cancellationToken)
            .ConfigureAwait(false);
        DraftAttachments = await _app
            .ListDraftAttachmentsAsync(accountId, draftId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RemoveDraftAttachmentAsync(
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        var accountId = RequireSelectedAccount();
        await _app
            .RemoveDraftAttachmentAsync(accountId, attachmentId, cancellationToken)
            .ConfigureAwait(false);
        if (SelectedDraftId is { } draftId)
        {
            DraftAttachments = await _app
                .ListDraftAttachmentsAsync(accountId, draftId, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            DraftAttachments = [];
        }
    }

    public async Task DiscardDraftAsync(Guid draftId, CancellationToken cancellationToken = default)
    {
        var accountId = RequireSelectedAccount();
        await _app.DiscardDraftAsync(accountId, draftId, cancellationToken).ConfigureAwait(false);
        if (SelectedDraftId == draftId)
        {
            SelectedDraftId = null;
        }

        await RefreshListsAsync(accountId, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendAsync(Guid draftId, CancellationToken cancellationToken = default)
    {
        var accountId = RequireSelectedAccount();
        await _app.SendAsync(accountId, draftId, cancellationToken).ConfigureAwait(false);
        if (SelectedDraftId == draftId)
        {
            SelectedDraftId = null;
        }

        await RefreshListsAsync(accountId, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendNowAsync(CancellationToken cancellationToken = default)
    {
        var accountId = RequireSelectedAccount();
        await _app.SendNowAsync(accountId, cancellationToken).ConfigureAwait(false);
        await RefreshListsAsync(accountId, cancellationToken).ConfigureAwait(false);
    }

    public async Task SyncNowAsync(CancellationToken cancellationToken = default)
    {
        var accountId = RequireSelectedAccount();
        await _app.SyncNowAsync(accountId, cancellationToken).ConfigureAwait(false);
    }

    public async Task RetryOutboxItemAsync(Guid outboxItemId, CancellationToken cancellationToken = default)
    {
        var accountId = RequireSelectedAccount();
        await _app.RetryOutboxItemAsync(accountId, outboxItemId, cancellationToken).ConfigureAwait(false);
        await RefreshListsAsync(accountId, cancellationToken).ConfigureAwait(false);
    }

    public async Task DiscardOutboxItemAsync(Guid outboxItemId, CancellationToken cancellationToken = default)
    {
        var accountId = RequireSelectedAccount();
        await _app.DiscardOutboxItemAsync(accountId, outboxItemId, cancellationToken).ConfigureAwait(false);
        await RefreshListsAsync(accountId, cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshListsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        Drafts = await _app.ListDraftsAsync(accountId, cancellationToken).ConfigureAwait(false);
        OutboxItems = await _app.ListOutboxAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (SelectedDraftId is { } draftId)
        {
            DraftAttachments = await _app
                .ListDraftAttachmentsAsync(accountId, draftId, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            DraftAttachments = [];
        }
    }

    private Guid RequireSelectedAccount() =>
        SelectedAccountId
        ?? throw new InvalidOperationException("Select an Account before composing.");

    private static IReadOnlyList<string> ParseAddresses(string toAddresses) =>
        toAddresses
            .Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
