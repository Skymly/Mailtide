using Avalonia.Controls;
using Avalonia.Interactivity;
using Mailtide.Core;
using Mailtide.Core.Auth;

namespace Mailtide.UI;

public partial class EditAccountDialog : UserControl
{
    private readonly BrowseShell _browse;
    private readonly AccountInfo _account;
    private readonly EditMode _mode;
    private readonly TaskCompletionSource<bool> _completion = new();

    public AccountInfo? UpdatedAccount { get; private set; }

    public Task<bool> Completion => _completion.Task;

    public EditAccountDialog()
    {
        // Designer
        _browse = null!;
        _account = null!;
        InitializeComponent();
    }

    public EditAccountDialog(BrowseShell browse, AccountInfo account)
    {
        ArgumentNullException.ThrowIfNull(browse);
        ArgumentNullException.ThrowIfNull(account);
        _browse = browse;
        _account = account;
        _mode = ResolveMode(account);
        InitializeComponent();
        Populate();
    }

    internal static bool MatchesQqMailPreset(AccountInfo account) =>
        account.CredentialKind == CredentialKind.Password
        && string.Equals(account.ImapHost, QqMailPreset.ImapHost, StringComparison.OrdinalIgnoreCase)
        && account.ImapPort == QqMailPreset.ImapPort
        && string.Equals(account.SmtpHost, QqMailPreset.SmtpHost, StringComparison.OrdinalIgnoreCase)
        && account.SmtpPort == QqMailPreset.SmtpPort;

    private static EditMode ResolveMode(AccountInfo account)
    {
        if (account.CredentialKind == CredentialKind.OAuth)
        {
            return EditMode.OAuth;
        }

        return MatchesQqMailPreset(account) ? EditMode.QqMail : EditMode.Manual;
    }

    private void Populate()
    {
        OAuthFields.IsVisible = _mode == EditMode.OAuth;
        PasswordFields.IsVisible = _mode is EditMode.Manual or EditMode.QqMail;
        QqSecretFields.IsVisible = _mode == EditMode.QqMail;
        ManualServerFields.IsVisible = _mode == EditMode.Manual;
        SaveButton.IsVisible = _mode is EditMode.Manual or EditMode.QqMail;
        ReauthorizeButton.IsVisible = _mode == EditMode.OAuth;

        AccountKindLabel.Text = _mode switch
        {
            EditMode.OAuth => _account.OAuthProvider switch
            {
                OAuthProvider.Google => "Google (OAuth)",
                OAuthProvider.MicrosoftConsumer => "Microsoft (OAuth)",
                _ => "OAuth",
            },
            EditMode.QqMail => "QQ Mail",
            _ => "Manual IMAP/SMTP",
        };

        if (_mode == EditMode.OAuth)
        {
            OAuthSummary.Text = $"{_account.DisplayName} · {_account.EmailAddress}";
            return;
        }

        DisplayNameBox.Text = _account.DisplayName;
        EmailBox.Text = _account.EmailAddress;

        if (_mode == EditMode.Manual)
        {
            ImapHostBox.Text = _account.ImapHost;
            ImapPortBox.Text = _account.ImapPort.ToString();
            SmtpHostBox.Text = _account.SmtpHost;
            SmtpPortBox.Text = _account.SmtpPort.ToString();
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => _completion.TrySetResult(false);

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        FormError.Text = string.Empty;
        SaveButton.IsEnabled = false;
        try
        {
            UpdatedAccount = _mode switch
            {
                EditMode.QqMail => await _browse
                    .UpdateQqMailAccountAsync(
                        _account.Id,
                        new QqMailAccountDraft(
                            DisplayNameOr(_account.DisplayName),
                            EmailBox.Text?.Trim() ?? string.Empty,
                            QqAuthCodeBox.Text ?? string.Empty))
                    .ConfigureAwait(true),
                EditMode.Manual => await _browse
                    .UpdateManualAccountAsync(_account.Id, BuildManualDraft())
                    .ConfigureAwait(true),
                _ => throw new InvalidOperationException("Save is not available for this Account."),
            };
            _completion.TrySetResult(true);
        }
        catch (Exception ex)
        {
            FormError.Text = ex.Message;
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private async void OnReauthorizeClick(object? sender, RoutedEventArgs e)
    {
        FormError.Text = string.Empty;
        ReauthorizeButton.IsEnabled = false;
        try
        {
            UpdatedAccount = await _browse
                .ReauthorizeAccountAsync(_account.Id)
                .ConfigureAwait(true);
            _completion.TrySetResult(true);
        }
        catch (Exception ex)
        {
            FormError.Text = ex.Message;
        }
        finally
        {
            ReauthorizeButton.IsEnabled = true;
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
            DisplayNameOr(_account.DisplayName),
            EmailBox.Text?.Trim() ?? string.Empty,
            ImapHostBox.Text?.Trim() ?? string.Empty,
            imapPort,
            SmtpHostBox.Text?.Trim() ?? string.Empty,
            smtpPort,
            PasswordBox.Text ?? string.Empty);
    }

    private enum EditMode
    {
        OAuth,
        QqMail,
        Manual,
    }
}
