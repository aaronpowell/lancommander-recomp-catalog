using Hex1bCatalog.Core;
using LANCommander.Packaging.LCX;
using LANCommander.SDK.Enums;
using LANCommander.SDK.Models.Manifest;
using ManifestArchive = LANCommander.SDK.Models.Manifest.Archive;
using ManifestAction = LANCommander.SDK.Models.Manifest.Action;

namespace Hex1bCatalog.Infrastructure;

public sealed class LcxPackageBuilder : IPackageBuilder
{
    public async Task<PackageResult> BuildAsync(
        PackageRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var selectedExecutable = ExecutableSelection.RequireExplicit(
            request.Artifact.Executables,
            request.Executable);
        var repositoryKey = request.Entry.Repository.ToLowerInvariant();
        var gameId = StableId.Create($"game:{repositoryKey}:{request.Entry.Name}");
        var archiveId = StableId.Create(
            $"archive:{repositoryKey}:{request.Release.Tag}:{request.Asset.Name}:{request.Artifact.Sha256}");
        var directoryName = string.IsNullOrWhiteSpace(request.Entry.FolderName)
            ? SanitizeDirectoryName(request.Entry.Name)
            : request.Entry.FolderName;

        var manifest = new Game
        {
            Id = gameId,
            Title = request.Entry.Name,
            SortTitle = request.Entry.Name,
            Version = request.Release.Tag,
            DirectoryName = directoryName,
            Description = BuildDescription(request),
            Notes = $"Source: https://github.com/{request.Entry.Repository}\n"
                + $"Release asset: {request.Asset.Name}\nSHA-256: {request.Artifact.Sha256}",
            Singleplayer = true,
            Type = GameType.MainGame,
            Actions =
            [
                new ManifestAction
                {
                    Name = "Play",
                    Path = selectedExecutable.Replace('/', Path.DirectorySeparatorChar),
                    WorkingDirectory = Path.GetDirectoryName(
                        selectedExecutable.Replace('/', Path.DirectorySeparatorChar)) ?? "",
                    IsPrimaryAction = true,
                    SortOrder = 0,
                    Platforms = RuntimePlatform.Windows,
                },
            ],
        };

        var archive = new ManifestArchive
        {
            Id = archiveId,
            Version = request.Release.Tag,
            Changelog = $"Imported from {request.Entry.Repository} release {request.Release.Tag}.",
            UncompressedSize = request.Artifact.UncompressedSize,
        };

        await using var content = File.OpenRead(request.Artifact.ZipPath);
        await LCXPackageWriter.WriteAsync(
            request.OutputPath,
            manifest,
            [new LCXArchiveContent(archive, content)],
            scripts: null,
            createdBy: "LANCommander Recomp Catalog Prototype",
            progress,
            cancellationToken);

        return new PackageResult(gameId, archiveId, request.OutputPath, request.Artifact.Sha256);
    }

    private static string BuildDescription(PackageRequest request)
    {
        var project = string.IsNullOrWhiteSpace(request.Entry.Project)
            ? request.Entry.Name
            : request.Entry.Project;
        return $"{project}, imported from GitHub release {request.Release.Tag}. "
            + "Catalog metadata uses a Quiver-compatible feed; this project is independent of Quiver.";
    }

    private static string SanitizeDirectoryName(string value) =>
        string.Concat(value.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_'));
}
