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
        var directoryName = SanitizeDirectoryName(
            string.IsNullOrWhiteSpace(request.Entry.FolderName)
                ? request.Entry.Name
                : request.Entry.FolderName);

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
        await LCXBuilder.BuildAsync(
            request.OutputPath,
            manifest,
            archive,
            content,
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

    internal static string SanitizeDirectoryName(string value)
    {
        var result = string.Concat(value.Where(
            character => char.IsLetterOrDigit(character) || character is '-' or '_'));
        if (string.IsNullOrWhiteSpace(result))
            result = "Game";
        if (result.Length > 120)
            result = result[..120];

        return WindowsReservedNames.Contains(result) ? $"_{result}" : result;
    }

    private static readonly HashSet<string> WindowsReservedNames = new(
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5",
         "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4",
         "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"],
        StringComparer.OrdinalIgnoreCase);
}
