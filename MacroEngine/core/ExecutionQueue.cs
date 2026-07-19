using System.Collections.Concurrent;

namespace MacroEngine.Core;

/// <summary>
/// Runs at most one automation job on a dedicated STA thread. New triggers are
/// rejected while a job is running or waiting, so input can never execute later
/// in a window the user no longer expects.
/// </summary>
internal sealed class ExecutionQueue : IDisposable
{
    private sealed record Job(string Description, Action<CancellationToken> Action);

    private readonly BlockingCollection<Job> _jobs = new(
        new ConcurrentQueue<Job>(), boundedCapacity: 1);
    private readonly Thread _worker;
    private readonly object _currentLock = new();

    private CancellationTokenSource? _currentCancellation;
    private int _cancelBeforeStart;
    private int _reserved;
    private bool _disposed;

    public event Action<string>? JobStarted;
    public event Action<string>? JobCompleted;
    public event Action<string, string?>? JobCancelled;
    public event Action<string, Exception>? JobFailed;

    public ExecutionQueue()
    {
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "MacroEngine.ExecutionQueue"
        };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    public int PendingCount => Volatile.Read(ref _reserved);
    public bool IsBusy => Volatile.Read(ref _reserved) != 0;

    public bool TryEnqueue(string description, Action<CancellationToken> action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Interlocked.CompareExchange(ref _reserved, 1, 0) != 0)
            return false;

        try
        {
            if (_jobs.TryAdd(new Job(description, action)))
                return true;
        }
        catch (InvalidOperationException) when (_jobs.IsAddingCompleted)
        {
            // Dispose raced this enqueue attempt.
        }

        Interlocked.Exchange(ref _reserved, 0);
        return false;
    }

    /// <summary>Cancel the running job or prevent a dequeued job from starting.</summary>
    public void CancelAll()
    {
        bool hasRunningJob;
        lock (_currentLock)
        {
            hasRunningJob = _currentCancellation != null;
            if (hasRunningJob)
                _currentCancellation!.Cancel();
            else if (Volatile.Read(ref _reserved) != 0)
                Interlocked.Exchange(ref _cancelBeforeStart, 1);
        }

        bool removedPending = false;
        while (_jobs.TryTake(out _))
            removedPending = true;

        if (removedPending && !hasRunningJob)
        {
            Interlocked.Exchange(ref _cancelBeforeStart, 0);
            Interlocked.Exchange(ref _reserved, 0);
        }
    }

    private void WorkerLoop()
    {
        foreach (Job job in _jobs.GetConsumingEnumerable())
        {
            using var cancellation = new CancellationTokenSource();
            lock (_currentLock)
                _currentCancellation = cancellation;

            if (Interlocked.Exchange(ref _cancelBeforeStart, 0) != 0)
                cancellation.Cancel();

            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                JobStarted?.Invoke(job.Description);
                job.Action(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                JobCompleted?.Invoke(job.Description);
            }
            catch (OperationCanceledException ex)
            {
                string? reason = cancellation.IsCancellationRequested ? null : ex.Message;
                JobCancelled?.Invoke(job.Description, reason);
            }
            catch (Exception ex)
            {
                JobFailed?.Invoke(job.Description, ex);
            }
            finally
            {
                lock (_currentLock)
                {
                    if (ReferenceEquals(_currentCancellation, cancellation))
                        _currentCancellation = null;
                }

                Interlocked.Exchange(ref _reserved, 0);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CancelAll();
        _jobs.CompleteAdding();
        if (!_worker.Join(TimeSpan.FromSeconds(2)))
            AppLog.Write("Execution worker did not stop within two seconds");
        _jobs.Dispose();
    }
}
