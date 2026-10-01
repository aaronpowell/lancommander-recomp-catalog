using Hex1bCatalog.Core;

namespace Hex1bCatalog.Infrastructure;

public sealed class PrototypeServices : IDisposable
{
    private readonly HttpClient _httpClient;

    public PrototypeServices(string? githubToken = null)
    {
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        })
        {
            Timeout = TimeSpan.FromMinutes(30),
        };

        Catalogs = new CatalogClient(_httpClient);
        Releases = new GitHubReleaseClient(_httpClient, githubToken);
        Normalizer = new ArtifactNormalizer(_httpClient);
        Packages = new LcxPackageBuilder();
    }

    public ICatalogClient Catalogs { get; }
    public IReleaseClient Releases { get; }
    public IArtifactNormalizer Normalizer { get; }
    public IPackageBuilder Packages { get; }

    public void Dispose() => _httpClient.Dispose();
}
