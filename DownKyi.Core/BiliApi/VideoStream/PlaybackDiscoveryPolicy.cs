using DownKyi.Core.BiliApi.BiliUtils;
using DownKyi.Core.BiliApi.VideoStream.Models;

namespace DownKyi.Core.BiliApi.VideoStream;

/// <summary>
/// Decides whether webpage discovery has enough actual, URL-backed media for a request.
/// Advertised qualities alone never satisfy this policy.
/// </summary>
public static class PlaybackDiscoveryPolicy
{
    public static bool NeedsApiSupplement(
        PlayUrlAvailability availability,
        int preferredVideoQuality,
        int preferredAudioQuality,
        bool requireVideo,
        bool requireAudio)
    {
        ArgumentNullException.ThrowIfNull(availability);

        return (requireVideo && !PlaybackQualityCatalog.SelectVideoQuality(
                    availability.Video.Select(video => video.Quality),
                    preferredVideoQuality).SatisfiesWithoutConfirmation)
               || (requireAudio && !PlaybackQualityCatalog.SelectAudioQuality(
                    availability.Audio,
                    preferredAudioQuality).SatisfiesWithoutConfirmation);
    }
}
