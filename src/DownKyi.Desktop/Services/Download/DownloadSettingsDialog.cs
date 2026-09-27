using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using DownKyi.Application.Desktop;
using DownKyi.Domain.Downloads;

namespace DownKyi.Services.Download;

internal static class DownloadSettingsDialog
{
    private const string SubtitleTracksParameter = "subtitleTracks", ResultParameter = "result";
    internal sealed record SubtitleTrack(long TrackId, string Language, string DisplayLanguage, int Type, string Url);
    public static AppDialogRequest CreateRequest(IReadOnlyList<SubtitleTrack> subtitleTracks) =>
        new(AppDialog.DownloadSettings, new Dictionary<string, object?>(StringComparer.Ordinal)
        { [SubtitleTracksParameter] = subtitleTracks });
    public static IReadOnlyList<SubtitleTrack> ReadSubtitleTracks(AppDialogRequest request) =>
        request.Parameters?.TryGetValue(SubtitleTracksParameter, out var value) == true &&
        value is IReadOnlyList<SubtitleTrack> tracks
            ? tracks
            : throw new InvalidOperationException(
                "DownloadSettings request is missing its subtitle tracks.");
    public static IReadOnlyDictionary<string, object?> EncodeResult(
        string directory,
        DownloadContentSelection requestedContent,
        IReadOnlyList<long>? selectedTrackIds,
        long? defaultTrackId)
    {
        if (selectedTrackIds != null)
        {
            var selected = requestedContent.Subtitle ? ImmutableArray.CreateRange(selectedTrackIds) : [];
            defaultTrackId = defaultTrackId is { } candidate && selected.IndexOf(candidate) >= 0
                ? candidate
                : null;
            requestedContent = requestedContent with
            {
                SelectedSubtitleTrackIds = selected,
                DefaultSubtitleTrackId = defaultTrackId
            };
        }
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        { [ResultParameter] = (directory, requestedContent) };
    }
    public static (string Directory, DownloadContentSelection RequestedContent)? DecodeResult(AppDialogResult result) =>
        result.Outcome != AppDialogOutcome.Accepted
            ? null
            : result.Parameters.TryGetValue(ResultParameter, out var value) &&
              value is ValueTuple<string, DownloadContentSelection> selection
            ? selection
            : throw new InvalidOperationException(
                "Accepted DownloadSettings result is missing its typed result.");
}
