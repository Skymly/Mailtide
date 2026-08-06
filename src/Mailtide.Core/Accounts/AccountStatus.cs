namespace Mailtide.Core;

public enum AccountSyncState
{
    Idle = 0,
    Syncing = 1,
    Error = 2,
}

public sealed record AccountStatus(AccountSyncState State, string? ErrorMessage = null)
{
    public static AccountStatus Idle() => new(AccountSyncState.Idle);

    public static AccountStatus Syncing() => new(AccountSyncState.Syncing);

    public static AccountStatus Error(string message) => new(AccountSyncState.Error, message);
}
