using System.Security.Cryptography;
using System.Text;

namespace AbraxasDB
{
    /// <summary>
    /// The descriptor reuses the recursive selector grammar used by durable conditions so one calculated index can be recreated after the owning record base is reopened.<br/>
    /// Runtime-only operations such as <c>External(...)</c> are rejected during capture because executable application code has no durable semantic identity.<br/>
    /// </summary>
    /// <typeparam name="T">Record type consumed by the selector.<br/></typeparam>
    internal sealed class SelectorDescriptor<T> : ISelector, ISelectorStructure
    {
        private const uint Magic = 0x53425841;
        private const int EnvelopeFormatVersion = 1;
        private readonly ConditionWireSelector selector;
        private readonly IBackendNeutralSelectorDescriptor semanticDescriptor;

        private SelectorDescriptor(ConditionWireSelector selector, Type resultType)
        {
            this.selector = selector ?? throw new ArgumentNullException(nameof(selector));
            ResultType = resultType ?? throw new ArgumentNullException(nameof(resultType));
            ConditionDescriptor<T>.ValidateSelector(selector);
            ValueOrExpression<string> replayed = ConditionWireReplayer<T>.CreateReplaySelector(selector);
            semanticDescriptor = replayed.SelectorDescriptor
                ?? throw new InvalidDataException("A durable selector replay produced no backend-neutral descriptor.");
        }

        /// <summary>
        /// Gets the exact CLR result type produced by this selector.<br/>
        /// Calculated-index creation uses this contract without widening, narrowing, or inferring a type from runtime values.<br/>
        /// </summary>
        public Type ResultType { get; }

        /// <summary>
        /// Gets a deterministic SHA-256 fingerprint over the selector-only binary descriptor.<br/>
        /// This is suitable for stable calculated-index identity; display text is intentionally not authoritative.<br/>
        /// </summary>
        public string SemanticFingerprint => Convert.ToHexString(SHA256.HashData(Serialize()));

        string? ISelectorStructure.SimplePropertyPath => null;
        string? ISelectorStructure.TypedSourcePropertyPath => null;
        Type? ISelectorStructure.ResultType => ResultType;
        IBackendNeutralSelectorDescriptor? ISelectorStructure.SemanticDescriptor => semanticDescriptor;

        /// <summary>
        /// Captures one typed direct or computed selector into the durable selector-only grammar.<br/>
        /// The source selector is not retained, so later builder mutation cannot change this descriptor.<br/>
        /// </summary>
        /// <param name="source">Typed selector with complete backend-neutral semantics.<br/></param>
        /// <returns>An immutable descriptor that can be serialized and replayed.<br/></returns>
        /// <exception cref="NotSupportedException">Thrown when the selector is untyped, incomplete, or contains runtime-only application code.<br/></exception>
        internal static SelectorDescriptor<T> FromSelector(ISelector source)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (source is not ISelectorStructure structure || structure.ResultType is null)
                throw new NotSupportedException("A durable calculated index requires an exact typed selector result.");

            var capture = new ConditionWireCaptureContext(typeof(T));
            ConditionWireSelector captured = capture.CaptureSelector<T>(source);
            if (!capture.SystemPersistable)
                throw new NotSupportedException("The selector contains runtime-only values and cannot be persisted as calculated-index intent.");

            return new SelectorDescriptor<T>(captured, structure.ResultType);
        }

        /// <summary>
        /// Captures an already frozen backend-neutral descriptor with an exact consuming result type for calculated-index plan matching.<br/>
        /// This internal path avoids rebuilding public selector fluency while retaining the same wire validation and fingerprint identity as authored selectors.<br/>
        /// </summary>
        /// <param name="descriptor">Immutable selector semantic node supplied by a condition leaf.<br/></param>
        /// <param name="resultType">Exact CLR result expected by the consuming operator.<br/></param>
        /// <returns>A selector descriptor suitable for exact fingerprint comparison.<br/></returns>
        internal static SelectorDescriptor<T> FromSemanticDescriptor(
            IBackendNeutralSelectorDescriptor descriptor,
            Type resultType)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            ArgumentNullException.ThrowIfNull(resultType);
            return FromSelector(new SemanticSelector(descriptor, resultType));
        }

        /// <summary>
        /// Serializes this descriptor into a deterministic primitive-only payload.<br/>
        /// The envelope records the record type, exact result type, and shared selector-wire version before the recursive selector node.<br/>
        /// </summary>
        /// <returns>Caller-owned bytes suitable for Abraxas intent persistence.<br/></returns>
        public byte[] Serialize()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(EnvelopeFormatVersion);
            writer.Write(ConditionDescriptor<T>.SelectorWireFormatVersion);
            writer.Write(ConditionWireTypes.GetCode(typeof(T)));
            writer.Write(ConditionWireTypes.GetCode(ResultType));
            ConditionWireCodec.WriteSelector(writer, selector, ConditionDescriptor<T>.SelectorWireFormatVersion);
            writer.Flush();
            return stream.ToArray();
        }

        /// <summary>
        /// Restores and validates one selector-only descriptor without executing the selector or reading record data.<br/>
        /// Record/result type identities, recursive structure, bounded lengths, and trailing bytes are checked before the descriptor is returned.<br/>
        /// </summary>
        /// <param name="binary">Selector-only payload previously produced by <see cref="Serialize"/>.<br/></param>
        /// <returns>An immutable replayable selector descriptor.<br/></returns>
        public static SelectorDescriptor<T> Deserialize(byte[] binary)
        {
            ArgumentNullException.ThrowIfNull(binary);
            using var stream = new MemoryStream(binary, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            if (reader.ReadUInt32() != Magic)
                throw new InvalidDataException("Serialized selector payload has an invalid signature.");
            int envelopeVersion = reader.ReadInt32();
            if (envelopeVersion != EnvelopeFormatVersion)
                throw new InvalidDataException($"Serialized selector envelope version '{envelopeVersion}' is unsupported.");
            int selectorVersion = reader.ReadInt32();
            if (selectorVersion < ConditionDescriptor<T>.MinimumSelectorWireFormatVersion ||
                selectorVersion > ConditionDescriptor<T>.SelectorWireFormatVersion)
            {
                throw new InvalidDataException(
                    $"Serialized selector wire version '{selectorVersion}' is unsupported; expected " +
                    $"'{ConditionDescriptor<T>.MinimumSelectorWireFormatVersion}' through '{ConditionDescriptor<T>.SelectorWireFormatVersion}'.");
            }
            string recordTypeCode = reader.ReadString();
            if (!string.Equals(recordTypeCode, ConditionWireTypes.GetCode(typeof(T)), StringComparison.Ordinal))
                throw new InvalidDataException($"Serialized selector record type '{recordTypeCode}' does not match '{typeof(T).FullName}'.");
            Type resultType = ConditionWireTypes.Resolve(reader.ReadString());
            ConditionWireSelector selector = ConditionWireCodec.ReadSelector(reader, selectorVersion);
            if (stream.Position != stream.Length)
                throw new InvalidDataException("Serialized selector payload contains unexpected trailing bytes.");
            return new SelectorDescriptor<T>(selector, resultType);
        }

        /// <summary>
        /// Returns this immutable descriptor as its own safe clone.<br/>
        /// </summary>
        /// <returns>This descriptor instance.<br/></returns>
        public ISelector Clone() => this;

        /// <summary>
        /// Produces the deterministic canonical selector identity used by diagnostics and calculated-index naming.<br/>
        /// </summary>
        /// <returns>Invariant backend-neutral selector text.<br/></returns>
        public override string ToString() => ConditionDescriptor<T>.ToCanonicalSelectorString(this);

        /// <summary>
        /// Gets the stored member path only when this selector is a direct property selection.<br/>
        /// Computed and transformed selectors have no equivalent authoritative member path for covered reads.<br/>
        /// </summary>
        internal string? DirectPropertyPath => selector.Kind == "Property" ? selector.PropertyPath : null;

        /// <summary>
        /// Supplies the minimum immutable selector structure required to capture an existing semantic node.<br/>
        /// </summary>
        private sealed class SemanticSelector : ISelector, ISelectorStructure
        {
            private readonly IBackendNeutralSelectorDescriptor descriptor;
            private readonly Type resultType;

            internal SemanticSelector(IBackendNeutralSelectorDescriptor descriptor, Type resultType)
            {
                this.descriptor = descriptor;
                this.resultType = resultType;
            }

            string? ISelectorStructure.SimplePropertyPath => null;
            string? ISelectorStructure.TypedSourcePropertyPath => null;
            Type? ISelectorStructure.ResultType => resultType;
            IBackendNeutralSelectorDescriptor? ISelectorStructure.SemanticDescriptor => descriptor;
            public ISelector Clone() => this;
        }
    }
}
