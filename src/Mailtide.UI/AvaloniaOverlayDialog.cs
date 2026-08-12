using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Mailtide.UI;

/// <summary>
/// In-tree modal overlay for platforms where <see cref="Window.ShowDialog"/> is unavailable
/// (Android / single-view lifetimes).
/// </summary>
internal static class AvaloniaOverlayDialog
{
    public static async Task<TResult> ShowAsync<TResult>(Visual host, Control content, Task<TResult> result)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(result);

        var layer = OverlayLayer.GetOverlayLayer(host)
            ?? throw new InvalidOperationException("Unable to open dialog overlay.");

        var chrome = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x88, 0, 0, 0)),
            Child = new Border
            {
                Background = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(16),
                CornerRadius = new CornerRadius(4),
                Child = content,
            },
        };

        layer.Children.Add(chrome);
        try
        {
            return await result.ConfigureAwait(true);
        }
        finally
        {
            layer.Children.Remove(chrome);
        }
    }
}
