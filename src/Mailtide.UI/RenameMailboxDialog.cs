using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Mailtide.UI;

public sealed class RenameMailboxDialog : UserControl
{
    private readonly TaskCompletionSource<string?> _completion = new();
    private readonly TextBox _nameBox;

    public Task<string?> Completion => _completion.Task;

    public RenameMailboxDialog()
    {
        _nameBox = new TextBox { PlaceholderText = "Mailbox name", MinWidth = 240 };
        var rename = new Button { Content = "Rename", Width = 90 };
        rename.Click += (_, _) => _completion.TrySetResult(_nameBox.Text);
        var cancel = new Button { Content = "Cancel", Width = 90 };
        cancel.Click += (_, _) => _completion.TrySetResult(null);

        Content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Rename Mailbox", FontWeight = FontWeight.SemiBold },
                _nameBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { rename, cancel },
                },
            },
        };
    }
}