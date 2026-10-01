using LANCommander.RecompCatalog.Plugin;
using LANCommander.SDK.Plugins;
using LANCommander.Server.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

[assembly: LANCommanderPlugin(
    typeof(RecompCatalogPlugin),
    Id = RecompCatalogPlugin.PluginId,
    Hosts = PluginHost.Server)]

namespace LANCommander.RecompCatalog.Plugin;

/// <summary>
/// Smoke-test entry point. Contributes a single routable page and a navigation entry so the
/// plugin load context, route assembly, and navigation contracts can be verified end to end
/// before any catalog functionality is layered on.
/// </summary>
public sealed class RecompCatalogPlugin : IPlugin
{
    internal const string PluginId = "dev.aaronpowell.lancommander.recompcatalog";

    public string Id => PluginId;
    public string Name => "Recomp Catalog";
    public string Version => "0.1.0";
    public string Author => "Aaron Powell";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IServerRouteAssemblyExtension>(
            new RecompCatalogRouteAssemblyExtension(typeof(RecompCatalogPlugin).Assembly));

        services.AddSingleton<IServerNavigationExtension, RecompCatalogNavigationExtension>();
    }

    public Task InitializeAsync(PluginContext context, CancellationToken cancellationToken)
    {
        context.Logger.LogInformation(
            "{Name} v{Version} initialized on host {Host}; page available at {Route}",
            Name, Version, context.Host, RecompCatalogNavigationExtension.Route);

        return Task.CompletedTask;
    }
}
