using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DownKyi.Domain.Downloads;

namespace DownKyi.Services.Download;

internal sealed class DownloadTaskQueueGateway : IDownloadTaskQueue
{
    private readonly Lock _sync = new();
    private readonly HashSet<DownloadTaskId> _pending = [];
    private IDownloadRuntime? _runtime;

    public async Task AttachAsync(
        IDownloadRuntime runtime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        DownloadTaskId[] pending;
        lock (_sync)
        {
            if (_runtime != null && !ReferenceEquals(_runtime, runtime))
            {
                throw new InvalidOperationException("A download runtime is already attached.");
            }

            _runtime = runtime;
            pending = [.. _pending];
            _pending.Clear();
        }

        try
        {
            foreach (var taskId in pending)
            {
                await runtime.EnqueueAsync(taskId, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            lock (_sync)
            {
                if (ReferenceEquals(_runtime, runtime))
                {
                    _runtime = null;
                }

                foreach (var taskId in pending)
                {
                    _pending.Add(taskId);
                }
            }

            throw;
        }
    }

    public void Detach(IDownloadRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        lock (_sync)
        {
            if (ReferenceEquals(_runtime, runtime))
            {
                _runtime = null;
            }
        }
    }

    public async Task EnqueueAsync(
        DownloadTaskId taskId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(taskId);
        cancellationToken.ThrowIfCancellationRequested();
        IDownloadRuntime? runtime;
        lock (_sync)
        {
            runtime = _runtime;
            if (runtime == null)
            {
                _pending.Add(taskId);
                return;
            }
        }

        try
        {
            await runtime.EnqueueAsync(taskId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ChannelClosedException or ObjectDisposedException)
        {
            lock (_sync)
            {
                if (ReferenceEquals(_runtime, runtime))
                {
                    _runtime = null;
                }

                _pending.Add(taskId);
            }
        }
    }

    public Task<bool> CancelAsync(DownloadTaskId taskId)
    {
        ArgumentNullException.ThrowIfNull(taskId);
        IDownloadRuntime? runtime;
        lock (_sync)
        {
            runtime = _runtime;
            _pending.Remove(taskId);
        }

        return runtime == null
            ? Task.FromResult(false)
            : runtime.CancelAsync(taskId);
    }

}
