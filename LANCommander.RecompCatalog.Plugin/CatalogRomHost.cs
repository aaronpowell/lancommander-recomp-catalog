using LANCommander.RecompCatalog.Core;
using LANCommander.RecompCatalog.Infrastructure;
using LANCommander.SDK.Enums;
using LANCommander.Server.ImportExport.Services;
using LANCommander.Server.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using DataRedistributable = LANCommander.Server.Data.Models.Redistributable;

namespace LANCommander.RecompCatalog.Plugin;

/// <summary>Host operations for ROM redistributables created by this plugin.</summary>
public interface ICatalogRomHost
{
    Task<IReadOnlyList<RomRedistributableInfo>> ListRomsAsync(CancellationToken cancellationToken = default);

    Task<RomRedistributableInfo> ImportRomAsync(
        NormalizedRom rom,
        string displayName,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    Task<GameRomLink?> GetGameRomAsync(Guid gameId, CancellationToken cancellationToken = default);

    Task LinkAsync(
        Guid gameId,
        Guid romId,
        RomPlacement placement,
        CancellationToken cancellationToken = default);

    Task UnlinkAsync(Guid gameId, CancellationToken cancellationToken = default);
}

public sealed class CatalogRomHost(
    RedistributableService redistributableService,
    GameVersionService gameVersionService,
    IServiceProvider serviceProvider) : ICatalogRomHost
{
    public async Task<IReadOnlyList<RomRedistributableInfo>> ListRomsAsync(
        CancellationToken cancellationToken = default)
    {
        var redistributables = await redistributableService
            .AsNoTracking()
            .GetAsync()
            .WaitAsync(cancellationToken);

        return redistributables
            .Select(Map)
            .OfType<RomRedistributableInfo>()
            .OrderBy(rom => rom.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<RomRedistributableInfo> ImportRomAsync(
        NormalizedRom rom,
        string displayName,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rom);

        var id = RomRedistributableBuilder.CreateId(rom.Sha256);
        var existing = await redistributableService
            .AsNoTracking()
            .FirstOrDefaultAsync(redistributable => redistributable.Id == id)
            .WaitAsync(cancellationToken);
        if (existing is not null)
        {
            progress?.Report("This ROM is already a redistributable; reusing it.");
            return Map(existing)
                ?? throw new InvalidOperationException(
                    $"Redistributable {id} exists but was not created by this plugin.");
        }

        var name = RomRedistributableBuilder.CreateName(displayName, rom.Sha256);
        if (await redistributableService
            .ExistsAsync(redistributable => redistributable.Name == name)
            .WaitAsync(cancellationToken))
        {
            // The importer matches by name as well as ID, so a clash would overwrite another redistributable.
            throw new InvalidOperationException(
                $"A different redistributable is already named '{name}'. Rename it and try again.");
        }

        var packagePath = Path.Combine(
            Path.GetDirectoryName(rom.Path) ?? Path.GetTempPath(),
            $"{Guid.NewGuid():N}.lcx");
        try
        {
            progress?.Report("Building ROM redistributable package...");
            var info = await RomRedistributableBuilder.BuildAsync(rom, displayName, packagePath, cancellationToken);

            progress?.Report("Importing ROM redistributable into LANCommander...");
            await using var packageStream = File.OpenRead(packagePath);
            var importRunner = serviceProvider.GetRequiredService<ImportRunner>();
            await importRunner.RunStreamAsync(
                packageStream,
                manifestType: ManifestType.Redistributable,
                cancellationToken: cancellationToken);

            return info;
        }
        finally
        {
            if (File.Exists(packagePath))
                File.Delete(packagePath);
        }
    }

    public async Task<GameRomLink?> GetGameRomAsync(
        Guid gameId,
        CancellationToken cancellationToken = default)
    {
        var versionId = await gameVersionService.GetLatestIdAsync(gameId).WaitAsync(cancellationToken);
        if (versionId is null)
            return null;

        var rows = await gameVersionService.GetRedistributablesAsync(versionId.Value)
            .WaitAsync(cancellationToken);
        foreach (var row in rows)
        {
            var rom = Map(row.Redistributable);
            if (rom is null)
                continue;

            var placement = RomPlacement.FromOptions(DeserializeOptions(row.Options))
                ?? new RomPlacement("", "");
            return new GameRomLink(rom, placement);
        }

        return null;
    }

    public async Task LinkAsync(
        Guid gameId,
        Guid romId,
        RomPlacement placement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(placement);
        var error = RomPlacement.Validate(placement.TargetFileName, placement.TargetDirectory);
        if (error is not null)
            throw new ArgumentException(error, nameof(placement));

        var rom = await redistributableService
            .AsNoTracking()
            .FirstOrDefaultAsync(redistributable => redistributable.Id == romId)
            .WaitAsync(cancellationToken);
        if (Map(rom) is null)
            throw new InvalidOperationException("The selected ROM redistributable no longer exists.");

        var versionId = await gameVersionService.GetOrCreateLatestIdAsync(gameId).WaitAsync(cancellationToken);
        var rows = await gameVersionService.GetRedistributablesAsync(versionId).WaitAsync(cancellationToken);
        await gameVersionService.SetRedistributablesAsync(
            versionId,
            ReplaceRom(rows.Select(row => (row.RedistributableId, IsPluginRom(row.Redistributable))), romId));
        await gameVersionService.SetRedistributableOptionsAsync(
            versionId,
            romId,
            JsonSerializer.Serialize(placement.ToOptions()));
    }

    public async Task UnlinkAsync(Guid gameId, CancellationToken cancellationToken = default)
    {
        var versionId = await gameVersionService.GetLatestIdAsync(gameId).WaitAsync(cancellationToken);
        if (versionId is null)
            return;

        var rows = await gameVersionService.GetRedistributablesAsync(versionId.Value).WaitAsync(cancellationToken);
        if (!rows.Any(row => IsPluginRom(row.Redistributable)))
            return;

        await gameVersionService.SetRedistributablesAsync(
            versionId.Value,
            ReplaceRom(rows.Select(row => (row.RedistributableId, IsPluginRom(row.Redistributable))), null));
    }

    /// <summary>
    /// Keeps every redistributable that is not a plugin ROM and adds the chosen ROM, so a game has at
    /// most one plugin ROM while redistributables added by hand are preserved.
    /// </summary>
    internal static IReadOnlyList<Guid> ReplaceRom(
        IEnumerable<(Guid Id, bool IsPluginRom)> current,
        Guid? romId)
    {
        var result = current
            .Where(item => !item.IsPluginRom && item.Id != romId)
            .Select(item => item.Id)
            .Distinct()
            .ToList();
        if (romId is { } id)
            result.Add(id);
        return result;
    }

    internal static IReadOnlyDictionary<string, string>? DeserializeOptions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsPluginRom(DataRedistributable? redistributable) => Map(redistributable) is not null;

    private static RomRedistributableInfo? Map(DataRedistributable? redistributable)
    {
        var sha256 = RomRedistributableBuilder.ReadSha256(redistributable?.Notes);
        return redistributable is null || sha256 is null
            ? null
            : new RomRedistributableInfo(redistributable.Id, redistributable.Name, sha256, redistributable.Description);
    }
}
