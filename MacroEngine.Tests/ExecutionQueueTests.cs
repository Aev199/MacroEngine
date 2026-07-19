using MacroEngine.Core;
using Xunit;

namespace MacroEngine.Tests;

public sealed class ExecutionQueueTests
{
    [Fact]
    public void Jobs_RunStrictlyOneAtATime()
    {
        using var queue = new ExecutionQueue();
        using var completed = new CountdownEvent(3);

        int active = 0;
        int maximumActive = 0;
        var order = new List<int>();
        object orderLock = new();

        queue.JobCompleted += _ => completed.Signal();

        for (int index = 1; index <= 3; index++)
        {
            int captured = index;
            Assert.True(queue.TryEnqueue($"job-{captured}", token =>
            {
                int nowActive = Interlocked.Increment(ref active);
                int observed;
                do
                {
                    observed = Volatile.Read(ref maximumActive);
                    if (observed >= nowActive) break;
                }
                while (Interlocked.CompareExchange(ref maximumActive, nowActive, observed) != observed);

                lock (orderLock) order.Add(captured);
                Assert.False(token.WaitHandle.WaitOne(40));
                Interlocked.Decrement(ref active);
            }));
        }

        Assert.True(completed.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, maximumActive);
        Assert.Equal(new[] { 1, 2, 3 }, order);
    }

    [Fact]
    public void CancelAll_CancelsRunningJobAndDropsPendingJobs()
    {
        using var queue = new ExecutionQueue();
        using var started = new ManualResetEventSlim();
        using var cancelled = new ManualResetEventSlim();

        int pendingExecutions = 0;
        queue.JobCancelled += _ => cancelled.Set();

        Assert.True(queue.TryEnqueue("running", token =>
        {
            started.Set();
            token.WaitHandle.WaitOne();
            token.ThrowIfCancellationRequested();
        }));
        Assert.True(queue.TryEnqueue("pending", _ => Interlocked.Increment(ref pendingExecutions)));

        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        queue.CancelAll();

        Assert.True(cancelled.Wait(TimeSpan.FromSeconds(5)));
        Thread.Sleep(100);
        Assert.Equal(0, Volatile.Read(ref pendingExecutions));
        Assert.Equal(0, queue.PendingCount);
    }
}
