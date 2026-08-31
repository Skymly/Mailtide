using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Mailtide.Core;

namespace Mailtide.UI;

public sealed class MoveMailboxDialog : UserControl
{
    private readonly TaskCompletionSource<Guid?> _completion = new();

    public Task<Guid?> Completion => _completion.Task;

    public MoveMailboxDialog(IReadOnlyList<MailboxInfo> mailboxes, Guid? currentMailboxId)
    {
        ArgumentNullException.ThrowIfNull(mailboxes);

        var list = new StackPanel { Spacing = 8, MinWidth = 240 };
        list.Children.Add(new TextBlock
        {
            Text = "Move to Mailbox",
            FontWeight = FontWeight.SemiBold,
        });

        foreach (var mailbox in mailboxes)
        {
            var destinationId = mailbox.Id;
            var label = mailbox.Role is { } role
                ? $"{mailbox.Name} ({role})"
                : mailbox.Name;
            if (destinationId == currentMailboxId)
            {
                label += " (current)";
            }

            var button = new Button
            {
                Content = label,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            button.Click += (_, _) => _completion.TrySetResult(destinationId);
            list.Children.Add(button);
        }

        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => _completion.TrySetResult(null);
        list.Children.Add(cancel);
        Content = list;
    }
}