using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using DownKyi.Application.Desktop;
using DownKyi.Application.Diagnostics;
using DownKyi.Commands;
using DownKyi.Core.Settings;
using DownKyi.Images;
using DownKyi.Presentation;
using DownKyi.Services;
using DownKyi.Services.Download;
using DownKyi.Services.Video;
using DownKyi.Utils;
using DownKyi.ViewModels.Dialogs;
using DownKyi.ViewModels.UiState;
using Microsoft.Extensions.Logging;

namespace DownKyi.ViewModels;

internal sealed class ViewVideoDetailViewModel : ViewModelBase
{
    public const string Tag = "PageVideoDetail";

    private readonly IClipboardService _clipboardService;
    private readonly IVideoDetailDownloadCoordinator _downloadCoordinator;
    private readonly ILogger<ViewVideoDetailViewModel> _logger;
    private readonly ISettingsStore _settingsStore;
    private readonly IVideoDetailWorkflowCoordinator _workflow;

    public ViewVideoDetailViewModel(
        IDesktopInteractionContext desktopInteractions,
        IClipboardService clipboardService,
        ISettingsStore settingsStore,
        IVideoDetailWorkflowCoordinator workflow,
        IVideoDetailDownloadCoordinator downloadCoordinator,
        ILogger<ViewVideoDetailViewModel> logger)
        : base(desktopInteractions)
    {
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _downloadCoordinator = downloadCoordinator ?? throw new ArgumentNullException(nameof(downloadCoordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var operationCommandGate = new DownKyiAsyncCommandGate();
        UiState.DownloadManage = CreateDownloadManageIcon();
        BackSpaceCommand = new RelayCommand(ExecuteBackSpace);
        DownloadManagerCommand = new RelayCommand(ExecuteDownloadManagerCommand);
        InputCommand = new DownKyiAsyncDelegateCommand(ExecuteInputCommandAsync, _logger, () => !UiState.IsBusy, executionGate: operationCommandGate);
        InputSearchCommand = new RelayCommand(() => _workflow.ApplySearch(UiState.InputSearchText));
        CopyCoverUrlCommand = new DownKyiAsyncDelegateCommand(ExecuteCopyCoverUrlCommandAsync, _logger);
        UpperCommand = new RelayCommand(ExecuteUpperCommand);
        SelectAllCommand = new RelayCommand(() => SetAllSelected(UiState.IsSelectAll));
        ClearSelectionCommand = new RelayCommand(() => SetAllSelected(isSelected: false));
        ParseCommand = new DownKyiAsyncDelegateCommand<object>(ExecuteParseCommandAsync, _logger, _ => !UiState.IsBusy, executionGate: operationCommandGate);
        ParseAllVideoCommand = new DownKyiAsyncDelegateCommand(ExecuteParseAllVideoCommandAsync, _logger, () => !UiState.IsBusy, executionGate: operationCommandGate);
        AddToDownloadCommand = new DownKyiAsyncDelegateCommand(() => AddToDownloadAsync(false), _logger, () => !UiState.IsBusy, executionGate: operationCommandGate);
        UiState.IsBusyChanged += OnIsBusyChanged;
    }

    public VideoDetailUiState UiState { get; } = new();
    public RangeObservableCollection<VideoSection> VideoSections { get; } = new();

    public RelayCommand BackSpaceCommand { get; }
    public RelayCommand DownloadManagerCommand { get; }
    public DownKyiAsyncDelegateCommand InputCommand { get; }
    public ICommand InputSearchCommand { get; }
    public ICommand CopyCoverUrlCommand { get; }
    public RelayCommand UpperCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public DownKyiAsyncDelegateCommand<object> ParseCommand { get; }
    public DownKyiAsyncDelegateCommand ParseAllVideoCommand { get; }
    public DownKyiAsyncDelegateCommand AddToDownloadCommand { get; }

    private void OnIsBusyChanged()
    {
        InputCommand.NotifyCanExecuteChanged();
        ParseCommand.NotifyCanExecuteChanged();
        ParseAllVideoCommand.NotifyCanExecuteChanged();
        AddToDownloadCommand.NotifyCanExecuteChanged();
    }

    protected internal override void ExecuteBackSpace()
    {
        _workflow.Cancel();
        if (TryNavigateBack())
        {
            return;
        }

        NavigateToParent();
    }

    private void ExecuteDownloadManagerCommand()
    {
        Navigation.Navigate(new AppNavigationRequest(
            AppRoute.DownloadManager,
            ParentRoute));
    }

    private Task ExecuteInputCommandAsync()
    {
        return ExecuteInputCommandAsync(UiState.InputText);
    }

    private async Task ExecuteInputCommandAsync(string? requestedInput)
    {
        var operation = _workflow.StartOperation();
        var settings = _settingsStore.Current;
        ResetView();
        if (string.IsNullOrWhiteSpace(requestedInput))
        {
            SetDisplayState(VideoDetailDisplayState.Idle);
            return;
        }

        SetOperationStatus(
            VideoDetailOperationStatus.Running,
            StatusText("VideoDetailStatusLoading", "正在读取视频信息……"));

        await RunOperationAsync(operation, async () =>
        {
            UiState.InputText = _workflow.SetInput(requestedInput);
            _logger.LogDebugMessage("Processing captured video input.");
            var result = await _workflow.LoadDetailAsync(operation).ConfigureAwait(true);
            await UiDispatcher.InvokeAsync(() => ApplyVideoDetailResult(result, operation.CancellationToken));
            if (_workflow.IsCurrent(operation) && settings.Basic.IsAutoParseVideo == AllowStatus.Yes)
            {
                await ExecuteParseAllVideoCommandAsync().ConfigureAwait(true);
            }
        }, VideoDetailDisplayState.Empty).ConfigureAwait(true);
    }

    private async Task ExecuteCopyCoverUrlCommandAsync()
    {
        if (UiState.VideoInfoView?.CoverUrl is not { } coverUrl)
        {
            return;
        }

        await _clipboardService.SetTextAsync(coverUrl).ConfigureAwait(true);
        _logger.LogInformationMessage("Video cover URL copied to the clipboard.");
    }

    private void ExecuteUpperCommand()
    {
        if (UiState.VideoInfoView != null)
        {
            var route = _settingsStore.Current.User.Mid == UiState.VideoInfoView.UpperMid
                ? AppRoute.MySpace
                : AppRoute.UserSpace;
            Navigation.Navigate(new AppNavigationRequest(
                route,
                AppRoute.VideoDetail,
                UiState.VideoInfoView.UpperMid));
        }
    }

    private void SetAllSelected(bool isSelected)
    {
        VideoSelectionState.SetAllSelected(VideoSelectionState.GetSelectedSection(VideoSections), isSelected);
        UiState.IsSelectAll = isSelected;
    }

    private async Task ExecuteParseCommandAsync(object? parameter)
    {
        if (parameter is not VideoPage page)
        {
            return;
        }

        var operation = _workflow.StartOperation();
        var settings = _settingsStore.Current;
        SetOperationStatus(
            VideoDetailOperationStatus.Running,
            StatusText("VideoDetailStatusParsing", "正在解析音质、画质和视频编码……"));
        SetDisplayState(VideoDetailDisplayState.Busy);
        await RunOperationAsync(operation, async () =>
        {
            var result = await _workflow.LoadPageStreamAsync(page, operation).ConfigureAwait(true);
            if (!_workflow.IsCurrent(operation))
            {
                return;
            }

            var batch = VideoStreamParseBatchResult.From(result);
            await UiDispatcher.InvokeAsync(() =>
            {
                if (!_workflow.IsCurrent(operation))
                {
                    return;
                }

                ApplyVideoStreamResults(
                    batch.Items,
                    settings,
                    operation.CancellationToken);
                PublishParseStatus(batch);
            });

            RestoreDisplayStateIfCurrent(operation);
        }, null).ConfigureAwait(true);
    }

    private async Task ExecuteParseAllVideoCommandAsync()
    {
        var parseScope = _settingsStore.Current.Basic.ParseScope;
        if (parseScope != ParseScope.None)
        {
            await ExecuteParseAsync(parseScope).ConfigureAwait(true);
            return;
        }

        SetOperationStatus(
            VideoDetailOperationStatus.Running,
            StatusText(
                "VideoDetailStatusChooseParseScope",
                "请选择本次要解析的范围。"));
        var result = await ParsingSelectorDialog.ShowAsync(AppDialogs).ConfigureAwait(true);
        if (result != null)
        {
            await ExecuteParseAsync(result.Scope).ConfigureAwait(true);
            return;
        }

        SetOperationStatus(
            VideoDetailOperationStatus.Canceled,
            StatusText("VideoDetailStatusCanceled", "操作已取消。"));
    }

    private async Task ExecuteParseAsync(ParseScope parseScope)
    {
        var operation = _workflow.StartOperation();
        var settings = _settingsStore.Current;
        SetOperationStatus(
            VideoDetailOperationStatus.Running,
            StatusText("VideoDetailStatusParsing", "正在解析音质、画质和视频编码……"));
        SetDisplayState(VideoDetailDisplayState.Busy);
        await RunOperationAsync(operation, async () =>
        {
            var result = await _workflow
                .LoadPageStreamsAsync(VideoSections, parseScope, operation)
                .ConfigureAwait(true);
            if (!_workflow.IsCurrent(operation))
            {
                return;
            }

            await UiDispatcher.InvokeAsync(() =>
            {
                if (!_workflow.IsCurrent(operation))
                {
                    return;
                }

                ApplyVideoStreamResults(
                    result.Items,
                    settings,
                    operation.CancellationToken);
                PublishParseStatus(result);
            });
            if (!_workflow.IsCurrent(operation))
            {
                return;
            }

            RestoreDisplayState();
            _logger.LogDebugMessage($"ParseScope: {parseScope:G}");
        }, null).ConfigureAwait(true);
    }

    private async Task AddToDownloadAsync(bool isAll)
    {
        SetOperationStatus(
            VideoDetailOperationStatus.Running,
            StatusText("VideoDetailStatusAddingDownloads", "正在建立下载任务……"));
        if (UiState.VideoInfoView == null)
        {
            PublishAddResult(DownloadAddResult.Stopped(
                DownloadAddOutcome.Rejected,
                DownloadAddStopReason.NoPagesSelected));
            return;
        }

        var operation = _workflow.StartOperation();
        await RunOperationAsync(operation, async () =>
        {
            var result = await _downloadCoordinator.AddAsync(
                _workflow.CurrentInput,
                UiState.VideoInfoView,
                VideoSections.ToList(),
                isAll,
                operation.CancellationToken).ConfigureAwait(true);
            if (_workflow.IsCurrent(operation))
            {
                PublishAddResult(result);
            }
        }, null).ConfigureAwait(true);
    }

    private void ResetView()
    {
        _workflow.Reset();
        UiState.GridResetVersion++;
        SetDisplayState(VideoDetailDisplayState.Busy);
        UiState.VideoInfoView = null;
        UiState.SelectedVideoPage = null;
        VideoSections.ReplaceRange(Array.Empty<VideoSection>());
        SetOperationStatus(VideoDetailOperationStatus.None, string.Empty);
    }

    private void ApplyVideoDetailResult(VideoDetailParseResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UiState.VideoInfoView = result.VideoInfoView;
        if (UiState.VideoInfoView == null)
        {
            SetDisplayState(VideoDetailDisplayState.Empty);
            SetOperationStatus(
                VideoDetailOperationStatus.Error,
                StatusText(
                    "VideoDetailStatusNoContent",
                    "没有找到可解析的视频内容。"));
            return;
        }

        VideoSections.ReplaceRange(result.VideoSections);
        UiState.SelectedVideoPage = VideoSelectionState.SelectInputPage(VideoSections, _workflow.CurrentInput);
        SetDisplayState(VideoDetailDisplayState.Content);
        SetOperationStatus(
            VideoDetailOperationStatus.Success,
            StatusText("VideoDetailStatusLoaded", "视频信息已载入，可选择内容并解析画质。"));
    }

    private void ApplyVideoStreamResults(
        IReadOnlyList<VideoStreamParseResult> results,
        ApplicationSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var result in results)
        {
            if (result.PlayUrl is { } playUrl)
            {
                VideoPagePlaybackMapper.ApplyPlayUrlPreservingSelection(
                    playUrl,
                    result.Page,
                    settings,
                    _logger);
            }
        }
    }

    private void PublishAddResult(DownloadAddResult result)
    {
        var notification = DownloadAddNotificationFormatter.Create(result);
        Notifications.Show(notification.Message);
        SetOperationStatus(notification.Kind switch
        {
            DownloadAddNotificationKind.Success => VideoDetailOperationStatus.Success,
            DownloadAddNotificationKind.Warning => VideoDetailOperationStatus.Warning,
            DownloadAddNotificationKind.Canceled => VideoDetailOperationStatus.Canceled,
            DownloadAddNotificationKind.Error => VideoDetailOperationStatus.Error,
            _ => throw new ArgumentOutOfRangeException(nameof(result), notification.Kind, null)
        }, notification.Message);
    }

    private void HandleOperationError(
        Exception exception,
        VideoDetailOperation operation,
        VideoDetailDisplayState? failureState)
    {
        if (!_workflow.IsCurrent(operation))
        {
            return;
        }

        _logger.LogErrorMessage("Video detail operation failed.", exception);
        Notifications.Show(exception.Message);
        SetOperationStatus(VideoDetailOperationStatus.Error, exception.Message);
        if (failureState is { } state)
        {
            SetDisplayState(state);
        }
        else
        {
            RestoreDisplayState();
        }
    }

    private async Task RunOperationAsync(
        VideoDetailOperation operation,
        Func<Task> action,
        VideoDetailDisplayState? failureState)
    {
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (DownloadAddCanceledException exception)
        {
            if (_workflow.IsCurrent(operation))
            {
                RestoreDisplayState();
                PublishAddResult(exception.PartialResult);
            }
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
            if (_workflow.IsCurrent(operation))
            {
                RestoreDisplayState();
                SetOperationStatus(
                    VideoDetailOperationStatus.Canceled,
                    StatusText("VideoDetailStatusCanceled", "操作已取消。"));
            }
        }
        catch (Exception exception) when (IsExpectedOperationException(exception))
        {
            HandleOperationError(exception, operation, failureState);
        }
        finally
        {
            SettleIncompleteOperation(operation, failureState);
        }
    }

    private void SettleIncompleteOperation(
        VideoDetailOperation operation,
        VideoDetailDisplayState? failureState)
    {
        if (!_workflow.IsCurrent(operation)
            || UiState.OperationStatus != VideoDetailOperationStatus.Running)
        {
            return;
        }

        if (failureState is { } state)
        {
            SetDisplayState(state);
        }
        else
        {
            RestoreDisplayState();
        }

        SetOperationStatus(
            VideoDetailOperationStatus.Error,
            StatusText("VideoDetailStatusFailed", "操作失败，请查看日志。"));
    }

    private static bool IsExpectedOperationException(Exception exception)
    {
        return exception is System.Net.Http.HttpRequestException or InvalidOperationException
            or ArgumentException or FormatException or RegexMatchTimeoutException
            or Newtonsoft.Json.JsonException or System.IO.IOException
            or UnauthorizedAccessException;
    }

    private void RestoreDisplayStateIfCurrent(VideoDetailOperation operation)
    {
        if (_workflow.IsCurrent(operation))
        {
            RestoreDisplayState();
        }
    }

    private void RestoreDisplayState()
    {
        SetDisplayState(UiState.VideoInfoView == null
            ? VideoDetailDisplayState.Idle
            : VideoDetailDisplayState.Content);
    }

    private void PublishParseStatus(VideoStreamParseBatchResult result)
    {
        if (!result.HadSelection)
        {
            SetOperationStatus(
                VideoDetailOperationStatus.Warning,
                StatusText(
                    "VideoDetailStatusNoSelection",
                    "没有选中要解析的视频。"));
            return;
        }

        if (result.SuccessfulCount == 0)
        {
            SetOperationStatus(
                VideoDetailOperationStatus.Warning,
                StatusText(
                    "VideoDetailStatusNoPlayback",
                    "解析没有返回可用的媒体信息。"));
            return;
        }

        var missingCodecs = result.Items
            .Where(item => item.HasUsableContent)
            .Select(item => new
            {
                Quality = item.Page.VideoQuality,
                CodecIds = item.Page.PlaybackAvailability?.Video
                    .Where(video => video.Quality == item.Page.VideoQuality?.Quality
                        && (video.StreamKind == DownKyi.Core.BiliApi.VideoStream.Models.PlayUrlStreamKind.Durl)
                            == item.Page.VideoQuality?.IsDurl)
                    .Select(video => video.CodecId)
                    .Distinct()
                    .Order()
                    .ToArray() ?? []
            })
            .Where(item => item.Quality != null && item.Quality.VideoCodecList.Count == 0)
            .ToArray();
        if (missingCodecs.Length > 0)
        {
            var qualityDetails = string.Join(
                "、",
                missingCodecs
                    .Select(item => StatusText(
                        "VideoDetailStatusMissingCodecDetail",
                        "{0}（编码 ID：{1}）",
                        string.IsNullOrWhiteSpace(item.Quality!.QualityFormat)
                            ? "未知画质"
                            : item.Quality.QualityFormat,
                        item.CodecIds.Length == 0
                            ? "未收到"
                            : string.Join(",", item.CodecIds)))
                    .Distinct(StringComparer.Ordinal));
            var message = result.UnavailableCount > 0
                ? StatusText(
                    "VideoDetailStatusPartialMissingCodecs",
                    "解析部分完成：已取得 {0} 个视频，{1} 个没有可用媒体；其中 {2} 个当前画质没有可识别的编码选项：{3}。",
                    result.SuccessfulCount,
                    result.UnavailableCount,
                    missingCodecs.Length,
                    qualityDetails)
                : StatusText(
                    "VideoDetailStatusMissingCodecs",
                    "解析完成，但 {0} 个视频的当前画质没有可识别的编码选项：{1}。",
                    missingCodecs.Length,
                    qualityDetails);
            SetOperationStatus(VideoDetailOperationStatus.Warning, message);
            return;
        }

        if (result.UnavailableCount > 0)
        {
            SetOperationStatus(
                VideoDetailOperationStatus.Warning,
                StatusText(
                    "VideoDetailStatusPartiallyParsed",
                    "解析部分完成：已更新 {0} 个视频，{1} 个没有可用媒体。",
                    result.SuccessfulCount,
                    result.UnavailableCount));
            return;
        }

        SetOperationStatus(
            VideoDetailOperationStatus.Success,
            StatusText(
                "VideoDetailStatusParsed",
                "解析完成：已更新 {0} 个视频。",
                result.SuccessfulCount));
    }

    private void SetOperationStatus(VideoDetailOperationStatus status, string message)
    {
        void Apply()
        {
            UiState.OperationStatusIcon = status switch
            {
                VideoDetailOperationStatus.Error => SystemIcon.Current.Error,
                VideoDetailOperationStatus.Warning => SystemIcon.Current.Warning,
                _ => SystemIcon.Current.Info
            };
            UiState.OperationStatusText = message;
            UiState.OperationStatus = status;
        }

        if (UiDispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            UiDispatcher.Post(Apply);
        }
    }

    private static string StatusText(
        string resourceKey,
        string fallback,
        params object[] arguments)
    {
        var format = DictionaryResource.GetString(resourceKey);
        if (string.IsNullOrWhiteSpace(format))
        {
            format = fallback;
        }

        return arguments.Length == 0
            ? format
            : string.Format(CultureInfo.CurrentCulture, format, arguments);
    }

    private void SetDisplayState(VideoDetailDisplayState state)
    {
        if (UiDispatcher.CheckAccess())
        {
            UiState.DisplayState = state;
        }
        else
        {
            UiDispatcher.Post(() => UiState.DisplayState = state);
        }
    }

    private static VectorImage CreateDownloadManageIcon()
    {
        var icon = ButtonIcon.DownloadManage;
        icon.Height = 24;
        icon.Width = 24;
        return icon;
    }

    public override void OnNavigatedTo(AppNavigationContext navigationContext)
    {
        ArgumentNullException.ThrowIfNull(navigationContext);
        UiState.DownloadManage = CreateDownloadManageIcon();
        if (navigationContext.Parameter is string parameter)
        {
            var input = parameter.Replace(AppConstant.ClipboardId, string.Empty, StringComparison.Ordinal);
            if (UiState.InputText != input || !parameter.EndsWith(AppConstant.ClipboardId, StringComparison.Ordinal))
            {
                if (InputCommand.CanExecute(null))
                {
                    UiState.InputText = input;
                    InputCommand.Execute(null);
                }
            }
        }

        base.OnNavigatedTo(navigationContext);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            UiState.IsBusyChanged -= OnIsBusyChanged;
            _workflow.Dispose();
        }

        base.Dispose(disposing);
    }
}
