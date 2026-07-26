using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DownKyi.Domain.Results;

namespace DownKyi.Services.Download;

internal sealed class DownloadArtifactsStage : IDownloadPipelineStage
{
    private readonly DownloadArtifactWriter _artifactWriter;

    public DownloadArtifactsStage(DownloadArtifactWriter artifactWriter)
    {
        _artifactWriter = artifactWriter ?? throw new ArgumentNullException(nameof(artifactWriter));
    }

    public string Name => nameof(DownloadArtifactsStage);

    public async Task<OperationResult<DownloadStageResult>> ExecuteAsync(
        DownloadExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var downloading = context.Downloading;
        if (context.Settings.Video.Content.GenerateMovieMetadata)
        {
            _artifactWriter.GenerateNfoFile(downloading);
        }

        if (context.NeedsDanmaku)
        {
            context.DanmakuFile = await _artifactWriter.DownloadDanmakuAsync(
                downloading,
                context.Settings.Danmaku,
                cancellationToken).ConfigureAwait(true);
        }

        context.EnsureActive(cancellationToken);
        if (context.NeedsSubtitle)
        {
            context.SubtitleFiles = await _artifactWriter.DownloadSubtitleAsync(
                downloading,
                cancellationToken).ConfigureAwait(true);
        }

        context.EnsureActive(cancellationToken);
        if (context.NeedsCover)
        {
            var pageCoverFileName =
                $"{downloading.DownloadBase.FilePath}.{GetImageExtension(downloading.DownloadBase.PageCoverUrl)}";
            context.PageCoverFile = await _artifactWriter.DownloadCoverAsync(
                downloading,
                downloading.DownloadBase.PageCoverUrl,
                pageCoverFileName,
                cancellationToken).ConfigureAwait(true);

            var coverFileName =
                $"{downloading.DownloadBase.FilePath}.Cover.{GetImageExtension(downloading.DownloadBase.CoverUrl)}";
            context.CoverFile = await _artifactWriter.DownloadCoverAsync(
                downloading,
                downloading.DownloadBase.CoverUrl,
                coverFileName,
                cancellationToken).ConfigureAwait(true);
        }

        context.EnsureActive(cancellationToken);
        return DownloadStageResult.Success(Name);
    }

    internal static string GetImageExtension(string? coverUrl)
    {
        if (string.IsNullOrWhiteSpace(coverUrl))
        {
            return string.Empty;
        }

        var candidate = coverUrl.StartsWith("//", StringComparison.Ordinal)
            ? $"{Uri.UriSchemeHttps}:{coverUrl}"
            : coverUrl;
        var path = Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            ? uri.AbsolutePath
            : coverUrl.Split('?', '#')[0];
        return Path.GetExtension(path).TrimStart('.');
    }
}
