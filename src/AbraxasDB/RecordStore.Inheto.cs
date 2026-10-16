namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Gets read-time property-path equivalence groups shared by object projections, direct property reads, conditions, and selectors.<br/>
        /// Exact requested members win over the canonical path and remaining aliases; serialization continues to use the runtime object's actual shape.<br/>
        /// Conditions touching an alias group intentionally use authoritative payload evaluation until a future multi-source index plan can cover every equivalent stored path.<br/>
        /// </summary>
        public RecordPropertyAliases Aliases => DeserializationOptions.Aliases;

        /// <summary>
        /// Checks and canonicalizes a textual property path against this store's declared record type without invoking developer code.<br/>
        /// The returned readonly value retains the existing cached immutable plan without allocating another wrapper.<br/>
        /// This checks declared CLR members, not record presence, runtime dictionary keys or collection positions; aliases do not add CLR members.<br/>
        /// </summary>
        /// <param name="propPath">Property, field, or admitted parameterless-method path whose leading dot is optional.<br/></param>
        /// <param name="expectedResultType">Optional result type required from the final path segment.<br/></param>
        /// <returns>The cached structural property-path check.<br/></returns>
        public RecordPropertyPathCheck CheckPropPath(string propPath, Type? expectedResultType = null)
            => new(Inheto.InhetoBinary.CheckPropPath(typeof(T), propPath, expectedResultType));

        /// <summary>
        /// Checks and canonicalizes a textual property path against this store's declared record type and requires a result compatible with <typeparamref name="TValue"/>.<br/>
        /// No property getter or method is invoked while the reusable plan is prepared.<br/>
        /// This structural check does not prove that a particular record contains the value.<br/>
        /// </summary>
        /// <typeparam name="TValue">Required final value type.<br/></typeparam>
        /// <param name="propPath">Property, field, or admitted parameterless-method path whose leading dot is optional.<br/></param>
        /// <returns>The cached structural property-path check.<br/></returns>
        public RecordPropertyPathCheck CheckPropPath<TValue>(string propPath)
            => new(Inheto.InhetoBinary.CheckPropPath<T, TValue>(propPath));

        /// <summary>
        /// Probes one runtime record with this store's active serialization policy without persisting it.<br/>
        /// The bounded report identifies throwing getters or enumerators, unsafe dictionary keys, unsupported values, unstable recursive expansion, and applicable configuration suggestions.<br/>
        /// Getters and enumerators may execute; registered custom serializers and getter stability checks run by default unless disabled in the probe options.<br/>
        /// Their side effects are not rolled back. Suggestions are guidance only, and a clean report does not certify unvisited or later-mutated values.<br/>
        /// </summary>
        /// <param name="record">Runtime record instance to inspect.<br/></param>
        /// <param name="options">Optional bounds and custom-serializer validation choices.<br/></param>
        /// <returns>A disconnected, read-only Abraxas compatibility report, retaining captured application exceptions without wrapping them.<br/></returns>
        public RecordTypeReport TestType(
            T record,
            RecordTypeProbeOptions? options = null)
        {
            var serializer = new Inheto.InhetoSerializer(NativeSerializationOptions);
            return new RecordTypeReport(serializer.TestType(record, options?.ToNative()));
        }
    }

    /// <summary>Identifies the outcome of a nonexecuting declared-CLR member-path check.<br/></summary>
    public enum RecordPropertyPathCheckKind
    {
        /// <summary>No valid structural plan was prepared.<br/></summary>
        Invalid = 0,
        /// <summary>The declared path and requested result type resolve without invoking application code.<br/></summary>
        Resolvable = 1
    }

    /// <summary>Identifies why a declared CLR member path could not be prepared.<br/></summary>
    public enum RecordPropertyPathFailureKind
    {
        /// <summary>No structural failure is recorded; a default check is nevertheless invalid.<br/></summary>
        None = 0,
        /// <summary>The request contains no admitted member segment.<br/></summary>
        EmptyPath = 1,
        /// <summary>A required property, field or admitted method could not be resolved.<br/></summary>
        MissingMember = 2,
        /// <summary>A parameterless method is outside the existing permitted-return-type policy.<br/></summary>
        BlockedMethod = 3,
        /// <summary>The final declared value type is incompatible with the requested result type.<br/></summary>
        ResultTypeMismatch = 4
    }

    /// <summary>
    /// Describes a nonexecuting declared-CLR member-path check while retaining its shared immutable native plan internally.<br/>
    /// Copying this readonly value does not allocate a wrapper or retain a record, reader or store; it can be cached and shared.<br/>
    /// A default value is invalid, has null type metadata and empty paths. Inspect IsValid before using a result.<br/>
    /// Runtime stored-key/element navigation, alias resolution and record-specific missing/null states are separate from this structural check.<br/>
    /// </summary>
    public readonly struct RecordPropertyPathCheck
    {
        /// <summary>Gets the shared component plan for internal execution; null denotes a default unprepared value.<br/></summary>
        internal Inheto.PropPathCheck? NativeCheck { get; }

        /// <summary>Retains an existing cached plan without copying metadata or preparing the path again.<br/></summary>
        /// <param name="check">Required immutable native plan returned by the existing structural cache.<br/></param>
        internal RecordPropertyPathCheck(Inheto.PropPathCheck check) => NativeCheck = check;

        /// <summary>Gets the declared root type, or null for a default unprepared value.<br/></summary>
        public Type? RootType => NativeCheck?.RootType;
        /// <summary>Gets the optional result type requested by the caller.<br/></summary>
        public Type? ExpectedResultType => NativeCheck?.ExpectedResultType;
        /// <summary>Gets the original requested spelling, or an empty string for a default value.<br/></summary>
        public string RequestedPath => NativeCheck?.RequestedPath ?? string.Empty;
        /// <summary>Gets the canonical member path, with admitted method segments carrying parentheses.<br/></summary>
        public string CanonicalPath => NativeCheck?.CanonicalPath ?? string.Empty;
        /// <summary>Gets the statically identified serialized prefix before a CLR continuation, if one is required.<br/></summary>
        public string SerializedPrefixCandidate => NativeCheck?.SerializedPrefixCandidate ?? string.Empty;
        /// <summary>Gets the CLR continuation following the statically identified serialized prefix.<br/></summary>
        public string ClrRemainderCandidate => NativeCheck?.ClrRemainderCandidate ?? string.Empty;
        /// <summary>Gets the final resolved CLR type, or the native partial/failure result when applicable.<br/></summary>
        public Type? ResultType => NativeCheck?.ResultType;
        /// <summary>Gets the explicitly mapped structural outcome; a default value is Invalid.<br/></summary>
        public RecordPropertyPathCheckKind Kind => NativeCheck?.Kind switch
        {
            null or Inheto.PropPathCheckKind.Invalid => RecordPropertyPathCheckKind.Invalid,
            Inheto.PropPathCheckKind.Resolvable => RecordPropertyPathCheckKind.Resolvable,
            _ => throw new InvalidOperationException("Unknown structural path-check outcome.")
        };
        /// <summary>Gets the explicitly mapped failure classification; a default value records None.<br/></summary>
        public RecordPropertyPathFailureKind FailureKind => NativeCheck?.FailureKind switch
        {
            null or Inheto.PropPathFailureKind.None => RecordPropertyPathFailureKind.None,
            Inheto.PropPathFailureKind.EmptyPath => RecordPropertyPathFailureKind.EmptyPath,
            Inheto.PropPathFailureKind.MissingMember => RecordPropertyPathFailureKind.MissingMember,
            Inheto.PropPathFailureKind.BlockedMethod => RecordPropertyPathFailureKind.BlockedMethod,
            Inheto.PropPathFailureKind.ResultTypeMismatch => RecordPropertyPathFailureKind.ResultTypeMismatch,
            _ => throw new InvalidOperationException("Unknown structural path-check failure.")
        };
        /// <summary>Gets the first unresolved or blocked segment, if available.<br/></summary>
        public string? FailedSegment => NativeCheck?.FailedSegment;
        /// <summary>Gets whether the prepared path includes an admitted parameterless CLR method.<br/></summary>
        public bool ContainsMethodInvocation => NativeCheck?.ContainsMethodInvocation ?? false;
        /// <summary>Gets whether execution requires CLR continuation after the serialized-value boundary.<br/></summary>
        public bool RequiresClrContinuation => NativeCheck?.RequiresClrContinuation ?? false;
        /// <summary>Gets whether the complete declared path and requested result type are structurally valid.<br/></summary>
        public bool IsValid => NativeCheck?.IsValid ?? false;
    }

    /// <summary>
    /// Configures one explicit runtime-record compatibility probe, independently of persistence settings.<br/>
    /// These immutable-after-initialization choices retain the existing native bounds/edge behavior and are mapped once per requested probe.<br/>
    /// Limits bound inspection rather than elapsed callback time; application code must still return from invoked getters, enumerators and hooks.<br/>
    /// </summary>
    public sealed record RecordTypeProbeOptions
    {
        /// <summary>Gets or initializes maximum accepted traversal depth, with the root at zero; defaults to 64.<br/></summary>
        public int MaxDepth { get; init; } = 64;
        /// <summary>Gets or initializes the node limit checked before probing a value; defaults to 100,000.<br/></summary>
        public int MaxNodes { get; init; } = 100_000;
        /// <summary>Gets or initializes inspected slots per array/enumerable/dictionary, including null slots; defaults to 256.<br/></summary>
        public int MaxEnumerableItems { get; init; } = 256;
        /// <summary>Gets or initializes whether registered custom serializers are invoked to check their results/errors; defaults to true.<br/></summary>
        public bool InvokeCustomSerializers { get; init; } = true;
        /// <summary>Gets or initializes whether selected getters may be called twice to detect fresh-reference expansion; defaults to true.<br/></summary>
        public bool ProbeGetterStability { get; init; } = true;

        /// <summary>Maps explicit probe choices once without changing the store's serialization policy.<br/></summary>
        /// <returns>Independent native probe options with exactly the supplied values.<br/></returns>
        internal Inheto.InhetoSerializer.TestTypeProbeOptions ToNative() => new()
        {
            MaxDepth = MaxDepth,
            MaxNodes = MaxNodes,
            MaxEnumerableItems = MaxEnumerableItems,
            InvokeCustomSerializers = InvokeCustomSerializers,
            ProbeGetterStability = ProbeGetterStability
        };
    }

    /// <summary>Classifies a compatibility observation or traversal limit from one explicit record probe.<br/></summary>
    public enum RecordTypeIssueKind : byte
    {
        /// <summary>The root was null, so no graph was traversed.<br/></summary>
        NullRoot,
        /// <summary>A branch exceeded the configured traversal depth.<br/></summary>
        DepthLimitReached,
        /// <summary>The probe reached its configured node limit.<br/></summary>
        NodeLimitReached,
        /// <summary>A getter failed or its member accessor was unsupported.<br/></summary>
        GetterThrows,
        /// <summary>Array/enumerable/dictionary traversal failed.<br/></summary>
        EnumerationThrows,
        /// <summary>An invoked custom serializer failed.<br/></summary>
        CustomSerializerThrows,
        /// <summary>A custom serializer returned an unsupported payload kind; member hooks permit text as well as bytes/null.<br/></summary>
        CustomSerializerReturnedNonBytes,
        /// <summary>An unrecognized value type has no public instance members through which its value can be preserved.<br/></summary>
        OpaqueValueType,
        /// <summary>A dictionary key cannot use the ordinary key-path representation.<br/></summary>
        UnsafeDictionaryKey,
        /// <summary>Fresh-reference getter/traversal patterns may expand without bound.<br/></summary>
        PotentialInfiniteExpansion
    }

    /// <summary>Owns one compatibility observation without exposing a component diagnostic type.<br/></summary>
    /// <param name="Kind">Observation or traversal-limit classification.<br/></param>
    /// <param name="MemberPath">Full affected path; empty denotes the root.<br/></param>
    /// <param name="DeclaringOrOwningType">Application type declaring/owning the affected value.<br/></param>
    /// <param name="ValueType">Observed or declared value type, if available.<br/></param>
    /// <param name="Message">Unmodified diagnostic explanation.<br/></param>
    /// <param name="Exception">Original captured application exception, if any; no wrapping or cloning.<br/></param>
    public readonly record struct RecordTypeIssue(RecordTypeIssueKind Kind, string MemberPath,
        Type DeclaringOrOwningType, Type? ValueType, string Message, Exception? Exception = null);

    /// <summary>Classifies optional configuration guidance; the probe never applies it automatically.<br/></summary>
    public enum RecordTypeSuggestionKind : byte
    {
        /// <summary>Consider excluding the indicated member.<br/></summary>
        ExcludeMember,
        /// <summary>Consider excluding the indicated non-root type.<br/></summary>
        ExcludeType,
        /// <summary>Consider explicit circular-member handling for the indicated type/token.<br/></summary>
        AddCircularProperty,
        /// <summary>Consider a member-specific custom serializer.<br/></summary>
        AddMemberSerializer,
        /// <summary>Consider a type-wide custom serializer.<br/></summary>
        AddTypeSerializer,
        /// <summary>Use exact member-token spelling for configured paths.<br/></summary>
        NormalizeMemberNameToken
    }

    /// <summary>Owns optional guidance without modifying application configuration.<br/></summary>
    /// <param name="Kind">Suggested action classification.<br/></param>
    /// <param name="Target">Affected member path, type name or configuration target.<br/></param>
    /// <param name="Message">Unmodified explanatory guidance.<br/></param>
    public readonly record struct RecordTypeSuggestion(RecordTypeSuggestionKind Kind, string Target, string Message);

    /// <summary>
    /// Holds a disconnected, read-only snapshot of one explicit compatibility probe.<br/>
    /// Collections cannot be mutated through a list/array cast. BCL type/string references and original exceptions are retained, not cloned.<br/>
    /// Absence of issues concerns only inspected values under this policy and these limits, not every possible later object graph.<br/>
    /// </summary>
    public sealed class RecordTypeReport
    {
        /// <summary>Gets the declared store record type used for the probe.<br/></summary>
        public Type RootType { get; }
        /// <summary>Gets the native traversal's recorded visited-value count.<br/></summary>
        public int NodesVisited { get; }
        /// <summary>Gets ordered compatibility observations and traversal-limit results.<br/></summary>
        public IReadOnlyList<RecordTypeIssue> Issues { get; }
        /// <summary>Gets deduplicated guidance; no suggestion is applied by constructing or reading this report.<br/></summary>
        public IReadOnlyList<RecordTypeSuggestion> Suggestions { get; }

        /// <summary>Copies bounded diagnostic value records once after native probing, without retaining a store or native report.<br/></summary>
        /// <param name="report">Completed native report whose issue/suggestion order and context must be preserved.<br/></param>
        internal RecordTypeReport(Inheto.InhetoSerializer.TestTypeReport report)
        {
            RootType = report.RootType;
            NodesVisited = report.NodesVisited;
            if (report.Issues.Count == 0) Issues = Array.Empty<RecordTypeIssue>();
            else
            {
                var issues = new RecordTypeIssue[report.Issues.Count];
                for (int i = 0; i < issues.Length; i++)
                {
                    var issue = report.Issues[i];
                    issues[i] = new RecordTypeIssue(Map(issue.Kind), issue.MemberPath, issue.DeclaringOrOwningType,
                        issue.ValueType, issue.Message, issue.Exception);
                }
                Issues = Array.AsReadOnly(issues);
            }
            if (report.Suggestions.Count == 0) Suggestions = Array.Empty<RecordTypeSuggestion>();
            else
            {
                var suggestions = new RecordTypeSuggestion[report.Suggestions.Count];
                for (int i = 0; i < suggestions.Length; i++)
                {
                    var suggestion = report.Suggestions[i];
                    suggestions[i] = new RecordTypeSuggestion(Map(suggestion.Kind), suggestion.Target, suggestion.Message);
                }
                Suggestions = Array.AsReadOnly(suggestions);
            }
        }

        /// <summary>Maps a native observation by name so enum ordinals cannot silently couple public contracts.<br/></summary>
        /// <param name="kind">Native observation classification.<br/></param>
        /// <returns>Owned equivalent; unknown internal values fail rather than being misclassified.<br/></returns>
        private static RecordTypeIssueKind Map(Inheto.InhetoSerializer.TestTypeIssueKind kind) => kind switch
        {
            Inheto.InhetoSerializer.TestTypeIssueKind.NullRoot => RecordTypeIssueKind.NullRoot,
            Inheto.InhetoSerializer.TestTypeIssueKind.DepthLimitReached => RecordTypeIssueKind.DepthLimitReached,
            Inheto.InhetoSerializer.TestTypeIssueKind.NodeLimitReached => RecordTypeIssueKind.NodeLimitReached,
            Inheto.InhetoSerializer.TestTypeIssueKind.GetterThrows => RecordTypeIssueKind.GetterThrows,
            Inheto.InhetoSerializer.TestTypeIssueKind.EnumerationThrows => RecordTypeIssueKind.EnumerationThrows,
            Inheto.InhetoSerializer.TestTypeIssueKind.CustomSerializerThrows => RecordTypeIssueKind.CustomSerializerThrows,
            Inheto.InhetoSerializer.TestTypeIssueKind.CustomSerializerReturnedNonBytes => RecordTypeIssueKind.CustomSerializerReturnedNonBytes,
            Inheto.InhetoSerializer.TestTypeIssueKind.OpaqueValueType => RecordTypeIssueKind.OpaqueValueType,
            Inheto.InhetoSerializer.TestTypeIssueKind.UnsafeDictionaryKey => RecordTypeIssueKind.UnsafeDictionaryKey,
            Inheto.InhetoSerializer.TestTypeIssueKind.PotentialInfiniteExpansion => RecordTypeIssueKind.PotentialInfiniteExpansion,
            _ => throw new InvalidOperationException("Unknown record-probe observation.")
        };

        /// <summary>Maps native guidance by name without rewriting messages or applying configuration.<br/></summary>
        /// <param name="kind">Native suggested action.<br/></param>
        /// <returns>Owned equivalent; unknown internal values fail rather than being misclassified.<br/></returns>
        private static RecordTypeSuggestionKind Map(Inheto.InhetoSerializer.TestTypeSuggestionKind kind) => kind switch
        {
            Inheto.InhetoSerializer.TestTypeSuggestionKind.ExcludeMember => RecordTypeSuggestionKind.ExcludeMember,
            Inheto.InhetoSerializer.TestTypeSuggestionKind.ExcludeType => RecordTypeSuggestionKind.ExcludeType,
            Inheto.InhetoSerializer.TestTypeSuggestionKind.AddCircularProperty => RecordTypeSuggestionKind.AddCircularProperty,
            Inheto.InhetoSerializer.TestTypeSuggestionKind.AddMemberSerializer => RecordTypeSuggestionKind.AddMemberSerializer,
            Inheto.InhetoSerializer.TestTypeSuggestionKind.AddTypeSerializer => RecordTypeSuggestionKind.AddTypeSerializer,
            Inheto.InhetoSerializer.TestTypeSuggestionKind.NormalizeMemberNameToken => RecordTypeSuggestionKind.NormalizeMemberNameToken,
            _ => throw new InvalidOperationException("Unknown record-probe suggestion.")
        };
    }
}
