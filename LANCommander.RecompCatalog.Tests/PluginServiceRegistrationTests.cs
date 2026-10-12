using LANCommander.RecompCatalog.Core;
using LANCommander.RecompCatalog.Plugin;
using LANCommander.Server.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LANCommander.RecompCatalog.Tests;

public class PluginServiceRegistrationTests
{
    [Fact]
    public void BuildsAndResolvesBrowserServiceGraphWithValidationEnabled()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection([])
                .Build());

        new RecompCatalogPlugin().ConfigureServices(services);
        services.RemoveAll<ICatalogHostIntegration>();
        services.AddScoped<ICatalogHostIntegration, StubCatalogHostIntegration>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        Assert.NotNull(provider.GetRequiredService<CatalogBrowserService>());
        Assert.NotNull(provider.GetRequiredService<IServerNavigationExtension>());
        Assert.NotNull(provider.GetRequiredService<IServerRouteAssemblyExtension>());
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICatalogHostIntegration>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<CatalogImportWorkflow>());
        using var otherScope = provider.CreateScope();
        Assert.Same(
            scope.ServiceProvider.GetRequiredService<CatalogImportGate>(),
            otherScope.ServiceProvider.GetRequiredService<CatalogImportGate>());
    }

    private sealed class StubCatalogHostIntegration : ICatalogHostIntegration
    {
        public Task<IReadOnlyDictionary<string, CatalogEntryStateView>> LoadImportedStatesAsync(
            IReadOnlyList<CatalogBrowserEntry> entries,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, CatalogEntryStateView>>(
                new Dictionary<string, CatalogEntryStateView>());

        public IReadOnlyList<string> GetMetadataProviders() => [];

        public Task<IReadOnlyList<MetadataLookupResult>> SearchMetadataAsync(
            string providerName,
            string query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MetadataLookupResult>>([]);

        public Task<MetadataLookupResult?> GetMetadataAsync(
            string providerName,
            string providerGameId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<MetadataLookupResult?>(null);
    }
}
