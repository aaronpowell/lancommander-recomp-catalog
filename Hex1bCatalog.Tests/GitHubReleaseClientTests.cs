using System.Net;
using System.Text;
using Hex1bCatalog.Core;
using Hex1bCatalog.Infrastructure;

namespace Hex1bCatalog.Tests;

public class GitHubReleaseClientTests
{
    [Fact]
    public async Task UsesFixtureAndFiltersWindowsAssets()
    {
        var json = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "github-release.json"));
        var client = new GitHubReleaseClient(new HttpClient(
            new StubHttpMessageHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            })));
        var entry = new CatalogEntry
        {
            Name = "Fixture Game",
            Repository = "fixture/game",
            FolderName = "FixtureGame",
            ReleaseAssetFilter = "portable",
        };

        var release = await client.GetLatestAsync(entry);

        Assert.Equal("v1.2.3", release.Tag);
        Assert.Equal("fixture-portable-win-x64.zip", Assert.Single(release.Assets).Name);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory());
    }
}
