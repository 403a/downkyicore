using System.Text.RegularExpressions;

namespace DownKyi.Architecture.Tests;

public sealed class ModuleBoundaryBaselineTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static readonly Dictionary<string, HashSet<string>> KnownDuplicateSimpleNames =
        new(StringComparer.Ordinal)
        {
            ["BangumiType"] =
            [
                "DownKyi.Core.BiliApi.Bangumi.BangumiType",
                "DownKyi.Core.BiliApi.Users.Models.BangumiType"
            ],
            ["Constant"] =
            [
                "DownKyi.Core.BiliApi.BiliUtils.Constant",
                "DownKyi.Core.Storage.Constant"
            ],
            ["FavoritesMedia"] =
            [
                "DownKyi.Core.BiliApi.Favorites.Models.FavoritesMedia",
                "DownKyi.Presentation.FavoritesMedia"
            ],
            ["Subtitle"] =
            [
                "DownKyi.Core.BiliApi.Models.Json.Subtitle",
                "DownKyi.Core.BiliApi.Video.Models.Subtitle",
                "DownKyi.Core.BiliApi.VideoStream.Models.Subtitle",
                "DownKyi.Core.Danmaku2Ass.Subtitle"
            ],
            ["Utils"] =
            [
                "DownKyi.Core.Danmaku2Ass.Utils",
                "DownKyi.Services.Utils"
            ],
            ["VideoInputResolver"] =
            [
                "DownKyi.Application.Media.VideoInputResolver",
                "DownKyi.Services.Video.VideoInputResolver"
            ],
            ["VideoPage"] =
            [
                "DownKyi.Core.BiliApi.Video.Models.VideoPage",
                "DownKyi.Presentation.VideoPage"
            ],
            ["ViewSeasonsSeries"] =
            [
                "DownKyi.Views.UserSpace.ViewSeasonsSeries",
                "DownKyi.Views.ViewSeasonsSeries"
            ],
            ["ViewSeasonsSeriesViewModel"] =
            [
                "DownKyi.ViewModels.UserSpace.ViewSeasonsSeriesViewModel",
                "DownKyi.ViewModels.ViewSeasonsSeriesViewModel"
            ]
        };

    private static readonly HashSet<string> KnownGenericTypeNames = new(StringComparer.Ordinal)
    {
        "DownKyi.Core/BiliApi/BiliUtils/Constant.cs -> Constant",
        "DownKyi.Core/Danmaku2Ass/Utils.cs -> Utils",
        "DownKyi.Core/Storage/Constant.cs -> Constant",
        "DownKyi.Core/Storage/StorageManager.cs -> StorageManager",
        "src/DownKyi.Desktop/Services/Utils.cs -> Utils"
    };

    private static readonly HashSet<string> KnownFileTypeMismatches = new(StringComparer.Ordinal)
    {
        "DownKyi.Core/BiliApi/Users/Models/SpaceSeasonsSeries.cs",
        "DownKyi.Core/BiliApi/Users/Models/SpaceSeriesMeta.cs",
        "DownKyi.Core/Models/NfoModels.cs",
        "src/DownKyi.Desktop/Commands/AsyncDelegateCommand.cs"
    };

    private static readonly Dictionary<string, int> KnownOversizedFiles = new(StringComparer.Ordinal)
    {
        ["DownKyi.Core/Aria2cNet/Client/AriaClient.cs"] = 1137,
        ["DownKyi.Core/BiliApi/BiliUtils/ParseEntrance.cs"] = 586,
        ["DownKyi.Core/Settings/SettingsManager.Network.cs"] = 671,
        ["src/DownKyi.Desktop/CustomControl/CustomPagerViewModel.cs"] = 506,
        ["src/DownKyi.Desktop/Services/Download/AddToDownloadService.cs"] = 667,
        ["src/DownKyi.Desktop/ViewModels/Settings/ViewNetworkViewModel.cs"] = 649,
        ["src/DownKyi.Desktop/ViewModels/Settings/ViewVideoViewModel.cs"] = 1020,
        ["src/DownKyi.Desktop/ViewModels/ViewMyBangumiFollowViewModel.cs"] = 531,
        ["src/DownKyi.Desktop/ViewModels/ViewMySpaceViewModel.cs"] = 669,
        ["src/DownKyi.Desktop/ViewModels/ViewUserSpaceViewModel.cs"] = 569,
        ["src/DownKyi.Desktop/Views/Settings/ViewNetwork.axaml"] = 608,
        ["src/DownKyi.Desktop/Views/ViewVideoDetail.axaml"] = 565,
        ["src/DownKyi.Infrastructure/Downloads/SqliteDownloadTaskStore.cs"] = 928
    };

    [Fact]
    public void CoreHasNoUiOrQrRenderingDependencies()
    {
        var coreRoot = Path.Combine(RepositoryRoot, "DownKyi.Core");
        var actual = Directory
            .EnumerateFiles(coreRoot, "*", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path =>
            {
                var extension = Path.GetExtension(path);
                if (string.Equals(extension, ".axaml", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var source = File.ReadAllText(path);
                return source.Contains("Avalonia", StringComparison.Ordinal) ||
                       source.Contains("QRCoder", StringComparison.Ordinal);
            })
            .Select(Relative)
            .ToArray();

        Assert.Empty(actual);
    }

    [Fact]
    public void ServiceContractsCannotAddPresentationDependencies()
    {
        var servicesRoot = Path.Combine(RepositoryRoot, "src", "DownKyi.Desktop", "Services");
        var actual = Directory
            .EnumerateFiles(servicesRoot, "I*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => File.ReadAllText(path).Contains("DownKyi.ViewModels", StringComparison.Ordinal))
            .Select(Relative)
            .ToArray();

        Assert.Empty(actual);
    }

    [Fact]
    public void DuplicateSimpleNamesCannotGrowBeyondTheKnownBaseline()
    {
        var declarations = ReadTypeDeclarations();
        var duplicateGroups = declarations
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .Select(group => new
            {
                group.Key,
                FullNames = group
                    .Select(item => item.FullName)
                    .Distinct(StringComparer.Ordinal)
                    .ToHashSet(StringComparer.Ordinal)
            })
            .Where(group => group.FullNames.Count > 1)
            .ToArray();
        var violations = new List<string>();

        foreach (var duplicate in duplicateGroups)
        {
            if (!KnownDuplicateSimpleNames.TryGetValue(duplicate.Key, out var knownNames))
            {
                violations.Add($"new duplicate simple name: {duplicate.Key}");
                continue;
            }

            violations.AddRange(duplicate.FullNames
                .Where(fullName => !knownNames.Contains(fullName))
                .Select(fullName => $"{duplicate.Key} gained {fullName}"));
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void GenericTypeNamesCannotGrowBeyondTheKnownBaseline()
    {
        var genericNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Constant",
            "StorageManager",
            "Utils"
        };
        var actual = ReadTypeDeclarations()
            .Where(item => genericNames.Contains(item.Name))
            .Select(item => $"{item.Path} -> {item.Name}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        AssertSubset(actual, KnownGenericTypeNames, "generic type name");
    }

    [Fact]
    public void FileAndPrimaryTypeMismatchesCannotGrowBeyondTheKnownBaseline()
    {
        var actual = EnumerateProductionFiles("*.cs")
            .Where(path => !path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
            .Where(path =>
            {
                var declaredNames = ReadDeclaredTypeNames(File.ReadAllText(path));
                if (declaredNames.Length == 0)
                {
                    return false;
                }

                var fileName = Path.GetFileNameWithoutExtension(path);
                if (fileName.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
                {
                    fileName = fileName[..^".axaml".Length];
                }

                return !declaredNames.Any(typeName =>
                    string.Equals(fileName, typeName, StringComparison.Ordinal) ||
                    fileName.StartsWith($"{typeName}.", StringComparison.Ordinal));
            })
            .Select(Relative)
            .ToArray();

        AssertSubset(actual, KnownFileTypeMismatches, "file/type mismatch");
    }

    [Fact]
    public void OversizedProductionFilesCannotGrowBeyondTheKnownBaseline()
    {
        const int lineThreshold = 500;
        var oversized = EnumerateProductionFiles("*.cs")
            .Concat(EnumerateProductionFiles("*.axaml"))
            .Select(path => new { Path = Relative(path), Lines = File.ReadAllLines(path).Length })
            .Where(item => item.Lines > lineThreshold)
            .ToArray();
        var violations = oversized
            .Where(item => !KnownOversizedFiles.TryGetValue(item.Path, out var maximum) || item.Lines > maximum)
            .Select(item => KnownOversizedFiles.TryGetValue(item.Path, out var maximum)
                ? $"{item.Path}: {item.Lines} lines exceeds baseline {maximum}"
                : $"{item.Path}: new oversized file with {item.Lines} lines")
            .ToArray();

        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void DomainRestoreIsRestrictedToPersistenceAndMigrationAdapters()
    {
        var actual = EnumerateProductionFiles("*.cs")
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("DownloadTask.Restore", StringComparison.Ordinal) ||
                       source.Contains("DomainDownloadTask.Restore", StringComparison.Ordinal);
            })
            .Select(Relative)
            .ToArray();

        Assert.Equal(
            [
                "src/DownKyi.Desktop/Services/Migration/LegacyDownloadTaskMapper.cs",
                "src/DownKyi.Infrastructure/Downloads/SqliteDownloadTaskStore.cs"
            ],
            actual.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void DownloadRuntimeDoesNotPollUiCollectionsForWork()
    {
        var downloadRoot = Path.Combine(RepositoryRoot, "src", "DownKyi.Desktop", "Services", "Download");
        var actual = Directory
            .EnumerateFiles(downloadRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("_downloadLists.Downloading", StringComparison.Ordinal) &&
                       source.Contains("Task.Delay", StringComparison.Ordinal);
            })
            .Select(Relative)
            .ToArray();

        Assert.Empty(actual);
    }

    [Fact]
    public void BilibiliHttpRuntimeCannotRestoreStaticOrSynchronousTransport()
    {
        var apiRoots = new[]
        {
            Path.Combine(RepositoryRoot, "DownKyi.Core", "BiliApi"),
            Path.Combine(RepositoryRoot, "src", "DownKyi.Infrastructure", "Bilibili")
        };
        var markers = new[]
        {
            "static BilibiliHttpClient? _client",
            "_httpClient.Send(",
            "reader.ReadToEnd()",
            "WaitHandle.WaitOne"
        };
        var actual = apiRoots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return markers.Any(marker => source.Contains(marker, StringComparison.Ordinal));
            })
            .Select(Relative)
            .ToArray();

        Assert.Empty(actual);
    }

    [Fact]
    public void DownloadListsExposeOnlyReadOnlyObservableCollections()
    {
        var customCollectionReferences = EnumerateProductionFiles("*.cs")
            .Where(path => File.ReadAllText(path).Contains("ImmutableObservableCollection", StringComparison.Ordinal))
            .Select(Relative)
            .ToArray();

        Assert.Empty(customCollectionReferences);
        Assert.False(File.Exists(Path.Combine(
            RepositoryRoot,
            "src", "DownKyi.Desktop",
            "ViewModels",
            "ImmutableObservableCollection.cs")));

        var stateSource = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src", "DownKyi.Desktop",
            "Services", "Download",
            "DownloadListState.cs"));
        Assert.Contains(
            "ReadOnlyObservableCollection<DownloadingItem> Downloading",
            stateSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "ReadOnlyObservableCollection<DownloadedItem> Downloaded",
            stateSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "private readonly RangeObservableCollection<DownloadingItem> _downloading",
            stateSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "private readonly RangeObservableCollection<DownloadedItem> _downloaded",
            stateSource,
            StringComparison.Ordinal);
    }

    private static TypeDeclaration[] ReadTypeDeclarations()
    {
        return EnumerateProductionFiles("*.cs")
            .SelectMany(path =>
            {
                var source = File.ReadAllText(path);
                var namespaceMatch = Regex.Match(
                    source,
                    @"(?m)^\s*namespace\s+([A-Za-z_][\w\.]*)\s*[;{]",
                    RegexOptions.CultureInvariant,
                    RegexTimeout);
                if (!namespaceMatch.Success)
                {
                    return [];
                }

                var namespaceName = namespaceMatch.Groups[1].Value;
                return ReadDeclaredTypeNames(source)
                    .Select(name => new TypeDeclaration(name, $"{namespaceName}.{name}", Relative(path)))
                    .ToArray();
            })
            .ToArray();
    }

    private static string[] ReadDeclaredTypeNames(string source)
    {
        const string declarationPattern =
            @"(?m)^\s*(?:public|internal|protected|private|file)?\s*" +
            @"(?:sealed\s+|abstract\s+|static\s+|partial\s+)*" +
            @"(?:class|record(?:\s+class|\s+struct)?|struct|interface|enum)\s+" +
            @"([A-Za-z_][\w]*)";
        return Regex.Matches(
                source,
                declarationPattern,
                RegexOptions.CultureInvariant,
                RegexTimeout)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateProductionFiles(string pattern)
    {
        return new[]
            {
                Path.Combine(RepositoryRoot, "DownKyi"),
                Path.Combine(RepositoryRoot, "DownKyi.Core"),
                Path.Combine(RepositoryRoot, "src")
            }
            .SelectMany(root => Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path));
    }

    private static void AssertSubset(
        IEnumerable<string> actual,
        HashSet<string> knownBaseline,
        string description)
    {
        var unexpected = actual
            .Distinct(StringComparer.Ordinal)
            .Where(item => !knownBaseline.Contains(item))
            .Order(StringComparer.Ordinal)
            .Select(item => $"New {description}: {item}")
            .ToArray();

        Assert.True(unexpected.Length == 0, string.Join(Environment.NewLine, unexpected));
    }

    private static bool IsBuildOutput(string path)
    {
        var relative = Path.GetRelativePath(RepositoryRoot, path);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("bin", StringComparer.OrdinalIgnoreCase) ||
               segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    private static string Relative(string path)
    {
        return Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/');
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DownKyi.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the DownKyi repository root.");
    }

    private sealed record TypeDeclaration(string Name, string FullName, string Path);
}
