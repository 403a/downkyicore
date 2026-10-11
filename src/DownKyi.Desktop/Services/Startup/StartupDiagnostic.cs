using System;
using System.Collections.Generic;
using System.Linq;
using DownKyi.Services.Download;

namespace DownKyi.Services.Startup;

internal enum StartupDiagnosticSource
{
    LegacyMigration,
    DownloadBootstrap
}

internal enum StartupDiagnosticKind
{
    LegacyMigrationFailed,
    DownloadRuntimeMissing,
    DownloadStoreSchemaMismatch,
    DownloadRuntimeFailed
}

internal enum StartupRecoveryAction
{
    None,
    ResetDownloadStore
}

internal sealed record StartupDiagnostic(
    StartupDiagnosticSource Source,
    StartupDiagnosticKind Kind,
    string TitleResourceKey,
    string DescriptionResourceKey,
    string ImpactResourceKey,
    Exception Failure,
    StartupRecoveryAction RecoveryAction = StartupRecoveryAction.None,
    string? Context = null)
{
    public static StartupDiagnostic ForLegacyMigration(
        Exception failure,
        string? context = null)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new StartupDiagnostic(
            StartupDiagnosticSource.LegacyMigration,
            StartupDiagnosticKind.LegacyMigrationFailed,
            "StartupLegacyMigrationFailureTitle",
            "StartupLegacyMigrationFailureDescription",
            "StartupLegacyMigrationFailureImpact",
            failure,
            Context: context);
    }

    public static StartupDiagnostic ForDownloadBootstrap(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        if (DownloadStoreRecoveryPresenter.CanReset(failure))
        {
            return new StartupDiagnostic(
                StartupDiagnosticSource.DownloadBootstrap,
                StartupDiagnosticKind.DownloadStoreSchemaMismatch,
                "StartupDownloadStoreFailureTitle",
                "StartupDownloadStoreFailureDescription",
                "StartupDownloadStoreFailureImpact",
                failure,
                StartupRecoveryAction.ResetDownloadStore);
        }

        var missingRuntime = Contains<FileNotFoundException>(failure);
        return new StartupDiagnostic(
            StartupDiagnosticSource.DownloadBootstrap,
            missingRuntime
                ? StartupDiagnosticKind.DownloadRuntimeMissing
                : StartupDiagnosticKind.DownloadRuntimeFailed,
            "StartupDownloadFailureTitle",
            missingRuntime
                ? "StartupDownloadMissingDescription"
                : "StartupDownloadFailureDescription",
            "StartupDownloadFailureImpact",
            failure);
    }

    private static bool Contains<TException>(Exception failure)
        where TException : Exception
    {
        if (failure is TException)
        {
            return true;
        }

        if (failure is AggregateException aggregate
            && aggregate.InnerExceptions.Any(Contains<TException>))
        {
            return true;
        }

        return failure.InnerException != null && Contains<TException>(failure.InnerException);
    }
}

internal sealed class StartupDiagnosticCollection
{
    private readonly List<StartupDiagnostic> _items = [];

    public IReadOnlyList<StartupDiagnostic> Items => _items;

    public void Add(StartupDiagnostic? diagnostic)
    {
        if (diagnostic is null)
        {
            return;
        }

        if (_items.Any(existing => RepresentsSameFailure(existing, diagnostic)))
        {
            return;
        }

        _items.Add(diagnostic);
    }

    private static bool RepresentsSameFailure(
        StartupDiagnostic first,
        StartupDiagnostic second)
    {
        if (first.Source != second.Source)
        {
            return false;
        }

        if (ReferenceEquals(first.Failure, second.Failure))
        {
            return true;
        }

        var firstLeaves = EnumerateLeaves(first.Failure).ToArray();
        return EnumerateLeaves(second.Failure)
            .Any(secondLeaf => firstLeaves.Any(firstLeaf => ReferenceEquals(firstLeaf, secondLeaf)));
    }

    private static IEnumerable<Exception> EnumerateLeaves(Exception failure)
    {
        if (failure is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                foreach (var leaf in EnumerateLeaves(inner))
                {
                    yield return leaf;
                }
            }

            yield break;
        }

        if (failure.InnerException is { } innerException)
        {
            foreach (var leaf in EnumerateLeaves(innerException))
            {
                yield return leaf;
            }

            yield break;
        }

        yield return failure;
    }
}
