using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Mailtide.Core;

namespace Mailtide.UI;

public sealed class MoveMailboxDialog : UserControl
{
    private readonly TaskCompletionSource<Guid?> _completion = new();
    private readonly IReadOnlyList<(Guid Id, string Label)> _items;
    private readonly Guid? _currentId;
    private readonly StackPanel _buttons = new() { Spacing = 8 };
    private readonly TextBox _filter = new()
    {
        PlaceholderText = "Filter",
        MinWidth = 240,
    };

    public Task<Guid?> Completion => _completion.Task;

    public MoveMailboxDialog(
        IReadOnlyList<MailboxInfo> mailboxes,
        Guid? currentMailboxId,
        IReadOnlyList<Guid>? recentIds = null)
        : this(mailboxes, currentMailboxId, recentIds, "Move to Mailbox")
    {
    }

    public MoveMailboxDialog(
        IReadOnlyList<MailboxInfo> mailboxes,
        Guid? currentMailboxId,
        IReadOnlyList<Guid>? recentIds,
        string title)
        : this(ToItems(mailboxes, currentMailboxId, recentIds), currentMailboxId, title)
    {
    }

    public MoveMailboxDialog(
        IReadOnlyList<(Guid Id, string Label)> items,
        Guid? currentId,
        string title)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        _items = items;
        _currentId = currentId;

        _filter.TextChanged += (_, _) => RebuildButtons();
        _filter.KeyDown += OnFilterKeyDown;

        var root = new StackPanel { Spacing = 8, MinWidth = 240 };
        root.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
        });
        root.Children.Add(_filter);
        root.Children.Add(_buttons);

        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => _completion.TrySetResult(null);
        root.Children.Add(cancel);
        Content = root;
        RebuildButtons();
    }

    private void OnFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_filter.Text))
        {
            return;
        }

        var match = MailShellFormatting.FilterPickerItems(_items, _filter.Text).FirstOrDefault();
        if (match.Label is null)
        {
            return;
        }

        e.Handled = true;
        _completion.TrySetResult(match.Id);
    }

    private void RebuildButtons()
    {
        _buttons.Children.Clear();
        foreach (var item in MailShellFormatting.FilterPickerItems(_items, _filter.Text))
        {
            var destinationId = item.Id;
            var button = new Button
            {
                Content = item.Label,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            button.Click += (_, _) => _completion.TrySetResult(destinationId);
            _buttons.Children.Add(button);
        }
    }

    private static IReadOnlyList<(Guid Id, string Label)> ToItems(
        IReadOnlyList<MailboxInfo> mailboxes,
        Guid? currentMailboxId,
        IReadOnlyList<Guid>? recentIds)
    {
        ArgumentNullException.ThrowIfNull(mailboxes);
        var recent = recentIds?.ToHashSet() ?? [];
        return mailboxes
            .Select(mailbox =>
            {
                var title = MailShellFormatting.MailboxMoveLabel(mailbox);
                var label = mailbox.Role is { } role
                    ? $"{title} ({role})"
                    : title;
                if (recent.Contains(mailbox.Id))
                {
                    label += " (recent)";
                }

                if (mailbox.Id == currentMailboxId)
                {
                    label += " (current)";
                }

                return (mailbox.Id, label);
            })
            .ToList();
    }
}
