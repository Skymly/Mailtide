using Duende.IdentityModel.Client;
using Mailtide.Core;

namespace Mailtide.Android.Host;

/// <summary>
/// Microsoft consumer discovery uses Authority .../consumers/v2.0 while the document
/// issuer is the MSA tenant GUID. Treat those URLs as aliases; keep other checks.
/// </summary>
internal sealed class MicrosoftConsumerAuthorityValidationStrategy : IAuthorityValidationStrategy
{
    private static readonly AuthorityUrlValidationStrategy Inner = new();

    public AuthorityValidationResult IsIssuerNameValid(string issuerName, string expectedAuthority)
    {
        var direct = Inner.IsIssuerNameValid(issuerName, expectedAuthority);
        if (direct.Success || AreAliases(issuerName, expectedAuthority))
        {
            return AuthorityValidationResult.SuccessResult;
        }

        return direct;
    }

    public AuthorityValidationResult IsEndpointValid(
        string endpoint,
        IEnumerable<string> expectedAuthority) =>
        Inner.IsEndpointValid(endpoint, expectedAuthority);

    private static bool AreAliases(string issuerName, string expectedAuthority)
    {
        var issuer = TrimSlash(issuerName);
        var authority = TrimSlash(expectedAuthority);
        var consumers = TrimSlash($"{MicrosoftConsumerMailPreset.Authority}/v2.0");
        var msa = TrimSlash(MicrosoftConsumerMailPreset.DiscoveryIssuer);
        return (Ordinal(authority, consumers) && Ordinal(issuer, msa))
            || (Ordinal(authority, msa) && Ordinal(issuer, consumers));
    }

    private static string TrimSlash(string value) => value.TrimEnd('/');

    private static bool Ordinal(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal);
}
