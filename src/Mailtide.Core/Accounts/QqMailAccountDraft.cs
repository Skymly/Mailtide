namespace Mailtide.Core;

/// <summary>
/// Person-facing input for adding a QQ Mail Account. Server endpoints come from <see cref="QqMailPreset"/>.
/// </summary>
public sealed record QqMailAccountDraft(
    string DisplayName,
    string EmailAddress,
    string AuthorizationCode);
