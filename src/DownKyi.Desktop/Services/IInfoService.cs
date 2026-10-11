using System.Collections.Generic;
using System.Threading.Tasks;
using DownKyi.Core.BiliApi.VideoStream.Models;
using DownKyi.Presentation;

namespace DownKyi.Services;

internal interface IInfoService
{
    VideoInfoView? GetVideoView(System.Threading.CancellationToken cancellationToken = default);

    IList<VideoSection>? GetVideoSections(bool noUgc, System.Threading.CancellationToken cancellationToken = default);

    IList<VideoPage>? GetVideoPages(System.Threading.CancellationToken cancellationToken = default);

    Task<PlayUrl?> GetVideoStreamAsync(
        VideoPage page,
        System.Threading.CancellationToken cancellationToken = default);

    // Playback discovery can limit API supplementation to media actually requested
    // by this download. Other implementations retain their existing behavior.
    Task<PlayUrl?> GetVideoStreamAsync(
        VideoPage page,
        bool requireVideo,
        bool requireAudio,
        System.Threading.CancellationToken cancellationToken = default) =>
        GetVideoStreamAsync(page, cancellationToken);
}
