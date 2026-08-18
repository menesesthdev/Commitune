using System.ComponentModel.DataAnnotations;

namespace Commitune.Infrastructure.Configuration;

public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";

    /// <summary>Env var: <c>GITHUB_CLIENT_ID</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Env var: <c>GITHUB_CLIENT_SECRET</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Absolute URL GitHub redirects back to. Env var: <c>GITHUB_CALLBACK_URL</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    [Url]
    public string CallbackUrl { get; set; } = string.Empty;

    /// <summary>How long an issued OAuth <c>state</c> stays valid.</summary>
    public TimeSpan StateLifetime { get; set; } = TimeSpan.FromMinutes(15);
}
