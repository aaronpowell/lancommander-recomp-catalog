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
            new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
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

    [Fact]
    public async Task SendsTokenOnlyOnGitHubApiRequest()
    {
        var json = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "github-release.json"));
        var authorization = new List<string?>();
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            authorization.Add(request.Headers.Authorization?.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }));
        var client = new GitHubReleaseClient(httpClient, "secret-token");

        await client.GetLatestAsync(new CatalogEntry
        {
            Name = "Fixture Game",
            Repository = "fixture/game",
            FolderName = "FixtureGame",
        });
        using var unrelatedResponse = await httpClient.GetAsync("https://catalog.test/index.json");

        Assert.Equal("Bearer secret-token", authorization[0]);
        Assert.Null(authorization[1]);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
