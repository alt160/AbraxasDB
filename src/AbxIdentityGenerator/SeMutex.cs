using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace AbraxasDB
{
    /// <summary>
    /// Abraxas compatibility surface for the shared Fractal native ownership implementation.<br/>
    /// Cross-process exclusion, cancellation and cross-thread release use one broker rather than divergent recovery code.<br/>
    /// </summary>
    internal sealed class Semutex : IDisposable
    {
        private readonly FractalKVS.Semutex _gate;

        /// <summary>
        /// Opens a cross-process gate without acquiring it.<br/>
        /// Heartbeat parameters remain compatible but are not used to infer ownership or abandonment.<br/>
        /// An existing legacy semaphore of the same name causes a native type-collision failure, never independent locking.<br/>
        /// </summary>
        public Semutex(string name, bool useWindowsLocal = false, bool enableAbandonDetection = false,
            TimeSpan? heartbeatInterval = null, TimeSpan? staleAfter = null)
            => _gate = new FractalKVS.Semutex(name, useWindowsLocal, enableAbandonDetection, heartbeatInterval, staleAfter);

        /// <summary>
        /// Acquires synchronously, returning false on timeout and preserving the Abraxas abandonment exception type.<br/>
        /// An AlreadyAcquired exception still requires release/disposal by the caller.<br/>
        /// </summary>
        public bool WaitOne(int millisecondsTimeout = Timeout.Infinite)
        {
            try { return _gate.WaitOne(millisecondsTimeout); }
            catch (FractalKVS.AbandonedSemutexException ex) { throw new AbandonedSemutexException(ex.Name, ex.AlreadyAcquired); }
        }

        /// <summary>
        /// Acquires asynchronously with cancellation of a pending wait and no blocked thread-pool worker.<br/>
        /// Continuations and release may execute on different caller threads; native ownership stays on the shared broker.<br/>
        /// </summary>
        public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct = default)
        {
            try { return await _gate.WaitAsync(timeout, ct).ConfigureAwait(false); }
            catch (FractalKVS.AbandonedSemutexException ex) { throw new AbandonedSemutexException(ex.Name, ex.AlreadyAcquired); }
        }

        /// <summary>Releases this instance's acquired gate from any thread after protected work has stopped.<br/></summary>
        public void Release() => _gate.ReleaseMutex();

        /// <summary>Releases ownership or cancels a pending wait, and waits for idempotent native cleanup.<br/></summary>
        public void Dispose() => _gate.Dispose();
    }
    internal sealed class AbandonedSemutexException : SystemException
    {
        public AbandonedSemutexException(string name, bool alreadyAcquired)
            : base($"Semutex '{name}' was abandoned; native ownership was acquired by this instance.")
        {
            Name = name;
            AlreadyAcquired = alreadyAcquired;
            HResult = unchecked((int)0x8013152D); // same code family as AbandonedMutexException
        }

        public string Name { get; }
        public bool AlreadyAcquired { get; }
    }
}
