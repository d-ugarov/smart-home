namespace SmartHome.Persistence;

/// <summary>
/// One operation on the database at a time. The state, the automations and the backup touch it from different threads,
/// and parallel SQLite connections may get "database is locked" instead of waiting; the server does a few operations a
/// minute, so waiting costs nothing.
/// </summary>
internal sealed class DatabaseGate
{
    private readonly SemaphoreSlim semaphore = new(1, 1);

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        return new Exit(semaphore);
    }

    public IDisposable Enter()
    {
        semaphore.Wait();
        return new Exit(semaphore);
    }

    private sealed class Exit(SemaphoreSlim semaphore) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                semaphore.Release();
        }
    }
}
