using Hex1b;
using Hex1bCatalog.Core;
using Hex1bCatalog.Infrastructure;

const string DefaultFeed =
    "https://raw.githubusercontent.com/tgeorgiadis/quiver-community-app-catalog/main/index.json";

var feed = GetOption(args, "--feed") ?? DefaultFeed;
var outputDirectory = Path.GetFullPath(GetOption(args, "--output") ?? Environment.CurrentDirectory);
var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
Directory.CreateDirectory(outputDirectory);

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

using var services = new PrototypeServices(githubToken);
var temporaryDirectory = Path.Combine(
    Path.GetTempPath(),
    "lancommander-recomp-catalog",
    Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporaryDirectory);

try
{
    if (await TryRunNonInteractiveAsync(
            args, feed, outputDirectory, temporaryDirectory, services, cancellation.Token))
    {
        return;
    }

    var allEntries = new List<CatalogEntry>();
    IReadOnlyList<CatalogEntry> visibleEntries = [];
    CatalogEntry? selectedEntry = null;
    ReleaseInfo? release = null;
    ReleaseAsset? selectedAsset = null;
    NormalizedArtifact? artifact = null;
    string? executable = null;
    var search = "";
    var status = "Loading catalog...";
    var busy = true;
    Hex1bApp? app = null;

    app = new Hex1bApp(ctx => ctx.VStack(v =>
    [
        v.Text("LANCommander Recomp Catalog - LCX Prototype"),
        v.Text("Independent from and not affiliated with or endorsed by Quiver."),
        v.Text("Quiver inspired the workflow; its public feed format/data are compatibility inputs only."),
        v.Text($"Feed: {feed}"),
        v.TextBox(search).OnTextChanged(changed =>
        {
            if (artifact != null && File.Exists(artifact.ZipPath))
                File.Delete(artifact.ZipPath);

            search = changed.NewText;
            visibleEntries = CatalogSearch.Filter(allEntries, search);
            selectedEntry = null;
            release = null;
            selectedAsset = null;
            artifact = null;
            executable = null;
            status = $"{visibleEntries.Count} matching catalog entries.";
        }),
        v.Text(status),
        v.Border(
            selectedEntry == null
                ? v.List(visibleEntries.Select(entry => entry.DisplayName).ToList())
                    .OnItemActivated(async activated =>
                    {
                        selectedEntry = visibleEntries[activated.ActivatedIndex];
                        busy = true;
                        status = $"Resolving latest release for {selectedEntry.DisplayName}...";
                        app!.Invalidate();

                        try
                        {
                            release = await services.Releases.GetLatestAsync(
                                selectedEntry, cancellation.Token);
                            status = $"Release {release.Tag}: select a Windows ZIP or EXE asset.";
                        }
                        catch (Exception ex)
                        {
                            status = $"Release lookup failed: {ex.Message}";
                            selectedEntry = null;
                        }
                        finally
                        {
                            busy = false;
                            app!.Invalidate();
                        }
                    })
                : artifact == null
                    ? v.List(
                            release?.Assets.Select(asset =>
                                $"{asset.Name} ({asset.Size:N0} bytes)").ToList() ?? [])
                        .OnItemActivated(async activated =>
                        {
                            selectedAsset = release!.Assets[activated.ActivatedIndex];
                            busy = true;
                            status = $"Downloading and normalizing {selectedAsset.Name}...";
                            app!.Invalidate();

                            try
                            {
                                artifact = await services.Normalizer.DownloadAndNormalizeAsync(
                                    selectedEntry,
                                    selectedAsset,
                                    temporaryDirectory,
                                    new Progress<string>(message =>
                                    {
                                        status = message;
                                        app!.Invalidate();
                                    }),
                                    cancellation.Token);
                                status = $"SHA-256 {artifact.Sha256}. Select the primary executable.";
                            }
                            catch (Exception ex)
                            {
                                status = $"Artifact preparation failed: {ex.Message}";
                            }
                            finally
                            {
                                busy = false;
                                app!.Invalidate();
                            }
                        })
                    : v.List(artifact.Executables)
                        .OnItemActivated(activated =>
                        {
                            executable = artifact.Executables[activated.ActivatedIndex];
                            status = $"Selected {executable}. Review the preview and build.";
                        }))
            .Title(selectedEntry?.DisplayName ?? "Search and browse catalog"),
        v.Border(
            v.VStack(preview =>
            [
                preview.Text($"Title: {selectedEntry?.Name ?? "-"}"),
                preview.Text($"Version: {release?.Tag ?? "-"}"),
                preview.Text($"Directory: {selectedEntry?.FolderName ?? "-"}"),
                preview.Text($"Action: {selectedEntry?.Project ?? "Play"} -> {executable ?? "-"}"),
                preview.Text($"Output: {GetOutputPath(outputDirectory, selectedEntry, release)}"),
            ]))
            .Title("LCX preview"),
        v.Button("Build LCX").OnClick(async _ =>
        {
            if (busy || selectedEntry == null || release == null || selectedAsset == null
                || artifact == null || executable == null)
            {
                status = "Select a catalog entry, release asset, and executable first.";
                app!.Invalidate();
                return;
            }

            busy = true;
            var outputPath = GetOutputPath(outputDirectory, selectedEntry, release);
            status = $"Building {outputPath}...";
            app!.Invalidate();

            try
            {
                var result = await services.Packages.BuildAsync(
                    new PackageRequest(
                        selectedEntry,
                        release,
                        selectedAsset,
                        artifact,
                        executable,
                        outputPath),
                    new Progress<string>(message =>
                    {
                        status = message;
                        app!.Invalidate();
                    }),
                    cancellation.Token);
                status = $"Created {result.OutputPath} (game {result.GameId}).";
            }
            catch (Exception ex)
            {
                status = $"LCX build failed: {ex.Message}";
            }
            finally
            {
                busy = false;
                app!.Invalidate();
            }
        }),
        v.Button("Quit").OnClick(clicked => clicked.Context.RequestStop()),
        v.InfoBar(["Type to search", "Enter: select", "Tab: next control", "Ctrl+C: cancel/quit"]),
    ]));

    try
    {
        var sources = await services.Catalogs.LoadAsync(new Uri(feed), cancellation.Token);
        allEntries.AddRange(sources
            .SelectMany(source => source.Entries)
            .Where(entry => entry.IsGitHub));
        visibleEntries = CatalogSearch.Filter(allEntries, search);
        status = $"Loaded {visibleEntries.Count} GitHub catalog entries. Search or select one.";
    }
    catch (Exception ex)
    {
        status = $"Catalog load failed: {ex.Message}";
    }
    finally
    {
        busy = false;
        app.Invalidate();
    }

    await app.RunAsync(cancellation.Token);
}
finally
{
    if (Directory.Exists(temporaryDirectory))
        Directory.Delete(temporaryDirectory, recursive: true);
}

static async Task<bool> TryRunNonInteractiveAsync(
    string[] arguments,
    string feed,
    string outputDirectory,
    string temporaryDirectory,
    PrototypeServices services,
    CancellationToken cancellationToken)
{
    var inspectRepository = GetOption(arguments, "--inspect");
    if (arguments.Contains("--list-only", StringComparer.OrdinalIgnoreCase)
        || inspectRepository != null)
    {
        var catalogSources = await services.Catalogs.LoadAsync(new Uri(feed), cancellationToken);
        if (inspectRepository == null)
        {
            foreach (var source in catalogSources)
                Console.WriteLine($"{source.Name} {source.Version}: {source.Entries.Count} entries");
        }
        else
        {
            var catalogEntry = catalogSources
                .SelectMany(source => source.Entries)
                .FirstOrDefault(entry => entry.Repository.Equals(
                    inspectRepository,
                    StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Repository '{inspectRepository}' was not found in the feed.");
            var latestRelease = await services.Releases.GetLatestAsync(catalogEntry, cancellationToken);
            Console.WriteLine($"{catalogEntry.DisplayName}: {latestRelease.Tag}");
            foreach (var releaseAsset in latestRelease.Assets)
                Console.WriteLine($"- {releaseAsset.Name} ({releaseAsset.Size:N0} bytes)");
        }

        return true;
    }

    var exerciseArtifact = GetOption(arguments, "--exercise");
    if (exerciseArtifact == null)
        return false;

    var selectedExecutable = GetOption(arguments, "--executable")
        ?? throw new ArgumentException("--exercise requires --executable <relative-path>.");
    var entry = new CatalogEntry
    {
        Name = "Pipeline Exercise",
        Project = "Fixture",
        Repository = "local/pipeline-exercise",
        FolderName = "PipelineExercise",
    };
    var artifact = await ArtifactNormalizer.NormalizeLocalAsync(
        entry,
        Path.GetFullPath(exerciseArtifact),
        temporaryDirectory,
        cancellationToken);
    var release = new ReleaseInfo("exercise", "Exercise", DateTimeOffset.UtcNow, []);
    var sourceInfo = new FileInfo(exerciseArtifact);
    var asset = new ReleaseAsset(
        sourceInfo.Name,
        new Uri("https://example.invalid/" + Uri.EscapeDataString(sourceInfo.Name)),
        sourceInfo.Length,
        "application/octet-stream");
    var outputPath = Path.Combine(outputDirectory, "pipeline-exercise.lcx");

    var result = await services.Packages.BuildAsync(
        new PackageRequest(entry, release, asset, artifact, selectedExecutable, outputPath),
        new Progress<string>(Console.WriteLine),
        cancellationToken);
    Console.WriteLine($"Created {result.OutputPath}");
    Console.WriteLine($"Source SHA-256: {result.Sha256}");
    return true;
}

static string? GetOption(string[] arguments, string name)
{
    var index = Array.FindIndex(arguments, argument =>
        argument.Equals(name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
}

static string GetOutputPath(
    string outputDirectory,
    CatalogEntry? entry,
    ReleaseInfo? release)
{
    if (entry == null || release == null)
        return "-";

    return Path.Combine(
        outputDirectory,
        $"{SafeFileName(entry.Name)}-{SafeFileName(release.Tag)}.lcx");
}

static string SafeFileName(string value)
{
    var invalid = Path.GetInvalidFileNameChars();
    return string.Concat(value.Select(character => invalid.Contains(character) ? '-' : character));
}
