using System.Security.Cryptography;
using System.Text;

namespace AbraxasDB
{
    /// <summary>
    /// Selects how an Abraxas reader resumes from a detached bookmark.<br/>
    /// </summary>
    public enum BookmarkConsistency
    {
        /// <summary>
        /// Requires the authoritative Fractal store USN captured by the bookmark to remain unchanged.<br/>
        /// This is the safe default because it resumes the same logical generation by ordinal.<br/>
        /// </summary>
        GenerationBound = 0,

        /// <summary>
        /// Re-evaluates the current result stream and continues after the captured logical identity anchor.<br/>
        /// This mode requires deterministic ordering and requires the anchor to remain present.<br/>
        /// </summary>
        LiveContinuation = 1
    }

    /// <summary>
    /// Identifies a stable Abraxas bookmark-contract failure without exposing Fractal or LibraDex exception vocabulary.<br/>
    /// </summary>
    public enum RecordBookmarkFailureKind
    {
        /// <summary>The serialized token is corrupt, truncated, or structurally invalid.<br/></summary>
        InvalidToken,
        /// <summary>The bookmark belongs to another store generation.<br/></summary>
        StoreMismatch,
        /// <summary>The bookmark was created for a different condition or State selection.<br/></summary>
        SelectionMismatch,
        /// <summary>The bookmark was created for a different object or property result shape.<br/></summary>
        ResultShapeMismatch,
        /// <summary>The bookmark was created for a different ordering contract.<br/></summary>
        OrderingMismatch,
        /// <summary>The authoritative store changed after a generation-bound bookmark was captured.<br/></summary>
        GenerationChanged,
        /// <summary>The requested live continuation does not have deterministic ordering.<br/></summary>
        UnstableContinuation,
        /// <summary>The live-continuation anchor no longer exists in the current result stream.<br/></summary>
        AnchorUnavailable,
        /// <summary>The query contains runtime application code or another shape without durable bookmark identity.<br/></summary>
        UnsupportedQuery
    }

    /// <summary>
    /// Reports an Abraxas-owned bookmark validation or continuation failure.<br/>
    /// </summary>
    public sealed class RecordBookmarkException : InvalidOperationException
    {
        /// <summary>
        /// Creates a typed bookmark failure.<br/>
        /// </summary>
        /// <param name="kind">Stable failure classification.<br/></param>
        /// <param name="message">Developer-facing explanation.<br/></param>
        /// <param name="innerException">Optional lower-level parse or validation failure.<br/></param>
        public RecordBookmarkException(
            RecordBookmarkFailureKind kind,
            string message,
            Exception? innerException = null)
            : base(message, innerException)
        {
            Kind = kind;
        }

        /// <summary>Gets the stable bookmark failure classification.<br/></summary>
        public RecordBookmarkFailureKind Kind { get; }
    }

    /// <summary>
    /// Represents an opaque, detached continuation point for an Abraxas record reader.<br/>
    /// The developer supplies the original query again when resuming; Abraxas validates its semantic selection, ordering, and result shape before reading.<br/>
    /// Binary and Base64URL forms are versioned and corruption-detecting but are not encrypted or authenticated for an untrusted boundary.<br/>
    /// </summary>
    public sealed class RecordBookmark
    {
        private static readonly byte[] Magic = "ABRB"u8.ToArray();
        private const int CurrentVersion = 1;
        private const int DigestLength = 32;
        private const int MaximumTokenLength = 64 * 1024;

        internal RecordBookmark(
            Guid storeInstanceId,
            byte storeId,
            string recordTypeIdentity,
            string selectionFingerprint,
            string resultShapeFingerprint,
            string orderingFingerprint,
            ulong sourceUsn,
            long resultsConsumed,
            ulong? lastIdentity,
            bool deterministicOrdering,
            BookmarkConsistency consistency,
            DateTimeOffset capturedAtUtc)
        {
            if (storeInstanceId == Guid.Empty)
                throw new ArgumentException("Store instance identity cannot be empty.", nameof(storeInstanceId));
            if (storeId == 0)
                throw new ArgumentOutOfRangeException(nameof(storeId), "Store identifier cannot be zero.");
            if (resultsConsumed < 0)
                throw new ArgumentOutOfRangeException(nameof(resultsConsumed));
            if (consistency is not (BookmarkConsistency.GenerationBound or BookmarkConsistency.LiveContinuation))
                throw new ArgumentOutOfRangeException(nameof(consistency));

            StoreInstanceId = storeInstanceId;
            StoreId = storeId;
            RecordTypeIdentity = RequireText(recordTypeIdentity, nameof(recordTypeIdentity));
            SelectionFingerprint = RequireText(selectionFingerprint, nameof(selectionFingerprint));
            ResultShapeFingerprint = RequireText(resultShapeFingerprint, nameof(resultShapeFingerprint));
            OrderingFingerprint = RequireText(orderingFingerprint, nameof(orderingFingerprint));
            SourceUsn = sourceUsn;
            ResultsConsumed = resultsConsumed;
            LastIdentity = lastIdentity;
            DeterministicOrdering = deterministicOrdering;
            Consistency = consistency;
            CapturedAtUtc = capturedAtUtc;
        }

        /// <summary>Gets the current serialized bookmark format version.<br/></summary>
        public int FormatVersion => CurrentVersion;

        /// <summary>Gets the number of logical results consumed before the bookmark's next unread result.<br/></summary>
        public long ResultsConsumed { get; }

        /// <summary>Gets the continuation policy under which this bookmark was most recently captured or resumed.<br/></summary>
        public BookmarkConsistency Consistency { get; }

        /// <summary>Gets when this detached bookmark snapshot was captured in UTC.<br/></summary>
        public DateTimeOffset CapturedAtUtc { get; }

        internal Guid StoreInstanceId { get; }
        internal byte StoreId { get; }
        internal string RecordTypeIdentity { get; }
        internal string SelectionFingerprint { get; }
        internal string ResultShapeFingerprint { get; }
        internal string OrderingFingerprint { get; }
        internal ulong SourceUsn { get; }
        internal ulong? LastIdentity { get; }
        internal bool DeterministicOrdering { get; }

        /// <summary>
        /// Serializes this bookmark into a caller-owned, versioned binary token with an appended SHA-256 corruption digest.<br/>
        /// The token can contain opaque ordering-anchor material and should be protected by the application when crossing an untrusted boundary.<br/>
        /// </summary>
        /// <returns>Caller-owned bookmark bytes.<br/></returns>
        public byte[] ToBytes()
        {
            using var payload = new MemoryStream();
            using (var writer = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Magic);
                writer.Write(CurrentVersion);
                writer.Write(StoreInstanceId.ToByteArray());
                writer.Write(StoreId);
                WriteText(writer, RecordTypeIdentity);
                WriteText(writer, SelectionFingerprint);
                WriteText(writer, ResultShapeFingerprint);
                WriteText(writer, OrderingFingerprint);
                writer.Write(SourceUsn);
                writer.Write(ResultsConsumed);
                writer.Write(LastIdentity.HasValue);
                if (LastIdentity.HasValue)
                    writer.Write(LastIdentity.Value);
                writer.Write(DeterministicOrdering);
                writer.Write((byte)Consistency);
                writer.Write(CapturedAtUtc.UtcTicks);
            }

            byte[] body = payload.ToArray();
            byte[] digest = SHA256.HashData(body);
            byte[] token = GC.AllocateUninitializedArray<byte>(body.Length + digest.Length);
            body.CopyTo(token, 0);
            digest.CopyTo(token, body.Length);
            return token;
        }

        /// <summary>
        /// Serializes this bookmark as an unpadded Base64URL token suitable for application storage or transport.<br/>
        /// The text is opaque and is not a human-readable query representation.<br/>
        /// </summary>
        /// <returns>Versioned Base64URL bookmark text.<br/></returns>
        public string ToTokenString()
            => Convert.ToBase64String(ToBytes()).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>
        /// Restores and fully validates one binary bookmark token without opening a RecordBase or executing a query.<br/>
        /// </summary>
        /// <param name="token">Bytes produced by <see cref="ToBytes"/>.<br/></param>
        /// <returns>The detached bookmark.<br/></returns>
        public static RecordBookmark FromBytes(ReadOnlySpan<byte> token)
        {
            try
            {
                if (token.Length <= DigestLength || token.Length > MaximumTokenLength)
                    throw InvalidToken("Bookmark token length is invalid.");

                ReadOnlySpan<byte> body = token[..^DigestLength];
                ReadOnlySpan<byte> suppliedDigest = token[^DigestLength..];
                Span<byte> expectedDigest = stackalloc byte[DigestLength];
                SHA256.HashData(body, expectedDigest);
                if (!CryptographicOperations.FixedTimeEquals(suppliedDigest, expectedDigest))
                    throw InvalidToken("Bookmark token failed its corruption check.");

                using var stream = new MemoryStream(body.ToArray(), writable: false);
                using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
                if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
                    throw InvalidToken("Bookmark token signature is invalid.");
                int version = reader.ReadInt32();
                if (version != CurrentVersion)
                    throw InvalidToken($"Bookmark format version '{version}' is unsupported.");

                Guid storeInstanceId = new(reader.ReadBytes(16));
                byte storeId = reader.ReadByte();
                string recordTypeIdentity = ReadText(reader);
                string selectionFingerprint = ReadText(reader);
                string resultShapeFingerprint = ReadText(reader);
                string orderingFingerprint = ReadText(reader);
                ulong sourceUsn = reader.ReadUInt64();
                long resultsConsumed = reader.ReadInt64();
                bool hasLastIdentity = reader.ReadBoolean();
                ulong? lastIdentity = hasLastIdentity ? reader.ReadUInt64() : null;
                bool deterministicOrdering = reader.ReadBoolean();
                var consistency = (BookmarkConsistency)reader.ReadByte();
                long capturedTicks = reader.ReadInt64();
                if (stream.Position != stream.Length)
                    throw InvalidToken("Bookmark token contains unexpected trailing data.");

                return new RecordBookmark(
                    storeInstanceId,
                    storeId,
                    recordTypeIdentity,
                    selectionFingerprint,
                    resultShapeFingerprint,
                    orderingFingerprint,
                    sourceUsn,
                    resultsConsumed,
                    lastIdentity,
                    deterministicOrdering,
                    consistency,
                    new DateTimeOffset(capturedTicks, TimeSpan.Zero));
            }
            catch (RecordBookmarkException)
            {
                throw;
            }
            catch (Exception exception) when (exception is EndOfStreamException or IOException or ArgumentException or ArgumentOutOfRangeException)
            {
                throw InvalidToken("Bookmark token is truncated or structurally invalid.", exception);
            }
        }

        /// <summary>
        /// Restores one unpadded Base64URL bookmark token.<br/>
        /// </summary>
        /// <param name="token">Text produced by <see cref="ToTokenString"/>.<br/></param>
        /// <returns>The detached bookmark.<br/></returns>
        public static RecordBookmark ParseToken(string token)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(token);
            try
            {
                string normalized = token.Replace('-', '+').Replace('_', '/');
                normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
                return FromBytes(Convert.FromBase64String(normalized));
            }
            catch (RecordBookmarkException)
            {
                throw;
            }
            catch (FormatException exception)
            {
                throw InvalidToken("Bookmark text is not valid Base64URL.", exception);
            }
        }

        /// <summary>
        /// Attempts to restore one Base64URL bookmark token without throwing for malformed input.<br/>
        /// </summary>
        /// <param name="token">Candidate token text.<br/></param>
        /// <param name="bookmark">Restored bookmark when validation succeeds; otherwise <see langword="null"/>.<br/></param>
        /// <returns><see langword="true"/> when the token is valid.<br/></returns>
        public static bool TryParseToken(string? token, out RecordBookmark? bookmark)
        {
            try
            {
                bookmark = string.IsNullOrWhiteSpace(token) ? null : ParseToken(token);
                return bookmark is not null;
            }
            catch (RecordBookmarkException)
            {
                bookmark = null;
                return false;
            }
        }

        private static string RequireText(string value, string parameterName)
            => string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Bookmark identity text cannot be empty.", parameterName)
                : value;

        private static void WriteText(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length > ushort.MaxValue)
                throw new InvalidOperationException("Bookmark identity text exceeds the supported token bound.");
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadText(BinaryReader reader)
        {
            int length = reader.ReadUInt16();
            if (length == 0)
                throw InvalidToken("Bookmark identity text cannot be empty.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
                throw new EndOfStreamException();
            return Encoding.UTF8.GetString(bytes);
        }

        private static RecordBookmarkException InvalidToken(string message, Exception? innerException = null)
            => new(RecordBookmarkFailureKind.InvalidToken, message, innerException);
    }
}
