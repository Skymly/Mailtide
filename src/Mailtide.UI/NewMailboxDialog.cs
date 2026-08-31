using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Mailtide.UI;

public sealed class NewMailboxDialog : UserControl
{
    private readonly TaskCompletionSource<string?> _completion = new();
    private readonly TextBox _nameBox;

    public Task<string?> Completion => _completion.Task;

    public NewMailboxDialog()
    {
        _nameBox = new TextBox { PlaceholderText = "Mailbox name", MinWidth = 240 };
        var create = new Button { Content = "Create", Width = 90 };
        create.Click += (_, _) => _completion.TrySetResult(_nameBox.Text);
        var cancel = new Button { Content = "Cancel", Width = 90 };
        cancel.Click += (_, _) => _completion.TrySetResult(null);

        Content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "New Mailbox", FontWeight = FontWeight.SemiBold },
                _nameBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { create, cancel },
                },
            },
        };
    }
}