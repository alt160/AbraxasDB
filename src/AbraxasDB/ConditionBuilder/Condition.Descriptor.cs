using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Inheto;
using LibraDex;

namespace AbraxasDB
{
    /// <summary>
    /// Identifies the semantic role of one value that must be supplied before a serialized condition can be rehydrated.<br/>
    /// The role is diagnostic and does not weaken exact CLR-type validation during binding.<br/>
    /// </summary>
    public enum ConditionParameterKind
    {
        /// <summary>
        /// A scalar value operand is required.<br/>
        /// </summary>
        Value,
        /// <summary>
        /// A collection operand with its declared element type is required.<br/>
        /// </summary>
        Collection,
        /// <summary>
        /// A selector binding is required rather than an ordinary scalar value.<br/>
        /// </summary>
        Selector,
        /// <summary>
        /// An operator binding is required rather than an ordinary scalar value.<br/>
        /// </summary>
        Operator,
        /// <summary>
        /// A condition binding is required rather than an ordinary scalar value.<br/>
        /// </summary>
        Condition
    }

    /// <summary>
    /// Describes one zero-based, left-to-right binding position in a serialized condition.<br/>
    /// Instances are immutable public inspection values and contain no delegate, runtime condition, or resolved value.<br/>
    /// </summary>
    public sealed class ConditionParameterInfo
    {
        internal ConditionParameterInfo(ConditionWireParameter source)
        {
            Position = source.Position;
            Name = string.IsNullOrWhiteSpace(source.Name) ? null : source.Name;
            ParameterType = ConditionWireTypes.Resolve(source.TypeCode);
            Kind = source.Kind;
            ElementType = string.IsNullOrWhiteSpace(source.ElementTypeCode)
                ? null
                : ConditionWireTypes.Resolve(source.ElementTypeCode);
            DiagnosticContext = source.DiagnosticContext ?? string.Empty;
        }

        /// <summary>
        /// Gets the zero-based binding position assigned by deterministic left-to-right descriptor traversal.<br/>
        /// </summary>
        public int Position { get; }
        /// <summary>
        /// Gets the optional binding name, or null for an unnamed position.<br/>
        /// </summary>
        public string? Name { get; }
        /// <summary>
        /// Gets the exact CLR type required when this serialized binding position is rehydrated.<br/>
        /// </summary>
        public Type ParameterType { get; }
        /// <summary>
        /// Gets the binding's semantic role rather than a storage or runtime delegate type.<br/>
        /// </summary>
        public ConditionParameterKind Kind { get; }
        /// <summary>
        /// Gets the declared collection element type when present, otherwise null.<br/>
        /// </summary>
        public Type? ElementType { get; }
        /// <summary>
        /// Gets whether this value or collection binding accepts an ordinary supplied value.<br/>
        /// </summary>
        public bool AcceptsValue => Kind is ConditionParameterKind.Value or ConditionParameterKind.Collection;
        /// <summary>
        /// Gets whether this value or collection binding accepts an expression-based value provider.<br/>
        /// </summary>
        public bool AcceptsLambda => AcceptsValue;
        /// <summary>
        /// Gets whether this value or collection binding accepts a deferred value provider.<br/>
        /// </summary>
        public bool AcceptsDeferred => AcceptsValue;
        /// <summary>
        /// Gets descriptive condition context used to explain a binding failure.<br/>
        /// </summary>
        public string DiagnosticContext { get; }
    }

    /// <summary>
    /// Reports a strict serialized-condition binding failure.<br/>
    /// The exception message includes the exact position, optional name, expected type, and diagnostic condition context whenever that information is available.<br/>
    /// </summary>
    public sealed class ConditionBindingException : InvalidOperationException
    {
        internal ConditionBindingException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Holds a versioned, backend-neutral description of an Abraxas condition independently of the condition builder's private runtime fragment classes.<br/>
    /// The descriptor can remain incomplete through typed binding slots, can be serialized through Inheto, and returns an executable condition only after strict rehydration succeeds.<br/>
    /// </summary>
    /// <typeparam name="T">Record type selected by the condition.<br/></typeparam>
    public sealed class ConditionDescriptor<T>
    {
        private const int MinimumSupportedFormatVersion = 2;
        private const int CurrentFormatVersion = 16;
        private readonly ConditionWireDocument document;
        private readonly ReadOnlyCollection<ConditionParameterInfo> parameters;

        /// <summary>
        /// Gets the current recursive selector wire version shared by condition and selector-only descriptors.<br/>
        /// Keeping one version source prevents calculated-index intent from silently diverging from durable condition replay.<br/>
        /// </summary>
        internal static int SelectorWireFormatVersion => CurrentFormatVersion;
        internal static int MinimumSelectorWireFormatVersion => MinimumSupportedFormatVersion;

        private ConditionDescriptor(ConditionWireDocument document)
        {
            this.document = document ?? throw new ArgumentNullException(nameof(document));
            ValidateDocument(document);
            parameters = Array.AsReadOnly(
                document.Parameters
                    .OrderBy(static parameter => parameter.Position)
                    .Select(static parameter => new ConditionParameterInfo(parameter))
                    .ToArray());
        }

        /// <summary>
        /// Gets the immutable zero-based binding contract in deterministic left-to-right condition order.<br/>
        /// Static operands and self-contained condition structure are omitted because the rehydrating developer does not need to supply them.<br/>
        /// </summary>
        public IReadOnlyList<ConditionParameterInfo> Parameters => parameters;

        /// <summary>
        /// Gets whether the descriptor requires no external bindings before it can produce an executable condition.<br/>
        /// This is structural/runtime completeness only; <see cref="IsSystemPersistable"/> applies the stricter durable-system-use boundary.<br/>
        /// </summary>
        public bool IsComplete => parameters.Count == 0;

        /// <summary>
        /// Gets whether the descriptor is complete and contains only literals or durably described built-in values.<br/>
        /// Process-local lambdas are intentionally represented as external binding slots and therefore cannot make an unbound descriptor system-persistable.<br/>
        /// </summary>
        public bool IsSystemPersistable => IsComplete && document.SystemPersistable;

        /// <summary>
        /// Gets the validated semantic sequence for assembly-internal planners that consume the same authoritative condition meaning without reparsing diagnostic text.<br/>
        /// Callers must treat the returned wire graph as immutable and must retain the descriptor's completeness and operand-materialization boundaries.<br/>
        /// </summary>
        internal ConditionWireSequence SemanticSequence => document.Sequence;

        /// <summary>
        /// Gets a deterministic SHA-256 fingerprint over the complete primitive semantic wire payload.<br/>
        /// Unlike the human-readable rendering, the fingerprint includes every family, operator, selector, typed literal, parameter contract, and semantic option.<br/>
        /// The fingerprint therefore remains safe for exact descriptor identity and conservative structural implication checks without treating diagnostic text as authoritative.<br/>
        /// </summary>
        public string SemanticFingerprint
            => Convert.ToHexString(SHA256.HashData(ConditionWireCodec.Encode(document)));

        /// <summary>
        /// Converts one existing completed Abraxas condition into a stable descriptor.<br/>
        /// Static operands are embedded as typed Inheto values; deferred operands become ordered external binding slots without being invoked.<br/>
        /// Unsupported selector or operation shapes fail explicitly instead of persisting a backend-specific or lossy approximation.<br/>
        /// </summary>
        /// <param name="condition">Completed condition whose semantic fragments should be captured.<br/></param>
        /// <returns>A versioned descriptor suitable for inspection, packaging, rendering, and strict rehydration.<br/></returns>
        public static ConditionDescriptor<T> FromCondition(Condition<T>.EndCondition condition)
        {
            ArgumentNullException.ThrowIfNull(condition);
            var capture = new ConditionWireCaptureContext(typeof(T));
            var sequence = capture.CaptureSequence(condition.builder);
            return new ConditionDescriptor<T>(new ConditionWireDocument
            {
                FormatVersion = CurrentFormatVersion,
                RecordTypeCode = ConditionWireTypes.GetCode(typeof(T)),
                Sequence = sequence,
                Parameters = capture.Parameters.ToList(),
                SystemPersistable = capture.SystemPersistable
            });
        }

        /// <summary>
        /// Captures the current condition tree after the owning execution builder has materialized every deferred operand.<br/>
        /// The returned sequence is planner-internal and transient: values come from the active parameter snapshot and retain the original leaf cache owner, so it must never be exposed as a durable descriptor or reused by another execution.<br/>
        /// </summary>
        /// <param name="condition">The completed condition whose active builder already began parameter materialization.<br/></param>
        /// <returns>An immutable-shape semantic sequence containing execution-frozen literal operands.<br/></returns>
        internal static ConditionWireSequence CaptureMaterializedSemanticSequence(Condition<T>.EndCondition condition)
        {
            ArgumentNullException.ThrowIfNull(condition);
            var capture = new ConditionWireCaptureContext(typeof(T), materializeParameters: true);
            return capture.CaptureSequence(condition.builder);
        }

        /// <summary>
        /// Restores a condition descriptor from its Inheto binary representation without evaluating, binding, compiling, or executing the condition.<br/>
        /// The record-type identity, format version, parameter positions, parameter names, and parameter types are validated before the descriptor is returned.<br/>
        /// </summary>
        /// <param name="binary">Inheto bytes previously produced by <see cref="Serialize"/>.<br/></param>
        /// <returns>The inspectable descriptor, which may still require external bindings.<br/></returns>
        public static ConditionDescriptor<T> Deserialize(byte[] binary)
        {
            ArgumentNullException.ThrowIfNull(binary);
            if (binary.Length == 0)
                throw new InvalidDataException("Serialized condition data cannot be empty.");

            var payload = InhetoBinary
                .FromStream(new BufferStream(binary))
                .ToObject<byte[]>()
                ?? throw new InvalidDataException("Serialized condition data did not contain a condition payload.");
            var restored = ConditionWireCodec.Decode(payload);
            return new ConditionDescriptor<T>(restored);
        }

        /// <summary>
        /// Inspects the ordered binding contract of an Inheto condition package without constructing an executable condition.<br/>
        /// The package is fully validated first, including format version, record type, contiguous zero-based positions, unique case-sensitive names, and CLR type identities.<br/>
        /// </summary>
        /// <param name="binary">Inheto condition-package bytes to inspect.<br/></param>
        /// <returns>An immutable list containing each required parameter's optional name, zero-based position, CLR type, kind, and diagnostic context.<br/></returns>
        public static IReadOnlyList<ConditionParameterInfo> Inspect(byte[] binary)
            => Deserialize(binary).Parameters;

        /// <summary>
        /// Serializes this descriptor to its compact Inheto binary representation.<br/>
        /// The binary remains authoritative; canonical and friendly text are generated projections and are not persisted as competing semantic sources.<br/>
        /// </summary>
        /// <returns>Caller-owned Inheto bytes.<br/></returns>
        public byte[] Serialize()
            => InhetoBinary.FromObject(ConditionWireCodec.Encode(document)).RawBytes.ToArray();

        /// <summary>
        /// Starts strict positional or named rehydration without evaluating any supplied lambda.<br/>
        /// The returned binder throws immediately for duplicate, extra, unknown-name, or incompatible bindings and throws from <c>Build()</c> if any position remains missing.<br/>
        /// </summary>
        /// <returns>A fresh binder whose state is isolated from this immutable descriptor.<br/></returns>
        public ConditionRehydrator<T> Rehydrate()
            => new(this, document);

        /// <summary>
        /// Produces deterministic invariant diagnostic text from the same semantic document used for binary serialization and rehydration.<br/>
        /// Typed parameter holes include their zero-based position and optional developer-supplied name.<br/>
        /// </summary>
        public string ToCanonicalString()
            => ConditionWireRenderer.Render(document.Sequence, document.Parameters, friendly: false);

        /// <summary>
        /// Produces the deterministic durable identity of one selector without wrapping it in an artificial condition.<br/>
        /// Runtime-only external delegates fail explicitly because executable application code has no reconstructable identity.<br/>
        /// </summary>
        /// <param name="selector">Direct or computed selector whose complete semantic expression is required.<br/></param>
        /// <returns>Invariant canonical selector text generated from its validated durable descriptor.<br/></returns>
        internal static string ToCanonicalSelectorString(ISelector selector)
        {
            ArgumentNullException.ThrowIfNull(selector);
            var capture = new ConditionWireCaptureContext(typeof(T));
            ConditionWireSelector captured = capture.CaptureSelector<T>(selector);
            ValidateSelector(captured);
            var builder = new StringBuilder();
            ConditionWireRenderer.RenderCanonicalSelectorIdentity(builder, captured);
            return builder.ToString();
        }

        /// <summary>
        /// Produces concise human-readable condition text for support, reporting, and UI display.<br/>
        /// This representation is descriptive rather than authoritative and is regenerated from the semantic document on demand.<br/>
        /// </summary>
        public string ToDisplayString()
            => ToDisplayString(new ConditionTextOptions());

        /// <summary>
        /// Produces human-readable condition text using the requested audience, layout, labeling, masking, culture, and value-formatting controls.<br/>
        /// Sensitive property operands are replaced before value deserialization or caller formatting, and the returned text remains a one-way non-authoritative projection.<br/>
        /// </summary>
        /// <param name="options">Presentation controls applied without changing the underlying semantic descriptor.<br/></param>
        /// <returns>Human-readable condition text rendered from this descriptor.<br/></returns>
        public string ToDisplayString(ConditionTextOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            return ConditionWireRenderer.RenderDisplay(
                document.Sequence,
                document.Parameters,
                typeof(T),
                options);
        }

        /// <summary>
        /// Attempts to produce default end-user narrative text without allowing an unsupported semantic descriptor shape to escape as an exception.<br/>
        /// Resolver, formatter, and other caller-code exceptions are not swallowed because they represent application failures rather than unsupported condition semantics.<br/>
        /// </summary>
        /// <param name="text">Rendered text when the condition is supported; otherwise an empty string.<br/></param>
        /// <returns><c>true</c> when display text was produced; otherwise <c>false</c>.<br/></returns>
        public bool TryToDisplayString(out string text)
            => TryToDisplayString(new ConditionTextOptions(), out text);

        /// <summary>
        /// Attempts to produce configured display text without allowing an unsupported semantic descriptor shape to escape as an exception.<br/>
        /// Resolver, formatter, and other caller-code exceptions are not swallowed because they represent application failures rather than unsupported condition semantics.<br/>
        /// </summary>
        /// <param name="options">Presentation controls applied without changing the semantic descriptor.<br/></param>
        /// <param name="text">Rendered text when the condition is supported; otherwise an empty string.<br/></param>
        /// <returns><c>true</c> when display text was produced; otherwise <c>false</c>.<br/></returns>
        public bool TryToDisplayString(ConditionTextOptions options, out string text)
        {
            ArgumentNullException.ThrowIfNull(options);
            try
            {
                text = ToDisplayString(options);
                return true;
            }
            catch (ConditionDisplayNotSupportedException)
            {
                text = string.Empty;
                return false;
            }
        }

        /// <summary>
        /// Returns the descriptor's canonical semantic string rather than a user-facing narrative description.<br/>
        /// </summary>
        /// <returns>The canonical condition representation.<br/></returns>
        public override string ToString() => ToCanonicalString();

        internal Condition<T>.EndCondition Build(IReadOnlyList<object?> bindings)
            => ConditionWireReplayer<T>.Replay(document, bindings);

        /// <summary>
        /// Proves the conservative structural implication used to authorize a partial index for one query condition.<br/>
        /// Both descriptors must be complete; every predicate conjunct must occur as a required AND conjunct in the query, while OR-containing groups are compared only as exact atomic subtrees.<br/>
        /// This intentionally rejects algebraically equivalent rewrites that are not structurally proven so an optimization can never broaden the result set.<br/>
        /// </summary>
        /// <param name="query">Complete query descriptor that may imply this descriptor's membership predicate.<br/></param>
        /// <returns><c>true</c> only when structural AND containment proves the implication.<br/></returns>
        internal bool IsImpliedBy(ConditionDescriptor<T> query)
        {
            ArgumentNullException.ThrowIfNull(query);
            if (!IsComplete || !query.IsComplete)
                return false;

            if (!TryCollectRequiredConjuncts(document.Sequence, out var required) ||
                !TryCollectRequiredConjuncts(query.document.Sequence, out var supplied))
            {
                return false;
            }

            var available = supplied
                .GroupBy(static fingerprint => fingerprint, StringComparer.Ordinal)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.Count(),
                    StringComparer.Ordinal);
            foreach (var fingerprint in required)
            {
                if (!available.TryGetValue(fingerprint, out var count) || count == 0)
                    return false;
                available[fingerprint] = count - 1;
            }

            return true;
        }

        /// <summary>
        /// Flattens a sequence only across required AND edges and emits deterministic fingerprints for its atomic leaves or OR-containing grouped subtrees.<br/>
        /// A top-level OR makes the sequence unusable as a source of globally required conjuncts.<br/>
        /// </summary>
        /// <param name="sequence">Validated semantic sequence to inspect.<br/></param>
        /// <param name="fingerprints">Required conjunct fingerprints when the sequence is an AND tree.<br/></param>
        /// <returns><c>true</c> when the sequence has no top-level OR connector; otherwise <c>false</c>.<br/></returns>
        private static bool TryCollectRequiredConjuncts(
            ConditionWireSequence sequence,
            out List<string> fingerprints)
        {
            fingerprints = new List<string>();
            for (var index = 0; index < sequence.Items.Length; index++)
            {
                var item = sequence.Items[index];
                if (item.Kind == "Or")
                    return false;
                if (item.Kind == "And")
                    continue;
                if (item.Kind == "Leaf")
                {
                    fingerprints.Add(FingerprintAtomicSequence(new ConditionWireSequence
                    {
                        Items = new[] { item }
                    }));
                    continue;
                }
                if (item.Kind != "Group" || item.Group is null)
                    return false;

                if (TryCollectRequiredConjuncts(item.Group, out var nested))
                    fingerprints.AddRange(nested);
                else
                    fingerprints.Add(FingerprintAtomicSequence(item.Group));
            }

            return fingerprints.Count != 0;
        }

        /// <summary>
        /// Hashes one complete static semantic subtree using the same deterministic primitive wire encoding as the descriptor fingerprint.<br/>
        /// </summary>
        /// <param name="sequence">Atomic leaf or grouped subtree whose exact semantics should be identified.<br/></param>
        /// <returns>Uppercase SHA-256 hexadecimal identity.<br/></returns>
        private static string FingerprintAtomicSequence(ConditionWireSequence sequence)
        {
            var atom = new ConditionWireDocument
            {
                FormatVersion = CurrentFormatVersion,
                RecordTypeCode = ConditionWireTypes.GetCode(typeof(T)),
                Sequence = sequence,
                Parameters = new List<ConditionWireParameter>(),
                SystemPersistable = true
            };
            return Convert.ToHexString(SHA256.HashData(ConditionWireCodec.Encode(atom)));
        }

        private static void ValidateDocument(ConditionWireDocument value)
        {
            if (value.FormatVersion < MinimumSupportedFormatVersion ||
                value.FormatVersion > CurrentFormatVersion)
            {
                throw new InvalidDataException(
                    $"Condition format version '{value.FormatVersion}' is unsupported; expected " +
                    $"'{MinimumSupportedFormatVersion}' through '{CurrentFormatVersion}'.");
            }
            if (!string.Equals(value.RecordTypeCode, ConditionWireTypes.GetCode(typeof(T)), StringComparison.Ordinal))
                throw new InvalidDataException(
                    $"Condition record type '{value.RecordTypeCode}' does not match '{ConditionWireTypes.GetCode(typeof(T))}'.");
            if (value.Sequence is null)
                throw new InvalidDataException("Condition document has no semantic sequence.");

            value.Parameters ??= new List<ConditionWireParameter>();
            var ordered = value.Parameters.OrderBy(static parameter => parameter.Position).ToArray();
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (var position = 0; position < ordered.Length; position++)
            {
                var parameter = ordered[position];
                if (parameter.Position != position)
                    throw new InvalidDataException(
                        $"Condition parameter positions must be contiguous and zero-based; expected '{position}', found '{parameter.Position}'.");
                var parameterType = ConditionWireTypes.Resolve(parameter.TypeCode);
                if (!Enum.IsDefined(parameter.Kind))
                    throw new InvalidDataException(
                        $"Condition parameter position '{position}' has unknown kind '{parameter.Kind}'.");
                if (!string.IsNullOrWhiteSpace(parameter.Name) && !names.Add(parameter.Name))
                    throw new InvalidDataException($"Condition parameter name '{parameter.Name}' is duplicated.");
                if (parameter.Kind == ConditionParameterKind.Collection)
                {
                    if (string.IsNullOrWhiteSpace(parameter.ElementTypeCode))
                        throw new InvalidDataException(
                            $"Condition collection parameter position '{position}' has no element type.");
                    _ = ConditionWireTypes.Resolve(parameter.ElementTypeCode);
                    if (!typeof(IEnumerable).IsAssignableFrom(parameterType))
                        throw new InvalidDataException(
                            $"Condition collection parameter position '{position}' type '{parameterType.FullName}' is not enumerable.");
                }
                else if (!string.IsNullOrWhiteSpace(parameter.ElementTypeCode))
                {
                    throw new InvalidDataException(
                        $"Condition non-collection parameter position '{position}' cannot declare an element type.");
                }
            }

            var references = new int[ordered.Length];
            ValidateSequence(value.Sequence, ordered, references);
            for (var position = 0; position < references.Length; position++)
            {
                if (references[position] != 1)
                    throw new InvalidDataException(
                        $"Condition parameter position '{position}' must be referenced exactly once; found '{references[position]}'.");
            }

            var derivedSystemPersistable = ordered.Length == 0;
            if (value.SystemPersistable != derivedSystemPersistable)
                throw new InvalidDataException(
                    $"Condition system-persistability flag '{value.SystemPersistable}' does not match derived value '{derivedSystemPersistable}'.");
        }

        /// <summary>
        /// Validates the complete recursive semantic tree and every parameter reference before inspection, rendering, or replay can occur.<br/>
        /// Unknown discriminators, missing payloads, unavailable CLR types, out-of-range positions, type mismatches, and multiply referenced slots fail closed.<br/>
        /// </summary>
        /// <param name="sequence">Semantic sequence to validate recursively.<br/></param>
        /// <param name="parameters">Ordered validated parameter contract.<br/></param>
        /// <param name="references">Per-position reference counters shared across recursive groups.<br/></param>
        private static void ValidateSequence(
            ConditionWireSequence sequence,
            IReadOnlyList<ConditionWireParameter> parameters,
            int[] references)
        {
            if (sequence.Items is null || sequence.Items.Length == 0)
                throw new InvalidDataException("Condition semantic sequence cannot be empty.");

            var expectOperand = true;
            foreach (var item in sequence.Items)
            {
                switch (item.Kind)
                {
                    case "And":
                    case "Or":
                        if (expectOperand)
                            throw new InvalidDataException(
                                $"Condition connector '{item.Kind}' cannot appear before an operand or immediately after another connector.");
                        if (item.Leaf is not null || item.Group is not null)
                            throw new InvalidDataException($"Condition connector '{item.Kind}' cannot contain a payload.");
                        expectOperand = true;
                        break;
                    case "Group":
                        if (!expectOperand)
                            throw new InvalidDataException("Adjacent condition operands require an AND or OR connector.");
                        if (item.Leaf is not null || item.Group is null)
                            throw new InvalidDataException("Condition group item must contain exactly one group payload.");
                        ValidateSequence(item.Group, parameters, references);
                        expectOperand = false;
                        break;
                    case "Leaf":
                        if (!expectOperand)
                            throw new InvalidDataException("Adjacent condition operands require an AND or OR connector.");
                        if (item.Leaf is null || item.Group is not null)
                            throw new InvalidDataException("Condition leaf item must contain exactly one leaf payload.");
                        ValidateLeaf(item.Leaf, parameters, references);
                        expectOperand = false;
                        break;
                    default:
                        throw new InvalidDataException($"Unknown condition item kind '{item.Kind}'.");
                }
            }

            if (expectOperand)
                throw new InvalidDataException("Condition semantic sequence cannot end with a connector.");
        }

        /// <summary>
        /// Validates one complete predicate leaf before inspection, rendering, fingerprinting, or replay.<br/>
        /// Family-specific option counts are exact so missing values cannot silently become defaults and unknown extra values cannot be ignored.<br/>
        /// Literal and parameter discriminators are mutually exclusive, and every typed payload is decoded once to prove it matches its declared CLR contract.<br/>
        /// </summary>
        /// <param name="leaf">Predicate leaf supplied by the decoded wire document.<br/></param>
        /// <param name="parameters">Validated ordered parameter contract.<br/></param>
        /// <param name="references">Per-position reference counters shared across the complete recursive tree.<br/></param>
        private static void ValidateLeaf(
            ConditionWireLeaf leaf,
            IReadOnlyList<ConditionWireParameter> parameters,
            int[] references)
        {
            if (string.IsNullOrWhiteSpace(leaf.Family) ||
                string.IsNullOrWhiteSpace(leaf.Operator) ||
                leaf.Selector is null)
            {
                throw new InvalidDataException("Condition leaf family, operator, and selector are required.");
            }

            ValidateSelector(leaf.Selector);
            if (leaf.OperandSelector is not null)
                ValidateSelector(leaf.OperandSelector);
            leaf.Operands ??= Array.Empty<ConditionWireValue>();
            leaf.TextOptions ??= Array.Empty<string>();
            leaf.IntegerOptions ??= Array.Empty<int>();
            leaf.BooleanOptions ??= Array.Empty<bool>();
            var valueType = ConditionWireTypes.Resolve(leaf.ValueTypeCode);

            switch (leaf.Family)
            {
                case "Numeric":
                    RequireOptionCounts(leaf, 0, 0, 0);
                    if (leaf.Operands.Length == 0 && leaf.Operator is not ("In" or "NotIn"))
                        throw new InvalidDataException("Numeric condition leaves require at least one operand.");
                    break;
                case "String":
                    RequireOptionCounts(leaf, 1, 1, 1);
                    if (leaf.Operator is "In" or "NotIn" or "InSet" or "NotInSet")
                    {
                        if (leaf.Operands.Length != 1 || !leaf.Operands[0].IsCollection)
                            throw new InvalidDataException(
                                $"String collection operation '{leaf.Operator}' requires exactly one collection operand.");
                    }
                    break;
                case "Boolean":
                    RequireOptionCounts(leaf, 0, 0, 0);
                    if (leaf.Operator != "EqualTo" || leaf.Operands.Length != 1 || valueType != typeof(bool))
                        throw new InvalidDataException("Boolean condition leaves require one Boolean EqualTo operand.");
                    break;
                case "Null":
                    RequireOptionCounts(leaf, 0, 0, 0);
                    if (leaf.Operator is not ("IsNull" or "IsNotNull") || leaf.Operands.Length != 0)
                        throw new InvalidDataException("Null condition leaves require IsNull or IsNotNull and no operands.");
                    break;
                case "Empty":
                    RequireOptionCounts(leaf, 0, 0, 0);
                    if (leaf.Operator is not ("IsEmpty" or "IsNotEmpty") || leaf.Operands.Length != 0)
                        throw new InvalidDataException("Empty condition leaves require IsEmpty or IsNotEmpty and no operands.");
                    break;
                case "Guid":
                    RequireOptionCounts(leaf, 0, 0, 0);
                    if (leaf.Operands.Length != 1)
                        throw new InvalidDataException("Guid condition leaves require exactly one operand.");
                    break;
                case "Binary":
                    RequireOptionCounts(leaf, 1, 0, 0);
                    if (leaf.Operator is not ("EqualTo" or "NotEqualTo") ||
                        leaf.Operands.Length != 1 ||
                        valueType != typeof(byte[]))
                    {
                        throw new InvalidDataException(
                            "Binary condition leaves require one byte-array EqualTo or NotEqualTo operand.");
                    }
                    break;
                case "TemporalWhole":
                case "TemporalStructured":
                    RequireOptionCounts(leaf, 0, 0, 0);
                    break;
                case "PropertyComparison":
                    RequireOptionCounts(leaf, 3, 1, 1);
                    if (leaf.OperandSelector is null || leaf.Operands.Length != 0 ||
                        leaf.Operator is not ("Equal" or "NotEqual" or "Greater" or "GreaterOrEqual" or "Less" or "LessOrEqual") ||
                        leaf.IntegerOptions[0] != 1)
                    {
                        throw new InvalidDataException(
                            "Property comparison leaves require two selectors, no literal operands, a known comparison, and coercion-policy version 1.");
                    }
                    var leftType = ConditionWireTypes.Resolve(leaf.TextOptions[0]);
                    var rightType = ConditionWireTypes.Resolve(leaf.TextOptions[1]);
                    bool leftSupported = Condition<T>.NumericPropertyPromotion.IsSupported(leftType) ||
                        leftType == typeof(string) || leftType == typeof(bool) || leftType == typeof(Guid) ||
                        leftType == typeof(byte[]) || leftType == typeof(DateTime) || leftType == typeof(DateOnly) ||
                        leftType == typeof(TimeOnly) || leftType == typeof(TimeSpan);
                    if (!leftSupported)
                        throw new InvalidDataException($"Property type '{leftType.FullName}' is not supported for selector-to-selector comparison.");
                    if (leftType == rightType)
                    {
                        if (valueType != leftType)
                            throw new InvalidDataException("An exact property comparison must retain the same left, right, and comparison type.");
                    }
                    else
                    {
                        Type promoted;
                        try
                        {
                            promoted = Condition<T>.NumericPropertyPromotion.Resolve(leftType, rightType);
                        }
                        catch (NotSupportedException ex)
                        {
                            throw new InvalidDataException("A property comparison declares an unsupported numeric coercion.", ex);
                        }
                        if (promoted != valueType)
                            throw new InvalidDataException("A property comparison's persisted common numeric type does not match coercion policy version 1.");
                    }
                    if ((leftType == typeof(bool) || leftType == typeof(Guid) || leftType == typeof(byte[])) &&
                        leaf.Operator is not ("Equal" or "NotEqual"))
                    {
                        throw new InvalidDataException($"Property type '{leftType.Name}' supports equality comparisons only.");
                    }
                    if (leftType != typeof(string) &&
                        (!string.IsNullOrEmpty(leaf.TextOptions[2]) || leaf.BooleanOptions[0]))
                    {
                        throw new InvalidDataException("Culture and case options are valid only for string property comparisons.");
                    }
                    if (leftType == typeof(string) && !string.IsNullOrEmpty(leaf.TextOptions[2]))
                        _ = CultureInfo.GetCultureInfo(leaf.TextOptions[2]);
                    break;
                default:
                    throw new InvalidDataException($"Unknown condition leaf family '{leaf.Family}'.");
            }

            if (leaf.Family != "PropertyComparison" && leaf.OperandSelector is not null)
                throw new InvalidDataException($"Condition leaf family '{leaf.Family}' cannot contain a right-hand selector.");

            foreach (var operand in leaf.Operands)
            {
                if (string.IsNullOrWhiteSpace(operand.TypeCode))
                    throw new InvalidDataException("Condition operands require a CLR type identity.");
                var operandType = ConditionWireTypes.Resolve(operand.TypeCode);
                if (operand.IsParameter)
                {
                    if (operand.IsNull || operand.Payload.Length != 0)
                        throw new InvalidDataException(
                            $"Condition parameter operand position '{operand.ParameterPosition}' cannot contain a literal payload or null discriminator.");
                    if (operand.ParameterPosition < 0 || operand.ParameterPosition >= parameters.Count)
                        throw new InvalidDataException(
                            $"Condition operand references invalid parameter position '{operand.ParameterPosition}'.");
                    var parameter = parameters[operand.ParameterPosition];
                    if (!string.Equals(operand.TypeCode, parameter.TypeCode, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            $"Condition operand type '{operandType.FullName}' does not match parameter " +
                            $"position '{parameter.Position}' type '{ConditionWireTypes.Resolve(parameter.TypeCode).FullName}'.");
                    }
                    if (operand.IsCollection != (parameter.Kind == ConditionParameterKind.Collection))
                        throw new InvalidDataException(
                            $"Condition operand collection discriminator does not match parameter position '{parameter.Position}'.");
                    references[operand.ParameterPosition]++;
                    continue;
                }

                if (operand.ParameterPosition != -1)
                    throw new InvalidDataException(
                        $"Condition literal operand cannot reference parameter position '{operand.ParameterPosition}'.");
                if (operand.IsNull)
                {
                    if (operand.Payload.Length != 0)
                        throw new InvalidDataException("Condition null literal cannot contain a payload.");
                    if (operandType.IsValueType && Nullable.GetUnderlyingType(operandType) is null)
                        throw new InvalidDataException(
                            $"Condition non-nullable value type '{operandType.FullName}' cannot contain a null literal.");
                    continue;
                }
                if (operand.Payload.Length == 0)
                    throw new InvalidDataException("Condition non-null literal must contain an Inheto payload.");
                _ = InhetoBinary
                    .FromStream(new BufferStream(operand.Payload))
                    .ToObject(operandType);
            }
        }

        /// <summary>
        /// Validates one recursive backend-neutral selector node before condition inspection, fingerprinting, rendering, or replay.<br/>
        /// Direct-property nodes own only a normalized property path; the first computed family owns one source selector plus explicit byte-array slice bounds.<br/>
        /// Unknown kinds, inactive-field leakage, unsupported slice kinds, and invalid bounds fail closed so a future format cannot be partially interpreted as current semantics.<br/>
        /// </summary>
        /// <param name="selector">Selector node decoded from the durable condition package.<br/></param>
        internal static void ValidateSelector(ConditionWireSelector selector)
        {
            ArgumentNullException.ThrowIfNull(selector);
            bool hasBuiltInFields = !string.IsNullOrEmpty(selector.BuiltInOperation) ||
                !string.IsNullOrEmpty(selector.BuiltInSourceTypeCode) ||
                !string.IsNullOrEmpty(selector.BuiltInResultTypeCode) ||
                selector.BuiltInFirstIntegerOperand != 0 ||
                selector.BuiltInSecondIntegerOperand != 0 ||
                selector.HasBuiltInTextOperand ||
                !string.IsNullOrEmpty(selector.BuiltInTextOperand);
            if (selector.Kind != "BuiltInOperation" && hasBuiltInFields)
                throw new InvalidDataException("A non-built-in selector cannot contain built-in operation fields.");
            if (selector.Kind != "TemporalArithmetic" && HasTemporalArithmeticFields(selector))
                throw new InvalidDataException("A non-temporal selector cannot contain temporal-arithmetic fields.");
            switch (selector.Kind)
            {
                case "SemanticIndex":
                    if (string.IsNullOrWhiteSpace(selector.PropertyPath) || Encoding.UTF8.GetByteCount(selector.PropertyPath) > 256)
                        throw new InvalidDataException("A semantic-index selector requires a name no longer than 256 UTF-8 bytes.");
                    if (selector.Source is not null ||
                        !string.IsNullOrEmpty(selector.BinarySliceKind) ||
                        selector.Offset != 0 || selector.Length != 0 || selector.EncodingCodePage != 0 || selector.TextCoercion != -1 ||
                        !string.IsNullOrEmpty(selector.DecimalOperation) || selector.OperandSource is not null ||
                        selector.HasDecimalOperand || selector.DecimalOperand != 0m || selector.HasIntegerOperand || selector.IntegerOperand != 0 ||
                        !string.IsNullOrEmpty(selector.TimeSpanProjection) || HasNumericArithmeticFields(selector) || HasStringOperationFields(selector))
                        throw new InvalidDataException("A semantic-index selector cannot contain computed-selector fields.");
                    return;
                case "Property":
                    if (string.IsNullOrWhiteSpace(selector.PropertyPath))
                        throw new InvalidDataException("A direct condition selector requires a property path.");
                    _ = new PropPath(selector.PropertyPath);
                    if (selector.Source is not null ||
                        !string.IsNullOrEmpty(selector.BinarySliceKind) ||
                        selector.Offset != 0 ||
                        selector.Length != 0 ||
                        selector.EncodingCodePage != 0 ||
                        selector.TextCoercion != -1 ||
                        !string.IsNullOrEmpty(selector.DecimalOperation) ||
                        selector.OperandSource is not null ||
                        selector.HasDecimalOperand ||
                        selector.DecimalOperand != 0m ||
                        selector.HasIntegerOperand ||
                        selector.IntegerOperand != 0 ||
                        !string.IsNullOrEmpty(selector.TimeSpanProjection) ||
                        HasNumericArithmeticFields(selector) ||
                        HasStringOperationFields(selector))
                    {
                        throw new InvalidDataException(
                            "A direct condition selector cannot contain computed-selector fields.");
                    }
                    return;
                case "BinarySlice":
                    if (selector.Source is null)
                        throw new InvalidDataException("A binary-slice selector requires a source selector.");
                    ValidateSelector(selector.Source);
                    var recognizedSlice = Enum.TryParse(
                        selector.BinarySliceKind,
                        ignoreCase: false,
                        out BinarySliceKind sliceKind);
                    var validNumericSlice =
                        recognizedSlice &&
                        Condition<T>.TryGetNumericBinarySliceShape(
                            sliceKind,
                            out _,
                            out var fixedByteWidth) &&
                        (fixedByteWidth == 0 || selector.Length == fixedByteWidth);
                    var validTextSlice =
                        recognizedSlice &&
                        Condition<T>.TryGetDeterministicTextBinarySliceShape(
                            sliceKind,
                            out var fixedTextByteWidth) &&
                        (fixedTextByteWidth == 0 || selector.Length == fixedTextByteWidth);
                    var validGuidSlice =
                        recognizedSlice &&
                        Condition<T>.TryGetDeterministicGuidBinarySliceShape(
                            sliceKind,
                            out var fixedGuidByteWidth) &&
                        selector.Length == fixedGuidByteWidth;
                    var validTemporalSlice =
                        recognizedSlice &&
                        Condition<T>.TryGetDeterministicTemporalBinarySliceShape(
                            sliceKind,
                            out _,
                            out var fixedTemporalByteWidth) &&
                        selector.Length == fixedTemporalByteWidth;
                    var validCustomTextSlice =
                        recognizedSlice &&
                        sliceKind == BinarySliceKind.CustomEncodingString &&
                        selector.Length > 0 &&
                        selector.EncodingCodePage > 0 &&
                        Enum.IsDefined((Coercion.Text)selector.TextCoercion) &&
                        TryValidateStableTextEncoding(
                            selector.EncodingCodePage,
                            (Coercion.Text)selector.TextCoercion);
                    var hasInactiveEncodingFields =
                        sliceKind != BinarySliceKind.CustomEncodingString &&
                        (selector.EncodingCodePage != 0 || selector.TextCoercion != -1);
                    if (!SelectorProducesType(selector.Source, typeof(byte[])) ||
                        !string.IsNullOrEmpty(selector.PropertyPath) ||
                        !recognizedSlice ||
                        (sliceKind != BinarySliceKind.ByteArray &&
                            !validNumericSlice &&
                            !validTextSlice &&
                            !validGuidSlice &&
                            !validTemporalSlice &&
                            !validCustomTextSlice) ||
                        hasInactiveEncodingFields ||
                        selector.Offset < 0 ||
                        selector.Length <= 0 ||
                        !string.IsNullOrEmpty(selector.DecimalOperation) ||
                        selector.OperandSource is not null ||
                        selector.HasDecimalOperand ||
                        selector.DecimalOperand != 0m ||
                        selector.HasIntegerOperand ||
                        selector.IntegerOperand != 0 ||
                        !string.IsNullOrEmpty(selector.TimeSpanProjection) ||
                        HasNumericArithmeticFields(selector) ||
                        HasStringOperationFields(selector))
                    {
                        throw new InvalidDataException(
                            "A binary-slice selector requires recognized byte-array, numeric, deterministic text, Guid, or raw temporal semantics, a non-negative offset, and a valid positive length.");
                    }
                    return;
                case "DecimalOperation":
                    if (selector.Source is null)
                        throw new InvalidDataException("A decimal-operation selector requires a source selector.");
                    ValidateSelector(selector.Source);
                    if (!Enum.TryParse(
                            selector.DecimalOperation,
                            ignoreCase: false,
                            out DecimalOperationKind decimalOperation) ||
                        !Enum.IsDefined(decimalOperation))
                    {
                        throw new InvalidDataException(
                            $"A decimal-operation selector contains unknown operation '{selector.DecimalOperation}'.");
                    }

                    var binaryDecimalOperation = decimalOperation is
                        DecimalOperationKind.Add or
                        DecimalOperationKind.Subtract or
                        DecimalOperationKind.Multiply or
                        DecimalOperationKind.Divide or
                        DecimalOperationKind.Modulo or
                        DecimalOperationKind.Discount;
                    var integerDecimalOperation = decimalOperation is
                        DecimalOperationKind.Round or
                        DecimalOperationKind.Truncate or
                        DecimalOperationKind.PowerInteger;
                    if (selector.OperandSource is not null)
                        ValidateSelector(selector.OperandSource);
                    var validDecimalOperands = binaryDecimalOperation
                        ? (selector.OperandSource is not null) != selector.HasDecimalOperand &&
                            !selector.HasIntegerOperand &&
                            (selector.OperandSource is null || SelectorProducesDecimal(selector.OperandSource))
                        : integerDecimalOperation
                            ? selector.OperandSource is null &&
                                !selector.HasDecimalOperand &&
                                selector.HasIntegerOperand
                            : selector.OperandSource is null &&
                                !selector.HasDecimalOperand &&
                                !selector.HasIntegerOperand;
                    if (!SelectorProducesDecimal(selector.Source) ||
                        !validDecimalOperands ||
                        !string.IsNullOrEmpty(selector.PropertyPath) ||
                        !string.IsNullOrEmpty(selector.BinarySliceKind) ||
                        selector.Offset != 0 ||
                        selector.Length != 0 ||
                        selector.EncodingCodePage != 0 ||
                        selector.TextCoercion != -1 ||
                        (!selector.HasDecimalOperand && selector.DecimalOperand != 0m) ||
                        (!selector.HasIntegerOperand && selector.IntegerOperand != 0) ||
                        !string.IsNullOrEmpty(selector.TimeSpanProjection) ||
                        HasNumericArithmeticFields(selector) ||
                        HasStringOperationFields(selector))
                    {
                        throw new InvalidDataException(
                            "A decimal-operation selector contains missing, conflicting, or inactive operand fields.");
                    }
                    return;
                case "TimeSpanProjection":
                    if (selector.Source is null)
                        throw new InvalidDataException("A TimeSpan projection selector requires a source selector.");
                    ValidateSelector(selector.Source);
                    if (!Enum.TryParse(
                            selector.TimeSpanProjection,
                            ignoreCase: false,
                            out TimeSpanProjectionKind timeSpanProjection) ||
                        !Enum.IsDefined(timeSpanProjection) ||
                        !SelectorProducesTimeSpan(selector.Source) ||
                        !string.IsNullOrEmpty(selector.PropertyPath) ||
                        !string.IsNullOrEmpty(selector.BinarySliceKind) ||
                        selector.Offset != 0 ||
                        selector.Length != 0 ||
                        selector.EncodingCodePage != 0 ||
                        selector.TextCoercion != -1 ||
                        !string.IsNullOrEmpty(selector.DecimalOperation) ||
                        selector.OperandSource is not null ||
                        selector.HasDecimalOperand ||
                        selector.DecimalOperand != 0m ||
                        selector.HasIntegerOperand ||
                        selector.IntegerOperand != 0 ||
                        HasNumericArithmeticFields(selector) ||
                        HasStringOperationFields(selector))
                    {
                        throw new InvalidDataException(
                            "A TimeSpan projection selector contains an unknown projection, an incompatible source, or inactive fields.");
                    }
                    return;
                case "NumericArithmetic":
                    if (selector.Source is null)
                        throw new InvalidDataException("A numeric-arithmetic selector requires a source selector.");
                    ValidateSelector(selector.Source);
                    if (selector.OperandSource is not null)
                        ValidateSelector(selector.OperandSource);
                    if (!Enum.TryParse(
                            selector.NumericOperation,
                            ignoreCase: false,
                            out NumericArithmeticOperationKind numericOperation) ||
                        !Enum.IsDefined(numericOperation))
                    {
                        throw new InvalidDataException(
                            $"A numeric-arithmetic selector contains unknown operation '{selector.NumericOperation}'.");
                    }

                    var sourceType = ConditionWireTypes.Resolve(selector.NumericSourceTypeCode);
                    var rightType = ConditionWireTypes.Resolve(selector.NumericRightTypeCode);
                    var resultType = ConditionWireTypes.Resolve(selector.NumericResultTypeCode);
                    var unaryNumericOperation = numericOperation is
                        NumericArithmeticOperationKind.Negate or
                        NumericArithmeticOperationKind.BitNot;
                    var bitwiseNumericOperation = numericOperation is >= NumericArithmeticOperationKind.BitAnd and <= NumericArithmeticOperationKind.BitNot;
                    var validNumericOperands = unaryNumericOperation
                        ? selector.OperandSource is null &&
                            !selector.HasNumericOperand &&
                            selector.NumericOperandPayload.Length == 0
                        : (selector.OperandSource is not null) != selector.HasNumericOperand &&
                            (selector.HasNumericOperand == (selector.NumericOperandPayload.Length > 0)) &&
                            (!selector.HasNumericOperand ||
                                TryValidateNumericLiteralPayload(
                                    selector.NumericOperandPayload,
                                    resultType));
                    if (!IsGenericNumber(sourceType) ||
                        !IsGenericNumber(rightType) ||
                        !IsGenericNumber(resultType) ||
                        (bitwiseNumericOperation &&
                            (sourceType != resultType || !IsGenericBinaryInteger(sourceType))) ||
                        !SelectorProducesNumericType(selector.Source, sourceType) ||
                        (selector.OperandSource is not null &&
                            !SelectorProducesNumericType(selector.OperandSource, rightType)) ||
                        (selector.HasNumericOperand && rightType != resultType) ||
                        !validNumericOperands ||
                        !string.IsNullOrEmpty(selector.PropertyPath) ||
                        !string.IsNullOrEmpty(selector.BinarySliceKind) ||
                        selector.Offset != 0 ||
                        selector.Length != 0 ||
                        selector.EncodingCodePage != 0 ||
                        selector.TextCoercion != -1 ||
                        !string.IsNullOrEmpty(selector.DecimalOperation) ||
                        selector.HasDecimalOperand ||
                        selector.DecimalOperand != 0m ||
                        selector.HasIntegerOperand ||
                        selector.IntegerOperand != 0 ||
                        !string.IsNullOrEmpty(selector.TimeSpanProjection) ||
                        HasStringOperationFields(selector))
                    {
                        throw new InvalidDataException(
                            "A numeric-arithmetic selector contains incompatible types, missing or conflicting operands, or inactive fields.");
                    }
                    return;
                case "TemporalArithmetic":
                    if (selector.Source is null)
                        throw new InvalidDataException("A temporal-arithmetic selector requires a source selector.");
                    ValidateSelector(selector.Source);
                    if (selector.OperandSource is not null)
                        ValidateSelector(selector.OperandSource);
                    if (!Enum.TryParse(
                            selector.TemporalOperation,
                            ignoreCase: false,
                            out Selector<T>.TemporalArithmeticOperationKind temporalOperation) ||
                        !Enum.IsDefined(temporalOperation))
                    {
                        throw new InvalidDataException(
                            $"A temporal-arithmetic selector contains unknown operation '{selector.TemporalOperation}'.");
                    }

                    Type temporalSourceType = ConditionWireTypes.Resolve(selector.TemporalSourceTypeCode);
                    Type temporalRightType = ConditionWireTypes.Resolve(selector.TemporalRightTypeCode);
                    Type temporalResultType = ConditionWireTypes.Resolve(selector.TemporalResultTypeCode);
                    bool validTemporalContract;
                    try
                    {
                        Selector<T>.ValidateTemporalArithmeticContract(
                            temporalOperation,
                            temporalSourceType,
                            temporalRightType,
                            temporalResultType);
                        validTemporalContract = true;
                    }
                    catch (ArgumentException)
                    {
                        validTemporalContract = false;
                    }
                    bool validTemporalOperands =
                        (selector.OperandSource is not null) != selector.HasTemporalOperand &&
                        (selector.HasTemporalOperand == (selector.TemporalOperandPayload.Length > 0)) &&
                        (selector.OperandSource is null ||
                            SelectorProducesType(selector.OperandSource, temporalRightType)) &&
                        (!selector.HasTemporalOperand ||
                            TryValidateTypedLiteralPayload(selector.TemporalOperandPayload, temporalRightType));
                    if (!validTemporalContract ||
                        !SelectorProducesType(selector.Source, temporalSourceType) ||
                        !validTemporalOperands ||
                        !string.IsNullOrEmpty(selector.PropertyPath) ||
                        !string.IsNullOrEmpty(selector.BinarySliceKind) ||
                        selector.Offset != 0 || selector.Length != 0 ||
                        selector.EncodingCodePage != 0 || selector.TextCoercion != -1 ||
                        !string.IsNullOrEmpty(selector.DecimalOperation) ||
                        selector.HasDecimalOperand || selector.DecimalOperand != 0m ||
                        selector.HasIntegerOperand || selector.IntegerOperand != 0 ||
                        !string.IsNullOrEmpty(selector.TimeSpanProjection) ||
                        HasNumericArithmeticFields(selector) ||
                        HasStringOperationFields(selector))
                    {
                        throw new InvalidDataException(
                            "A temporal-arithmetic selector contains incompatible types, missing or conflicting operands, or inactive fields.");
                    }
                    return;
                case "StringOperation":
                    if (selector.Source is null)
                        throw new InvalidDataException("A string-operation selector requires a source selector.");
                    ValidateSelector(selector.Source);
                    if (!Enum.TryParse(
                            selector.StringOperation,
                            ignoreCase: false,
                            out StringOperationKind stringOperation) ||
                        !Enum.IsDefined(stringOperation) ||
                        stringOperation == StringOperationKind.External ||
                        !SelectorProducesString(selector.Source))
                    {
                        throw new InvalidDataException(
                            $"A string-operation selector contains unknown, external, or incompatible operation '{selector.StringOperation}'.");
                    }

                    if (selector.OperandSource is not null)
                        ValidateSelector(selector.OperandSource);
                    var selectorOperandExpected = stringOperation == StringOperationKind.ConcatSelector;
                    var validSelectorOperand =
                        (selector.OperandSource is not null) == selectorOperandExpected &&
                        (selector.OperandSource is null || SelectorProducesString(selector.OperandSource));

                    var textOperandExpected = stringOperation is
                        StringOperationKind.IndexOf or
                        StringOperationKind.LastIndexOf or
                        StringOperationKind.TrimAscii or
                        StringOperationKind.TrimStartAscii or
                        StringOperationKind.TrimEndAscii or
                        StringOperationKind.Trim or
                        StringOperationKind.TrimStart or
                        StringOperationKind.TrimEnd or
                        StringOperationKind.Replace or
                        StringOperationKind.Insert or
                        StringOperationKind.MatchValue;
                    var secondTextOperandExpected = stringOperation == StringOperationKind.Replace;
                    var longOperandExpected = stringOperation is
                        StringOperationKind.Substring or
                        StringOperationKind.Left or
                        StringOperationKind.Right or
                        StringOperationKind.RuneSubstring or
                        StringOperationKind.RuneLeft or
                        StringOperationKind.RuneRight or
                        StringOperationKind.SortKey or
                        StringOperationKind.PadLeft or
                        StringOperationKind.PadRight or
                        StringOperationKind.Insert or
                        StringOperationKind.Remove or
                        StringOperationKind.MatchValue;
                    var secondLongOperandAllowed = stringOperation is
                        StringOperationKind.Substring or
                        StringOperationKind.RuneSubstring or
                        StringOperationKind.Remove or
                        StringOperationKind.MatchValue;
                    var secondLongOperandRequired = stringOperation == StringOperationKind.MatchValue;
                    var characterOperandExpected = stringOperation is
                        StringOperationKind.PadLeft or
                        StringOperationKind.PadRight;
                    var comparisonOptionsAllowed = stringOperation is
                        StringOperationKind.IndexOf or
                        StringOperationKind.LastIndexOf or
                        StringOperationKind.Replace;
                    var cultureAllowed = comparisonOptionsAllowed ||
                        stringOperation is
                            StringOperationKind.Lower or
                            StringOperationKind.Upper or
                            StringOperationKind.SortKey;
                    var validTextState = selector.TextOperandState is >= 0 and <= 2 &&
                        ((selector.TextOperandState != 0) == textOperandExpected) &&
                        (selector.TextOperandState == 2 || selector.TextOperand.Length == 0);
                    var validSecondTextState = selector.SecondTextOperandState is >= 0 and <= 2 &&
                        ((selector.SecondTextOperandState != 0) == secondTextOperandExpected) &&
                        (selector.SecondTextOperandState == 2 || selector.SecondTextOperand.Length == 0);
                    if (!validSelectorOperand ||
                        !validTextState ||
                        !validSecondTextState ||
                        selector.HasLongOperand != longOperandExpected ||
                        (!selector.HasLongOperand && selector.LongOperand != 0) ||
                        (selector.HasSecondLongOperand && !secondLongOperandAllowed) ||
                        (secondLongOperandRequired && !selector.HasSecondLongOperand) ||
                        (!selector.HasSecondLongOperand && selector.SecondLongOperand != 0) ||
                        selector.HasCharacterOperand != characterOperandExpected ||
                        (selector.HasCharacterOperand && selector.CharacterOperand is < char.MinValue or > char.MaxValue) ||
                        (!selector.HasCharacterOperand && selector.CharacterOperand != 0) ||
                        (!comparisonOptionsAllowed && selector.StringIgnoreCase) ||
                        (!cultureAllowed && selector.StringCultureName.Length != 0) ||
                        (stringOperation != StringOperationKind.MatchValue &&
                            (selector.StringRegexOptions != 0 || selector.StringRegexMatchTimeoutTicks != 0)) ||
                        (stringOperation == StringOperationKind.MatchValue &&
                            !TryValidateMatchValueSelector(selector)) ||
                        !string.IsNullOrEmpty(selector.PropertyPath) ||
                        !string.IsNullOrEmpty(selector.BinarySliceKind) ||
                        selector.Offset != 0 ||
                        selector.Length != 0 ||
                        selector.EncodingCodePage != 0 ||
                        selector.TextCoercion != -1 ||
                        !string.IsNullOrEmpty(selector.DecimalOperation) ||
                        selector.HasDecimalOperand ||
                        selector.DecimalOperand != 0m ||
                        selector.HasIntegerOperand ||
                        selector.IntegerOperand != 0 ||
                        !string.IsNullOrEmpty(selector.TimeSpanProjection) ||
                        HasNumericArithmeticFields(selector))
                    {
                        throw new InvalidDataException(
                            "A string-operation selector contains missing, conflicting, or inactive fields.");
                    }

                    if (selector.StringCultureName.Length != 0)
                        _ = CultureInfo.GetCultureInfo(selector.StringCultureName);
                    return;
                case "BuiltInOperation":
                    if (selector.Source is null)
                        throw new InvalidDataException("A built-in selector operation requires a source selector.");
                    ValidateSelector(selector.Source);
                    if (!Enum.TryParse(
                            selector.BuiltInOperation,
                            ignoreCase: false,
                            out BuiltInSelectorOperationKind builtInOperation) ||
                        !Enum.IsDefined(builtInOperation))
                    {
                        throw new InvalidDataException(
                            $"A built-in selector contains unknown operation '{selector.BuiltInOperation}'.");
                    }
                    Type builtInSourceType = ConditionWireTypes.Resolve(selector.BuiltInSourceTypeCode);
                    Type builtInResultType = ConditionWireTypes.Resolve(selector.BuiltInResultTypeCode);
                    try
                    {
                        _ = new Selector<T>.BuiltInSelectorDescriptor(
                            new DirectPropertySelectorDescriptor(".Value"),
                            builtInOperation,
                            builtInSourceType,
                            builtInResultType,
                            selector.BuiltInFirstIntegerOperand,
                            selector.BuiltInSecondIntegerOperand,
                            selector.HasBuiltInTextOperand ? selector.BuiltInTextOperand : null);
                    }
                    catch (ArgumentException exception)
                    {
                        throw new InvalidDataException(
                            "A built-in selector contains incompatible types or operands.",
                            exception);
                    }
                    if ((!selector.HasBuiltInTextOperand && selector.BuiltInTextOperand.Length != 0) ||
                        !string.IsNullOrEmpty(selector.PropertyPath) ||
                        !string.IsNullOrEmpty(selector.BinarySliceKind) ||
                        selector.Offset != 0 ||
                        selector.Length != 0 ||
                        selector.EncodingCodePage != 0 ||
                        selector.TextCoercion != -1 ||
                        !string.IsNullOrEmpty(selector.DecimalOperation) ||
                        selector.OperandSource is not null ||
                        selector.HasDecimalOperand ||
                        selector.DecimalOperand != 0m ||
                        selector.HasIntegerOperand ||
                        selector.IntegerOperand != 0 ||
                        !string.IsNullOrEmpty(selector.TimeSpanProjection) ||
                        HasNumericArithmeticFields(selector) ||
                        HasStringOperationFields(selector))
                    {
                        throw new InvalidDataException("A built-in selector contains inactive fields.");
                    }
                    return;
                default:
                    throw new InvalidDataException($"Unknown condition selector kind '{selector.Kind}'.");
            }
        }

        /// <summary>
        /// Detects version-7 numeric-arithmetic fields on a selector owned by another family.<br/>
        /// Keeping this check centralized makes inactive-field rejection complete as the recursive selector grammar grows.<br/>
        /// </summary>
        /// <param name="selector">Selector node whose inactive fields are being checked.<br/></param>
        /// <returns><see langword="true"/> when any numeric-arithmetic-only field is populated.<br/></returns>
        private static bool HasNumericArithmeticFields(ConditionWireSelector selector)
            => !string.IsNullOrEmpty(selector.NumericOperation) ||
                !string.IsNullOrEmpty(selector.NumericSourceTypeCode) ||
                !string.IsNullOrEmpty(selector.NumericRightTypeCode) ||
                !string.IsNullOrEmpty(selector.NumericResultTypeCode) ||
                selector.HasNumericOperand ||
                selector.NumericOperandPayload.Length != 0;

        /// <summary>
        /// Detects version-14 temporal-arithmetic fields on a selector owned by another family.<br/>
        /// Central rejection prevents dormant fields from changing a canonical selector's meaning in a later runtime.<br/>
        /// </summary>
        /// <param name="selector">Selector node whose inactive fields are being checked.<br/></param>
        /// <returns><see langword="true"/> when any temporal-arithmetic-only field is populated.<br/></returns>
        private static bool HasTemporalArithmeticFields(ConditionWireSelector selector)
            => !string.IsNullOrEmpty(selector.TemporalOperation) ||
                !string.IsNullOrEmpty(selector.TemporalSourceTypeCode) ||
                !string.IsNullOrEmpty(selector.TemporalRightTypeCode) ||
                !string.IsNullOrEmpty(selector.TemporalResultTypeCode) ||
                selector.HasTemporalOperand ||
                selector.TemporalOperandPayload.Length != 0;

        /// <summary>
        /// Detects version-8 string-operation fields on a selector owned by another family.<br/>
        /// Rejecting inactive data keeps canonical fingerprints unambiguous and prevents ignored wire values from acquiring accidental meaning later.<br/>
        /// </summary>
        /// <param name="selector">Selector node whose inactive fields are being checked.<br/></param>
        /// <returns><see langword="true"/> when any string-operation-only field is populated.<br/></returns>
        private static bool HasStringOperationFields(ConditionWireSelector selector)
            => !string.IsNullOrEmpty(selector.StringOperation) ||
                selector.TextOperandState != 0 ||
                selector.TextOperand.Length != 0 ||
                selector.SecondTextOperandState != 0 ||
                selector.SecondTextOperand.Length != 0 ||
                selector.HasLongOperand ||
                selector.LongOperand != 0 ||
                selector.HasSecondLongOperand ||
                selector.SecondLongOperand != 0 ||
                selector.HasCharacterOperand ||
                selector.CharacterOperand != 0 ||
                selector.StringIgnoreCase ||
                selector.StringCultureName.Length != 0 ||
                selector.StringRegexOptions != 0 ||
                selector.StringRegexMatchTimeoutTicks != 0;

        private static bool TryValidateMatchValueSelector(ConditionWireSelector selector)
        {
            try
            {
                var regex = Selector<T>.StringOperationSelectorDescriptor.CreateRuntimeRegex(
                    selector.TextOperand,
                    (RegexOptions)selector.StringRegexOptions,
                    TimeSpan.FromTicks(selector.StringRegexMatchTimeoutTicks));
                return selector.HasLongOperand &&
                    selector.LongOperand >= 0 &&
                    regex.GetGroupNumbers().Contains(checked((int)selector.LongOperand)) &&
                    selector.HasSecondLongOperand &&
                    selector.SecondLongOperand >= 0;
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                OverflowException)
            {
                return false;
            }
        }

        /// <summary>
        /// Determines whether one CLR type satisfies its own generic-math number contract.<br/>
        /// Persisted type identities are validated before reflection closes the typed replay descriptor.<br/>
        /// </summary>
        /// <param name="type">Resolved CLR type from the durable selector node.<br/></param>
        /// <returns><see langword="true"/> when the type implements <see cref="INumber{TSelf}"/> for itself.<br/></returns>
        private static bool IsGenericNumber(Type type)
            => typeof(INumber<>).MakeGenericType(type).IsAssignableFrom(type);

        /// <summary>
        /// Determines whether one CLR type satisfies its own generic-math binary-integer contract.<br/>
        /// This guards reflected replay of persisted bitwise selector nodes before a generic type is closed.<br/>
        /// </summary>
        /// <param name="type">Resolved CLR type from the durable selector node.<br/></param>
        /// <returns><see langword="true"/> when the type implements <see cref="IBinaryInteger{TSelf}"/> for itself.<br/></returns>
        private static bool IsGenericBinaryInteger(Type type)
            => type.GetInterfaces().Any(static contract =>
                contract.IsGenericType &&
                contract.GetGenericTypeDefinition() == typeof(IBinaryInteger<>));

        /// <summary>
        /// Verifies that one Inheto literal payload restores as the exact promoted numeric result type.<br/>
        /// Malformed, truncated, mismatched, or unsupported literal bytes fail descriptor validation before display or replay can observe them.<br/>
        /// </summary>
        /// <param name="payload">Persisted Inheto bytes for the arithmetic literal.<br/></param>
        /// <param name="resultType">Exact CLR numeric type required by the arithmetic node.<br/></param>
        /// <returns><see langword="true"/> only when restoration succeeds as the required non-null type.<br/></returns>
        private static bool TryValidateNumericLiteralPayload(
            byte[] payload,
            Type resultType)
        {
            try
            {
                var restored = InhetoBinary
                    .FromStream(new BufferStream(payload))
                    .ToObject(resultType);
                return restored is not null && restored.GetType() == resultType;
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                InvalidCastException or
                InvalidDataException or
                NotSupportedException or
                OverflowException)
            {
                return false;
            }
        }

        /// <summary>
        /// Verifies that one Inheto payload restores as the exact declared operand type.<br/>
        /// Temporal literals use this shared exact-type boundary so malformed or coercing wire data fails before replay.<br/>
        /// </summary>
        /// <param name="payload">Persisted typed literal bytes.<br/></param>
        /// <param name="expectedType">Exact CLR type required by the semantic operation.<br/></param>
        /// <returns><see langword="true"/> only when restoration succeeds as the required non-null type.<br/></returns>
        private static bool TryValidateTypedLiteralPayload(byte[] payload, Type expectedType)
        {
            try
            {
                object? restored = InhetoBinary
                    .FromStream(new BufferStream(payload))
                    .ToObject(expectedType);
                return restored is not null && restored.GetType() == expectedType;
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                InvalidCastException or
                InvalidDataException or
                NotSupportedException or
                OverflowException)
            {
                return false;
            }
        }

        /// <summary>
        /// Verifies the exact numeric result type produced by one recursive selector source.<br/>
        /// Direct properties take their type from the consuming selector contract; computed nodes must state or imply the exact same CLR type.<br/>
        /// </summary>
        /// <param name="selector">Validated source selector.<br/></param>
        /// <param name="expectedType">Exact CLR type required by the arithmetic node.<br/></param>
        /// <returns><see langword="true"/> when the source can produce the required numeric type.<br/></returns>
        private static bool SelectorProducesNumericType(
            ConditionWireSelector selector,
            Type expectedType)
        {
            if (selector.Kind is "Property" or "SemanticIndex")
                return true;
            if (selector.Kind == "NumericArithmetic")
            {
                return string.Equals(
                    selector.NumericResultTypeCode,
                    ConditionWireTypes.GetCode(expectedType),
                    StringComparison.Ordinal);
            }
            if (selector.Kind == "TemporalArithmetic")
            {
                return string.Equals(
                    selector.TemporalResultTypeCode,
                    ConditionWireTypes.GetCode(expectedType),
                    StringComparison.Ordinal);
            }
            if (selector.Kind == "BuiltInOperation")
            {
                return string.Equals(
                    selector.BuiltInResultTypeCode,
                    ConditionWireTypes.GetCode(expectedType),
                    StringComparison.Ordinal);
            }
            if (selector.Kind == "BinarySlice" &&
                Enum.TryParse(selector.BinarySliceKind, ignoreCase: false, out BinarySliceKind sliceKind))
            {
                return Condition<T>.TryGetNumericBinarySliceShape(
                        sliceKind,
                        out var sliceType,
                        out _) &&
                    sliceType == expectedType;
            }
            if (selector.Kind == "TimeSpanProjection" &&
                Enum.TryParse(
                    selector.TimeSpanProjection,
                    ignoreCase: false,
                    out TimeSpanProjectionKind projection))
            {
                return expectedType ==
                    (projection == TimeSpanProjectionKind.Ticks ? typeof(long) : typeof(double));
            }
            if (selector.Kind == "StringOperation" &&
                expectedType == typeof(long) &&
                Enum.TryParse(
                    selector.StringOperation,
                    ignoreCase: false,
                    out StringOperationKind stringOperation))
            {
                return stringOperation is
                    StringOperationKind.Length or
                    StringOperationKind.RuneCount or
                    StringOperationKind.IndexOf or
                    StringOperationKind.LastIndexOf;
            }
            return false;
        }

        /// <summary>
        /// Verifies the exact CLR result type produced by a recursive selector node.<br/>
        /// Direct properties inherit their type from the consuming semantic contract; computed nodes must expose an exact durable result identity.<br/>
        /// </summary>
        /// <param name="selector">Validated recursive selector node.<br/></param>
        /// <param name="expectedType">Exact CLR type required by its consumer.<br/></param>
        /// <returns><see langword="true"/> when the selector can produce that type without an implicit wire-time coercion.<br/></returns>
        private static bool SelectorProducesType(ConditionWireSelector selector, Type expectedType)
        {
            if (selector.Kind is "Property" or "SemanticIndex")
                return true;
            if (selector.Kind == "StringOperation" &&
                expectedType == typeof(byte[]) &&
                Enum.TryParse(
                    selector.StringOperation,
                    ignoreCase: false,
                    out StringOperationKind binaryStringOperation))
            {
                return binaryStringOperation is
                    StringOperationKind.SortKey or
                    StringOperationKind.FromHex or
                    StringOperationKind.FromBase64 or
                    StringOperationKind.AsUtf8;
            }
            if (selector.Kind == "TemporalArithmetic")
            {
                return string.Equals(
                    selector.TemporalResultTypeCode,
                    ConditionWireTypes.GetCode(expectedType),
                    StringComparison.Ordinal);
            }
            if (IsGenericNumber(expectedType) && SelectorProducesNumericType(selector, expectedType))
                return true;
            if (selector.Kind == "BuiltInOperation")
            {
                return string.Equals(
                    selector.BuiltInResultTypeCode,
                    ConditionWireTypes.GetCode(expectedType),
                    StringComparison.Ordinal);
            }
            if (selector.Kind == "BinarySlice" &&
                Enum.TryParse(selector.BinarySliceKind, ignoreCase: false, out BinarySliceKind sliceKind))
            {
                return Condition<T>.TryGetDeterministicTemporalBinarySliceShape(
                        sliceKind,
                        out Type temporalType,
                        out _) &&
                    temporalType == expectedType;
            }
            return false;
        }

        /// <summary>
        /// Determines whether one validated selector node produces a decimal value suitable as a decimal-operation source.<br/>
        /// Direct properties are typed by the consuming decimal condition contract; nested decimal operations remain decimal except for <see cref="DecimalOperationKind.Sign"/>, whose integral result cannot feed another decimal operation.<br/>
        /// </summary>
        /// <param name="selector">Validated selector node whose result family is being inspected.<br/></param>
        /// <returns><see langword="true"/> for direct properties and decimal-returning operation nodes; otherwise <see langword="false"/>.<br/></returns>
        private static bool SelectorProducesDecimal(ConditionWireSelector selector)
        {
            if (selector.Kind is "Property" or "SemanticIndex")
                return true;
            return selector.Kind == "DecimalOperation" &&
                Enum.TryParse(
                    selector.DecimalOperation,
                    ignoreCase: false,
                    out DecimalOperationKind operation) &&
                operation != DecimalOperationKind.Sign;
        }

        /// <summary>
        /// Determines whether one validated selector node produces a TimeSpan suitable for a scalar projection.<br/>
        /// Direct properties are typed by the consuming TimeSpan contract; binary sources must use the explicit raw TimeSpan-ticks representation.<br/>
        /// </summary>
        /// <param name="selector">Validated selector node whose result family is being inspected.<br/></param>
        /// <returns><see langword="true"/> for direct properties and deterministic TimeSpan binary slices.<br/></returns>
        private static bool SelectorProducesTimeSpan(ConditionWireSelector selector)
        {
            if (selector.Kind is "Property" or "SemanticIndex")
                return true;
            if (selector.Kind == "TemporalArithmetic")
            {
                return string.Equals(
                    selector.TemporalResultTypeCode,
                    ConditionWireTypes.GetCode(typeof(TimeSpan)),
                    StringComparison.Ordinal);
            }
            return selector.Kind == "BinarySlice" &&
                Enum.TryParse(
                    selector.BinarySliceKind,
                    ignoreCase: false,
                    out BinarySliceKind sliceKind) &&
                sliceKind == BinarySliceKind.TimeSpanTicks;
        }

        /// <summary>
        /// Determines whether one validated recursive selector produces a string that may feed another string operation.<br/>
        /// Numeric position/count, binary conversion, and runtime-only external results are excluded from durable string chaining.<br/>
        /// </summary>
        /// <param name="selector">The wire selector whose result family should be classified.<br/></param>
        /// <returns><see langword="true"/> when the selector produces a durable string result.<br/></returns>
        private static bool SelectorProducesString(ConditionWireSelector selector)
        {
            if (selector.Kind == "Property")
                return true;
            if (selector.Kind == "BuiltInOperation")
            {
                return string.Equals(
                    selector.BuiltInResultTypeCode,
                    ConditionWireTypes.GetCode(typeof(string)),
                    StringComparison.Ordinal);
            }
            if (selector.Kind != "StringOperation" ||
                !Enum.TryParse(
                    selector.StringOperation,
                    ignoreCase: false,
                    out StringOperationKind operation))
            {
                return false;
            }

            return operation is not
                StringOperationKind.Length and not
                StringOperationKind.RuneCount and not
                StringOperationKind.IndexOf and not
                StringOperationKind.LastIndexOf and not
                StringOperationKind.SortKey and not
                StringOperationKind.FromHex and not
                StringOperationKind.FromBase64 and not
                StringOperationKind.AsUtf8 and not
                StringOperationKind.External;
        }

        /// <summary>
        /// Verifies that one persisted text-encoding identity can be recreated by the current LibraDex runtime without substituting different semantics.<br/>
        /// Missing code-page providers and unsupported registrations fail descriptor validation before a condition can be rendered or executed.<br/>
        /// </summary>
        /// <param name="codePage">The persisted registered numeric code page.<br/></param>
        /// <param name="coercion">The persisted malformed-input policy.<br/></param>
        /// <returns><c>true</c> when LibraDex can recreate the complete encoding contract; otherwise <c>false</c>.<br/></returns>
        private static bool TryValidateStableTextEncoding(
            int codePage,
            Coercion.Text coercion)
        {
            try
            {
                _ = LibraDexTextEncoding.ForCodePage(codePage, coercion);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
        }

        /// <summary>
        /// Enforces exact family-specific option cardinality for one decoded predicate leaf.<br/>
        /// Exact counts prevent malformed packages from silently substituting default values or hiding unrecognized future semantics.<br/>
        /// </summary>
        /// <param name="leaf">Decoded predicate leaf.<br/></param>
        /// <param name="text">Required text-option count.<br/></param>
        /// <param name="integer">Required integer-option count.<br/></param>
        /// <param name="boolean">Required Boolean-option count.<br/></param>
        private static void RequireOptionCounts(
            ConditionWireLeaf leaf,
            int text,
            int integer,
            int boolean)
        {
            if (leaf.TextOptions.Length != text ||
                leaf.IntegerOptions.Length != integer ||
                leaf.BooleanOptions.Length != boolean)
            {
                throw new InvalidDataException(
                    $"Condition leaf family '{leaf.Family}' requires option counts " +
                    $"text={text}, integer={integer}, Boolean={boolean}; found " +
                    $"text={leaf.TextOptions.Length}, integer={leaf.IntegerOptions.Length}, Boolean={leaf.BooleanOptions.Length}.");
            }
        }
    }

    /// <summary>
    /// Collects strict positional and named bindings for one immutable condition descriptor.<br/>
    /// No executable condition is exposed until <see cref="Build"/> verifies that every required position has been assigned exactly once.<br/>
    /// </summary>
    /// <typeparam name="T">Record type selected by the condition.<br/></typeparam>
    public sealed class ConditionRehydrator<T>
    {
        private readonly ConditionDescriptor<T> owner;
        private readonly ConditionWireDocument document;
        private readonly object?[] bindings;
        private readonly bool[] assigned;
        private int nextPosition;

        internal ConditionRehydrator(ConditionDescriptor<T> owner, ConditionWireDocument document)
        {
            this.owner = owner;
            this.document = document;
            bindings = new object?[document.Parameters.Count];
            assigned = new bool[document.Parameters.Count];
        }

        /// <summary>
        /// Supplies one direct value to the next unassigned left-to-right parameter position.<br/>
        /// </summary>
        public ConditionRehydrator<T> Next<TValue>(TValue value)
            => SetNext(value);

        /// <summary>
        /// Supplies one process-local deferred lambda to the next unassigned left-to-right parameter position without invoking it.<br/>
        /// </summary>
        public ConditionRehydrator<T> NextDeferred<TValue>(Func<TValue> valueFactory)
            => SetNext(new ValueOrExpression<TValue>(valueFactory));

        /// <summary>
        /// Supplies an existing Abraxas static/deferred wrapper to the next unassigned left-to-right parameter position without invoking it.<br/>
        /// </summary>
        public ConditionRehydrator<T> NextDeferred<TValue>(ValueOrExpression<TValue> valueOrExpression)
            => SetNext(valueOrExpression);

        /// <summary>
        /// Supplies one direct value to a uniquely named parameter without changing the relative order of remaining positional bindings.<br/>
        /// </summary>
        public ConditionRehydrator<T> Set<TValue>(string name, TValue value)
            => SetNamed(name, value);

        /// <summary>
        /// Supplies one process-local deferred lambda to a uniquely named parameter without invoking it.<br/>
        /// </summary>
        public ConditionRehydrator<T> SetDeferred<TValue>(string name, Func<TValue> valueFactory)
            => SetNamed(name, new ValueOrExpression<TValue>(valueFactory));

        /// <summary>
        /// Supplies an existing Abraxas static/deferred wrapper to a uniquely named parameter without invoking it.<br/>
        /// </summary>
        public ConditionRehydrator<T> SetDeferred<TValue>(string name, ValueOrExpression<TValue> valueOrExpression)
            => SetNamed(name, valueOrExpression);

        /// <summary>
        /// Returns the executable completed condition only when every required binding has been supplied exactly once.<br/>
        /// Missing bindings throw with the first missing position, optional name, expected CLR type, and diagnostic condition context.<br/>
        /// Deferred scalar and whole-set bindings remain live until execution preparation; building does not invoke their factories or enumerate deferred membership values.<br/>
        /// Direct collection bindings supplied with Next or Set retain their established build-time copy semantics.<br/>
        /// </summary>
        public Condition<T>.EndCondition Build()
        {
            for (var position = 0; position < assigned.Length; position++)
            {
                if (!assigned[position])
                    throw Missing(document.Parameters[position]);
            }

            return owner.Build(bindings);
        }

        private ConditionRehydrator<T> SetNext(object? value)
        {
            while (nextPosition < assigned.Length && assigned[nextPosition])
                nextPosition++;
            if (nextPosition >= assigned.Length)
                throw new ConditionBindingException(
                    $"Condition requires '{assigned.Length}' bindings; no position remains for an additional value.");

            Assign(nextPosition, value);
            nextPosition++;
            return this;
        }

        private ConditionRehydrator<T> SetNamed(string name, object? value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            var parameter = document.Parameters.FirstOrDefault(
                candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
            if (parameter is null)
                throw new ConditionBindingException($"Condition has no parameter named '{name}'.");

            Assign(parameter.Position, value);
            return this;
        }

        private void Assign(int position, object? value)
        {
            var parameter = document.Parameters[position];
            if (assigned[position])
                throw new ConditionBindingException(
                    $"Condition parameter at position '{position}'{FormatName(parameter.Name)} has already been supplied.");

            var expected = ConditionWireTypes.Resolve(parameter.TypeCode);
            var actual = ConditionWireTypes.GetSuppliedValueType(value);
            if (!ConditionWireTypes.IsCompatible(expected, value))
            {
                throw new ConditionBindingException(
                    $"Condition parameter at position '{position}'{FormatName(parameter.Name)} expected " +
                    $"'{expected.FullName}', but received '{actual?.FullName ?? "<null>"}'. Context: {parameter.DiagnosticContext}");
            }

            bindings[position] = value;
            assigned[position] = true;
        }

        private static ConditionBindingException Missing(ConditionWireParameter parameter)
            => new(
                $"Condition parameter at position '{parameter.Position}'{FormatName(parameter.Name)} was not supplied. " +
                $"Expected '{ConditionWireTypes.Resolve(parameter.TypeCode).FullName}'. Context: {parameter.DiagnosticContext}");

        private static string FormatName(string? name)
            => string.IsNullOrWhiteSpace(name) ? string.Empty : $" ('{name}')";
    }

    internal sealed class ConditionWireDocument
    {
        /// <summary>
        /// Creates an empty mutable wire document for Inheto deserialization.<br/>
        /// Runtime callers receive only the validated immutable descriptor facade rather than this transport object.<br/>
        /// </summary>
        public ConditionWireDocument()
        {
        }

        public int FormatVersion { get; set; }
        public string RecordTypeCode { get; set; } = string.Empty;
        public ConditionWireSequence Sequence { get; set; } = new();
        public List<ConditionWireParameter> Parameters { get; set; } = new();
        public bool SystemPersistable { get; set; }
    }

    internal sealed class ConditionWireSequence
    {
        /// <summary>
        /// Creates an empty semantic sequence for Inheto deserialization.<br/>
        /// Validation and replay reject structurally invalid sequences before executable state is returned.<br/>
        /// </summary>
        public ConditionWireSequence()
        {
        }

        public ConditionWireItem[] Items { get; set; } = Array.Empty<ConditionWireItem>();
    }

    internal sealed class ConditionWireItem
    {
        /// <summary>
        /// Creates an empty discriminated semantic item for Inheto deserialization.<br/>
        /// Its kind and matching leaf or group payload are validated by the descriptor replay boundary.<br/>
        /// </summary>
        public ConditionWireItem()
        {
        }

        public string Kind { get; set; } = string.Empty;
        public ConditionWireLeaf? Leaf { get; set; }
        public ConditionWireSequence? Group { get; set; }
    }

    internal sealed class ConditionWireLeaf
    {
        /// <summary>
        /// Creates an empty backend-neutral predicate leaf for Inheto deserialization.<br/>
        /// Family-specific replay validates the operator, selector, options, value types, and operand cardinality.<br/>
        /// </summary>
        public ConditionWireLeaf()
        {
        }

        public string Family { get; set; } = string.Empty;
        public string Operator { get; set; } = string.Empty;
        public string ValueTypeCode { get; set; } = string.Empty;
        public ConditionWireSelector Selector { get; set; } = new();
        public ConditionWireSelector? OperandSelector { get; set; }
        public ConditionWireValue[] Operands { get; set; } = Array.Empty<ConditionWireValue>();
        public string[] TextOptions { get; set; } = Array.Empty<string>();
        public int[] IntegerOptions { get; set; } = Array.Empty<int>();
        public bool[] BooleanOptions { get; set; } = Array.Empty<bool>();
    }

    internal sealed class ConditionWireSelector
    {
        /// <summary>
        /// Creates an empty recursive selector node for deterministic wire decoding.<br/>
        /// Descriptor validation requires one known discriminator and the exact active fields for that selector family before the node can be rendered or replayed.<br/>
        /// </summary>
        public ConditionWireSelector()
        {
        }

        public string Kind { get; set; } = string.Empty;
        public string PropertyPath { get; set; } = string.Empty;
        public ConditionWireSelector? Source { get; set; }
        public string BinarySliceKind { get; set; } = string.Empty;
        public long Offset { get; set; }
        public long Length { get; set; }
        public int EncodingCodePage { get; set; }
        public int TextCoercion { get; set; } = -1;
        public string DecimalOperation { get; set; } = string.Empty;
        public ConditionWireSelector? OperandSource { get; set; }
        public bool HasDecimalOperand { get; set; }
        public decimal DecimalOperand { get; set; }
        public bool HasIntegerOperand { get; set; }
        public int IntegerOperand { get; set; }
        public string TimeSpanProjection { get; set; } = string.Empty;
        public string NumericOperation { get; set; } = string.Empty;
        public string NumericSourceTypeCode { get; set; } = string.Empty;
        public string NumericRightTypeCode { get; set; } = string.Empty;
        public string NumericResultTypeCode { get; set; } = string.Empty;
        public bool HasNumericOperand { get; set; }
        public byte[] NumericOperandPayload { get; set; } = Array.Empty<byte>();
        public string TemporalOperation { get; set; } = string.Empty;
        public string TemporalSourceTypeCode { get; set; } = string.Empty;
        public string TemporalRightTypeCode { get; set; } = string.Empty;
        public string TemporalResultTypeCode { get; set; } = string.Empty;
        public bool HasTemporalOperand { get; set; }
        public byte[] TemporalOperandPayload { get; set; } = Array.Empty<byte>();
        public string StringOperation { get; set; } = string.Empty;
        public int TextOperandState { get; set; }
        public string TextOperand { get; set; } = string.Empty;
        public int SecondTextOperandState { get; set; }
        public string SecondTextOperand { get; set; } = string.Empty;
        public bool HasLongOperand { get; set; }
        public long LongOperand { get; set; }
        public bool HasSecondLongOperand { get; set; }
        public long SecondLongOperand { get; set; }
        public bool HasCharacterOperand { get; set; }
        public int CharacterOperand { get; set; }
        public bool StringIgnoreCase { get; set; }
        public string StringCultureName { get; set; } = string.Empty;
        public int StringRegexOptions { get; set; }
        public long StringRegexMatchTimeoutTicks { get; set; }
        public string BuiltInOperation { get; set; } = string.Empty;
        public string BuiltInSourceTypeCode { get; set; } = string.Empty;
        public string BuiltInResultTypeCode { get; set; } = string.Empty;
        public long BuiltInFirstIntegerOperand { get; set; }
        public long BuiltInSecondIntegerOperand { get; set; }
        public bool HasBuiltInTextOperand { get; set; }
        public string BuiltInTextOperand { get; set; } = string.Empty;

        /// <summary>
        /// Creates one normalized direct-property selector node.<br/>
        /// </summary>
        /// <param name="propertyPath">Abraxas property path selected by the condition.<br/></param>
        /// <returns>A self-contained direct selector node.<br/></returns>
        internal static ConditionWireSelector Property(string propertyPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(propertyPath);
            return new ConditionWireSelector
            {
                Kind = "Property",
                PropertyPath = new PropPath(propertyPath).Path
            };
        }

        /// <summary>
        /// Creates one normalized semantic-index selector node whose name remains distinct from a property path after persistence.<br/>
        /// </summary>
        /// <param name="semanticName">Exact case-sensitive function or expression index name.<br/></param>
        /// <returns>A self-contained semantic-index selector node.<br/></returns>
        internal static ConditionWireSelector SemanticIndex(string semanticName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(semanticName);
            return new ConditionWireSelector { Kind = "SemanticIndex", PropertyPath = semanticName };
        }
    }

    internal sealed class ConditionWireValue
    {
        /// <summary>
        /// Creates an empty literal-or-parameter operand for Inheto deserialization.<br/>
        /// The containing descriptor determines whether its typed payload or its validated binding position is authoritative.<br/>
        /// </summary>
        public ConditionWireValue()
        {
        }

        public bool IsParameter { get; set; }
        public int ParameterPosition { get; set; } = -1;
        public string TypeCode { get; set; } = string.Empty;
        public bool IsNull { get; set; }
        public bool IsCollection { get; set; }
        public byte[] Payload { get; set; } = Array.Empty<byte>();
    }

    internal sealed class ConditionWireParameter
    {
        /// <summary>
        /// Creates an empty binding-contract entry for Inheto deserialization.<br/>
        /// Descriptor validation enforces contiguous zero-based position, unique case-sensitive name, and resolvable CLR type identity.<br/>
        /// </summary>
        public ConditionWireParameter()
        {
        }

        public int Position { get; set; }
        public string? Name { get; set; }
        public string TypeCode { get; set; } = string.Empty;
        public ConditionParameterKind Kind { get; set; }
        public string? ElementTypeCode { get; set; }
        public string? DiagnosticContext { get; set; }
    }

    internal static class ConditionWireCodec
    {
        private const uint Magic = 0x58425241;

        /// <summary>
        /// Encodes the validated semantic document into a deterministic primitive-only payload before Inheto packages it.<br/>
        /// This avoids coupling the durable format to CLR member discovery and keeps repeated text independent of object-reference deduplication.<br/>
        /// </summary>
        /// <param name="document">Semantic condition document to encode.<br/></param>
        /// <returns>Deterministic payload bytes suitable for the outer Inheto binary envelope.<br/></returns>
        internal static byte[] Encode(ConditionWireDocument document)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(Magic);
            writer.Write(document.FormatVersion);
            writer.Write(document.RecordTypeCode);
            writer.Write(document.SystemPersistable);
            WriteSequence(writer, document.Sequence, document.FormatVersion);
            writer.Write(document.Parameters.Count);
            foreach (var parameter in document.Parameters)
            {
                writer.Write(parameter.Position);
                WriteNullableString(writer, parameter.Name);
                writer.Write(parameter.TypeCode);
                writer.Write((int)parameter.Kind);
                WriteNullableString(writer, parameter.ElementTypeCode);
                WriteNullableString(writer, parameter.DiagnosticContext);
            }

            writer.Flush();
            return stream.ToArray();
        }

        /// <summary>
        /// Decodes one primitive-only condition payload from the outer Inheto envelope.<br/>
        /// Invalid magic, negative collection counts, truncated data, and trailing bytes fail before descriptor validation or runtime replay.<br/>
        /// </summary>
        /// <param name="payload">Decoded payload bytes obtained from Inheto.<br/></param>
        /// <returns>The mutable wire document consumed immediately by immutable descriptor validation.<br/></returns>
        internal static ConditionWireDocument Decode(byte[] payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            using var stream = new MemoryStream(payload, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            if (reader.ReadUInt32() != Magic)
                throw new InvalidDataException("Serialized condition payload has an invalid signature.");

            var formatVersion = reader.ReadInt32();
            var document = new ConditionWireDocument
            {
                FormatVersion = formatVersion,
                RecordTypeCode = reader.ReadString(),
                SystemPersistable = reader.ReadBoolean(),
                Sequence = ReadSequence(reader, formatVersion)
            };
            var parameterCount = ReadCount(reader, "parameter");
            for (var index = 0; index < parameterCount; index++)
            {
                document.Parameters.Add(new ConditionWireParameter
                {
                    Position = reader.ReadInt32(),
                    Name = ReadNullableString(reader),
                    TypeCode = reader.ReadString(),
                    Kind = (ConditionParameterKind)reader.ReadInt32(),
                    ElementTypeCode = ReadNullableString(reader),
                    DiagnosticContext = ReadNullableString(reader)
                });
            }

            if (stream.Position != stream.Length)
                throw new InvalidDataException("Serialized condition payload contains unexpected trailing data.");
            return document;
        }

        /// <summary>
        /// Writes one ordered condition sequence recursively while retaining explicit item discriminators.<br/>
        /// Leaf and group payload presence is encoded separately so malformed combinations remain detectable during read and replay.<br/>
        /// </summary>
        private static void WriteSequence(
            BinaryWriter writer,
            ConditionWireSequence sequence,
            int formatVersion)
        {
            writer.Write(sequence.Items.Length);
            foreach (var item in sequence.Items)
            {
                writer.Write(item.Kind);
                writer.Write(item.Leaf is not null);
                if (item.Leaf is not null)
                    WriteLeaf(writer, item.Leaf, formatVersion);
                writer.Write(item.Group is not null);
                if (item.Group is not null)
                    WriteSequence(writer, item.Group, formatVersion);
            }
        }

        /// <summary>
        /// Reads one recursively encoded semantic sequence with bounded non-negative item counts.<br/>
        /// The returned order is identical to authoring traversal and therefore remains the source of positional binding order.<br/>
        /// </summary>
        private static ConditionWireSequence ReadSequence(BinaryReader reader, int formatVersion)
        {
            var count = ReadCount(reader, "sequence item");
            var items = new ConditionWireItem[count];
            for (var index = 0; index < count; index++)
            {
                var item = new ConditionWireItem { Kind = reader.ReadString() };
                if (reader.ReadBoolean())
                    item.Leaf = ReadLeaf(reader, formatVersion);
                if (reader.ReadBoolean())
                    item.Group = ReadSequence(reader, formatVersion);
                items[index] = item;
            }

            return new ConditionWireSequence { Items = items };
        }

        /// <summary>
        /// Writes one backend-neutral predicate leaf and all typed operands and rendering options.<br/>
        /// Literal payloads remain their own Inheto bytes while parameter operands retain only their contract position and type metadata.<br/>
        /// </summary>
        private static void WriteLeaf(
            BinaryWriter writer,
            ConditionWireLeaf leaf,
            int formatVersion)
        {
            writer.Write(leaf.Family);
            writer.Write(leaf.Operator);
            writer.Write(leaf.ValueTypeCode);
            if (formatVersion < 3)
            {
                writer.Write(GetDirectPropertyPath(leaf.Selector));
            }
            else
            {
                WriteSelector(writer, leaf.Selector, formatVersion);
            }
            if (formatVersion >= 13)
            {
                writer.Write(leaf.OperandSelector is not null);
                if (leaf.OperandSelector is not null)
                    WriteSelector(writer, leaf.OperandSelector, formatVersion);
            }
            writer.Write(leaf.Operands.Length);
            foreach (var operand in leaf.Operands)
            {
                writer.Write(operand.IsParameter);
                writer.Write(operand.ParameterPosition);
                writer.Write(operand.TypeCode);
                writer.Write(operand.IsNull);
                writer.Write(operand.IsCollection);
                writer.Write(operand.Payload.Length);
                writer.Write(operand.Payload);
            }
            writer.Write(leaf.TextOptions.Length);
            foreach (var option in leaf.TextOptions) writer.Write(option);
            writer.Write(leaf.IntegerOptions.Length);
            foreach (var option in leaf.IntegerOptions) writer.Write(option);
            writer.Write(leaf.BooleanOptions.Length);
            foreach (var option in leaf.BooleanOptions) writer.Write(option);
        }

        /// <summary>
        /// Reads one predicate leaf and reconstructs its typed literal-or-parameter operand records.<br/>
        /// Every variable-length section uses the same guarded count reader to prevent negative or unreasonable allocations from corrupt packages.<br/>
        /// </summary>
        private static ConditionWireLeaf ReadLeaf(BinaryReader reader, int formatVersion)
        {
            var leaf = new ConditionWireLeaf
            {
                Family = reader.ReadString(),
                Operator = reader.ReadString(),
                ValueTypeCode = reader.ReadString(),
                Selector = formatVersion < 3
                    ? ConditionWireSelector.Property(reader.ReadString())
                    : ReadSelector(reader, formatVersion)
            };
            if (formatVersion >= 13 && reader.ReadBoolean())
                leaf.OperandSelector = ReadSelector(reader, formatVersion);
            var operandCount = ReadCount(reader, "operand");
            leaf.Operands = new ConditionWireValue[operandCount];
            for (var index = 0; index < operandCount; index++)
            {
                var operand = new ConditionWireValue
                {
                    IsParameter = reader.ReadBoolean(),
                    ParameterPosition = reader.ReadInt32(),
                    TypeCode = reader.ReadString(),
                    IsNull = reader.ReadBoolean(),
                    IsCollection = reader.ReadBoolean()
                };
                var payloadLength = ReadCount(reader, "literal payload byte");
                operand.Payload = reader.ReadBytes(payloadLength);
                if (operand.Payload.Length != payloadLength)
                    throw new EndOfStreamException("Serialized condition literal payload ended unexpectedly.");
                leaf.Operands[index] = operand;
            }

            var textCount = ReadCount(reader, "text option");
            leaf.TextOptions = new string[textCount];
            for (var index = 0; index < textCount; index++) leaf.TextOptions[index] = reader.ReadString();
            var integerCount = ReadCount(reader, "integer option");
            leaf.IntegerOptions = new int[integerCount];
            for (var index = 0; index < integerCount; index++) leaf.IntegerOptions[index] = reader.ReadInt32();
            var booleanCount = ReadCount(reader, "boolean option");
            leaf.BooleanOptions = new bool[booleanCount];
            for (var index = 0; index < booleanCount; index++) leaf.BooleanOptions[index] = reader.ReadBoolean();
            return leaf;
        }

        /// <summary>
        /// Writes one recursive selector node with an explicit discriminator and family-specific fields.<br/>
        /// The selector grammar is independent of predicate operands so later selector families can evolve without overloading condition comparison options.<br/>
        /// </summary>
        /// <param name="writer">Primitive condition payload writer.<br/></param>
        /// <param name="selector">Validated selector node to encode.<br/></param>
        /// <param name="formatVersion">The outer condition format version that determines which selector families and fields can be encoded.<br/></param>
        internal static void WriteSelector(
            BinaryWriter writer,
            ConditionWireSelector selector,
            int formatVersion)
        {
            writer.Write(selector.Kind);
            switch (selector.Kind)
            {
                case "SemanticIndex":
                    if (formatVersion < 11)
                        throw new InvalidDataException("Condition format versions before 11 cannot encode semantic-index selectors.");
                    writer.Write(selector.PropertyPath);
                    return;
                case "Property":
                    writer.Write(selector.PropertyPath);
                    return;
                case "BinarySlice":
                    WriteSelector(
                        writer,
                        selector.Source ?? throw new InvalidDataException(
                            "A binary-slice selector cannot be encoded without its source."),
                        formatVersion);
                    writer.Write(selector.BinarySliceKind);
                    writer.Write(selector.Offset);
                    writer.Write(selector.Length);
                    if (formatVersion >= 4)
                    {
                        writer.Write(selector.EncodingCodePage);
                        writer.Write(selector.TextCoercion);
                    }
                    return;
                case "DecimalOperation":
                    if (formatVersion < 5)
                        throw new InvalidDataException("Condition format versions before 5 cannot encode decimal-operation selectors.");
                    WriteSelector(
                        writer,
                        selector.Source ?? throw new InvalidDataException(
                            "A decimal-operation selector cannot be encoded without its source."),
                        formatVersion);
                    writer.Write(selector.DecimalOperation);
                    writer.Write(selector.OperandSource is not null);
                    if (selector.OperandSource is not null)
                        WriteSelector(writer, selector.OperandSource, formatVersion);
                    writer.Write(selector.HasDecimalOperand);
                    if (selector.HasDecimalOperand)
                        writer.Write(selector.DecimalOperand);
                    writer.Write(selector.HasIntegerOperand);
                    if (selector.HasIntegerOperand)
                        writer.Write(selector.IntegerOperand);
                    return;
                case "TimeSpanProjection":
                    if (formatVersion < 6)
                        throw new InvalidDataException("Condition format versions before 6 cannot encode TimeSpan projection selectors.");
                    WriteSelector(
                        writer,
                        selector.Source ?? throw new InvalidDataException(
                            "A TimeSpan projection selector cannot be encoded without its source."),
                        formatVersion);
                    writer.Write(selector.TimeSpanProjection);
                    return;
                case "NumericArithmetic":
                    if (formatVersion < 7)
                        throw new InvalidDataException("Condition format versions before 7 cannot encode numeric-arithmetic selectors.");
                    WriteSelector(
                        writer,
                        selector.Source ?? throw new InvalidDataException(
                            "A numeric-arithmetic selector cannot be encoded without its source."),
                        formatVersion);
                    writer.Write(selector.NumericOperation);
                    writer.Write(selector.NumericSourceTypeCode);
                    writer.Write(selector.NumericResultTypeCode);
                    if (formatVersion >= 14)
                        writer.Write(selector.NumericRightTypeCode);
                    writer.Write(selector.OperandSource is not null);
                    if (selector.OperandSource is not null)
                        WriteSelector(writer, selector.OperandSource, formatVersion);
                    writer.Write(selector.HasNumericOperand);
                    if (selector.HasNumericOperand)
                    {
                        writer.Write(selector.NumericOperandPayload.Length);
                        writer.Write(selector.NumericOperandPayload);
                    }
                    return;
                case "TemporalArithmetic":
                    if (formatVersion < 14)
                        throw new InvalidDataException("Condition format versions before 14 cannot encode temporal-arithmetic selectors.");
                    WriteSelector(
                        writer,
                        selector.Source ?? throw new InvalidDataException(
                            "A temporal-arithmetic selector cannot be encoded without its source."),
                        formatVersion);
                    writer.Write(selector.TemporalOperation);
                    writer.Write(selector.TemporalSourceTypeCode);
                    writer.Write(selector.TemporalRightTypeCode);
                    writer.Write(selector.TemporalResultTypeCode);
                    writer.Write(selector.OperandSource is not null);
                    if (selector.OperandSource is not null)
                        WriteSelector(writer, selector.OperandSource, formatVersion);
                    writer.Write(selector.HasTemporalOperand);
                    if (selector.HasTemporalOperand)
                    {
                        writer.Write(selector.TemporalOperandPayload.Length);
                        writer.Write(selector.TemporalOperandPayload);
                    }
                    return;
                case "StringOperation":
                    if (formatVersion < 8)
                        throw new InvalidDataException("Condition format versions before 8 cannot encode string-operation selectors.");
                    if (formatVersion < 12 && selector.OperandSource is not null)
                        throw new InvalidDataException("Condition format versions before 12 cannot encode selector-to-selector string concatenation.");
                    WriteSelector(
                        writer,
                        selector.Source ?? throw new InvalidDataException(
                            "A string-operation selector cannot be encoded without its source."),
                        formatVersion);
                    writer.Write(selector.StringOperation);
                    writer.Write(selector.TextOperandState);
                    if (selector.TextOperandState == 2)
                        writer.Write(selector.TextOperand);
                    writer.Write(selector.SecondTextOperandState);
                    if (selector.SecondTextOperandState == 2)
                        writer.Write(selector.SecondTextOperand);
                    writer.Write(selector.HasLongOperand);
                    if (selector.HasLongOperand)
                        writer.Write(selector.LongOperand);
                    writer.Write(selector.HasSecondLongOperand);
                    if (selector.HasSecondLongOperand)
                        writer.Write(selector.SecondLongOperand);
                    writer.Write(selector.HasCharacterOperand);
                    if (selector.HasCharacterOperand)
                        writer.Write(selector.CharacterOperand);
                    writer.Write(selector.StringIgnoreCase);
                    writer.Write(selector.StringCultureName);
                    if (formatVersion >= 10)
                    {
                        writer.Write(selector.StringRegexOptions);
                        writer.Write(selector.StringRegexMatchTimeoutTicks);
                    }
                    else if (selector.StringOperation == StringOperationKind.MatchValue.ToString())
                    {
                        throw new InvalidDataException("Condition format versions before 10 cannot encode match-value selectors.");
                    }
                    if (formatVersion >= 12)
                    {
                        writer.Write(selector.OperandSource is not null);
                        if (selector.OperandSource is not null)
                            WriteSelector(writer, selector.OperandSource, formatVersion);
                    }
                    return;
                case "BuiltInOperation":
                    if (formatVersion < 9)
                        throw new InvalidDataException("Condition format versions before 9 cannot encode built-in selector operations.");
                    WriteSelector(
                        writer,
                        selector.Source ?? throw new InvalidDataException(
                            "A built-in selector operation cannot be encoded without its source."),
                        formatVersion);
                    writer.Write(selector.BuiltInOperation);
                    writer.Write(selector.BuiltInSourceTypeCode);
                    writer.Write(selector.BuiltInResultTypeCode);
                    writer.Write(selector.BuiltInFirstIntegerOperand);
                    writer.Write(selector.BuiltInSecondIntegerOperand);
                    writer.Write(selector.HasBuiltInTextOperand);
                    if (selector.HasBuiltInTextOperand)
                        writer.Write(selector.BuiltInTextOperand);
                    return;
                default:
                    throw new InvalidDataException($"Unknown condition selector kind '{selector.Kind}'.");
            }
        }

        /// <summary>
        /// Reads one recursive selector node from a versioned condition payload.<br/>
        /// Unknown discriminators fail immediately so an older runtime cannot execute a newer selector with incomplete semantics.<br/>
        /// </summary>
        /// <param name="reader">Primitive condition payload reader.<br/></param>
        /// <param name="formatVersion">The outer condition format version that determines the selector families and fields to decode.<br/></param>
        /// <returns>The mutable selector node consumed by descriptor validation.<br/></returns>
        internal static ConditionWireSelector ReadSelector(BinaryReader reader, int formatVersion)
        {
            var kind = reader.ReadString();
            return kind switch
            {
                "Property" => ConditionWireSelector.Property(reader.ReadString()),
                "SemanticIndex" when formatVersion >= 11 => ConditionWireSelector.SemanticIndex(reader.ReadString()),
                "BinarySlice" => ReadBinarySliceSelector(reader, formatVersion),
                "DecimalOperation" when formatVersion >= 5 => ReadDecimalOperationSelector(reader, formatVersion),
                "TimeSpanProjection" when formatVersion >= 6 => ReadTimeSpanProjectionSelector(reader, formatVersion),
                "NumericArithmetic" when formatVersion >= 7 => ReadNumericArithmeticSelector(reader, formatVersion),
                "TemporalArithmetic" when formatVersion >= 14 => ReadTemporalArithmeticSelector(reader, formatVersion),
                "StringOperation" when formatVersion >= 8 => ReadStringOperationSelector(reader, formatVersion),
                "BuiltInOperation" when formatVersion >= 9 => ReadBuiltInOperationSelector(reader, formatVersion),
                _ => throw new InvalidDataException($"Unknown condition selector kind '{kind}'.")
            };
        }

        /// <summary>
        /// Reads one binary-slice selector using the fields defined by its containing condition format version.<br/>
        /// Version 3 supplies only deterministic built-in slice semantics; version 4 additionally carries the stable code page and malformed-input policy required by custom encoded text.<br/>
        /// </summary>
        /// <param name="reader">Primitive condition payload reader positioned after the selector discriminator.<br/></param>
        /// <param name="formatVersion">The validated outer condition format version.<br/></param>
        /// <returns>The decoded mutable binary-slice selector.<br/></returns>
        private static ConditionWireSelector ReadBinarySliceSelector(
            BinaryReader reader,
            int formatVersion)
        {
            var selector = new ConditionWireSelector
            {
                Kind = "BinarySlice",
                Source = ReadSelector(reader, formatVersion),
                BinarySliceKind = reader.ReadString(),
                Offset = reader.ReadInt64(),
                Length = reader.ReadInt64()
            };
            if (formatVersion >= 4)
            {
                selector.EncodingCodePage = reader.ReadInt32();
                selector.TextCoercion = reader.ReadInt32();
            }

            return selector;
        }

        /// <summary>
        /// Reads one version-5 decimal-operation selector with explicit recursive source, optional selector operand, optional decimal literal, and optional scale or exponent fields.<br/>
        /// Structural validation later enforces the exact active-field combination required by the decoded operation kind.<br/>
        /// </summary>
        /// <param name="reader">Primitive payload reader positioned after the decimal-operation discriminator.<br/></param>
        /// <param name="formatVersion">Validated outer condition format version used for recursive selector decoding.<br/></param>
        /// <returns>The mutable decimal-operation selector node consumed by descriptor validation.<br/></returns>
        private static ConditionWireSelector ReadDecimalOperationSelector(
            BinaryReader reader,
            int formatVersion)
        {
            var selector = new ConditionWireSelector
            {
                Kind = "DecimalOperation",
                Source = ReadSelector(reader, formatVersion),
                DecimalOperation = reader.ReadString()
            };
            if (reader.ReadBoolean())
                selector.OperandSource = ReadSelector(reader, formatVersion);
            selector.HasDecimalOperand = reader.ReadBoolean();
            if (selector.HasDecimalOperand)
                selector.DecimalOperand = reader.ReadDecimal();
            selector.HasIntegerOperand = reader.ReadBoolean();
            if (selector.HasIntegerOperand)
                selector.IntegerOperand = reader.ReadInt32();
            return selector;
        }

        /// <summary>
        /// Reads one version-6 TimeSpan projection selector with its recursive source and explicit projection identity.<br/>
        /// Structural validation later rejects incompatible source kinds and unknown future projections.<br/>
        /// </summary>
        /// <param name="reader">Primitive payload reader positioned after the selector discriminator.<br/></param>
        /// <param name="formatVersion">Validated outer format version used for recursive source decoding.<br/></param>
        /// <returns>The mutable TimeSpan projection selector consumed by descriptor validation.<br/></returns>
        private static ConditionWireSelector ReadTimeSpanProjectionSelector(
            BinaryReader reader,
            int formatVersion)
            => new()
            {
                Kind = "TimeSpanProjection",
                Source = ReadSelector(reader, formatVersion),
                TimeSpanProjection = reader.ReadString()
            };

        /// <summary>
        /// Reads one version-7 numeric-arithmetic selector with exact source and result CLR identities plus one optional recursive or literal operand.<br/>
        /// Structural validation later enforces generic-math compatibility, operation arity, and exact operand-result typing before replay.<br/>
        /// </summary>
        /// <param name="reader">Primitive payload reader positioned after the selector discriminator.<br/></param>
        /// <param name="formatVersion">Validated outer format version used for recursive selector decoding.<br/></param>
        /// <returns>The mutable numeric-arithmetic selector consumed by descriptor validation.<br/></returns>
        private static ConditionWireSelector ReadNumericArithmeticSelector(
            BinaryReader reader,
            int formatVersion)
        {
            var selector = new ConditionWireSelector
            {
                Kind = "NumericArithmetic",
                Source = ReadSelector(reader, formatVersion),
                NumericOperation = reader.ReadString(),
                NumericSourceTypeCode = reader.ReadString(),
                NumericResultTypeCode = reader.ReadString()
            };
            selector.NumericRightTypeCode = formatVersion >= 14
                ? reader.ReadString()
                : selector.NumericResultTypeCode;
            if (reader.ReadBoolean())
                selector.OperandSource = ReadSelector(reader, formatVersion);
            selector.HasNumericOperand = reader.ReadBoolean();
            if (selector.HasNumericOperand)
                selector.NumericOperandPayload = reader.ReadBytes(ReadCount(reader, "numeric operand byte"));
            return selector;
        }

        /// <summary>
        /// Reads one version-14 temporal-arithmetic selector with exact left, right, and result CLR identities.<br/>
        /// The right operand remains either a recursive semantic selector or an exact Inheto literal payload; validation later enforces the operation matrix.<br/>
        /// </summary>
        /// <param name="reader">Primitive payload reader positioned after the selector discriminator.<br/></param>
        /// <param name="formatVersion">Validated outer format version used for recursive selector decoding.<br/></param>
        /// <returns>The mutable temporal-arithmetic selector consumed by descriptor validation.<br/></returns>
        private static ConditionWireSelector ReadTemporalArithmeticSelector(
            BinaryReader reader,
            int formatVersion)
        {
            var selector = new ConditionWireSelector
            {
                Kind = "TemporalArithmetic",
                Source = ReadSelector(reader, formatVersion),
                TemporalOperation = reader.ReadString(),
                TemporalSourceTypeCode = reader.ReadString(),
                TemporalRightTypeCode = reader.ReadString(),
                TemporalResultTypeCode = reader.ReadString()
            };
            if (reader.ReadBoolean())
                selector.OperandSource = ReadSelector(reader, formatVersion);
            selector.HasTemporalOperand = reader.ReadBoolean();
            if (selector.HasTemporalOperand)
                selector.TemporalOperandPayload = reader.ReadBytes(ReadCount(reader, "temporal operand byte"));
            return selector;
        }

        /// <summary>
        /// Reads one version-8 string-operation selector while preserving nullable text operands separately from absent operands.<br/>
        /// Validation runs after the complete payload is read so malformed arity and inactive-field combinations fail deterministically.<br/>
        /// </summary>
        /// <param name="reader">Binary reader positioned after the selector-kind tag.<br/></param>
        /// <param name="formatVersion">Durable condition format version controlling recursive selector support.<br/></param>
        /// <returns>The unvalidated string-operation wire selector.<br/></returns>
        private static ConditionWireSelector ReadStringOperationSelector(
            BinaryReader reader,
            int formatVersion)
        {
            var selector = new ConditionWireSelector
            {
                Kind = "StringOperation",
                Source = ReadSelector(reader, formatVersion),
                StringOperation = reader.ReadString(),
                TextOperandState = reader.ReadInt32()
            };
            if (selector.TextOperandState == 2)
                selector.TextOperand = reader.ReadString();
            selector.SecondTextOperandState = reader.ReadInt32();
            if (selector.SecondTextOperandState == 2)
                selector.SecondTextOperand = reader.ReadString();
            selector.HasLongOperand = reader.ReadBoolean();
            if (selector.HasLongOperand)
                selector.LongOperand = reader.ReadInt64();
            selector.HasSecondLongOperand = reader.ReadBoolean();
            if (selector.HasSecondLongOperand)
                selector.SecondLongOperand = reader.ReadInt64();
            selector.HasCharacterOperand = reader.ReadBoolean();
            if (selector.HasCharacterOperand)
                selector.CharacterOperand = reader.ReadInt32();
            selector.StringIgnoreCase = reader.ReadBoolean();
            selector.StringCultureName = reader.ReadString();
            if (formatVersion >= 10)
            {
                selector.StringRegexOptions = reader.ReadInt32();
                selector.StringRegexMatchTimeoutTicks = reader.ReadInt64();
            }
            if (formatVersion >= 12 && reader.ReadBoolean())
                selector.OperandSource = ReadSelector(reader, formatVersion);
            return selector;
        }

        /// <summary>
        /// Reads one version-9 deterministic built-in selector with its recursive source, exact CLR contracts, immutable integral operands, and optional text operand.<br/>
        /// Structural validation reconstructs the descriptor contract after decoding so malformed operation/type/operand combinations cannot reach replay.<br/>
        /// </summary>
        /// <param name="reader">Binary reader positioned after the selector-kind tag.<br/></param>
        /// <param name="formatVersion">Validated outer format version used for recursive source decoding.<br/></param>
        /// <returns>The unvalidated built-in selector wire node.<br/></returns>
        private static ConditionWireSelector ReadBuiltInOperationSelector(
            BinaryReader reader,
            int formatVersion)
        {
            var selector = new ConditionWireSelector
            {
                Kind = "BuiltInOperation",
                Source = ReadSelector(reader, formatVersion),
                BuiltInOperation = reader.ReadString(),
                BuiltInSourceTypeCode = reader.ReadString(),
                BuiltInResultTypeCode = reader.ReadString(),
                BuiltInFirstIntegerOperand = reader.ReadInt64(),
                BuiltInSecondIntegerOperand = reader.ReadInt64(),
                HasBuiltInTextOperand = reader.ReadBoolean()
            };
            if (selector.HasBuiltInTextOperand)
                selector.BuiltInTextOperand = reader.ReadString();
            return selector;
        }

        /// <summary>
        /// Extracts the direct property path required by the legacy version-2 leaf layout.<br/>
        /// Version-2 encoding cannot represent computed selectors and therefore rejects them rather than discarding their semantics.<br/>
        /// </summary>
        /// <param name="selector">Selector node being encoded through the legacy layout.<br/></param>
        /// <returns>The normalized direct property path.<br/></returns>
        private static string GetDirectPropertyPath(ConditionWireSelector selector)
        {
            if (selector.Kind != "Property" || string.IsNullOrWhiteSpace(selector.PropertyPath))
                throw new InvalidDataException("Condition format version 2 cannot encode computed selectors.");
            return selector.PropertyPath;
        }

        /// <summary>
        /// Writes a nullable string without reserving a sentinel text value that could collide with developer-supplied names.<br/>
        /// </summary>
        private static void WriteNullableString(BinaryWriter writer, string? value)
        {
            writer.Write(value is not null);
            if (value is not null) writer.Write(value);
        }

        /// <summary>
        /// Reads the explicit nullable-string presence flag followed by its UTF-8 value when present.<br/>
        /// </summary>
        private static string? ReadNullableString(BinaryReader reader)
            => reader.ReadBoolean() ? reader.ReadString() : null;

        /// <summary>
        /// Reads and validates a variable-length section count before any allocation occurs.<br/>
        /// A conservative upper bound rejects malformed packages while remaining far beyond practical condition sizes.<br/>
        /// </summary>
        private static int ReadCount(BinaryReader reader, string description)
        {
            var count = reader.ReadInt32();
            if (count < 0 || count > 1_000_000)
                throw new InvalidDataException($"Serialized condition {description} count '{count}' is invalid.");
            return count;
        }
    }

    internal static class ConditionWireTypes
    {
        internal static string GetCode(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            var qualified = type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
            return System.Text.RegularExpressions.Regex.Replace(
                qualified,
                @",\s*(Version|Culture|PublicKeyToken)=[^,\]]+",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }

        internal static Type Resolve(string code)
            => Type.GetType(code, throwOnError: false)
               ?? throw new InvalidDataException($"Condition CLR type '{code}' is unavailable.");

        internal static Type? GetSuppliedValueType(object? value)
        {
            if (value is null)
                return null;
            var type = value.GetType();
            return IsValueOrExpression(type) ? type.GetGenericArguments()[0] : type;
        }

        internal static bool IsCompatible(Type expected, object? value)
        {
            if (value is null)
                return !expected.IsValueType || Nullable.GetUnderlyingType(expected) is not null;

            var actual = GetSuppliedValueType(value)!;
            if (expected.IsAssignableFrom(actual))
                return true;
            if (expected.IsArray && typeof(IEnumerable).IsAssignableFrom(actual))
                return true;
            return false;
        }

        internal static bool IsValueOrExpression(Type type)
            => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueOrExpression<>);
    }

    internal sealed class ConditionWireCaptureContext
    {
        private readonly Type recordType;
        private readonly bool materializeParameters;
        private readonly List<ConditionWireParameter> parameters = new();
        private readonly HashSet<string> parameterNames = new(StringComparer.Ordinal);
        private Func<object, Type, string?, string?, object?, object?>? materializeParameter;
        private object? materializationOwner;
        private DateTime? executionUtcNow;

        internal ConditionWireCaptureContext(Type recordType, bool materializeParameters = false)
        {
            this.recordType = recordType;
            this.materializeParameters = materializeParameters;
        }

        internal IReadOnlyList<ConditionWireParameter> Parameters => parameters;
        internal bool SystemPersistable => parameters.Count == 0;

        /// <summary>
        /// A direct typed view is represented by its normalized source property, while a computed selector must expose a complete immutable semantic descriptor.<br/>
        /// </summary>
        /// <typeparam name="TRecord">Record type addressed by the selector.<br/></typeparam>
        /// <param name="selector">Selector whose durable recursive identity is required.<br/></param>
        /// <returns>The selector's complete wire node.<br/></returns>
        internal ConditionWireSelector CaptureSelector<TRecord>(ISelector selector)
        {
            ArgumentNullException.ThrowIfNull(selector);
            if (recordType != typeof(TRecord))
                throw new InvalidOperationException("The selector record type does not match the condition wire capture context.");
            if (selector is not ISelectorStructure structure || structure.ResultType is null)
                throw new NotSupportedException("A durable selector identity requires a typed backend-neutral selector.");

            IBackendNeutralSelectorDescriptor? descriptor = structure.SemanticDescriptor;
            if (descriptor is null && !string.IsNullOrWhiteSpace(structure.TypedSourcePropertyPath))
                descriptor = new DirectPropertySelectorDescriptor(structure.TypedSourcePropertyPath);
            if (descriptor is null)
            {
                throw new NotSupportedException(
                    "The computed selector does not expose a complete durable backend-neutral descriptor.");
            }

            return ReadSelector<TRecord>(
                new ValueOrExpression<string>(() => string.Empty)
                    .WithSelectorDescriptor(descriptor));
        }

        internal ConditionWireSequence CaptureSequence<TRecord>(Condition<TRecord>.Builder builder)
        {
            Func<object, Type, string?, string?, object?, object?>? previousMaterializer = materializeParameter;
            if (materializeParameters)
            {
                materializeParameter = (operand, valueType, propertyPath, parameterAlias, owner) =>
                    builder.MaterializeParameterUntyped(operand, valueType, propertyPath, parameterAlias, owner);
                executionUtcNow = builder.UtcNow;
            }

            try
            {
                var items = new List<ConditionWireItem>();
                foreach (var fragment in builder.Fragments)
                {
                    if (fragment is Condition<TRecord>.WhereFragment or Condition<TRecord>.EndCondition or
                        Condition<TRecord>.GroupStartFragment or Condition<TRecord>.GroupEndFragment)
                    {
                        continue;
                    }
                    if (fragment is Condition<TRecord>.AndOperation)
                    {
                        items.Add(new ConditionWireItem { Kind = "And" });
                        continue;
                    }
                    if (fragment is Condition<TRecord>.OrOperation)
                    {
                        items.Add(new ConditionWireItem { Kind = "Or" });
                        continue;
                    }
                    if (fragment is Condition<TRecord>.GroupConditionFragment group)
                    {
                        using var groupContext = group.GroupBuilder.PushParameterContext(builder.CurrentParameterContext);
                        items.Add(new ConditionWireItem
                        {
                            Kind = "Group",
                            Group = CaptureSequence(group.GroupBuilder)
                        });
                        continue;
                    }

                    object? previousOwner = materializationOwner;
                    materializationOwner = fragment;
                    try
                    {
                        items.Add(new ConditionWireItem
                        {
                            Kind = "Leaf",
                            Leaf = CaptureLeaf<TRecord>(fragment)
                        });
                    }
                    finally
                    {
                        materializationOwner = previousOwner;
                    }
                }

                if (items.Count == 0)
                    throw new InvalidOperationException("A serialized condition must contain at least one semantic predicate.");
                return new ConditionWireSequence { Items = items.ToArray() };
            }
            finally
            {
                materializeParameter = previousMaterializer;
            }
        }

        private ConditionWireLeaf CaptureLeaf<TRecord>(Condition<TRecord>.IConditionFragment fragment)
        {
            if (fragment is Condition<TRecord>.IPropertyComparisonSource propertyComparison)
            {
                return new ConditionWireLeaf
                {
                    Family = "PropertyComparison",
                    Operator = propertyComparison.Comparison.ToString(),
                    ValueTypeCode = ConditionWireTypes.GetCode(propertyComparison.ComparisonType),
                    Selector = ReadSelector<TRecord>(propertyComparison.LeftSelector),
                    OperandSelector = ReadSelector<TRecord>(propertyComparison.RightSelector),
                    Operands = Array.Empty<ConditionWireValue>(),
                    TextOptions =
                    [
                        ConditionWireTypes.GetCode(propertyComparison.LeftType),
                        ConditionWireTypes.GetCode(propertyComparison.RightType),
                        propertyComparison.Culture
                    ],
                    IntegerOptions = [1],
                    BooleanOptions = [propertyComparison.IgnoreCase]
                };
            }

            var type = fragment.GetType();
            if (TryCaptureTemporal<TRecord>(fragment, type, out var temporal))
                return temporal;

            var declaring = type.DeclaringType;
            var declaringName = declaring?.IsGenericType == true
                ? declaring.GetGenericTypeDefinition().Name
                : declaring?.Name;
            return declaringName switch
            {
                "NumericOperator`1" => CaptureNumeric<TRecord>(fragment, type),
                "StringOperator" => CaptureString<TRecord>(fragment, type),
                "BooleanOperator" => CaptureBoolean<TRecord>(fragment, type),
                "IsNullOperator" => CaptureNull<TRecord>(fragment, type, isNull: true),
                "IsNotNullOperator" => CaptureNull<TRecord>(fragment, type, isNull: false),
                "Condition`1" when type.Name == "EmptyStateOperation" => CaptureEmpty<TRecord>(fragment, type),
                "GuidOperator" => CaptureGuid<TRecord>(fragment, type),
                "BinaryOperator" => CaptureBinary<TRecord>(fragment, type),
                _ => throw new NotSupportedException(
                    $"Condition leaf '{type.FullName}' does not yet expose a durable backend-neutral descriptor.")
            };
        }

        private bool TryCaptureTemporal<TRecord>(
            Condition<TRecord>.IConditionFragment fragment,
            Type type,
            out ConditionWireLeaf leaf)
        {
            var whole = FindGenericBase(type, "LibraDexTemporalWholeValueOperation`1");
            if (whole is not null)
            {
                var selector = ReadField(fragment, whole, "selector");
                var operation = ReadField(fragment, whole, "operation")?.ToString() ?? string.Empty;
                var operands = (IEnumerable?)ReadField(fragment, whole, "operands");
                var setOperand = ReadField(fragment, whole, "setOperand");
                var valueType = whole.GetGenericArguments().Last();
                var selectorNode = ReadSelector<TRecord>(selector);
                var selectorPath = GetSelectorSourcePropertyPath(selectorNode);
                leaf = new ConditionWireLeaf
                {
                    Family = "TemporalWhole",
                    Operator = operation,
                    ValueTypeCode = ConditionWireTypes.GetCode(valueType),
                    Selector = selectorNode,
                    Operands = setOperand is null
                        ? CaptureOperands(operands, valueType, selectorPath, operation)
                        : new[]
                        {
                            CaptureOperand(
                                setOperand,
                                typeof(IEnumerable<>).MakeGenericType(valueType),
                                selectorPath,
                                operation,
                                isCollection: true)
                        }
                };
                return true;
            }

            var structured = FindGenericBase(type, "LibraDexTemporalStructuredOperation`1");
            if (structured is not null)
            {
                var selector = ReadField(fragment, structured, "selector");
                var operation = ReadField(fragment, structured, "operation")?.ToString() ?? string.Empty;
                var selectorNode = ReadSelector<TRecord>(selector);
                var selectorPath = GetSelectorSourcePropertyPath(selectorNode);
                var values = new List<ConditionWireValue>();
                CaptureOperandsInto(
                    values,
                    (IEnumerable?)ReadField(fragment, structured, "integerOperands"),
                    typeof(int),
                    selectorPath,
                    operation);
                CaptureOperandsInto(
                    values,
                    (IEnumerable?)ReadField(fragment, structured, "yearMonthOperands"),
                    typeof((int year, int month)),
                    selectorPath,
                    operation);
                CaptureOperandsInto(
                    values,
                    (IEnumerable?)ReadField(fragment, structured, "yearMonthDayOperands"),
                    typeof((int year, int month, int day)),
                    selectorPath,
                    operation);
                var leading = ReadField(fragment, structured, "leadingIntegerOperand");
                if (leading is not null)
                    values.Insert(0, CaptureOperand(leading, typeof(int), selectorPath, operation));
                if (materializeParameters &&
                    executionUtcNow is DateTime utcNow &&
                    operation is "IsToday" or "IsYesterday" or "InLastDays" or "InLastHours" or "InLastMinutes")
                {
                    values.Add(CaptureLiteral(utcNow, typeof(DateTime), isCollection: false));
                }

                leaf = new ConditionWireLeaf
                {
                    Family = "TemporalStructured",
                    Operator = operation,
                    ValueTypeCode = ConditionWireTypes.GetCode(structured.GetGenericArguments().Last()),
                    Selector = selectorNode,
                    Operands = values.ToArray()
                };
                return true;
            }

            leaf = default!;
            return false;
        }

        private ConditionWireLeaf CaptureNumeric<TRecord>(object fragment, Type type)
        {
            var valueType = type.GetGenericArguments().LastOrDefault(
                    static argument => !string.Equals(argument.Name, "T", StringComparison.Ordinal))
                ?? type.DeclaringType!.GetGenericArguments().Last();
            var selector = ReadField(fragment, type, "selectorExpression");
            var operation = ReadField(fragment, type, "symbol")?.ToString() ?? string.Empty;
            var setValues = ReadField(fragment, type, "setValues");
            var selectorNode = ReadSelector<TRecord>(selector);
            var selectorPath = GetSelectorSourcePropertyPath(selectorNode);
            return new ConditionWireLeaf
            {
                Family = "Numeric",
                Operator = operation,
                ValueTypeCode = ConditionWireTypes.GetCode(valueType),
                Selector = selectorNode,
                Operands = setValues is null
                    ? CaptureOperands(
                        (IEnumerable?)ReadField(fragment, type, "params"),
                        valueType,
                        selectorPath,
                        operation)
                    : new[]
                    {
                        CaptureOperand(
                            setValues,
                            typeof(IEnumerable<>).MakeGenericType(valueType),
                            selectorPath,
                            operation,
                            isCollection: true)
                    }
            };
        }

        private ConditionWireLeaf CaptureString<TRecord>(object fragment, Type type)
        {
            var selector = ReadField(fragment, type, "selectorExpression");
            if (selector is null)
            {
                object? matchValue = ReadField(fragment, type, "matchValue");
                object? descriptorValue = matchValue?.GetType()
                    .GetProperty("Descriptor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(matchValue);
                if (descriptorValue is not Condition<TRecord>.StringOperator.MatchValueSelectorDescriptor descriptor ||
                    !descriptor.PatternExpression.IsStaticValue ||
                    descriptor.PatternExpression.StaticValue is not Regex pattern ||
                    descriptor.SourceSelector.SelectorDescriptor is not IBackendNeutralSelectorDescriptor sourceDescriptor)
                {
                    throw new NotSupportedException(
                        "A serializable MatchValue condition requires a static regular expression and a durable source selector. Deferred regexes remain runtime-bound condition values.");
                }

                selector = new ValueOrExpression<string>(static () => string.Empty)
                    .WithSelectorDescriptor(
                        Selector<TRecord>.StringOperationSelectorDescriptor.CreateMatchValue(
                            sourceDescriptor,
                            pattern,
                            descriptor.GroupNumber,
                            descriptor.OccurrenceNumber ?? 0));
            }

            var operation = ReadField(fragment, type, "symbol")?.ToString() ?? string.Empty;
            var selectorNode = ReadSelector<TRecord>(selector);
            var selectorPath = GetSelectorSourcePropertyPath(selectorNode);
            var operands = new List<ConditionWireValue>();
            var strings = (IEnumerable?)ReadField(fragment, type, "strings");
            var stringList = ReadField(fragment, type, "stringList");
            var stringSet = ReadField(fragment, type, "stringSet");

            if (operation is "In" or "NotIn" or "InSet" or "NotInSet")
            {
                var source = stringList ?? stringSet;
                if (source is not null)
                    operands.Add(CaptureOperand(
                        source,
                        typeof(IEnumerable<string>),
                        selectorPath,
                        operation,
                        isCollection: true));
                else
                    operands.Add(CaptureCollection(strings, typeof(string), selectorPath, operation));
            }
            else
            {
                CaptureOperandsInto(operands, strings, typeof(string), selectorPath, operation);
            }

            return new ConditionWireLeaf
            {
                Family = "String",
                Operator = operation,
                ValueTypeCode = ConditionWireTypes.GetCode(typeof(string)),
                Selector = selectorNode,
                Operands = operands.ToArray(),
                TextOptions = new[] { (string?)ReadField(fragment, type, "culture") ?? string.Empty },
                IntegerOptions = new[] { Convert.ToInt32(ReadField(fragment, type, "indexType"), CultureInfo.InvariantCulture) },
                BooleanOptions = new[] { (bool?)ReadField(fragment, type, "ignoreCase") ?? false }
            };
        }

        private ConditionWireLeaf CaptureBoolean<TRecord>(object fragment, Type type)
        {
            var selectorNode = ReadSelector<TRecord>(ReadField(fragment, type, "selectorExpression"));
            var selectorPath = GetSelectorSourcePropertyPath(selectorNode);
            return new ConditionWireLeaf
            {
                Family = "Boolean",
                Operator = "EqualTo",
                ValueTypeCode = ConditionWireTypes.GetCode(typeof(bool)),
                Selector = selectorNode,
                Operands = new[]
                {
                    CaptureOperand(ReadField(fragment, type, "value")!, typeof(bool), selectorPath, "EqualTo")
                }
            };
        }

        private ConditionWireLeaf CaptureNull<TRecord>(object fragment, Type type, bool isNull)
            => new()
            {
                Family = "Null",
                Operator = isNull ? "IsNull" : "IsNotNull",
                ValueTypeCode = ConditionWireTypes.GetCode(typeof(object)),
                Selector = ReadSelector<TRecord>(ReadField(fragment, type, "selectorExpression")),
                Operands = Array.Empty<ConditionWireValue>()
            };

        /// <summary>
        /// Captures one family-neutral empty or non-empty predicate without manufacturing a string or binary operand.<br/>
        /// The selector remains authoritative and the polarity is read from the immutable operation leaf for deterministic replay.<br/>
        /// </summary>
        /// <typeparam name="TRecord">Record type owned by the condition descriptor.<br/></typeparam>
        /// <param name="fragment">Concrete empty-state condition operation.<br/></param>
        /// <param name="type">Runtime operation type used to read its private immutable fields.<br/></param>
        /// <returns>A backend-neutral operand-free empty-state wire leaf.<br/></returns>
        private ConditionWireLeaf CaptureEmpty<TRecord>(object fragment, Type type)
            => new()
            {
                Family = "Empty",
                Operator = (bool?)ReadField(fragment, type, "excludeEmpty") == true
                    ? "IsNotEmpty"
                    : "IsEmpty",
                ValueTypeCode = ConditionWireTypes.GetCode(typeof(object)),
                Selector = ReadSelector<TRecord>(ReadField(fragment, type, "selectorExpression")),
                Operands = Array.Empty<ConditionWireValue>()
            };

        private ConditionWireLeaf CaptureGuid<TRecord>(object fragment, Type type)
        {
            var selectorNode = ReadSelector<TRecord>(ReadField(fragment, type, "selector"));
            var selectorPath = GetSelectorSourcePropertyPath(selectorNode);
            var operand = ReadField(fragment, type, "param")
                ?? throw new InvalidOperationException("Guid condition operand was missing.");
            var operandType = operand.GetType().GetGenericArguments()[0];
            var operation = ReadField(fragment, type, "compareType")?.ToString() ?? string.Empty;
            return new ConditionWireLeaf
            {
                Family = "Guid",
                Operator = operation,
                ValueTypeCode = ConditionWireTypes.GetCode(operandType),
                Selector = selectorNode,
                Operands = new[] { CaptureOperand(operand, operandType, selectorPath, operation) }
            };
        }

        private ConditionWireLeaf CaptureBinary<TRecord>(object fragment, Type type)
        {
            if (!string.Equals(type.Name, "BinaryEqualsOperation", StringComparison.Ordinal))
                throw new NotSupportedException($"Binary condition leaf '{type.Name}' is not a whole-value comparison.");

            var selectorNode = ReadSelector<TRecord>(ReadField(fragment, type, "selector"));
            var selectorPath = GetSelectorSourcePropertyPath(selectorNode);
            var isEqual = (bool?)ReadField(fragment, type, "isEqual") ?? false;
            return new ConditionWireLeaf
            {
                Family = "Binary",
                Operator = isEqual ? "EqualTo" : "NotEqualTo",
                ValueTypeCode = ConditionWireTypes.GetCode(typeof(byte[])),
                Selector = selectorNode,
                Operands = new[]
                {
                    CaptureOperand(ReadField(fragment, type, "param")!, typeof(byte[]), selectorPath, isEqual ? "EqualTo" : "NotEqualTo")
                },
                TextOptions = new[] { (string?)ReadField(fragment, type, "parameterAlias") ?? string.Empty }
            };
        }

        /// <summary>
        /// Direct properties become property nodes; the first computed family accepts only a byte-array binary slice whose source is itself a durably described selector.<br/>
        /// Unsupported computed shapes fail explicitly instead of degrading to their source property.<br/>
        /// </summary>
        /// <typeparam name="TRecord">Record type addressed by the selector.<br/></typeparam>
        /// <param name="selector">Runtime condition selector wrapper.<br/></param>
        /// <returns>A complete backend-neutral selector node.<br/></returns>
        private ConditionWireSelector ReadSelector<TRecord>(object? selector)
        {
            if (selector is not ValueOrExpression<string> typed)
                throw new NotSupportedException("Condition selectors require a typed backend-neutral wrapper.");

            if (typed.SelectorDescriptor is DirectPropertySelectorDescriptor direct)
                return ConditionWireSelector.Property(direct.PropertyPath);

            if (typed.SelectorDescriptor is NamedIndexSelectorDescriptor named)
                return ConditionWireSelector.SemanticIndex(named.SemanticName);

            if (typed.SelectorDescriptor is IRuntimeSelectorDescriptor<TRecord>)
            {
                throw new NotSupportedException(
                    "Selector.External(...) contains runtime application code and cannot be serialized. " +
                    "Use a built-in selector operation, a registered function index, or recreate the external selector after loading.");
            }

            if (typed.SelectorDescriptor is Selector<TRecord>.StringOperationSelectorDescriptor stringOperation)
                return ReadStringOperationSelector<TRecord>(stringOperation);

            if (typed.SelectorDescriptor is Selector<TRecord>.DecimalOperationSelectorDescriptor decimalOperation)
                return ReadDecimalSelector<TRecord>(decimalOperation);

            if (typed.SelectorDescriptor is Selector<TRecord>.TimeSpanProjectionSelectorDescriptor timeSpanProjection)
                return ReadTimeSpanProjectionSelector<TRecord>(timeSpanProjection);

            if (typed.SelectorDescriptor is Selector<TRecord>.NumericArithmeticSelectorDescriptor numericArithmetic)
                return ReadNumericArithmeticSelector<TRecord>(numericArithmetic);

            if (typed.SelectorDescriptor is Selector<TRecord>.TemporalArithmeticSelectorDescriptor temporalArithmetic)
                return ReadTemporalArithmeticSelector<TRecord>(temporalArithmetic);

            if (typed.SelectorDescriptor is Selector<TRecord>.BuiltInSelectorDescriptor builtIn)
            {
                return new ConditionWireSelector
                {
                    Kind = "BuiltInOperation",
                    Source = ReadSelector<TRecord>(
                        new ValueOrExpression<string>(() => string.Empty)
                            .WithSelectorDescriptor(builtIn.Source)),
                    BuiltInOperation = builtIn.Operation.ToString(),
                    BuiltInSourceTypeCode = ConditionWireTypes.GetCode(builtIn.SourceType),
                    BuiltInResultTypeCode = ConditionWireTypes.GetCode(builtIn.ResultType),
                    BuiltInFirstIntegerOperand = builtIn.FirstIntegerOperand,
                    BuiltInSecondIntegerOperand = builtIn.SecondIntegerOperand,
                    HasBuiltInTextOperand = builtIn.TextOperand is not null,
                    BuiltInTextOperand = builtIn.TextOperand ?? string.Empty
                };
            }

            if (typed.SelectorDescriptor is Condition<TRecord>.BinarySliceSelectorDescriptor slice)
            {
                var durableNumeric =
                    Condition<TRecord>.TryGetNumericBinarySliceShape(
                        slice.SliceType,
                        out _,
                        out var fixedByteWidth) &&
                    slice.Length > 0 &&
                    (fixedByteWidth == 0 || slice.Length == fixedByteWidth);
                var durableText =
                    Condition<TRecord>.TryGetDeterministicTextBinarySliceShape(
                        slice.SliceType,
                        out var fixedTextByteWidth) &&
                    slice.Length > 0 &&
                    (fixedTextByteWidth == 0 || slice.Length == fixedTextByteWidth);
                var durableGuid =
                    Condition<TRecord>.TryGetDeterministicGuidBinarySliceShape(
                        slice.SliceType,
                        out var fixedGuidByteWidth) &&
                    slice.Length == fixedGuidByteWidth;
                var durableTemporal =
                    Condition<TRecord>.TryGetDeterministicTemporalBinarySliceShape(
                        slice.SliceType,
                        out _,
                        out var fixedTemporalByteWidth) &&
                    slice.Length == fixedTemporalByteWidth;
                var durableCustomText =
                    slice.SliceType == BinarySliceKind.CustomEncodingString &&
                    slice.Length > 0 &&
                    slice.StableEncoding is not null;
                if (slice.SliceType != BinarySliceKind.ByteArray &&
                    !durableNumeric &&
                    !durableText &&
                    !durableGuid &&
                    !durableTemporal &&
                    !durableCustomText)
                {
                    throw new NotSupportedException(
                        $"Binary-slice selector kind '{slice.SliceType}' does not yet have a durable wire contract.");
                }

                return new ConditionWireSelector
                {
                    Kind = "BinarySlice",
                    Source = ReadSelector<TRecord>(slice.SourceSelector),
                    BinarySliceKind = slice.SliceType.ToString(),
                    Offset = slice.Offset,
                    Length = slice.Length,
                    EncodingCodePage = slice.StableEncoding?.CodePage ?? 0,
                    TextCoercion = slice.StableEncoding is null
                        ? -1
                        : (int)slice.StableEncoding.Coercion
                };
            }

            throw new NotSupportedException(
                "The computed selector does not yet expose an operation-specific durable wire contract.");
        }

        /// <summary>
        /// Captures one immutable built-in string-operation tree into the version-8 recursive selector grammar.<br/>
        /// Null text arguments retain an explicit state distinct from empty strings, while external delegates fail because executable application code has no durable identity.<br/>
        /// </summary>
        /// <typeparam name="TRecord">Record type addressed by the selector tree.<br/></typeparam>
        /// <param name="descriptor">Runtime built-in string operation to capture.<br/></param>
        /// <returns>A complete durable string-operation selector node.<br/></returns>
        private ConditionWireSelector ReadStringOperationSelector<TRecord>(
            Selector<TRecord>.StringOperationSelectorDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            if (descriptor.Operation == StringOperationKind.External || descriptor.ExternalOperation is not null)
            {
                throw new NotSupportedException(
                    "Selector.External(...) contains runtime application code and cannot be serialized. Use a built-in selector operation or recreate the external selector after loading.");
            }

            var textExpected = descriptor.Operation is
                StringOperationKind.IndexOf or
                StringOperationKind.LastIndexOf or
                StringOperationKind.TrimAscii or
                StringOperationKind.TrimStartAscii or
                StringOperationKind.TrimEndAscii or
                StringOperationKind.Trim or
                StringOperationKind.TrimStart or
                StringOperationKind.TrimEnd or
                StringOperationKind.Replace or
                StringOperationKind.Insert;
            if (descriptor.Operation == StringOperationKind.MatchValue)
                textExpected = true;
            var secondTextExpected = descriptor.Operation == StringOperationKind.Replace;
            return new ConditionWireSelector
            {
                Kind = "StringOperation",
                Source = ReadSelector<TRecord>(
                    new ValueOrExpression<string>(() => string.Empty)
                        .WithSelectorDescriptor(descriptor.Source)),
                StringOperation = descriptor.Operation.ToString(),
                TextOperandState = !textExpected ? 0 : descriptor.TextOperand is null ? 1 : 2,
                TextOperand = descriptor.TextOperand ?? string.Empty,
                SecondTextOperandState = !secondTextExpected ? 0 : descriptor.SecondTextOperand is null ? 1 : 2,
                SecondTextOperand = descriptor.SecondTextOperand ?? string.Empty,
                HasLongOperand = descriptor.IntegerOperand is not null,
                LongOperand = descriptor.IntegerOperand ?? 0,
                HasSecondLongOperand = descriptor.SecondIntegerOperand is not null,
                SecondLongOperand = descriptor.SecondIntegerOperand ?? 0,
                HasCharacterOperand = descriptor.CharacterOperand is not null,
                CharacterOperand = descriptor.CharacterOperand ?? 0,
                StringIgnoreCase = descriptor.IgnoreCase,
                StringCultureName = descriptor.CultureName,
                StringRegexOptions = (int)descriptor.MatchOptions,
                StringRegexMatchTimeoutTicks = descriptor.MatchTimeoutTicks,
                OperandSource = descriptor.RightSource is null
                    ? null
                    : ReadSelector<TRecord>(
                        new ValueOrExpression<string>(() => string.Empty)
                            .WithSelectorDescriptor(descriptor.RightSource))
            };
        }

        /// <summary>
        /// Direct and preceding decimal sources remain recursive nodes; exactly one selector, literal-decimal, or integer operand is activated according to the operation contract.<br/>
        /// </summary>
        /// <typeparam name="TRecord">Record type addressed by the selector tree.<br/></typeparam>
        /// <param name="descriptor">Validated runtime decimal-operation descriptor.<br/></param>
        /// <returns>A complete durable decimal-operation selector node.<br/></returns>
        private ConditionWireSelector ReadDecimalSelector<TRecord>(
            Selector<TRecord>.DecimalOperationSelectorDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            return new ConditionWireSelector
            {
                Kind = "DecimalOperation",
                Source = ReadDecimalSourceSelector<TRecord>(descriptor.Source),
                DecimalOperation = descriptor.Operation.ToString(),
                OperandSource = descriptor.RightSource is null
                    ? null
                    : ReadDecimalSourceSelector<TRecord>(descriptor.RightSource),
                HasDecimalOperand = descriptor.LiteralOperand is not null,
                DecimalOperand = descriptor.LiteralOperand ?? 0m,
                HasIntegerOperand = descriptor.IntegerOperand is not null,
                IntegerOperand = descriptor.IntegerOperand ?? 0
            };
        }

        /// <summary>
        /// Captures one immutable TimeSpan projection descriptor into the version-6 recursive selector grammar.<br/>
        /// </summary>
        /// <typeparam name="TRecord">Record type addressed by the selector tree.<br/></typeparam>
        /// <param name="descriptor">Validated runtime TimeSpan projection descriptor.<br/></param>
        /// <returns>A complete durable TimeSpan projection selector node.<br/></returns>
        private ConditionWireSelector ReadTimeSpanProjectionSelector<TRecord>(
            Selector<TRecord>.TimeSpanProjectionSelectorDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            return new ConditionWireSelector
            {
                Kind = "TimeSpanProjection",
                Source = ReadTimeSpanSourceSelector<TRecord>(descriptor.Source),
                TimeSpanProjection = descriptor.Projection.ToString()
            };
        }

        /// <summary>
        /// Captures one immutable checked numeric-arithmetic descriptor into the version-7 recursive selector grammar.<br/>
        /// Exact source and promoted-result types are retained, while a right operand is represented as either a recursive selector or its Inheto typed literal bytes.<br/>
        /// </summary>
        /// <typeparam name="TRecord">Record type addressed by the selector tree.<br/></typeparam>
        /// <param name="descriptor">Validated runtime integral or real arithmetic descriptor.<br/></param>
        /// <returns>A complete durable numeric-arithmetic selector node.<br/></returns>
        private ConditionWireSelector ReadNumericArithmeticSelector<TRecord>(
            Selector<TRecord>.NumericArithmeticSelectorDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            return new ConditionWireSelector
            {
                Kind = "NumericArithmetic",
                Source = ReadNumericSourceSelector<TRecord>(descriptor.Source),
                NumericOperation = descriptor.Operation.ToString(),
                NumericSourceTypeCode = ConditionWireTypes.GetCode(descriptor.SourceType),
                NumericRightTypeCode = ConditionWireTypes.GetCode(descriptor.RightType),
                NumericResultTypeCode = ConditionWireTypes.GetCode(descriptor.ResultType),
                OperandSource = descriptor.RightSource is null
                    ? null
                    : ReadNumericSourceSelector<TRecord>(descriptor.RightSource),
                HasNumericOperand = descriptor.HasLiteralOperand,
                NumericOperandPayload = descriptor.HasLiteralOperand
                    ? InhetoBinary.FromObject(descriptor.BoxedLiteralOperand!).RawBytes.ToArray()
                    : Array.Empty<byte>()
            };
        }

        /// <summary>
        /// Captures one immutable temporal arithmetic descriptor into the version-14 recursive selector grammar.<br/>
        /// Both operand types and the result type remain explicit so later execution can bind the same semantic intent without guessing a coercion domain.<br/>
        /// </summary>
        /// <typeparam name="TRecord">Record type addressed by the selector tree.<br/></typeparam>
        /// <param name="descriptor">Validated runtime temporal-arithmetic descriptor.<br/></param>
        /// <returns>A complete durable temporal-arithmetic selector node.<br/></returns>
        private ConditionWireSelector ReadTemporalArithmeticSelector<TRecord>(
            Selector<TRecord>.TemporalArithmeticSelectorDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            return new ConditionWireSelector
            {
                Kind = "TemporalArithmetic",
                Source = ReadNumericSourceSelector<TRecord>(descriptor.Source),
                TemporalOperation = descriptor.Operation.ToString(),
                TemporalSourceTypeCode = ConditionWireTypes.GetCode(descriptor.SourceType),
                TemporalRightTypeCode = ConditionWireTypes.GetCode(descriptor.RightType),
                TemporalResultTypeCode = ConditionWireTypes.GetCode(descriptor.ResultType),
                OperandSource = descriptor.RightSource is null
                    ? null
                    : ReadNumericSourceSelector<TRecord>(descriptor.RightSource),
                HasTemporalOperand = descriptor.HasLiteralOperand,
                TemporalOperandPayload = descriptor.HasLiteralOperand
                    ? InhetoBinary.FromObject(descriptor.BoxedLiteralOperand!).RawBytes.ToArray()
                    : Array.Empty<byte>()
            };
        }

        /// <summary>
        /// Captures one direct, binary-projected, TimeSpan-projected, or nested arithmetic numeric source from its structured selector metadata.<br/>
        /// The ordinary selector capture path remains the single source of durable grammar rules for every supported source family.<br/>
        /// </summary>
        /// <typeparam name="TRecord">Record type addressed by the selector tree.<br/></typeparam>
        /// <param name="descriptor">Backend-neutral numeric-producing descriptor.<br/></param>
        /// <returns>A durable recursive selector node preserving the complete source semantics.<br/></returns>
        private ConditionWireSelector ReadNumericSourceSelector<TRecord>(
            IBackendNeutralSelectorDescriptor descriptor)
            => ReadSelector<TRecord>(
                new ValueOrExpression<string>(() => string.Empty)
                    .WithSelectorDescriptor(descriptor));

        /// <summary>
        /// Captures one direct-property or deterministic raw TimeSpan binary-slice source.<br/>
        /// Other derived families fail explicitly because no TimeSpan coercion has been proven for them.<br/>
        /// </summary>
        /// <typeparam name="TRecord">Record type addressed by the source.<br/></typeparam>
        /// <param name="descriptor">Backend-neutral TimeSpan-producing descriptor.<br/></param>
        /// <returns>A durable recursive source selector.<br/></returns>
        private ConditionWireSelector ReadTimeSpanSourceSelector<TRecord>(
            IBackendNeutralSelectorDescriptor descriptor)
        {
            if (descriptor is DirectPropertySelectorDescriptor direct)
                return ConditionWireSelector.Property(direct.PropertyPath);
            if (descriptor is Condition<TRecord>.BinarySliceSelectorDescriptor slice &&
                slice.SliceType == BinarySliceKind.TimeSpanTicks)
            {
                return ReadSelector<TRecord>(
                    new ValueOrExpression<string>(() => string.Empty)
                        .WithSelectorDescriptor(slice));
            }
            if (descriptor is Selector<TRecord>.TemporalArithmeticSelectorDescriptor temporal &&
                temporal.ResultType == typeof(TimeSpan))
            {
                return ReadSelector<TRecord>(
                    new ValueOrExpression<string>(() => string.Empty)
                        .WithSelectorDescriptor(temporal));
            }

            throw new NotSupportedException(
                "A TimeSpan projection source must be a direct property, deterministic raw TimeSpan binary slice, or TimeSpan-producing temporal expression.");
        }

        /// <summary>
        /// Captures one direct-property or nested decimal-operation source into durable selector form.<br/>
        /// Other descriptor families fail explicitly because decimal arithmetic has no proven coercion from their result types.<br/>
        /// </summary>
        /// <typeparam name="TRecord">Record type addressed by the selector tree.<br/></typeparam>
        /// <param name="descriptor">Direct or computed decimal source descriptor.<br/></param>
        /// <returns>A durable recursive selector node preserving the complete source semantics.<br/></returns>
        private ConditionWireSelector ReadDecimalSourceSelector<TRecord>(
            IBackendNeutralSelectorDescriptor descriptor)
        {
            if (descriptor is DirectPropertySelectorDescriptor direct)
                return ConditionWireSelector.Property(direct.PropertyPath);
            if (descriptor is Selector<TRecord>.DecimalOperationSelectorDescriptor decimalOperation)
                return ReadDecimalSelector<TRecord>(decimalOperation);
            throw new NotSupportedException(
                $"Decimal selector source descriptor '{descriptor.GetType().FullName}' has no durable decimal coercion contract.");
        }

        /// <summary>
        /// Resolves the direct source property used for operand diagnostics and binding metadata from one recursive selector node.<br/>
        /// Computed selectors walk to their source without discarding their separately persisted derivation.<br/>
        /// </summary>
        /// <param name="selector">Captured selector node.<br/></param>
        /// <returns>The normalized source-property path.<br/></returns>
        private static string GetSelectorSourcePropertyPath(ConditionWireSelector selector)
        {
            ArgumentNullException.ThrowIfNull(selector);
            return selector.Kind switch
            {
                "Property" when !string.IsNullOrWhiteSpace(selector.PropertyPath) => selector.PropertyPath,
                "SemanticIndex" when !string.IsNullOrWhiteSpace(selector.PropertyPath) => selector.PropertyPath,
                "BinarySlice" when selector.Source is not null => GetSelectorSourcePropertyPath(selector.Source),
                "DecimalOperation" when selector.Source is not null => GetSelectorSourcePropertyPath(selector.Source),
                "TimeSpanProjection" when selector.Source is not null => GetSelectorSourcePropertyPath(selector.Source),
                "NumericArithmetic" when selector.Source is not null => GetSelectorSourcePropertyPath(selector.Source),
                "TemporalArithmetic" when selector.Source is not null => GetSelectorSourcePropertyPath(selector.Source),
                "StringOperation" when selector.Source is not null => GetSelectorSourcePropertyPath(selector.Source),
                "BuiltInOperation" when selector.Source is not null => GetSelectorSourcePropertyPath(selector.Source),
                _ => throw new InvalidDataException(
                    $"Condition selector kind '{selector.Kind}' has no direct source property.")
            };
        }

        private ConditionWireValue[] CaptureOperands(
            IEnumerable? source,
            Type valueType,
            string selector,
            string operation)
        {
            var values = new List<ConditionWireValue>();
            CaptureOperandsInto(values, source, valueType, selector, operation);
            return values.ToArray();
        }

        private void CaptureOperandsInto(
            List<ConditionWireValue> destination,
            IEnumerable? source,
            Type valueType,
            string selector,
            string operation)
        {
            if (source is null)
                return;
            foreach (var operand in source)
                destination.Add(CaptureOperand(operand!, valueType, selector, operation));
        }

        private ConditionWireValue CaptureCollection(
            IEnumerable? source,
            Type elementType,
            string selector,
            string operation)
        {
            var wrappers = source?.Cast<object>().ToArray() ?? Array.Empty<object>();
            if (materializeParameters)
            {
                var array = Array.CreateInstance(elementType, wrappers.Length);
                for (var index = 0; index < wrappers.Length; index++)
                {
                    object wrapper = wrappers[index];
                    object? value = IsStaticWrapper(wrapper)
                        ? ReadStaticWrapper(wrapper)
                        : materializeParameter!(
                            wrapper,
                            elementType,
                            selector,
                            ReadBindingName(wrapper),
                            materializationOwner);
                    array.SetValue(value, index);
                }

                return CaptureLiteral(array, array.GetType(), isCollection: true);
            }

            if (wrappers.All(IsStaticWrapper))
            {
                var array = Array.CreateInstance(elementType, wrappers.Length);
                for (var index = 0; index < wrappers.Length; index++)
                    array.SetValue(ReadStaticWrapper(wrappers[index]), index);
                return CaptureLiteral(array, array.GetType(), isCollection: true);
            }

            var name = wrappers
                .Select(ReadBindingName)
                .FirstOrDefault(static candidate => !string.IsNullOrWhiteSpace(candidate));
            return CaptureParameter(
                wrappers.FirstOrDefault(),
                elementType.MakeArrayType(),
                name,
                selector,
                operation,
                isCollection: true);
        }

        private ConditionWireValue CaptureOperand(
            object wrapper,
            Type valueType,
            string selector,
            string operation,
            bool isCollection = false)
        {
            if (!ConditionWireTypes.IsValueOrExpression(wrapper.GetType()))
                return CaptureLiteral(wrapper, valueType, isCollection);
            if (IsStaticWrapper(wrapper))
                return CaptureLiteral(ReadStaticWrapper(wrapper), valueType, isCollection);

            if (materializeParameters)
            {
                return CaptureLiteral(
                    materializeParameter!(
                        wrapper,
                        valueType,
                        selector,
                        ReadBindingName(wrapper),
                        materializationOwner),
                    valueType,
                    isCollection);
            }

            return CaptureParameter(
                wrapper,
                valueType,
                ReadBindingName(wrapper),
                selector,
                operation,
                isCollection);
        }

        private ConditionWireValue CaptureParameter(
            object? wrapper,
            Type valueType,
            string? name,
            string selector,
            string operation,
            bool isCollection)
        {
            if (!string.IsNullOrWhiteSpace(name) && !parameterNames.Add(name))
                throw new InvalidOperationException($"Condition parameter name '{name}' is duplicated.");

            var position = parameters.Count;
            var normalizedType = valueType;
            parameters.Add(new ConditionWireParameter
            {
                Position = position,
                Name = name,
                TypeCode = ConditionWireTypes.GetCode(normalizedType),
                Kind = isCollection ? ConditionParameterKind.Collection : ConditionParameterKind.Value,
                ElementTypeCode = isCollection
                    ? ConditionWireTypes.GetCode(
                        normalizedType.GetElementType() ??
                        normalizedType.GetGenericArguments().FirstOrDefault() ??
                        typeof(object))
                    : null,
                DiagnosticContext = $"{selector} {operation} [{name ?? position.ToString(CultureInfo.InvariantCulture)}]"
            });

            return new ConditionWireValue
            {
                IsParameter = true,
                ParameterPosition = position,
                TypeCode = ConditionWireTypes.GetCode(normalizedType),
                IsCollection = isCollection
            };
        }

        private static ConditionWireValue CaptureLiteral(object? value, Type valueType, bool isCollection)
            => new()
            {
                IsParameter = false,
                TypeCode = ConditionWireTypes.GetCode(valueType),
                IsNull = value is null,
                IsCollection = isCollection,
                Payload = value is null
                    ? Array.Empty<byte>()
                    : InhetoBinary.FromObject(value).RawBytes.ToArray()
            };

        private static bool IsStaticWrapper(object wrapper)
            => (bool?)wrapper.GetType().GetProperty(
                    "IsStaticValue",
                    BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(wrapper)
               ?? false;

        private static object? ReadStaticWrapper(object wrapper)
            => wrapper.GetType().GetProperty(
                    "StaticValue",
                    BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(wrapper);

        private static string? ReadBindingName(object wrapper)
            => wrapper.GetType().GetProperty(
                    "BindingName",
                    BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(wrapper) as string;

        private static Type? FindGenericBase(Type type, string openGenericName)
        {
            for (var current = type.BaseType; current is not null; current = current.BaseType)
            {
                if (current.IsGenericType &&
                    string.Equals(
                        current.GetGenericTypeDefinition().Name,
                        openGenericName,
                        StringComparison.Ordinal))
                    return current;
            }

            return null;
        }

        private static object? ReadField(object instance, Type declaringType, string name)
            => declaringType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(instance);
    }

    internal static class ConditionWireReplayer<T>
    {
        internal static Condition<T>.EndCondition Replay(
            ConditionWireDocument document,
            IReadOnlyList<object?> bindings)
        {
            var builder = new Condition<T>.Builder();
            builder.AddFragment(new Condition<T>.WhereFragment());
            ReplaySequence(builder, document.Sequence, bindings);
            return Condition<T>.EndCondition.AddToBuilder(builder);
        }

        private static void ReplaySequence(
            Condition<T>.Builder builder,
            ConditionWireSequence sequence,
            IReadOnlyList<object?> bindings)
        {
            foreach (var item in sequence.Items)
            {
                switch (item.Kind)
                {
                    case "And":
                        builder.AddFragment(new Condition<T>.AndOperation(builder));
                        break;
                    case "Or":
                        builder.AddFragment(new Condition<T>.OrOperation(builder));
                        break;
                    case "Group":
                        var groupBuilder = new Condition<T>.Builder();
                        groupBuilder.AddFragment(new Condition<T>.WhereFragment());
                        ReplaySequence(
                            groupBuilder,
                            item.Group ?? throw new InvalidDataException("Condition group has no sequence."),
                            bindings);
                        builder.AddGroup(Condition<T>.EndCondition.AddToBuilder(groupBuilder));
                        break;
                    case "Leaf":
                        ReplayLeaf(
                            builder,
                            item.Leaf ?? throw new InvalidDataException("Condition leaf item has no leaf."),
                            bindings);
                        break;
                    default:
                        throw new InvalidDataException($"Unknown condition item kind '{item.Kind}'.");
                }
            }
        }

        private static void ReplayLeaf(
            Condition<T>.Builder builder,
            ConditionWireLeaf leaf,
            IReadOnlyList<object?> bindings)
        {
            var selector = CreateReplaySelector(leaf.Selector);
            switch (leaf.Family)
            {
                case "Numeric":
                    InvokeGeneric(nameof(ReplayNumeric), ConditionWireTypes.Resolve(leaf.ValueTypeCode), builder, leaf, bindings);
                    return;
                case "String":
                    ReplayString(builder, leaf, bindings);
                    return;
                case "Boolean":
                    builder.AddFragment(new Condition<T>.BooleanOperator.BooleanOperation(
                        selector,
                        ResolveValue<bool>(leaf.Operands[0], bindings)));
                    return;
                case "Null":
                    builder.AddFragment(leaf.Operator == "IsNull"
                        ? new Condition<T>.IsNullOperator.IsNullOperation(selector)
                        : new Condition<T>.IsNotNullOperator.IsNotNullOperation(selector));
                    return;
                case "Empty":
                    builder.AddFragment(new Condition<T>.EmptyStateOperation(
                        selector,
                        excludeEmpty: leaf.Operator == "IsNotEmpty"));
                    return;
                case "Guid":
                    ReplayGuid(builder, leaf, bindings);
                    return;
                case "Binary":
                    Condition<T>.BinaryOperator.BinaryEqualsOperation.AddToBuilder(
                        builder,
                        selector,
                        ResolveValue<byte[]>(leaf.Operands[0], bindings),
                        leaf.Operator == "EqualTo",
                        leaf.TextOptions.FirstOrDefault());
                    return;
                case "TemporalWhole":
                    InvokeGeneric(nameof(ReplayTemporalWhole), ConditionWireTypes.Resolve(leaf.ValueTypeCode), builder, leaf, bindings);
                    return;
                case "TemporalStructured":
                    InvokeGeneric(nameof(ReplayTemporalStructured), ConditionWireTypes.Resolve(leaf.ValueTypeCode), builder, leaf, bindings);
                    return;
                case "PropertyComparison":
                    ReplayPropertyComparison(builder, leaf);
                    return;
                default:
                    throw new InvalidDataException($"Unknown condition leaf family '{leaf.Family}'.");
            }
        }

        private static void ReplayNumeric<TValue>(
            Condition<T>.Builder builder,
            ConditionWireLeaf leaf,
            IReadOnlyList<object?> bindings)
            where TValue : System.Numerics.INumber<TValue>
        {
            var operation = Enum.Parse<Condition<T>.NumericOperator<TValue>.NumericOperatorSymbolsEnum>(leaf.Operator);
            if (leaf.Operands.Length == 1 && leaf.Operands[0].IsCollection &&
                TryResolveDeferredCollection<TValue>(leaf.Operands[0], bindings, out var deferredSet))
            {
                builder.AddFragment(new Condition<T>.NumericOperator<TValue>.NumericOperation(
                    operation, CreateReplaySelector(leaf.Selector), deferredSet));
                return;
            }
            var operands = leaf.Operands.Length == 1 && leaf.Operands[0].IsCollection
                ? ResolveCollection<TValue>(leaf.Operands[0], bindings)
                    .Select(static value => (ValueOrExpression<TValue>)value)
                    .ToArray()
                : leaf.Operands.Select(value => ResolveValue<TValue>(value, bindings)).ToArray();
            builder.AddFragment(new Condition<T>.NumericOperator<TValue>.NumericOperation(
                operation,
                CreateReplaySelector(leaf.Selector),
                operands));
        }

        /// <summary>
        /// Recreates one selector-to-selector comparison from its independently typed operands and frozen common comparison domain.<br/>
        /// Reflection occurs only at descriptor replay; the resulting closed generic leaf builds strongly typed readers for the execution hot path.<br/>
        /// </summary>
        private static void ReplayPropertyComparison(
            Condition<T>.Builder builder,
            ConditionWireLeaf leaf)
        {
            var comparison = Enum.Parse<Condition<T>.PropertyComparisonKind>(leaf.Operator);
            var leftType = ConditionWireTypes.Resolve(leaf.TextOptions[0]);
            var rightType = ConditionWireTypes.Resolve(leaf.TextOptions[1]);
            var compareType = ConditionWireTypes.Resolve(leaf.ValueTypeCode);
            var left = CreateReplaySelector(leaf.Selector);
            var right = CreateReplaySelector(leaf.OperandSelector
                ?? throw new InvalidDataException("A property comparison has no right-hand selector."));

            Type operationType;
            object?[] args;
            if (leftType == rightType && rightType == compareType)
            {
                operationType = typeof(Condition<>).GetNestedType(
                        "PropertyComparisonOperation`1",
                        BindingFlags.NonPublic)!
                    .MakeGenericType(typeof(T), compareType);
                args = new object?[]
                {
                    comparison,
                    left,
                    right,
                    leaf.TextOptions[2],
                    leaf.BooleanOptions[0]
                };
            }
            else
            {
                operationType = typeof(Condition<>).GetNestedType(
                        "NumericPropertyComparisonOperation`3",
                        BindingFlags.NonPublic)!
                    .MakeGenericType(typeof(T), leftType, rightType, compareType);
                args = new object?[] { comparison, left, right };
            }

            var operation = Activator.CreateInstance(
                operationType,
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args,
                CultureInfo.InvariantCulture) as Condition<T>.IConditionFragment
                ?? throw new InvalidOperationException("A persisted property comparison could not be recreated.");
            builder.AddFragment(operation);
        }

        private static void ReplayString(
            Condition<T>.Builder builder,
            ConditionWireLeaf leaf,
            IReadOnlyList<object?> bindings)
        {
            var opType = typeof(Condition<T>.StringOperator);
            var operationType = opType.GetNestedType("StringOperation", BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("String condition runtime adapter is unavailable.");
            if (operationType.ContainsGenericParameters)
                operationType = operationType.MakeGenericType(typeof(T));
            var symbolType = opType.GetNestedType("StringOperatorSymbolsEnum", BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("String condition operator vocabulary is unavailable.");
            if (symbolType.ContainsGenericParameters)
                symbolType = symbolType.MakeGenericType(typeof(T));
            var indexType = typeof(Condition<T>.StringOperator.IndexType);
            var symbol = Enum.Parse(symbolType, leaf.Operator);
            var index = Enum.ToObject(indexType, leaf.IntegerOptions.FirstOrDefault());
            var culture = leaf.TextOptions.FirstOrDefault() ?? string.Empty;
            var ignoreCase = leaf.BooleanOptions.FirstOrDefault();
            var selector = CreateReplaySelector(leaf.Selector);

            object? instance;
            if (leaf.Operator is "In" or "NotIn" or "InSet" or "NotInSet")
            {
                object operand = TryResolveDeferredCollection<string>(leaf.Operands[0], bindings, out var deferredSet)
                    ? deferredSet
                    : ResolveCollection<string>(leaf.Operands[0], bindings)
                        .Select(static value => (ValueOrExpression<string>)value).ToArray();
                instance = Activator.CreateInstance(
                    operationType,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    args: new object?[] { symbol, selector, culture, ignoreCase, index, operand },
                    culture: CultureInfo.InvariantCulture);
            }
            else
            {
                var wrappers = leaf.Operands.Select(value => ResolveValue<string>(value, bindings)).ToArray();
                instance = Activator.CreateInstance(
                    operationType,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    args: new object?[] { symbol, selector, culture, ignoreCase, index, wrappers },
                    culture: CultureInfo.InvariantCulture);
            }

            builder.AddFragment((Condition<T>.IConditionFragment)(instance
                ?? throw new InvalidOperationException("String condition runtime adapter could not be created.")));
        }

        private static void ReplayGuid(
            Condition<T>.Builder builder,
            ConditionWireLeaf leaf,
            IReadOnlyList<object?> bindings)
        {
            var compareType = Enum.Parse<GuidComparisonKind>(leaf.Operator);
            var operandType = ConditionWireTypes.Resolve(leaf.ValueTypeCode);
            var method = typeof(ConditionWireReplayer<T>)
                .GetMethod(nameof(ReplayGuidTyped), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(operandType);
            method.Invoke(null, new object?[] { builder, leaf, bindings, compareType });
        }

        private static void ReplayGuidTyped<TValue>(
            Condition<T>.Builder builder,
            ConditionWireLeaf leaf,
            IReadOnlyList<object?> bindings,
            GuidComparisonKind compareType)
        {
            Condition<T>.GuidOperator.GuidCompareOperation.AddToBuilder(
                builder,
                CreateReplaySelector(leaf.Selector),
                ResolveValue<TValue>(leaf.Operands[0], bindings),
                compareType);
        }

        private static void ReplayTemporalWhole<TValue>(
            Condition<T>.Builder builder,
            ConditionWireLeaf leaf,
            IReadOnlyList<object?> bindings)
            where TValue : struct
        {
            var outer = GetTemporalOperatorType(typeof(TValue));
            var operationClassName = leaf.Operator switch
            {
                "EqualTo" => "EqualOperation",
                "NotEqualTo" => "NotEqualOperation",
                "GreaterThan" => "GreaterThanOperation",
                "GreaterOrEqual" => "GreaterThanEqualOperation",
                "LessThan" => "LessThanOperation",
                "LessOrEqual" => "LessThanEqualOperation",
                _ => leaf.Operator + "Operation"
            };
            var operationType = outer.GetNestedType(operationClassName, BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidDataException(
                    $"Temporal operation '{leaf.Operator}' is unavailable for '{typeof(TValue).FullName}'.");
            if (operationType.ContainsGenericParameters)
                operationType = operationType.MakeGenericType(typeof(T));
            if (leaf.Operands.Length == 1 && leaf.Operands[0].IsCollection &&
                TryResolveDeferredCollection<TValue>(leaf.Operands[0], bindings, out var deferredSet))
            {
                var deferredInstance = Activator.CreateInstance(
                    operationType,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    args: new object?[] { CreateReplaySelector(leaf.Selector), deferredSet },
                    culture: CultureInfo.InvariantCulture);
                builder.AddFragment((Condition<T>.IConditionFragment)(deferredInstance
                    ?? throw new InvalidOperationException("Temporal membership runtime adapter could not be created.")));
                return;
            }
            var operands = leaf.Operands.Length == 1 && leaf.Operands[0].IsCollection
                ? ResolveCollection<TValue>(leaf.Operands[0], bindings)
                    .Select(static value => (ValueOrExpression<TValue>)value)
                    .ToArray()
                : leaf.Operands.Select(value => ResolveValue<TValue>(value, bindings)).ToArray();
            var selector = CreateReplaySelector(leaf.Selector);
            var args = leaf.Operator is "In" or "NotIn"
                ? new object?[] { selector, operands }
                : operands.Length switch
            {
                1 => new object?[] { selector, operands[0] },
                2 => new object?[] { selector, operands[0], operands[1] },
                _ => new object?[] { selector, operands }
            };
            var instance = Activator.CreateInstance(
                operationType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args,
                CultureInfo.InvariantCulture);
            builder.AddFragment((Condition<T>.IConditionFragment)(instance
                ?? throw new InvalidOperationException("Temporal condition runtime adapter could not be created.")));
        }

        private static void ReplayTemporalStructured<TValue>(
            Condition<T>.Builder builder,
            ConditionWireLeaf leaf,
            IReadOnlyList<object?> bindings)
            where TValue : struct
        {
            var outer = GetTemporalOperatorType(typeof(TValue));
            var operationType = outer.GetNestedType(leaf.Operator + "Operation", BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidDataException(
                    $"Structured temporal operation '{leaf.Operator}' is unavailable for '{typeof(TValue).FullName}'.");
            if (operationType.ContainsGenericParameters)
                operationType = operationType.MakeGenericType(typeof(T));

            var constructor = operationType
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .OrderBy(static candidate => candidate.GetParameters().Length)
                .First();
            var args = new object?[constructor.GetParameters().Length];
            args[0] = CreateReplaySelector(leaf.Selector);
            var operandIndex = 0;
            for (var parameterIndex = 1; parameterIndex < args.Length; parameterIndex++)
            {
                var parameterType = constructor.GetParameters()[parameterIndex].ParameterType;
                if (parameterType.IsGenericType &&
                    parameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    var elementType = parameterType.GetGenericArguments()[0];
                    args[parameterIndex] = CreateTypedWrapperArray(elementType, leaf.Operands.Skip(operandIndex), bindings);
                    operandIndex = leaf.Operands.Length;
                }
                else
                {
                    args[parameterIndex] = ResolveValueAs(
                        parameterType.GetGenericArguments().FirstOrDefault() ?? parameterType,
                        leaf.Operands[operandIndex++],
                        bindings);
                }
            }

            var instance = constructor.Invoke(args);
            builder.AddFragment((Condition<T>.IConditionFragment)instance);
        }

        /// <summary>
        /// Recreates one durable direct or computed selector with immutable backend-neutral structure.<br/>
        /// Property nodes restore direct index identity; byte-array slice nodes recursively restore their source plus the frozen native slice descriptor.<br/>
        /// </summary>
        /// <param name="selector">Validated recursive selector node stored in the durable condition leaf.<br/></param>
        /// <returns>A selector wrapper ready for semantic evaluation and LibraDex planning.<br/></returns>
        internal static ValueOrExpression<string> CreateReplaySelector(ConditionWireSelector selector)
        {
            ArgumentNullException.ThrowIfNull(selector);
            if (selector.Kind == "Property")
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(selector.PropertyPath);
                return ((ValueOrExpression<string>)selector.PropertyPath).WithSelectorDescriptor(
                    new DirectPropertySelectorDescriptor(selector.PropertyPath));
            }

            if (selector.Kind == "SemanticIndex")
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(selector.PropertyPath);
                return ((ValueOrExpression<string>)string.Empty).WithSelectorDescriptor(
                    new NamedIndexSelectorDescriptor(selector.PropertyPath));
            }

            if (selector.Kind == "BinarySlice" &&
                selector.Source is not null &&
                Enum.TryParse(selector.BinarySliceKind, ignoreCase: false, out BinarySliceKind sliceKind))
            {
                var source = CreateReplaySelector(selector.Source);
                LibraDexTextEncoding? stableEncoding =
                    sliceKind == BinarySliceKind.CustomEncodingString
                        ? LibraDexTextEncoding.ForCodePage(
                            selector.EncodingCodePage,
                            (Coercion.Text)selector.TextCoercion)
                        : null;
                var descriptor = new Condition<T>.BinarySliceSelectorDescriptor(
                    source,
                    sliceKind,
                    selector.Offset,
                    selector.Length,
                    Encoding.UTF8,
                    stableEncoding);
                return ((ValueOrExpression<string>)string.Empty)
                    .WithSelectorDescriptor(descriptor);
            }

            if (selector.Kind == "StringOperation" &&
                selector.Source is not null &&
                Enum.TryParse(
                    selector.StringOperation,
                    ignoreCase: false,
                    out StringOperationKind stringOperation) &&
                stringOperation != StringOperationKind.External)
            {
                var source = CreateReplaySelector(selector.Source);
                var sourceDescriptor = source.SelectorDescriptor
                    ?? throw new InvalidDataException("A replayed string selector source has no backend-neutral descriptor.");
                var right = selector.OperandSource is null
                    ? null
                    : CreateReplaySelector(selector.OperandSource);
                var descriptor = new Selector<T>.StringOperationSelectorDescriptor(
                    sourceDescriptor,
                    stringOperation,
                    selector.TextOperandState == 2 ? selector.TextOperand : null,
                    selector.SecondTextOperandState == 2 ? selector.SecondTextOperand : null,
                    selector.HasLongOperand ? selector.LongOperand : null,
                    selector.HasSecondLongOperand ? selector.SecondLongOperand : null,
                    selector.HasCharacterOperand ? (char)selector.CharacterOperand : null,
                    selector.StringIgnoreCase,
                    selector.StringCultureName,
                    matchOptions: (RegexOptions)selector.StringRegexOptions,
                    matchTimeoutTicks: selector.StringRegexMatchTimeoutTicks,
                    rightSource: right?.SelectorDescriptor);
                return ((ValueOrExpression<string>)string.Empty)
                    .WithSelectorDescriptor(descriptor);
            }

            if (selector.Kind == "DecimalOperation" &&
                selector.Source is not null &&
                Enum.TryParse(
                    selector.DecimalOperation,
                    ignoreCase: false,
                    out DecimalOperationKind decimalOperation))
            {
                var source = CreateReplaySelector(selector.Source);
                var right = selector.OperandSource is null
                    ? null
                    : CreateReplaySelector(selector.OperandSource);
                var sourceDescriptor = source.SelectorDescriptor
                    ?? throw new InvalidDataException("A replayed decimal selector source has no backend-neutral descriptor.");
                var rightDescriptor = right?.SelectorDescriptor;
                var descriptor = new Selector<T>.DecimalOperationSelectorDescriptor(
                    sourceDescriptor,
                    decimalOperation,
                    rightDescriptor,
                    selector.HasDecimalOperand ? selector.DecimalOperand : null,
                    selector.HasIntegerOperand ? selector.IntegerOperand : null);
                return ((ValueOrExpression<string>)string.Empty)
                    .WithSelectorDescriptor(descriptor);
            }

            if (selector.Kind == "TimeSpanProjection" &&
                selector.Source is not null &&
                Enum.TryParse(
                    selector.TimeSpanProjection,
                    ignoreCase: false,
                    out TimeSpanProjectionKind timeSpanProjection))
            {
                var source = CreateReplaySelector(selector.Source);
                var sourceDescriptor = source.SelectorDescriptor
                    ?? throw new InvalidDataException("A replayed TimeSpan projection source has no backend-neutral descriptor.");
                var descriptor = new Selector<T>.TimeSpanProjectionSelectorDescriptor(
                    sourceDescriptor,
                    timeSpanProjection);
                return ((ValueOrExpression<string>)string.Empty)
                    .WithSelectorDescriptor(descriptor);
            }

            if (selector.Kind == "NumericArithmetic" &&
                selector.Source is not null &&
                Enum.TryParse(
                    selector.NumericOperation,
                    ignoreCase: false,
                    out NumericArithmeticOperationKind numericOperation))
            {
                var source = CreateReplaySelector(selector.Source);
                var right = selector.OperandSource is null
                    ? null
                    : CreateReplaySelector(selector.OperandSource);
                var sourceDescriptor = source.SelectorDescriptor
                    ?? throw new InvalidDataException("A replayed numeric selector source has no backend-neutral descriptor.");
                var rightDescriptor = right?.SelectorDescriptor;
                var sourceType = ConditionWireTypes.Resolve(selector.NumericSourceTypeCode);
                var rightType = ConditionWireTypes.Resolve(selector.NumericRightTypeCode);
                var resultType = ConditionWireTypes.Resolve(selector.NumericResultTypeCode);
                object? literal = selector.HasNumericOperand
                    ? InhetoBinary
                        .FromStream(new BufferStream(selector.NumericOperandPayload))
                        .ToObject(resultType)
                    : null;
                bool bitwise = numericOperation is >= NumericArithmeticOperationKind.BitAnd and <= NumericArithmeticOperationKind.BitNot;
                string factoryName = bitwise
                    ? nameof(CreateIntegralBitwiseDescriptor)
                    : selector.OperandSource is not null
                        ? nameof(CreateNumericPropertyArithmeticDescriptor)
                        : nameof(CreateNumericArithmeticDescriptor);
                Type[] factoryTypes = bitwise
                    ? new[] { resultType }
                    : selector.OperandSource is not null
                        ? new[] { sourceType, rightType, resultType }
                        : new[] { sourceType, resultType };
                var factory = typeof(ConditionWireReplayer<T>)
                    .GetMethod(
                        factoryName,
                        BindingFlags.Static | BindingFlags.NonPublic)
                    ?.MakeGenericMethod(factoryTypes)
                    ?? throw new MissingMethodException(
                        typeof(ConditionWireReplayer<T>).FullName,
                        factoryName);
                object? descriptor = factory.Invoke(
                    null,
                    new object?[]
                    {
                        sourceDescriptor,
                        numericOperation,
                        rightDescriptor,
                        selector.HasNumericOperand,
                        literal
                    });
                if (descriptor is not IBackendNeutralSelectorDescriptor typedDescriptor)
                    throw new InvalidDataException("A replayed numeric selector descriptor could not be constructed.");
                return ((ValueOrExpression<string>)string.Empty)
                    .WithSelectorDescriptor(typedDescriptor);
            }

            if (selector.Kind == "TemporalArithmetic" &&
                selector.Source is not null &&
                Enum.TryParse(
                    selector.TemporalOperation,
                    ignoreCase: false,
                    out Selector<T>.TemporalArithmeticOperationKind temporalOperation))
            {
                ValueOrExpression<string> source = CreateReplaySelector(selector.Source);
                ValueOrExpression<string>? right = selector.OperandSource is null
                    ? null
                    : CreateReplaySelector(selector.OperandSource);
                IBackendNeutralSelectorDescriptor sourceDescriptor = source.SelectorDescriptor
                    ?? throw new InvalidDataException("A replayed temporal selector source has no backend-neutral descriptor.");
                IBackendNeutralSelectorDescriptor? rightDescriptor = right?.SelectorDescriptor;
                Type sourceType = ConditionWireTypes.Resolve(selector.TemporalSourceTypeCode);
                Type rightType = ConditionWireTypes.Resolve(selector.TemporalRightTypeCode);
                Type resultType = ConditionWireTypes.Resolve(selector.TemporalResultTypeCode);
                object? literal = selector.HasTemporalOperand
                    ? InhetoBinary
                        .FromStream(new BufferStream(selector.TemporalOperandPayload))
                        .ToObject(rightType)
                    : null;
                MethodInfo factory = typeof(ConditionWireReplayer<T>)
                    .GetMethod(
                        nameof(CreateTemporalArithmeticDescriptor),
                        BindingFlags.Static | BindingFlags.NonPublic)
                    ?.MakeGenericMethod(sourceType, rightType, resultType)
                    ?? throw new MissingMethodException(
                        typeof(ConditionWireReplayer<T>).FullName,
                        nameof(CreateTemporalArithmeticDescriptor));
                object? descriptor = factory.Invoke(
                    null,
                    new object?[]
                    {
                        sourceDescriptor,
                        temporalOperation,
                        rightDescriptor,
                        selector.HasTemporalOperand,
                        literal
                    });
                if (descriptor is not IBackendNeutralSelectorDescriptor typedDescriptor)
                    throw new InvalidDataException("A replayed temporal selector descriptor could not be constructed.");
                return ((ValueOrExpression<string>)string.Empty)
                    .WithSelectorDescriptor(typedDescriptor);
            }

            if (selector.Kind == "BuiltInOperation" &&
                selector.Source is not null &&
                Enum.TryParse(
                    selector.BuiltInOperation,
                    ignoreCase: false,
                    out BuiltInSelectorOperationKind builtInOperation))
            {
                ValueOrExpression<string> source = CreateReplaySelector(selector.Source);
                IBackendNeutralSelectorDescriptor sourceDescriptor = source.SelectorDescriptor
                    ?? throw new InvalidDataException("A replayed built-in selector source has no backend-neutral descriptor.");
                var descriptor = new Selector<T>.BuiltInSelectorDescriptor(
                    sourceDescriptor,
                    builtInOperation,
                    ConditionWireTypes.Resolve(selector.BuiltInSourceTypeCode),
                    ConditionWireTypes.Resolve(selector.BuiltInResultTypeCode),
                    selector.BuiltInFirstIntegerOperand,
                    selector.BuiltInSecondIntegerOperand,
                    selector.HasBuiltInTextOperand ? selector.BuiltInTextOperand : null);
                return ((ValueOrExpression<string>)string.Empty)
                    .WithSelectorDescriptor(descriptor);
            }

            throw new InvalidDataException(
                $"Condition selector kind '{selector.Kind}' cannot be replayed by this runtime.");
        }

        /// <summary>
        /// Recreates one typed numeric-arithmetic descriptor after the durable CLR identities have passed validation.<br/>
        /// Generic closure proves both numeric contracts once during replay; normal per-record evaluation remains reflection-free and allocation-free.<br/>
        /// </summary>
        /// <typeparam name="TSource">Exact source numeric type.<br/></typeparam>
        /// <typeparam name="TResult">Exact promoted result numeric type.<br/></typeparam>
        /// <param name="source">Replayed left source descriptor.<br/></param>
        /// <param name="operation">Validated arithmetic operation.<br/></param>
        /// <param name="right">Optional selector-valued right descriptor.<br/></param>
        /// <param name="hasLiteral">Whether the node owns a literal right operand.<br/></param>
        /// <param name="literal">Restored exact-result-type literal when present.<br/></param>
        /// <returns>The immutable typed descriptor consumed by native predicate evaluation.<br/></returns>
        private static IBackendNeutralSelectorDescriptor CreateNumericArithmeticDescriptor<TSource, TResult>(
            IBackendNeutralSelectorDescriptor source,
            NumericArithmeticOperationKind operation,
            IBackendNeutralSelectorDescriptor? right,
            bool hasLiteral,
            object? literal)
            where TSource : INumber<TSource>
            where TResult : INumber<TResult>
            => hasLiteral
                ? new Selector<T>.NumericArithmeticSelectorDescriptor<TSource, TResult>(
                    source,
                    operation,
                    (TResult)literal!)
                : new Selector<T>.NumericArithmeticSelectorDescriptor<TSource, TResult>(
                    source,
                    operation,
                    right);

        /// <summary>
        /// Recreates selector-to-selector numeric arithmetic with independently declared operand and result types.<br/>
        /// Generic closure occurs once during replay; record evaluation remains checked, typed, and reflection-free.<br/>
        /// </summary>
        /// <typeparam name="TSource">Exact left operand type.<br/></typeparam>
        /// <typeparam name="TRight">Exact right operand type.<br/></typeparam>
        /// <typeparam name="TResult">Explicit arithmetic result domain.<br/></typeparam>
        /// <param name="source">Replayed left semantic descriptor.<br/></param>
        /// <param name="operation">Validated arithmetic operation.<br/></param>
        /// <param name="right">Replayed right semantic descriptor.<br/></param>
        /// <param name="hasLiteral">Must be <see langword="false"/> for property arithmetic.<br/></param>
        /// <param name="literal">Must be absent for property arithmetic.<br/></param>
        /// <returns>The immutable typed property-arithmetic descriptor.<br/></returns>
        private static IBackendNeutralSelectorDescriptor CreateNumericPropertyArithmeticDescriptor<TSource, TRight, TResult>(
            IBackendNeutralSelectorDescriptor source,
            NumericArithmeticOperationKind operation,
            IBackendNeutralSelectorDescriptor? right,
            bool hasLiteral,
            object? literal)
            where TSource : INumber<TSource>
            where TRight : INumber<TRight>
            where TResult : INumber<TResult>
        {
            if (hasLiteral || literal is not null || right is null)
                throw new InvalidDataException("Numeric property arithmetic replay requires exactly one selector-valued right operand.");
            return new Selector<T>.NumericPropertyArithmeticSelectorDescriptor<TSource, TRight, TResult>(
                source,
                operation,
                right);
        }

        /// <summary>
        /// Recreates one closed temporal-arithmetic descriptor after durable operation and type validation succeeds.<br/>
        /// Reflection closes the generic types once during replay; ordinary record evaluation then uses the preselected typed operation delegate.<br/>
        /// </summary>
        /// <typeparam name="TLeft">Exact persisted left operand type.<br/></typeparam>
        /// <typeparam name="TRight">Exact persisted right operand type.<br/></typeparam>
        /// <typeparam name="TResult">Exact persisted result type.<br/></typeparam>
        /// <param name="source">Replayed left semantic descriptor.<br/></param>
        /// <param name="operation">Validated temporal operation.<br/></param>
        /// <param name="right">Optional replayed right semantic descriptor.<br/></param>
        /// <param name="hasLiteral">Whether the node owns a typed literal right operand.<br/></param>
        /// <param name="literal">Restored right-type literal when present.<br/></param>
        /// <returns>The immutable typed descriptor consumed by native semantic evaluation.<br/></returns>
        private static IBackendNeutralSelectorDescriptor CreateTemporalArithmeticDescriptor<TLeft, TRight, TResult>(
            IBackendNeutralSelectorDescriptor source,
            Selector<T>.TemporalArithmeticOperationKind operation,
            IBackendNeutralSelectorDescriptor? right,
            bool hasLiteral,
            object? literal)
            => hasLiteral
                ? new Selector<T>.TemporalArithmeticSelectorDescriptor<TLeft, TRight, TResult>(
                    source,
                    (TRight)literal!,
                    operation)
                : new Selector<T>.TemporalArithmeticSelectorDescriptor<TLeft, TRight, TResult>(
                    source,
                    right ?? throw new InvalidDataException(
                        "A replayed selector-valued temporal operation has no right descriptor."),
                    operation);

        /// <summary>
        /// Recreates one typed integral bitwise descriptor after durable type and operand validation succeeds.<br/>
        /// Reflection occurs once during replay; normal record evaluation uses the closed generic descriptor directly without boxing or dispatch discovery.<br/>
        /// </summary>
        /// <typeparam name="TInteger">Exact persisted integral source and result type.<br/></typeparam>
        /// <param name="source">Replayed left source descriptor.<br/></param>
        /// <param name="operation">Validated bitwise operation.<br/></param>
        /// <param name="right">Optional same-type selector operand.<br/></param>
        /// <param name="hasLiteral">Whether the node owns a literal operand or shift count.<br/></param>
        /// <param name="literal">Restored same-type literal when present.<br/></param>
        /// <returns>The immutable typed descriptor consumed by native predicate evaluation.<br/></returns>
        private static IBackendNeutralSelectorDescriptor CreateIntegralBitwiseDescriptor<TInteger>(
            IBackendNeutralSelectorDescriptor source,
            NumericArithmeticOperationKind operation,
            IBackendNeutralSelectorDescriptor? right,
            bool hasLiteral,
            object? literal)
            where TInteger : INumber<TInteger>, IBinaryInteger<TInteger>
            => hasLiteral
                ? new Selector<T>.IntegralBitwiseSelectorDescriptor<TInteger>(
                    source,
                    operation,
                    (TInteger)literal!)
                : new Selector<T>.IntegralBitwiseSelectorDescriptor<TInteger>(
                    source,
                    operation,
                    right);

        private static Type GetTemporalOperatorType(Type valueType)
        {
            if (valueType == typeof(DateOnly)) return typeof(Condition<T>.DateOnlyOperator);
            if (valueType == typeof(DateTime)) return typeof(Condition<T>.DateTimeOperator);
            if (valueType == typeof(TimeOnly)) return typeof(Condition<T>.TimeOnlyOperator);
            if (valueType == typeof(TimeSpan)) return typeof(Condition<T>.TimeSpanOperator);
            throw new InvalidDataException($"Unsupported temporal condition value type '{valueType.FullName}'.");
        }

        private static ValueOrExpression<TValue> ResolveValue<TValue>(
            ConditionWireValue wire,
            IReadOnlyList<object?> bindings)
        {
            if (wire.IsParameter)
            {
                var supplied = bindings[wire.ParameterPosition];
                if (supplied is ValueOrExpression<TValue> wrapper)
                    return wrapper;
                return (TValue)supplied!;
            }

            if (wire.IsNull)
                return (TValue)(object?)null!;
            var restored = InhetoBinary
                .FromStream(new BufferStream(wire.Payload))
                .ToObject(typeof(TValue));
            return (TValue)restored!;
        }

        private static IReadOnlyList<TValue> ResolveCollection<TValue>(
            ConditionWireValue wire,
            IReadOnlyList<object?> bindings)
        {
            object? source;
            if (wire.IsParameter)
            {
                source = bindings[wire.ParameterPosition];
                if (source is ValueOrExpression<IEnumerable<TValue>> deferred)
                    source = deferred.Invoke();
                else if (source is ValueOrExpression<TValue[]> deferredArray)
                    source = deferredArray.Invoke();
            }
            else
            {
                source = InhetoBinary
                    .FromStream(new BufferStream(wire.Payload))
                    .ToObject(ConditionWireTypes.Resolve(wire.TypeCode));
            }

            return source is IEnumerable<TValue> typed
                ? typed.ToArray()
                : throw new ConditionBindingException(
                    $"Condition collection operand expected values of type '{typeof(TValue).FullName}'.");
        }

        private static object ResolveValueAs(
            Type valueType,
            ConditionWireValue wire,
            IReadOnlyList<object?> bindings)
        {
            var method = typeof(ConditionWireReplayer<T>)
                .GetMethod(nameof(ResolveValue), BindingFlags.Static | BindingFlags.NonPublic)!
                .MakeGenericMethod(valueType);
            return method.Invoke(null, new object?[] { wire, bindings })!;
        }

        /// <summary>
        /// Restores one structured temporal collection using the constructor's declared <c>ValueOrExpression&lt;T&gt;</c> element type.<br/>
        /// The supplied type already contains the operand wrapper; only its value type is extracted for binding or literal restoration.<br/>
        /// Existing deferred wrappers are retained without invoking application factories during replay.<br/>
        /// </summary>
        /// <param name="elementType">Exact wrapped element type from the retained operation constructor's <c>IEnumerable&lt;ValueOrExpression&lt;T&gt;&gt;</c> parameter.<br/></param>
        /// <param name="values">Ordered stored collection operands, including any externally bound elements.<br/></param>
        /// <param name="bindings">Validated external values or deferred wrappers supplied to this replay.<br/></param>
        /// <returns>An array of the declared wrapped element type, assignable to the operation constructor's collection parameter.<br/></returns>
        private static Array CreateTypedWrapperArray(
            Type elementType,
            IEnumerable<ConditionWireValue> values,
            IReadOnlyList<object?> bindings)
        {
            var source = values.ToArray();
            var valueType = elementType.GetGenericArguments()[0];
            var result = Array.CreateInstance(elementType, source.Length);
            for (var index = 0; index < source.Length; index++)
                result.SetValue(ResolveValueAs(valueType, source[index], bindings), index);
            return result;
        }

        private static void InvokeGeneric(
            string methodName,
            Type genericType,
            Condition<T>.Builder builder,
            ConditionWireLeaf leaf,
            IReadOnlyList<object?> bindings)
        {
            try
            {
                typeof(ConditionWireReplayer<T>)
                    .GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!
                    .MakeGenericMethod(genericType)
                    .Invoke(null, new object?[] { builder, leaf, bindings });
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            }
        }

        /// <summary>
        /// Recognizes a deferred whole-set binding without invoking it or fixing its membership cardinality during descriptor replay.<br/>
        /// Enumerable wrappers are retained verbatim, preserving explicit-parameter identity and execution snapshots.<br/>
        /// Array wrappers are adapted once to an enumerable factory; their optional name is retained and their values are read during ordinary leaf preparation, never per record.<br/>
        /// Literal and non-deferred bindings return false so their established build-time copying and per-element hooks remain unchanged.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact membership element type required by the replayed operation.<br/></typeparam>
        /// <param name="wire">Persisted collection operand metadata.<br/></param>
        /// <param name="bindings">Validated caller-supplied descriptor bindings.<br/></param>
        /// <param name="operand">Receives the retained or adapted whole-set wrapper when recognized.<br/></param>
        /// <returns>True only for a supported deferred collection wrapper.<br/></returns>
        private static bool TryResolveDeferredCollection<TValue>(
            ConditionWireValue wire,
            IReadOnlyList<object?> bindings,
            out ValueOrExpression<IEnumerable<TValue>> operand)
        {
            operand = default!;
            if (!wire.IsParameter)
                return false;
            if (bindings[wire.ParameterPosition] is ValueOrExpression<IEnumerable<TValue>> deferred)
            {
                operand = deferred;
                return true;
            }
            if (bindings[wire.ParameterPosition] is ValueOrExpression<TValue[]> deferredArray)
            {
                operand = new ValueOrExpression<IEnumerable<TValue>>(() => deferredArray.Invoke());
                if (deferredArray.BindingName is { } name)
                    operand.Named(name);
                return true;
            }
            return false;
        }
    }

    internal static class ConditionWireRenderer
    {
        /// <summary>
        /// Resolves the direct source property used for labels, sensitivity checks, and value-formatting context from one recursive selector node.<br/>
        /// The derived operation remains visible in the surrounding display label while policy hooks continue to receive the stable source path configured by the developer.<br/>
        /// </summary>
        /// <param name="selector">Validated selector node being displayed.<br/></param>
        /// <returns>The normalized direct source-property path.<br/></returns>
        private static string GetSelectorSourcePropertyPath(ConditionWireSelector selector)
        {
            ArgumentNullException.ThrowIfNull(selector);
            return selector.Kind switch
            {
                "Property" when !string.IsNullOrWhiteSpace(selector.PropertyPath) => selector.PropertyPath,
                "SemanticIndex" when !string.IsNullOrWhiteSpace(selector.PropertyPath) => selector.PropertyPath,
                "BinarySlice" when selector.Source is not null => GetSelectorSourcePropertyPath(selector.Source),
                "DecimalOperation" when selector.Source is not null => GetSelectorSourcePropertyPath(selector.Source),
                "TimeSpanProjection" when selector.Source is not null => GetSelectorSourcePropertyPath(selector.Source),
                "NumericArithmetic" when selector.Source is not null => GetSelectorSourcePropertyPath(selector.Source),
                "TemporalArithmetic" when selector.Source is not null => GetSelectorSourcePropertyPath(selector.Source),
                "StringOperation" when selector.Source is not null => GetSelectorSourcePropertyPath(selector.Source),
                "BuiltInOperation" when selector.Source is not null => GetSelectorSourcePropertyPath(selector.Source),
                _ => throw new ConditionDisplayNotSupportedException(
                    $"Condition selector kind '{selector.Kind}' has no displayable source property.")
            };
        }

        internal static string Render(
            ConditionWireSequence sequence,
            IReadOnlyList<ConditionWireParameter> parameters,
            bool friendly)
        {
            var builder = new StringBuilder();
            RenderSequence(builder, sequence, parameters, friendly);
            return builder.ToString();
        }

        private static void RenderSequence(
            StringBuilder builder,
            ConditionWireSequence sequence,
            IReadOnlyList<ConditionWireParameter> parameters,
            bool friendly)
        {
            foreach (var item in sequence.Items)
            {
                if (builder.Length > 0 && builder[^1] != ' ')
                    builder.Append(' ');
                switch (item.Kind)
                {
                    case "And":
                        builder.Append(friendly ? "and" : "AND");
                        break;
                    case "Or":
                        builder.Append(friendly ? "or" : "OR");
                        break;
                    case "Group":
                        builder.Append('(');
                        RenderSequence(builder, item.Group!, parameters, friendly);
                        builder.Append(')');
                        break;
                    case "Leaf":
                        RenderLeaf(builder, item.Leaf!, parameters, friendly);
                        break;
                }
            }
        }

        private static void RenderLeaf(
            StringBuilder builder,
            ConditionWireLeaf leaf,
            IReadOnlyList<ConditionWireParameter> parameters,
            bool friendly)
        {
            if (!friendly)
                builder.Append('(');
            RenderCanonicalSelector(builder, leaf.Selector);
            builder.Append(friendly ? ' ' : " AS ");
            if (!friendly)
            {
                builder.Append(ConditionWireTypes.Resolve(leaf.ValueTypeCode).Name);
                builder.Append(' ');
            }
            builder.Append(friendly ? FriendlyOperator(leaf.Operator) : leaf.Operator.ToUpperInvariant());

            if (leaf.OperandSelector is not null)
            {
                builder.Append(' ');
                RenderCanonicalSelector(builder, leaf.OperandSelector);
            }

            if (leaf.Operands.Length > 0)
            {
                builder.Append(' ');
                for (var index = 0; index < leaf.Operands.Length; index++)
                {
                    if (index > 0)
                        builder.Append(leaf.Operator.Contains("Between", StringComparison.Ordinal) ? " AND " : ", ");
                    RenderValue(builder, leaf.Operands[index], parameters);
                }
            }
            RenderOptions(builder, leaf, friendly);
            if (!friendly)
                builder.Append(')');
        }

        /// <summary>
        /// Appends one deterministic backend-neutral selector expression to canonical condition text.<br/>
        /// Computed nodes retain their operation and arguments so canonical output cannot collapse a derived selector into its source property.<br/>
        /// </summary>
        /// <param name="builder">Destination text builder.<br/></param>
        /// <param name="selector">Validated recursive selector node.<br/></param>
        private static void RenderCanonicalSelector(
            StringBuilder builder,
            ConditionWireSelector selector)
        {
            switch (selector.Kind)
            {
                case "Property":
                    builder.Append(selector.PropertyPath);
                    return;
                case "SemanticIndex":
                    builder.Append("Index(\"").Append(selector.PropertyPath.Replace("\"", "\\\"", StringComparison.Ordinal)).Append("\")");
                    return;
                case "BinarySlice":
                    RenderCanonicalSelector(
                        builder,
                        selector.Source ?? throw new InvalidDataException(
                            "A binary-slice selector has no canonical source."));
                    if (string.Equals(selector.BinarySliceKind, nameof(BinarySliceKind.ByteArray), StringComparison.Ordinal))
                    {
                        builder
                            .Append(".Slice(")
                            .Append(selector.Offset.ToString(CultureInfo.InvariantCulture))
                            .Append(", ")
                            .Append(selector.Length.ToString(CultureInfo.InvariantCulture))
                            .Append(')');
                    }
                    else
                    {
                        builder
                            .Append(".BinarySlice(")
                            .Append(selector.BinarySliceKind)
                            .Append(", ")
                            .Append(selector.Offset.ToString(CultureInfo.InvariantCulture))
                            .Append(", ")
                            .Append(selector.Length.ToString(CultureInfo.InvariantCulture));
                        if (string.Equals(
                                selector.BinarySliceKind,
                                nameof(BinarySliceKind.CustomEncodingString),
                                StringComparison.Ordinal))
                        {
                            builder
                                .Append(", codePage: ")
                                .Append(selector.EncodingCodePage.ToString(CultureInfo.InvariantCulture))
                                .Append(", coercion: ")
                                .Append(((Coercion.Text)selector.TextCoercion).ToString());
                        }

                        builder.Append(')');
                    }
                    return;
                case "DecimalOperation":
                    RenderCanonicalSelector(
                        builder,
                        selector.Source ?? throw new InvalidDataException(
                            "A decimal-operation selector has no canonical source."));
                    builder
                        .Append('.')
                        .Append(selector.DecimalOperation)
                        .Append('(');
                    if (selector.OperandSource is not null)
                    {
                        RenderCanonicalSelector(builder, selector.OperandSource);
                    }
                    else if (selector.HasDecimalOperand)
                    {
                        builder.Append(selector.DecimalOperand.ToString(CultureInfo.InvariantCulture));
                    }
                    else if (selector.HasIntegerOperand)
                    {
                        builder.Append(selector.IntegerOperand.ToString(CultureInfo.InvariantCulture));
                    }
                    builder.Append(')');
                    return;
                case "TimeSpanProjection":
                    RenderCanonicalSelector(
                        builder,
                        selector.Source ?? throw new InvalidDataException(
                            "A TimeSpan projection selector has no canonical source."));
                    builder
                        .Append('.')
                        .Append(selector.TimeSpanProjection);
                    return;
                case "NumericArithmetic":
                    RenderCanonicalSelector(
                        builder,
                        selector.Source ?? throw new InvalidDataException(
                            "A numeric-arithmetic selector has no canonical source."));
                    builder
                        .Append('.')
                        .Append(selector.NumericOperation)
                        .Append('(');
                    if (selector.OperandSource is not null)
                    {
                        RenderCanonicalSelector(builder, selector.OperandSource);
                    }
                    else if (selector.HasNumericOperand)
                    {
                        var resultType = ConditionWireTypes.Resolve(selector.NumericResultTypeCode);
                        var literal = InhetoBinary
                            .FromStream(new BufferStream(selector.NumericOperandPayload))
                            .ToObject(resultType);
                        builder.Append(Convert.ToString(literal, CultureInfo.InvariantCulture));
                    }
                    builder.Append(')');
                    return;
                case "TemporalArithmetic":
                    RenderCanonicalSelector(
                        builder,
                        selector.Source ?? throw new InvalidDataException(
                            "A temporal-arithmetic selector has no canonical source."));
                    builder
                        .Append('.')
                        .Append(GetTemporalCanonicalVerb(selector.TemporalOperation))
                        .Append('(');
                    if (selector.OperandSource is not null)
                    {
                        RenderCanonicalSelector(builder, selector.OperandSource);
                    }
                    else if (selector.HasTemporalOperand)
                    {
                        Type rightType = ConditionWireTypes.Resolve(selector.TemporalRightTypeCode);
                        object? literal = InhetoBinary
                            .FromStream(new BufferStream(selector.TemporalOperandPayload))
                            .ToObject(rightType);
                        builder.Append(Convert.ToString(literal, CultureInfo.InvariantCulture));
                    }
                    builder.Append(')');
                    return;
                case "StringOperation":
                    RenderCanonicalSelector(
                        builder,
                        selector.Source ?? throw new InvalidDataException(
                            "A string-operation selector has no canonical source."));
                    if (selector.StringOperation == StringOperationKind.AsUtf8.ToString())
                    {
                        builder.Append(".AsUtf8");
                        return;
                    }
                    builder
                        .Append('.')
                        .Append(selector.StringOperation)
                        .Append('(');
                    var hasArgument = false;
                    if (selector.OperandSource is not null)
                    {
                        RenderCanonicalSelector(builder, selector.OperandSource);
                        hasArgument = true;
                    }
                    if (selector.TextOperandState != 0)
                    {
                        AppendSelectorTextArgument(builder, selector.TextOperandState, selector.TextOperand);
                        hasArgument = true;
                    }
                    if (selector.SecondTextOperandState != 0)
                    {
                        if (hasArgument) builder.Append(", ");
                        AppendSelectorTextArgument(builder, selector.SecondTextOperandState, selector.SecondTextOperand);
                        hasArgument = true;
                    }
                    if (selector.HasLongOperand)
                    {
                        if (hasArgument) builder.Append(", ");
                        builder.Append(selector.LongOperand.ToString(CultureInfo.InvariantCulture));
                        hasArgument = true;
                    }
                    if (selector.HasSecondLongOperand)
                    {
                        if (hasArgument) builder.Append(", ");
                        builder.Append(selector.SecondLongOperand.ToString(CultureInfo.InvariantCulture));
                        hasArgument = true;
                    }
                    if (selector.HasCharacterOperand)
                    {
                        if (hasArgument) builder.Append(", ");
                        AppendSelectorCharacterArgument(builder, (char)selector.CharacterOperand);
                        hasArgument = true;
                    }
                    if (selector.StringIgnoreCase)
                    {
                        if (hasArgument) builder.Append(", ");
                        builder.Append("ignoreCase: true");
                        hasArgument = true;
                    }
                    if (selector.StringCultureName.Length != 0)
                    {
                        if (hasArgument) builder.Append(", ");
                        builder.Append("culture: ");
                        AppendSelectorTextArgument(builder, 2, selector.StringCultureName);
                        hasArgument = true;
                    }
                    if (selector.StringOperation == StringOperationKind.MatchValue.ToString())
                    {
                        if (hasArgument) builder.Append(", ");
                        builder
                            .Append("options: ")
                            .Append(selector.StringRegexOptions.ToString(CultureInfo.InvariantCulture))
                            .Append(", timeoutTicks: ")
                            .Append(selector.StringRegexMatchTimeoutTicks.ToString(CultureInfo.InvariantCulture));
                    }
                    builder.Append(')');
                    return;
                case "BuiltInOperation":
                    RenderCanonicalSelector(
                        builder,
                        selector.Source ?? throw new InvalidDataException(
                            "A built-in selector operation has no canonical source."));
                    RenderBuiltInCanonicalOperation(builder, selector);
                    return;
                default:
                    throw new InvalidDataException(
                        $"Condition selector kind '{selector.Kind}' cannot be rendered.");
            }
        }

        /// <summary>
        /// Maps one exact temporal operation identity to the public .NET-vernacular selector verb used by canonical text.<br/>
        /// Operand and result types already disambiguate the overload, so implementation-specific type names do not leak into developer-facing syntax.<br/>
        /// </summary>
        /// <param name="operation">Persisted temporal operation identity.<br/></param>
        /// <returns><c>Add</c>, <c>Subtract</c>, <c>Multiply</c>, or <c>Divide</c>.<br/></returns>
        private static string GetTemporalCanonicalVerb(string operation)
            => operation switch
            {
                "DateTimeAddTimeSpan" or
                "TimeOnlyAddTimeSpan" or
                "TimeSpanAddTimeSpan" => "Add",
                "DateTimeSubtractTimeSpan" or
                "DateTimeSubtractDateTime" or
                "TimeOnlySubtractTimeSpan" or
                "TimeOnlySubtractTimeOnly" or
                "TimeSpanSubtractTimeSpan" => "Subtract",
                "TimeSpanMultiplyDouble" => "Multiply",
                "TimeSpanDivideDouble" or
                "TimeSpanDivideTimeSpan" => "Divide",
                _ => throw new InvalidDataException(
                    $"Temporal operation '{operation}' has no canonical selector verb.")
            };

        /// <summary>
        /// Appends the public-fluent form of one validated built-in selector transformation.<br/>
        /// Component projections remain properties while argument-bearing binary and Guid operations retain invariant immutable operands.<br/>
        /// </summary>
        /// <param name="builder">Destination canonical text builder.<br/></param>
        /// <param name="selector">Validated built-in wire selector.<br/></param>
        private static void RenderBuiltInCanonicalOperation(
            StringBuilder builder,
            ConditionWireSelector selector)
        {
            if (!Enum.TryParse(
                    selector.BuiltInOperation,
                    ignoreCase: false,
                    out BuiltInSelectorOperationKind operation))
            {
                throw new InvalidDataException(
                    $"Unknown built-in selector operation '{selector.BuiltInOperation}'.");
            }

            string? property = operation switch
            {
                BuiltInSelectorOperationKind.DateTimeYear or BuiltInSelectorOperationKind.DateOnlyYear => "Year",
                BuiltInSelectorOperationKind.DateTimeMonth or BuiltInSelectorOperationKind.DateOnlyMonth => "Month",
                BuiltInSelectorOperationKind.DateTimeDay or BuiltInSelectorOperationKind.DateOnlyDay => "Day",
                BuiltInSelectorOperationKind.DateTimeHour or BuiltInSelectorOperationKind.TimeOnlyHour => "Hour",
                BuiltInSelectorOperationKind.DateTimeMinute or BuiltInSelectorOperationKind.TimeOnlyMinute => "Minute",
                BuiltInSelectorOperationKind.DateTimeSecond or BuiltInSelectorOperationKind.TimeOnlySecond => "Second",
                BuiltInSelectorOperationKind.DateTimeMillisecond or BuiltInSelectorOperationKind.TimeOnlyMillisecond => "Millisecond",
                BuiltInSelectorOperationKind.DateTimeNanoSlice or BuiltInSelectorOperationKind.TimeOnlyNanoSlice => "NanoSlice",
                BuiltInSelectorOperationKind.DateTimeDayOfWeek or BuiltInSelectorOperationKind.DateOnlyDayOfWeek => "DayOfWeek",
                BuiltInSelectorOperationKind.GuidAsBinary => "AsBinary",
                BuiltInSelectorOperationKind.NumericToDecimal => "AsDecimal",
                BuiltInSelectorOperationKind.DecimalToInt64 => "AsLong",
                _ => null
            };
            if (property is not null)
            {
                builder.Append('.').Append(property);
                return;
            }

            string method = operation switch
            {
                BuiltInSelectorOperationKind.BinaryGetByte => "GetByte",
                BuiltInSelectorOperationKind.BinaryToHex => "ToHex",
                BuiltInSelectorOperationKind.BinaryToBase64 => "ToBase64",
                BuiltInSelectorOperationKind.NumericToHex => "ToHex",
                BuiltInSelectorOperationKind.NumericToBase64 => "ToBase64",
                BuiltInSelectorOperationKind.StringConcatLiteral => "Concat",
                BuiltInSelectorOperationKind.StringPrependLiteral => "PrependLiteral",
                BuiltInSelectorOperationKind.ScalarToInvariantString => "AsInvariantString",
                BuiltInSelectorOperationKind.EnumToString => "AsString",
                BuiltInSelectorOperationKind.GuidAsStringHyphenated or BuiltInSelectorOperationKind.GuidAsStringNumeric => "AsString",
                BuiltInSelectorOperationKind.GuidSegment => "Segment",
                BuiltInSelectorOperationKind.GuidSlice => "Slice",
                BuiltInSelectorOperationKind.GuidStartsWith => "StartsWith",
                BuiltInSelectorOperationKind.GuidEndsWith => "EndsWith",
                BuiltInSelectorOperationKind.GuidContains => "Contains",
                BuiltInSelectorOperationKind.GuidMatchesPattern => "MatchesPattern",
                BuiltInSelectorOperationKind.GuidEqual => "EqualTo",
                BuiltInSelectorOperationKind.GuidNotEqual => "NotEqualTo",
                _ => throw new InvalidDataException($"Unknown built-in selector operation '{operation}'.")
            };
            builder.Append('.').Append(method).Append('(');
            switch (operation)
            {
                case BuiltInSelectorOperationKind.BinaryGetByte:
                case BuiltInSelectorOperationKind.GuidSegment:
                    builder.Append(selector.BuiltInFirstIntegerOperand.ToString(CultureInfo.InvariantCulture));
                    break;
                case BuiltInSelectorOperationKind.NumericToHex:
                case BuiltInSelectorOperationKind.NumericToBase64:
                    if (selector.BuiltInFirstIntegerOperand != 0)
                        builder.Append("bigEndian: true");
                    break;
                case BuiltInSelectorOperationKind.GuidSlice:
                    builder
                        .Append(selector.BuiltInFirstIntegerOperand.ToString(CultureInfo.InvariantCulture))
                        .Append(", ")
                        .Append(selector.BuiltInSecondIntegerOperand.ToString(CultureInfo.InvariantCulture));
                    break;
                case BuiltInSelectorOperationKind.GuidAsStringHyphenated:
                    builder.Append(nameof(GuidStringFormat.Hyphenated));
                    break;
                case BuiltInSelectorOperationKind.GuidAsStringNumeric:
                    builder.Append(nameof(GuidStringFormat.Numeric));
                    break;
                case BuiltInSelectorOperationKind.GuidStartsWith:
                case BuiltInSelectorOperationKind.GuidEndsWith:
                case BuiltInSelectorOperationKind.GuidContains:
                case BuiltInSelectorOperationKind.GuidMatchesPattern:
                case BuiltInSelectorOperationKind.GuidEqual:
                case BuiltInSelectorOperationKind.GuidNotEqual:
                    AppendSelectorTextArgument(builder, 2, selector.BuiltInTextOperand);
                    break;
                case BuiltInSelectorOperationKind.StringConcatLiteral:
                case BuiltInSelectorOperationKind.StringPrependLiteral:
                    AppendSelectorTextArgument(builder, 2, selector.BuiltInTextOperand);
                    break;
                case BuiltInSelectorOperationKind.ScalarToInvariantString:
                    if (selector.HasBuiltInTextOperand)
                        AppendSelectorTextArgument(builder, 2, selector.BuiltInTextOperand);
                    break;
            }
            builder.Append(')');
        }

        /// <summary>
        /// Appends one validated selector's deterministic canonical identity for internal non-condition consumers such as grouping metadata.<br/>
        /// The implementation deliberately delegates to the condition renderer's single recursive selector grammar so persisted condition text and grouping identity cannot drift.<br/>
        /// </summary>
        /// <param name="builder">Destination receiving canonical selector text.<br/></param>
        /// <param name="selector">Validated recursive selector node.<br/></param>
        internal static void RenderCanonicalSelectorIdentity(
            StringBuilder builder,
            ConditionWireSelector selector)
            => RenderCanonicalSelector(builder, selector);

        /// <summary>
        /// Appends one null-or-string selector argument using deterministic C#-like quoting for canonical display.<br/>
        /// This helper is diagnostic only; durable execution consumes the explicit operand-state field rather than reparsing display text.<br/>
        /// </summary>
        /// <param name="builder">Destination canonical-text builder.<br/></param>
        /// <param name="state">One for null or two for a concrete string value.<br/></param>
        /// <param name="value">Concrete value when <paramref name="state"/> is two.<br/></param>
        private static void AppendSelectorTextArgument(StringBuilder builder, int state, string value)
        {
            if (state == 1)
            {
                builder.Append("null");
                return;
            }

            builder.Append('"');
            for (var index = 0; index < value.Length; index++)
            {
                var c = value[index];
                if (c is '\\' or '"') builder.Append('\\');
                builder.Append(c);
            }
            builder.Append('"');
        }

        /// <summary>
        /// Appends one canonical C#-style character literal with escapes for delimiters, backslashes, and control characters.<br/>
        /// This prevents distinct character operands from collapsing to visually ambiguous canonical selector text.<br/>
        /// </summary>
        /// <param name="builder">Destination canonical text builder.<br/></param>
        /// <param name="value">Character operand to append.<br/></param>
        private static void AppendSelectorCharacterArgument(StringBuilder builder, char value)
        {
            builder.Append('\'');
            switch (value)
            {
                case '\\': builder.Append("\\\\"); break;
                case '\'': builder.Append("\\'"); break;
                case '\r': builder.Append("\\r"); break;
                case '\n': builder.Append("\\n"); break;
                case '\t': builder.Append("\\t"); break;
                case '\0': builder.Append("\\0"); break;
                default:
                    if (char.IsControl(value))
                        builder.Append("\\u").Append(((int)value).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        builder.Append(value);
                    break;
            }
            builder.Append('\'');
        }

        /// <summary>
        /// Appends semantic leaf options that affect execution but are not ordinary value operands.<br/>
        /// Canonical output names every option defined by the current descriptor families so diagnostic text explains intent instead of leaking primitive storage buckets.<br/>
        /// A deterministic typed-array fallback remains for a future family until that family defines stable semantic option names.<br/>
        /// Friendly output retains the established support-oriented string and binary descriptions.<br/>
        /// </summary>
        /// <param name="builder">Destination text builder.<br/></param>
        /// <param name="leaf">Predicate leaf whose options should be rendered.<br/></param>
        /// <param name="friendly">Whether support-oriented text rather than canonical diagnostic text is requested.<br/></param>
        private static void RenderOptions(
            StringBuilder builder,
            ConditionWireLeaf leaf,
            bool friendly)
        {
            if (leaf.TextOptions.Length == 0 &&
                leaf.IntegerOptions.Length == 0 &&
                leaf.BooleanOptions.Length == 0)
            {
                return;
            }

            if (!friendly)
            {
                RenderCanonicalOptions(builder, leaf);
                return;
            }

            if (leaf.Family == "String")
            {
                builder.Append(" (");
                builder.Append(leaf.BooleanOptions[0] ? "ignore case" : "case sensitive");
                if (!string.IsNullOrEmpty(leaf.TextOptions[0]))
                    builder.Append(", culture ").Append(leaf.TextOptions[0]);
                builder.Append(", index mode ")
                    .Append(leaf.IntegerOptions[0].ToString(CultureInfo.InvariantCulture))
                    .Append(')');
                return;
            }

            if (leaf.Family == "Binary" && !string.IsNullOrEmpty(leaf.TextOptions[0]))
                builder.Append(" (parameter alias ").Append(leaf.TextOptions[0]).Append(')');
        }

        /// <summary>
        /// Appends deterministic family-aware canonical option names for every option-bearing predicate family in the current descriptor format.<br/>
        /// Names describe stable execution semantics rather than the primitive arrays used by the binary wire document.<br/>
        /// Unknown future families fall back to lossless typed arrays so diagnostic output never silently drops identity-affecting state.<br/>
        /// </summary>
        /// <param name="builder">Destination canonical-text builder.<br/></param>
        /// <param name="leaf">Validated predicate leaf whose option counts and family contract are already known.<br/></param>
        private static void RenderCanonicalOptions(StringBuilder builder, ConditionWireLeaf leaf)
        {
            builder.Append(" OPTIONS(");
            switch (leaf.Family)
            {
                case "String":
                    AppendCanonicalNamedString(builder, "culture", leaf.TextOptions[0]);
                    builder.Append(", indexType=").Append(leaf.IntegerOptions[0] switch
                    {
                        0 => "Default",
                        1 => "CollationKey",
                        2 => "FoldedText",
                        _ => leaf.IntegerOptions[0].ToString(CultureInfo.InvariantCulture)
                    });
                    builder.Append(", ignoreCase=").Append(leaf.BooleanOptions[0] ? "true" : "false");
                    break;
                case "Binary":
                    AppendCanonicalNamedString(builder, "parameterAlias", leaf.TextOptions[0]);
                    break;
                case "PropertyComparison":
                    builder.Append("leftType=")
                        .Append(GetFriendlyTypeName(ConditionWireTypes.Resolve(leaf.TextOptions[0])))
                        .Append(", rightType=")
                        .Append(GetFriendlyTypeName(ConditionWireTypes.Resolve(leaf.TextOptions[1])))
                        .Append(", ");
                    AppendCanonicalNamedString(builder, "culture", leaf.TextOptions[2]);
                    builder.Append(", coercionPolicyVersion=")
                        .Append(leaf.IntegerOptions[0].ToString(CultureInfo.InvariantCulture));
                    builder.Append(", ignoreCase=").Append(leaf.BooleanOptions[0] ? "true" : "false");
                    break;
                default:
                    builder.Append("text=[");
                    AppendEscapedStrings(builder, leaf.TextOptions);
                    builder.Append("], integer=[");
                    builder.Append(string.Join(",", leaf.IntegerOptions.Select(
                        static value => value.ToString(CultureInfo.InvariantCulture))));
                    builder.Append("], boolean=[");
                    builder.Append(string.Join(",", leaf.BooleanOptions.Select(
                        static value => value ? "true" : "false")));
                    builder.Append(']');
                    break;
            }
            builder.Append(')');
        }

        /// <summary>
        /// Appends one named canonical string option using the same invariant escaping contract as canonical option collections.<br/>
        /// Empty strings remain explicit because they are meaningful sentinels such as invariant culture or an absent parameter alias.<br/>
        /// </summary>
        /// <param name="builder">Destination canonical-text builder.<br/></param>
        /// <param name="name">Stable semantic option name.<br/></param>
        /// <param name="value">Exact option value to quote and escape.<br/></param>
        private static void AppendCanonicalNamedString(StringBuilder builder, string name, string value)
        {
            builder.Append(name).Append('=');
            AppendEscapedString(builder, value);
        }

        /// <summary>
        /// Appends a deterministic comma-separated sequence of quoted strings for canonical diagnostic output.<br/>
        /// Backslashes and quote characters are escaped so distinct semantic option values cannot collapse into the same rendered text.<br/>
        /// </summary>
        /// <param name="builder">Destination text builder.<br/></param>
        /// <param name="values">Ordered string values to append without surrounding collection brackets.<br/></param>
        private static void AppendEscapedStrings(StringBuilder builder, IEnumerable<string> values)
        {
            var first = true;
            foreach (var value in values)
            {
                if (!first)
                    builder.Append(',');
                AppendEscapedString(builder, value);
                first = false;
            }
        }

        /// <summary>
        /// Appends one deterministically escaped canonical string literal without allocating an intermediate replacement string.<br/>
        /// Backslashes and quotes receive a leading backslash; all other characters retain their exact ordinal representation.<br/>
        /// </summary>
        /// <param name="builder">Destination canonical-text builder.<br/></param>
        /// <param name="value">Exact string value to quote and escape.<br/></param>
        private static void AppendEscapedString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (char character in value)
            {
                if (character is '\\' or '"')
                    builder.Append('\\');
                builder.Append(character);
            }
            builder.Append('"');
        }

        private static void RenderValue(
            StringBuilder builder,
            ConditionWireValue value,
            IReadOnlyList<ConditionWireParameter> parameters)
        {
            if (value.IsParameter)
            {
                var parameter = parameters[value.ParameterPosition];
                builder.Append('$');
                builder.Append(string.IsNullOrWhiteSpace(parameter.Name)
                    ? parameter.Position.ToString(CultureInfo.InvariantCulture)
                    : parameter.Name);
                builder.Append(':');
                builder.Append(ConditionWireTypes.Resolve(parameter.TypeCode).Name);
                return;
            }
            if (value.IsNull)
            {
                builder.Append("null");
                return;
            }

            var type = ConditionWireTypes.Resolve(value.TypeCode);
            var restored = InhetoBinary.FromStream(new BufferStream(value.Payload)).ToObject(type);
            if (restored is string text)
                builder.Append('"').Append(text.Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
            else if (restored is IEnumerable enumerable and not byte[])
                builder.Append('[').Append(string.Join(", ", enumerable.Cast<object?>())).Append(']');
            else if (restored is byte[] bytes)
                builder.Append("0x").Append(Convert.ToHexString(bytes));
            else
                builder.Append(Convert.ToString(restored, CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Renders one validated semantic condition for the requested human audience and layout.<br/>
        /// This path renders the validated semantic nodes directly and masks configured sensitive operands before literal restoration or caller formatting.<br/>
        /// </summary>
        /// <param name="sequence">Validated semantic condition sequence.<br/></param>
        /// <param name="parameters">Validated ordered parameter contract.<br/></param>
        /// <param name="recordType">Generic record type owned by the condition.<br/></param>
        /// <param name="options">Presentation controls for this rendering operation.<br/></param>
        /// <returns>One-way human-readable text.<br/></returns>
        internal static string RenderDisplay(
            ConditionWireSequence sequence,
            IReadOnlyList<ConditionWireParameter> parameters,
            Type recordType,
            ConditionTextOptions options)
        {
            ArgumentNullException.ThrowIfNull(sequence);
            ArgumentNullException.ThrowIfNull(parameters);
            ArgumentNullException.ThrowIfNull(recordType);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(options.Culture);
            ArgumentNullException.ThrowIfNull(options.SensitivePropertyPaths);
            if (char.IsControl(options.SensitiveMaskCharacter) ||
                char.IsWhiteSpace(options.SensitiveMaskCharacter))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "SensitiveMaskCharacter cannot be whitespace or a control character.");
            }

            var context = new DisplayRenderContext(recordType, options);
            DisplayNode root = ParseDisplaySequence(sequence);
            var builder = new StringBuilder();
            if (options.Layout == ConditionTextLayout.Outline)
            {
                if (options.IncludeRecordType)
                {
                    builder.Append(context.RecordHeading);
                    builder.AppendLine();
                    builder.AppendLine();
                }
                RenderOutlineNode(builder, root, parameters, context, 0);
                return builder.ToString().TrimEnd();
            }

            if (options.IncludeRecordType)
            {
                builder.Append(context.RecordHeading);
                builder.Append(": ");
            }
            RenderNarrativeNode(builder, root, parameters, context, parentPrecedence: 0);
            if (builder.Length > 0 && builder[^1] != '.')
                builder.Append('.');
            return builder.ToString();
        }

        /// <summary>
        /// Parses one flat wire sequence into an explicit precedence tree while preserving every authored group boundary.<br/>
        /// </summary>
        /// <param name="sequence">Validated wire sequence to parse.<br/></param>
        /// <returns>An explicit display tree.<br/></returns>
        private static DisplayNode ParseDisplaySequence(ConditionWireSequence sequence)
        {
            var orSegments = new List<List<DisplayNode>>();
            var currentAndSegment = new List<DisplayNode>();
            foreach (ConditionWireItem item in sequence.Items)
            {
                switch (item.Kind)
                {
                    case "Leaf":
                        currentAndSegment.Add(new DisplayLeafNode(
                            item.Leaf ?? throw new InvalidDataException("Display leaf item has no semantic leaf.")));
                        break;
                    case "Group":
                        currentAndSegment.Add(new DisplayGroupNode(ParseDisplaySequence(
                            item.Group ?? throw new InvalidDataException("Display group item has no semantic sequence."))));
                        break;
                    case "And":
                        break;
                    case "Or":
                        if (currentAndSegment.Count == 0)
                            throw new InvalidDataException("Display condition contains an OR without a left operand.");
                        orSegments.Add(currentAndSegment);
                        currentAndSegment = new List<DisplayNode>();
                        break;
                    default:
                        throw new ConditionDisplayNotSupportedException(
                            $"Condition item kind '{item.Kind}' has no human-text representation.");
                }
            }

            if (currentAndSegment.Count == 0)
                throw new InvalidDataException("Display condition contains no final semantic operand.");
            orSegments.Add(currentAndSegment);

            var orNodes = orSegments
                .Select(static segment => MakeJunction(isAnd: true, segment))
                .ToList();
            return MakeJunction(isAnd: false, orNodes);
        }

        /// <summary>
        /// Creates a junction only when multiple children require one, avoiding presentation-only single-child containers.<br/>
        /// Explicit authored groups remain represented by <see cref="DisplayGroupNode"/> and are not flattened here.<br/>
        /// </summary>
        /// <param name="isAnd">Whether the junction requires every child rather than any child.<br/></param>
        /// <param name="children">Ordered child nodes.<br/></param>
        /// <returns>The sole child or a new junction preserving child order.<br/></returns>
        private static DisplayNode MakeJunction(bool isAnd, IReadOnlyList<DisplayNode> children)
        {
            if (children.Count == 0)
                throw new InvalidDataException("Display junction cannot be empty.");
            return children.Count == 1
                ? children[0]
                : new DisplayJunctionNode(isAnd, children);
        }

        /// <summary>
        /// Appends one stable narrative expression with parentheses only where authored grouping or operator precedence requires them.<br/>
        /// </summary>
        /// <param name="builder">Destination text builder.<br/></param>
        /// <param name="node">Display tree node to render.<br/></param>
        /// <param name="parameters">Ordered parameter contract.<br/></param>
        /// <param name="context">Resolved presentation context.<br/></param>
        /// <param name="parentPrecedence">Parent junction precedence used to protect meaning with parentheses.<br/></param>
        private static void RenderNarrativeNode(
            StringBuilder builder,
            DisplayNode node,
            IReadOnlyList<ConditionWireParameter> parameters,
            DisplayRenderContext context,
            int parentPrecedence)
        {
            switch (node)
            {
                case DisplayLeafNode leaf:
                    RenderDisplayLeaf(builder, leaf.Leaf, parameters, context);
                    return;
                case DisplayGroupNode group:
                    builder.Append('(');
                    RenderNarrativeNode(builder, group.Inner, parameters, context, 0);
                    builder.Append(')');
                    return;
                case DisplayJunctionNode junction:
                {
                    int precedence = junction.IsAnd ? 2 : 1;
                    bool parenthesize = precedence < parentPrecedence;
                    if (parenthesize)
                        builder.Append('(');
                    string connector = context.Options.Audience == ConditionTextAudience.Developer
                        ? junction.IsAnd ? " AND " : " OR "
                        : junction.IsAnd ? " and " : " or ";
                    for (var index = 0; index < junction.Children.Count; index++)
                    {
                        if (index > 0)
                            builder.Append(connector);
                        RenderNarrativeNode(
                            builder,
                            junction.Children[index],
                            parameters,
                            context,
                            precedence);
                    }
                    if (parenthesize)
                        builder.Append(')');
                    return;
                }
                default:
                    throw new InvalidDataException($"Unsupported display node '{node.GetType().Name}'.");
            }
        }

        /// <summary>
        /// Appends one indented semantic tree using ALL/ANY headings so nested Boolean intent is immediately visible.<br/>
        /// </summary>
        /// <param name="builder">Destination text builder.<br/></param>
        /// <param name="node">Display tree node to render.<br/></param>
        /// <param name="parameters">Ordered parameter contract.<br/></param>
        /// <param name="context">Resolved presentation context.<br/></param>
        /// <param name="indent">Current space indentation.<br/></param>
        private static void RenderOutlineNode(
            StringBuilder builder,
            DisplayNode node,
            IReadOnlyList<ConditionWireParameter> parameters,
            DisplayRenderContext context,
            int indent)
        {
            switch (node)
            {
                case DisplayLeafNode leaf:
                    builder.Append(' ', indent).Append("- ");
                    RenderDisplayLeaf(builder, leaf.Leaf, parameters, context);
                    builder.AppendLine();
                    return;
                case DisplayGroupNode group:
                    RenderOutlineNode(builder, group.Inner, parameters, context, indent);
                    return;
                case DisplayJunctionNode junction:
                    builder.Append(' ', indent);
                    if (context.Options.Audience == ConditionTextAudience.Developer)
                        builder.Append(junction.IsAnd ? "ALL:" : "ANY:");
                    else
                        builder.Append(junction.IsAnd ? "All of:" : "Any of:");
                    builder.AppendLine();
                    foreach (DisplayNode child in junction.Children)
                        RenderOutlineNode(builder, child, parameters, context, indent + 2);
                    return;
                default:
                    throw new InvalidDataException($"Unsupported display node '{node.GetType().Name}'.");
            }
        }

        /// <summary>
        /// Appends one predicate leaf using audience-appropriate labels, type detail, operators, values, and comparison options.<br/>
        /// </summary>
        /// <param name="builder">Destination text builder.<br/></param>
        /// <param name="leaf">Semantic predicate leaf.<br/></param>
        /// <param name="parameters">Ordered parameter contract.<br/></param>
        /// <param name="context">Resolved presentation context.<br/></param>
        private static void RenderDisplayLeaf(
            StringBuilder builder,
            ConditionWireLeaf leaf,
            IReadOnlyList<ConditionWireParameter> parameters,
            DisplayRenderContext context)
        {
            string sourcePropertyPath = GetSelectorSourcePropertyPath(leaf.Selector);
            bool customLabel;
            string propertyLabel = context.ResolvePropertyLabel(sourcePropertyPath, out customLabel);
            bool sensitive = context.IsSensitive(sourcePropertyPath);
            if (leaf.Selector.Kind == "BinarySlice")
            {
                var isByteArraySlice = string.Equals(
                    leaf.Selector.BinarySliceKind,
                    nameof(BinarySliceKind.ByteArray),
                    StringComparison.Ordinal);
                propertyLabel = context.Options.Audience == ConditionTextAudience.Developer
                    ? isByteArraySlice
                        ? $"{sourcePropertyPath}.Slice({leaf.Selector.Offset.ToString(CultureInfo.InvariantCulture)}, {leaf.Selector.Length.ToString(CultureInfo.InvariantCulture)})"
                        : string.Equals(
                                leaf.Selector.BinarySliceKind,
                                nameof(BinarySliceKind.CustomEncodingString),
                                StringComparison.Ordinal)
                            ? $"{sourcePropertyPath}.BinarySlice({leaf.Selector.BinarySliceKind}, {leaf.Selector.Offset.ToString(CultureInfo.InvariantCulture)}, {leaf.Selector.Length.ToString(CultureInfo.InvariantCulture)}, codePage: {leaf.Selector.EncodingCodePage.ToString(CultureInfo.InvariantCulture)}, coercion: {((Coercion.Text)leaf.Selector.TextCoercion)})"
                            : $"{sourcePropertyPath}.BinarySlice({leaf.Selector.BinarySliceKind}, {leaf.Selector.Offset.ToString(CultureInfo.InvariantCulture)}, {leaf.Selector.Length.ToString(CultureInfo.InvariantCulture)})"
                    : isByteArraySlice
                        ? $"{propertyLabel} bytes starting at {leaf.Selector.Offset.ToString(context.Options.Culture)} for {leaf.Selector.Length.ToString(context.Options.Culture)} bytes"
                        : string.Equals(
                                leaf.Selector.BinarySliceKind,
                                nameof(BinarySliceKind.CustomEncodingString),
                                StringComparison.Ordinal)
                            ? $"encoded text in {propertyLabel} starting at byte {leaf.Selector.Offset.ToString(context.Options.Culture)}"
                            : $"{leaf.Selector.BinarySliceKind} value in {propertyLabel} starting at byte {leaf.Selector.Offset.ToString(context.Options.Culture)}";
                customLabel = true;
            }
            else if (leaf.Selector.Kind == "TimeSpanProjection" &&
                Enum.TryParse(
                    leaf.Selector.TimeSpanProjection,
                    ignoreCase: false,
                    out TimeSpanProjectionKind timeSpanProjection))
            {
                propertyLabel = context.Options.Audience == ConditionTextAudience.Developer
                    ? $"{sourcePropertyPath}.{timeSpanProjection}"
                    : timeSpanProjection switch
                    {
                        TimeSpanProjectionKind.Ticks => $"ticks in {propertyLabel}",
                        TimeSpanProjectionKind.TotalDays => $"total days in {propertyLabel}",
                        TimeSpanProjectionKind.TotalHours => $"total hours in {propertyLabel}",
                        TimeSpanProjectionKind.TotalMinutes => $"total minutes in {propertyLabel}",
                        TimeSpanProjectionKind.TotalSeconds => $"total seconds in {propertyLabel}",
                        TimeSpanProjectionKind.TotalMilliseconds => $"total milliseconds in {propertyLabel}",
                        _ => propertyLabel
                    };
                customLabel = true;
            }
            else if (leaf.Selector.Kind == "NumericArithmetic")
            {
                var selectorText = new StringBuilder();
                if (context.Options.Audience == ConditionTextAudience.Developer)
                {
                    RenderCanonicalSelector(selectorText, leaf.Selector);
                }
                else
                {
                    RenderNaturalNumericSelector(
                        selectorText,
                        leaf.Selector,
                        context,
                        sensitive);
                }
                propertyLabel = selectorText.ToString();
                customLabel = true;
            }
            else if (leaf.Selector.Kind == "TemporalArithmetic")
            {
                var selectorText = new StringBuilder();
                RenderCanonicalSelector(selectorText, leaf.Selector);
                propertyLabel = context.Options.Audience == ConditionTextAudience.Developer
                    ? selectorText.ToString()
                    : $"calculated value {selectorText}";
                customLabel = true;
            }

            if (context.Options.Audience == ConditionTextAudience.EndUser &&
                !sensitive &&
                TryReadLiteralBoolean(leaf, out bool booleanValue) &&
                TryRenderNaturalBoolean(
                    builder,
                    sourcePropertyPath,
                    propertyLabel,
                    customLabel,
                    booleanValue,
                    context.Options.Culture))
            {
                RenderDisplayOptions(builder, leaf, context);
                return;
            }

            builder.Append(propertyLabel);
            if (context.Options.Audience == ConditionTextAudience.Developer)
            {
                builder.Append(" as ");
                builder.Append(GetFriendlyTypeName(ConditionWireTypes.Resolve(leaf.ValueTypeCode)));
            }
            builder.Append(' ');
            builder.Append(FriendlyOperator(leaf.Operator));

            if (leaf.OperandSelector is not null)
            {
                builder.Append(' ');
                string rightPath = GetSelectorSourcePropertyPath(leaf.OperandSelector);
                if (context.Options.Audience == ConditionTextAudience.Developer)
                    RenderCanonicalSelector(builder, leaf.OperandSelector);
                else
                    builder.Append(context.ResolvePropertyLabel(rightPath, out _));
            }

            if (leaf.Operands.Length > 0)
            {
                builder.Append(' ');
                for (var index = 0; index < leaf.Operands.Length; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(
                            leaf.Operator.Contains("Between", StringComparison.Ordinal)
                                ? " and "
                                : ", ");
                    }
                    RenderDisplayValue(
                        builder,
                        leaf,
                        leaf.Operands[index],
                        parameters,
                        context,
                        index,
                        sensitive);
                }
            }
            RenderDisplayOptions(builder, leaf, context);
        }

        /// <summary>
        /// Attempts the conservative built-in natural-language rewrite for Boolean properties named with an <c>Is</c> prefix.<br/>
        /// Custom labels remain authoritative; labels already beginning with “is” are negated without duplicating the verb.<br/>
        /// </summary>
        /// <param name="builder">Destination text builder.<br/></param>
        /// <param name="propertyPath">Normalized source selector path.<br/></param>
        /// <param name="propertyLabel">Resolved display label.<br/></param>
        /// <param name="customLabel">Whether the application supplied the label.<br/></param>
        /// <param name="value">Static Boolean equality value.<br/></param>
        /// <param name="culture">Culture used for the conservative fallback label casing.<br/></param>
        /// <returns><c>true</c> when natural Boolean text was appended; otherwise <c>false</c>.<br/></returns>
        private static bool TryRenderNaturalBoolean(
            StringBuilder builder,
            string propertyPath,
            string propertyLabel,
            bool customLabel,
            bool value,
            CultureInfo culture)
        {
            string rawName = propertyPath.Trim().TrimStart('.', '!', '#', '@');
            int separator = rawName.LastIndexOf('.');
            if (separator >= 0)
                rawName = rawName.Substring(separator + 1);
            if (!rawName.StartsWith("Is", StringComparison.Ordinal) || rawName.Length <= 2)
                return false;

            if (propertyLabel.StartsWith("is ", StringComparison.OrdinalIgnoreCase))
            {
                if (!customLabel)
                {
                    builder.Append(value ? "is " : "is not ");
                    builder.Append(HumanizeIdentifier(rawName.Substring(2), lowerFirst: true, culture));
                }
                else if (value)
                {
                    builder.Append(propertyLabel);
                }
                else
                {
                    builder.Append("is not ").Append(propertyLabel.Substring(3));
                }
                return true;
            }

            builder.Append(value ? "is " : "is not ");
            builder.Append(customLabel ? propertyLabel : HumanizeIdentifier(rawName.Substring(2), lowerFirst: true, culture));
            return true;
        }

        /// <summary>
        /// Reads the sole static Boolean operand only for an equality leaf suitable for natural-language contraction.<br/>
        /// Deferred, null, non-Boolean, and non-equality operands remain in the ordinary rendering path.<br/>
        /// </summary>
        /// <param name="leaf">Semantic leaf to inspect.<br/></param>
        /// <param name="value">Restored Boolean value when successful.<br/></param>
        /// <returns><c>true</c> when the leaf is a static Boolean equality; otherwise <c>false</c>.<br/></returns>
        private static bool TryReadLiteralBoolean(ConditionWireLeaf leaf, out bool value)
        {
            value = false;
            if (leaf.Family != "Boolean" ||
                leaf.Operator is not ("Equal" or "EqualTo") ||
                leaf.Operands.Length != 1 ||
                leaf.Operands[0].IsParameter ||
                leaf.Operands[0].IsNull)
            {
                return false;
            }

            object? restored = InhetoBinary
                .FromStream(new BufferStream(leaf.Operands[0].Payload))
                .ToObject(ConditionWireTypes.Resolve(leaf.Operands[0].TypeCode));
            if (restored is not bool boolean)
                return false;
            value = boolean;
            return true;
        }

        /// <summary>
        /// Appends one parameter or literal operand, enforcing fixed-length sensitive masking before any value restoration or custom callback.<br/>
        /// </summary>
        /// <param name="builder">Destination text builder.<br/></param>
        /// <param name="leaf">Owning predicate leaf.<br/></param>
        /// <param name="value">Wire operand to render.<br/></param>
        /// <param name="parameters">Ordered parameter contract.<br/></param>
        /// <param name="context">Resolved presentation context.<br/></param>
        /// <param name="operandIndex">Zero-based operand position.<br/></param>
        /// <param name="sensitive">Whether the owning property is configured as sensitive.<br/></param>
        private static void RenderDisplayValue(
            StringBuilder builder,
            ConditionWireLeaf leaf,
            ConditionWireValue value,
            IReadOnlyList<ConditionWireParameter> parameters,
            DisplayRenderContext context,
            int operandIndex,
            bool sensitive)
        {
            if (sensitive)
            {
                builder.Append(context.Mask);
                return;
            }

            if (value.IsParameter)
            {
                ConditionWireParameter parameter = parameters[value.ParameterPosition];
                if (context.Options.Audience == ConditionTextAudience.Developer)
                {
                    builder.Append('$');
                    builder.Append(string.IsNullOrWhiteSpace(parameter.Name)
                        ? parameter.Position.ToString(CultureInfo.InvariantCulture)
                        : parameter.Name);
                    builder.Append(':');
                    builder.Append(GetFriendlyTypeName(ConditionWireTypes.Resolve(parameter.TypeCode)));
                }
                else
                {
                    string name = string.IsNullOrWhiteSpace(parameter.Name)
                        ? $"value {parameter.Position + 1}"
                        : HumanizeIdentifier(parameter.Name!, lowerFirst: true, context.Options.Culture);
                    builder.Append('{').Append(name).Append('}');
                }
                return;
            }

            if (value.IsNull)
            {
                builder.Append("null");
                return;
            }

            Type type = ConditionWireTypes.Resolve(value.TypeCode);
            object? restored = InhetoBinary.FromStream(new BufferStream(value.Payload)).ToObject(type);
            string? custom = context.Options.ValueFormatter?.Invoke(
                new ConditionTextValueContext(
                    GetSelectorSourcePropertyPath(leaf.Selector),
                    restored,
                    type,
                    operandIndex,
                    context.Options.Culture,
                    context.Options.Audience));
            if (custom is not null)
            {
                builder.Append(custom);
                return;
            }

            AppendDefaultDisplayValue(builder, restored, type, context.Options);
        }

        /// <summary>
        /// Appends one restored CLR literal using stable quoting and the requested display culture.<br/>
        /// Collections are formatted element-by-element, while byte arrays remain compact hexadecimal values.<br/>
        /// </summary>
        /// <param name="builder">Destination text builder.<br/></param>
        /// <param name="value">Restored CLR value.<br/></param>
        /// <param name="valueType">Declared CLR value type.<br/></param>
        /// <param name="options">Presentation controls for culture and audience.<br/></param>
        private static void AppendDefaultDisplayValue(
            StringBuilder builder,
            object? value,
            Type valueType,
            ConditionTextOptions options)
        {
            if (value is null)
            {
                builder.Append("null");
                return;
            }
            if (value is string text)
            {
                builder.Append('"').Append(text.Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
                return;
            }
            if (value is byte[] bytes)
            {
                builder.Append("0x").Append(Convert.ToHexString(bytes));
                return;
            }
            if (value is IEnumerable enumerable)
            {
                builder.Append('[');
                bool first = true;
                foreach (object? item in enumerable)
                {
                    if (!first)
                        builder.Append(", ");
                    AppendDefaultDisplayValue(
                        builder,
                        item,
                        item?.GetType() ?? typeof(object),
                        options);
                    first = false;
                }
                builder.Append(']');
                return;
            }
            if (value is bool boolean)
            {
                builder.Append(boolean ? "true" : "false");
                return;
            }
            if (value is IFormattable formattable)
            {
                builder.Append(formattable.ToString(null, options.Culture));
                return;
            }
            builder.Append(Convert.ToString(value, options.Culture));
        }

        /// <summary>
        /// Developer output uses named comparison/index details; end-user output mentions only non-default case or culture behavior.<br/>
        /// </summary>
        /// <param name="builder">Destination text builder.<br/></param>
        /// <param name="leaf">Semantic predicate leaf.<br/></param>
        /// <param name="context">Resolved presentation context.<br/></param>
        private static void RenderDisplayOptions(
            StringBuilder builder,
            ConditionWireLeaf leaf,
            DisplayRenderContext context)
        {
            if (leaf.Family == "String" &&
                leaf.BooleanOptions.Length > 0 &&
                leaf.TextOptions.Length > 0 &&
                leaf.IntegerOptions.Length > 0)
            {
                bool ignoreCase = leaf.BooleanOptions[0];
                string culture = leaf.TextOptions[0];
                if (context.Options.Audience == ConditionTextAudience.EndUser)
                {
                    if (!ignoreCase && string.IsNullOrEmpty(culture))
                        return;
                    builder.Append(" (");
                    if (ignoreCase)
                        builder.Append("ignoring case");
                    else
                        builder.Append("case sensitive");
                    if (!string.IsNullOrEmpty(culture))
                        builder.Append(" using ").Append(culture);
                    builder.Append(')');
                    return;
                }

                builder.Append(" [");
                builder.Append(ignoreCase ? "case-insensitive" : "case-sensitive");
                builder.Append(string.IsNullOrEmpty(culture)
                    ? ", invariant culture"
                    : $", culture {culture}");
                builder.Append(", index ");
                builder.Append(leaf.IntegerOptions[0] switch
                {
                    0 => "Default",
                    1 => "CollationKey",
                    2 => "FoldedText",
                    _ => leaf.IntegerOptions[0].ToString(CultureInfo.InvariantCulture)
                });
                builder.Append(']');
                return;
            }

            if (leaf.Family == "PropertyComparison" &&
                context.Options.Audience == ConditionTextAudience.Developer &&
                leaf.BooleanOptions.Length > 0 &&
                leaf.TextOptions.Length > 2 &&
                leaf.IntegerOptions.Length > 0)
            {
                Type leftType = ConditionWireTypes.Resolve(leaf.TextOptions[0]);
                Type rightType = ConditionWireTypes.Resolve(leaf.TextOptions[1]);
                builder.Append(" [left ")
                    .Append(GetFriendlyTypeName(leftType))
                    .Append(", right ")
                    .Append(GetFriendlyTypeName(rightType))
                    .Append(", coercion policy ")
                    .Append(leaf.IntegerOptions[0].ToString(CultureInfo.InvariantCulture));
                if (leftType == typeof(string))
                {
                    builder.Append(leaf.BooleanOptions[0] ? ", case-insensitive" : ", case-sensitive");
                    builder.Append(string.IsNullOrEmpty(leaf.TextOptions[2])
                        ? ", invariant culture"
                        : $", culture {leaf.TextOptions[2]}");
                }
                builder.Append(']');
                return;
            }

            if (context.Options.Audience == ConditionTextAudience.Developer &&
                leaf.Family == "Binary" &&
                leaf.TextOptions.Length > 0 &&
                !string.IsNullOrEmpty(leaf.TextOptions[0]))
            {
                builder.Append(" [parameter alias ").Append(leaf.TextOptions[0]).Append(']');
            }
        }

        /// <summary>
        /// Appends one end-user-oriented numeric selector expression using operation words and configured property labels.<br/>
        /// Nested arithmetic remains grouped with parentheses, while sensitive literal operands are masked before their Inheto payload is restored.<br/>
        /// </summary>
        /// <param name="builder">Destination display builder.<br/></param>
        /// <param name="selector">Validated numeric-arithmetic selector node.<br/></param>
        /// <param name="context">Resolved labels, culture, and masking policy.<br/></param>
        /// <param name="sensitive">Whether the selector source property is configured as sensitive.<br/></param>
        private static void RenderNaturalNumericSelector(
            StringBuilder builder,
            ConditionWireSelector selector,
            DisplayRenderContext context,
            bool sensitive)
        {
            sensitive = context.IsSensitive(GetSelectorSourcePropertyPath(selector));
            if (selector.Kind != "NumericArithmetic" ||
                selector.Source is null ||
                !Enum.TryParse(
                    selector.NumericOperation,
                    ignoreCase: false,
                    out NumericArithmeticOperationKind operation))
            {
                var sourcePath = GetSelectorSourcePropertyPath(selector);
                bool customLabel;
                builder.Append(context.ResolvePropertyLabel(sourcePath, out customLabel));
                if (selector.Kind == "TimeSpanProjection")
                {
                    builder.Append(' ').Append(selector.TimeSpanProjection switch
                    {
                        nameof(TimeSpanProjectionKind.Ticks) => "ticks",
                        nameof(TimeSpanProjectionKind.TotalDays) => "total days",
                        nameof(TimeSpanProjectionKind.TotalHours) => "total hours",
                        nameof(TimeSpanProjectionKind.TotalMinutes) => "total minutes",
                        nameof(TimeSpanProjectionKind.TotalSeconds) => "total seconds",
                        nameof(TimeSpanProjectionKind.TotalMilliseconds) => "total milliseconds",
                        _ => selector.TimeSpanProjection
                    });
                }
                return;
            }

            if (operation == NumericArithmeticOperationKind.Negate)
            {
                builder.Append("negative ");
                RenderNaturalNumericSelector(builder, selector.Source, context, sensitive);
                return;
            }
            if (operation == NumericArithmeticOperationKind.BitNot)
            {
                builder.Append("bitwise complement of ");
                RenderNaturalNumericSelector(builder, selector.Source, context, sensitive);
                return;
            }

            builder.Append('(');
            RenderNaturalNumericSelector(builder, selector.Source, context, sensitive);
            builder.Append(operation switch
            {
                NumericArithmeticOperationKind.Add => " plus ",
                NumericArithmeticOperationKind.Subtract => " minus ",
                NumericArithmeticOperationKind.Multiply => " multiplied by ",
                NumericArithmeticOperationKind.Discount => " discounted by ",
                NumericArithmeticOperationKind.Divide => " divided by ",
                NumericArithmeticOperationKind.Modulo => " modulo ",
                NumericArithmeticOperationKind.BitAnd => " bitwise AND ",
                NumericArithmeticOperationKind.BitOr => " bitwise OR ",
                NumericArithmeticOperationKind.BitXor => " bitwise XOR ",
                NumericArithmeticOperationKind.LeftShift => " shifted left by ",
                NumericArithmeticOperationKind.RightShift => " shifted right by ",
                _ => throw new InvalidDataException($"Unknown numeric arithmetic operation '{operation}'.")
            });
            if (selector.OperandSource is not null)
            {
                RenderNaturalNumericSelector(builder, selector.OperandSource, context, sensitive);
            }
            else if (sensitive)
            {
                builder.Append(context.Mask);
            }
            else
            {
                var resultType = ConditionWireTypes.Resolve(selector.NumericResultTypeCode);
                var literal = InhetoBinary
                    .FromStream(new BufferStream(selector.NumericOperandPayload))
                    .ToObject(resultType);
                builder.Append(Convert.ToString(literal, context.Options.Culture));
            }
            builder.Append(')');
        }

        /// <summary>
        /// Returns one readable CLR type name including generic argument names without assembly-qualified noise.<br/>
        /// </summary>
        /// <param name="type">CLR type to describe.<br/></param>
        /// <returns>Friendly deterministic type name.<br/></returns>
        private static string GetFriendlyTypeName(Type type)
        {
            if (!type.IsGenericType)
                return type.Name;
            string name = type.Name;
            int arity = name.IndexOf('`');
            if (arity >= 0)
                name = name.Substring(0, arity);
            return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(GetFriendlyTypeName))}>";
        }

        /// <summary>
        /// Converts a CLR/property identifier into conservative spaced text without attempting domain-specific grammar.<br/>
        /// </summary>
        /// <param name="identifier">Identifier to humanize.<br/></param>
        /// <param name="lowerFirst">Whether the first output character should be lower-cased.<br/></param>
        /// <param name="culture">Culture used only for casing the first character.<br/></param>
        /// <returns>Human-readable identifier text.<br/></returns>
        private static string HumanizeIdentifier(
            string identifier,
            bool lowerFirst,
            CultureInfo culture)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                return identifier;

            var builder = new StringBuilder(identifier.Length + 8);
            for (var index = 0; index < identifier.Length; index++)
            {
                char current = identifier[index];
                if (current is '_' or '-')
                {
                    if (builder.Length > 0 && builder[^1] != ' ')
                        builder.Append(' ');
                    continue;
                }

                if (index > 0 &&
                    char.IsUpper(current) &&
                    (char.IsLower(identifier[index - 1]) ||
                     (index + 1 < identifier.Length && char.IsLower(identifier[index + 1]))))
                {
                    builder.Append(' ');
                }
                builder.Append(current);
            }

            string result = builder.ToString().Trim();
            if (lowerFirst && result.Length > 0)
                result = char.ToLower(result[0], culture) + result.Substring(1);
            return result;
        }

        private static string FriendlyOperator(string operation)
            => operation switch
            {
                "Equal" or "EqualTo" => "equals",
                "NotEqual" or "NotEqualTo" => "does not equal",
                "Greater" or "GreaterThan" => "is greater than",
                "GreaterOrEqual" => "is at least",
                "Less" or "LessThan" => "is less than",
                "LessOrEqual" => "is at most",
                "In" or "InSet" => "is in",
                "NotIn" or "NotInSet" => "is not in",
                "Between" => "is between",
                "NotBetween" => "is not between",
                "IsNull" => "is null",
                "IsNotNull" => "is not null",
                "IsEmpty" => "is empty",
                "IsNotEmpty" => "is not empty",
                "StartsWith" => "starts with",
                "EndsWith" => "ends with",
                "Contains" => "contains",
                _ => HumanizeIdentifier(operation, lowerFirst: true, CultureInfo.InvariantCulture)
            };

        private abstract class DisplayNode
        {
        }

        private sealed class DisplayLeafNode : DisplayNode
        {
            internal DisplayLeafNode(ConditionWireLeaf leaf)
            {
                Leaf = leaf;
            }

            internal ConditionWireLeaf Leaf { get; }
        }

        private sealed class DisplayGroupNode : DisplayNode
        {
            internal DisplayGroupNode(DisplayNode inner)
            {
                Inner = inner;
            }

            internal DisplayNode Inner { get; }
        }

        private sealed class DisplayJunctionNode : DisplayNode
        {
            internal DisplayJunctionNode(bool isAnd, IReadOnlyList<DisplayNode> children)
            {
                IsAnd = isAnd;
                Children = children;
            }

            internal bool IsAnd { get; }
            internal IReadOnlyList<DisplayNode> Children { get; }
        }

        private sealed class DisplayRenderContext
        {
            private readonly HashSet<string> sensitivePaths;

            internal DisplayRenderContext(Type recordType, ConditionTextOptions options)
            {
                Options = options;
                sensitivePaths = new HashSet<string>(
                    options.SensitivePropertyPaths
                        .Where(static path => !string.IsNullOrWhiteSpace(path))
                        .Select(NormalizeComparisonPath),
                    StringComparer.Ordinal);

                string? suppliedRecordLabel = options.Audience == ConditionTextAudience.EndUser
                    ? options.RecordTypeLabelResolver?.Invoke(recordType)
                    : null;
                string recordLabel = string.IsNullOrWhiteSpace(suppliedRecordLabel)
                    ? GetFriendlyTypeName(recordType)
                    : suppliedRecordLabel.Trim();
                if (options.Audience == ConditionTextAudience.EndUser &&
                    string.IsNullOrWhiteSpace(suppliedRecordLabel))
                {
                    recordLabel = HumanizeIdentifier(recordLabel, lowerFirst: false, options.Culture);
                }

                RecordHeading = options.Audience == ConditionTextAudience.Developer
                    ? $"Condition<{recordLabel}>"
                    : $"{recordLabel} filter";
                Mask = new string(options.SensitiveMaskCharacter, 4);
            }

            internal ConditionTextOptions Options { get; }
            internal string RecordHeading { get; }
            internal string Mask { get; }

            internal bool IsSensitive(string propertyPath)
                => sensitivePaths.Contains(NormalizeComparisonPath(propertyPath));

            internal string ResolvePropertyLabel(string propertyPath, out bool custom)
            {
                if (Options.Audience == ConditionTextAudience.Developer)
                {
                    custom = false;
                    return propertyPath;
                }

                string? supplied = Options.PropertyLabelResolver?.Invoke(propertyPath);
                custom = !string.IsNullOrWhiteSpace(supplied);
                if (custom)
                    return supplied!.Trim();

                string trimmed = propertyPath.Trim().TrimStart('.', '!', '#', '@');
                string[] segments = trimmed.Split(
                    '.',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return string.Join(
                    " → ",
                    segments.Select(segment => HumanizeIdentifier(
                        segment,
                        lowerFirst: true,
                        Options.Culture)));
            }

            private static string NormalizeComparisonPath(string propertyPath)
                => propertyPath.Trim().TrimStart('.');
        }
    }

    /// <summary>
    /// Selects how much implementation-oriented detail a human-readable condition projection contains.<br/>
    /// This affects presentation only and never changes descriptor identity, serialization, rehydration, or execution semantics.<br/>
    /// </summary>
    public enum ConditionTextAudience
    {
        /// <summary>
        /// Renders application-oriented descriptions with reduced implementation detail.<br/>
        /// </summary>
        EndUser,
        /// <summary>
        /// Includes developer-oriented selector and condition details in the rendered description.<br/>
        /// </summary>
        Developer
    }

    /// <summary>
    /// Selects whether a condition is rendered as stable narrative text or as an indented Boolean tree.<br/>
    /// Both layouts are generated from the same validated semantic descriptor and preserve grouping and AND/OR precedence.<br/>
    /// </summary>
    public enum ConditionTextLayout
    {
        /// <summary>
        /// Renders the condition as narrative text while preserving grouping and Boolean precedence.<br/>
        /// </summary>
        Narrative,
        /// <summary>
        /// Renders the condition as an indented Boolean outline.<br/>
        /// </summary>
        Outline
    }

    /// <summary>
    /// Supplies optional presentation controls for descriptor-backed condition text.<br/>
    /// A newly constructed instance produces end-user narrative text with a record-type heading, humanized property paths, current-culture literal formatting, and no assumed sensitive paths.<br/>
    /// </summary>
    public sealed class ConditionTextOptions
    {
        /// <summary>
        /// Gets or sets the intended reader profile.<br/>
        /// End-user output favors natural wording, while developer output retains property paths, CLR value types, and semantic comparison details.<br/>
        /// </summary>
        public ConditionTextAudience Audience { get; set; } = ConditionTextAudience.EndUser;

        /// <summary>
        /// Gets or sets the output layout.<br/>
        /// Narrative output is the stable default; outline output uses indented ALL/ANY group structure.<br/>
        /// </summary>
        public ConditionTextLayout Layout { get; set; } = ConditionTextLayout.Narrative;

        /// <summary>
        /// Gets or sets whether the condition's generic record type is included as the display heading.<br/>
        /// The default is <c>true</c>; the condition does not require or retain a particular logical store instance name.<br/>
        /// </summary>
        public bool IncludeRecordType { get; set; } = true;

        /// <summary>
        /// Gets or sets an optional end-user resolver that replaces the default display label for the generic record type.<br/>
        /// Returning null or whitespace falls back to the CLR type's friendly name; developer output always identifies the actual generic CLR type.<br/>
        /// </summary>
        public Func<Type, string?>? RecordTypeLabelResolver { get; set; }

        /// <summary>
        /// Gets or sets an optional end-user property-path label resolver.<br/>
        /// Returning null or whitespace uses a humanized undotted path; developer output always retains the exact source path.<br/>
        /// </summary>
        public Func<string, string?>? PropertyLabelResolver { get; set; }

        /// <summary>
        /// Gets or sets property paths whose operands must be replaced by a fixed four-character mask.<br/>
        /// Dotted and undotted paths are treated equivalently, comparison is ordinal, and masking occurs before literal deserialization or custom formatting.<br/>
        /// </summary>
        public IEnumerable<string> SensitivePropertyPaths { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the character repeated four times for sensitive values.<br/>
        /// The default bullet avoids leaking the source value's character or collection length.<br/>
        /// </summary>
        public char SensitiveMaskCharacter { get; set; } = '•';

        /// <summary>
        /// Gets or sets the culture used by default literal formatting and identifier casing.<br/>
        /// The default is the current culture captured when this options instance is created.<br/>
        /// </summary>
        public CultureInfo Culture { get; set; } = CultureInfo.CurrentCulture;

        /// <summary>
        /// Gets or sets an optional literal-value formatter.<br/>
        /// Returning null delegates to the built-in formatter; sensitive operands never reach this callback.<br/>
        /// </summary>
        public Func<ConditionTextValueContext, string?>? ValueFormatter { get; set; }
    }

    /// <summary>
    /// Describes one non-sensitive literal being offered to a caller-provided condition text formatter.<br/>
    /// The context is presentation-only and contains the restored CLR value, selector path, operand position, culture, and audience.<br/>
    /// </summary>
    public sealed class ConditionTextValueContext
    {
        internal ConditionTextValueContext(
            string propertyPath,
            object? value,
            Type valueType,
            int operandIndex,
            CultureInfo culture,
            ConditionTextAudience audience)
        {
            PropertyPath = propertyPath;
            Value = value;
            ValueType = valueType;
            OperandIndex = operandIndex;
            Culture = culture;
            Audience = audience;
        }

        /// <summary>
        /// Gets the normalized source selector path associated with the literal.<br/>
        /// </summary>
        public string PropertyPath { get; }

        /// <summary>
        /// Gets the restored non-sensitive CLR literal value.<br/>
        /// </summary>
        public object? Value { get; }

        /// <summary>
        /// Gets the CLR type declared by the semantic wire value.<br/>
        /// </summary>
        public Type ValueType { get; }

        /// <summary>
        /// Gets the zero-based operand position within the condition leaf.<br/>
        /// </summary>
        public int OperandIndex { get; }

        /// <summary>
        /// Gets the culture selected for this rendering operation.<br/>
        /// </summary>
        public CultureInfo Culture { get; }

        /// <summary>
        /// Gets the audience profile selected for this rendering operation.<br/>
        /// </summary>
        public ConditionTextAudience Audience { get; }
    }

    internal sealed class ConditionDisplayNotSupportedException : NotSupportedException
    {
        internal ConditionDisplayNotSupportedException(string message)
            : base(message)
        {
        }
    }

}
