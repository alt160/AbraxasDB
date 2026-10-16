using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace AbraxasDB
{
    /// <summary>
    /// Controls how registered link members participate in one record write.<br/>
    /// The handling applies only to members registered through <see cref="RecordStore{T}.Links"/>; ordinary members retain normal Inheto behavior.<br/>
    /// </summary>
    public enum LinkHandling
    {
        /// <summary>Runs the registered link serialization behavior normally.<br/></summary>
        Normal = 0,
        /// <summary>Ignores the supplied link value and retains the existing stored custom-property payload without invoking its serializer.<br/></summary>
        Preserve = 1,
        /// <summary>Ignores the supplied link value and removes that member from the newly stored payload.<br/></summary>
        Clear = 2
    }

    /// <summary>
    /// Selects a non-property binary source for one derived read-only link.<br/>
    /// Property-backed sources use the textual source-path overload instead.<br/>
    /// </summary>
    public enum LinkSource
    {
        /// <summary>Encodes the current owning record identity as a standalone Inheto <see cref="ulong"/> payload.<br/></summary>
        RecordID = 1
    }

    /// <summary>
    /// Associates one textual Inheto property path with its requested write handling.<br/>
    /// Value tuples implicitly convert to this struct so mixed handling remains compact at the call site.<br/>
    /// </summary>
    public readonly struct LinkWriteRule
    {
        /// <summary>
        /// Creates one link write rule.<br/>
        /// The owning record store validates and canonicalizes <paramref name="propPath"/> before mutation begins.<br/>
        /// </summary>
        /// <param name="propPath">Registered link property path.<br/></param>
        /// <param name="handling">Handling applied to that link for one write.<br/></param>
        public LinkWriteRule(string propPath, LinkHandling handling)
        {
            if (string.IsNullOrWhiteSpace(propPath))
                throw new ArgumentException("A link property path is required.", nameof(propPath));
            if (!Enum.IsDefined(handling))
                throw new ArgumentOutOfRangeException(nameof(handling));
            PropPath = propPath;
            Handling = handling;
        }

        /// <summary>Gets the developer-supplied link property path.<br/></summary>
        public string PropPath { get; }

        /// <summary>Gets the handling requested for the link.<br/></summary>
        public LinkHandling Handling { get; }

        /// <summary>Converts compact tuple syntax into one strongly typed link write rule.<br/></summary>
        /// <param name="rule">Property path and handling pair.<br/></param>
        public static implicit operator LinkWriteRule((string PropPath, LinkHandling Handling) rule)
            => new(rule.PropPath, rule.Handling);
    }

    public partial class RecordStore<T>
    {
        internal sealed class LinkReadPolicy
        {
            private readonly object sync = new();
            private Inheto.DeserializationOptions? sourceOptions;
            private Inheto.DeserializationOptions? effectiveOptions;

            internal LinkReadPolicy(string key, string[] propPaths)
            {
                Key = key;
                PropPaths = propPaths;
                ExcludedMembers = new HashSet<string>(propPaths, StringComparer.Ordinal);
            }

            internal HashSet<string> ExcludedMembers { get; }
            internal string Key { get; }
            internal string[] PropPaths { get; }

            internal Inheto.DeserializationOptions Options(Inheto.DeserializationOptions source)
            {
                lock (sync)
                {
                    if (!ReferenceEquals(sourceOptions, source))
                    {
                        sourceOptions = source;
                        effectiveOptions = source.ForkExcluding(ExcludedMembers);
                    }
                    return effectiveOptions!;
                }
            }
        }

        internal sealed class LinkWritePlan
        {
            internal LinkWritePlan(Dictionary<string, LinkHandling> rules) => Rules = rules;
            internal Dictionary<string, LinkHandling> Rules { get; }
            internal Dictionary<string, byte[]>? PreservedPayloads { get; set; }
            internal bool NeedsExisting
            {
                get
                {
                    foreach (var rule in Rules)
                        if (rule.Value == LinkHandling.Preserve)
                            return true;
                    return false;
                }
            }
        }

        /// <summary>
        /// Registers persistent property links whose developer-supplied binary codecs remain authoritative across every object-producing and mutation path.<br/>
        /// Abraxas stores the writer's exact bytes, gives those bytes to the reader, and internally adapts the semantic result when a compatible projection requests a different CLR shape.<br/>
        /// </summary>
        public sealed class LinkCollection
        {
            private delegate byte[] BinaryWriter(object? value);
            private delegate object? BinaryReader(ReadOnlySpan<byte> payload);

            private sealed class Definition
            {
                internal Definition(
                    string propPath,
                    Type valueType,
                    MemberInfo[] sourceMembers,
                    string sourcePath,
                    Type? sourceType,
                    LinkSource? source,
                    BinaryWriter? writer,
                    BinaryReader? reader,
                    bool readOnly,
                    bool derived,
                    string[] aliases)
                {
                    PropPath = propPath;
                    ValueType = valueType;
                    SourceMembers = sourceMembers;
                    SourcePath = sourcePath;
                    SourceType = sourceType;
                    Source = source;
                    Writer = writer;
                    Reader = reader;
                    ReadOnly = readOnly;
                    Derived = derived;
                    Aliases = aliases;
                }

                internal string PropPath { get; }
                internal Type ValueType { get; }
                internal MemberInfo[] SourceMembers { get; }
                internal string SourcePath { get; }
                internal Type? SourceType { get; }
                internal LinkSource? Source { get; }
                internal BinaryWriter? Writer { get; }
                internal BinaryReader? Reader { get; }
                internal bool ReadOnly { get; }
                internal bool Derived { get; }
                internal string[] Aliases { get; }
                internal bool UsesExplicitCodec => Reader is not null;
            }

            private readonly object sync = new();
            private readonly RecordStore<T> store;
            private readonly Dictionary<string, Definition> definitions = new(StringComparer.Ordinal);
            private readonly Dictionary<string, LinkReadPolicy> readPolicies = new(StringComparer.Ordinal);
            private readonly Inheto.DeserializationOptions.RootMaterializerDelegate rootMaterializer;
            private readonly Inheto.DeserializationOptions.RootOptionsDelegate rootOptionsPreparer;
            private readonly Dictionary<(Inheto.DeserializationOptions Options, Type Type), Inheto.DeserializationOptions> preparedOptions = new();
            private readonly Dictionary<Inheto.DeserializationOptions, Inheto.DeserializationOptions> preparedSources = new();
            private Definition[] snapshot = Array.Empty<Definition>();
            private Dictionary<string, Definition> targets = new(StringComparer.Ordinal);

            internal LinkCollection(RecordStore<T> store)
            {
                this.store = store ?? throw new ArgumentNullException(nameof(store));
                rootMaterializer = Materialize;
                rootOptionsPreparer = PrepareMaterializationOptions;
                store.NativeDeserializationOptions.AddRootMaterializer(rootMaterializer);
                store.NativeDeserializationOptions.AddRootOptionsPreparer(rootOptionsPreparer);
            }

            /// <summary>Gets the number of registered link members.<br/></summary>
            public int Count
            {
                get
                {
                    lock (sync)
                        return definitions.Count;
                }
            }

            /// <summary>
            /// Registers one persistent property link with an authoritative binary writer and reader.<br/>
            /// The writer receives the named property's semantic value and returns the exact stored payload; the reader receives that payload and restores the semantic value.<br/>
            /// Matching remains property-path based, so compatible alternate projection types receive the same decoded value through internal Inheto reprojection.<br/>
            /// </summary>
            /// <typeparam name="TValue">Semantic CLR type accepted and returned by the link codec.<br/></typeparam>
            /// <param name="propPath">Canonical source property or field path.<br/></param>
            /// <param name="write">Developer-owned conversion from the property value to its exact stored bytes.<br/></param>
            /// <param name="read">Developer-owned conversion from the exact stored bytes to the property's semantic value.<br/></param>
            /// <param name="aliases">Optional alternate property paths accepted on requested projection types.<br/></param>
            /// <returns><see langword="true"/> when a new link was registered; otherwise <see langword="false"/>.<br/></returns>
            public bool Add<TValue>(
                string propPath,
                Func<TValue, byte[]> write,
                BinaryDecoder<TValue> read,
                params string[] aliases)
            {
                ArgumentNullException.ThrowIfNull(write);
                ArgumentNullException.ThrowIfNull(read);
                ArgumentNullException.ThrowIfNull(aliases);
                return AddCore(
                    propPath,
                    typeof(TValue),
                    value => write(value is null ? default! : (TValue)value),
                    payload => read(payload),
                    readOnly: false,
                    aliases);
            }

            /// <summary>
            /// Registers an explicitly read-only persistent property link.<br/>
            /// Normal writes fail before persistence; callers must choose <see cref="LinkHandling.Preserve"/> or <see cref="LinkHandling.Clear"/> for that operation.<br/>
            /// Hydration still invokes the supplied binary reader for the store's declared type and every compatible projected shape.<br/>
            /// </summary>
            /// <typeparam name="TValue">Semantic CLR type returned by the link reader.<br/></typeparam>
            /// <param name="propPath">Canonical source property or field path.<br/></param>
            /// <param name="read">Developer-owned conversion from the exact stored bytes to the property's semantic value.<br/></param>
            /// <param name="aliases">Optional alternate property paths accepted on requested projection types.<br/></param>
            /// <returns><see langword="true"/> when a new link was registered; otherwise <see langword="false"/>.<br/></returns>
            public bool AddReadOnly<TValue>(
                string propPath,
                BinaryDecoder<TValue> read,
                params string[] aliases)
            {
                ArgumentNullException.ThrowIfNull(read);
                ArgumentNullException.ThrowIfNull(aliases);
                return AddCore(
                    propPath,
                    typeof(TValue),
                    null,
                    payload => read(payload),
                    readOnly: true,
                    aliases);
            }

            /// <summary>
            /// Registers one derived projection-only link whose target is populated from another stored property.<br/>
            /// A custom-property source is passed to <paramref name="read"/> as its exact stored custom payload; an ordinary Inheto property is passed as a standalone payload decoded with <see cref="Inheto.InhetoBinaryCodec.Decode{TValue}(ReadOnlySpan{byte})"/> using the source property's CLR type.<br/>
            /// The target may exist only on an alternate projection. It is excluded from writes and never makes an ordinary store write fail.<br/>
            /// </summary>
            /// <typeparam name="TValue">Semantic target value returned by the derived reader.<br/></typeparam>
            /// <param name="propPath">Projection target property or field path.<br/></param>
            /// <param name="source">Stored source property or field path on <typeparamref name="T"/>.<br/></param>
            /// <param name="read">Developer-owned conversion from the source's binary payload to the target semantic value.<br/></param>
            /// <param name="aliases">Optional alternate target paths accepted on requested projection types.<br/></param>
            /// <returns><see langword="true"/> when a new target link was registered; otherwise <see langword="false"/>.<br/></returns>
            public bool AddReadOnly<TValue>(
                string propPath,
                string source,
                BinaryDecoder<TValue> read,
                params string[] aliases)
            {
                ArgumentNullException.ThrowIfNull(read);
                ArgumentNullException.ThrowIfNull(aliases);
                return AddDerivedCore(propPath, source, null, typeof(TValue), payload => read(payload), aliases);
            }

            /// <summary>
            /// Registers one derived projection-only link whose reader receives the selected non-property binary source.<br/>
            /// <see cref="LinkSource.RecordID"/> is encoded as a standalone Inheto <see cref="ulong"/> payload and may be decoded with <see cref="Inheto.InhetoBinaryCodec.Decode{TValue}(ReadOnlySpan{byte})"/>.<br/>
            /// The target may exist only on an alternate projection. It is excluded from writes and never makes an ordinary store write fail.<br/>
            /// </summary>
            /// <typeparam name="TValue">Semantic target value returned by the derived reader.<br/></typeparam>
            /// <param name="propPath">Projection target property or field path.<br/></param>
            /// <param name="source">Non-property binary source supplied to the reader.<br/></param>
            /// <param name="read">Developer-owned conversion from the selected source payload to the target semantic value.<br/></param>
            /// <param name="aliases">Optional alternate target paths accepted on requested projection types.<br/></param>
            /// <returns><see langword="true"/> when a new target link was registered; otherwise <see langword="false"/>.<br/></returns>
            public bool AddReadOnly<TValue>(
                string propPath,
                LinkSource source,
                BinaryDecoder<TValue> read,
                params string[] aliases)
            {
                ArgumentNullException.ThrowIfNull(read);
                ArgumentNullException.ThrowIfNull(aliases);
                if (!Enum.IsDefined(source))
                    throw new ArgumentOutOfRangeException(nameof(source));
                return AddDerivedCore(propPath, null, source, typeof(TValue), payload => read(payload), aliases);
            }

            /// <summary>
            /// Registers one typed member with its authoritative binary writer and reader.<br/>
            /// </summary>
            /// <typeparam name="TValue">Selected member and semantic codec type.<br/></typeparam>
            /// <param name="propPath">Member expression resolved through the existing Abraxas property-path vocabulary.<br/></param>
            /// <param name="write">Developer-owned conversion from the property value to its exact stored bytes.<br/></param>
            /// <param name="read">Developer-owned conversion from the exact stored bytes to the property's semantic value.<br/></param>
            /// <param name="aliases">Optional alternate property paths accepted on requested projection types.<br/></param>
            /// <returns><see langword="true"/> when a new link was registered; otherwise <see langword="false"/>.<br/></returns>
            public bool Add<TValue>(
                Expression<Func<T, TValue>> propPath,
                Func<TValue, byte[]> write,
                BinaryDecoder<TValue> read,
                params string[] aliases)
            {
                ArgumentNullException.ThrowIfNull(propPath);
                return Add(propPath.GetPropertyPath(), write, read, aliases);
            }

            /// <summary>
            /// Registers one typed member as an explicitly read-only persistent property link.<br/>
            /// Normal writes fail before persistence; Preserve and Clear remain available per operation.<br/>
            /// </summary>
            /// <typeparam name="TValue">Selected member and semantic reader type.<br/></typeparam>
            /// <param name="propPath">Member expression resolved through the existing Abraxas property-path vocabulary.<br/></param>
            /// <param name="read">Developer-owned conversion from the exact stored bytes to the property's semantic value.<br/></param>
            /// <param name="aliases">Optional alternate property paths accepted on requested projection types.<br/></param>
            /// <returns><see langword="true"/> when a new link was registered; otherwise <see langword="false"/>.<br/></returns>
            public bool AddReadOnly<TValue>(
                Expression<Func<T, TValue>> propPath,
                BinaryDecoder<TValue> read,
                params string[] aliases)
            {
                ArgumentNullException.ThrowIfNull(propPath);
                return AddReadOnly(propPath.GetPropertyPath(), read, aliases);
            }

            /// <summary>
            /// Removes one persistent property link by its canonical source path.<br/>
            /// Removing a missing link is a no-op and does not alter Inheto serializer or deserializer registrations owned by the developer.<br/>
            /// </summary>
            /// <param name="propPath">Canonical source property path.<br/></param>
            /// <returns><see langword="true"/> when a registration was removed; otherwise <see langword="false"/>.<br/></returns>
            public bool Remove(string propPath)
            {
                string path = NormalizeProjectionPath(propPath, nameof(propPath));
                lock (sync)
                {
                    if (!definitions.Remove(path))
                        return false;
                    RefreshSnapshot();
                    return true;
                }
            }

            /// <summary>
            /// Removes every persistent property link owned by this store while leaving developer-owned Inheto handlers unchanged.<br/>
            /// </summary>
            public void Clear()
            {
                lock (sync)
                {
                    if (definitions.Count == 0)
                        return;
                    definitions.Clear();
                    RefreshSnapshot();
                }
            }

            internal void Rebind(
                Inheto.DeserializationOptions previous,
                Inheto.DeserializationOptions current)
            {
                if (ReferenceEquals(previous, current))
                    return;
                previous.RemoveRootMaterializer(rootMaterializer);
                previous.RemoveRootOptionsPreparer(rootOptionsPreparer);
                current.AddRootMaterializer(rootMaterializer);
                current.AddRootOptionsPreparer(rootOptionsPreparer);
                lock (sync)
                {
                    preparedOptions.Clear();
                    preparedSources.Clear();
                }
            }

            private bool AddCore(
                string propPath,
                Type semanticType,
                BinaryWriter? writer,
                BinaryReader? reader,
                bool readOnly,
                string[] aliases)
            {
                string path = Normalize(propPath, out Type valueType);
                string[] normalizedAliases = NormalizeAliases(path, aliases);
                if (!TryResolveReadableMember(typeof(T), path, out MemberInfo[] sourceMembers, out Type sourceValueType))
                    throw new ArgumentException($"Link path '{path}' is not readable on '{typeof(T).FullName}'.", nameof(propPath));
                if (sourceValueType != valueType)
                    throw new InvalidOperationException($"Link path '{path}' resolved inconsistently as '{valueType}' and '{sourceValueType}'.");
                if (writer is not null && !semanticType.IsAssignableFrom(sourceValueType))
                    throw new ArgumentException(
                        $"Link writer type '{semanticType.FullName}' cannot accept '{path}' values of type '{sourceValueType.FullName}'.",
                        nameof(propPath));
                lock (sync)
                {
                    if (definitions.ContainsKey(path))
                        return false;
                    definitions.Add(
                        path,
                        new Definition(
                            path,
                            semanticType,
                            sourceMembers,
                            path,
                            sourceValueType,
                            null,
                            writer,
                            reader,
                            readOnly,
                            derived: false,
                            normalizedAliases));
                    RefreshSnapshot();
                    return true;
                }
            }

            private bool AddDerivedCore(
                string propPath,
                string? sourcePath,
                LinkSource? source,
                Type semanticType,
                BinaryReader reader,
                string[] aliases)
            {
                string targetPath = NormalizeProjectionPath(propPath, nameof(propPath));
                string[] normalizedAliases = NormalizeAliases(targetPath, aliases);
                Type? sourceType = null;
                if (sourcePath is not null)
                    sourcePath = Normalize(sourcePath, out sourceType);
                lock (sync)
                {
                    if (definitions.ContainsKey(targetPath))
                        return false;
                    definitions.Add(
                        targetPath,
                        new Definition(
                            targetPath,
                            semanticType,
                            Array.Empty<MemberInfo>(),
                            sourcePath ?? string.Empty,
                            sourceType,
                            source,
                            null,
                            reader,
                            readOnly: true,
                            derived: true,
                            normalizedAliases));
                    RefreshSnapshot();
                    return true;
                }
            }

            internal LinkReadPolicy SuppressAll()
            {
                lock (sync)
                {
                    const string key = "*";
                    if (readPolicies.TryGetValue(key, out LinkReadPolicy? policy))
                        return policy;
                    var paths = new string[definitions.Count];
                    definitions.Keys.CopyTo(paths, 0);
                    Array.Sort(paths, StringComparer.Ordinal);
                    policy = new LinkReadPolicy(key, paths);
                    readPolicies.Add(key, policy);
                    return policy;
                }
            }

            internal LinkReadPolicy Suppress(string propPath, params string[] additionalLinks)
            {
                ArgumentNullException.ThrowIfNull(additionalLinks);
                var paths = new string[additionalLinks.Length + 1];
                paths[0] = RequireRegistered(propPath);
                for (int index = 0; index < additionalLinks.Length; index++)
                    paths[index + 1] = RequireRegistered(additionalLinks[index]);
                Array.Sort(paths, StringComparer.Ordinal);
                int count = 1;
                for (int index = 1; index < paths.Length; index++)
                    if (!StringComparer.Ordinal.Equals(paths[index], paths[count - 1]))
                        paths[count++] = paths[index];
                if (count != paths.Length)
                    Array.Resize(ref paths, count);
                string key = string.Join('\n', paths);
                lock (sync)
                {
                    if (!readPolicies.TryGetValue(key, out LinkReadPolicy? policy))
                    {
                        policy = new LinkReadPolicy(key, paths);
                        readPolicies.Add(key, policy);
                    }
                    return policy;
                }
            }

            internal LinkWritePlan? Plan(LinkHandling handling)
            {
                ValidateHandling(handling);
                if (handling == LinkHandling.Normal)
                    return null;
                lock (sync)
                {
                    var rules = new Dictionary<string, LinkHandling>(definitions.Count, StringComparer.Ordinal);
                    foreach (var definition in definitions)
                        if (!definition.Value.Derived)
                            rules.Add(definition.Key, handling);
                    return rules.Count == 0 ? null : new LinkWritePlan(rules);
                }
            }

            internal LinkWritePlan? Plan(LinkHandling handling, string propPath, string[] additionalLinks)
            {
                ValidateHandling(handling);
                ArgumentNullException.ThrowIfNull(additionalLinks);
                var rules = new Dictionary<string, LinkHandling>(additionalLinks.Length + 1, StringComparer.Ordinal);
                Add(propPath);
                for (int index = 0; index < additionalLinks.Length; index++)
                    Add(additionalLinks[index]);
                return handling == LinkHandling.Normal || rules.Count == 0 ? null : new LinkWritePlan(rules);

                void Add(string requestedPath)
                {
                    string path = RequireRegistered(requestedPath);
                    lock (sync)
                        if (!definitions[path].Derived)
                            rules[path] = handling;
                }
            }

            internal LinkWritePlan? Plan(LinkWriteRule first, LinkWriteRule[] additionalRules)
            {
                ArgumentNullException.ThrowIfNull(additionalRules);
                var rules = new Dictionary<string, LinkHandling>(additionalRules.Length + 1, StringComparer.Ordinal);
                Add(first);
                for (int index = 0; index < additionalRules.Length; index++)
                    Add(additionalRules[index]);
                foreach (var rule in rules)
                    if (rule.Value != LinkHandling.Normal)
                        return new LinkWritePlan(rules);
                return null;

                void Add(LinkWriteRule rule)
                {
                    ValidateHandling(rule.Handling);
                    string path = RequireRegistered(rule.PropPath);
                    lock (sync)
                        if (definitions[path].Derived)
                            return;
                    if (rules.TryGetValue(path, out LinkHandling existing) && existing != rule.Handling)
                        throw new ArgumentException($"Link '{path}' has conflicting '{existing}' and '{rule.Handling}' write rules.");
                    rules[path] = rule.Handling;
                }
            }

            internal Inheto.SerializationOptions PrepareWrite(
                Inheto.SerializationOptions source,
                T record,
                Inheto.InhetoBinary? prior,
                bool isNewRecord,
                LinkWritePlan? plan)
            {
                Definition[] current = snapshot;
                if (current.Length == 0)
                    return source;

                Inheto.SerializationOptions options = source.ForkForOperation();
                for (int index = 0; index < current.Length; index++)
                {
                    Definition definition = current[index];
                    if (definition.Derived)
                    {
                        options.ExcludedMembers.Add(definition.PropPath);
                        continue;
                    }
                    LinkHandling handling = plan?.Rules.TryGetValue(definition.PropPath, out LinkHandling requested) == true
                        ? requested
                        : LinkHandling.Normal;
                    if (handling == LinkHandling.Normal)
                    {
                        if (definition.ReadOnly)
                            throw new InvalidOperationException(
                                $"Link '{definition.PropPath}' is read-only. Use LinkHandling.Preserve or LinkHandling.Clear for this write.");
                        if (definition.UsesExplicitCodec)
                        {
                            object? value = GetMemberValue(record!, definition.SourceMembers, definition.PropPath);
                            byte[] payload = definition.Writer!(value) ??
                                throw new InvalidOperationException($"Link writer '{definition.PropPath}' returned null instead of a byte array.");
                            options.SerializedMemberOverrides.Add(definition.PropPath, payload);
                        }
                        continue;
                    }
                    if (handling == LinkHandling.Clear || isNewRecord || prior is null)
                    {
                        options.ExcludedMembers.Add(definition.PropPath);
                        continue;
                    }

                    if (!prior.TryReadCustomPropertyPayload(definition.PropPath, out byte[] preservedPayload))
                    {
                        options.ExcludedMembers.Add(definition.PropPath);
                        continue;
                    }
                    options.SerializedMemberOverrides.Add(definition.PropPath, preservedPayload);
                    (plan!.PreservedPayloads ??= new Dictionary<string, byte[]>(StringComparer.Ordinal))
                        .Add(definition.PropPath, preservedPayload);
                }
                return options;
            }

            internal void ValidateWrite(LinkWritePlan? plan)
            {
                Definition[] current = snapshot;
                for (int index = 0; index < current.Length; index++)
                {
                    Definition definition = current[index];
                    if (definition.Derived)
                        continue;
                    LinkHandling handling = plan?.Rules.TryGetValue(definition.PropPath, out LinkHandling requested) == true
                        ? requested
                        : LinkHandling.Normal;
                    if (handling != LinkHandling.Normal)
                        continue;
                    if (definition.ReadOnly)
                        throw new InvalidOperationException(
                            $"Link '{definition.PropPath}' is read-only. Use LinkHandling.Preserve or LinkHandling.Clear for this write.");
                }
            }

            internal void ValidateWrite(Inheto.InhetoBinary binary, LinkWritePlan? plan)
            {
                if (plan?.PreservedPayloads is null)
                    return;
                foreach (var preserved in plan.PreservedPayloads)
                {
                    if (!binary.TryReadCustomPropertyPayload(preserved.Key, out byte[] actual) ||
                        !actual.AsSpan().SequenceEqual(preserved.Value))
                    {
                        throw new InvalidOperationException(
                            $"Link '{preserved.Key}' could not retain its exact stored payload. Ensure every parent object on a nested link path is present in the replacement record.");
                    }
                }
            }

            internal bool TryResolveTarget(string propPath, out string targetPath)
            {
                string requestedPath = PropPath.NormalizePath(propPath);
                if (targets.TryGetValue(requestedPath, out Definition? definition))
                {
                    targetPath = definition.PropPath;
                    return true;
                }
                targetPath = requestedPath;
                return false;
            }

            internal bool TryReadProperty(
                Inheto.InhetoBinary binary,
                ulong recordID,
                string targetPath,
                Type requestedType,
                LinkReadPolicy? policy,
                Inheto.DeserializationOptions sourceOptions,
                out object? value)
            {
                if (!targets.TryGetValue(targetPath, out Definition? definition) || !definition.UsesExplicitCodec)
                {
                    value = null;
                    return false;
                }
                if (policy?.ExcludedMembers.Contains(definition.PropPath) == true ||
                    !TryReadSemanticValue(binary, recordID, definition, sourceOptions, out object? semanticValue))
                {
                    value = ReprojectValue(null, requestedType, sourceOptions);
                    return true;
                }
                value = ReprojectValue(semanticValue, requestedType, sourceOptions);
                return true;
            }

            private object? Materialize(
                Inheto.InhetoBinary binary,
                object? record,
                Type requestedType,
                object? context,
                Inheto.DeserializationOptions effectiveOptions)
            {
                if (record is null || context is not ulong)
                    return record;

                object boxed = record;
                Inheto.DeserializationOptions sourceOptions;
                lock (sync)
                    sourceOptions = preparedSources.TryGetValue(effectiveOptions, out Inheto.DeserializationOptions? source)
                        ? source
                        : effectiveOptions;
                Definition[] current = snapshot;
                for (int index = 0; index < current.Length; index++)
                {
                    Definition definition = current[index];
                    if (!definition.UsesExplicitCodec ||
                        sourceOptions.ExcludedMembers?.Contains(definition.PropPath) == true)
                    {
                        continue;
                    }

                    if (!TryResolveWritableMember(
                        requestedType,
                        definition,
                        out string matchedPath,
                        out MemberInfo[] members,
                        out Type valueType))
                    {
                        continue;
                    }

                    if (!TryReadSemanticValue(binary, (ulong)context, definition, sourceOptions, out object? semanticValue))
                        continue;
                    object? projectedValue = ReprojectValue(semanticValue, valueType, sourceOptions);
                    SetMemberValue(boxed, members, projectedValue, matchedPath);
                }
                return boxed;
            }

            private static bool TryReadSemanticValue(
                Inheto.InhetoBinary binary,
                ulong recordID,
                Definition definition,
                Inheto.DeserializationOptions sourceOptions,
                out object? value)
            {
                byte[]? encodedSource = null;
                ReadOnlySpan<byte> payload;
                if (definition.Source == LinkSource.RecordID)
                {
                    encodedSource = Inheto.InhetoBinaryCodec.Encode(recordID);
                    payload = encodedSource;
                }
                else if (binary.TryReadCustomBinaryPayload(definition.SourcePath, out ReadOnlyMemory<byte> customPayload))
                {
                    payload = customPayload.Span;
                }
                else
                {
                    if (!binary.PropExists(definition.SourcePath))
                    {
                        value = null;
                        return false;
                    }
                    object? sourceValue = binary.ReadPropAs(
                        definition.SourceType!,
                        definition.SourcePath,
                        sourceOptions);
                    encodedSource = Inheto.InhetoBinaryCodec.Encode(sourceValue);
                    payload = encodedSource;
                }
                value = definition.Reader!(payload);
                return true;
            }

            private void RefreshSnapshot()
            {
                snapshot = new List<Definition>(definitions.Values).ToArray();
                var nextTargets = new Dictionary<string, Definition>(StringComparer.Ordinal);
                for (int index = 0; index < snapshot.Length; index++)
                    nextTargets.Add(snapshot[index].PropPath, snapshot[index]);
                for (int index = 0; index < snapshot.Length; index++)
                    for (int alias = 0; alias < snapshot[index].Aliases.Length; alias++)
                        nextTargets.TryAdd(snapshot[index].Aliases[alias], snapshot[index]);
                targets = nextTargets;
                readPolicies.Clear();
                preparedOptions.Clear();
                preparedSources.Clear();
            }

            private Inheto.DeserializationOptions PrepareMaterializationOptions(
                Type requestedType,
                object? context,
                Inheto.DeserializationOptions effectiveOptions)
            {
                if (context is not ulong)
                    return effectiveOptions;
                lock (sync)
                {
                    var key = (effectiveOptions, requestedType);
                    if (preparedOptions.TryGetValue(key, out Inheto.DeserializationOptions? prepared))
                        return prepared;

                    HashSet<string>? exclusions = effectiveOptions.ExcludedMembers is null
                        ? null
                        : new HashSet<string>(effectiveOptions.ExcludedMembers, StringComparer.Ordinal);
                    Definition[] current = snapshot;
                    for (int index = 0; index < current.Length; index++)
                    {
                        Definition definition = current[index];
                        if (!definition.UsesExplicitCodec ||
                            !TryResolveWritableMember(requestedType, definition, out string matchedPath, out _, out _))
                        {
                            continue;
                        }
                        (exclusions ??= new HashSet<string>(StringComparer.Ordinal)).Add(matchedPath);
                    }

                    prepared = exclusions is null
                        ? effectiveOptions
                        : effectiveOptions.ForkExcluding(exclusions);
                    preparedOptions.Add(key, prepared);
                    if (!ReferenceEquals(prepared, effectiveOptions))
                        preparedSources[prepared] = effectiveOptions;
                    return prepared;
                }
            }

            private static bool TryResolveWritableMember(
                Type rootType,
                Definition definition,
                out string matchedPath,
                out MemberInfo[] members,
                out Type valueType)
            {
                if (TryResolveWritableMember(rootType, definition.PropPath, out members, out valueType))
                {
                    matchedPath = definition.PropPath;
                    return true;
                }
                for (int index = 0; index < definition.Aliases.Length; index++)
                    if (TryResolveWritableMember(rootType, definition.Aliases[index], out members, out valueType))
                    {
                        matchedPath = definition.Aliases[index];
                        return true;
                    }
                matchedPath = string.Empty;
                members = Array.Empty<MemberInfo>();
                valueType = typeof(object);
                return false;
            }

            private static bool TryResolveWritableMember(
                Type rootType,
                string propPath,
                out MemberInfo[] members,
                out Type valueType)
                => TryResolveMember(rootType, propPath, requireWritable: true, out members, out valueType);

            private static bool TryResolveReadableMember(
                Type rootType,
                string propPath,
                out MemberInfo[] members,
                out Type valueType)
                => TryResolveMember(rootType, propPath, requireWritable: false, out members, out valueType);

            private static bool TryResolveMember(
                Type rootType,
                string propPath,
                bool requireWritable,
                out MemberInfo[] members,
                out Type valueType)
            {
                string[] segments = propPath.TrimStart('.').Split('.', StringSplitOptions.RemoveEmptyEntries);
                var resolved = new MemberInfo[segments.Length];
                Type current = rootType;
                for (int index = 0; index < segments.Length; index++)
                {
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
                    MemberInfo? member = current.GetProperty(segments[index], flags) ??
                        (MemberInfo?)current.GetField(segments[index], flags);
                    if (member is null)
                    {
                        members = Array.Empty<MemberInfo>();
                        valueType = typeof(object);
                        return false;
                    }
                    if (requireWritable &&
                        index == segments.Length - 1 &&
                        member is PropertyInfo finalProperty &&
                        finalProperty.SetMethod is null)
                    {
                        members = Array.Empty<MemberInfo>();
                        valueType = typeof(object);
                        return false;
                    }
                    resolved[index] = member;
                    current = member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;
                }
                members = resolved;
                valueType = current;
                return resolved.Length != 0;
            }

            private static object? GetMemberValue(object root, MemberInfo[] members, string propPath)
            {
                object? current = root;
                for (int index = 0; index < members.Length; index++)
                {
                    if (current is null)
                        throw new InvalidOperationException(
                            $"Property link '{propPath}' could not traverse null member '{members[index - 1].Name}'.");
                    current = members[index] is PropertyInfo property
                        ? property.GetValue(current)
                        : ((FieldInfo)members[index]).GetValue(current);
                }
                return current;
            }

            private static void SetMemberValue(object root, MemberInfo[] members, object? value, string propPath)
            {
                object current = root;
                for (int index = 0; index < members.Length - 1; index++)
                {
                    object? next = members[index] is PropertyInfo property
                        ? property.GetValue(current)
                        : ((FieldInfo)members[index]).GetValue(current);
                    if (next is null)
                        throw new InvalidOperationException(
                            $"Property link '{propPath}' could not traverse null member '{members[index].Name}'.");
                    current = next;
                }

                MemberInfo target = members[^1];
                if (target is PropertyInfo targetProperty)
                    targetProperty.SetValue(current, value);
                else
                    ((FieldInfo)target).SetValue(current, value);
            }

            private static object? ReprojectValue(
                object? value,
                Type targetType,
                Inheto.DeserializationOptions options)
            {
                if (value is null)
                    return targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null
                        ? Activator.CreateInstance(targetType)
                        : null;
                if (targetType.IsInstanceOfType(value))
                    return value;
                if (value is IEnumerable source && value is not string && TryGetCollectionElementType(targetType, out Type elementType))
                {
                    Type listType = typeof(List<>).MakeGenericType(elementType);
                    IList projected = (IList)Activator.CreateInstance(listType)!;
                    foreach (object? item in source)
                        projected.Add(ReprojectSingleValue(item, elementType, options));
                    return BuildCollection(projected, listType, targetType, elementType);
                }
                return ReprojectSingleValue(value, targetType, options);
            }

            private static object? ReprojectSingleValue(
                object? value,
                Type targetType,
                Inheto.DeserializationOptions options)
            {
                if (value is null || targetType.IsInstanceOfType(value))
                    return value;
                if (Inheto.FastCoerce.TryCoerce(value, targetType, out object? coerced))
                    return coerced;
                Inheto.InhetoBinary binary = Inheto.InhetoBinary.FromObject(value, null, options);
                return binary.ToObject(targetType, options);
            }

            private static object BuildCollection(IList values, Type listType, Type targetType, Type elementType)
            {
                if (targetType.IsArray)
                {
                    Array array = Array.CreateInstance(elementType, values.Count);
                    values.CopyTo(array, 0);
                    return array;
                }
                if (targetType.IsAssignableFrom(listType))
                    return values;
                Type enumerableType = typeof(IEnumerable<>).MakeGenericType(elementType);
                ConstructorInfo? constructor = targetType.GetConstructor(new[] { enumerableType });
                if (constructor is not null)
                    return constructor.Invoke(new object[] { values });
                object? target = targetType.IsInterface || targetType.IsAbstract
                    ? null
                    : Activator.CreateInstance(targetType);
                if (target is IList targetList)
                {
                    for (int index = 0; index < values.Count; index++)
                        targetList.Add(values[index]);
                    return targetList;
                }
                throw new NotSupportedException(
                    $"Link projection collection '{targetType.FullName}' must be an array, accept IEnumerable<{elementType.Name}>, or implement IList.");
            }

            private static bool TryGetCollectionElementType(Type collectionType, out Type elementType)
            {
                if (collectionType.IsArray)
                {
                    elementType = collectionType.GetElementType()!;
                    return true;
                }
                if (collectionType.IsGenericType && collectionType.GetGenericArguments().Length == 1)
                {
                    elementType = collectionType.GetGenericArguments()[0];
                    return true;
                }
                foreach (Type contract in collectionType.GetInterfaces())
                    if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    {
                        elementType = contract.GetGenericArguments()[0];
                        return true;
                    }
                elementType = typeof(object);
                return false;
            }

            private string RequireRegistered(string propPath)
            {
                string path = NormalizeProjectionPath(propPath, nameof(propPath));
                lock (sync)
                    if (!definitions.ContainsKey(path))
                        throw new ArgumentException($"Property path '{path}' is not registered in this store's Links collection.", nameof(propPath));
                return path;
            }

            private static string NormalizeProjectionPath(string propPath, string paramName)
            {
                if (string.IsNullOrWhiteSpace(propPath))
                    throw new ArgumentException("A link property path is required.", paramName);
                string path = propPath.Trim();
                if (path[0] != '.')
                    path = "." + path;
                string[] segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 0 || path.Contains('(') || path.Contains(')') || path.Contains('!') || path.Contains('#'))
                    throw new ArgumentException($"Link path '{propPath}' must contain property or field segments only.", paramName);
                for (int index = 0; index < segments.Length; index++)
                    if (string.IsNullOrWhiteSpace(segments[index]))
                        throw new ArgumentException($"Link path '{propPath}' contains an empty member segment.", paramName);
                return "." + string.Join('.', segments);
            }

            private static string Normalize(string propPath, out Type valueType)
            {
                if (string.IsNullOrWhiteSpace(propPath))
                    throw new ArgumentException("A link property path is required.", nameof(propPath));
                Inheto.PropPathCheck check = Inheto.InhetoBinary.CheckPropPath(typeof(T), propPath);
                if (!check.IsValid || check.RequiresClrContinuation || check.ContainsMethodInvocation || check.ResultType is null)
                {
                    throw new ArgumentException(
                        $"Link path '{propPath}' must resolve to a directly serializable property or field on '{typeof(T).FullName}'.",
                        nameof(propPath));
                }
                valueType = check.ResultType;
                return check.CanonicalPath;
            }

            private static string[] NormalizeAliases(string canonicalPath, string[] aliases)
            {
                if (aliases.Length == 0)
                    return Array.Empty<string>();
                var unique = new HashSet<string>(StringComparer.Ordinal);
                for (int index = 0; index < aliases.Length; index++)
                {
                    string alias = aliases[index]?.Trim() ?? string.Empty;
                    if (alias.Length == 0)
                        throw new ArgumentException("Link aliases cannot be null or blank.", nameof(aliases));
                    if (alias[0] != '.')
                        alias = "." + alias;
                    if (alias.Contains('(') || alias.Contains(')') || alias.Contains('!') || alias.Contains('#'))
                        throw new ArgumentException($"Link alias '{alias}' must contain property or field segments only.", nameof(aliases));
                    if (!StringComparer.Ordinal.Equals(alias, canonicalPath))
                        unique.Add(alias);
                }
                var result = new string[unique.Count];
                unique.CopyTo(result);
                Array.Sort(result, StringComparer.Ordinal);
                return result;
            }

            private static void ValidateHandling(LinkHandling handling)
            {
                if (!Enum.IsDefined(handling))
                    throw new ArgumentOutOfRangeException(nameof(handling));
            }
        }

        /// <summary>
        /// Persists a new record while applying one handling mode to every registered link.<br/>
        /// </summary>
        /// <param name="record">Record value to serialize and persist.<br/></param>
        /// <param name="handling">Handling applied to every registered link for this operation.<br/></param>
        /// <param name="state">Optional developer state stored with the record.<br/></param>
        /// <returns>The stable identity assigned to the new record.<br/></returns>
        public ulong Put(T record, LinkHandling handling, long? state = null)
            => PersistNewRecord(record, state, nameof(Put), linkWrite: Links.Plan(handling));

        /// <summary>
        /// Persists a new record while applying one handling mode only to the named links; every unlisted link remains normal.<br/>
        /// </summary>
        /// <param name="record">Record value to serialize and persist.<br/></param>
        /// <param name="handling">Handling applied to the selected registered links.<br/></param>
        /// <param name="link">First registered link property path.<br/></param>
        /// <param name="additionalLinks">Additional registered link property paths receiving the same handling.<br/></param>
        /// <returns>The stable identity assigned to the new record.<br/></returns>
        public ulong Put(T record, LinkHandling handling, string link, params string[] additionalLinks)
            => PersistNewRecord(record, null, nameof(Put), linkWrite: Links.Plan(handling, link, additionalLinks));

        /// <summary>
        /// Persists a new record with independently selected handling for one or more registered links.<br/>
        /// </summary>
        /// <param name="record">Record value to serialize and persist.<br/></param>
        /// <param name="link">First registered link write rule.<br/></param>
        /// <param name="additionalLinks">Additional registered link write rules.<br/></param>
        /// <returns>The stable identity assigned to the new record.<br/></returns>
        public ulong Put(T record, LinkWriteRule link, params LinkWriteRule[] additionalLinks)
            => PersistNewRecord(record, null, nameof(Put), linkWrite: Links.Plan(link, additionalLinks));

        /// <summary>
        /// Persists a new record without serializing registered links; because no prior payload exists, every link is stored absent.<br/>
        /// </summary>
        /// <param name="record">Record value to serialize and persist.<br/></param>
        /// <param name="state">Optional developer state stored with the record.<br/></param>
        /// <returns>The stable identity assigned to the new record.<br/></returns>
        public ulong PutNoLinks(T record, long? state = null)
            => PersistNewRecord(record, state, nameof(PutNoLinks), linkWrite: Links.Plan(LinkHandling.Preserve));

        /// <summary>
        /// Persists a new record without serializing the named links; unlisted links retain normal behavior.<br/>
        /// </summary>
        /// <param name="record">Record value to serialize and persist.<br/></param>
        /// <param name="link">First registered link property path to omit.<br/></param>
        /// <param name="additionalLinks">Additional registered link property paths to omit.<br/></param>
        /// <returns>The stable identity assigned to the new record.<br/></returns>
        public ulong PutNoLinks(T record, string link, params string[] additionalLinks)
            => PersistNewRecord(record, null, nameof(PutNoLinks), linkWrite: Links.Plan(LinkHandling.Preserve, link, additionalLinks));

        /// <summary>
        /// Persists a new record with every registered link stored absent.<br/>
        /// </summary>
        /// <param name="record">Record value to serialize and persist.<br/></param>
        /// <param name="state">Optional developer state stored with the record.<br/></param>
        /// <returns>The stable identity assigned to the new record.<br/></returns>
        public ulong PutClearLinks(T record, long? state = null)
            => PersistNewRecord(record, state, nameof(PutClearLinks), linkWrite: Links.Plan(LinkHandling.Clear));

        /// <summary>
        /// Persists a new record with only the named links stored absent; unlisted links retain normal behavior.<br/>
        /// </summary>
        /// <param name="record">Record value to serialize and persist.<br/></param>
        /// <param name="link">First registered link property path to clear.<br/></param>
        /// <param name="additionalLinks">Additional registered link property paths to clear.<br/></param>
        /// <returns>The stable identity assigned to the new record.<br/></returns>
        public ulong PutClearLinks(T record, string link, params string[] additionalLinks)
            => PersistNewRecord(record, null, nameof(PutClearLinks), linkWrite: Links.Plan(LinkHandling.Clear, link, additionalLinks));

        /// <summary>
        /// Replaces one existing record while applying one handling mode to every registered link.<br/>
        /// </summary>
        /// <param name="recordID">Stable identity of the record to replace.<br/></param>
        /// <param name="record">Replacement record value.<br/></param>
        /// <param name="handling">Handling applied to every registered link.<br/></param>
        /// <param name="state">Optional developer state stored with the replacement.<br/></param>
        /// <returns>The stable identity of the replaced record.<br/></returns>
        public ulong Put(ulong recordID, T record, LinkHandling handling, long? state = null)
            => PutLinked(recordID, record, state, nameof(Put), Links.Plan(handling));

        /// <summary>
        /// Replaces one existing record while applying one handling mode only to the named registered links.<br/>
        /// Every unlisted link retains normal hook behavior.<br/>
        /// </summary>
        /// <param name="recordID">Stable identity of the record to replace.<br/></param>
        /// <param name="record">Replacement record value.<br/></param>
        /// <param name="handling">Handling applied to the selected registered links.<br/></param>
        /// <param name="link">First registered link property path.<br/></param>
        /// <param name="additionalLinks">Additional registered link property paths receiving the same handling.<br/></param>
        /// <returns>The stable identity of the replaced record.<br/></returns>
        public ulong Put(
            ulong recordID,
            T record,
            LinkHandling handling,
            string link,
            params string[] additionalLinks)
            => PutLinked(recordID, record, null, nameof(Put), Links.Plan(handling, link, additionalLinks));

        /// <summary>
        /// Replaces one existing record while preserving every registered stored link payload without invoking its serializer.<br/>
        /// </summary>
        /// <param name="recordID">Stable identity of the record to replace.<br/></param>
        /// <param name="record">Replacement record value whose registered link values are ignored.<br/></param>
        /// <param name="state">Optional developer state stored with the replacement.<br/></param>
        /// <returns>The stable identity of the replaced record.<br/></returns>
        public ulong PutNoLinks(ulong recordID, T record, long? state = null)
            => PutLinked(recordID, record, state, nameof(PutNoLinks), Links.Plan(LinkHandling.Preserve));

        /// <summary>
        /// Replaces one existing record while preserving only the named registered link payloads without invoking their serializers.<br/>
        /// Every unlisted link retains normal hook behavior.<br/>
        /// </summary>
        /// <param name="recordID">Stable identity of the record to replace.<br/></param>
        /// <param name="record">Replacement record value.<br/></param>
        /// <param name="link">First registered link property path to preserve.<br/></param>
        /// <param name="additionalLinks">Additional registered link property paths to preserve.<br/></param>
        /// <returns>The stable identity of the replaced record.<br/></returns>
        public ulong PutNoLinks(
            ulong recordID,
            T record,
            string link,
            params string[] additionalLinks)
            => PutLinked(recordID, record, null, nameof(PutNoLinks), Links.Plan(LinkHandling.Preserve, link, additionalLinks));

        /// <summary>
        /// Replaces one existing record while clearing every registered link member from storage.<br/>
        /// </summary>
        /// <param name="recordID">Stable identity of the record to replace.<br/></param>
        /// <param name="record">Replacement record value whose registered link values are ignored.<br/></param>
        /// <param name="state">Optional developer state stored with the replacement.<br/></param>
        /// <returns>The stable identity of the replaced record.<br/></returns>
        public ulong PutClearLinks(ulong recordID, T record, long? state = null)
            => PutLinked(recordID, record, state, nameof(PutClearLinks), Links.Plan(LinkHandling.Clear));

        /// <summary>
        /// Replaces one existing record while clearing only the named registered links from storage.<br/>
        /// Every unlisted link retains normal hook behavior and the caller-owned object is never modified.<br/>
        /// </summary>
        /// <param name="recordID">Stable identity of the record to replace.<br/></param>
        /// <param name="record">Replacement record value.<br/></param>
        /// <param name="link">First registered link property path to clear.<br/></param>
        /// <param name="additionalLinks">Additional registered link property paths to clear.<br/></param>
        /// <returns>The stable identity of the replaced record.<br/></returns>
        public ulong PutClearLinks(
            ulong recordID,
            T record,
            string link,
            params string[] additionalLinks)
            => PutLinked(recordID, record, null, nameof(PutClearLinks), Links.Plan(LinkHandling.Clear, link, additionalLinks));

        /// <summary>
        /// Replaces one existing record while applying independently selected handling to one or more registered links.<br/>
        /// </summary>
        /// <param name="recordID">Stable identity of the record to replace.<br/></param>
        /// <param name="record">Replacement record value.<br/></param>
        /// <param name="link">First registered link write rule.<br/></param>
        /// <param name="additionalLinks">Additional registered link write rules.<br/></param>
        /// <returns>The stable identity of the replaced record.<br/></returns>
        public ulong Put(ulong recordID, T record, LinkWriteRule link, params LinkWriteRule[] additionalLinks)
            => PutLinked(recordID, record, null, nameof(Put), Links.Plan(link, additionalLinks));

        private ulong PutLinked(
            ulong recordID,
            T record,
            long? state,
            string operationName,
            LinkWritePlan? links)
        {
            EnsureRecordBelongsToCurrentStore(recordID, nameof(recordID));
            if (!rbase.data.RecordExists(recordID))
                throw new InvalidOperationException("recordID does not exist in this store; cannot overwrite.");
            return PersistRecord(recordID, record, state, operationName, isNewRecord: false, linkWrite: links);
        }
    }
}
