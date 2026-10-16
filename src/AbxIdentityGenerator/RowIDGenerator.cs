using System.Collections.Concurrent;
using System.Diagnostics;

namespace AbraxasDB
{
    using System;
    using System.IO;
    using FractalKVS; // for AtomicIntegers

    internal sealed class RowIDGenerator : IDisposable
    {
        private const int PhysicalStoreIdBitCount = 8;
        private const int PhysicalRecordIdBitCount = CompiledIdentityFormat.PhysicalRecordIdBitCount;
        private const byte MaximumStoreId = CompiledIdentityFormat.MaximumStoreID;
        private const ulong MaximumCounter = CompiledIdentityFormat.MaximumCounter;
        private readonly string _filePath;
        private readonly ulong _blockSize;
        private readonly AtomicIntegers _atomics;

        // Per-store in-memory block cache to amortize MMF atomic ops and flushes.
        private sealed class StoreBlock
        {
            public long Next;
            public long End;
            public readonly object Sync = new();
        }

        private readonly ConcurrentDictionary<byte, StoreBlock> _blocks = new();

        /// <summary>
        /// Gets the largest non-reserved store identity that this compiled edition permits.<br/>
        /// Store identity zero remains reserved for non-application Fractal records.<br/>
        /// </summary>
        public static byte MaximumStoreID => MaximumStoreId;

        /// <summary>
        /// Gets the largest record counter that this compiled edition permits for one store.<br/>
        /// </summary>
        public static ulong MaximumRecordID => MaximumCounter;

        /// <summary>
        /// Gets the initial persisted counter for a newly assigned store identity.<br/>
        /// Each compiled edition retains its own initial-counter policy without changing the physical identity layout.<br/>
        /// </summary>
        public static ulong GetNewStoreInitialCounter()
        {
            return 0;
        }

        /// <summary>
        /// Verifies that a physical identity is admitted by the compiled edition's structural format.<br/>
        /// </summary>
        /// <param name="identity">Physical Fractal/LibraDex identity under validation.<br/></param>
        /// <param name="storeID">Expected store identity.<br/></param>
        /// <returns>True when the identity belongs to the store and is representable by this edition.<br/></returns>
        public static bool IsValidRecordID(ulong identity, byte storeID)
            => CompiledIdentityFormat.IsValidPhysicalIdentity(identity, storeID);

        public RowIDGenerator(string filePath, int idBlockSize = 100)
        {
            ValidateCompiledCapacity();
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException(nameof(filePath));

            if (!filePath.EndsWith(".idgen"))
                filePath += ".idgen";

            if (idBlockSize == 0)
                throw new ArgumentOutOfRangeException(nameof(idBlockSize));

            if (idBlockSize < 10) idBlockSize = 10;

            _filePath = filePath;
            _blockSize = (ulong)idBlockSize;

            // Backing file for AtomicIntegers region. 8KB region, persisted.
            // We allow all 64-bit slots to be used; StoreID will map to slot index.
            var preserve64 = new int[256];
            for (int i = 0; i < preserve64.Length; i++) preserve64[i] = i;

            _atomics = new AtomicIntegers(_filePath, preserve64: preserve64);
        }

        // Initialize a store’s counter (lower 56 bits). Only advances if startingID > current.
        // Uses CAS to avoid races under cross-process concurrency.
        public void InitGenerator(byte storeID, ulong startingID)
        {
            ValidateStoreID(storeID);
            int slot = storeID; // 0..255

            // Lower 56 bits only
            ulong request = startingID & MaximumCounter;

            // CAS loop: advance only if request > current
            while (true)
            {
                long currentRaw = _atomics.Read64(slot);
                ulong current = unchecked((ulong)currentRaw) & MaximumCounter;

                if (request <= current)
                    break; // nothing to do

                // Attempt to set to request if still equal to currentRaw
                long desiredRaw = unchecked((long)request);
                long prev = _atomics.CompareExchange64(slot, desiredRaw, currentRaw);

                if (prev == currentRaw)
                {
                    _atomics.Flush(flushToDisk: true);
                    break; // succeeded
                }

                // else: someone updated concurrently; retry with new current
            }

            // Reset local block cache for this store
            var sb = _blocks.GetOrAdd(storeID, _ => new StoreBlock());
            lock (sb.Sync)
            {
                sb.Next = 0;
                sb.End = 0;
            }
        }

        // Returns ID with upper 8 bits = StoreID, lower 56 bits = monotonic counter.
        public ulong GetRowID(byte storeID)
        {
            ValidateStoreID(storeID);
            var sb = _blocks.GetOrAdd(storeID, _ => new StoreBlock());

            // Fast path: atomically claim one value from the currently published block.
            while (true)
            {
                long next = System.Threading.Volatile.Read(ref sb.Next);
                long observedEnd = System.Threading.Volatile.Read(ref sb.End);
                if (next >= observedEnd)
                    break;

                long claimed = next + 1;
                if (System.Threading.Interlocked.CompareExchange(ref sb.Next, claimed, next) == next)
                    return ComposeID(storeID, unchecked((ulong)claimed));
            }

            // Slow path: reserve a new block from atomic slot + flush once
            lock (sb.Sync)
            {
                while (true)
                {
                    long next = System.Threading.Volatile.Read(ref sb.Next);
                    long observedEnd = System.Threading.Volatile.Read(ref sb.End);
                    if (next >= observedEnd)
                        break;

                    long claimed = next + 1;
                    if (System.Threading.Interlocked.CompareExchange(ref sb.Next, claimed, next) == next)
                        return ComposeID(storeID, unchecked((ulong)claimed));
                }

                int slot = storeID;
                while (true)
                {
                    long currentRaw = _atomics.Read64(slot);
                    if (currentRaw < 0 || (ulong)currentRaw > MaximumCounter - _blockSize)
                    {
                        throw new OverflowException(
                            $"Store {storeID} has exhausted its {MaximumCounter:N0} available row identities.");
                    }

                    long newEnd = checked(currentRaw + (long)_blockSize);
                    if (_atomics.CompareExchange64(slot,newEnd,currentRaw) != currentRaw)
                        continue;

                    _atomics.Flush(flushToDisk: true);

                    long newStart = newEnd - (long)_blockSize + 1;
                    sb.Next = newStart;
                    System.Threading.Volatile.Write(ref sb.End, newEnd);

                    return ComposeID(storeID, unchecked((ulong)newStart));
                }
            }
        }

        private static ulong ComposeID(byte storeID, ulong lower56)
        {
            return CompiledIdentityFormat.Compose(storeID, lower56);
        }

        /// <summary>
        /// Verifies that the selected edition only narrows the established physical Fractal identity layout.<br/>
        /// </summary>
        private static void ValidateCompiledCapacity()
        {
            int storeIdBitCount = IdentityCapacity.StoreIdBitCount;
            if (storeIdBitCount < 1 || storeIdBitCount > PhysicalStoreIdBitCount)
            {
                throw new InvalidOperationException(
                    $"{nameof(IdentityCapacity.StoreIdBitCount)} must be between 1 and {PhysicalStoreIdBitCount}.");
            }

            int recordIdBitCount = IdentityCapacity.RecordIdBitCount;
            if (recordIdBitCount < 1 || recordIdBitCount > PhysicalRecordIdBitCount)
            {
                throw new InvalidOperationException(
                    $"{nameof(IdentityCapacity.RecordIdBitCount)} must be between 1 and {PhysicalRecordIdBitCount}.");
            }
        }

        /// <summary>
        /// Rejects a physical Fractal group key that lies outside the capacity compiled into this edition.<br/>
        /// </summary>
        /// <param name="storeID">Physical upper-byte Fractal group key requested for an application record.<br/></param>
        private static void ValidateStoreID(byte storeID)
        {
            if (storeID == 0 || storeID > MaximumStoreID)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(storeID),
                    storeID,
                    $"Store identities must be between 1 and {MaximumStoreID} for this compiled Abraxas edition.");
            }
        }

        public void Dispose()
        {
            _atomics.Dispose();
        }
    }
}
