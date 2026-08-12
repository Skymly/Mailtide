using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Mailtide.UI;

/// <summary>
/// Avalonia confirmation dialog for Account removal.
/// </summary>
public sealed class AvaloniaConfirmAccountRemoval : IConfirmAccountRemoval
{
    private readonly Func<Window?> _ownerFactory;

    public AvaloniaConfirmAccountRemoval(Func<Window?> ownerFactory)
    {
        ArgumentNullException.ThrowIfNull(ownerFactory);
        _ownerFactory = ownerFactory;
    }

    public async Task<bool> ConfirmAsync(
        string accountDisplayName,
        CancellationToken cancellationToken = default)
    {
        var owner = _ownerFactory();
        var tcs = new TaskCompletionSource<bool>();

        var cancel = new Button { Content = "Cancel", Width = 88 };
        var remove = new Button { Content = "Remove", Width = 88 };

        var dialog = new Window
        {
            Title = "Remove Account",
            Width = 420,
            Height = 180,
            CanResize = false,
            WindowStartupLocation = owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner,
            Content = new DockPanel
            {
                Margin = new Thickness(16),
                Children =
                {
                    new TextBlock
                    {
                        Text =
                            $"Remove account \"{accountDisplayName}\"? Local Messages, related data, and credentials will be cleared.",
                        TextWrapping = TextWrapping.Wrap,
                        [DockPanel.DockProperty] = Dock.Top,
                        Margin = new Thickness(0, 0, 0, 16),
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        [DockPanel.DockProperty] = Dock.Bottom,
                        Children = { cancel, remove },
                    },
                },
            },
        };

        cancel.Click += (_, _) =>
        {
            tcs.TrySetResult(false);
            dialog.Close();
        };
        remove.Click += (_, _) =>
        {
            tcs.TrySetResult(true);
            dialog.Close();
        };
        dialog.Closed += (_, _) => tcs.TrySetResult(false);

        if (owner is null)
        {
            dialog.Show();
        }
        else
        {
            await dialog.ShowDialog(owner).ConfigureAwait(true);
        }

        return await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
    }
}
