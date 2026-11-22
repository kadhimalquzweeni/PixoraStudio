namespace PixoraStudio.Core.Threading;

/// <summary>
/// Runs asynchronous operations one at a time. Waiting operations can be
/// cancelled without affecting the operation currently holding the gate.
/// </summary>
public sealed class AsyncOperationGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task ExecuteAsync(
        Func<Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await operation().ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}