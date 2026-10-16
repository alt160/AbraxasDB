using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AbraxasDB
{
    public partial class RecordBase
    {
        private const ulong CoordinationStateCatalogRecordId = 10;
        private const int CoordinationStateCapacityBytes = 64 * 1024;
        private const int CoordinationStateHeaderBytes = 64;
        private const int CoordinationStateChecksumOffset = 32;
        private const int CoordinationStateChecksumBytes = 32;
        private const int CoordinationStateMaximumEntries = 256;
        private const int CoordinationStateMaximumNameBytes = 256;
        private const int CoordinationStateFormatVersion = 1;
        private const ulong CoordinationStateMagic = 0x3154_4154_5358_4241UL;
        private readonly object _coordinationStateCatalogLock = new();

        /// <summary>
        /// Reads one namespaced, versioned coordination-state value from the RecordBase-owned Fractal catalog.<br/>
        /// Existing RecordBases lazily create and A/B-seed the fixed 64 KiB catalog on first use; a missing logical name still returns <see langword="false"/>.<br/>
        /// The returned value and metadata are detached from the persisted image and remain valid after later catalog mutations.<br/>
        /// </summary>
        /// <typeparam name="TValue">Inheto-serializable coordination value type expected by the caller.<br/></typeparam>
        /// <param name="name">Stable case-sensitive application or feature namespace.<br/></param>
        /// <param name="state">Detached value, schema version, and optimistic-concurrency generation when present.<br/></param>
        /// <returns><see langword="true"/> when the named state exists; otherwise <see langword="false"/>.<br/></returns>
        public bool TryGetCoordinationState<TValue>(
            string name,
            out RecordBaseCoordinationState<TValue>? state)
        {
            string normalizedName = ValidateCoordinationStateName(name);
            lock (_coordinationStateCatalogLock)
            {
                EnsureCoordinationStateCatalogCore();
                CoordinationStateCatalog catalog = ReadCoordinationStateCatalogCore();
                if (!catalog.Entries.TryGetValue(normalizedName, out CoordinationStateCatalogEntry? entry))
                {
                    state = null;
                    return false;
                }

                var binary = Inheto.InhetoBinary.FromStream(new BufferStream(entry.Payload.AsMemory()), null);
                TValue? value = binary.ToObject<TValue>();
                if (value is null)
                    throw new InvalidDataException($"Coordination state '{normalizedName}' restored as null.");

                state = new RecordBaseCoordinationState<TValue>(
                    normalizedName,
                    entry.SchemaVersion,
                    entry.Generation,
                    value);
                return true;
            }
        }

        /// <summary>
        /// Adds or atomically replaces one namespaced coordination-state value inside the fixed RecordBase catalog.<br/>
        /// The optional expected generation supplies compare-and-set behavior: zero requires an absent entry, while a positive value must match the current entry exactly.<br/>
        /// Catalog read/modify/write is serialized across threads and processes, followed by complete persisted readback verification before success is returned.<br/>
        /// </summary>
        /// <typeparam name="TValue">Inheto-serializable coordination value type to persist.<br/></typeparam>
        /// <param name="name">Stable case-sensitive application or feature namespace.<br/></param>
        /// <param name="schemaVersion">Positive application-owned schema version for the supplied value.<br/></param>
        /// <param name="value">Complete immutable-by-convention state-machine snapshot.<br/></param>
        /// <param name="expectedGeneration">Optional current generation, or zero to require that the name is absent.<br/></param>
        /// <returns>The persisted value together with its newly assigned generation.<br/></returns>
        public RecordBaseCoordinationState<TValue> SetCoordinationState<TValue>(
            string name,
            int schemaVersion,
            TValue value,
            long? expectedGeneration = null)
        {
            string normalizedName = ValidateCoordinationStateName(name);
            if (schemaVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(schemaVersion), "Coordination-state schema versions must be positive.");
            ArgumentNullException.ThrowIfNull(value);
            if (expectedGeneration < 0)
                throw new ArgumentOutOfRangeException(nameof(expectedGeneration), "Expected generations cannot be negative.");

            var binary = Inheto.InhetoBinary.FromObject(value, null);
            byte[] payload = binary.RawBytes.AsReadOnlySpan.ToArray();

            lock (_coordinationStateCatalogLock)
            {
                using var crossProcessGate = CreateCoordinationStateGate();
                AcquireCoordinationStateGate(crossProcessGate);
                EnsureCoordinationStateCatalogCore(crossProcessGateAlreadyHeld: true);

                CoordinationStateCatalog catalog = ReadCoordinationStateCatalogCore();
                catalog.Entries.TryGetValue(normalizedName, out CoordinationStateCatalogEntry? current);
                ValidateExpectedCoordinationGeneration(normalizedName, current?.Generation ?? 0, expectedGeneration);

                long generation = checked((current?.Generation ?? 0) + 1);
                catalog.Entries[normalizedName] = new CoordinationStateCatalogEntry(
                    schemaVersion,
                    generation,
                    payload);
                catalog.Generation = checked(catalog.Generation + 1);
                WriteCoordinationStateCatalogCore(catalog);

                CoordinationStateCatalog persisted = ReadCoordinationStateCatalogCore();
                if (!persisted.Entries.TryGetValue(normalizedName, out CoordinationStateCatalogEntry? verified) ||
                    verified.SchemaVersion != schemaVersion ||
                    verified.Generation != generation ||
                    !verified.Payload.AsSpan().SequenceEqual(payload))
                {
                    throw new InvalidDataException($"Coordination state '{normalizedName}' did not survive complete persisted readback verification.");
                }

                return new RecordBaseCoordinationState<TValue>(
                    normalizedName,
                    schemaVersion,
                    generation,
                    value);
            }
        }

        /// <summary>
        /// Removes one namespaced coordination state through the same atomic catalog replacement and compare-and-set contract used for updates.<br/>
        /// A missing name returns <see langword="false"/> unless a positive expected generation was supplied, in which case a conflict is reported.<br/>
        /// </summary>
        /// <param name="name">Stable case-sensitive application or feature namespace to remove.<br/></param>
        /// <param name="expectedGeneration">Optional current generation that must match before removal.<br/></param>
        /// <returns><see langword="true"/> when an existing entry was removed; otherwise <see langword="false"/>.<br/></returns>
        public bool RemoveCoordinationState(string name, long? expectedGeneration = null)
        {
            string normalizedName = ValidateCoordinationStateName(name);
            if (expectedGeneration < 0)
                throw new ArgumentOutOfRangeException(nameof(expectedGeneration), "Expected generations cannot be negative.");

            lock (_coordinationStateCatalogLock)
            {
                using var crossProcessGate = CreateCoordinationStateGate();
                AcquireCoordinationStateGate(crossProcessGate);
                EnsureCoordinationStateCatalogCore(crossProcessGateAlreadyHeld: true);

                CoordinationStateCatalog catalog = ReadCoordinationStateCatalogCore();
                catalog.Entries.TryGetValue(normalizedName, out CoordinationStateCatalogEntry? current);
                ValidateExpectedCoordinationGeneration(normalizedName, current?.Generation ?? 0, expectedGeneration);
                if (current is null)
                    return false;

                catalog.Entries.Remove(normalizedName);
                catalog.Generation = checked(catalog.Generation + 1);
                WriteCoordinationStateCatalogCore(catalog);

                CoordinationStateCatalog persisted = ReadCoordinationStateCatalogCore();
                if (persisted.Entries.ContainsKey(normalizedName))
                    throw new InvalidDataException($"Coordination state '{normalizedName}' remained present after persisted removal verification.");
                return true;
            }
        }

        /// <summary>
        /// Creates the reserved fixed-capacity coordination catalog for a newly created RecordBase and deliberately performs two distinct writes.<br/>
        /// The second write seeds Fractal's alternate durable mutation slot while the data file is still small, allowing later same-capacity images to ping-pong without file growth.<br/>
        /// </summary>
        private void InitializeCoordinationStateCatalogForNewRecordBase()
        {
            lock (_coordinationStateCatalogLock)
            {
                using var crossProcessGate = CreateCoordinationStateGate();
                AcquireCoordinationStateGate(crossProcessGate);
                if (TryReadCoordinationStateCatalogCore(out _))
                    return;

                WriteCoordinationStateCatalogCore(new CoordinationStateCatalog(0));
                WriteCoordinationStateCatalogCore(new CoordinationStateCatalog(1));
                CoordinationStateCatalog verified = ReadCoordinationStateCatalogCore();
                if (verified.Generation != 1 || verified.Entries.Count != 0)
                    throw new InvalidDataException("The newly seeded coordination-state catalog failed persisted verification.");
            }
        }

        /// <summary>
        /// Ensures an older RecordBase has the same two-slot fixed-capacity coordination representation as a newly created base.<br/>
        /// The caller may indicate that the cross-process catalog gate is already held to preserve one lock order during read/modify/write operations.<br/>
        /// </summary>
        /// <param name="crossProcessGateAlreadyHeld">Whether the caller already owns the coordination-state Semutex.<br/></param>
        private void EnsureCoordinationStateCatalogCore(bool crossProcessGateAlreadyHeld = false)
        {
            if (TryReadCoordinationStateCatalogCore(out _))
                return;

            if (crossProcessGateAlreadyHeld)
            {
                if (!TryReadCoordinationStateCatalogCore(out _))
                {
                    WriteCoordinationStateCatalogCore(new CoordinationStateCatalog(0));
                    WriteCoordinationStateCatalogCore(new CoordinationStateCatalog(1));
                }
                return;
            }

            using var crossProcessGate = CreateCoordinationStateGate();
            AcquireCoordinationStateGate(crossProcessGate);
            if (!TryReadCoordinationStateCatalogCore(out _))
            {
                WriteCoordinationStateCatalogCore(new CoordinationStateCatalog(0));
                WriteCoordinationStateCatalogCore(new CoordinationStateCatalog(1));
            }
        }

        /// <summary>
        /// Opens and validates the complete fixed-capacity coordination catalog from reserved Fractal record 10.<br/>
        /// </summary>
        /// <returns>The decoded mutable catalog snapshot.<br/></returns>
        private CoordinationStateCatalog ReadCoordinationStateCatalogCore()
            => TryReadCoordinationStateCatalogCore(out CoordinationStateCatalog? catalog)
                ? catalog!
                : throw new InvalidDataException("The required coordination-state catalog is missing.");

        /// <summary>
        /// Attempts to read and validate reserved Fractal record 10 without creating it.<br/>
        /// </summary>
        /// <param name="catalog">Decoded catalog when present; otherwise null.<br/></param>
        /// <returns><see langword="true"/> when the reserved record exists.<br/></returns>
        private bool TryReadCoordinationStateCatalogCore(out CoordinationStateCatalog? catalog)
        {
            if (!data.TryGetOne(CoordinationStateCatalogRecordId, out FractalKVS.Record record))
            {
                catalog = null;
                return false;
            }

            try
            {
                catalog = CoordinationStateCatalogCodec.Decode(record.Data.Span);
                return true;
            }
            finally
            {
                record.Dispose();
            }
        }

        /// <summary>
        /// Encodes and atomically replaces reserved Fractal record 10 with one complete 64 KiB coordination image.<br/>
        /// </summary>
        /// <param name="catalog">Validated catalog snapshot to persist.<br/></param>
        private void WriteCoordinationStateCatalogCore(CoordinationStateCatalog catalog)
        {
            byte[] image = CoordinationStateCatalogCodec.Encode(catalog);
            data.PutOne(CoordinationStateCatalogRecordId, image);
        }

        /// <summary>
        /// Creates the cross-process gate that prevents two RecordBase instances from losing independent catalog read/modify/write updates.<br/>
        /// Abandon detection permits a later process to recover ownership after an abnormal prior-process termination.<br/>
        /// </summary>
        /// <returns>An unacquired coordination-state Semutex.<br/></returns>
        private Semutex CreateCoordinationStateGate()
            => new($"Abraxas-CoordinationState-{data.UUID:N}", enableAbandonDetection: true);

        /// <summary>
        /// Acquires one coordination-state Semutex with bounded wait and accepts abandoned-lock recovery only when this instance received ownership.<br/>
        /// </summary>
        /// <param name="gate">Gate created for the current RecordBase identity.<br/></param>
        private static void AcquireCoordinationStateGate(Semutex gate)
        {
            try
            {
                if (!gate.WaitOne((int)TimeSpan.FromSeconds(15).TotalMilliseconds))
                    throw new TimeoutException("Timed out waiting for the RecordBase coordination-state writer gate.");
            }
            catch (AbandonedSemutexException abandoned) when (abandoned.AlreadyAcquired)
            {
                // The Semutex repaired an abandoned semaphore and transferred ownership to this instance.
            }
        }

        /// <summary>
        /// Validates a caller's optional optimistic generation against the latest persisted entry generation.<br/>
        /// </summary>
        /// <param name="name">Logical coordination-state name used in conflict diagnostics.<br/></param>
        /// <param name="actualGeneration">Current generation, or zero when absent.<br/></param>
        /// <param name="expectedGeneration">Optional required generation.<br/></param>
        private static void ValidateExpectedCoordinationGeneration(
            string name,
            long actualGeneration,
            long? expectedGeneration)
        {
            if (expectedGeneration.HasValue && expectedGeneration.Value != actualGeneration)
            {
                throw new RecordBaseCoordinationStateConflictException(
                    name,
                    expectedGeneration.Value,
                    actualGeneration);
            }
        }

        /// <summary>
        /// Normalizes and bounds one public coordination-state namespace before it participates in persisted catalog lookup.<br/>
        /// </summary>
        /// <param name="name">Caller-supplied case-sensitive namespace.<br/></param>
        /// <returns>The trimmed validated namespace.<br/></returns>
        private static string ValidateCoordinationStateName(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            string normalized = name.Trim();
            int byteCount = Encoding.UTF8.GetByteCount(normalized);
            if (byteCount > CoordinationStateMaximumNameBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(name),
                    $"Coordination-state names cannot exceed {CoordinationStateMaximumNameBytes:N0} UTF-8 bytes.");
            }
            return normalized;
        }

        /// <summary>
        /// Mutable in-process representation of the complete persisted coordination catalog.<br/>
        /// </summary>
        private sealed class CoordinationStateCatalog
        {
            /// <summary>
            /// Creates an empty catalog at the supplied physical generation.<br/>
            /// </summary>
            /// <param name="generation">Nonnegative whole-catalog generation.<br/></param>
            public CoordinationStateCatalog(long generation)
            {
                Generation = generation;
            }

            /// <summary>
            /// Gets or sets the whole-catalog physical generation used to make every persisted image distinct.<br/>
            /// </summary>
            public long Generation { get; set; }

            /// <summary>
            /// Gets the case-sensitive logical entries owned by applications and Abraxas features.<br/>
            /// </summary>
            public Dictionary<string, CoordinationStateCatalogEntry> Entries { get; } =
                new(StringComparer.Ordinal);
        }

        /// <summary>
        /// Stores one detached serialized coordination value and its application and optimistic-concurrency versions.<br/>
        /// </summary>
        /// <param name="SchemaVersion">Positive application-owned payload schema version.<br/></param>
        /// <param name="Generation">Positive logical entry generation.<br/></param>
        /// <param name="Payload">Complete Inheto binary payload.<br/></param>
        private sealed record CoordinationStateCatalogEntry(
            int SchemaVersion,
            long Generation,
            byte[] Payload);

        /// <summary>
        /// Encodes and validates the fixed-capacity catalog independently from Inheto payload semantics.<br/>
        /// </summary>
        private static class CoordinationStateCatalogCodec
        {
            /// <summary>
            /// Encodes one complete catalog into exactly 64 KiB with a used-length boundary, SHA-256 digest, and deterministic zero padding.<br/>
            /// </summary>
            /// <param name="catalog">Catalog to validate and encode.<br/></param>
            /// <returns>Exactly one fixed-capacity Fractal payload.<br/></returns>
            public static byte[] Encode(CoordinationStateCatalog catalog)
            {
                ArgumentNullException.ThrowIfNull(catalog);
                if (catalog.Generation < 0)
                    throw new InvalidDataException("Coordination-state catalog generation cannot be negative.");
                if (catalog.Entries.Count > CoordinationStateMaximumEntries)
                    throw new InvalidDataException($"Coordination-state catalog exceeds {CoordinationStateMaximumEntries:N0} entries.");

                using var payloadStream = new MemoryStream();
                using (var writer = new BinaryWriter(payloadStream, new UTF8Encoding(false, true), leaveOpen: true))
                {
                    foreach ((string name, CoordinationStateCatalogEntry entry) in catalog.Entries.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                    {
                        string normalizedName = ValidateCoordinationStateName(name);
                        if (entry.SchemaVersion <= 0 || entry.Generation <= 0)
                            throw new InvalidDataException($"Coordination state '{normalizedName}' has invalid version metadata.");
                        byte[] nameBytes = Encoding.UTF8.GetBytes(normalizedName);
                        writer.Write(nameBytes.Length);
                        writer.Write(nameBytes);
                        writer.Write(entry.SchemaVersion);
                        writer.Write(entry.Generation);
                        writer.Write(entry.Payload.Length);
                        writer.Write(entry.Payload);
                    }
                }

                int usedLength = checked(CoordinationStateHeaderBytes + (int)payloadStream.Length);
                if (usedLength > CoordinationStateCapacityBytes)
                {
                    throw new InvalidOperationException(
                        $"The coordination-state catalog requires {usedLength:N0} bytes, exceeding its fixed {CoordinationStateCapacityBytes:N0}-byte capacity. " +
                        "A versioned capacity migration is required before this state can be persisted.");
                }

                var image = new byte[CoordinationStateCapacityBytes];
                Span<byte> header = image.AsSpan(0, CoordinationStateHeaderBytes);
                BinaryPrimitives.WriteUInt64LittleEndian(header, CoordinationStateMagic);
                BinaryPrimitives.WriteInt32LittleEndian(header.Slice(8), CoordinationStateFormatVersion);
                BinaryPrimitives.WriteInt32LittleEndian(header.Slice(12), CoordinationStateCapacityBytes);
                BinaryPrimitives.WriteInt32LittleEndian(header.Slice(16), usedLength);
                BinaryPrimitives.WriteInt32LittleEndian(header.Slice(20), catalog.Entries.Count);
                BinaryPrimitives.WriteInt64LittleEndian(header.Slice(24), catalog.Generation);
                payloadStream.GetBuffer().AsSpan(0, (int)payloadStream.Length)
                    .CopyTo(image.AsSpan(CoordinationStateHeaderBytes));
                SHA256.HashData(
                    image.AsSpan(CoordinationStateHeaderBytes, usedLength - CoordinationStateHeaderBytes),
                    image.AsSpan(CoordinationStateChecksumOffset, CoordinationStateChecksumBytes));
                return image;
            }

            /// <summary>
            /// Decodes one fixed-capacity image and fails closed on incompatible format, checksum, bounds, duplicate names, trailing bytes, or nonzero padding.<br/>
            /// </summary>
            /// <param name="image">Borrowed Fractal payload for reserved record 10.<br/></param>
            /// <returns>Validated mutable catalog snapshot.<br/></returns>
            public static CoordinationStateCatalog Decode(ReadOnlySpan<byte> image)
            {
                if (image.Length != CoordinationStateCapacityBytes)
                    throw new InvalidDataException($"Coordination-state image length is {image.Length:N0}; expected {CoordinationStateCapacityBytes:N0} bytes.");
                if (BinaryPrimitives.ReadUInt64LittleEndian(image) != CoordinationStateMagic)
                    throw new InvalidDataException("Coordination-state image magic is invalid.");
                if (BinaryPrimitives.ReadInt32LittleEndian(image.Slice(8)) != CoordinationStateFormatVersion)
                    throw new InvalidDataException("Coordination-state image format version is unsupported.");
                if (BinaryPrimitives.ReadInt32LittleEndian(image.Slice(12)) != CoordinationStateCapacityBytes)
                    throw new InvalidDataException("Coordination-state image capacity metadata is invalid.");

                int usedLength = BinaryPrimitives.ReadInt32LittleEndian(image.Slice(16));
                int entryCount = BinaryPrimitives.ReadInt32LittleEndian(image.Slice(20));
                long generation = BinaryPrimitives.ReadInt64LittleEndian(image.Slice(24));
                if (usedLength < CoordinationStateHeaderBytes || usedLength > image.Length)
                    throw new InvalidDataException("Coordination-state used-length boundary is invalid.");
                if (entryCount < 0 || entryCount > CoordinationStateMaximumEntries)
                    throw new InvalidDataException("Coordination-state entry count is invalid.");
                if (generation < 0)
                    throw new InvalidDataException("Coordination-state catalog generation is invalid.");

                Span<byte> computedChecksum = stackalloc byte[CoordinationStateChecksumBytes];
                SHA256.HashData(
                    image.Slice(CoordinationStateHeaderBytes, usedLength - CoordinationStateHeaderBytes),
                    computedChecksum);
                if (!CryptographicOperations.FixedTimeEquals(
                    image.Slice(CoordinationStateChecksumOffset, CoordinationStateChecksumBytes),
                    computedChecksum))
                {
                    throw new InvalidDataException("Coordination-state payload checksum is invalid.");
                }
                if (image.Slice(usedLength).IndexOfAnyExcept((byte)0) >= 0)
                    throw new InvalidDataException("Coordination-state image contains nonzero bytes beyond its used-length boundary.");

                var catalog = new CoordinationStateCatalog(generation);
                int offset = CoordinationStateHeaderBytes;
                for (int index = 0; index < entryCount; index++)
                {
                    int nameLength = ReadInt32(image, ref offset, usedLength);
                    if (nameLength <= 0 || nameLength > CoordinationStateMaximumNameBytes)
                        throw new InvalidDataException("Coordination-state entry name length is invalid.");
                    ReadOnlySpan<byte> nameBytes = ReadBytes(image, ref offset, nameLength, usedLength);
                    string name;
                    try
                    {
                        name = new UTF8Encoding(false, true).GetString(nameBytes);
                    }
                    catch (DecoderFallbackException exception)
                    {
                        throw new InvalidDataException("Coordination-state entry name is not valid UTF-8.", exception);
                    }
                    if (!string.Equals(name, ValidateCoordinationStateName(name), StringComparison.Ordinal))
                        throw new InvalidDataException("Coordination-state entry name is not canonical.");

                    int schemaVersion = ReadInt32(image, ref offset, usedLength);
                    long entryGeneration = ReadInt64(image, ref offset, usedLength);
                    int payloadLength = ReadInt32(image, ref offset, usedLength);
                    if (schemaVersion <= 0 || entryGeneration <= 0 || payloadLength < 0)
                        throw new InvalidDataException($"Coordination state '{name}' has invalid entry metadata.");
                    byte[] payload = ReadBytes(image, ref offset, payloadLength, usedLength).ToArray();
                    if (!catalog.Entries.TryAdd(name, new CoordinationStateCatalogEntry(schemaVersion, entryGeneration, payload)))
                        throw new InvalidDataException($"Coordination state '{name}' is duplicated.");
                }
                if (offset != usedLength)
                    throw new InvalidDataException("Coordination-state image contains trailing logical bytes.");
                return catalog;
            }

            /// <summary>
            /// Reads one little-endian Int32 from a validated logical boundary and advances the caller's offset.<br/>
            /// </summary>
            private static int ReadInt32(ReadOnlySpan<byte> image, ref int offset, int usedLength)
                => BinaryPrimitives.ReadInt32LittleEndian(ReadBytes(image, ref offset, sizeof(int), usedLength));

            /// <summary>
            /// Reads one little-endian Int64 from a validated logical boundary and advances the caller's offset.<br/>
            /// </summary>
            private static long ReadInt64(ReadOnlySpan<byte> image, ref int offset, int usedLength)
                => BinaryPrimitives.ReadInt64LittleEndian(ReadBytes(image, ref offset, sizeof(long), usedLength));

            /// <summary>
            /// Borrows one bounded field from the logical image and advances the caller's offset without allocation.<br/>
            /// </summary>
            private static ReadOnlySpan<byte> ReadBytes(
                ReadOnlySpan<byte> image,
                ref int offset,
                int length,
                int usedLength)
            {
                if (length < 0 || offset < CoordinationStateHeaderBytes || offset > usedLength - length)
                    throw new InvalidDataException("Coordination-state field exceeds the used-length boundary.");
                ReadOnlySpan<byte> value = image.Slice(offset, length);
                offset += length;
                return value;
            }
        }
    }

    /// <summary>
    /// Represents one detached typed coordination-state snapshot stored by a RecordBase.<br/>
    /// </summary>
    /// <typeparam name="TValue">Application-owned state value type.<br/></typeparam>
    /// <param name="Name">Stable case-sensitive application or feature namespace.<br/></param>
    /// <param name="SchemaVersion">Positive application-owned payload schema version.<br/></param>
    /// <param name="Generation">Positive optimistic-concurrency generation assigned by Abraxas.<br/></param>
    /// <param name="Value">Detached deserialized state-machine snapshot.<br/></param>
    public sealed record RecordBaseCoordinationState<TValue>(
        string Name,
        int SchemaVersion,
        long Generation,
        TValue Value);

    /// <summary>
    /// Reports that a coordination-state compare-and-set operation observed a different current generation.<br/>
    /// </summary>
    public sealed class RecordBaseCoordinationStateConflictException : InvalidOperationException
    {
        /// <summary>
        /// Creates a conflict carrying the exact logical name and expected and actual generations.<br/>
        /// </summary>
        /// <param name="name">Conflicting coordination-state namespace.<br/></param>
        /// <param name="expectedGeneration">Generation required by the caller.<br/></param>
        /// <param name="actualGeneration">Generation observed under the writer gate, or zero when absent.<br/></param>
        public RecordBaseCoordinationStateConflictException(
            string name,
            long expectedGeneration,
            long actualGeneration)
            : base($"Coordination state '{name}' expected generation {expectedGeneration:N0}, but the current generation is {actualGeneration:N0}.")
        {
            Name = name;
            ExpectedGeneration = expectedGeneration;
            ActualGeneration = actualGeneration;
        }

        /// <summary>Gets the conflicting logical namespace.<br/></summary>
        public string Name { get; }

        /// <summary>Gets the generation required by the caller.<br/></summary>
        public long ExpectedGeneration { get; }

        /// <summary>Gets the generation observed by Abraxas, or zero when the entry was absent.<br/></summary>
        public long ActualGeneration { get; }
    }
}
