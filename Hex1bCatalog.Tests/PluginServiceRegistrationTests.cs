using Hex1bCatalog.Core;
using LANCommander.RecompCatalog.Plugin;
using LANCommander.Server.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hex1bCatalog.Tests;

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

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        Assert.NotNull(provider.GetRequiredService<CatalogBrowserService>());
        Assert.NotNull(provider.GetRequiredService<IServerNavigationExtension>());
        Assert.NotNull(provider.GetRequiredService<IServerRouteAssemblyExtension>());
    }
}
