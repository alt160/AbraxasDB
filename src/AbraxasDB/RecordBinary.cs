namespace AbraxasDB;

/// <summary>
/// Provides decoded record bytes and selective hydration through Abraxas-owned contracts.<br/>
/// This readonly value adds no heap wrapper; copies share the native cursor/caches and are not independently thread-safe.<br/>
/// GetBinary results own independent payload bytes. Reader results and all their slices/shallow clones are borrowed until reader movement or disposal.<br/>
/// Call Detach while the source is valid to retain independent bytes. Policies/callbacks remain shared; linked callbacks may still require their owning store.<br/>
/// </summary>
public readonly struct RecordBinary
{
    private readonly Inheto.InhetoBinary? binary;

    /// <summary>Wraps the existing reader without copying bytes or constructing another native reader.<br/></summary>
    /// <param name="binary">Existing decoded payload reader.<br/></param>
    /// <param name="isBorrowed">Whether bytes are valid only during the supplying reader's current row.<br/></param>
    internal RecordBinary(Inheto.InhetoBinary binary, bool isBorrowed = false)
    {
        this.binary = binary;
        IsBorrowed = isBorrowed;
    }

    /// <summary>Gets the retained native reader for internal dispatch, rejecting an uninitialized value.<br/></summary>
    private Inheto.InhetoBinary Native => binary ?? throw new InvalidOperationException("The record binary is uninitialized.");
    /// <summary>Gets whether a reader was supplied; this does not validate an expired borrowed lifetime.<br/></summary>
    public bool IsValid => binary is not null;
    /// <summary>Gets whether this value requires the supplying reader's current-row lifetime; default values are invalid, not borrowed.<br/></summary>
    public bool IsBorrowed { get; }
    /// <summary>Gets complete decoded bytes without copying; borrowed values expire on reader movement/disposal.<br/></summary>
    public ReadOnlyMemory<byte> RawMemory => Native.RawMemory;
    /// <summary>Gets complete decoded bytes without allocating a segment cursor; source lifetime still applies.<br/></summary>
    public ReadOnlySpan<byte> RawSpan => Native.RawSpan;
    /// <summary>Gets an independent list of stored names, preserving native naming and list-allocation behavior.<br/></summary>
    public List<string> AllNames => Native.AllNames;

    /// <summary>Explicitly copies complete decoded bytes into a caller-owned array.<br/></summary>
    /// <returns>Independent bytes valid after source movement/disposal.<br/></returns>
    public byte[] CopyPayload() => Native.RawMemory.ToArray();

    /// <summary>
    /// Explicitly copies this payload into an independent reader with the same live policy and process-local materialization context.<br/>
    /// This copies bytes even for an already-owned value; it does not freeze configuration or clone callback/store dependencies.<br/>
    /// Call before a borrowed source expires. Ordinary retrieval/access never calls this method automatically.<br/>
    /// </summary>
    /// <returns>Nonborrowed binary with independent bytes and cursor/caches.<br/></returns>
    public RecordBinary Detach()
    {
        var source = Native;
        var detached = Inheto.InhetoBinary.FromStream(new BufferStream(source.RawMemory.ToArray()), source.deserializationOptions);
        if (source.TryGetMaterializationContext<object>(out var context)) detached.SetMaterializationContext(context);
        return new(detached);
    }

    /// <summary>
    /// Creates independent cursor/materialization caches over the same bytes and live policy.<br/>
    /// No payload copy occurs. A clone of a borrowed row still expires when that row moves or its reader is disposed.<br/>
    /// </summary>
    /// <returns>Shallow reader clone retaining the source borrowing contract.<br/></returns>
    public RecordBinary ShallowClone() => new(Native.ShallowClone(), IsBorrowed);

    /// <summary>Tests for an encoded entry, including explicit null, honoring supplied aliases under native precedence rules.<br/></summary>
    /// <param name="propPath">Full progressive stored path; empty selects the root where supported.<br/></param>
    /// <param name="options">Optional per-call read policy; omission preserves native default behavior.<br/></param>
    /// <returns>Whether the stored entry exists; CLR continuation is not evaluated.<br/></returns>
    public bool PropExists(string propPath, RecordDeserializationOptions? options = null)
        => Native.PropExists(propPath, options?.NativeOptions);

    /// <summary>Reads one value or subtree as the requested shape in an independent native materialization operation.<br/></summary>
    /// <typeparam name="TValue">Requested value/destination shape.<br/></typeparam>
    /// <param name="propPath">Full progressive path; empty selects the root.<br/></param>
    /// <param name="options">Optional policy; omission retains this reader's policy.<br/></param>
    /// <returns>Native conversion result, including null/default for missing values where supported; callback/conversion errors propagate.<br/></returns>
    public TValue? ReadPropAs<TValue>(string propPath, RecordDeserializationOptions? options = null)
        => Native.ReadPropAs<TValue>(propPath, options?.NativeOptions);

    /// <summary>Reads a runtime-selected value/subtree without reconstructing unrelated members.<br/></summary>
    /// <param name="returnType">Requested CLR destination type.<br/></param><param name="propPath">Full progressive path.<br/></param>
    /// <param name="options">Optional read policy.<br/></param><returns>Requested value or native null/missing result.<br/></returns>
    public object? ReadPropAs(Type returnType, string propPath, RecordDeserializationOptions? options = null)
        => Native.ReadPropAs(returnType, propPath, options?.NativeOptions);

    /// <summary>Reads a non-null value only when its runtime type exactly matches TValue.<br/></summary>
    /// <typeparam name="TValue">Exact required type.<br/></typeparam><param name="propPath">Full progressive path.<br/></param>
    /// <param name="options">Optional read policy.<br/></param><returns>Exact value, otherwise default under native semantics.<br/></returns>
    public TValue? ReadPropExact<TValue>(string propPath, RecordDeserializationOptions? options = null)
        => Native.ReadPropExact<TValue>(propPath, options?.NativeOptions);

    /// <summary>Fills an existing destination from a stored subtree, preserving native identity/omission/hook behavior.<br/></summary>
    /// <typeparam name="TValue">Destination type.<br/></typeparam><param name="instance">Existing application destination.<br/></param>
    /// <param name="propPath">Full source path.<br/></param><param name="options">Optional read policy.<br/></param>
    public void ReadPropOnto<TValue>(TValue instance, string propPath, RecordDeserializationOptions? options = null)
        => Native.ReadPropOnto(instance, propPath, options?.NativeOptions);

    /// <summary>Fills an existing destination through runtime-selected native dispatch.<br/></summary>
    /// <param name="instanceType">Requested destination type.<br/></param><param name="instance">Existing application destination.<br/></param>
    /// <param name="propPath">Full source path.<br/></param><param name="options">Optional read policy.<br/></param>
    public void ReadPropOnto(Type instanceType, object instance, string propPath, RecordDeserializationOptions? options = null)
        => Native.ReadPropOnto(instanceType, instance, propPath, options?.NativeOptions);

    /// <summary>Hydrates the complete root into an application-selected shape with ordinary sharing/cycle and hook semantics.<br/></summary>
    /// <typeparam name="TValue">Requested root type, not necessarily the original persisted type.<br/></typeparam>
    /// <param name="options">Optional policy; omission retains this reader's policy.<br/></param><returns>Requested root result.<br/></returns>
    public TValue? ToObject<TValue>(RecordDeserializationOptions? options = null) => Native.ToObject<TValue>(options?.NativeOptions);

    /// <summary>Hydrates the complete root as a runtime-selected shape using native dispatch.<br/></summary>
    /// <param name="objectType">Requested root type.<br/></param><param name="options">Optional read policy.<br/></param>
    /// <returns>Requested root result, or native null/suppression result.<br/></returns>
    public object? ToObject(Type objectType, RecordDeserializationOptions? options = null) => Native.ToObject(objectType, options?.NativeOptions);

    /// <summary>Reads a typed collection, optionally filling the supplied collection under existing native collection semantics.<br/></summary>
    /// <typeparam name="TValue">Requested element shape.<br/></typeparam><param name="propPath">Full collection path.<br/></param>
    /// <param name="ontoThisInstance">Optional existing destination collection.<br/></param><param name="options">Optional read policy.<br/></param>
    /// <returns>Native collection result; any borrowed/lazy views still require the source lifetime.<br/></returns>
    public IEnumerable<TValue>? ReadEnumerable<TValue>(string propPath, IEnumerable<TValue>? ontoThisInstance = null, RecordDeserializationOptions? options = null)
        => Native.ReadEnumerable(propPath, ontoThisInstance, options?.NativeOptions);

    /// <summary>Reads a runtime collection, optionally filling a supplied destination without changing native collection semantics.<br/></summary>
    /// <param name="propPath">Full collection path.<br/></param><param name="ontoThisInstance">Optional existing collection.<br/></param>
    /// <param name="options">Optional read policy.<br/></param><returns>Native collection result, subject to source lifetime for borrowed/lazy views.<br/></returns>
    public System.Collections.IEnumerable? ReadEnumerable(string propPath, System.Collections.IEnumerable? ontoThisInstance = null, RecordDeserializationOptions? options = null)
        => Native.ReadEnumerable(propPath, ontoThisInstance, options?.NativeOptions);

    /// <summary>Exposes exact custom-property/type bytes without copying or decoding them.<br/></summary>
    /// <param name="propPath">Full custom-value path.<br/></param><param name="payload">Borrowed slice with this binary's lifetime.<br/></param>
    /// <returns>Whether a custom binary payload exists; native errors propagate.<br/></returns>
    public bool TryReadCustomBinaryPayload(string propPath, out ReadOnlyMemory<byte> payload)
        => Native.TryReadCustomBinaryPayload(propPath, out payload);

    /// <summary>Exposes natural UTF-8 string bytes without decoding, framing bytes or a payload copy.<br/></summary>
    /// <param name="propPath">Full string path.<br/></param><param name="payload">Borrowed bytes; empty strings are successful empty slices.<br/></param>
    /// <returns>False for missing/null; wrong-type or malformed-data errors retain native behavior.<br/></returns>
    public bool TryReadStringUtf8Payload(string propPath, out ReadOnlyMemory<byte> payload)
        => Native.TryReadStringUtf8Payload(propPath, out payload);

    /// <summary>Exposes a string's UTF-8 bytes while honoring supplied read-time aliases without copying or decoding.<br/></summary>
    /// <param name="propPath">Full logical string path.<br/></param><param name="options">Optional alias policy with native precedence.<br/></param>
    /// <param name="payload">Borrowed bytes following this binary's lifetime.<br/></param><returns>Whether a non-null string exists.<br/></returns>
    public bool TryReadStringUtf8Payload(string propPath, RecordDeserializationOptions? options, out ReadOnlyMemory<byte> payload)
        => Native.TryReadStringUtf8Payload(propPath, options?.NativeOptions, out payload);

    /// <summary>Checks a prepared CLR path against stored bytes without executing its CLR continuation.<br/></summary>
    /// <param name="path">Owned cached structural check; default/unprepared yields Invalid.<br/></param>
    /// <returns>Payload-specific match metadata; this native check does not apply per-call alias options.<br/></returns>
    public RecordPropertyPathMatch CheckPropPath(RecordPropertyPathCheck path)
    {
        var source = Native;
        return path.NativeCheck is { } check ? new(source.CheckPropPath(check), path)
            : new(path, RecordPropertyPathMatchKind.Invalid, string.Empty, string.Empty);
    }

    /// <summary>Executes a cached structural plan with native missing/null/continuation/error semantics.<br/></summary>
    /// <typeparam name="TValue">Requested result type.<br/></typeparam><param name="path">Owned structural plan; default yields Invalid.<br/></param>
    /// <param name="value">Resolved result or default for nonvalue states.<br/></param><param name="options">Optional native-equivalent read policy.<br/></param>
    /// <param name="throwOnInvocationError">True propagates invocation/materialization/conversion errors rather than returning InvocationFailed.<br/></param>
    /// <returns>Explicitly mapped native read state without reparsing/preparing the supplied plan.<br/></returns>
    public RecordPropertyPathReadState TryReadPropPath<TValue>(RecordPropertyPathCheck path, out TValue? value,
        RecordDeserializationOptions? options = null, bool throwOnInvocationError = false)
    {
        var source = Native;
        if (path.NativeCheck is not { } check) { value = default; return RecordPropertyPathReadState.Invalid; }
        return source.TryReadPropPath(check, out value, options?.NativeOptions, throwOnInvocationError) switch
        {
            Inheto.PropPathReadState.Invalid => RecordPropertyPathReadState.Invalid,
            Inheto.PropPathReadState.Value => RecordPropertyPathReadState.Value,
            Inheto.PropPathReadState.Null => RecordPropertyPathReadState.Null,
            Inheto.PropPathReadState.SourceMissing => RecordPropertyPathReadState.SourceMissing,
            Inheto.PropPathReadState.SourceNull => RecordPropertyPathReadState.SourceNull,
            Inheto.PropPathReadState.InvocationFailed => RecordPropertyPathReadState.InvocationFailed,
            _ => throw new InvalidOperationException("Unknown record property-path read state.")
        };
    }
}

/// <summary>Classifies a prepared CLR path's relationship to one stored record payload.<br/></summary>
public enum RecordPropertyPathMatchKind
{
    /// <summary>No valid structural plan was supplied.<br/></summary>
    Invalid,
    /// <summary>The complete encoded value exists.<br/></summary>
    ExactSerializedValue,
    /// <summary>A stored prefix exists and the remaining CLR suffix must execute on its hydrated value.<br/></summary>
    SerializedPrefixWithClrContinuation,
    /// <summary>The required stored source is absent.<br/></summary>
    SerializedSourceMissing,
    /// <summary>The required stored source is explicitly null.<br/></summary>
    SerializedSourceNull
}

/// <summary>Holds copied path-match metadata without retaining a current-row binary or native name entry.<br/></summary>
public readonly struct RecordPropertyPathMatch
{
    /// <summary>Maps native match metadata by name, preserving the caller's cached owned path.<br/></summary>
    /// <param name="match">Native payload check.<br/></param><param name="path">Original owned structural plan.<br/></param>
    internal RecordPropertyPathMatch(Inheto.PropPathBlobCheck match, RecordPropertyPathCheck path)
        : this(path, match.Kind switch
        {
            Inheto.PropPathBlobMatchKind.Invalid => RecordPropertyPathMatchKind.Invalid,
            Inheto.PropPathBlobMatchKind.ExactSerializedValue => RecordPropertyPathMatchKind.ExactSerializedValue,
            Inheto.PropPathBlobMatchKind.SerializedPrefixWithClrContinuation => RecordPropertyPathMatchKind.SerializedPrefixWithClrContinuation,
            Inheto.PropPathBlobMatchKind.SerializedSourceMissing => RecordPropertyPathMatchKind.SerializedSourceMissing,
            Inheto.PropPathBlobMatchKind.SerializedSourceNull => RecordPropertyPathMatchKind.SerializedSourceNull,
            _ => throw new InvalidOperationException("Unknown record property-path match kind.")
        }, match.SerializedPrefix, match.ClrRemainder) { }

    /// <summary>Stores immutable match metadata, including the default/unprepared classification.<br/></summary>
    /// <param name="path">Cached structural plan.<br/></param><param name="kind">Match classification.<br/></param>
    /// <param name="serializedPrefix">Physical stored prefix.<br/></param><param name="clrRemainder">Remaining CLR suffix.<br/></param>
    internal RecordPropertyPathMatch(RecordPropertyPathCheck path, RecordPropertyPathMatchKind kind, string serializedPrefix, string clrRemainder)
    { Path = path; Kind = kind; SerializedPrefix = serializedPrefix; ClrRemainder = clrRemainder; }
    /// <summary>Gets the retained cached structural plan.<br/></summary>
    public RecordPropertyPathCheck Path { get; }
    /// <summary>Gets the named match classification; default is Invalid.<br/></summary>
    public RecordPropertyPathMatchKind Kind { get; }
    /// <summary>Gets the physical stored prefix, or null for a default match.<br/></summary>
    public string? SerializedPrefix { get; }
    /// <summary>Gets the remaining CLR suffix, or null for a default match.<br/></summary>
    public string? ClrRemainder { get; }
    /// <summary>Gets whether a value or a stored prefix with executable suffix exists.<br/></summary>
    public bool CanRead => Kind is RecordPropertyPathMatchKind.ExactSerializedValue or RecordPropertyPathMatchKind.SerializedPrefixWithClrContinuation;
}

/// <summary>Classifies execution of a prepared CLR path against one binary record.<br/></summary>
public enum RecordPropertyPathReadState
{
    /// <summary>The supplied structural plan is invalid/unprepared.<br/></summary>
    Invalid,
    /// <summary>A non-null value was produced.<br/></summary>
    Value,
    /// <summary>Execution produced null, distinct from absent or null stored source.<br/></summary>
    Null,
    /// <summary>The required encoded source is absent.<br/></summary>
    SourceMissing,
    /// <summary>A null encoded source prevents execution.<br/></summary>
    SourceNull,
    /// <summary>Invocation/materialization/conversion failed and error propagation was not requested.<br/></summary>
    InvocationFailed
}
