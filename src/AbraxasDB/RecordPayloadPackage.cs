using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;

namespace AbraxasDB
{
    /// <summary>
    /// Identifies the record-byte representation carried by an Abraxas transfer package.<br/>
    /// </summary>
    public enum RecordPayloadPackageRepresentation : byte
    {
        /// <summary>Complete decoded and decompressed INHETO serialization.<br/></summary>
        InhetoBinary = 1,

        /// <summary>Exact Fractal application payload, which can contain Abraxas compression or encryption envelopes.<br/></summary>
        StoredPayload = 2
    }

    /// <summary>
    /// Selects the deterministic action when an imported package's source identity already exists in the destination store.<br/>
    /// </summary>
    public enum RecordPayloadImportConflictMode
    {
        /// <summary>Reject the import before any destination mutation.<br/></summary>
        Throw = 0,

        /// <summary>Replace the existing destination record at the matching identity and replace its persistent State from the package.<br/></summary>
        Overwrite = 1,

        /// <summary>Allocate a new destination identity and persist a destination-transformed copy of the decoded package content.<br/></summary>
        ReencryptWithNewId = 2
    }

    /// <summary>
    /// Configures one RecordStore transfer-package import.<br/>
    /// </summary>
    public sealed class RecordPayloadImportOptions
    {
        /// <summary>
        /// Gets or sets the action used when the package source identity already exists in the destination store.<br/>
        /// The default rejects a collision before any destination payload, State, or index mutation.<br/>
        /// </summary>
        public RecordPayloadImportConflictMode ConflictMode { get; set; } = RecordPayloadImportConflictMode.Throw;
    }

    /// <summary>
    /// Describes one completed transfer-package import and the identity selected at the destination.<br/>
    /// </summary>
    /// <param name="SourceRecordId">Identity recorded by the exporting store.<br/></param>
    /// <param name="DestinationRecordId">Identity that now owns the imported record in the destination store.<br/></param>
    /// <param name="Representation">Representation decoded from the package.<br/></param>
    /// <param name="ConflictMode">Conflict policy applied by the import.<br/></param>
    /// <param name="OverwroteExisting">Whether an existing destination record was replaced.<br/></param>
    /// <param name="RewrittenThroughDestinationPolicy">Whether imported content was written through the destination serialization and transform policy.<br/></param>
    public readonly record struct RecordPayloadImportResult(
        ulong SourceRecordId,
        ulong DestinationRecordId,
        RecordPayloadPackageRepresentation Representation,
        RecordPayloadImportConflictMode ConflictMode,
        bool OverwroteExisting,
        bool RewrittenThroughDestinationPolicy);

    /// <summary>
    /// Encodes and validates the versioned, integrity-protected transfer envelope around exported RecordStore payload bytes.<br/>
    /// The envelope is not persisted inside Fractal and therefore never changes the exact-byte contract of stored-payload accessors.<br/>
    /// </summary>
    internal static class RecordPayloadPackageCodec
    {
        private static ReadOnlySpan<byte> Magic => "ABRXPAY01"u8;
        private const byte CurrentVersion = 1;
        private const byte HasStateFlag = 1;
        private const int SourceRecordIdOffset = 13;
        private const int SourceStoreInstanceIdOffset = 21;
        private const int StateOffset = 37;
        private const int PayloadLengthOffset = 45;
        private const int HashOffset = 49;
        private const int HashLength = 32;
        private const int HeaderLength = HashOffset + HashLength;

        /// <summary>
        /// Creates one caller-owned transfer package around the supplied exact representation bytes.<br/>
        /// The SHA-256 checksum covers fixed metadata and payload so a package cannot silently change representation, source identity, State, or content.<br/>
        /// </summary>
        /// <param name="representation">Payload representation being exported.<br/></param>
        /// <param name="sourceRecordId">Identity of the exported source record.<br/></param>
        /// <param name="sourceStoreInstanceId">Durable identity of the store that supplied the bytes.<br/></param>
        /// <param name="hasState">Whether the source record owns a persistent State value.<br/></param>
        /// <param name="state">Source persistent State when <paramref name="hasState"/> is true.<br/></param>
        /// <param name="payload">Exact bytes corresponding to <paramref name="representation"/>.<br/></param>
        /// <returns>Caller-owned transfer package beginning with <c>ABRXPAY01</c>.<br/></returns>
        internal static byte[] Encode(
            RecordPayloadPackageRepresentation representation,
            ulong sourceRecordId,
            Guid sourceStoreInstanceId,
            bool hasState,
            long state,
            ReadOnlySpan<byte> payload)
        {
            if (!Enum.IsDefined(representation))
                throw new ArgumentOutOfRangeException(nameof(representation));

            var package = new byte[checked(HeaderLength + payload.Length)];
            Span<byte> header = package.AsSpan(0, HeaderLength);
            Magic.CopyTo(header);
            header[9] = CurrentVersion;
            header[10] = (byte)representation;
            header[11] = hasState ? HasStateFlag : (byte)0;
            BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(SourceRecordIdOffset, sizeof(ulong)), sourceRecordId);
            sourceStoreInstanceId.TryWriteBytes(header.Slice(SourceStoreInstanceIdOffset, 16));
            BinaryPrimitives.WriteInt64LittleEndian(header.Slice(StateOffset, sizeof(long)), state);
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(PayloadLengthOffset, sizeof(int)), payload.Length);
            payload.CopyTo(package.AsSpan(HeaderLength));
            SHA256.HashData(package).CopyTo(package, HashOffset);
            return package;
        }

        /// <summary>
        /// Validates one transfer package and returns its owned decoded metadata and payload view.<br/>
        /// </summary>
        /// <param name="package">Complete transfer package supplied by a developer or transport.<br/></param>
        /// <returns>Validated metadata plus an owned payload view.<br/></returns>
        internal static RecordPayloadPackage Read(ReadOnlyMemory<byte> package)
        {
            if (package.Length < HeaderLength)
                throw new InvalidDataException("The RecordStore payload package is truncated.");

            byte[] owned = package.ToArray();
            ReadOnlySpan<byte> header = owned.AsSpan(0, HeaderLength);
            if (!header.Slice(0, Magic.Length).SequenceEqual(Magic))
                throw new InvalidDataException("The RecordStore payload package magic is invalid.");
            if (header[9] != CurrentVersion)
                throw new InvalidDataException($"Unsupported RecordStore payload package version '{header[9]}'.");
            if (header[12] != 0)
                throw new InvalidDataException("The RecordStore payload package contains unsupported reserved-header data.");
            RecordPayloadPackageRepresentation representation = (RecordPayloadPackageRepresentation)header[10];
            if (!Enum.IsDefined(representation))
                throw new InvalidDataException($"Unsupported RecordStore payload package representation '{header[10]}'.");
            if ((header[11] & ~HasStateFlag) != 0)
                throw new InvalidDataException("The RecordStore payload package contains unsupported flags.");

            int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(PayloadLengthOffset, sizeof(int)));
            if (payloadLength < 0 || package.Length != checked(HeaderLength + payloadLength))
                throw new InvalidDataException("The RecordStore payload package length is invalid.");

            byte[] expectedHash = header.Slice(HashOffset, HashLength).ToArray();
            owned.AsSpan(HashOffset, HashLength).Clear();
            byte[] actualHash = SHA256.HashData(owned);
            if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
                throw new InvalidDataException("The RecordStore payload package integrity hash does not match.");
            expectedHash.AsSpan().Clear();
            actualHash.AsSpan().Clear();

            return new RecordPayloadPackage(
                representation,
                BinaryPrimitives.ReadUInt64LittleEndian(header.Slice(SourceRecordIdOffset, sizeof(ulong))),
                new Guid(header.Slice(SourceStoreInstanceIdOffset, 16)),
                (header[11] & HasStateFlag) != 0,
                BinaryPrimitives.ReadInt64LittleEndian(header.Slice(StateOffset, sizeof(long))),
                owned.AsMemory(HeaderLength, payloadLength));
        }
    }

    /// <summary>
    /// Holds validated transfer metadata and an owned payload view while one import is prepared.<br/>
    /// </summary>
    internal readonly record struct RecordPayloadPackage(
        RecordPayloadPackageRepresentation Representation,
        ulong SourceRecordId,
        Guid SourceStoreInstanceId,
        bool HasState,
        long State,
        ReadOnlyMemory<byte> Payload);
}
