using Hex1bCatalog.Core;

namespace Hex1bCatalog.Tests;

public class CatalogBrowserTests
{
    [Fact]
    public async Task MapsFeedEntriesAndFacets()
    {
        var service = CreateService(
            [
                Source("Nintendo",
                    Entry("Zelda", "org/zelda", ["recomp", "n64", "windows"])),
                Source("PlayStation",
                    Entry("Crash", "org/crash", ["recreation", "ps1", "linux"])),
            ]);

        var result = await service.LoadAsync("https://catalog.test/index.json");

        Assert.Equal(CatalogBrowserStatus.Ready, result.Status);
        Assert.Equal(["Nintendo", "PlayStation"], result.OriginalPlatforms);
        Assert.Equal(["Linux", "Windows"], result.TargetPlatforms);
        Assert.Equal(["linux", "n64", "ps1", "recomp", "recreation", "windows"], result.Tags);

        var zelda = Assert.Single(result.Entries, entry => entry.DisplayName == "Zelda");
        Assert.Equal("Nintendo", zelda.OriginalPlatform);
        Assert.Equal(["Windows"], zelda.TargetPlatforms);
        Assert.Equal(new Uri("https://github.com/org/zelda"), zelda.RepositoryUrl);
    }

    [Fact]
    public async Task FiltersDeterministicallyAcrossSearchAndFacets()
    {
        var service = CreateService(
            [
                Source("Nintendo",
                    Entry("Zelda", "org/zelda", ["recomp", "n64", "windows"]),
                    Entry("Banjo", "org/banjo", ["recomp", "n64", "windows"])),
                Source("PlayStation",
                    Entry("Zelda Tool", "org/tool", ["tool", "ps1", "windows"])),
            ]);
        var loaded = await service.LoadAsync("https://catalog.test/index.json");

        var results = CatalogBrowserService.ApplyFilter(
            loaded.Entries,
            new CatalogBrowserFilter(
                Query: "zelda",
                Tag: "recomp",
                OriginalPlatform: "Nintendo",
                TargetPlatform: "Windows"));

        Assert.Equal("Zelda", Assert.Single(results).DisplayName);
    }

    [Fact]
    public async Task ReportsUnconfiguredEmptyAndFailedFeeds()
    {
        var unconfigured = await CreateService([]).LoadAsync(null);
        var empty = await CreateService([]).LoadAsync("https://catalog.test/index.json");
        var failed = await new CatalogBrowserService(
            new ThrowingCatalogClient(),
            new StubReleaseClient())
            .LoadAsync("https://catalog.test/index.json");

        Assert.Equal(CatalogBrowserStatus.Unconfigured, unconfigured.Status);
        Assert.Equal(CatalogBrowserStatus.Empty, empty.Status);
        Assert.Equal(CatalogBrowserStatus.Failed, failed.Status);
        Assert.Equal("Feed unavailable.", failed.ErrorMessage);
    }

    [Fact]
    public async Task ResolvesReleaseMetadataWithoutDownloadingAssets()
    {
        var entry = Entry("Zelda", "org/zelda", ["recomp"]);
        var browserEntry = new CatalogBrowserEntry(
            "source:org/zelda:Zelda",
            entry,
            "source",
            "Nintendo",
            new Uri("https://catalog.test/nintendo.json"),
            "Nintendo",
            [],
            new Uri("https://github.com/org/zelda"));
        var release = new ReleaseInfo(
            "v1.2.3",
            "Version 1.2.3",
            DateTimeOffset.Parse("2026-10-01T00:00:00Z"),
            [new ReleaseAsset("zelda-win.zip", new Uri("https://downloads.test/zelda.zip"), 42, "application/zip")]);
        var service = new CatalogBrowserService(
            new StubCatalogClient([]),
            new StubReleaseClient(release));

        var preview = await service.PreviewImportAsync(browserEntry);

        Assert.Equal(ImportPreviewStatus.Ready, preview.Status);
        var previewRelease = Assert.IsType<ReleaseInfo>(preview.Release);
        Assert.Same(release, previewRelease);
        Assert.Single(previewRelease.Assets);
    }

    private static CatalogBrowserService CreateService(IReadOnlyList<CatalogSource> sources) =>
        new(new StubCatalogClient(sources), new StubReleaseClient());

    private static CatalogSource Source(string name, params CatalogEntry[] entries) =>
        new(
            name.ToLowerInvariant(),
            new Uri($"https://catalog.test/{name.ToLowerInvariant()}.json"),
            name,
            $"{name} entries",
            "1.0.0",
            entries);

    private static CatalogEntry Entry(
        string name,
        string repository,
        IReadOnlyList<string> tags) =>
        new()
        {
            Name = name,
            Repository = repository,
            FolderName = name.Replace(" ", "", StringComparison.Ordinal),
            Tags = tags,
        };

    private sealed class StubCatalogClient(IReadOnlyList<CatalogSource> sources) : ICatalogClient
    {
        public Task<IReadOnlyList<CatalogSource>> LoadAsync(
            Uri indexLocation,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(sources);
    }

    private sealed class ThrowingCatalogClient : ICatalogClient
    {
        public Task<IReadOnlyList<CatalogSource>> LoadAsync(
            Uri indexLocation,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("Feed unavailable.");
    }

    private sealed class StubReleaseClient(ReleaseInfo? release = null) : IReleaseClient
    {
        public Task<ReleaseInfo> GetLatestAsync(
            CatalogEntry entry,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(release ?? new ReleaseInfo("v1", "v1", null, []));
    }
}
