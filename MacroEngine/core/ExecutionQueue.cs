using System.Collections.Concurrent;

namespace MacroEngine.Core;

/// <summary>
/// Runs automation jobs one at a time on a dedicated STA thread. This prevents
/// macros from interleaving keyboard, mouse and focus operations.
/// </summary>
internal sealed class ExecutionQueue : IDisposable
{
    private sealed record Job(string Description, Action<CancellationToken> Action);

    private readonly BlockingCollection<Job> _jobs = new(
        new ConcurrentQueue<Job>(), boundedCapacity: 32);
    private readonly Thread _worker;
    private readonly object _currentLock = new();

    private CancellationTokenSource? _currentCancellation;
    private bool _disposed;

    public event Action<string>? JobStarted;
    public event Action<string>? JobCompleted;
    public event Action<string>? JobCancelled;
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

    public int PendingCount => _jobs.Count;

    public bool TryEnqueue(string description, Action<CancellationToken> action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _jobs.TryAdd(new Job(description, action));
    }

    /// <summary>Cancel the running job and discard jobs that have not started.</summary>
    public void CancelAll()
    {
        lock (_currentLock)
            _currentCancellation?.Cancel();

        while (_jobs.TryTake(out _)) { }
    }

    private void WorkerLoop()
    {
        foreach (Job job in _jobs.GetConsumingEnumerable())
        {
            using var cancellation = new CancellationTokenSource();
            lock (_currentLock)
                _currentCancellation = cancellation;

            try
            {
                JobStarted?.Invoke(job.Description);
                job.Action(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                JobCompleted?.Invoke(job.Description);
            }
            catch (OperationCanceledException)
            {
                JobCancelled?.Invoke(job.Description);
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
            AppLog.Write("Execution queue did not stop within two seconds");
        _jobs.Dispose();
    }
}
