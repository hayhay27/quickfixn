using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace QuickFix.Internal
{
    public abstract class Disposable : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
                return;

            DisposeCore();
        }

        protected bool IsDisposed => Volatile.Read(ref _disposed) == 1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void ThrowIfDisposed()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(GetType().FullName);
        }

        protected abstract void DisposeCore();
    }
}
