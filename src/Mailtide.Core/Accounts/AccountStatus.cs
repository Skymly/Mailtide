namespace Mailtide.Core;

public enum AccountSyncState
{
    Idle = 0,
    Syncing = 1,
    Error = 2,
}

public sealed record AccountStatus(
    AccountSyncState State,
    string? ErrorMessage = null,
    bool RequiresSignIn = false)
{
    public const string AuthenticationFailedMessage = "Authentication failed. Sign in again.";

    public static AccountStatus Idle() => new(AccountSyncState.Idle);

    public static AccountStatus Syncing() => new(AccountSyncState.Syncing);

    public static AccountStatus Error(string message, bool requiresSignIn = false) =>
        new(AccountSyncState.Error, message, requiresSignIn);

    public static AccountStatus AuthenticationFailed() =>
        Error(AuthenticationFailedMessage, requiresSignIn: true);
}