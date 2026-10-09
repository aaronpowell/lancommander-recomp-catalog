using Hex1bCatalog.Core;
using Hex1bCatalog.Infrastructure;
using LANCommander.RecompCatalog.Plugin;
using LANCommander.SDK.Plugins;
using LANCommander.Server.Plugins;
using LANCommander.Server.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

[assembly: LANCommanderPlugin(
    typeof(RecompCatalogPlugin),
    Id = RecompCatalogPlugin.PluginId,
    Hosts = PluginHost.Server)]

namespace LANCommander.RecompCatalog.Plugin;

/// <summary>
/// Server plugin entry point for browsing compatible recompilation catalogs and previewing
/// GitHub release assets before a future import workflow is confirmed.
/// </summary>
public sealed class RecompCatalogPlugin : IPlugin
{
    internal const string PluginId = "dev.aaronpowell.lancommander.recompcatalog";

    public string Id => PluginId;
    public string Name => "Recomp Catalog";
    public string Version => "0.4.0";
    public string Author => "Aaron Powell";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IServerRouteAssemblyExtension>(
            new RecompCatalogRouteAssemblyExtension(typeof(RecompCatalogPlugin).Assembly));

        services.AddSingleton<IServerNavigationExtension>(
            new RecompCatalogNavigationExtension());

        services.AddSingleton(serviceProvider =>
            RecompCatalogSettings.FromConfiguration(
                serviceProvider.GetRequiredService<IConfiguration>()));
        services.AddSingleton<RecompCatalogHttpClient>();
        services.AddSingleton<ICatalogClient>(serviceProvider =>
            new CatalogClient(
                serviceProvider.GetRequiredService<RecompCatalogHttpClient>().Client));
        services.AddSingleton<IReleaseClient>(serviceProvider =>
            new GitHubReleaseClient(
                serviceProvider.GetRequiredService<RecompCatalogHttpClient>().Client,
                GitHubAuthentication.FromEnvironment().Token));
        services.AddSingleton<CatalogBrowserService>();
        services.AddScoped<CatalogImportWorkflow>();
        services.AddScoped<ICatalogHostIntegration>(serviceProvider =>
            new CatalogHostIntegration(
                serviceProvider.GetRequiredService<GameService>(),
                serviceProvider.GetRequiredService<MetadataService>()));
    }

    public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken)
    {
        context.Logger.LogInformation(
            "{Name} v{Version} initialized on host {Host}; page available at {Route}",
            Name, Version, context.Host, RecompCatalogNavigationExtension.Route);

        return Task.CompletedTask;
    }
}
