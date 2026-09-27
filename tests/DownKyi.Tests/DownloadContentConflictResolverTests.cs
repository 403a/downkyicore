using DownKyi.Application.Desktop;
using DownKyi.Core.BiliApi.VideoStream.Models;
using DownKyi.Domain.Downloads;
using DownKyi.Presentation;
using DownKyi.Services.Download;

namespace DownKyi.Tests;

public sealed class DownloadContentConflictResolverTests
{
    [Fact]
    public async Task AvailableRequestPassesThroughWithoutDialog()
    {
        var dialogs = new RecordingDialogService();
        var requested = DownloadContentSelection.None with
        {
            Video = true,
            Subtitle = true
        };

        var finalized = await ResolveAsync(
            dialogs,
            requested,
            CreatePreparedDownload(CreatePage(video: true, audio: false)));

        var page = Assert.Single(Assert.Single(finalized.Sections).Pages);
        Assert.Same(requested, page.RequestedContent);
        Assert.Empty(dialogs.Requests);
    }

    [Fact]
    public async Task AvailableMediaChoiceBecomesPageRequestedContent()
    {
        var dialogs = new RecordingDialogService(
            new DownloadContentConflictDecision(
                DownloadContentConflictAction.UseAvailableMedia,
                ApplyToAll: false));
        var requested = new DownloadContentSelection(
            Audio: true,
            Video: true,
            Danmaku: true,
            Subtitle: false,
            Cover: true);

        var finalized = await ResolveAsync(
            dialogs,
            requested,
            CreatePreparedDownload(CreatePage(video: true, audio: false)));

        var page = Assert.Single(Assert.Single(finalized.Sections).Pages);
        Assert.Equal(
            requested with { Audio = false, Video = true },
            page.RequestedContent);
        var prompt = Assert.IsType<DownloadContentConflictPrompt>(
            Assert.Single(dialogs.Requests).Parameters![DownloadContentConflictDialogContract.PromptParameter]);
        Assert.Equal(new DownloadMediaCapabilities(Video: true, Audio: false), prompt.Conflict.AvailableMedia);
    }

    [Fact]
    public async Task SkipChoiceRemovesPageBeforeTaskCreation()
    {
        var dialogs = new RecordingDialogService(
            new DownloadContentConflictDecision(
                DownloadContentConflictAction.SkipPage,
                ApplyToAll: false));

        var finalized = await ResolveAsync(
            dialogs,
            DownloadContentSelection.All,
            CreatePreparedDownload(CreatePage(video: true, audio: false)));

        Assert.Empty(Assert.Single(finalized.Sections).Pages);
        Assert.Single(dialogs.Requests);
    }

    [Fact]
    public async Task ApplyToAllReusesChoiceAcrossPreparedDownloads()
    {
        var dialogs = new RecordingDialogService(
            new DownloadContentConflictDecision(
                DownloadContentConflictAction.UseAvailableMedia,
                ApplyToAll: true));
        var resolver = new DownloadContentConflictResolver(dialogs);
        var choices = new DownloadContentConflictChoices();

        var first = Assert.IsType<FinalizedDownload>(await resolver.ResolveAsync(
            DownloadContentSelection.All,
            CreatePreparedDownload(CreatePage(video: true, audio: false)),
            isAll: false,
            choices,
            TestContext.Current.CancellationToken));
        var second = Assert.IsType<FinalizedDownload>(await resolver.ResolveAsync(
            DownloadContentSelection.All,
            CreatePreparedDownload(CreatePage(video: true, audio: false)),
            isAll: false,
            choices,
            TestContext.Current.CancellationToken));

        Assert.False(Assert.Single(Assert.Single(first.Sections).Pages).RequestedContent.Audio);
        Assert.False(Assert.Single(Assert.Single(second.Sections).Pages).RequestedContent.Audio);
        Assert.Single(dialogs.Requests);
    }

    [Fact]
    public async Task ApplyToAllDoesNotHideADifferentConflict()
    {
        var dialogs = new RecordingDialogService(
            new DownloadContentConflictDecision(
                DownloadContentConflictAction.UseAvailableMedia,
                ApplyToAll: true),
            new DownloadContentConflictDecision(
                DownloadContentConflictAction.SkipPage,
                ApplyToAll: false));
        var resolver = new DownloadContentConflictResolver(dialogs);
        var choices = new DownloadContentConflictChoices();

        await resolver.ResolveAsync(
            DownloadContentSelection.All,
            CreatePreparedDownload(CreatePage(video: true, audio: false)),
            isAll: false,
            choices,
            TestContext.Current.CancellationToken);
        var audioOnly = Assert.IsType<FinalizedDownload>(await resolver.ResolveAsync(
            DownloadContentSelection.All,
            CreatePreparedDownload(CreatePage(video: false, audio: true)),
            isAll: false,
            choices,
            TestContext.Current.CancellationToken));

        Assert.Empty(Assert.Single(audioOnly.Sections).Pages);
        Assert.Equal(2, dialogs.Requests.Count);
    }

    [Fact]
    public async Task PageWithoutAnyMediaIsSkippedWithoutOfferingInvalidChoice()
    {
        var dialogs = new RecordingDialogService();

        var finalized = await ResolveAsync(
            dialogs,
            DownloadContentSelection.All,
            CreatePreparedDownload(CreatePage(video: false, audio: false)));

        Assert.Empty(Assert.Single(finalized.Sections).Pages);
        Assert.Empty(dialogs.Requests);
    }

    [Fact]
    public async Task AudioOnlyPageDoesNotRequireVideoQuality()
    {
        var dialogs = new RecordingDialogService(
            new DownloadContentConflictDecision(
                DownloadContentConflictAction.UseAvailableMedia,
                ApplyToAll: false));
        var page = CreatePage(video: false, audio: true);
        page.VideoQuality = null!;

        var finalized = await ResolveAsync(
            dialogs,
            DownloadContentSelection.All,
            CreatePreparedDownload(page));

        var finalizedPage = Assert.Single(Assert.Single(finalized.Sections).Pages);
        Assert.Same(page, finalizedPage.Page);
        Assert.Equal(
            DownloadContentSelection.All with { Video = false },
            finalizedPage.RequestedContent);
    }

    [Fact]
    public async Task CanceledDialogCancelsConflictResolution()
    {
        var resolver = new DownloadContentConflictResolver(new CanceledDialogService());

        var finalized = await resolver.ResolveAsync(
            DownloadContentSelection.All,
            CreatePreparedDownload(CreatePage(video: true, audio: false)),
            isAll: false,
            new DownloadContentConflictChoices(),
            TestContext.Current.CancellationToken);

        Assert.Null(finalized);
    }

    private static async Task<FinalizedDownload> ResolveAsync(
        RecordingDialogService dialogs,
        DownloadContentSelection requested,
        PreparedDownload prepared) => Assert.IsType<FinalizedDownload>(
            await new DownloadContentConflictResolver(dialogs).ResolveAsync(
                requested,
                prepared,
                isAll: false,
                new DownloadContentConflictChoices(),
                TestContext.Current.CancellationToken).ConfigureAwait(true));

    private static PreparedDownload CreatePreparedDownload(VideoPage page) => PreparedDownload.Create(
        new VideoInfoView(),
        [
            new VideoSection
            {
                VideoPages = [page]
            }
        ]);

    private static VideoPage CreatePage(bool video, bool audio)
    {
        return new VideoPage
        {
            IsSelected = true,
            Name = "page",
            PlayUrl = new PlayUrl
            {
                Dash = new PlayUrlDash
                {
                    Video = video ? [new PlayUrlDashVideo()] : [],
                    Audio = audio ? [new PlayUrlDashVideo()] : []
                }
            },
            VideoQuality = new VideoQuality()
        };
    }

    private sealed class RecordingDialogService(params DownloadContentConflictDecision[] decisions)
        : IAppDialogService
    {
        private readonly Queue<DownloadContentConflictDecision> _decisions = new(decisions);

        public List<AppDialogRequest> Requests { get; } = [];

        public Task<AppDialogResult> ShowAsync(
            AppDialogRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            var decision = _decisions.Dequeue();
            return Task.FromResult(new AppDialogResult(
                AppDialogOutcome.Accepted,
                DownloadContentConflictDialogContract.Encode(decision)));
        }
    }

    private sealed class CanceledDialogService : IAppDialogService
    {
        public Task<AppDialogResult> ShowAsync(
            AppDialogRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new AppDialogResult(
                AppDialogOutcome.Canceled,
                new Dictionary<string, object?>()));
        }
    }
}
