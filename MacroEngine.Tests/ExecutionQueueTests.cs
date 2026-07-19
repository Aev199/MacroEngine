using MacroEngine.Core;
using Xunit;

namespace MacroEngine.Tests;

public sealed class ExecutionQueueTests
{
    [Fact]
    public void SecondJob_IsRejectedWhileFirstRuns_AndAcceptedAfterCompletion()
    {
        using var queue = new ExecutionQueue();
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var firstCompleted = new ManualResetEventSlim();
        using var secondCompleted = new ManualResetEventSlim();

        queue.JobCompleted += description =>
        {
            if (description == "first") firstCompleted.Set();
            if (description == "second") secondCompleted.Set();
        };

        Assert.True(queue.TryEnqueue("first", token =>
        {
            started.Set();
            WaitHandle.WaitAny(new[] { release.WaitHandle, token.WaitHandle });
            token.ThrowIfCancellationRequested();
        }));

        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(queue.IsBusy);
        Assert.False(queue.TryEnqueue("rejected", _ => throw new InvalidOperationException()));

        release.Set();
        Assert.True(firstCompleted.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(SpinWait.SpinUntil(() => !queue.IsBusy, TimeSpan.FromSeconds(2)));

        Assert.True(queue.TryEnqueue("second", _ => { }));
        Assert.True(secondCompleted.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void CancelAll_CancelsRunningJob_AndAllowsNextJob()
    {
        using var queue = new ExecutionQueue();
        using var started = new ManualResetEventSlim();
        using var cancelled = new ManualResetEventSlim();
        using var nextCompleted = new ManualResetEventSlim();

        queue.JobCancelled += _ => cancelled.Set();
        queue.JobCompleted += description =>
        {
            if (description == "next") nextCompleted.Set();
        };

        Assert.True(queue.TryEnqueue("running", token =>
        {
            started.Set();
            token.WaitHandle.WaitOne();
            token.ThrowIfCancellationRequested();
        }));

        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        queue.CancelAll();

        Assert.True(cancelled.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(SpinWait.SpinUntil(() => !queue.IsBusy, TimeSpan.FromSeconds(2)));
        Assert.True(queue.TryEnqueue("next", _ => { }));
        Assert.True(nextCompleted.Wait(TimeSpan.FromSeconds(5)));
    }
}
