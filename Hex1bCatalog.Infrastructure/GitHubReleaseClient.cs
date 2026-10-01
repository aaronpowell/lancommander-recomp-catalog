using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hex1bCatalog.Core;

namespace Hex1bCatalog.Infrastructure;

public sealed class GitHubReleaseClient : IReleaseClient
{
    private readonly HttpClient _httpClient;

    public GitHubReleaseClient(HttpClient httpClient, string? token = null)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LANCommander-Recomp-Catalog/0.1");
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        if (!string.IsNullOrWhiteSpace(token))
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<ReleaseInfo> GetLatestAsync(
        CatalogEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (!entry.IsGitHub)
            throw new NotSupportedException("The prototype currently supports GitHub repositories only.");

        var parts = entry.Repository.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            throw new InvalidDataException($"Repository '{entry.Repository}' must use owner/name format.");

        using var response = await _httpClient.GetAsync(
            $"https://api.github.com/repos/{parts[0]}/{parts[1]}/releases/latest",
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(
            stream,
            cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("GitHub returned an empty release.");

        var assets = ReleaseAssetSelection.WindowsPackages(
            release.Assets.Select(asset => new ReleaseAsset(
                asset.Name,
                new Uri(asset.BrowserDownloadUrl),
                asset.Size,
                asset.ContentType ?? "application/octet-stream")),
            entry.ReleaseAssetFilter)
            .Select(asset => new ReleaseAsset(
                asset.Name,
                asset.DownloadUrl,
                asset.Size,
                asset.ContentType))
            .ToList();

        if (assets.Count == 0)
            throw new InvalidDataException(
                $"Release '{release.TagName}' has no selectable Windows ZIP or EXE assets.");

        return new ReleaseInfo(
            release.TagName,
            release.Name ?? release.TagName,
            release.PublishedAt,
            assets);
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
        [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAsset> Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("content_type")] string? ContentType);
}
