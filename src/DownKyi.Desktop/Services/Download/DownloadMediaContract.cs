using System;
using System.Linq;
using DownKyi.Core.BiliApi.BiliUtils;
using DownKyi.Core.BiliApi.VideoStream.Models;
using DownKyi.Domain.Downloads;
using DownKyi.Domain.Results;

namespace DownKyi.Services.Download;

internal static class DownloadMediaContract
{
    public static DownloadMediaKind Detect(PlayUrl? playUrl)
    {
        if (playUrl?.Dash is { } dash &&
            (dash.Video is { Count: > 0 } || DownloadAudioSelection.HasAnyAudio(dash)))
        {
            return DownloadMediaKind.Dash;
        }

        return playUrl?.Durl is { Count: > 0 }
            ? DownloadMediaKind.Durl
            : DownloadMediaKind.None;
    }

    public static OperationError? Validate(
        DownloadExecutionContext context,
        PlayUrl? playUrl)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.NeedsMedia)
        {
            return context.Input.RequestedContent.MediaKind == DownloadMediaKind.None
                ? null
                : Failure("The stored media contract does not match the requested content.");
        }

        var requestedMediaKind = context.Input.RequestedContent.MediaKind;
        if (requestedMediaKind is null)
        {
            return OperationError.Unexpected(
                "download.media.recreate-task",
                "This unfinished download predates the finalized media contract and must be recreated.");
        }

        if (Detect(playUrl) != requestedMediaKind.Value)
        {
            return Failure("The refreshed playback format does not match the finalized media contract.");
        }

        return requestedMediaKind.Value switch
        {
            DownloadMediaKind.Dash => ValidateDash(context, playUrl),
            DownloadMediaKind.Durl => ValidateDurl(context, playUrl),
            _ => Failure("The finalized media contract does not contain a downloadable media format.")
        };
    }

    public static PlayUrlDashVideo? SelectAudio(
        DownloadExecutionContext context,
        PlayUrl? playUrl) =>
        DownloadAudioSelection.Select(context.Input.Metadata.AudioCodec.Id, playUrl);

    public static PlayUrlDashVideo? SelectVideo(
        DownloadExecutionContext context,
        PlayUrl? playUrl)
    {
        var metadata = context.Input.Metadata;
        return playUrl?.Dash?.Video?.FirstOrDefault(item =>
        {
            var codec = PlaybackQualityCatalog.GetCodecIds().FirstOrDefault(candidate =>
                candidate.Id == item.CodecId);
            return item.Id == metadata.Resolution.Id &&
                   codec?.Name == metadata.VideoCodecName;
        });
    }

    private static OperationError? ValidateDash(
        DownloadExecutionContext context,
        PlayUrl? playUrl)
    {
        if (context.NeedsAudio &&
            context.AudioFile == null &&
            SelectAudio(context, playUrl) == null)
        {
            return Failure("The finalized audio stream is unavailable.");
        }

        return context.NeedsVideo && SelectVideo(context, playUrl) == null
            ? Failure("The finalized video stream is unavailable.")
            : null;
    }

    private static OperationError? ValidateDurl(
        DownloadExecutionContext context,
        PlayUrl? playUrl)
    {
        if (context.NeedsAudio && !context.NeedsVideo)
        {
            return OperationError.Unexpected(
                "download.media.durl-audio-only",
                "Audio-only DURL downloads are not supported.");
        }

        var metadata = context.Input.Metadata;
        if (playUrl == null || playUrl.Quality != metadata.Resolution.Id)
        {
            return Failure("The refreshed DURL quality does not match the finalized selection.");
        }

        var codec = PlaybackQualityCatalog.GetCodecIds().FirstOrDefault(candidate =>
            candidate.Id == playUrl.VideoCodecid);
        return codec?.Name != metadata.VideoCodecName
            ? Failure("The refreshed DURL codec does not match the finalized selection.")
            : null;
    }

    private static OperationError Failure(string message) =>
        OperationError.Unexpected("download.media.contract", message);
}
