namespace Mailtide.UI;

/// <summary>
/// UI/Host confirmation gate before Core RemoveAccount runs.
/// </summary>
public interface IConfirmAccountRemoval
{
    Task<bool> ConfirmAsync(string accountDisplayName, CancellationToken cancellationToken = default);
}
