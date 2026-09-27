using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DownKyi.Application.Desktop;
using DownKyi.Core.Settings;

namespace DownKyi.Services;

internal sealed record ParsingSelectorResult(ParseScope Scope);

internal static class ParsingSelectorDialog
{
    private const string ScopeParameter = "parseScope";

    public static IReadOnlyDictionary<string, object?> Encode(ParsingSelectorResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        EnsureExecutableScope(result.Scope);

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ScopeParameter] = result.Scope
        };
    }

    public static async Task<ParsingSelectorResult?> ShowAsync(
        IAppDialogService dialogService,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dialogService);
        var result = await dialogService.ShowAsync(
            new AppDialogRequest(AppDialog.ParsingSelector),
            cancellationToken).ConfigureAwait(true);
        if (result.Outcome != AppDialogOutcome.Accepted)
        {
            return null;
        }

        if (!result.Parameters.TryGetValue(ScopeParameter, out var value)
            || value is not ParseScope scope)
        {
            throw new InvalidOperationException(
                "Accepted ParsingSelector result is missing a valid ParseScope.");
        }

        EnsureExecutableScope(scope);
        return new ParsingSelectorResult(scope);
    }

    private static void EnsureExecutableScope(ParseScope scope)
    {
        if (scope is ParseScope.SelectedItem or ParseScope.CurrentSection or ParseScope.All)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Accepted ParsingSelector result contains a non-executable ParseScope: {scope}.");
    }
}
