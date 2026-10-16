namespace AbraxasDB;

/// <summary>Selects the instance members stored or hydrated by Abraxas; selection does not bypass getters/setters.<br/></summary>
public enum RecordMemberTypes
{
    /// <summary>Public instance properties and fields (the default).<br/></summary>
    PublicPropertiesAndFields,
    /// <summary>Public instance properties only.<br/></summary>
    PublicPropertiesOnly,
    /// <summary>Public instance fields only.<br/></summary>
    PublicFieldsOnly,
    /// <summary>Public and non-public instance fields, excluding properties.<br/></summary>
    PublicAndPrivateFields,
    /// <summary>Non-public instance fields only.<br/></summary>
    PrivateFieldsOnly
}

/// <summary>
/// Configures record member selection, exclusions, circular-member rules and custom writers.<br/>
/// Each instance retains one live serialization policy; collection properties are not snapshots.<br/>
/// Finish configuration before concurrent use. Custom writers/getters execute application code; failures propagate.<br/>
/// </summary>
public sealed class RecordSerializationOptions
{
    internal Inheto.SerializationOptions NativeOptions { get; } = new();

    /// <summary>Creates an independent policy selecting public properties and fields with no custom registrations.<br/></summary>
    public RecordSerializationOptions() { }

    /// <summary>Gets the live exact property-path exclusions; omitted members are not stored.<br/></summary>
    public HashSet<string> ExcludedMembers => NativeOptions.ExcludedMembers;
    /// <summary>Gets live exact-runtime-type exclusions for non-root named values; a null entry excludes named nulls.<br/></summary>
    public HashSet<Type> ExcludedTypes => NativeOptions.ExcludedTypes;
    /// <summary>Gets live included paths. An empty set includes all selected members except exclusions.<br/></summary>
    public HashSet<string> IncludedMembers => NativeOptions.IncludedMembers;
    /// <summary>Gets/sets instance member visibility selection; defaults to public properties and fields.<br/></summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a declared selection.<br/></exception>
    public RecordMemberTypes MemberTypes
    {
        get => RecordMemberTypeBinding.FromNative(NativeOptions.MemberTypes);
        set => NativeOptions.MemberTypes = RecordMemberTypeBinding.ToNative(value);
    }
    /// <summary>Gets/sets numbered key/value-pair storage for dictionaries with mixed key types; defaults to false.<br/></summary>
    public bool AllowMixedKeyTypesInDictionaryTypes
    {
        get => NativeOptions.AllowMixedKeyTypesInDictionaryTypes;
        set => NativeOptions.AllowMixedKeyTypesInDictionaryTypes = value;
    }
    /// <summary>
    /// Gets/sets rejection of unsupported ordinary values during writing; defaults to false.<br/>
    /// Exclusions and custom writers take precedence. This is not a second graph scan or a promise of every target projection.<br/>
    /// Getter/enumerator/hook failures still propagate; a failed partial payload must not be persisted.<br/>
    /// </summary>
    public bool FailOnUnsupportedType
    {
        get => NativeOptions.FailOnUnsupportedType;
        set => NativeOptions.FailOnUnsupportedType = value;
    }

    /// <summary>Registers a circular-member rule for an exact owner type and encoded member token.<br/></summary>
    /// <param name="objectType">Exact declaring/runtime owner type.<br/></param>
    /// <param name="memberName">Encoded member token, matching writer naming.<br/></param>
    public void AddCircularProperty(Type objectType, string memberName) => NativeOptions.AddCircularProperty(objectType, memberName);
    /// <summary>Registers a circular-member rule without resolving the owner type at runtime.<br/></summary>
    /// <typeparam name="T">Exact owner type.<br/></typeparam><param name="memberName">Encoded member token.<br/></param>
    public void AddCircularProperty<T>(string memberName) => NativeOptions.AddCircularProperty<T>(memberName);
    /// <summary>Removes one circular-member rule; a missing rule is a no-op.<br/></summary>
    /// <param name="objectType">Exact owner type.<br/></param><param name="memberName">Registered member token.<br/></param>
    public void RemoveCircularProperty(Type objectType, string memberName) => NativeOptions.RemoveCircularProperty(objectType, memberName);

    /// <summary>
    /// Registers/replaces a member-specific text writer, taking precedence over type-wide writers.<br/>
    /// Text uses UTF-8 without a BOM; empty text and null remain distinct. Pair with a text or byte reader.<br/>
    /// </summary>
    /// <typeparam name="TValue">Application member type.<br/></typeparam>
    /// <param name="propName">Exact property path.<br/></param><param name="serializer">Application writer.<br/></param>
    public void AddSerializer<TValue>(string propName, Func<TValue, string> serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        NativeOptions.AddSerializer(propName, serializer);
    }
    /// <summary>Registers/replaces a member-specific binary writer; its exact returned bytes are stored.<br/></summary>
    /// <typeparam name="TValue">Application member type.<br/></typeparam>
    /// <param name="propName">Exact property path.<br/></param><param name="serializer">Application binary writer.<br/></param>
    public void AddSerializer<TValue>(string propName, Func<TValue, byte[]> serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        NativeOptions.AddSerializer(propName, serializer);
    }
    /// <summary>Registers/replaces an exact-type binary writer; a member-specific writer takes precedence.<br/></summary>
    /// <typeparam name="TValue">Exact application value type.<br/></typeparam><param name="serializer">Application binary writer.<br/></param>
    public void AddTypeSerializer<TValue>(Func<TValue, byte[]> serializer) => AddTypeSerializer(false, serializer);
    /// <summary>
    /// Registers a binary writer for the selected type and optionally its base classes, excluding System.Object.<br/>
    /// Base registrations still invoke the supplied typed callback; values must be compatible with TValue.<br/>
    /// </summary>
    /// <typeparam name="TValue">Application value type.<br/></typeparam>
    /// <param name="includeBaseTypes">Also register base classes.<br/></param><param name="serializer">Application binary writer.<br/></param>
    public void AddTypeSerializer<TValue>(bool includeBaseTypes, Func<TValue, byte[]> serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        NativeOptions.AddTypeSerializer(includeBaseTypes, serializer);
    }
    /// <summary>Removes an exact-type writer; base registrations, if any, remain independently registered.<br/></summary>
    /// <typeparam name="TValue">Exact type to remove.<br/></typeparam>
    public void RemoveSerializer<TValue>() => NativeOptions.RemoveSerializer<TValue>();
    /// <summary>Removes one member writer and its binary/text registration metadata.<br/></summary>
    /// <param name="memberName">Exact registered path.<br/></param>
    public void RemoveSerializer(string memberName) => NativeOptions.RemoveSerializer(memberName);
    /// <summary>Inspects effective binary-writer registration without invoking application code or converting data.<br/></summary>
    /// <param name="valueType">Exact runtime type.<br/></param><param name="propPath">Optional member path; member writers take precedence.<br/></param>
    /// <returns>True for an effective binary writer; a member text writer masks a type binary writer.<br/></returns>
    public bool HasBinarySerializer(Type valueType, string? propPath = null) => NativeOptions.HasBinarySerializer(valueType, propPath);
    /// <summary>Invokes an effective binary writer without an outer record envelope or copying its result.<br/></summary>
    /// <param name="value">Non-null application value.<br/></param><param name="valueType">Exact lookup type.<br/></param>
    /// <param name="propPath">Optional overriding member path.<br/></param><param name="bytes">Writer-owned bytes, or default when no binary result exists.<br/></param>
    /// <returns>True when a binary writer returns non-null bytes; callback failures propagate.<br/></returns>
    public bool TrySerializeBinary(object value, Type valueType, string? propPath, out byte[] bytes)
        => NativeOptions.TrySerializeBinary(value, valueType, propPath, out bytes);
    /// <summary>Invokes an effective typed binary writer without copying its returned array.<br/></summary>
    /// <typeparam name="TValue">Exact type used for writer lookup.<br/></typeparam>
    /// <param name="value">Non-null value.<br/></param><param name="propPath">Optional member path.<br/></param>
    /// <param name="bytes">Writer-owned result.<br/></param><returns>True for a non-null binary result.<br/></returns>
    public bool TrySerializeBinary<TValue>(TValue value, string? propPath, out byte[] bytes)
        => NativeOptions.TrySerializeBinary(value, propPath, out bytes);
}

/// <summary>Maps owned member selections at configuration time, never during record traversal.<br/></summary>
internal static class RecordMemberTypeBinding
{
    /// <summary>Validates an owned selection before native state changes.<br/></summary>
    /// <param name="value">Owned selection.<br/></param><returns>Corresponding native selection.<br/></returns>
    internal static Inheto.MemberTypesEnum ToNative(RecordMemberTypes value) => value switch
    {
        RecordMemberTypes.PublicPropertiesAndFields => Inheto.MemberTypesEnum.PublicPropertiesAndFields,
        RecordMemberTypes.PublicPropertiesOnly => Inheto.MemberTypesEnum.PublicPropertiesOnly,
        RecordMemberTypes.PublicFieldsOnly => Inheto.MemberTypesEnum.PublicFieldsOnly,
        RecordMemberTypes.PublicAndPrivateFields => Inheto.MemberTypesEnum.PublicAndPrivateFields,
        RecordMemberTypes.PrivateFieldsOnly => Inheto.MemberTypesEnum.PrivateFieldsOnly,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown record member selection.")
    };
    /// <summary>Reads the exact existing selection without boxing/reflection.<br/></summary>
    /// <param name="value">Native selection.<br/></param><returns>Owned selection.<br/></returns>
    internal static RecordMemberTypes FromNative(Inheto.MemberTypesEnum value) => value switch
    {
        Inheto.MemberTypesEnum.PublicPropertiesAndFields => RecordMemberTypes.PublicPropertiesAndFields,
        Inheto.MemberTypesEnum.PublicPropertiesOnly => RecordMemberTypes.PublicPropertiesOnly,
        Inheto.MemberTypesEnum.PublicFieldsOnly => RecordMemberTypes.PublicFieldsOnly,
        Inheto.MemberTypesEnum.PublicAndPrivateFields => RecordMemberTypes.PublicAndPrivateFields,
        Inheto.MemberTypesEnum.PrivateFieldsOnly => RecordMemberTypes.PrivateFieldsOnly,
        _ => throw new InvalidOperationException("Unknown native record member selection.")
    };
}
