using System.Text.Json;
using System.Text.RegularExpressions;

namespace DownKyi.Architecture.Tests;

public sealed partial class BilibiliApiInventoryArchitectureTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void EveryHardCodedBilibiliApiEndpointIsRecordedInTheAudit()
    {
        var report = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "docs",
            "operations",
            "bilibili-api-audit.md"));
        var endpoints = Directory
            .EnumerateFiles(
                Path.Combine(RepositoryRoot, "DownKyi.Core", "BiliApi"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}Models{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .SelectMany(ExtractEndpoints)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var missing = endpoints
            .Where(endpoint => !report.Contains($"`{endpoint}`", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"Bilibili API audit is missing source endpoints: {string.Join(", ", missing)}");
    }

    [Fact]
    public void AnonymousNonSuccessCodeExceptionIsScopedToNavigation()
    {
        var usages = Directory
            .EnumerateFiles(
                Path.Combine(RepositoryRoot, "DownKyi.Core"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(
                "RequestJsonAllowingCode<",
                StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "DownKyi.Core/BiliApi/BiliApiRequest.cs",
                "DownKyi.Core/BiliApi/Users/UserInfo.cs"
            ],
            usages);
    }

    [Fact]
    public void OptionalJsonEnvelopeFieldsCannotInventPayloads()
    {
        var violations = new List<string>();
        foreach (var path in Directory.EnumerateFiles(
                     Path.Combine(RepositoryRoot, "DownKyi.Core", "BiliApi"),
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                if (!IsOptionalEnvelopeAttribute(lines[index]))
                {
                    continue;
                }

                var declaration = lines[index];
                if (!declaration.Contains("public ", StringComparison.Ordinal)
                    && index + 1 < lines.Length)
                {
                    declaration += lines[index + 1];
                }

                var propertyEnd = declaration.IndexOf('}', StringComparison.Ordinal);
                if (propertyEnd >= 0)
                {
                    declaration = declaration[..(propertyEnd + 1)];
                }

                if (declaration.Contains("= new", StringComparison.Ordinal)
                    || declaration.Contains("= Array.Empty", StringComparison.Ordinal)
                    || declaration.Contains("= []", StringComparison.Ordinal))
                {
                    violations.Add(
                        $"{Path.GetRelativePath(RepositoryRoot, path)}:{index + 1}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Optional JSON envelopes must preserve missing fields: {string.Join(", ", violations)}");
    }

    [Fact]
    public void LiveProbeIsExplicitAndDoesNotLoadCookies()
    {
        var script = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "script",
            "audit-bilibili-api.ps1"));

        Assert.Contains("[switch]$ConfirmLive", script, StringComparison.Ordinal);
        Assert.Contains("Authentication = 'anonymous", script, StringComparison.Ordinal);
        Assert.DoesNotContain("SESSDATA", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetLoginInfoCookies", script, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthenticatedLiveProbeArtifactIsExplicitAndSanitized()
    {
        var script = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "script",
            "audit-bilibili-authenticated-api.ps1"));
        var artifact = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "docs",
            "operations",
            "bilibili-authenticated-api-audit.json"));

        Assert.Contains("[switch]$ConfirmAuthenticatedLive", script, StringComparison.Ordinal);
        Assert.Contains("$environmentVariableName = 'BILIBILI_TEST_COOKIE'", script, StringComparison.Ordinal);
        Assert.Contains("-EnvironmentVariableLoaded $true", script, StringComparison.Ordinal);
        Assert.DoesNotContain("GetLoginInfoCookies", script, StringComparison.Ordinal);
        Assert.Equal(1, SetContentPattern().Count(script));

        foreach (var sensitiveName in new[]
                 {
                     "BILIBILI_TEST_COOKIE",
                     "SESSDATA",
                     "bili_jct",
                     "DedeUserID",
                     "Cookie",
                     "Request Headers"
                 })
        {
            Assert.DoesNotContain(sensitiveName, artifact, StringComparison.OrdinalIgnoreCase);
        }

        using var document = JsonDocument.Parse(artifact);
        var root = document.RootElement;
        Assert.True(root.GetProperty("EnvironmentVariableLoaded").GetBoolean());
        Assert.True(root.GetProperty("NavigationGatePassed").GetBoolean());
        Assert.Equal(
            [
                "Architecture",
                "CapturedAtUtc",
                "Commit",
                "EnvironmentVariableLoaded",
                "NavigationGatePassed",
                "OperatingSystem",
                "Results",
                "Runtime",
                "SchemaVersion"
            ],
            root.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());

        var results = root.GetProperty("Results").EnumerateArray().ToArray();
        Assert.NotEmpty(results);
        foreach (var result in results)
        {
            Assert.Equal(
                [
                    "BilibiliCode",
                    "ContractDrift",
                    "ErrorType",
                    "HttpStatus",
                    "Name",
                    "Outcome",
                    "Path",
                    "RequiredFieldsPresent",
                    "RequiresLogin",
                    "ResponseStructureMatchesExpected"
                ],
                result.EnumerateObject()
                    .Select(property => property.Name)
                    .Order(StringComparer.Ordinal)
                    .ToArray());
            Assert.StartsWith("/", result.GetProperty("Path").GetString(), StringComparison.Ordinal);
            Assert.DoesNotContain("?", result.GetProperty("Path").GetString(), StringComparison.Ordinal);
            Assert.Equal(200, result.GetProperty("HttpStatus").GetInt32());
            Assert.Equal(0, result.GetProperty("BilibiliCode").GetInt32());
            Assert.True(result.GetProperty("ResponseStructureMatchesExpected").GetBoolean());
            Assert.True(result.GetProperty("RequiredFieldsPresent").GetBoolean());
            Assert.False(result.GetProperty("ContractDrift").GetBoolean());
            Assert.Equal("passed", result.GetProperty("Outcome").GetString());
            Assert.Equal(JsonValueKind.Null, result.GetProperty("ErrorType").ValueKind);
        }

        var navigation = Assert.Single(results, result =>
            string.Equals(
                result.GetProperty("Path").GetString(),
                "/x/web-interface/nav",
                StringComparison.Ordinal));
        Assert.False(navigation.GetProperty("RequiresLogin").GetBoolean());
    }

    private static bool IsOptionalEnvelopeAttribute(string line)
    {
        return line.Contains("[JsonProperty(\"data\")]", StringComparison.Ordinal)
               || line.Contains("[JsonProperty(\"result\")]", StringComparison.Ordinal)
               || line.Contains("[JsonPropertyName(\"data\")]", StringComparison.Ordinal)
               || line.Contains("[JsonPropertyName(\"result\")]", StringComparison.Ordinal);
    }

    private static IEnumerable<string> ExtractEndpoints(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in EndpointPattern().Matches(line))
            {
                yield return $"{match.Groups["host"].Value}{match.Groups["path"].Value}";
            }
        }
    }

    [GeneratedRegex(
        "https://(?<host>(?:api|passport|space)\\.bilibili\\.com)(?<path>/(?:x|pgc|pugv|ajax)/[^\\\"?]*)",
        RegexOptions.CultureInvariant,
        1000)]
    private static partial Regex EndpointPattern();

    [GeneratedRegex("Set-Content", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SetContentPattern();

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
}
