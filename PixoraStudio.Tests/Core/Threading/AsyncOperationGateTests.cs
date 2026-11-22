using PixoraStudio.Core.Threading;

namespace PixoraStudio.Tests.Core.Threading;

public sealed class AsyncOperationGateTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldNeverRunOperationsConcurrently()
    {
        AsyncOperationGate gate = new();
        TaskCompletionSource firstEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource secondEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int activeOperations = 0;
        int maximumConcurrentOperations = 0;

        async Task RunOperation(TaskCompletionSource? entered = null)
        {
            int active = Interlocked.Increment(ref activeOperations);
            UpdateMaximum(ref maximumConcurrentOperations, active);
            entered?.TrySetResult();

            if (entered == firstEntered)
                await releaseFirst.Task;

            Interlocked.Decrement(ref activeOperations);
        }

        Task first = gate.ExecuteAsync(() => RunOperation(firstEntered));
        await firstEntered.Task;

        Task second = gate.ExecuteAsync(() => RunOperation(secondEntered));
        Assert.False(secondEntered.Task.IsCompleted);

        releaseFirst.SetResult();
        await Task.WhenAll(first, second);

        Assert.True(secondEntered.Task.IsCompleted);
        Assert.Equal(1, maximumConcurrentOperations);
        Assert.Equal(0, activeOperations);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledWaiter_ShouldNotRunOperation()
    {
        AsyncOperationGate gate = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cancellation = new();

        Task first = gate.ExecuteAsync(async () =>
        {
            entered.SetResult();
            await release.Task;
        });

        await entered.Task;

        bool secondOperationRan = false;
        Task second = gate.ExecuteAsync(() =>
        {
            secondOperationRan = true;
            return Task.CompletedTask;
        }, cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.False(secondOperationRan);

        release.SetResult();
        await first;

        bool followingOperationRan = false;
        await gate.ExecuteAsync(() =>
        {
            followingOperationRan = true;
            return Task.CompletedTask;
        });

        Assert.True(followingOperationRan);
    }

    [Fact]
    public async Task ExecuteAsync_WhenOperationThrows_ShouldReleaseGate()
    {
        AsyncOperationGate gate = new();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.ExecuteAsync(() => throw new InvalidOperationException("Expected test failure.")));

        bool nextOperationRan = false;
        await gate.ExecuteAsync(() =>
        {
            nextOperationRan = true;
            return Task.CompletedTask;
        });

        Assert.True(nextOperationRan);
    }

    private static void UpdateMaximum(ref int maximum, int value)
    {
        int observed;
        do
        {
            observed = Volatile.Read(ref maximum);
            if (observed >= value)
                return;
        }
        while (Interlocked.CompareExchange(ref maximum, value, observed) != observed);
    }
}