using DownKyi.ViewModels.UiState;

namespace DownKyi.Tests;

public sealed class VideoDetailUiStateTests
{
    [Theory]
    [InlineData((int)VideoDetailDisplayState.Idle, false, false, false)]
    [InlineData((int)VideoDetailDisplayState.Busy, true, false, false)]
    [InlineData((int)VideoDetailDisplayState.Content, false, true, false)]
    [InlineData((int)VideoDetailDisplayState.Empty, false, false, true)]
    public void DisplayStateProjectsBaseVisibility(
        int stateValue,
        bool expectedBusy,
        bool expectedContent,
        bool expectedEmpty)
    {
        var uiState = new VideoDetailUiState
        {
            DisplayState = (VideoDetailDisplayState)stateValue
        };

        Assert.Equal(expectedBusy, uiState.IsBusy);
        Assert.Equal(expectedContent, uiState.IsContentVisible);
        Assert.Equal(expectedEmpty, uiState.IsEmptyVisible);
    }

    [Fact]
    public void BusyStateKeepsLoadedContentVisible()
    {
        var uiState = new VideoDetailUiState
        {
            VideoInfoView = new DownKyi.Presentation.VideoInfoView(),
            DisplayState = VideoDetailDisplayState.Busy
        };

        Assert.True(uiState.IsBusy);
        Assert.True(uiState.IsContentVisible);
        Assert.False(uiState.IsEmptyVisible);
    }

    [Fact]
    public void BusyContentVisibilityTracksLoadedContent()
    {
        var uiState = new VideoDetailUiState
        {
            DisplayState = VideoDetailDisplayState.Busy
        };
        var visibilityChanges = 0;
        uiState.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(VideoDetailUiState.IsContentVisible))
            {
                visibilityChanges++;
            }
        };

        Assert.False(uiState.IsContentVisible);

        uiState.VideoInfoView = new DownKyi.Presentation.VideoInfoView();

        Assert.True(uiState.IsContentVisible);

        uiState.VideoInfoView = null;

        Assert.False(uiState.IsContentVisible);
        Assert.Equal(2, visibilityChanges);
    }
}
