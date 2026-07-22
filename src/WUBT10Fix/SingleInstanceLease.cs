namespace WUBT10Fix;

internal sealed class SingleInstanceLease : IDisposable
{
    internal const string ApplicationMutexName = @"Local\WU-BT10-Windows-Fix-4A1B695F-3BDA-4DAA-BB53-82B90FD739A6";

    private Mutex? _mutex;

    private SingleInstanceLease(Mutex? mutex)
    {
        _mutex = mutex;
    }

    internal bool IsAcquired => _mutex is not null;

    internal static SingleInstanceLease TryAcquire(string? mutexName = null)
    {
        var mutex = new Mutex(initiallyOwned: true, mutexName ?? ApplicationMutexName, out var createdNew);
        if (createdNew)
            return new SingleInstanceLease(mutex);

        mutex.Dispose();
        return new SingleInstanceLease(null);
    }

    public void Dispose()
    {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is null)
            return;

        try
        {
            mutex.ReleaseMutex();
        }
        finally
        {
            mutex.Dispose();
        }
    }
}
