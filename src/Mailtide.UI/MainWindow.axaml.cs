using Avalonia.Controls;

namespace Mailtide.UI;

public partial class MainWindow : Window
{
    /// <summary>Designer / XAML loader entry point.</summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MailShellView shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        InitializeComponent();
        Content = shell;
    }
}
