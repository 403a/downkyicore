using System.Text.Json;

namespace DownKyi.Architecture.Tests;

public sealed class AssemblyLifecycleArchitectureTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void EveryTestAssemblyUsesFixtureOwnedDataIsolation()
    {
        var testProps = Read("tests/Directory.Build.props");
        var registration = Read("tests/TestInfrastructure/TestDataIsolationRegistration.cs");
        var fixture = Read("tests/TestInfrastructure/TestDataIsolation.cs");

        Assert.Contains("DownKyi.TestInfrastructure.csproj", testProps, StringComparison.Ordinal);
        Assert.Contains("TestDataIsolationRegistration.cs", testProps, StringComparison.Ordinal);
        Assert.Contains("Xunit.AssemblyFixture", registration, StringComparison.Ordinal);
        Assert.Contains("IAsyncDisposable", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain(
            string.Concat("Module", "Initializer"),
            fixture,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            string.Concat("Process", "Exit"),
            fixture,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LifecycleGateMeasuresEveryProcessStageAndCapturesForensics()
    {
        var source = Read("script/test-assembly-lifecycle.ps1");
        string[] requiredTokens =
        [
            "\"load\"",
            "\"assembly-info\"",
            "\"discovery\"",
            "\"execution\"",
            "\"assembly-teardown\"",
            "\"process-exit\"",
            "P50",
            "P95",
            "P99",
            "WaitReason",
            "Get-ProcessTree",
            "dotnet-stack",
            "managed-stack.txt",
            "DOWNKYI_LIFECYCLE_MARKER",
            "stdoutPolluted",
            "stderrPolluted",
            "residualChildCount",
            "workingTreeDirty",
            "slowEvidenceStatus",
            "slowEvidenceComplete",
            "slowEvidenceMissingCount",
            "diagnosticCaptureDurationMs",
            "processExitedAtUnixMs",
            "ValidateForensics"
        ];

        foreach (var token in requiredTokens)
        {
            Assert.Contains(token, source, StringComparison.Ordinal);
        }

        Assert.Contains("-Phase \"execution\"", source, StringComparison.Ordinal);
        Assert.Contains("-LifecycleMarkerPath $selfTestMarker", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "[string]::IsNullOrWhiteSpace($LifecycleMarkerPath) -and",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LifecycleProfilesAreRequiredByPrMainAndReleaseWorkflows()
    {
        var quality = Read(".github/workflows/quality.yml");
        var release = Read(".github/workflows/build.yml");

        Assert.Contains("assembly-lifecycle:", quality, StringComparison.Ordinal);
        Assert.Contains("\"PR\"", quality, StringComparison.Ordinal);
        Assert.Contains("\"Main\"", quality, StringComparison.Ordinal);
        Assert.Contains("-ValidateForensics", quality, StringComparison.Ordinal);
        Assert.Contains("dotnet-stack", quality, StringComparison.Ordinal);

        Assert.Contains("assembly-lifecycle-release:", release, StringComparison.Ordinal);
        Assert.Contains("-Profile Rehearsal", release, StringComparison.Ordinal);
        Assert.Contains("-ValidateForensics", release, StringComparison.Ordinal);
        Assert.Contains("assembly-lifecycle-release", release, StringComparison.Ordinal);
    }

    [Fact]
    public void FormalVerificationCannotOmitLifecycleOwnershipOrRepeatedProcessChecks()
    {
        var livePlan = Read("docs/refactoring-live-plan.md");
        var operations = Read("docs/operations/verification-and-rollback.md");
        var releaseGate = Read("docs/product-specs/v1.1.0-release-gate.md");

        Assert.Contains("audit-lifecycle-ownership.ps1", livePlan, StringComparison.Ordinal);
        Assert.Contains("-Iterations 5", livePlan, StringComparison.Ordinal);
        Assert.Contains("-ValidateForensics", livePlan, StringComparison.Ordinal);
        Assert.Contains("Gate 10 is complete only", livePlan, StringComparison.Ordinal);

        Assert.Contains("-Iterations 5", operations, StringComparison.Ordinal);
        Assert.Contains("-Profile Rehearsal", operations, StringComparison.Ordinal);
        Assert.Contains(
            "-ResultsDirectory ./artifacts/assembly-lifecycle/release",
            operations,
            StringComparison.Ordinal);

        Assert.Contains("100", releaseGate, StringComparison.Ordinal);
        Assert.Contains("machine-readable report", releaseGate, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopMainLoopAwaitsApplicationAndHostTeardown()
    {
        var desktopApplication = Read("src/DownKyi.Desktop/DesktopApplication.cs");
        var app = Read("src/DownKyi.Desktop/App.axaml.cs");
        const string desktopPrefix = "desktop.";
        const string legacyExitSubscription = "Exit += OnExit";

        Assert.Contains("finally", desktopApplication, StringComparison.Ordinal);
        Assert.Contains("await application.DisposeAsync()", desktopApplication, StringComparison.Ordinal);
        Assert.Contains("IAsyncDisposable", app, StringComparison.Ordinal);
        Assert.Contains("RequestShutdownAsync(CancellationToken.None)", app, StringComparison.Ordinal);
        Assert.Contains("DetachUnhandledExceptionLogging()", app, StringComparison.Ordinal);
        Assert.Contains(
            "Dispatcher.UIThread.UnhandledException -= OnUiUnhandledException",
            app,
            StringComparison.Ordinal);
        Assert.Contains(
            "AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException",
            app,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            desktopPrefix + legacyExitSubscription,
            app,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LifecycleOwnerPolicyDefinesActionableTeardownContracts()
    {
        var policyPath = Path.Combine(
            RepositoryRoot,
            "docs",
            "testing",
            "assembly-lifecycle-owners.json");
        using var document = JsonDocument.Parse(File.ReadAllText(policyPath));
        var owners = document.RootElement.GetProperty("owners").EnumerateArray().ToArray();

        Assert.NotEmpty(owners);
        foreach (var owner in owners)
        {
            Assert.False(string.IsNullOrWhiteSpace(owner.GetProperty("id").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(owner.GetProperty("owner").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(owner.GetProperty("start").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(owner.GetProperty("stop").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(owner.GetProperty("teardown").GetString()));
            Assert.NotEmpty(owner.GetProperty("paths").EnumerateArray());
            Assert.NotEmpty(owner.GetProperty("allowedMechanisms").EnumerateArray());
        }
    }

    private static string Read(string relativePath)
    {
        return File.ReadAllText(Path.Combine(
            RepositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
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

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
