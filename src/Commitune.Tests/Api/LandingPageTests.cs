using System.Net;

namespace Commitune.Tests.Api;

/// <summary>
/// The landing page is served by the same app that answers the webhook, from wwwroot. What
/// can break silently is the wiring, not the HTML: a missing UseStaticFiles, or a publish that
/// leaves wwwroot behind, both turn "/" into a 404 that nothing else notices.
/// </summary>
public class LandingPageTests : IDisposable
{
    private readonly CommituneAppFactory _app = new();

    public void Dispose()
    {
        _app.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task The_root_serves_the_landing_page()
    {
        var response = await _app.CreateClient().GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("https://t.me/CommituneBot", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/styles.css", "text/css")]
    [InlineData("/commitune-logo.png", "image/png")]
    public async Task The_assets_the_page_asks_for_are_served(string path, string mediaType)
    {
        var response = await _app.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// Static files run before the endpoints, so a file named like a route would quietly take
    /// it over. Nothing in wwwroot may shadow what the bot and GitHub call.
    /// </summary>
    [Fact]
    public async Task The_endpoints_still_answer_with_the_landing_page_in_front_of_them()
    {
        var response = await _app.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }
}
