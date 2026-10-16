using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace AbraxasShowcase;

/// <summary>Serializable description of one live C# value without serializing the value itself.<br/></summary>
internal sealed record ObjectNodeDescriptor(
    string SessionId,
    string NodeId,
    string Name,
    string DeclaredType,
    string RuntimeType,
    string Kind,
    string Access,
    string State,
    string Summary,
    bool Expandable,
    string? ReferenceTo,
    string? InspectionDetails = null);

/// <summary>Measured result of expanding one retained C# value through a registered viewer.<br/></summary>
internal sealed record ObjectNodeExpansion(
    string SessionId,
    string NodeId,
    string Viewer,
    IReadOnlyList<ObjectNodeDescriptor> Children,
    bool Truncated,
    string? Note,
    double ElapsedMilliseconds);

/// <summary>
/// Owns the single active bounded inspection session used by the showcase result panel.<br/>
/// Starting another scenario or regenerating the laboratory expires every prior node identifier and releases retained values.<br/>
/// </summary>
internal sealed class ObjectInspectionManager : IDisposable
{
    private readonly object sync = new();
    private readonly Dictionary<string, ObjectInspectionSession> sessions = new(StringComparer.Ordinal);

    /// <summary>Starts a new live session rooted in the actual scenario value and expires the previous session.<br/></summary>
    /// <param name="value">Actual C# value returned by the compiled scenario.<br/></param>
    /// <param name="declaredType">Declared .NET result type at the scenario boundary.<br/></param>
    /// <returns>A root descriptor containing types and expandability but no eager member serialization.<br/></returns>
    internal ObjectNodeDescriptor Begin(object? value, Type declaredType)
    {
        lock (sync)
        {
            ResetCore();
            return AddCore(value, declaredType, "Result", "Scenario result");
        }
    }

    /// <summary>Adds an independently expandable live root without expiring other rows on the current browser page.<br/></summary>
    /// <param name="value">Actual C# object hydrated only after the user requests this row.<br/></param>
    /// <param name="declaredType">Store record type shown at the inspection boundary.<br/></param>
    /// <param name="name">Root label, normally the Abraxas RecordID.<br/></param>
    /// <param name="access">Truthful description of the retrieval that produced the object.<br/></param>
    /// <returns>A root descriptor whose later expansions read from the retained C# object.<br/></returns>
    internal ObjectNodeDescriptor Add(object? value, Type declaredType, string name, string access)
    {
        lock (sync)
        {
            if (sessions.Count >= 100) ResetCore();
            return AddCore(value, declaredType, name, access);
        }
    }

    /// <summary>Expands one node through the viewer selected for its retained runtime value.<br/></summary>
    /// <param name="sessionId">Opaque active-session identifier supplied by the root descriptor.<br/></param>
    /// <param name="nodeId">Opaque node identifier supplied by C#.<br/></param>
    /// <returns>Bounded typed children measured independently from scenario execution.<br/></returns>
    internal ObjectNodeExpansion Expand(string sessionId, string nodeId)
    {
        lock (sync)
        {
            if (!sessions.TryGetValue(sessionId, out ObjectInspectionSession? session))
                throw new InvalidOperationException("This inspection session has expired. Run the scenario again.");
            return session.Expand(nodeId);
        }
    }

    /// <summary>Expires the active session before the shared RecordBase is regenerated.<br/></summary>
    internal void Reset()
    {
        lock (sync)
        {
            ResetCore();
        }
    }

    /// <summary>Releases all retained inspection references.<br/></summary>
    public void Dispose() => Reset();

    /// <summary>Creates and registers one retained inspection session while the caller owns synchronization.<br/></summary>
    private ObjectNodeDescriptor AddCore(object? value, Type declaredType, string name, string access)
    {
        var session = new ObjectInspectionSession(value, declaredType, name, access);
        sessions.Add(session.Id, session);
        return session.Root;
    }

    /// <summary>Releases every retained session while the caller owns synchronization.<br/></summary>
    private void ResetCore()
    {
        foreach (ObjectInspectionSession session in sessions.Values) session.Dispose();
        sessions.Clear();
    }
}

/// <summary>One bounded graph of retained C# values and lazily produced descriptors.<br/></summary>
internal sealed class ObjectInspectionSession : IDisposable
{
    private const int MaximumDepth = 12;
    private const int MaximumChildren = 200;
    private readonly Dictionary<string, InspectionTarget> targets = new(StringComparer.Ordinal);
    private readonly Dictionary<object, string> references = new(ReferenceEqualityComparer.Instance);
    private readonly IReadOnlyList<IObjectNodeViewer> viewers;
    private int nextNode;

    internal string Id { get; } = Guid.NewGuid().ToString("N");
    internal ObjectNodeDescriptor Root { get; }

    /// <summary>Creates an inspection session without reading any property, field, or collection element from the root.<br/></summary>
    /// <param name="value">Actual root retained strongly for the session lifetime.<br/></param>
    /// <param name="declaredType">Declared type shown independently from the runtime type.<br/></param>
    internal ObjectInspectionSession(object? value, Type declaredType, string name = "Result", string access = "Scenario result")
    {
        viewers = [new ScalarNodeViewer(), new DictionaryNodeViewer(), new EnumerableNodeViewer(), new ObjectMemberNodeViewer()];
        Root = Register(name, declaredType, value, access, 0, null);
    }

    /// <summary>Selects the registered viewer for one retained node and caches its first real expansion.<br/></summary>
    /// <param name="nodeId">Opaque node identifier within this session.<br/></param>
    /// <returns>The first measured expansion or its cached descriptors on repeated UI expansion.<br/></returns>
    internal ObjectNodeExpansion Expand(string nodeId)
    {
        if (!targets.TryGetValue(nodeId, out InspectionTarget? target)) throw new ArgumentException("Unknown or expired object node.");
        if (target.CachedExpansion is not null) return target.CachedExpansion;
        if (target.Value is null) throw new InvalidOperationException("A null value has no children.");
        if (target.Depth >= MaximumDepth) throw new InvalidOperationException($"The explorer depth limit of {MaximumDepth} has been reached.");
        IObjectNodeViewer viewer = SelectViewer(target.Value.GetType());
        if (!viewer.CanExpand) throw new InvalidOperationException("This scalar value has no children.");

        var timer = Stopwatch.StartNew();
        ViewerExpansion expanded = viewer.Expand(this, target, MaximumChildren);
        timer.Stop();
        target.CachedExpansion = new ObjectNodeExpansion(Id, nodeId, viewer.Name, expanded.Children, expanded.Truncated, expanded.Note, timer.Elapsed.TotalMilliseconds);
        return target.CachedExpansion;
    }

    /// <summary>Registers one already-read child value, detecting nulls, repeated references, and cycles before assigning a node.<br/></summary>
    /// <param name="name">Member, index, or dictionary-entry label.<br/></param>
    /// <param name="declaredType">Declared .NET type of the access boundary.<br/></param>
    /// <param name="value">Actual value read from the retained parent object.<br/></param>
    /// <param name="access">Truthful access operation used to obtain the value.<br/></param>
    /// <param name="depth">Child graph depth.<br/></param>
    /// <param name="error">Getter or enumeration failure instead of a value.<br/></param>
    /// <returns>A descriptor safe for WebView2 transport.<br/></returns>
    internal ObjectNodeDescriptor Register(string name, Type declaredType, object? value, string access, int depth, Exception? error)
    {
        string nodeId = "n" + (++nextNode).ToString(CultureInfo.InvariantCulture);
        if (error is not null)
            return new ObjectNodeDescriptor(Id, nodeId, name, TypeNameFormatter.Format(declaredType), "Unavailable", "error", access, "error", "Value unavailable", false, null, error.ToString());
        if (value is null)
            return new ObjectNodeDescriptor(Id, nodeId, name, TypeNameFormatter.Format(declaredType), "null", "null", access, "null", "null", false, null);

        Type runtimeType = value.GetType();
        IObjectNodeViewer viewer = SelectViewer(runtimeType);
        if (!runtimeType.IsValueType && runtimeType != typeof(string) && references.TryGetValue(value, out string? original))
            return new ObjectNodeDescriptor(Id, nodeId, name, TypeNameFormatter.Format(declaredType), TypeNameFormatter.Format(runtimeType), viewer.Name, access, "reference", "Reference to " + original, false, original);

        var target = new InspectionTarget(nodeId, name, declaredType, value, access, depth);
        targets.Add(nodeId, target);
        if (!runtimeType.IsValueType && runtimeType != typeof(string)) references.Add(value, nodeId);
        bool expandable = viewer.CanExpand && depth < MaximumDepth;
        string state = expandable ? "collapsed" : "value";
        return new ObjectNodeDescriptor(Id, nodeId, name, TypeNameFormatter.Format(declaredType), TypeNameFormatter.Format(runtimeType), viewer.Name, access, state, viewer.Summarize(value), expandable, null);
    }

    /// <summary>Releases retained values and cached expansions without disposing arbitrary scenario-owned domain objects.<br/></summary>
    public void Dispose()
    {
        targets.Clear();
        references.Clear();
    }

    /// <summary>Returns the first registered viewer supporting the runtime type.<br/></summary>
    /// <param name="runtimeType">Actual C# value type.<br/></param>
    /// <returns>The viewer governing summary and expansion semantics.<br/></returns>
    private IObjectNodeViewer SelectViewer(Type runtimeType) => viewers.First(viewer => viewer.Supports(runtimeType));
}

/// <summary>Retained native node state never serialized to WebView2.<br/></summary>
internal sealed class InspectionTarget
{
    internal string NodeId { get; }
    internal string Name { get; }
    internal Type DeclaredType { get; }
    internal object? Value { get; }
    internal string Access { get; }
    internal int Depth { get; }
    internal ObjectNodeExpansion? CachedExpansion { get; set; }

    /// <summary>Retains one value and its access semantics for later viewer expansion.<br/></summary>
    internal InspectionTarget(string nodeId, string name, Type declaredType, object? value, string access, int depth)
    {
        NodeId = nodeId;
        Name = name;
        DeclaredType = declaredType;
        Value = value;
        Access = access;
        Depth = depth;
    }
}

/// <summary>Internal viewer output before expansion timing is attached.<br/></summary>
internal sealed record ViewerExpansion(IReadOnlyList<ObjectNodeDescriptor> Children, bool Truncated = false, string? Note = null);

/// <summary>Extensible native renderer contract selected recursively for every retained runtime value.<br/></summary>
internal interface IObjectNodeViewer
{
    string Name { get; }
    bool CanExpand { get; }
    bool Supports(Type runtimeType);
    string Summarize(object value);
    ViewerExpansion Expand(ObjectInspectionSession session, InspectionTarget target, int maximumChildren);
}

/// <summary>Terminal viewer for values that have a stable concise textual representation.<br/></summary>
internal sealed class ScalarNodeViewer : IObjectNodeViewer
{
    public string Name => "scalar";
    public bool CanExpand => false;

    /// <summary>Returns whether the runtime type is intentionally represented as one terminal value.<br/></summary>
    public bool Supports(Type runtimeType) => runtimeType.IsPrimitive || runtimeType.IsEnum || runtimeType == typeof(string)
        || runtimeType == typeof(decimal) || runtimeType == typeof(DateTime) || runtimeType == typeof(DateTimeOffset)
        || runtimeType == typeof(DateOnly) || runtimeType == typeof(TimeOnly) || runtimeType == typeof(TimeSpan)
        || runtimeType == typeof(Guid) || runtimeType == typeof(Uri);

    /// <summary>
    /// Formats one scalar without changing its native type, adding display-only quoting, or suggesting JSON semantics.<br/>
    /// The surrounding type column already communicates whether an unquoted value is a string, character, number, or another scalar.<br/>
    /// </summary>
    public string Summarize(object value) => value switch
    {
        string text => text,
        char character => character.ToString(),
        bool boolean => boolean ? "true" : "false",
        DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
        _ => value.ToString() ?? ""
    };

    /// <summary>Rejects expansion because scalar values have no children.<br/></summary>
    public ViewerExpansion Expand(ObjectInspectionSession session, InspectionTarget target, int maximumChildren)
        => throw new InvalidOperationException("Scalar values cannot be expanded.");
}

/// <summary>Viewer for non-generic and generic dictionary values, with key/value entries kept explicit.<br/></summary>
internal sealed class DictionaryNodeViewer : IObjectNodeViewer
{
    public string Name => "dictionary";
    public bool CanExpand => true;

    /// <summary>Recognizes dictionaries before the general enumerable viewer.<br/></summary>
    public bool Supports(Type runtimeType) => typeof(IDictionary).IsAssignableFrom(runtimeType)
        || runtimeType.GetInterfaces().Any(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>));

    /// <summary>Reports the live dictionary count when its public contract exposes one.<br/></summary>
    public string Summarize(object value) => value is ICollection collection ? $"{collection.Count} entries" : "Expandable dictionary";

    /// <summary>Enumerates a bounded real dictionary view and retains each actual entry object for recursive inspection.<br/></summary>
    public ViewerExpansion Expand(ObjectInspectionSession session, InspectionTarget target, int maximumChildren)
    {
        var children = new List<ObjectNodeDescriptor>();
        int index = 0;
        try
        {
            foreach (object? item in (IEnumerable)target.Value!)
            {
                if (index == maximumChildren) return new ViewerExpansion(children, true, $"Showing the first {maximumChildren} entries.");
                Type declared = item?.GetType() ?? typeof(object);
                children.Add(session.Register($"[{index}]", declared, item, "Dictionary enumeration", target.Depth + 1, null));
                index++;
            }
            return new ViewerExpansion(children);
        }
        catch (Exception error)
        {
            children.Add(session.Register("Enumeration", typeof(object), null, "Dictionary enumeration", target.Depth + 1, error));
            return new ViewerExpansion(children, false, "The object explorer could not finish enumerating this dictionary.");
        }
    }
}

/// <summary>Viewer for arrays and other enumerable runtime values with bounded real enumeration.<br/></summary>
internal sealed class EnumerableNodeViewer : IObjectNodeViewer
{
    public string Name => "collection";
    public bool CanExpand => true;

    /// <summary>Recognizes enumerable values after strings and dictionaries have been claimed by earlier viewers.<br/></summary>
    public bool Supports(Type runtimeType) => typeof(IEnumerable).IsAssignableFrom(runtimeType);

    /// <summary>Reports a live collection count when available without enumerating deferred sources.<br/></summary>
    public string Summarize(object value) => value is ICollection collection ? $"{collection.Count} items" : "Deferred enumerable · expand to enumerate";

    /// <summary>Enumerates at most the bounded child limit and reports that this is real collection access.<br/></summary>
    public ViewerExpansion Expand(ObjectInspectionSession session, InspectionTarget target, int maximumChildren)
    {
        var children = new List<ObjectNodeDescriptor>();
        Type elementType = target.Value!.GetType().IsArray
            ? target.Value.GetType().GetElementType() ?? typeof(object)
            : target.Value.GetType().GetInterfaces()
                .FirstOrDefault(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                ?.GetGenericArguments()[0] ?? typeof(object);
        int index = 0;
        try
        {
            foreach (object? item in (IEnumerable)target.Value)
            {
                if (index == maximumChildren) return new ViewerExpansion(children, true, $"Showing the first {maximumChildren} items from real enumeration.");
                children.Add(session.Register($"[{index}]", elementType, item, "Collection enumeration", target.Depth + 1, null));
                index++;
            }
            return new ViewerExpansion(children, false, "Enumerated the retained C# collection on expansion.");
        }
        catch (Exception error)
        {
            children.Add(session.Register("Enumeration", elementType, null, "Collection enumeration", target.Depth + 1, error));
            return new ViewerExpansion(children, false, "The object explorer could not finish enumerating this collection.");
        }
    }
}

/// <summary>Fallback viewer that reads public instance properties and fields from the retained object on expansion.<br/></summary>
internal sealed class ObjectMemberNodeViewer : IObjectNodeViewer
{
    public string Name => "object";
    public bool CanExpand => true;

    /// <summary>Claims every non-terminal runtime type not handled by a more specialized viewer.<br/></summary>
    public bool Supports(Type runtimeType) => true;

    /// <summary>Describes the retained runtime instance without invoking <see cref="object.ToString"/> on arbitrary domain code.<br/></summary>
    public string Summarize(object value) => "Expandable " + TypeNameFormatter.Format(value.GetType());

    /// <summary>
    /// Invokes each public parameterless instance property getter and reads each public instance field exactly once on first expansion.<br/>
    /// Individual failures become visible child nodes and do not hide successful sibling values.<br/>
    /// </summary>
    public ViewerExpansion Expand(ObjectInspectionSession session, InspectionTarget target, int maximumChildren)
    {
        var children = new List<ObjectNodeDescriptor>();
        Type type = target.Value!.GetType();
        IEnumerable<PropertyInfo> properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetMethod is { IsStatic: false } && property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.MetadataToken);
        IEnumerable<FieldInfo> fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Where(field => !field.IsStatic)
            .OrderBy(field => field.MetadataToken);

        foreach (PropertyInfo property in properties)
        {
            if (children.Count == maximumChildren) return new ViewerExpansion(children, true, $"Showing the first {maximumChildren} public members.");
            try
            {
                object? value = property.GetValue(target.Value);
                children.Add(session.Register(property.Name, property.PropertyType, value, "Property getter", target.Depth + 1, null));
            }
            catch (TargetInvocationException error)
            {
                children.Add(session.Register(property.Name, property.PropertyType, null, "Property getter", target.Depth + 1, error.InnerException ?? error));
            }
            catch (Exception error)
            {
                children.Add(session.Register(property.Name, property.PropertyType, null, "Property getter", target.Depth + 1, error));
            }
        }

        foreach (FieldInfo field in fields)
        {
            if (children.Count == maximumChildren) return new ViewerExpansion(children, true, $"Showing the first {maximumChildren} public members.");
            try
            {
                children.Add(session.Register(field.Name, field.FieldType, field.GetValue(target.Value), "Field read", target.Depth + 1, null));
            }
            catch (Exception error)
            {
                children.Add(session.Register(field.Name, field.FieldType, null, "Field read", target.Depth + 1, error));
            }
        }
        return new ViewerExpansion(children, false, "Values were read from the retained C# object on expansion.");
    }
}

/// <summary>Formats declared and runtime types as readable fully qualified .NET type names.<br/></summary>
internal static class TypeNameFormatter
{
    /// <summary>Formats arrays, nullable types, and constructed generics without exposing compiler arity suffixes.<br/></summary>
    /// <param name="type">Declared or runtime .NET type.<br/></param>
    /// <returns>A stable readable type name suitable for the explorer header.<br/></returns>
    internal static string Format(Type type)
    {
        if (type.IsArray) return Format(type.GetElementType() ?? typeof(object)) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        Type? nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null) return Format(nullable) + "?";
        if (!type.IsGenericType) return type.FullName ?? type.Name;
        string name = type.GetGenericTypeDefinition().FullName ?? type.Name;
        int tick = name.IndexOf('`');
        if (tick >= 0) name = name[..tick];
        return name + "<" + string.Join(", ", type.GetGenericArguments().Select(Format)) + ">";
    }
}
