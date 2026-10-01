using System.Reflection;
using LANCommander.SDK;
using LANCommander.Server.Plugins;

namespace LANCommander.RecompCatalog.Plugin;

/// <summary>Exposes this assembly's routable Razor components to the server's router.</summary>
internal sealed record RecompCatalogRouteAssemblyExtension(Assembly Assembly) : IServerRouteAssemblyExtension;

/// <summary>Adds the plugin's page to the server's primary navigation.</summary>
internal sealed class RecompCatalogNavigationExtension : IServerNavigationExtension
{
    internal const string Route = "/Plugins/RecompCatalog";

    public string Id => RecompCatalogPlugin.PluginId;
    public string Label => "Recomp Catalog";
    public string Href => Route;
    public string? Icon => "Download";
    public int Order => 100;

    // Visibility only. The page enforces the same role independently via [Authorize].
    public string? RequiredRole => Roles.Administrator;
}
