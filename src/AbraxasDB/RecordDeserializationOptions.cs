using System.Diagnostics.CodeAnalysis;

namespace AbraxasDB;

/// <summary>Controls automatic member hydration for custom property bytes without a registered reader.<br/></summary>
public enum UnhandledRecordCustomPropertyHandling
{
    /// <summary>Skip assignment without copying the payload, preserving an initialized destination member.<br/></summary>
    Ignore = 0,
    /// <summary>Use exact payload bytes with ordinary conversion; incompatible target members still throw.<br/></summary>
    AsBinary = 1
}

/// <summary>Creates one application-selected object from borrowed record context; null declines creation.<br/></summary>
/// <param name="context">Context valid only during this callback; do not retain its borrowed data.<br/></param>
/// <returns>Created object, or null to suppress this instance. Application exceptions propagate.<br/></returns>
public delegate object? RecordActivator(RecordActivationContext context);

/// <summary>
/// Supplies an activator's logical path, optional construction metadata and borrowed source access.<br/>
/// This readonly value is passed without a context-object allocation. Do not retain it beyond the callback.<br/>
/// Public path reads are independent operations; reentry preserves the surrounding hydration state.<br/>
/// </summary>
public readonly struct RecordActivationContext
{
    private readonly Inheto.InhetoBinary binary;
    /// <summary>Wraps native callback context without copying the payload.<br/></summary>
    /// <param name="context">Native borrowed context.<br/></param>
    internal RecordActivationContext(Inheto.DeserializationOptions.ActivatorContext context)
    {
        binary = context.Binary;
        PropPath = context.PropPath;
        StoredContext = context.StoredContext;
    }
    /// <summary>Gets the full logical path of the instance being constructed; empty selects the root.<br/></summary>
    public string PropPath { get; }
    /// <summary>Gets optional stored construction metadata, such as a comparer; no clone is made.<br/></summary>
    public object? StoredContext { get; }
    /// <summary>Gets borrowed complete record bytes; copy before retaining them beyond the callback.<br/></summary>
    public ReadOnlyMemory<byte> RawMemory => binary.RawMemory;
    /// <summary>Gets borrowed complete record bytes without a copy; valid only during this callback.<br/></summary>
    public ReadOnlySpan<byte> RawSpan => binary.RawSpan;
    /// <summary>Gets an independent list of stored member names, using the existing reader behavior.<br/></summary>
    public List<string> AllNames => binary.AllNames;
    /// <summary>Reads a requested value/shape by full path without exposing the component reader.<br/></summary>
    /// <typeparam name="TValue">Requested destination type.<br/></typeparam><param name="propPath">Full path, not relative to PropPath.<br/></param>
    /// <param name="options">Optional per-read policy; omission retains source-reader policy.<br/></param>
    /// <returns>Requested value or the existing null/missing result; conversion/hook failures propagate.<br/></returns>
    public TValue? ReadPropAs<TValue>(string propPath, RecordDeserializationOptions? options = null)
        => binary.ReadPropAs<TValue>(propPath, options?.NativeOptions);
    /// <summary>Reads a runtime-selected destination shape in an independent materialization operation.<br/></summary>
    /// <param name="returnType">Application destination type.<br/></param><param name="propPath">Full logical path.<br/></param>
    /// <param name="options">Optional read policy.<br/></param><returns>Requested value or null.<br/></returns>
    public object? ReadPropAs(Type returnType, string propPath, RecordDeserializationOptions? options = null)
        => binary.ReadPropAs(returnType, propPath, options?.NativeOptions);
    /// <summary>Reads a value only when its non-null runtime type exactly matches TValue.<br/></summary>
    /// <typeparam name="TValue">Required exact type.<br/></typeparam><param name="propPath">Full path.<br/></param>
    /// <param name="options">Optional read policy.<br/></param><returns>Exact value, otherwise default.<br/></returns>
    public TValue? ReadPropExact<TValue>(string propPath, RecordDeserializationOptions? options = null)
        => binary.ReadPropExact<TValue>(propPath, options?.NativeOptions);
    /// <summary>Fills an application-owned destination from a stored subtree, retaining existing fill semantics.<br/></summary>
    /// <typeparam name="TValue">Destination type.<br/></typeparam><param name="instance">Existing destination.<br/></param>
    /// <param name="propPath">Full source path.<br/></param><param name="options">Optional read policy.<br/></param>
    public void ReadPropOnto<TValue>(TValue instance, string propPath, RecordDeserializationOptions? options = null)
        => binary.ReadPropOnto(instance, propPath, options?.NativeOptions);
    /// <summary>Gets exact custom-property/type bytes without decoding or copying them.<br/></summary>
    /// <param name="propPath">Full custom-value path.<br/></param><param name="payload">Borrowed payload, valid only during this callback.<br/></param>
    /// <returns>True for an available custom binary payload.<br/></returns>
    public bool TryReadCustomBinaryPayload(string propPath, out ReadOnlyMemory<byte> payload)
        => binary.TryReadCustomBinaryPayload(propPath, out payload);
}

/// <summary>
/// Configures Abraxas member hydration, aliases, binary/text readers, activators and collection comparers.<br/>
/// One live native policy is retained; no settings snapshot is created per record.<br/>
/// Finish configuration before concurrent use. Replacing a store's options rebinds that store's links.<br/>
/// Use independent instances for different stores: deserialization policies carry store-bound link hooks.<br/>
/// </summary>
public sealed class RecordDeserializationOptions
{
    internal Inheto.DeserializationOptions NativeOptions { get; } = new();
    private Dictionary<Type, RecordActivator>? activators;
    /// <summary>Creates independent registrations and aliases, with public properties/fields and Ignore custom-property handling.<br/></summary>
    public RecordDeserializationOptions() => Aliases = new RecordPropertyAliases(NativeOptions.Aliases);
    /// <summary>Gets fresh independent options on each access, not a shared mutable singleton.<br/></summary>
    public static RecordDeserializationOptions Default => new();
    /// <summary>Gets/sets instance member selection; defaults to public properties and fields.<br/></summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is undefined.<br/></exception>
    public RecordMemberTypes MemberTypes
    {
        get => RecordMemberTypeBinding.FromNative(NativeOptions.MemberTypes);
        set => NativeOptions.MemberTypes = RecordMemberTypeBinding.ToNative(value);
    }
    /// <summary>Gets live read-time alias groups; exact requested names take precedence.<br/></summary>
    public RecordPropertyAliases Aliases { get; }
    /// <summary>
    /// Gets/sets special stored zero-input/singleton type activation; defaults to true for compatibility.<br/>
    /// False requires a concrete requested destination or explicit stored-type approval for those representations.<br/>
    /// This does not govern ordinary shape reconstruction, System.Type values, hooks or CLR path continuations.<br/>
    /// </summary>
    public bool AllowStoredTypeActivation
    {
        get => NativeOptions.AllowStoredTypeActivation;
        set => NativeOptions.AllowStoredTypeActivation = value;
    }
    /// <summary>
    /// Gets/sets automatic member handling for a custom property without an applicable reader; defaults to Ignore.<br/>
    /// Ignore preserves initialized destination members without copying payload bytes. AsBinary uses exact bytes and retains incompatible-target exceptions.<br/>
    /// Registered readers (including null results/errors), ordinary byte arrays, custom types, collection elements and explicit raw reads are unchanged.<br/>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The policy is undefined; previous state remains intact.<br/></exception>
    public UnhandledRecordCustomPropertyHandling UnhandledCustomProperties
    {
        get => NativeOptions.UnhandledCustomProperties switch
        {
            Inheto.UnhandledCustomPropertyHandling.Ignore => UnhandledRecordCustomPropertyHandling.Ignore,
            Inheto.UnhandledCustomPropertyHandling.AsBinary => UnhandledRecordCustomPropertyHandling.AsBinary,
            _ => throw new InvalidOperationException("Unknown native custom-property policy.")
        };
        set => NativeOptions.UnhandledCustomProperties = value switch
        {
            UnhandledRecordCustomPropertyHandling.Ignore => Inheto.UnhandledCustomPropertyHandling.Ignore,
            UnhandledRecordCustomPropertyHandling.AsBinary => Inheto.UnhandledCustomPropertyHandling.AsBinary,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown custom-property policy.")
        };
    }
    /// <summary>Registers/replaces a member binary reader, binding its original callback once without a forwarding decode layer.<br/></summary>
    /// <typeparam name="TValue">Application result type.<br/></typeparam><param name="propPath">Exact member path.<br/></param>
    /// <param name="deserializer">Borrowed-span reader; copy bytes before retaining them.<br/></param>
    public void AddDeserializer<TValue>(string propPath, BinaryDecoder<TValue> deserializer)
    {
        ArgumentNullException.ThrowIfNull(deserializer);
        NativeOptions.AddDeserializer(propPath, BinaryDecoderBinding.ToInheto(deserializer));
    }
    /// <summary>Registers/replaces an exact-type binary reader; member-specific readers take precedence.<br/></summary>
    /// <typeparam name="TValue">Exact requested application type.<br/></typeparam><param name="deserializer">Borrowed-span reader.<br/></param>
    public void AddTypeDeserializer<TValue>(BinaryDecoder<TValue> deserializer) => AddTypeDeserializer(false, deserializer);
    /// <summary>Registers a binary reader for a type and optionally its base classes, excluding System.Object.<br/></summary>
    /// <typeparam name="TValue">Application result type.<br/></typeparam><param name="includeBaseTypes">Also register base classes.<br/></param>
    /// <param name="deserializer">Borrowed-span reader, bound once at registration.<br/></param>
    public void AddTypeDeserializer<TValue>(bool includeBaseTypes, BinaryDecoder<TValue> deserializer)
    {
        ArgumentNullException.ThrowIfNull(deserializer);
        NativeOptions.AddTypeDeserializer(includeBaseTypes, BinaryDecoderBinding.ToInheto(deserializer));
    }
    /// <summary>Removes a member reader; a missing registration is a no-op.<br/></summary>
    /// <param name="propPath">Registered exact path.<br/></param>
    public void RemoveDeserializer(string propPath) => NativeOptions.RemoveDeserializer(propPath);
    /// <summary>Removes an exact-type reader; separately registered bases remain unchanged.<br/></summary>
    /// <param name="type">Exact registered type.<br/></param>
    public void RemoveTypeDeserializer(Type type) => NativeOptions.RemoveTypeDeserializer(type);
    /// <summary>Inspects member-before-type binary reader registration without invoking application code.<br/></summary>
    /// <param name="valueType">Exact requested type.<br/></param><param name="propPath">Optional member path.<br/></param>
    /// <returns>True for a registered effective reader.<br/></returns>
    public bool HasBinaryDeserializer(Type valueType, string? propPath = null) => NativeOptions.HasBinaryDeserializer(valueType, propPath);
    /// <summary>Inspects reader registration for a compile-time destination type.<br/></summary>
    /// <typeparam name="TValue">Exact destination type.<br/></typeparam><param name="propPath">Optional member path.<br/></param>
    /// <returns>True for a registered effective reader.<br/></returns>
    public bool HasBinaryDeserializer<TValue>(string? propPath = null) => NativeOptions.HasBinaryDeserializer<TValue>(propPath);

    /// <summary>Registers/replaces an exact-type activator. Null results decline creation, not request default construction.<br/></summary>
    /// <typeparam name="TValue">Exact destination type.<br/></typeparam><param name="activator">Application constructor/factory callback.<br/></param>
    public void AddActivator<TValue>(RecordActivator activator) => AddActivator(typeof(TValue), activator);
    /// <summary>
    /// Registers/replaces an exact-type activator with one configuration-time context adapter.<br/>
    /// Each invocation passes a readonly value context without a heap context object; callback exceptions propagate.<br/>
    /// </summary>
    /// <param name="type">Exact destination type.<br/></param><param name="activator">Application constructor/factory callback.<br/></param>
    public void AddActivator(Type type, RecordActivator activator)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(activator);
        NativeOptions.AddActivator(type, context => activator(new RecordActivationContext(context)));
        (activators ??= new())[type] = activator;
    }
    /// <summary>Removes an exact-type activator; existing materialized objects are unchanged.<br/></summary>
    /// <typeparam name="TValue">Exact destination type.<br/></typeparam><returns>True when removed.<br/></returns>
    public bool RemoveActivator<TValue>() => RemoveActivator(typeof(TValue));
    /// <summary>Removes an exact-type activator, following existing native construction/cache rules.<br/></summary>
    /// <param name="type">Exact destination type.<br/></param><returns>True when removed.<br/></returns>
    public bool RemoveActivator(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        activators?.Remove(type);
        return NativeOptions.RemoveActivator(type);
    }
    /// <summary>Checks registration without constructing an instance.<br/></summary>
    /// <param name="type">Exact destination type.<br/></param><returns>True for an exact registration.<br/></returns>
    public bool HasActivator(Type type) => NativeOptions.HasActivator(type);
    /// <summary>Returns the original registered callback without creating a wrapper or invoking it.<br/></summary>
    /// <param name="type">Exact destination type.<br/></param><param name="activator">Original application callback, or null.<br/></param>
    /// <returns>True when registered.<br/></returns>
    public bool TryGetActivator(Type type, [NotNullWhen(true)] out RecordActivator? activator)
    {
        ArgumentNullException.ThrowIfNull(type);
        activator = null;
        return activators?.TryGetValue(type, out activator) == true;
    }
    /// <summary>Registers a comparer used by compatible collection construction at an exact path; no clone is made.<br/></summary>
    /// <typeparam name="TValue">Compared element/key type.<br/></typeparam><param name="propPath">Exact member path; empty selects the root.<br/></param>
    /// <param name="comparer">Application comparer.<br/></param>
    public void AddMemberComparer<TValue>(string propPath, IComparer<TValue> comparer) => NativeOptions.AddMemberComparer(propPath, comparer);
    /// <summary>Registers a comparer for one exact collection-consumer type.<br/></summary>
    /// <typeparam name="TConsumer">Exact collection type.<br/></typeparam><typeparam name="TElement">Compared key/element type.<br/></typeparam>
    /// <param name="comparer">Application comparer, retained directly.<br/></param>
    public void AddTypeComparer<TConsumer, TElement>(IComparer<TElement> comparer) => NativeOptions.AddTypeComparer<TConsumer, TElement>(comparer);
    /// <summary>Removes an exact member comparer; a missing registration is a no-op.<br/></summary>
    /// <param name="propPath">Registered path.<br/></param>
    public void RemoveMemberComparer(string propPath) => NativeOptions.RemoveMemberComparer(propPath);
    /// <summary>Removes an exact-type collection comparer.<br/></summary>
    /// <param name="type">Registered consumer type.<br/></param>
    public void RemoveTypeComparer(Type type) => NativeOptions.RemoveTypeComparer(type);
    /// <summary>Removes an exact-type collection comparer.<br/></summary>
    /// <typeparam name="TValue">Registered consumer type.<br/></typeparam>
    public void RemoveTypeComparer<TValue>() => NativeOptions.RemoveTypeComparer<TValue>();
    /// <summary>Approves an exact stored zero-input type when automatic stored-type activation is disabled.<br/></summary>
    /// <typeparam name="TValue">Exact approved type; approval does not install a constructor/codec.<br/></typeparam>
    public void AllowStoredType<TValue>() => NativeOptions.AllowStoredType<TValue>();
    /// <summary>Approves a caller-selected stored type under its exact assembly-qualified name.<br/></summary>
    /// <param name="type">Approved type, retained directly; approval does not install a constructor/codec.<br/></param>
    public void AllowStoredType(Type type) => NativeOptions.AllowStoredType(type);
    /// <summary>Removes explicit stored-type approval without removing its activator.<br/></summary>
    /// <typeparam name="TValue">Exact approved type.<br/></typeparam><returns>True if approval was removed.<br/></returns>
    public bool RemoveAllowedStoredType<TValue>() => NativeOptions.RemoveAllowedStoredType<TValue>();
}

/// <summary>Supplies UTF-8 text readers while keeping ordinary byte-span lambdas unambiguous.<br/></summary>
public static class RecordDeserializationTextExtensions
{
    /// <summary>
    /// Registers/replaces a member reader receiving UTF-8 text from custom bytes.<br/>
    /// Use an explicitly typed string lambda or Func. One registration adapter is created; decoding creates a string, not an intermediate byte copy.<br/>
    /// Null markers bypass decoding; empty bytes yield String.Empty. Standard replacement fallback applies.<br/>
    /// </summary>
    /// <typeparam name="TValue">Application result type.<br/></typeparam><param name="options">Owning settings.<br/></param>
    /// <param name="propPath">Exact member path.<br/></param><param name="deserializer">Application text reader.<br/></param>
    public static void AddDeserializer<TValue>(this RecordDeserializationOptions options, string propPath, Func<string, TValue> deserializer)
    {
        ArgumentNullException.ThrowIfNull(options);
        Inheto.DeserializationOptionsTextExtensions.AddDeserializer(options.NativeOptions, propPath, deserializer);
    }
}

/// <summary>
/// Stores read-time property-path equivalence groups without exposing component contracts.<br/>
/// Exact requested names win, followed by the canonical name and aliases in registration order. Writing retains actual member names.<br/>
/// </summary>
public sealed class RecordPropertyAliases
{
    private readonly Inheto.PropertyAliasCollection aliases;
    /// <summary>Creates independent empty alias groups.<br/></summary>
    public RecordPropertyAliases() : this(new Inheto.PropertyAliasCollection()) { }
    /// <summary>Wraps the current native collection once without copying registrations.<br/></summary>
    /// <param name="aliases">Live native collection.<br/></param>
    internal RecordPropertyAliases(Inheto.PropertyAliasCollection aliases) => this.aliases = aliases;
    /// <summary>Gets the number of equivalence groups, not the number of individual paths.<br/></summary>
    public int Count => aliases.Count;
    /// <summary>Registers a canonical property/field path and ordered equivalents; conflicting groups throw.<br/></summary>
    /// <param name="canonicalPath">Stable first fallback path.<br/></param><param name="aliases">Equivalent property/field paths, not collection/key/method paths.<br/></param>
    /// <returns>True when added; false for an identical existing group.<br/></returns>
    public bool Add(string canonicalPath, params string[] aliases) => this.aliases.Add(canonicalPath, aliases);
    /// <summary>Removes the group containing a canonical path or alias.<br/></summary>
    /// <param name="propPath">Any group member.<br/></param><returns>True when removed.<br/></returns>
    public bool Remove(string propPath) => aliases.Remove(propPath);
    /// <summary>Removes all groups from this live policy.<br/></summary>
    public void Clear() => aliases.Clear();
    /// <summary>Checks whether alias resolution can affect a path or one of its ancestors.<br/></summary>
    /// <param name="propPath">Path to inspect.<br/></param><returns>True when alias resolution applies.<br/></returns>
    public bool Contains(string propPath) => aliases.Contains(propPath);
}
