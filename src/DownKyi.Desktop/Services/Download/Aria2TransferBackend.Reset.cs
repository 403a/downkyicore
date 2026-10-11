using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DownKyi.Application.Diagnostics;
using DownKyi.Core.Aria2cNet.Client;

namespace DownKyi.Services.Download;

internal sealed partial class Aria2TransferBackend
{
    public async Task<DownloadTransferResult> ResetAsync(
        string? backendIdentity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(backendIdentity))
        {
            return DownloadTransferResult.Succeeded();
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                _ = await _ariaClient
                    .ForceRemoveAsync(backendIdentity, timeout.Token)
                    .ConfigureAwait(true);
            }
            catch (AriaRpcException exception) when (exception.IsNotFound)
            {
                _logger.LogDebugMessage(
                    "aria2 reset found no active task for the persisted backend identity.");
            }
            catch (AriaRpcException)
            {
                return DownloadTransferResult.Failed(
                    DownloadTransferFailureKind.TransientNetwork,
                    "download.transfer.aria2-reset-rejected");
            }

            try
            {
                _ = await _ariaClient
                    .RemoveDownloadResultAsync(backendIdentity, timeout.Token)
                    .ConfigureAwait(true);
            }
            catch (AriaRpcException exception) when (exception.IsNotFound)
            {
                _logger.LogDebugMessage(
                    "aria2 reset found no retained result for the persisted backend identity.");
            }
            catch (AriaRpcException)
            {
                return DownloadTransferResult.Failed(
                    DownloadTransferFailureKind.TransientNetwork,
                    "download.transfer.aria2-reset-result-rejected");
            }

            return DownloadTransferResult.Succeeded();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is OperationCanceledException
            or HttpRequestException
            or IOException
            or TimeoutException)
        {
            _logger.LogWarningMessage(
                $"aria2 transfer reset failed; type={exception.GetType().Name}.");
            return DownloadTransferResult.Failed(
                DownloadTransferFailureKind.TransientNetwork,
                "download.transfer.aria2-reset-rpc");
        }
        catch (Newtonsoft.Json.JsonException exception)
        {
            _logger.LogWarningMessage(
                $"aria2 transfer reset contract failed; type={exception.GetType().Name}.");
            return DownloadTransferResult.Failed(
                DownloadTransferFailureKind.Permanent,
                "download.transfer.aria2-reset-contract");
        }
    }
}
