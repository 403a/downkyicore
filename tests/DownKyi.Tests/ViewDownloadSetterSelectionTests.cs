using System.Collections.Specialized;
using DownKyi.Application.Desktop;
using DownKyi.Domain.Downloads;
using DownKyi.Services.Download;
using DownKyi.ViewModels.Dialogs;
using Microsoft.Extensions.Logging.Abstractions;

namespace DownKyi.Tests;

public sealed class ViewDownloadSetterSelectionTests
{
    [Fact]
    public void DownloadCommandReturnsSelectedAndDefaultSubtitleTrackIds()
    {
        using var settings = new TestSettingsStore();
        var interaction = new TestDesktopInteractionContext();
        var viewModel = new ViewDownloadSetterViewModel(
            interaction.Notifications,
            new StubFilePickerService(),
            settings.Store,
            NullLogger<ViewDownloadSetterViewModel>.Instance)
        {
            Directory = Path.GetTempPath(),
            DownloadSubtitle = true
        };
        viewModel.OnDialogOpened(DownloadSettingsDialog.CreateRequest(
        [
            new(11, "zh", "中文", 0, "//zh"),
            new(22, "en", "English", 1, "//en")
        ]));
        viewModel.SubtitleTracks[0].IsSelected = false;
        AppDialogResult? result = null;
        viewModel.CloseRequested += (_, value) => result = value;

        viewModel.DownloadCommand.Execute(null);

        Assert.NotNull(result);
        var selection = DownloadSettingsDialog.DecodeResult(result);
        Assert.True(selection.HasValue);
        Assert.True(selection.Value.RequestedContent.SelectedSubtitleTrackIds.HasValue);
        Assert.Equal([22L], selection.Value.RequestedContent.SelectedSubtitleTrackIds.GetValueOrDefault().ToArray());
        Assert.Equal(22L, selection.Value.RequestedContent.DefaultSubtitleTrackId);
        Assert.Equal("en", viewModel.SubtitleTracks[1].Language);
        Assert.Equal("English", viewModel.SubtitleTracks[1].DisplayLanguage);
        Assert.Equal(1, viewModel.SubtitleTracks[1].Type);
        Assert.Equal("//en", viewModel.SubtitleTracks[1].Url);
    }

    [Fact]
    public void DownloadCommandPreservesSelectionWhenReorderingReentersTheBinding()
    {
        using var settings = new TestSettingsStore();
        var interaction = new TestDesktopInteractionContext();
        var viewModel = new ViewDownloadSetterViewModel(
            interaction.Notifications,
            new StubFilePickerService(),
            settings.Store,
            NullLogger<ViewDownloadSetterViewModel>.Instance);
        var selectedDirectory = Path.GetTempPath();
        var reentrantSelection = Path.GetPathRoot(selectedDirectory)!;
        viewModel.DirectoryList.Clear();
        viewModel.DirectoryList.Add("other-directory");
        viewModel.DirectoryList.Add(selectedDirectory);
        viewModel.Directory = selectedDirectory;
        var observedSelections = new List<string>();
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(viewModel.Directory))
            {
                observedSelections.Add(viewModel.Directory);
            }
        };
        viewModel.DirectoryList.CollectionChanged += (_, eventArgs) =>
        {
            if (eventArgs.Action == NotifyCollectionChangedAction.Remove)
            {
                viewModel.Directory = reentrantSelection;
            }
        };

        viewModel.DownloadCommand.Execute(null);

        Assert.Equal(selectedDirectory, viewModel.Directory);
        Assert.Equal(selectedDirectory, viewModel.DirectoryList[0]);
        Assert.Equal(selectedDirectory, settings.Store.Current.Video.SaveVideoRootPath);
        Assert.Equal([reentrantSelection, selectedDirectory], observedSelections);
    }

    [Fact]
    public void DownloadAllSelectsEverySubtitleTrackAndDefault()
    {
        using var settings = new TestSettingsStore();
        var interaction = new TestDesktopInteractionContext();
        var viewModel = new ViewDownloadSetterViewModel(
            interaction.Notifications,
            new StubFilePickerService(),
            settings.Store,
            NullLogger<ViewDownloadSetterViewModel>.Instance);
        viewModel.OnDialogOpened(DownloadSettingsDialog.CreateRequest(
        [
            new(11, "zh", "中文", 0, "//zh"),
            new(22, "en", "English", 1, "//en")
        ]));
        viewModel.DownloadAll = true;

        viewModel.DownloadAllCommand.Execute(null);

        Assert.All(viewModel.SubtitleTracks, track => Assert.True(track.IsSelected));
        Assert.True(viewModel.SubtitleTracks[0].IsDefault);
    }

    [Fact]
    public void SelectingEveryContentOptionSelectsEverySubtitleTrackAndDefault()
    {
        using var settings = new TestSettingsStore();
        var interaction = new TestDesktopInteractionContext();
        var viewModel = new ViewDownloadSetterViewModel(
            interaction.Notifications,
            new StubFilePickerService(),
            settings.Store,
            NullLogger<ViewDownloadSetterViewModel>.Instance)
        {
            DownloadAudio = false,
            DownloadVideo = false,
            DownloadDanmaku = false,
            DownloadSubtitle = false,
            DownloadCover = false
        };
        viewModel.OnDialogOpened(DownloadSettingsDialog.CreateRequest(
        [
            new(11, "zh", "中文", 0, "//zh"),
            new(22, "en", "English", 1, "//en")
        ]));

        viewModel.DownloadAudio = true;
        viewModel.DownloadAudioCommand.Execute(null);
        viewModel.DownloadVideo = true;
        viewModel.DownloadVideoCommand.Execute(null);
        viewModel.DownloadDanmaku = true;
        viewModel.DownloadDanmakuCommand.Execute(null);
        viewModel.DownloadSubtitle = true;
        viewModel.DownloadSubtitleCommand.Execute(null);
        viewModel.DownloadCover = true;
        viewModel.DownloadCoverCommand.Execute(null);

        Assert.True(viewModel.DownloadAll);
        Assert.All(viewModel.SubtitleTracks, track => Assert.True(track.IsSelected));
        Assert.True(viewModel.SubtitleTracks[0].IsDefault);
    }

    [Fact]
    public void SubtitleTrackSelectionRaisesBindingNotification()
    {
        var track = new SubtitleTrackItem(
            new DownloadSettingsDialog.SubtitleTrack(11, "zh", "中文", 0, "//zh"),
            isSelected: false,
            isDefault: false);
        var changed = new List<string?>();
        track.PropertyChanged += (_, eventArgs) => changed.Add(eventArgs.PropertyName);

        track.IsSelected = true;

        Assert.Contains(nameof(track.IsSelected), changed);
    }

    private sealed class StubFilePickerService : IFilePickerService
    {
        public Task<string?> SelectFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<string?> SelectVideoAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>> SelectVideosAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }
}
