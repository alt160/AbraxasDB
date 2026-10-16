using LibraDex;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Provides the shared lifecycle, conditional membership, and borrowed-byte surface for typed composite value readers.<br/>
        /// Each derived arity retains its exact CLR key types while this base owns Abraxas operation lifetime and natural-order filtering.<br/>
        /// </summary>
        public abstract class CompositeValueReaderBase : IDisposable
        {
            private readonly IDisposable operation;
            private readonly HashSet<ulong>? acceptedIdentities;
            private ulong id;
            private long ordinal = -1;
            private bool positioned;
            private bool disposed;

            /// <summary>Creates one reader around an active store operation and optional authoritative condition membership set.<br/></summary>
            /// <param name="operation">The store operation lease owned until disposal.<br/></param>
            /// <param name="acceptedIdentities">Optional exact matching identities for a condition-filtered traversal.<br/></param>
            protected CompositeValueReaderBase(IDisposable operation, HashSet<ulong>? acceptedIdentities)
            {
                this.operation = operation ?? throw new ArgumentNullException(nameof(operation));
                this.acceptedIdentities = acceptedIdentities;
            }

            /// <summary>Gets the zero-based ordinal of the current returned row.<br/></summary>
            public long Ordinal => ordinal;

            /// <summary>Gets the current Abraxas record identity after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public ulong ID => positioned ? id : throw new InvalidOperationException("The composite value reader is not positioned on a row.");

            /// <summary>
            /// Advances in natural persisted composite order.<br/>
            /// Conditional readers intersect that order with identities selected through Abraxas' ordinary authoritative condition pipeline.<br/>
            /// </summary>
            /// <returns><see langword="true"/> when current values and <see cref="ID"/> are available; otherwise <see langword="false"/>.<br/></returns>
            public bool Read()
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                positioned = false;
                while (ReadNative(out ulong candidate))
                {
                    if (acceptedIdentities is not null && !acceptedIdentities.Contains(candidate))
                        continue;
                    id = candidate;
                    ordinal++;
                    positioned = true;
                    return true;
                }
                return false;
            }

            /// <summary>Advances to the next selected composite row.<br/></summary>
            /// <returns><see langword="true"/> when current values are available; otherwise <see langword="false"/>.<br/></returns>
            public bool MoveNext() => Read();

            /// <summary>Tests whether one current composite value is a persisted logical null route.<br/></summary>
            /// <param name="ordinal">Zero-based composite value ordinal.<br/></param>
            /// <returns><see langword="true"/> for a null route; otherwise <see langword="false"/>.<br/></returns>
            public abstract bool IsNull(int ordinal);

            /// <summary>Gets exact encoded bytes for one current composite value.<br/>The span is borrowed until the next <see cref="CompositeValueReaderBase.Read()"/> or disposal.<br/></summary>
            /// <param name="ordinal">Zero-based composite value ordinal.<br/></param>
            /// <returns>The borrowed encoded bytes.<br/></returns>
            public abstract ReadOnlySpan<byte> GetRawSpan(int ordinal);

            /// <summary>Gets exact encoded bytes for one current composite value as borrowed memory.<br/>The memory is valid until the next <see cref="CompositeValueReaderBase.Read()"/> or disposal.<br/></summary>
            /// <param name="ordinal">Zero-based composite value ordinal.<br/></param>
            /// <returns>The borrowed encoded memory.<br/></returns>
            public abstract ReadOnlyMemory<byte> GetRawMem(int ordinal);

            /// <summary>Gets logical UTF-8 bytes for one current string composite value without UTF-16 allocation.<br/>The span is borrowed until the next <see cref="CompositeValueReaderBase.Read()"/> or disposal.<br/></summary>
            /// <param name="ordinal">Zero-based string composite value ordinal.<br/></param>
            /// <returns>The borrowed UTF-8 payload.<br/></returns>
            public abstract ReadOnlySpan<byte> GetUtf8Span(int ordinal);

            /// <summary>Gets logical UTF-8 bytes for one current string composite value as borrowed memory without UTF-16 allocation.<br/>The memory is valid until the next <see cref="CompositeValueReaderBase.Read()"/> or disposal.<br/></summary>
            /// <param name="ordinal">Zero-based string composite value ordinal.<br/></param>
            /// <returns>The borrowed UTF-8 payload memory.<br/></returns>
            public abstract ReadOnlyMemory<byte> GetUtf8Mem(int ordinal);

            /// <summary>Disposes the underlying page-native reader and releases the store operation lease.<br/></summary>
            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                positioned = false;
                try { DisposeNative(); }
                finally { operation.Dispose(); }
            }

            /// <summary>Advances the arity-specific page-native reader.<br/></summary>
            /// <param name="identity">The native row's current ulong identity.<br/></param>
            /// <returns><see langword="true"/> if a native row is available; otherwise <see langword="false"/>.<br/></returns>
            protected abstract bool ReadNative(out ulong identity);

            /// <summary>Disposes the arity-specific page-native reader.<br/></summary>
            protected abstract void DisposeNative();
        }

        /// <summary>
        /// Provides a strongly typed Abraxas reader over a persisted 2-part composite index.<br/>
        /// Scalar values decode from current index bytes; selecting a string or byte-array property deliberately materializes only that requested CLR value.<br/>
        /// </summary>
        /// <typeparam name="TValue1">The first persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue2">The second persisted composite value type.<br/></typeparam>
        public sealed class CompositeValueReader<TValue1, TValue2> : CompositeValueReaderBase
        {
            private readonly LibraDexCompositeReader<TValue1, TValue2, ulong> reader;

            /// <summary>Creates one reader over an already validated native composite traversal.<br/></summary>
            /// <param name="operation">The lifecycle lease retained by this reader.<br/></param>
            /// <param name="reader">The page-native typed LibraDex reader.<br/></param>
            /// <param name="acceptedIdentities">Optional authoritative condition membership set.<br/></param>
            internal CompositeValueReader(IDisposable operation, LibraDexCompositeReader<TValue1, TValue2, ulong> reader, HashSet<ulong>? acceptedIdentities)
                : base(operation, acceptedIdentities)
            {
                this.reader = reader;
            }

            /// <summary>Gets the current first composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue1 Value1 => reader.Part1;

            /// <summary>Gets the current second composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue2 Value2 => reader.Part2;

            /// <inheritdoc/>
            public override bool IsNull(int ordinal) => reader.IsNull(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetRawSpan(int ordinal) => reader.GetRawSpan(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetRawMem(int ordinal) => reader.GetRawMem(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetUtf8Span(int ordinal) => reader.GetUtf8Span(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetUtf8Mem(int ordinal) => reader.GetUtf8Mem(ordinal);

            /// <inheritdoc/>
            protected override bool ReadNative(out ulong identity)
            {
                if (!reader.Read())
                {
                    identity = default;
                    return false;
                }
                identity = reader.Identity;
                return true;
            }

            /// <inheritdoc/>
            protected override void DisposeNative() => reader.Dispose();
        }

        /// <summary>
        /// Provides a strongly typed Abraxas reader over a persisted 3-part composite index.<br/>
        /// Scalar values decode from current index bytes; selecting a string or byte-array property deliberately materializes only that requested CLR value.<br/>
        /// </summary>
        /// <typeparam name="TValue1">The first persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue2">The second persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue3">The third persisted composite value type.<br/></typeparam>
        public sealed class CompositeValueReader<TValue1, TValue2, TValue3> : CompositeValueReaderBase
        {
            private readonly LibraDexCompositeReader<TValue1, TValue2, TValue3, ulong> reader;

            /// <summary>Creates one reader over an already validated native composite traversal.<br/></summary>
            /// <param name="operation">The lifecycle lease retained by this reader.<br/></param>
            /// <param name="reader">The page-native typed LibraDex reader.<br/></param>
            /// <param name="acceptedIdentities">Optional authoritative condition membership set.<br/></param>
            internal CompositeValueReader(IDisposable operation, LibraDexCompositeReader<TValue1, TValue2, TValue3, ulong> reader, HashSet<ulong>? acceptedIdentities)
                : base(operation, acceptedIdentities)
            {
                this.reader = reader;
            }

            /// <summary>Gets the current first composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue1 Value1 => reader.Part1;

            /// <summary>Gets the current second composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue2 Value2 => reader.Part2;

            /// <summary>Gets the current third composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue3 Value3 => reader.Part3;

            /// <inheritdoc/>
            public override bool IsNull(int ordinal) => reader.IsNull(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetRawSpan(int ordinal) => reader.GetRawSpan(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetRawMem(int ordinal) => reader.GetRawMem(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetUtf8Span(int ordinal) => reader.GetUtf8Span(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetUtf8Mem(int ordinal) => reader.GetUtf8Mem(ordinal);

            /// <inheritdoc/>
            protected override bool ReadNative(out ulong identity)
            {
                if (!reader.Read())
                {
                    identity = default;
                    return false;
                }
                identity = reader.Identity;
                return true;
            }

            /// <inheritdoc/>
            protected override void DisposeNative() => reader.Dispose();
        }

        /// <summary>
        /// Provides a strongly typed Abraxas reader over a persisted 4-part composite index.<br/>
        /// Scalar values decode from current index bytes; selecting a string or byte-array property deliberately materializes only that requested CLR value.<br/>
        /// </summary>
        /// <typeparam name="TValue1">The first persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue2">The second persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue3">The third persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue4">The fourth persisted composite value type.<br/></typeparam>
        public sealed class CompositeValueReader<TValue1, TValue2, TValue3, TValue4> : CompositeValueReaderBase
        {
            private readonly LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, ulong> reader;

            /// <summary>Creates one reader over an already validated native composite traversal.<br/></summary>
            /// <param name="operation">The lifecycle lease retained by this reader.<br/></param>
            /// <param name="reader">The page-native typed LibraDex reader.<br/></param>
            /// <param name="acceptedIdentities">Optional authoritative condition membership set.<br/></param>
            internal CompositeValueReader(IDisposable operation, LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, ulong> reader, HashSet<ulong>? acceptedIdentities)
                : base(operation, acceptedIdentities)
            {
                this.reader = reader;
            }

            /// <summary>Gets the current first composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue1 Value1 => reader.Part1;

            /// <summary>Gets the current second composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue2 Value2 => reader.Part2;

            /// <summary>Gets the current third composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue3 Value3 => reader.Part3;

            /// <summary>Gets the current fourth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue4 Value4 => reader.Part4;

            /// <inheritdoc/>
            public override bool IsNull(int ordinal) => reader.IsNull(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetRawSpan(int ordinal) => reader.GetRawSpan(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetRawMem(int ordinal) => reader.GetRawMem(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetUtf8Span(int ordinal) => reader.GetUtf8Span(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetUtf8Mem(int ordinal) => reader.GetUtf8Mem(ordinal);

            /// <inheritdoc/>
            protected override bool ReadNative(out ulong identity)
            {
                if (!reader.Read())
                {
                    identity = default;
                    return false;
                }
                identity = reader.Identity;
                return true;
            }

            /// <inheritdoc/>
            protected override void DisposeNative() => reader.Dispose();
        }

        /// <summary>
        /// Provides a strongly typed Abraxas reader over a persisted 5-part composite index.<br/>
        /// Scalar values decode from current index bytes; selecting a string or byte-array property deliberately materializes only that requested CLR value.<br/>
        /// </summary>
        /// <typeparam name="TValue1">The first persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue2">The second persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue3">The third persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue4">The fourth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue5">The fifth persisted composite value type.<br/></typeparam>
        public sealed class CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5> : CompositeValueReaderBase
        {
            private readonly LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, ulong> reader;

            /// <summary>Creates one reader over an already validated native composite traversal.<br/></summary>
            /// <param name="operation">The lifecycle lease retained by this reader.<br/></param>
            /// <param name="reader">The page-native typed LibraDex reader.<br/></param>
            /// <param name="acceptedIdentities">Optional authoritative condition membership set.<br/></param>
            internal CompositeValueReader(IDisposable operation, LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, ulong> reader, HashSet<ulong>? acceptedIdentities)
                : base(operation, acceptedIdentities)
            {
                this.reader = reader;
            }

            /// <summary>Gets the current first composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue1 Value1 => reader.Part1;

            /// <summary>Gets the current second composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue2 Value2 => reader.Part2;

            /// <summary>Gets the current third composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue3 Value3 => reader.Part3;

            /// <summary>Gets the current fourth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue4 Value4 => reader.Part4;

            /// <summary>Gets the current fifth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue5 Value5 => reader.Part5;

            /// <inheritdoc/>
            public override bool IsNull(int ordinal) => reader.IsNull(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetRawSpan(int ordinal) => reader.GetRawSpan(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetRawMem(int ordinal) => reader.GetRawMem(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetUtf8Span(int ordinal) => reader.GetUtf8Span(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetUtf8Mem(int ordinal) => reader.GetUtf8Mem(ordinal);

            /// <inheritdoc/>
            protected override bool ReadNative(out ulong identity)
            {
                if (!reader.Read())
                {
                    identity = default;
                    return false;
                }
                identity = reader.Identity;
                return true;
            }

            /// <inheritdoc/>
            protected override void DisposeNative() => reader.Dispose();
        }

        /// <summary>
        /// Provides a strongly typed Abraxas reader over a persisted 6-part composite index.<br/>
        /// Scalar values decode from current index bytes; selecting a string or byte-array property deliberately materializes only that requested CLR value.<br/>
        /// </summary>
        /// <typeparam name="TValue1">The first persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue2">The second persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue3">The third persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue4">The fourth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue5">The fifth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue6">The sixth persisted composite value type.<br/></typeparam>
        public sealed class CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6> : CompositeValueReaderBase
        {
            private readonly LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, ulong> reader;

            /// <summary>Creates one reader over an already validated native composite traversal.<br/></summary>
            /// <param name="operation">The lifecycle lease retained by this reader.<br/></param>
            /// <param name="reader">The page-native typed LibraDex reader.<br/></param>
            /// <param name="acceptedIdentities">Optional authoritative condition membership set.<br/></param>
            internal CompositeValueReader(IDisposable operation, LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, ulong> reader, HashSet<ulong>? acceptedIdentities)
                : base(operation, acceptedIdentities)
            {
                this.reader = reader;
            }

            /// <summary>Gets the current first composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue1 Value1 => reader.Part1;

            /// <summary>Gets the current second composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue2 Value2 => reader.Part2;

            /// <summary>Gets the current third composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue3 Value3 => reader.Part3;

            /// <summary>Gets the current fourth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue4 Value4 => reader.Part4;

            /// <summary>Gets the current fifth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue5 Value5 => reader.Part5;

            /// <summary>Gets the current sixth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue6 Value6 => reader.Part6;

            /// <inheritdoc/>
            public override bool IsNull(int ordinal) => reader.IsNull(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetRawSpan(int ordinal) => reader.GetRawSpan(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetRawMem(int ordinal) => reader.GetRawMem(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetUtf8Span(int ordinal) => reader.GetUtf8Span(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetUtf8Mem(int ordinal) => reader.GetUtf8Mem(ordinal);

            /// <inheritdoc/>
            protected override bool ReadNative(out ulong identity)
            {
                if (!reader.Read())
                {
                    identity = default;
                    return false;
                }
                identity = reader.Identity;
                return true;
            }

            /// <inheritdoc/>
            protected override void DisposeNative() => reader.Dispose();
        }

        /// <summary>
        /// Provides a strongly typed Abraxas reader over a persisted 7-part composite index.<br/>
        /// Scalar values decode from current index bytes; selecting a string or byte-array property deliberately materializes only that requested CLR value.<br/>
        /// </summary>
        /// <typeparam name="TValue1">The first persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue2">The second persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue3">The third persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue4">The fourth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue5">The fifth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue6">The sixth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue7">The seventh persisted composite value type.<br/></typeparam>
        public sealed class CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7> : CompositeValueReaderBase
        {
            private readonly LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, ulong> reader;

            /// <summary>Creates one reader over an already validated native composite traversal.<br/></summary>
            /// <param name="operation">The lifecycle lease retained by this reader.<br/></param>
            /// <param name="reader">The page-native typed LibraDex reader.<br/></param>
            /// <param name="acceptedIdentities">Optional authoritative condition membership set.<br/></param>
            internal CompositeValueReader(IDisposable operation, LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, ulong> reader, HashSet<ulong>? acceptedIdentities)
                : base(operation, acceptedIdentities)
            {
                this.reader = reader;
            }

            /// <summary>Gets the current first composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue1 Value1 => reader.Part1;

            /// <summary>Gets the current second composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue2 Value2 => reader.Part2;

            /// <summary>Gets the current third composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue3 Value3 => reader.Part3;

            /// <summary>Gets the current fourth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue4 Value4 => reader.Part4;

            /// <summary>Gets the current fifth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue5 Value5 => reader.Part5;

            /// <summary>Gets the current sixth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue6 Value6 => reader.Part6;

            /// <summary>Gets the current seventh composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue7 Value7 => reader.Part7;

            /// <inheritdoc/>
            public override bool IsNull(int ordinal) => reader.IsNull(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetRawSpan(int ordinal) => reader.GetRawSpan(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetRawMem(int ordinal) => reader.GetRawMem(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetUtf8Span(int ordinal) => reader.GetUtf8Span(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetUtf8Mem(int ordinal) => reader.GetUtf8Mem(ordinal);

            /// <inheritdoc/>
            protected override bool ReadNative(out ulong identity)
            {
                if (!reader.Read())
                {
                    identity = default;
                    return false;
                }
                identity = reader.Identity;
                return true;
            }

            /// <inheritdoc/>
            protected override void DisposeNative() => reader.Dispose();
        }

        /// <summary>
        /// Provides a strongly typed Abraxas reader over a persisted 8-part composite index.<br/>
        /// Scalar values decode from current index bytes; selecting a string or byte-array property deliberately materializes only that requested CLR value.<br/>
        /// </summary>
        /// <typeparam name="TValue1">The first persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue2">The second persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue3">The third persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue4">The fourth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue5">The fifth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue6">The sixth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue7">The seventh persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue8">The eighth persisted composite value type.<br/></typeparam>
        public sealed class CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8> : CompositeValueReaderBase
        {
            private readonly LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, ulong> reader;

            /// <summary>Creates one reader over an already validated native composite traversal.<br/></summary>
            /// <param name="operation">The lifecycle lease retained by this reader.<br/></param>
            /// <param name="reader">The page-native typed LibraDex reader.<br/></param>
            /// <param name="acceptedIdentities">Optional authoritative condition membership set.<br/></param>
            internal CompositeValueReader(IDisposable operation, LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, ulong> reader, HashSet<ulong>? acceptedIdentities)
                : base(operation, acceptedIdentities)
            {
                this.reader = reader;
            }

            /// <summary>Gets the current first composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue1 Value1 => reader.Part1;

            /// <summary>Gets the current second composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue2 Value2 => reader.Part2;

            /// <summary>Gets the current third composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue3 Value3 => reader.Part3;

            /// <summary>Gets the current fourth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue4 Value4 => reader.Part4;

            /// <summary>Gets the current fifth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue5 Value5 => reader.Part5;

            /// <summary>Gets the current sixth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue6 Value6 => reader.Part6;

            /// <summary>Gets the current seventh composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue7 Value7 => reader.Part7;

            /// <summary>Gets the current eighth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue8 Value8 => reader.Part8;

            /// <inheritdoc/>
            public override bool IsNull(int ordinal) => reader.IsNull(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetRawSpan(int ordinal) => reader.GetRawSpan(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetRawMem(int ordinal) => reader.GetRawMem(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetUtf8Span(int ordinal) => reader.GetUtf8Span(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetUtf8Mem(int ordinal) => reader.GetUtf8Mem(ordinal);

            /// <inheritdoc/>
            protected override bool ReadNative(out ulong identity)
            {
                if (!reader.Read())
                {
                    identity = default;
                    return false;
                }
                identity = reader.Identity;
                return true;
            }

            /// <inheritdoc/>
            protected override void DisposeNative() => reader.Dispose();
        }

        /// <summary>
        /// Provides a strongly typed Abraxas reader over a persisted 9-part composite index.<br/>
        /// Scalar values decode from current index bytes; selecting a string or byte-array property deliberately materializes only that requested CLR value.<br/>
        /// </summary>
        /// <typeparam name="TValue1">The first persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue2">The second persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue3">The third persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue4">The fourth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue5">The fifth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue6">The sixth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue7">The seventh persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue8">The eighth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue9">The ninth persisted composite value type.<br/></typeparam>
        public sealed class CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9> : CompositeValueReaderBase
        {
            private readonly LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, ulong> reader;

            /// <summary>Creates one reader over an already validated native composite traversal.<br/></summary>
            /// <param name="operation">The lifecycle lease retained by this reader.<br/></param>
            /// <param name="reader">The page-native typed LibraDex reader.<br/></param>
            /// <param name="acceptedIdentities">Optional authoritative condition membership set.<br/></param>
            internal CompositeValueReader(IDisposable operation, LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, ulong> reader, HashSet<ulong>? acceptedIdentities)
                : base(operation, acceptedIdentities)
            {
                this.reader = reader;
            }

            /// <summary>Gets the current first composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue1 Value1 => reader.Part1;

            /// <summary>Gets the current second composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue2 Value2 => reader.Part2;

            /// <summary>Gets the current third composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue3 Value3 => reader.Part3;

            /// <summary>Gets the current fourth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue4 Value4 => reader.Part4;

            /// <summary>Gets the current fifth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue5 Value5 => reader.Part5;

            /// <summary>Gets the current sixth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue6 Value6 => reader.Part6;

            /// <summary>Gets the current seventh composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue7 Value7 => reader.Part7;

            /// <summary>Gets the current eighth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue8 Value8 => reader.Part8;

            /// <summary>Gets the current ninth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue9 Value9 => reader.Part9;

            /// <inheritdoc/>
            public override bool IsNull(int ordinal) => reader.IsNull(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetRawSpan(int ordinal) => reader.GetRawSpan(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetRawMem(int ordinal) => reader.GetRawMem(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetUtf8Span(int ordinal) => reader.GetUtf8Span(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetUtf8Mem(int ordinal) => reader.GetUtf8Mem(ordinal);

            /// <inheritdoc/>
            protected override bool ReadNative(out ulong identity)
            {
                if (!reader.Read())
                {
                    identity = default;
                    return false;
                }
                identity = reader.Identity;
                return true;
            }

            /// <inheritdoc/>
            protected override void DisposeNative() => reader.Dispose();
        }

        /// <summary>
        /// Provides a strongly typed Abraxas reader over a persisted 10-part composite index.<br/>
        /// Scalar values decode from current index bytes; selecting a string or byte-array property deliberately materializes only that requested CLR value.<br/>
        /// </summary>
        /// <typeparam name="TValue1">The first persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue2">The second persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue3">The third persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue4">The fourth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue5">The fifth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue6">The sixth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue7">The seventh persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue8">The eighth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue9">The ninth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue10">The tenth persisted composite value type.<br/></typeparam>
        public sealed class CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10> : CompositeValueReaderBase
        {
            private readonly LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, ulong> reader;

            /// <summary>Creates one reader over an already validated native composite traversal.<br/></summary>
            /// <param name="operation">The lifecycle lease retained by this reader.<br/></param>
            /// <param name="reader">The page-native typed LibraDex reader.<br/></param>
            /// <param name="acceptedIdentities">Optional authoritative condition membership set.<br/></param>
            internal CompositeValueReader(IDisposable operation, LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, ulong> reader, HashSet<ulong>? acceptedIdentities)
                : base(operation, acceptedIdentities)
            {
                this.reader = reader;
            }

            /// <summary>Gets the current first composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue1 Value1 => reader.Part1;

            /// <summary>Gets the current second composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue2 Value2 => reader.Part2;

            /// <summary>Gets the current third composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue3 Value3 => reader.Part3;

            /// <summary>Gets the current fourth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue4 Value4 => reader.Part4;

            /// <summary>Gets the current fifth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue5 Value5 => reader.Part5;

            /// <summary>Gets the current sixth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue6 Value6 => reader.Part6;

            /// <summary>Gets the current seventh composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue7 Value7 => reader.Part7;

            /// <summary>Gets the current eighth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue8 Value8 => reader.Part8;

            /// <summary>Gets the current ninth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue9 Value9 => reader.Part9;

            /// <summary>Gets the current tenth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue10 Value10 => reader.Part10;

            /// <inheritdoc/>
            public override bool IsNull(int ordinal) => reader.IsNull(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetRawSpan(int ordinal) => reader.GetRawSpan(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetRawMem(int ordinal) => reader.GetRawMem(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetUtf8Span(int ordinal) => reader.GetUtf8Span(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetUtf8Mem(int ordinal) => reader.GetUtf8Mem(ordinal);

            /// <inheritdoc/>
            protected override bool ReadNative(out ulong identity)
            {
                if (!reader.Read())
                {
                    identity = default;
                    return false;
                }
                identity = reader.Identity;
                return true;
            }

            /// <inheritdoc/>
            protected override void DisposeNative() => reader.Dispose();
        }

        /// <summary>
        /// Provides a strongly typed Abraxas reader over a persisted 11-part composite index.<br/>
        /// Scalar values decode from current index bytes; selecting a string or byte-array property deliberately materializes only that requested CLR value.<br/>
        /// </summary>
        /// <typeparam name="TValue1">The first persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue2">The second persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue3">The third persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue4">The fourth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue5">The fifth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue6">The sixth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue7">The seventh persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue8">The eighth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue9">The ninth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue10">The tenth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue11">The eleventh persisted composite value type.<br/></typeparam>
        public sealed class CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11> : CompositeValueReaderBase
        {
            private readonly LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, ulong> reader;

            /// <summary>Creates one reader over an already validated native composite traversal.<br/></summary>
            /// <param name="operation">The lifecycle lease retained by this reader.<br/></param>
            /// <param name="reader">The page-native typed LibraDex reader.<br/></param>
            /// <param name="acceptedIdentities">Optional authoritative condition membership set.<br/></param>
            internal CompositeValueReader(IDisposable operation, LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, ulong> reader, HashSet<ulong>? acceptedIdentities)
                : base(operation, acceptedIdentities)
            {
                this.reader = reader;
            }

            /// <summary>Gets the current first composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue1 Value1 => reader.Part1;

            /// <summary>Gets the current second composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue2 Value2 => reader.Part2;

            /// <summary>Gets the current third composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue3 Value3 => reader.Part3;

            /// <summary>Gets the current fourth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue4 Value4 => reader.Part4;

            /// <summary>Gets the current fifth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue5 Value5 => reader.Part5;

            /// <summary>Gets the current sixth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue6 Value6 => reader.Part6;

            /// <summary>Gets the current seventh composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue7 Value7 => reader.Part7;

            /// <summary>Gets the current eighth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue8 Value8 => reader.Part8;

            /// <summary>Gets the current ninth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue9 Value9 => reader.Part9;

            /// <summary>Gets the current tenth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue10 Value10 => reader.Part10;

            /// <summary>Gets the current eleventh composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue11 Value11 => reader.Part11;

            /// <inheritdoc/>
            public override bool IsNull(int ordinal) => reader.IsNull(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetRawSpan(int ordinal) => reader.GetRawSpan(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetRawMem(int ordinal) => reader.GetRawMem(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetUtf8Span(int ordinal) => reader.GetUtf8Span(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetUtf8Mem(int ordinal) => reader.GetUtf8Mem(ordinal);

            /// <inheritdoc/>
            protected override bool ReadNative(out ulong identity)
            {
                if (!reader.Read())
                {
                    identity = default;
                    return false;
                }
                identity = reader.Identity;
                return true;
            }

            /// <inheritdoc/>
            protected override void DisposeNative() => reader.Dispose();
        }

        /// <summary>
        /// Provides a strongly typed Abraxas reader over a persisted 12-part composite index.<br/>
        /// Scalar values decode from current index bytes; selecting a string or byte-array property deliberately materializes only that requested CLR value.<br/>
        /// </summary>
        /// <typeparam name="TValue1">The first persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue2">The second persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue3">The third persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue4">The fourth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue5">The fifth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue6">The sixth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue7">The seventh persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue8">The eighth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue9">The ninth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue10">The tenth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue11">The eleventh persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue12">The twelfth persisted composite value type.<br/></typeparam>
        public sealed class CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12> : CompositeValueReaderBase
        {
            private readonly LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, ulong> reader;

            /// <summary>Creates one reader over an already validated native composite traversal.<br/></summary>
            /// <param name="operation">The lifecycle lease retained by this reader.<br/></param>
            /// <param name="reader">The page-native typed LibraDex reader.<br/></param>
            /// <param name="acceptedIdentities">Optional authoritative condition membership set.<br/></param>
            internal CompositeValueReader(IDisposable operation, LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, ulong> reader, HashSet<ulong>? acceptedIdentities)
                : base(operation, acceptedIdentities)
            {
                this.reader = reader;
            }

            /// <summary>Gets the current first composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue1 Value1 => reader.Part1;

            /// <summary>Gets the current second composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue2 Value2 => reader.Part2;

            /// <summary>Gets the current third composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue3 Value3 => reader.Part3;

            /// <summary>Gets the current fourth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue4 Value4 => reader.Part4;

            /// <summary>Gets the current fifth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue5 Value5 => reader.Part5;

            /// <summary>Gets the current sixth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue6 Value6 => reader.Part6;

            /// <summary>Gets the current seventh composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue7 Value7 => reader.Part7;

            /// <summary>Gets the current eighth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue8 Value8 => reader.Part8;

            /// <summary>Gets the current ninth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue9 Value9 => reader.Part9;

            /// <summary>Gets the current tenth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue10 Value10 => reader.Part10;

            /// <summary>Gets the current eleventh composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue11 Value11 => reader.Part11;

            /// <summary>Gets the current twelfth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue12 Value12 => reader.Part12;

            /// <inheritdoc/>
            public override bool IsNull(int ordinal) => reader.IsNull(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetRawSpan(int ordinal) => reader.GetRawSpan(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetRawMem(int ordinal) => reader.GetRawMem(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetUtf8Span(int ordinal) => reader.GetUtf8Span(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetUtf8Mem(int ordinal) => reader.GetUtf8Mem(ordinal);

            /// <inheritdoc/>
            protected override bool ReadNative(out ulong identity)
            {
                if (!reader.Read())
                {
                    identity = default;
                    return false;
                }
                identity = reader.Identity;
                return true;
            }

            /// <inheritdoc/>
            protected override void DisposeNative() => reader.Dispose();
        }

        /// <summary>
        /// Provides a strongly typed Abraxas reader over a persisted 13-part composite index.<br/>
        /// Scalar values decode from current index bytes; selecting a string or byte-array property deliberately materializes only that requested CLR value.<br/>
        /// </summary>
        /// <typeparam name="TValue1">The first persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue2">The second persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue3">The third persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue4">The fourth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue5">The fifth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue6">The sixth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue7">The seventh persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue8">The eighth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue9">The ninth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue10">The tenth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue11">The eleventh persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue12">The twelfth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue13">The thirteenth persisted composite value type.<br/></typeparam>
        public sealed class CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13> : CompositeValueReaderBase
        {
            private readonly LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, ulong> reader;

            /// <summary>Creates one reader over an already validated native composite traversal.<br/></summary>
            /// <param name="operation">The lifecycle lease retained by this reader.<br/></param>
            /// <param name="reader">The page-native typed LibraDex reader.<br/></param>
            /// <param name="acceptedIdentities">Optional authoritative condition membership set.<br/></param>
            internal CompositeValueReader(IDisposable operation, LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, ulong> reader, HashSet<ulong>? acceptedIdentities)
                : base(operation, acceptedIdentities)
            {
                this.reader = reader;
            }

            /// <summary>Gets the current first composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue1 Value1 => reader.Part1;

            /// <summary>Gets the current second composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue2 Value2 => reader.Part2;

            /// <summary>Gets the current third composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue3 Value3 => reader.Part3;

            /// <summary>Gets the current fourth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue4 Value4 => reader.Part4;

            /// <summary>Gets the current fifth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue5 Value5 => reader.Part5;

            /// <summary>Gets the current sixth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue6 Value6 => reader.Part6;

            /// <summary>Gets the current seventh composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue7 Value7 => reader.Part7;

            /// <summary>Gets the current eighth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue8 Value8 => reader.Part8;

            /// <summary>Gets the current ninth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue9 Value9 => reader.Part9;

            /// <summary>Gets the current tenth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue10 Value10 => reader.Part10;

            /// <summary>Gets the current eleventh composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue11 Value11 => reader.Part11;

            /// <summary>Gets the current twelfth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue12 Value12 => reader.Part12;

            /// <summary>Gets the current thirteenth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue13 Value13 => reader.Part13;

            /// <inheritdoc/>
            public override bool IsNull(int ordinal) => reader.IsNull(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetRawSpan(int ordinal) => reader.GetRawSpan(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetRawMem(int ordinal) => reader.GetRawMem(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetUtf8Span(int ordinal) => reader.GetUtf8Span(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetUtf8Mem(int ordinal) => reader.GetUtf8Mem(ordinal);

            /// <inheritdoc/>
            protected override bool ReadNative(out ulong identity)
            {
                if (!reader.Read())
                {
                    identity = default;
                    return false;
                }
                identity = reader.Identity;
                return true;
            }

            /// <inheritdoc/>
            protected override void DisposeNative() => reader.Dispose();
        }

        /// <summary>
        /// Provides a strongly typed Abraxas reader over a persisted 14-part composite index.<br/>
        /// Scalar values decode from current index bytes; selecting a string or byte-array property deliberately materializes only that requested CLR value.<br/>
        /// </summary>
        /// <typeparam name="TValue1">The first persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue2">The second persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue3">The third persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue4">The fourth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue5">The fifth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue6">The sixth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue7">The seventh persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue8">The eighth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue9">The ninth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue10">The tenth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue11">The eleventh persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue12">The twelfth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue13">The thirteenth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue14">The fourteenth persisted composite value type.<br/></typeparam>
        public sealed class CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14> : CompositeValueReaderBase
        {
            private readonly LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, ulong> reader;

            /// <summary>Creates one reader over an already validated native composite traversal.<br/></summary>
            /// <param name="operation">The lifecycle lease retained by this reader.<br/></param>
            /// <param name="reader">The page-native typed LibraDex reader.<br/></param>
            /// <param name="acceptedIdentities">Optional authoritative condition membership set.<br/></param>
            internal CompositeValueReader(IDisposable operation, LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, ulong> reader, HashSet<ulong>? acceptedIdentities)
                : base(operation, acceptedIdentities)
            {
                this.reader = reader;
            }

            /// <summary>Gets the current first composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue1 Value1 => reader.Part1;

            /// <summary>Gets the current second composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue2 Value2 => reader.Part2;

            /// <summary>Gets the current third composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue3 Value3 => reader.Part3;

            /// <summary>Gets the current fourth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue4 Value4 => reader.Part4;

            /// <summary>Gets the current fifth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue5 Value5 => reader.Part5;

            /// <summary>Gets the current sixth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue6 Value6 => reader.Part6;

            /// <summary>Gets the current seventh composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue7 Value7 => reader.Part7;

            /// <summary>Gets the current eighth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue8 Value8 => reader.Part8;

            /// <summary>Gets the current ninth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue9 Value9 => reader.Part9;

            /// <summary>Gets the current tenth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue10 Value10 => reader.Part10;

            /// <summary>Gets the current eleventh composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue11 Value11 => reader.Part11;

            /// <summary>Gets the current twelfth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue12 Value12 => reader.Part12;

            /// <summary>Gets the current thirteenth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue13 Value13 => reader.Part13;

            /// <summary>Gets the current fourteenth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue14 Value14 => reader.Part14;

            /// <inheritdoc/>
            public override bool IsNull(int ordinal) => reader.IsNull(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetRawSpan(int ordinal) => reader.GetRawSpan(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetRawMem(int ordinal) => reader.GetRawMem(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetUtf8Span(int ordinal) => reader.GetUtf8Span(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetUtf8Mem(int ordinal) => reader.GetUtf8Mem(ordinal);

            /// <inheritdoc/>
            protected override bool ReadNative(out ulong identity)
            {
                if (!reader.Read())
                {
                    identity = default;
                    return false;
                }
                identity = reader.Identity;
                return true;
            }

            /// <inheritdoc/>
            protected override void DisposeNative() => reader.Dispose();
        }

        /// <summary>
        /// Provides a strongly typed Abraxas reader over a persisted 15-part composite index.<br/>
        /// Scalar values decode from current index bytes; selecting a string or byte-array property deliberately materializes only that requested CLR value.<br/>
        /// </summary>
        /// <typeparam name="TValue1">The first persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue2">The second persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue3">The third persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue4">The fourth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue5">The fifth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue6">The sixth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue7">The seventh persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue8">The eighth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue9">The ninth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue10">The tenth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue11">The eleventh persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue12">The twelfth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue13">The thirteenth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue14">The fourteenth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue15">The fifteenth persisted composite value type.<br/></typeparam>
        public sealed class CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15> : CompositeValueReaderBase
        {
            private readonly LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15, ulong> reader;

            /// <summary>Creates one reader over an already validated native composite traversal.<br/></summary>
            /// <param name="operation">The lifecycle lease retained by this reader.<br/></param>
            /// <param name="reader">The page-native typed LibraDex reader.<br/></param>
            /// <param name="acceptedIdentities">Optional authoritative condition membership set.<br/></param>
            internal CompositeValueReader(IDisposable operation, LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15, ulong> reader, HashSet<ulong>? acceptedIdentities)
                : base(operation, acceptedIdentities)
            {
                this.reader = reader;
            }

            /// <summary>Gets the current first composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue1 Value1 => reader.Part1;

            /// <summary>Gets the current second composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue2 Value2 => reader.Part2;

            /// <summary>Gets the current third composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue3 Value3 => reader.Part3;

            /// <summary>Gets the current fourth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue4 Value4 => reader.Part4;

            /// <summary>Gets the current fifth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue5 Value5 => reader.Part5;

            /// <summary>Gets the current sixth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue6 Value6 => reader.Part6;

            /// <summary>Gets the current seventh composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue7 Value7 => reader.Part7;

            /// <summary>Gets the current eighth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue8 Value8 => reader.Part8;

            /// <summary>Gets the current ninth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue9 Value9 => reader.Part9;

            /// <summary>Gets the current tenth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue10 Value10 => reader.Part10;

            /// <summary>Gets the current eleventh composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue11 Value11 => reader.Part11;

            /// <summary>Gets the current twelfth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue12 Value12 => reader.Part12;

            /// <summary>Gets the current thirteenth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue13 Value13 => reader.Part13;

            /// <summary>Gets the current fourteenth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue14 Value14 => reader.Part14;

            /// <summary>Gets the current fifteenth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue15 Value15 => reader.Part15;

            /// <inheritdoc/>
            public override bool IsNull(int ordinal) => reader.IsNull(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetRawSpan(int ordinal) => reader.GetRawSpan(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetRawMem(int ordinal) => reader.GetRawMem(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetUtf8Span(int ordinal) => reader.GetUtf8Span(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetUtf8Mem(int ordinal) => reader.GetUtf8Mem(ordinal);

            /// <inheritdoc/>
            protected override bool ReadNative(out ulong identity)
            {
                if (!reader.Read())
                {
                    identity = default;
                    return false;
                }
                identity = reader.Identity;
                return true;
            }

            /// <inheritdoc/>
            protected override void DisposeNative() => reader.Dispose();
        }

        /// <summary>
        /// Provides a strongly typed Abraxas reader over a persisted 16-part composite index.<br/>
        /// Scalar values decode from current index bytes; selecting a string or byte-array property deliberately materializes only that requested CLR value.<br/>
        /// </summary>
        /// <typeparam name="TValue1">The first persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue2">The second persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue3">The third persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue4">The fourth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue5">The fifth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue6">The sixth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue7">The seventh persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue8">The eighth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue9">The ninth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue10">The tenth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue11">The eleventh persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue12">The twelfth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue13">The thirteenth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue14">The fourteenth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue15">The fifteenth persisted composite value type.<br/></typeparam>
        /// <typeparam name="TValue16">The sixteenth persisted composite value type.<br/></typeparam>
        public sealed class CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15, TValue16> : CompositeValueReaderBase
        {
            private readonly LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15, TValue16, ulong> reader;

            /// <summary>Creates one reader over an already validated native composite traversal.<br/></summary>
            /// <param name="operation">The lifecycle lease retained by this reader.<br/></param>
            /// <param name="reader">The page-native typed LibraDex reader.<br/></param>
            /// <param name="acceptedIdentities">Optional authoritative condition membership set.<br/></param>
            internal CompositeValueReader(IDisposable operation, LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15, TValue16, ulong> reader, HashSet<ulong>? acceptedIdentities)
                : base(operation, acceptedIdentities)
            {
                this.reader = reader;
            }

            /// <summary>Gets the current first composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue1 Value1 => reader.Part1;

            /// <summary>Gets the current second composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue2 Value2 => reader.Part2;

            /// <summary>Gets the current third composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue3 Value3 => reader.Part3;

            /// <summary>Gets the current fourth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue4 Value4 => reader.Part4;

            /// <summary>Gets the current fifth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue5 Value5 => reader.Part5;

            /// <summary>Gets the current sixth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue6 Value6 => reader.Part6;

            /// <summary>Gets the current seventh composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue7 Value7 => reader.Part7;

            /// <summary>Gets the current eighth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue8 Value8 => reader.Part8;

            /// <summary>Gets the current ninth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue9 Value9 => reader.Part9;

            /// <summary>Gets the current tenth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue10 Value10 => reader.Part10;

            /// <summary>Gets the current eleventh composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue11 Value11 => reader.Part11;

            /// <summary>Gets the current twelfth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue12 Value12 => reader.Part12;

            /// <summary>Gets the current thirteenth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue13 Value13 => reader.Part13;

            /// <summary>Gets the current fourteenth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue14 Value14 => reader.Part14;

            /// <summary>Gets the current fifteenth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue15 Value15 => reader.Part15;

            /// <summary>Gets the current sixteenth composite value after a successful <see cref="CompositeValueReaderBase.Read()"/>.<br/></summary>
            public TValue16 Value16 => reader.Part16;

            /// <inheritdoc/>
            public override bool IsNull(int ordinal) => reader.IsNull(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetRawSpan(int ordinal) => reader.GetRawSpan(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetRawMem(int ordinal) => reader.GetRawMem(ordinal);

            /// <inheritdoc/>
            public override ReadOnlySpan<byte> GetUtf8Span(int ordinal) => reader.GetUtf8Span(ordinal);

            /// <inheritdoc/>
            public override ReadOnlyMemory<byte> GetUtf8Mem(int ordinal) => reader.GetUtf8Mem(ordinal);

            /// <inheritdoc/>
            protected override bool ReadNative(out ulong identity)
            {
                if (!reader.Read())
                {
                    identity = default;
                    return false;
                }
                identity = reader.Identity;
                return true;
            }

            /// <inheritdoc/>
            protected override void DisposeNative() => reader.Dispose();
        }

        /// <summary>Resolves a named file- or memory-backed composite index and validates each requested value type before a direct reader is opened.<br/></summary>
        /// <param name="name">The developer-defined composite index name.<br/></param>
        /// <param name="valueTypes">Exact CLR types expected for the ordered key values.<br/></param>
        /// <returns>The validated active composite index entry.<br/></returns>
        private LibraDexCompositeIndex ResolveCompositeValueReaderIndex(string name, params Type[] valueTypes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentNullException.ThrowIfNull(valueTypes);
            if (libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly && libraDexCompositeIndexes.All(static candidate => candidate.OwnerCatalog is null))
                throw new NotSupportedException("No composite index is attached to this store.");
            EnsureLibraDexConvergence(nameof(OpenCompositeValueReader));
            LibraDexCompositeIndex? entry;
            lock (libraDexIndexLock)
                entry = Array.Find(libraDexCompositeIndexes, candidate => string.Equals(candidate.LogicalName, name, StringComparison.Ordinal));
            if (entry is null)
                throw new KeyNotFoundException($"Composite index '{name}' is not registered on this store.");
            if (entry.Specs.Length != valueTypes.Length)
                throw new InvalidOperationException($"Composite index '{name}' has {entry.Specs.Length} values, but this reader declares {valueTypes.Length}.");
            for (int i = 0; i < valueTypes.Length; i++)
            {
                Type expected = NormalizeValueType(valueTypes[i]);
                if (entry.Specs[i].KeyType != expected)
                    throw new InvalidOperationException($"Composite index '{name}' value {i + 1} is '{entry.Specs[i].KeyType.FullName}', but this reader declares '{expected.FullName}'.");
            }
            return entry;
        }

        /// <summary>
        /// Resolves the exact identity group that owns a validated composite entry.<br/>
        /// Memory-only readers traverse the volatile catalog directly; persistent readers retain the bound file-backed group.<br/>
        /// </summary>
        /// <param name="entry">Validated composite index whose physical owner is required.<br/></param>
        /// <returns>The entry's owning LibraDex identity group.<br/></returns>
        private CatalogIdentityGroupIndexes GetCompositeReaderGroup(LibraDexCompositeIndex entry)
            => entry.OwnerCatalog is null
                ? libraDexIdentityIndexes ?? throw new InvalidOperationException("A persistent LibraDex identity group is not bound to this store.")
                : entry.OwnerCatalog.Indexes.IndexSet(indexGroupName);

        /// <summary>Collects exact condition membership through Abraxas' established identity reader before a differently ordered composite traversal begins.<br/></summary>
        /// <param name="where">Completed condition whose ordinary semantics remain authoritative.<br/></param>
        /// <returns>The matching identity membership set.<br/></returns>
        private HashSet<ulong> CollectCompositeValueReaderIdentities(Condition<T>.EndCondition where)
        {
            ArgumentNullException.ThrowIfNull(where);
            var identities = new HashSet<ulong>();
            using IdentityReader reader = Readers.OpenIdentities(where);
            while (reader.Read())
                identities.Add(reader.Get());
            return identities;
        }

        private CompositeValueReader<TValue1, TValue2> OpenCompositeValueReader<TValue1, TValue2>(Condition<T>.EndCondition? where, string name)
        {
            IDisposable operation = EnterStoreOperationScope();
            try
            {
                LibraDexCompositeIndex entry = ResolveCompositeValueReaderIndex(name, typeof(TValue1), typeof(TValue2));
                HashSet<ulong>? accepted = where is null ? null : CollectCompositeValueReaderIdentities(where);
                CatalogIdentityGroupIndexes group = GetCompositeReaderGroup(entry);
                LibraDexCompositeReader<TValue1, TValue2, ulong> native = group.OpenCompositeReader<TValue1, TValue2, ulong>(entry.PhysicalName);
                return new CompositeValueReader<TValue1, TValue2>(operation, native, accepted);
            }
            catch
            {
                operation.Dispose();
                throw;
            }
        }

        private CompositeValueReader<TValue1, TValue2, TValue3> OpenCompositeValueReader<TValue1, TValue2, TValue3>(Condition<T>.EndCondition? where, string name)
        {
            IDisposable operation = EnterStoreOperationScope();
            try
            {
                LibraDexCompositeIndex entry = ResolveCompositeValueReaderIndex(name, typeof(TValue1), typeof(TValue2), typeof(TValue3));
                HashSet<ulong>? accepted = where is null ? null : CollectCompositeValueReaderIdentities(where);
                CatalogIdentityGroupIndexes group = GetCompositeReaderGroup(entry);
                LibraDexCompositeReader<TValue1, TValue2, TValue3, ulong> native = group.OpenCompositeReader<TValue1, TValue2, TValue3, ulong>(entry.PhysicalName);
                return new CompositeValueReader<TValue1, TValue2, TValue3>(operation, native, accepted);
            }
            catch
            {
                operation.Dispose();
                throw;
            }
        }

        private CompositeValueReader<TValue1, TValue2, TValue3, TValue4> OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4>(Condition<T>.EndCondition? where, string name)
        {
            IDisposable operation = EnterStoreOperationScope();
            try
            {
                LibraDexCompositeIndex entry = ResolveCompositeValueReaderIndex(name, typeof(TValue1), typeof(TValue2), typeof(TValue3), typeof(TValue4));
                HashSet<ulong>? accepted = where is null ? null : CollectCompositeValueReaderIdentities(where);
                CatalogIdentityGroupIndexes group = GetCompositeReaderGroup(entry);
                LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, ulong> native = group.OpenCompositeReader<TValue1, TValue2, TValue3, TValue4, ulong>(entry.PhysicalName);
                return new CompositeValueReader<TValue1, TValue2, TValue3, TValue4>(operation, native, accepted);
            }
            catch
            {
                operation.Dispose();
                throw;
            }
        }

        private CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5> OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5>(Condition<T>.EndCondition? where, string name)
        {
            IDisposable operation = EnterStoreOperationScope();
            try
            {
                LibraDexCompositeIndex entry = ResolveCompositeValueReaderIndex(name, typeof(TValue1), typeof(TValue2), typeof(TValue3), typeof(TValue4), typeof(TValue5));
                HashSet<ulong>? accepted = where is null ? null : CollectCompositeValueReaderIdentities(where);
                CatalogIdentityGroupIndexes group = GetCompositeReaderGroup(entry);
                LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, ulong> native = group.OpenCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, ulong>(entry.PhysicalName);
                return new CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5>(operation, native, accepted);
            }
            catch
            {
                operation.Dispose();
                throw;
            }
        }

        private CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6> OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6>(Condition<T>.EndCondition? where, string name)
        {
            IDisposable operation = EnterStoreOperationScope();
            try
            {
                LibraDexCompositeIndex entry = ResolveCompositeValueReaderIndex(name, typeof(TValue1), typeof(TValue2), typeof(TValue3), typeof(TValue4), typeof(TValue5), typeof(TValue6));
                HashSet<ulong>? accepted = where is null ? null : CollectCompositeValueReaderIdentities(where);
                CatalogIdentityGroupIndexes group = GetCompositeReaderGroup(entry);
                LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, ulong> native = group.OpenCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, ulong>(entry.PhysicalName);
                return new CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6>(operation, native, accepted);
            }
            catch
            {
                operation.Dispose();
                throw;
            }
        }

        private CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7> OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7>(Condition<T>.EndCondition? where, string name)
        {
            IDisposable operation = EnterStoreOperationScope();
            try
            {
                LibraDexCompositeIndex entry = ResolveCompositeValueReaderIndex(name, typeof(TValue1), typeof(TValue2), typeof(TValue3), typeof(TValue4), typeof(TValue5), typeof(TValue6), typeof(TValue7));
                HashSet<ulong>? accepted = where is null ? null : CollectCompositeValueReaderIdentities(where);
                CatalogIdentityGroupIndexes group = GetCompositeReaderGroup(entry);
                LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, ulong> native = group.OpenCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, ulong>(entry.PhysicalName);
                return new CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7>(operation, native, accepted);
            }
            catch
            {
                operation.Dispose();
                throw;
            }
        }

        private CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8> OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8>(Condition<T>.EndCondition? where, string name)
        {
            IDisposable operation = EnterStoreOperationScope();
            try
            {
                LibraDexCompositeIndex entry = ResolveCompositeValueReaderIndex(name, typeof(TValue1), typeof(TValue2), typeof(TValue3), typeof(TValue4), typeof(TValue5), typeof(TValue6), typeof(TValue7), typeof(TValue8));
                HashSet<ulong>? accepted = where is null ? null : CollectCompositeValueReaderIdentities(where);
                CatalogIdentityGroupIndexes group = GetCompositeReaderGroup(entry);
                LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, ulong> native = group.OpenCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, ulong>(entry.PhysicalName);
                return new CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8>(operation, native, accepted);
            }
            catch
            {
                operation.Dispose();
                throw;
            }
        }

        private CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9> OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9>(Condition<T>.EndCondition? where, string name)
        {
            IDisposable operation = EnterStoreOperationScope();
            try
            {
                LibraDexCompositeIndex entry = ResolveCompositeValueReaderIndex(name, typeof(TValue1), typeof(TValue2), typeof(TValue3), typeof(TValue4), typeof(TValue5), typeof(TValue6), typeof(TValue7), typeof(TValue8), typeof(TValue9));
                HashSet<ulong>? accepted = where is null ? null : CollectCompositeValueReaderIdentities(where);
                CatalogIdentityGroupIndexes group = GetCompositeReaderGroup(entry);
                LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, ulong> native = group.OpenCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, ulong>(entry.PhysicalName);
                return new CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9>(operation, native, accepted);
            }
            catch
            {
                operation.Dispose();
                throw;
            }
        }

        private CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10> OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10>(Condition<T>.EndCondition? where, string name)
        {
            IDisposable operation = EnterStoreOperationScope();
            try
            {
                LibraDexCompositeIndex entry = ResolveCompositeValueReaderIndex(name, typeof(TValue1), typeof(TValue2), typeof(TValue3), typeof(TValue4), typeof(TValue5), typeof(TValue6), typeof(TValue7), typeof(TValue8), typeof(TValue9), typeof(TValue10));
                HashSet<ulong>? accepted = where is null ? null : CollectCompositeValueReaderIdentities(where);
                CatalogIdentityGroupIndexes group = GetCompositeReaderGroup(entry);
                LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, ulong> native = group.OpenCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, ulong>(entry.PhysicalName);
                return new CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10>(operation, native, accepted);
            }
            catch
            {
                operation.Dispose();
                throw;
            }
        }

        private CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11> OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11>(Condition<T>.EndCondition? where, string name)
        {
            IDisposable operation = EnterStoreOperationScope();
            try
            {
                LibraDexCompositeIndex entry = ResolveCompositeValueReaderIndex(name, typeof(TValue1), typeof(TValue2), typeof(TValue3), typeof(TValue4), typeof(TValue5), typeof(TValue6), typeof(TValue7), typeof(TValue8), typeof(TValue9), typeof(TValue10), typeof(TValue11));
                HashSet<ulong>? accepted = where is null ? null : CollectCompositeValueReaderIdentities(where);
                CatalogIdentityGroupIndexes group = GetCompositeReaderGroup(entry);
                LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, ulong> native = group.OpenCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, ulong>(entry.PhysicalName);
                return new CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11>(operation, native, accepted);
            }
            catch
            {
                operation.Dispose();
                throw;
            }
        }

        private CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12> OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12>(Condition<T>.EndCondition? where, string name)
        {
            IDisposable operation = EnterStoreOperationScope();
            try
            {
                LibraDexCompositeIndex entry = ResolveCompositeValueReaderIndex(name, typeof(TValue1), typeof(TValue2), typeof(TValue3), typeof(TValue4), typeof(TValue5), typeof(TValue6), typeof(TValue7), typeof(TValue8), typeof(TValue9), typeof(TValue10), typeof(TValue11), typeof(TValue12));
                HashSet<ulong>? accepted = where is null ? null : CollectCompositeValueReaderIdentities(where);
                CatalogIdentityGroupIndexes group = GetCompositeReaderGroup(entry);
                LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, ulong> native = group.OpenCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, ulong>(entry.PhysicalName);
                return new CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12>(operation, native, accepted);
            }
            catch
            {
                operation.Dispose();
                throw;
            }
        }

        private CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13> OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13>(Condition<T>.EndCondition? where, string name)
        {
            IDisposable operation = EnterStoreOperationScope();
            try
            {
                LibraDexCompositeIndex entry = ResolveCompositeValueReaderIndex(name, typeof(TValue1), typeof(TValue2), typeof(TValue3), typeof(TValue4), typeof(TValue5), typeof(TValue6), typeof(TValue7), typeof(TValue8), typeof(TValue9), typeof(TValue10), typeof(TValue11), typeof(TValue12), typeof(TValue13));
                HashSet<ulong>? accepted = where is null ? null : CollectCompositeValueReaderIdentities(where);
                CatalogIdentityGroupIndexes group = GetCompositeReaderGroup(entry);
                LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, ulong> native = group.OpenCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, ulong>(entry.PhysicalName);
                return new CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13>(operation, native, accepted);
            }
            catch
            {
                operation.Dispose();
                throw;
            }
        }

        private CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14> OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14>(Condition<T>.EndCondition? where, string name)
        {
            IDisposable operation = EnterStoreOperationScope();
            try
            {
                LibraDexCompositeIndex entry = ResolveCompositeValueReaderIndex(name, typeof(TValue1), typeof(TValue2), typeof(TValue3), typeof(TValue4), typeof(TValue5), typeof(TValue6), typeof(TValue7), typeof(TValue8), typeof(TValue9), typeof(TValue10), typeof(TValue11), typeof(TValue12), typeof(TValue13), typeof(TValue14));
                HashSet<ulong>? accepted = where is null ? null : CollectCompositeValueReaderIdentities(where);
                CatalogIdentityGroupIndexes group = GetCompositeReaderGroup(entry);
                LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, ulong> native = group.OpenCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, ulong>(entry.PhysicalName);
                return new CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14>(operation, native, accepted);
            }
            catch
            {
                operation.Dispose();
                throw;
            }
        }

        private CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15> OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15>(Condition<T>.EndCondition? where, string name)
        {
            IDisposable operation = EnterStoreOperationScope();
            try
            {
                LibraDexCompositeIndex entry = ResolveCompositeValueReaderIndex(name, typeof(TValue1), typeof(TValue2), typeof(TValue3), typeof(TValue4), typeof(TValue5), typeof(TValue6), typeof(TValue7), typeof(TValue8), typeof(TValue9), typeof(TValue10), typeof(TValue11), typeof(TValue12), typeof(TValue13), typeof(TValue14), typeof(TValue15));
                HashSet<ulong>? accepted = where is null ? null : CollectCompositeValueReaderIdentities(where);
                CatalogIdentityGroupIndexes group = GetCompositeReaderGroup(entry);
                LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15, ulong> native = group.OpenCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15, ulong>(entry.PhysicalName);
                return new CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15>(operation, native, accepted);
            }
            catch
            {
                operation.Dispose();
                throw;
            }
        }

        private CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15, TValue16> OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15, TValue16>(Condition<T>.EndCondition? where, string name)
        {
            IDisposable operation = EnterStoreOperationScope();
            try
            {
                LibraDexCompositeIndex entry = ResolveCompositeValueReaderIndex(name, typeof(TValue1), typeof(TValue2), typeof(TValue3), typeof(TValue4), typeof(TValue5), typeof(TValue6), typeof(TValue7), typeof(TValue8), typeof(TValue9), typeof(TValue10), typeof(TValue11), typeof(TValue12), typeof(TValue13), typeof(TValue14), typeof(TValue15), typeof(TValue16));
                HashSet<ulong>? accepted = where is null ? null : CollectCompositeValueReaderIdentities(where);
                CatalogIdentityGroupIndexes group = GetCompositeReaderGroup(entry);
                LibraDexCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15, TValue16, ulong> native = group.OpenCompositeReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15, TValue16, ulong>(entry.PhysicalName);
                return new CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15, TValue16>(operation, native, accepted);
            }
            catch
            {
                operation.Dispose();
                throw;
            }
        }

        public readonly partial struct ReaderOperations
        {
            /// <summary>Opens the named 2-part file- or memory-backed composite index in direct natural order.<br/>Values and <see cref="CompositeValueReaderBase.ID"/> remain page-native and do not hydrate records.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable reader positioned before its first composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2> OpenCompositeValues<TValue1, TValue2>(string name)
                => store.OpenCompositeValueReader<TValue1, TValue2>(where: null, name);

            /// <summary>Opens the named 2-part file- or memory-backed composite index filtered by a completed Abraxas condition.<br/>The condition is evaluated authoritatively before its matching identities are intersected with direct composite rows.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <param name="where">Completed condition selecting returned record identities.<br/></param>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable filtered reader positioned before its first matching composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2> OpenCompositeValues<TValue1, TValue2>(Condition<T>.EndCondition where, string name)
            {
                ArgumentNullException.ThrowIfNull(where);
                return store.OpenCompositeValueReader<TValue1, TValue2>(where, name);
            }

            /// <summary>Opens the named 3-part file- or memory-backed composite index in direct natural order.<br/>Values and <see cref="CompositeValueReaderBase.ID"/> remain page-native and do not hydrate records.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable reader positioned before its first composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3> OpenCompositeValues<TValue1, TValue2, TValue3>(string name)
                => store.OpenCompositeValueReader<TValue1, TValue2, TValue3>(where: null, name);

            /// <summary>Opens the named 3-part file- or memory-backed composite index filtered by a completed Abraxas condition.<br/>The condition is evaluated authoritatively before its matching identities are intersected with direct composite rows.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <param name="where">Completed condition selecting returned record identities.<br/></param>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable filtered reader positioned before its first matching composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3> OpenCompositeValues<TValue1, TValue2, TValue3>(Condition<T>.EndCondition where, string name)
            {
                ArgumentNullException.ThrowIfNull(where);
                return store.OpenCompositeValueReader<TValue1, TValue2, TValue3>(where, name);
            }

            /// <summary>Opens the named 4-part file- or memory-backed composite index in direct natural order.<br/>Values and <see cref="CompositeValueReaderBase.ID"/> remain page-native and do not hydrate records.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable reader positioned before its first composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4>(string name)
                => store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4>(where: null, name);

            /// <summary>Opens the named 4-part file- or memory-backed composite index filtered by a completed Abraxas condition.<br/>The condition is evaluated authoritatively before its matching identities are intersected with direct composite rows.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <param name="where">Completed condition selecting returned record identities.<br/></param>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable filtered reader positioned before its first matching composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4>(Condition<T>.EndCondition where, string name)
            {
                ArgumentNullException.ThrowIfNull(where);
                return store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4>(where, name);
            }

            /// <summary>Opens the named 5-part file- or memory-backed composite index in direct natural order.<br/>Values and <see cref="CompositeValueReaderBase.ID"/> remain page-native and do not hydrate records.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <typeparam name="TValue5">The fifth composite value type.<br/></typeparam>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable reader positioned before its first composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4, TValue5>(string name)
                => store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5>(where: null, name);

            /// <summary>Opens the named 5-part file- or memory-backed composite index filtered by a completed Abraxas condition.<br/>The condition is evaluated authoritatively before its matching identities are intersected with direct composite rows.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <typeparam name="TValue5">The fifth composite value type.<br/></typeparam>
            /// <param name="where">Completed condition selecting returned record identities.<br/></param>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable filtered reader positioned before its first matching composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4, TValue5>(Condition<T>.EndCondition where, string name)
            {
                ArgumentNullException.ThrowIfNull(where);
                return store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5>(where, name);
            }

            /// <summary>Opens the named 6-part file- or memory-backed composite index in direct natural order.<br/>Values and <see cref="CompositeValueReaderBase.ID"/> remain page-native and do not hydrate records.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <typeparam name="TValue5">The fifth composite value type.<br/></typeparam>
            /// <typeparam name="TValue6">The sixth composite value type.<br/></typeparam>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable reader positioned before its first composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6>(string name)
                => store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6>(where: null, name);

            /// <summary>Opens the named 6-part file- or memory-backed composite index filtered by a completed Abraxas condition.<br/>The condition is evaluated authoritatively before its matching identities are intersected with direct composite rows.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <typeparam name="TValue5">The fifth composite value type.<br/></typeparam>
            /// <typeparam name="TValue6">The sixth composite value type.<br/></typeparam>
            /// <param name="where">Completed condition selecting returned record identities.<br/></param>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable filtered reader positioned before its first matching composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6>(Condition<T>.EndCondition where, string name)
            {
                ArgumentNullException.ThrowIfNull(where);
                return store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6>(where, name);
            }

            /// <summary>Opens the named 7-part file- or memory-backed composite index in direct natural order.<br/>Values and <see cref="CompositeValueReaderBase.ID"/> remain page-native and do not hydrate records.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <typeparam name="TValue5">The fifth composite value type.<br/></typeparam>
            /// <typeparam name="TValue6">The sixth composite value type.<br/></typeparam>
            /// <typeparam name="TValue7">The seventh composite value type.<br/></typeparam>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable reader positioned before its first composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7>(string name)
                => store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7>(where: null, name);

            /// <summary>Opens the named 7-part file- or memory-backed composite index filtered by a completed Abraxas condition.<br/>The condition is evaluated authoritatively before its matching identities are intersected with direct composite rows.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <typeparam name="TValue5">The fifth composite value type.<br/></typeparam>
            /// <typeparam name="TValue6">The sixth composite value type.<br/></typeparam>
            /// <typeparam name="TValue7">The seventh composite value type.<br/></typeparam>
            /// <param name="where">Completed condition selecting returned record identities.<br/></param>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable filtered reader positioned before its first matching composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7>(Condition<T>.EndCondition where, string name)
            {
                ArgumentNullException.ThrowIfNull(where);
                return store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7>(where, name);
            }

            /// <summary>Opens the named 8-part file- or memory-backed composite index in direct natural order.<br/>Values and <see cref="CompositeValueReaderBase.ID"/> remain page-native and do not hydrate records.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <typeparam name="TValue5">The fifth composite value type.<br/></typeparam>
            /// <typeparam name="TValue6">The sixth composite value type.<br/></typeparam>
            /// <typeparam name="TValue7">The seventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue8">The eighth composite value type.<br/></typeparam>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable reader positioned before its first composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8>(string name)
                => store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8>(where: null, name);

            /// <summary>Opens the named 8-part file- or memory-backed composite index filtered by a completed Abraxas condition.<br/>The condition is evaluated authoritatively before its matching identities are intersected with direct composite rows.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <typeparam name="TValue5">The fifth composite value type.<br/></typeparam>
            /// <typeparam name="TValue6">The sixth composite value type.<br/></typeparam>
            /// <typeparam name="TValue7">The seventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue8">The eighth composite value type.<br/></typeparam>
            /// <param name="where">Completed condition selecting returned record identities.<br/></param>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable filtered reader positioned before its first matching composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8>(Condition<T>.EndCondition where, string name)
            {
                ArgumentNullException.ThrowIfNull(where);
                return store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8>(where, name);
            }

            /// <summary>Opens the named 9-part file- or memory-backed composite index in direct natural order.<br/>Values and <see cref="CompositeValueReaderBase.ID"/> remain page-native and do not hydrate records.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <typeparam name="TValue5">The fifth composite value type.<br/></typeparam>
            /// <typeparam name="TValue6">The sixth composite value type.<br/></typeparam>
            /// <typeparam name="TValue7">The seventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue8">The eighth composite value type.<br/></typeparam>
            /// <typeparam name="TValue9">The ninth composite value type.<br/></typeparam>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable reader positioned before its first composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9>(string name)
                => store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9>(where: null, name);

            /// <summary>Opens the named 9-part file- or memory-backed composite index filtered by a completed Abraxas condition.<br/>The condition is evaluated authoritatively before its matching identities are intersected with direct composite rows.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <typeparam name="TValue5">The fifth composite value type.<br/></typeparam>
            /// <typeparam name="TValue6">The sixth composite value type.<br/></typeparam>
            /// <typeparam name="TValue7">The seventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue8">The eighth composite value type.<br/></typeparam>
            /// <typeparam name="TValue9">The ninth composite value type.<br/></typeparam>
            /// <param name="where">Completed condition selecting returned record identities.<br/></param>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable filtered reader positioned before its first matching composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9>(Condition<T>.EndCondition where, string name)
            {
                ArgumentNullException.ThrowIfNull(where);
                return store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9>(where, name);
            }

            /// <summary>Opens the named 10-part file- or memory-backed composite index in direct natural order.<br/>Values and <see cref="CompositeValueReaderBase.ID"/> remain page-native and do not hydrate records.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <typeparam name="TValue5">The fifth composite value type.<br/></typeparam>
            /// <typeparam name="TValue6">The sixth composite value type.<br/></typeparam>
            /// <typeparam name="TValue7">The seventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue8">The eighth composite value type.<br/></typeparam>
            /// <typeparam name="TValue9">The ninth composite value type.<br/></typeparam>
            /// <typeparam name="TValue10">The tenth composite value type.<br/></typeparam>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable reader positioned before its first composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10>(string name)
                => store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10>(where: null, name);

            /// <summary>Opens the named 10-part file- or memory-backed composite index filtered by a completed Abraxas condition.<br/>The condition is evaluated authoritatively before its matching identities are intersected with direct composite rows.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <typeparam name="TValue5">The fifth composite value type.<br/></typeparam>
            /// <typeparam name="TValue6">The sixth composite value type.<br/></typeparam>
            /// <typeparam name="TValue7">The seventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue8">The eighth composite value type.<br/></typeparam>
            /// <typeparam name="TValue9">The ninth composite value type.<br/></typeparam>
            /// <typeparam name="TValue10">The tenth composite value type.<br/></typeparam>
            /// <param name="where">Completed condition selecting returned record identities.<br/></param>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable filtered reader positioned before its first matching composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10>(Condition<T>.EndCondition where, string name)
            {
                ArgumentNullException.ThrowIfNull(where);
                return store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10>(where, name);
            }

            /// <summary>Opens the named 11-part file- or memory-backed composite index in direct natural order.<br/>Values and <see cref="CompositeValueReaderBase.ID"/> remain page-native and do not hydrate records.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <typeparam name="TValue5">The fifth composite value type.<br/></typeparam>
            /// <typeparam name="TValue6">The sixth composite value type.<br/></typeparam>
            /// <typeparam name="TValue7">The seventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue8">The eighth composite value type.<br/></typeparam>
            /// <typeparam name="TValue9">The ninth composite value type.<br/></typeparam>
            /// <typeparam name="TValue10">The tenth composite value type.<br/></typeparam>
            /// <typeparam name="TValue11">The eleventh composite value type.<br/></typeparam>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable reader positioned before its first composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11>(string name)
                => store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11>(where: null, name);

            /// <summary>Opens the named 11-part file- or memory-backed composite index filtered by a completed Abraxas condition.<br/>The condition is evaluated authoritatively before its matching identities are intersected with direct composite rows.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <typeparam name="TValue5">The fifth composite value type.<br/></typeparam>
            /// <typeparam name="TValue6">The sixth composite value type.<br/></typeparam>
            /// <typeparam name="TValue7">The seventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue8">The eighth composite value type.<br/></typeparam>
            /// <typeparam name="TValue9">The ninth composite value type.<br/></typeparam>
            /// <typeparam name="TValue10">The tenth composite value type.<br/></typeparam>
            /// <typeparam name="TValue11">The eleventh composite value type.<br/></typeparam>
            /// <param name="where">Completed condition selecting returned record identities.<br/></param>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable filtered reader positioned before its first matching composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11>(Condition<T>.EndCondition where, string name)
            {
                ArgumentNullException.ThrowIfNull(where);
                return store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11>(where, name);
            }

            /// <summary>Opens the named 12-part file- or memory-backed composite index in direct natural order.<br/>Values and <see cref="CompositeValueReaderBase.ID"/> remain page-native and do not hydrate records.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <typeparam name="TValue5">The fifth composite value type.<br/></typeparam>
            /// <typeparam name="TValue6">The sixth composite value type.<br/></typeparam>
            /// <typeparam name="TValue7">The seventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue8">The eighth composite value type.<br/></typeparam>
            /// <typeparam name="TValue9">The ninth composite value type.<br/></typeparam>
            /// <typeparam name="TValue10">The tenth composite value type.<br/></typeparam>
            /// <typeparam name="TValue11">The eleventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue12">The twelfth composite value type.<br/></typeparam>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable reader positioned before its first composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12>(string name)
                => store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12>(where: null, name);

            /// <summary>Opens the named 12-part file- or memory-backed composite index filtered by a completed Abraxas condition.<br/>The condition is evaluated authoritatively before its matching identities are intersected with direct composite rows.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <typeparam name="TValue5">The fifth composite value type.<br/></typeparam>
            /// <typeparam name="TValue6">The sixth composite value type.<br/></typeparam>
            /// <typeparam name="TValue7">The seventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue8">The eighth composite value type.<br/></typeparam>
            /// <typeparam name="TValue9">The ninth composite value type.<br/></typeparam>
            /// <typeparam name="TValue10">The tenth composite value type.<br/></typeparam>
            /// <typeparam name="TValue11">The eleventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue12">The twelfth composite value type.<br/></typeparam>
            /// <param name="where">Completed condition selecting returned record identities.<br/></param>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable filtered reader positioned before its first matching composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12>(Condition<T>.EndCondition where, string name)
            {
                ArgumentNullException.ThrowIfNull(where);
                return store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12>(where, name);
            }

            /// <summary>Opens the named 13-part file- or memory-backed composite index in direct natural order.<br/>Values and <see cref="CompositeValueReaderBase.ID"/> remain page-native and do not hydrate records.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <typeparam name="TValue5">The fifth composite value type.<br/></typeparam>
            /// <typeparam name="TValue6">The sixth composite value type.<br/></typeparam>
            /// <typeparam name="TValue7">The seventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue8">The eighth composite value type.<br/></typeparam>
            /// <typeparam name="TValue9">The ninth composite value type.<br/></typeparam>
            /// <typeparam name="TValue10">The tenth composite value type.<br/></typeparam>
            /// <typeparam name="TValue11">The eleventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue12">The twelfth composite value type.<br/></typeparam>
            /// <typeparam name="TValue13">The thirteenth composite value type.<br/></typeparam>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable reader positioned before its first composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13>(string name)
                => store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13>(where: null, name);

            /// <summary>Opens the named 13-part file- or memory-backed composite index filtered by a completed Abraxas condition.<br/>The condition is evaluated authoritatively before its matching identities are intersected with direct composite rows.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <typeparam name="TValue5">The fifth composite value type.<br/></typeparam>
            /// <typeparam name="TValue6">The sixth composite value type.<br/></typeparam>
            /// <typeparam name="TValue7">The seventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue8">The eighth composite value type.<br/></typeparam>
            /// <typeparam name="TValue9">The ninth composite value type.<br/></typeparam>
            /// <typeparam name="TValue10">The tenth composite value type.<br/></typeparam>
            /// <typeparam name="TValue11">The eleventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue12">The twelfth composite value type.<br/></typeparam>
            /// <typeparam name="TValue13">The thirteenth composite value type.<br/></typeparam>
            /// <param name="where">Completed condition selecting returned record identities.<br/></param>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable filtered reader positioned before its first matching composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13>(Condition<T>.EndCondition where, string name)
            {
                ArgumentNullException.ThrowIfNull(where);
                return store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13>(where, name);
            }

            /// <summary>Opens the named 14-part file- or memory-backed composite index in direct natural order.<br/>Values and <see cref="CompositeValueReaderBase.ID"/> remain page-native and do not hydrate records.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <typeparam name="TValue5">The fifth composite value type.<br/></typeparam>
            /// <typeparam name="TValue6">The sixth composite value type.<br/></typeparam>
            /// <typeparam name="TValue7">The seventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue8">The eighth composite value type.<br/></typeparam>
            /// <typeparam name="TValue9">The ninth composite value type.<br/></typeparam>
            /// <typeparam name="TValue10">The tenth composite value type.<br/></typeparam>
            /// <typeparam name="TValue11">The eleventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue12">The twelfth composite value type.<br/></typeparam>
            /// <typeparam name="TValue13">The thirteenth composite value type.<br/></typeparam>
            /// <typeparam name="TValue14">The fourteenth composite value type.<br/></typeparam>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable reader positioned before its first composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14>(string name)
                => store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14>(where: null, name);

            /// <summary>Opens the named 14-part file- or memory-backed composite index filtered by a completed Abraxas condition.<br/>The condition is evaluated authoritatively before its matching identities are intersected with direct composite rows.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <typeparam name="TValue5">The fifth composite value type.<br/></typeparam>
            /// <typeparam name="TValue6">The sixth composite value type.<br/></typeparam>
            /// <typeparam name="TValue7">The seventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue8">The eighth composite value type.<br/></typeparam>
            /// <typeparam name="TValue9">The ninth composite value type.<br/></typeparam>
            /// <typeparam name="TValue10">The tenth composite value type.<br/></typeparam>
            /// <typeparam name="TValue11">The eleventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue12">The twelfth composite value type.<br/></typeparam>
            /// <typeparam name="TValue13">The thirteenth composite value type.<br/></typeparam>
            /// <typeparam name="TValue14">The fourteenth composite value type.<br/></typeparam>
            /// <param name="where">Completed condition selecting returned record identities.<br/></param>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable filtered reader positioned before its first matching composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14>(Condition<T>.EndCondition where, string name)
            {
                ArgumentNullException.ThrowIfNull(where);
                return store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14>(where, name);
            }

            /// <summary>Opens the named 15-part file- or memory-backed composite index in direct natural order.<br/>Values and <see cref="CompositeValueReaderBase.ID"/> remain page-native and do not hydrate records.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <typeparam name="TValue5">The fifth composite value type.<br/></typeparam>
            /// <typeparam name="TValue6">The sixth composite value type.<br/></typeparam>
            /// <typeparam name="TValue7">The seventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue8">The eighth composite value type.<br/></typeparam>
            /// <typeparam name="TValue9">The ninth composite value type.<br/></typeparam>
            /// <typeparam name="TValue10">The tenth composite value type.<br/></typeparam>
            /// <typeparam name="TValue11">The eleventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue12">The twelfth composite value type.<br/></typeparam>
            /// <typeparam name="TValue13">The thirteenth composite value type.<br/></typeparam>
            /// <typeparam name="TValue14">The fourteenth composite value type.<br/></typeparam>
            /// <typeparam name="TValue15">The fifteenth composite value type.<br/></typeparam>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable reader positioned before its first composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15>(string name)
                => store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15>(where: null, name);

            /// <summary>Opens the named 15-part file- or memory-backed composite index filtered by a completed Abraxas condition.<br/>The condition is evaluated authoritatively before its matching identities are intersected with direct composite rows.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <typeparam name="TValue5">The fifth composite value type.<br/></typeparam>
            /// <typeparam name="TValue6">The sixth composite value type.<br/></typeparam>
            /// <typeparam name="TValue7">The seventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue8">The eighth composite value type.<br/></typeparam>
            /// <typeparam name="TValue9">The ninth composite value type.<br/></typeparam>
            /// <typeparam name="TValue10">The tenth composite value type.<br/></typeparam>
            /// <typeparam name="TValue11">The eleventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue12">The twelfth composite value type.<br/></typeparam>
            /// <typeparam name="TValue13">The thirteenth composite value type.<br/></typeparam>
            /// <typeparam name="TValue14">The fourteenth composite value type.<br/></typeparam>
            /// <typeparam name="TValue15">The fifteenth composite value type.<br/></typeparam>
            /// <param name="where">Completed condition selecting returned record identities.<br/></param>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable filtered reader positioned before its first matching composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15>(Condition<T>.EndCondition where, string name)
            {
                ArgumentNullException.ThrowIfNull(where);
                return store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15>(where, name);
            }

            /// <summary>Opens the named 16-part file- or memory-backed composite index in direct natural order.<br/>Values and <see cref="CompositeValueReaderBase.ID"/> remain page-native and do not hydrate records.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <typeparam name="TValue5">The fifth composite value type.<br/></typeparam>
            /// <typeparam name="TValue6">The sixth composite value type.<br/></typeparam>
            /// <typeparam name="TValue7">The seventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue8">The eighth composite value type.<br/></typeparam>
            /// <typeparam name="TValue9">The ninth composite value type.<br/></typeparam>
            /// <typeparam name="TValue10">The tenth composite value type.<br/></typeparam>
            /// <typeparam name="TValue11">The eleventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue12">The twelfth composite value type.<br/></typeparam>
            /// <typeparam name="TValue13">The thirteenth composite value type.<br/></typeparam>
            /// <typeparam name="TValue14">The fourteenth composite value type.<br/></typeparam>
            /// <typeparam name="TValue15">The fifteenth composite value type.<br/></typeparam>
            /// <typeparam name="TValue16">The sixteenth composite value type.<br/></typeparam>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable reader positioned before its first composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15, TValue16> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15, TValue16>(string name)
                => store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15, TValue16>(where: null, name);

            /// <summary>Opens the named 16-part file- or memory-backed composite index filtered by a completed Abraxas condition.<br/>The condition is evaluated authoritatively before its matching identities are intersected with direct composite rows.<br/></summary>
            /// <typeparam name="TValue1">The first composite value type.<br/></typeparam>
            /// <typeparam name="TValue2">The second composite value type.<br/></typeparam>
            /// <typeparam name="TValue3">The third composite value type.<br/></typeparam>
            /// <typeparam name="TValue4">The fourth composite value type.<br/></typeparam>
            /// <typeparam name="TValue5">The fifth composite value type.<br/></typeparam>
            /// <typeparam name="TValue6">The sixth composite value type.<br/></typeparam>
            /// <typeparam name="TValue7">The seventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue8">The eighth composite value type.<br/></typeparam>
            /// <typeparam name="TValue9">The ninth composite value type.<br/></typeparam>
            /// <typeparam name="TValue10">The tenth composite value type.<br/></typeparam>
            /// <typeparam name="TValue11">The eleventh composite value type.<br/></typeparam>
            /// <typeparam name="TValue12">The twelfth composite value type.<br/></typeparam>
            /// <typeparam name="TValue13">The thirteenth composite value type.<br/></typeparam>
            /// <typeparam name="TValue14">The fourteenth composite value type.<br/></typeparam>
            /// <typeparam name="TValue15">The fifteenth composite value type.<br/></typeparam>
            /// <typeparam name="TValue16">The sixteenth composite value type.<br/></typeparam>
            /// <param name="where">Completed condition selecting returned record identities.<br/></param>
            /// <param name="name">The developer-defined file- or memory-backed composite index name.<br/></param>
            /// <returns>A disposable filtered reader positioned before its first matching composite row.<br/></returns>
            public CompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15, TValue16> OpenCompositeValues<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15, TValue16>(Condition<T>.EndCondition where, string name)
            {
                ArgumentNullException.ThrowIfNull(where);
                return store.OpenCompositeValueReader<TValue1, TValue2, TValue3, TValue4, TValue5, TValue6, TValue7, TValue8, TValue9, TValue10, TValue11, TValue12, TValue13, TValue14, TValue15, TValue16>(where, name);
            }

        }
    }
}
