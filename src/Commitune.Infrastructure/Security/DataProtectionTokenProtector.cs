using Microsoft.AspNetCore.DataProtection;

namespace Commitune.Infrastructure.Security;

public sealed class DataProtectionTokenProtector : ITokenProtector
{
    /// <summary>Changing this purpose invalidates every stored token — treat it as permanent.</summary>
    public const string Purpose = "Commitune.GithubAccessToken.v1";

    private readonly IDataProtector _protector;

    public DataProtectionTokenProtector(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector(Purpose);

    public string Protect(string accessToken) => _protector.Protect(accessToken);

    public string Unprotect(string protectedAccessToken) => _protector.Unprotect(protectedAccessToken);
}
