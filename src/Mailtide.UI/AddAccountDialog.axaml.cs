using Avalonia.Controls;
using Avalonia.Interactivity;
using Mailtide.Core;

namespace Mailtide.UI;

public partial class AddAccountDialog : UserControl
{
    private readonly BrowseShell _browse;
    private readonly TaskCompletionSource<bool> _completion = new();

    public AccountInfo? CreatedAccount { get; private set; }

    public Task<bool> Completion => _completion.Task;

    public AddAccountDialog()
    {
        // Designer
        _browse = null!;
        InitializeComponent();
    }

    public AddAccountDialog(BrowseShell browse)
    {
        ArgumentNullException.ThrowIfNull(browse);
        _browse = browse;
        InitializeComponent();
        AccountTypeBox.SelectionChanged += (_, _) => UpdateFieldVisibility();
        UpdateFieldVisibility();
    }

    private void UpdateFieldVisibility()
    {
        var kind = SelectedKind();
        QqFields.IsVisible = kind == "QQ";
        ManualFields.IsVisible = kind == "Manual";
    }

    private string SelectedKind()
    {
        if (AccountTypeBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            return tag;
        }

        return "Google";
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => _completion.TrySetResult(false);

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        FormError.Text = string.Empty;
        AddButton.IsEnabled = false;
        try
        {
            CreatedAccount = SelectedKind() switch
            {
                "Google" => await _browse
                    .AddGoogleAccountAsync(DisplayNameOr("Gmail"))
                    .ConfigureAwait(true),
                "Microsoft" => await _browse
                    .AddMicrosoftConsumerAccountAsync(DisplayNameOr("Outlook"))
                    .ConfigureAwait(true),
                "QQ" => await _browse
                    .AddQqMailAccountAsync(
                        new QqMailAccountDraft(
                            DisplayNameOr("QQ Mail"),
                            QqEmailBox.Text?.Trim() ?? string.Empty,
                            QqAuthCodeBox.Text?.Trim() ?? string.Empty))
                    .ConfigureAwait(true),
                "Manual" => await _browse
                    .AddManualAccountAsync(BuildManualDraft())
                    .ConfigureAwait(true),
                _ => throw new InvalidOperationException("Unknown account type."),
            };
            _completion.TrySetResult(true);
        }
        catch (Exception ex)
        {
            FormError.Text = ex.Message;
        }
        finally
        {
            AddButton.IsEnabled = true;
        }
    }

    private string DisplayNameOr(string fallback)
    {
        var value = DisplayNameBox.Text?.Trim();
        return string.IsNullOrEmpty(value) ? fallback : value;
    }

    private ManualAccountDraft BuildManualDraft()
    {
        if (!int.TryParse(ImapPortBox.Text, out var imapPort))
        {
            throw new InvalidOperationException("IMAP port must be a number.");
        }

        if (!int.TryParse(SmtpPortBox.Text, out var smtpPort))
        {
            throw new InvalidOperationException("SMTP port must be a number.");
        }

        return new ManualAccountDraft(
            DisplayNameOr("Manual"),
            ManualEmailBox.Text?.Trim() ?? string.Empty,
            ImapHostBox.Text?.Trim() ?? string.Empty,
            imapPort,
            SmtpHostBox.Text?.Trim() ?? string.Empty,
            smtpPort,
            PasswordBox.Text ?? string.Empty);
    }
}
