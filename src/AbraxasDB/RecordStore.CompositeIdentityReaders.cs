using LibraDex;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Reads only record identities from a named composite index in natural composite order.<br/>
        /// Each advance follows the index's encoded key routes without decoding or allocating CLR key parts.<br/>
        /// A conditional reader first collects authoritative Abraxas matches, then intersects them with this index's population and order.<br/>
        /// </summary>
        public sealed class CompositeIdentityReader : IDisposable
        {
            private readonly IDisposable operation;
            private readonly LibraDexCompositeIdentityReader<ulong> reader;
            private readonly HashSet<ulong>? acceptedIdentities;
            private ulong id;
            private long ordinal = -1;
            private bool positioned;
            private bool disposed;

            /// <summary>Creates a reader from an active store lease, native cursor, and optional exact condition membership.<br/></summary>
            /// <param name="operation">The store operation lease retained until disposal.<br/></param>
            /// <param name="reader">The native identity-only composite reader.<br/></param>
            /// <param name="acceptedIdentities">Optional authoritative condition membership set.<br/></param>
            internal CompositeIdentityReader(IDisposable operation, LibraDexCompositeIdentityReader<ulong> reader, HashSet<ulong>? acceptedIdentities)
            {
                this.operation = operation ?? throw new ArgumentNullException(nameof(operation));
                this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
                this.acceptedIdentities = acceptedIdentities;
            }

            /// <summary>Gets the zero-based ordinal among identities returned by this reader.<br/></summary>
            public long Ordinal => ordinal;

            /// <summary>Gets the current record identity after a successful <see cref="Read"/>.<br/></summary>
            public ulong ID => positioned ? id : throw new InvalidOperationException("The composite identity reader is not positioned on a row.");

            /// <summary>Gets the current record identity after a successful <see cref="Read"/>.<br/></summary>
            /// <returns>The current ulong record identity.<br/></returns>
            public ulong Get() => ID;

            /// <summary>Advances to the next matching identity in natural composite order.<br/>No key part is materialized; conditional membership lookup does not decode key parts either.<br/></summary>
            /// <returns><see langword="true"/> when <see cref="ID"/> is available; otherwise <see langword="false"/>.<br/></returns>
            public bool Read()
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                positioned = false;
                while (reader.Read())
                {
                    ulong candidate = reader.Identity;
                    if (acceptedIdentities is not null && !acceptedIdentities.Contains(candidate))
                        continue;
                    id = candidate;
                    ordinal++;
                    positioned = true;
                    return true;
                }
                return false;
            }

            /// <summary>Advances to the next matching composite-index identity.<br/></summary>
            /// <returns><see langword="true"/> when a current identity is available; otherwise <see langword="false"/>.<br/></returns>
            public bool MoveNext() => Read();

            /// <summary>Disposes the native page cursor and releases the store operation lease.<br/></summary>
            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                positioned = false;
                try { reader.Dispose(); }
                finally { operation.Dispose(); }
            }
        }

        /// <summary>Opens a named composite index for identity-only traversal with optional authoritative condition filtering.<br/></summary>
        /// <param name="where">Optional completed Abraxas condition used for exact membership.<br/></param>
        /// <param name="name">The logical file- or memory-backed composite index name.<br/></param>
        /// <returns>An unpositioned identity-only reader owned by the caller.<br/></returns>
        private CompositeIdentityReader OpenCompositeIdentityReader(Condition<T>.EndCondition? where, string name)
        {
            IDisposable operation = EnterStoreOperationScope();
            try
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(name);
                EnsureLibraDexConvergence(nameof(OpenCompositeIdentityReader));
                LibraDexCompositeIndex? entry;
                lock (libraDexIndexLock)
                    entry = Array.Find(libraDexCompositeIndexes, candidate => string.Equals(candidate.LogicalName, name, StringComparison.Ordinal));
                if (entry is null)
                    throw new KeyNotFoundException($"Composite index '{name}' is not registered on this store.");
                HashSet<ulong>? accepted = where is null ? null : CollectCompositeValueReaderIdentities(where);
                CatalogIdentityGroupIndexes group = GetCompositeReaderGroup(entry);
                LibraDexCompositeIdentityReader<ulong> native = group.OpenCompositeIdentityReader<ulong>(entry.PhysicalName);
                return new CompositeIdentityReader(operation, native, accepted);
            }
            catch
            {
                operation.Dispose();
                throw;
            }
        }

        public readonly partial struct ReaderOperations
        {
            /// <summary>Opens the named file- or memory-backed composite index for identity-only traversal in natural key order.<br/>Key parts are neither decoded nor materialized, and records are not hydrated.<br/></summary>
            /// <param name="name">The logical composite index name.<br/></param>
            /// <returns>A disposable reader positioned before its first identity.<br/></returns>
            public CompositeIdentityReader OpenCompositeIDs(string name)
                => store.OpenCompositeIdentityReader(where: null, name);

            /// <summary>Opens only the identities matching a completed condition, ordered by the named composite index.<br/>The condition is evaluated through Abraxas's authoritative pipeline before intersection; this setup may allocate a membership set or access record payloads, but traversal never materializes key parts.<br/></summary>
            /// <param name="where">Completed condition selecting returned record identities.<br/></param>
            /// <param name="name">The logical composite index name.<br/></param>
            /// <returns>A disposable reader positioned before its first matching identity.<br/></returns>
            public CompositeIdentityReader OpenCompositeIDs(Condition<T>.EndCondition where, string name)
            {
                ArgumentNullException.ThrowIfNull(where);
                return store.OpenCompositeIdentityReader(where, name);
            }
        }
    }
}
