using Hex1bCatalog.Core;
using LANCommander.Server.Services;
using LANCommander.Server.Services.Providers.Metadata;
using System.Text.Json;
using DataGame = LANCommander.Server.Data.Models.Game;
using ManifestGame = LANCommander.SDK.Models.Manifest.Game;

namespace LANCommander.RecompCatalog.Plugin;

public interface ICatalogHostIntegration
{
    Task<IReadOnlyDictionary<string, CatalogEntryStateView>> LoadImportedStatesAsync(
        IReadOnlyList<CatalogBrowserEntry> entries,
        CancellationToken cancellationToken = default);

    IReadOnlyList<string> GetMetadataProviders();

    Task<IReadOnlyList<MetadataLookupResult>> SearchMetadataAsync(
        string providerName,
        string query,
        CancellationToken cancellationToken = default);

    Task<MetadataLookupResult?> GetMetadataAsync(
        string providerName,
        string providerGameId,
        CancellationToken cancellationToken = default);
}

public sealed class CatalogHostIntegration(
    GameService gameService,
    MetadataService metadataService) : ICatalogHostIntegration
{
    internal const string CatalogIdentityField = "RecompCatalog.Identity";
    internal const string ReleaseIdentityField = "RecompCatalog.Release";
    internal const string UpdatePolicyField = "RecompCatalog.UpdatePolicy";

    public async Task<IReadOnlyDictionary<string, CatalogEntryStateView>> LoadImportedStatesAsync(
        IReadOnlyList<CatalogBrowserEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var games = await gameService
            .AsNoTracking()
            .AsSplitQuery()
            .Include(game => game.CustomFields!, game => game.Versions!)
            .GetAsync()
            .WaitAsync(cancellationToken);
        var byCatalogIdentity = games
            .Select(game => new
            {
                Game = game,
                Identity = game.CustomFields?
                    .FirstOrDefault(field => field.Name == CatalogIdentityField)?.Value,
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Identity))
            .GroupBy(item => item.Identity!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Game, StringComparer.Ordinal);
        var byGameId = games.ToDictionary(game => game.Id);
        var states = new Dictionary<string, CatalogEntryStateView>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var legacyGameId = StableId.Create(
                $"game:{entry.CatalogEntry.Repository.ToLowerInvariant()}:{entry.CatalogEntry.Name}");
            if (!byCatalogIdentity.TryGetValue(entry.Identity.Value, out var game)
                && !byGameId.TryGetValue(legacyGameId, out game))
            {
                states[entry.Id] = CatalogEntryStateView.NeverImported();
                continue;
            }

            var importedTag = game.Versions?
                .OrderByDescending(version => version.SortOrder)
                .ThenByDescending(version => version.CreatedOn)
                .Select(version => version.Version)
                .FirstOrDefault(version => !string.IsNullOrWhiteSpace(version))
                ?? "";
            var releaseValue = game.CustomFields?
                .FirstOrDefault(field => field.Name == ReleaseIdentityField)?.Value;
            _ = ReleaseIdentity.TryParse(releaseValue ?? "", out var release);
            var policyValue = game.CustomFields?
                .FirstOrDefault(field => field.Name == UpdatePolicyField)?.Value;
            var policy = DeserializePolicy(policyValue)
                ?? (release is null
                    ? null
                    : new PackageUpdatePolicy(
                        release.RepositoryProvider,
                        release.Repository,
                        entry.CatalogEntry.ReleaseAssetFilter,
                        release.AssetName,
                        "zip-or-exe",
                        true));
            var provenance = new CatalogImportProvenance(
                game.Id,
                game.Title,
                entry.Identity,
                importedTag,
                release,
                policy);

            states[entry.Id] = CatalogEntryStateClassifier.Classify(provenance);
        }

        return states;
    }

    public IReadOnlyList<string> GetMetadataProviders() =>
        metadataService.GetProviderNames()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public async Task<IReadOnlyList<MetadataLookupResult>> SearchMetadataAsync(
        string providerName,
        string query,
        CancellationToken cancellationToken = default)
    {
        var provider = RequireProvider(providerName);
        var results = await provider.SearchGamesAsync(query.Trim(), 10, 0)
            .WaitAsync(cancellationToken);

        return results?.Results
            .Select(result => Map(providerName, result.Id, result.Data))
            .ToList()
            ?? [];
    }

    public async Task<MetadataLookupResult?> GetMetadataAsync(
        string providerName,
        string providerGameId,
        CancellationToken cancellationToken = default)
    {
        var provider = RequireProvider(providerName);
        var game = await provider.GetGameAsync(providerGameId).WaitAsync(cancellationToken);
        return game is null ? null : Map(providerName, providerGameId, game);
    }

    private IMetadataProvider RequireProvider(string providerName) =>
        metadataService.GetProvider(providerName)
        ?? throw new InvalidOperationException(
            $"LANCommander metadata provider '{providerName}' is not available.");

    private static PackageUpdatePolicy? DeserializePolicy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            return JsonSerializer.Deserialize<PackageUpdatePolicy>(value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static MetadataLookupResult Map(
        string provider,
        string providerGameId,
        ManifestGame game) =>
        new(
            provider,
            providerGameId,
            game.Title,
            game.Description,
            game.ReleasedOn == default ? null : game.ReleasedOn,
            game.Singleplayer,
            game.Engine?.Name,
            Names(game.Developers),
            Names(game.Publishers),
            Names(game.Genres),
            Names(game.Tags),
            game.ExternalIds?
                .Where(id => !string.IsNullOrWhiteSpace(id.Provider)
                    && !string.IsNullOrWhiteSpace(id.ExternalId))
                .Select(id => $"{id.Provider}: {id.ExternalId}")
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? []);

    private static IReadOnlyList<string> Names<T>(IEnumerable<T>? values)
        where T : LANCommander.SDK.Models.Manifest.BaseModel =>
        values?
            .Select(value => value switch
            {
                LANCommander.SDK.Models.Manifest.Company company => company.Name,
                LANCommander.SDK.Models.Manifest.Genre genre => genre.Name,
                LANCommander.SDK.Models.Manifest.Tag tag => tag.Name,
                _ => null,
            })
            .OfType<string>()
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? [];
}
