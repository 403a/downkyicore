using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DownKyi.Domain.Results;

namespace DownKyi.Services.Download;

internal sealed class ValidateStage : IDownloadPipelineStage
{
    public string Name => nameof(ValidateStage);

    public Task<OperationResult<DownloadStageResult>> ExecuteAsync(
        DownloadExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.EnsureActive(cancellationToken);
        if (context.NeedsMedia &&
            (!context.MediaSucceeded || !File.Exists(context.OutputMedia)))
        {
            return Task.FromResult(DownloadStageResult.Failure(
                "download.validate.media",
                "The finalized media file is missing or invalid."));
        }

        if (context.NeedsDanmaku && !File.Exists(context.DanmakuFile))
        {
            return Task.FromResult(DownloadStageResult.Failure(
                "download.validate.danmaku",
                "The requested danmaku file was not created."));
        }

        if (context.NeedsSubtitle &&
            context.SubtitleFiles != null &&
            context.SubtitleFiles.Any(subtitle => !File.Exists(subtitle)))
        {
            return Task.FromResult(DownloadStageResult.Failure(
                "download.validate.subtitle",
                "One or more requested subtitle files were not created."));
        }

        if (context.NeedsCover &&
            !File.Exists(context.CoverFile) &&
            !File.Exists(context.PageCoverFile))
        {
            return Task.FromResult(DownloadStageResult.Failure(
                "download.validate.cover",
                "The requested cover files were not created."));
        }

        return Task.FromResult(DownloadStageResult.Success(Name));
    }
}
