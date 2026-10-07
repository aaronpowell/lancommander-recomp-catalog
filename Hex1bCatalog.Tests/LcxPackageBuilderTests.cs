using System.IO.Compression;
using Hex1bCatalog.Core;
using Hex1bCatalog.Infrastructure;
using LANCommander.SDK.Helpers;
using LANCommander.SDK.Models.Manifest;

namespace Hex1bCatalog.Tests;

public class LcxPackageBuilderTests
{
    [Theory]
    [InlineData("..", "Game")]
    [InlineData(@"..\outside", "outside")]
    [InlineData(@"nested/folder", "nestedfolder")]
    [InlineData("CON", "_CON")]
    public void ProducesSafeSingleSegmentDirectoryNames(string value, string expected)
    {
        Assert.Equal(expected, LcxPackageBuilder.SanitizeDirectoryName(value));
    }

    [Fact]
    public async Task BuildsImportableLcxWithStableIdsAndProvenance()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var normalizedPath = Path.Combine(directory, "normalized.zip");
        var outputPath = Path.Combine(directory, "game.lcx");

        try
        {
            using (var normalized = ZipFile.Open(normalizedPath, ZipArchiveMode.Create))
            {
                var executable = normalized.CreateEntry("bin/game.exe");
                await using var content = executable.Open();
                await content.WriteAsync(new byte[] { 1, 2, 3 });
            }

            var entry = new CatalogEntry
            {
                Name = "Test Game",
                Project = "Test Recomp",
                Repository = "owner/repository",
                FolderName = "TestGame-TestRecomp",
            };
            var release = new ReleaseInfo("v1.2.3", "Release", DateTimeOffset.UtcNow, []);
            var asset = new ReleaseAsset(
                "game-win.zip",
                new Uri("https://example.test/game-win.zip"),
                3,
                "application/zip");
            var artifact = new NormalizedArtifact(
                normalizedPath,
                "0123456789abcdef",
                new FileInfo(normalizedPath).Length,
                3,
                ["bin/game.exe"]);
            var catalogIdentity = CatalogIdentity.Create(
                new Uri("https://catalog.test/index.json"),
                "test",
                new Uri("https://catalog.test/list.json"),
                entry);
            var releaseIdentity = ReleaseIdentity.Create(catalogIdentity, release, asset);
            var provenance = new CatalogImportProvenance(
                StableId.Create("game:owner/repository:Test Game"),
                entry.Name,
                catalogIdentity,
                release.Tag,
                releaseIdentity,
                new PackageUpdatePolicy(
                    "github",
                    entry.Repository,
                    null,
                    asset.Name,
                    "zip",
                    true,
                    "bin/game.exe"));

            var result = await new LcxPackageBuilder().BuildAsync(
                new PackageRequest(
                    entry,
                    release,
                    asset,
                    artifact,
                    "bin/game.exe",
                    outputPath,
                    provenance));

            Assert.True(File.Exists(outputPath));
            Assert.Equal(StableId.Create("game:owner/repository:Test Game"), result.GameId);

            using var package = ZipFile.OpenRead(outputPath);
            Assert.NotNull(package.GetEntry($"Archives/{result.ArchiveId}"));
            var manifestEntry = package.GetEntry(ManifestHelper.ManifestFilename);
            Assert.NotNull(manifestEntry);
            await using var manifestStream = manifestEntry.Open();
            using var reader = new StreamReader(manifestStream);
            var manifest = ManifestHelper.Deserialize<Game>(await reader.ReadToEndAsync());

            Assert.Equal(result.GameId, manifest.Id);
            Assert.Equal("Test Game", manifest.Title);
            Assert.Equal("v1.2.3", manifest.Version);
            Assert.Contains("owner/repository", manifest.Notes);
            Assert.Equal("bin\\game.exe", Assert.Single(manifest.Actions).Path);
            Assert.Equal(result.ArchiveId, Assert.Single(manifest.Archives).Id);
            Assert.Contains(
                manifest.CustomFields,
                field => field.Name == "RecompCatalog.Identity"
                    && field.Value == catalogIdentity.Value);
            Assert.Contains(
                manifest.CustomFields,
                field => field.Name == "RecompCatalog.Release"
                    && field.Value == releaseIdentity.Value);
            Assert.Contains(
                manifest.CustomFields,
                field => field.Name == "RecompCatalog.UpdatePolicy"
                    && field.Value.Contains(asset.Name, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
