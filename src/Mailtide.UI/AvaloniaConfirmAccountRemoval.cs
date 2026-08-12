using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Mailtide.UI;

/// <summary>
/// Avalonia confirmation dialog for Account removal.
/// Uses an in-tree overlay so Android / single-view lifetimes work (no Window owner).
/// </summary>
public sealed class AvaloniaConfirmAccountRemoval : IConfirmAccountRemoval
{
    private readonly Func<Visual?> _hostFactory;

    public AvaloniaConfirmAccountRemoval(Func<Visual?> hostFactory)
    {
        ArgumentNullException.ThrowIfNull(hostFactory);
        _hostFactory = hostFactory;
    }

    public async Task<bool> ConfirmAsync(
        string accountDisplayName,
        CancellationToken cancellationToken = default)
    {
        var host = _hostFactory()
            ?? throw new InvalidOperationException("Unable to open Remove Account confirmation.");

        var tcs = new TaskCompletionSource<bool>();

        var cancel = new Button { Content = "Cancel", Width = 88 };
        var remove = new Button { Content = "Remove", Width = 88 };

        var content = new DockPanel
        {
            Width = 388,
            Children =
            {
                new TextBlock
                {
                    Text = "Remove Account",
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                    [DockPanel.DockProperty] = Dock.Top,
                    Margin = new Thickness(0, 0, 0, 12),
                },
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
        };

        cancel.Click += (_, _) => tcs.TrySetResult(false);
        remove.Click += (_, _) => tcs.TrySetResult(true);

        return await AvaloniaOverlayDialog
            .ShowAsync(host, content, tcs.Task.WaitAsync(cancellationToken))
            .ConfigureAwait(true);
    }
}
