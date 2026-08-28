using System.Collections.Concurrent;

namespace ElsaControl.PackageCatalog.Core.Sync;

public sealed class SyncConcurrencyGuard
{
    private readonly ConcurrentDictionary<string, byte> _runningScopes = new(StringComparer.OrdinalIgnoreCase);

    public async Task<bool> TryRunAsync(string scope, Func<Task> action)
    {
        if (!TryAcquire(scope, out var lease))
            return false;

        using (lease)
        {
            await action();
            return true;
        }
    }

    public bool TryAcquire(string scope, out IDisposable lease)
    {
        if (!_runningScopes.TryAdd(scope, 0))
        {
            lease = EmptyLease.Instance;
            return false;
        }

        lease = new ScopeLease(this, scope);
        return true;
    }

    private void Release(string scope) =>
        _runningScopes.TryRemove(scope, out _);

    private sealed class ScopeLease(SyncConcurrencyGuard guard, string scope) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                guard.Release(scope);
        }
    }

    private sealed class EmptyLease : IDisposable
    {
        public static EmptyLease Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
