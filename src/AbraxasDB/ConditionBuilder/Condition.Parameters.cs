using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using LibraDex;

namespace AbraxasDB;

/// <summary>
/// Describes one explicit mutable value used by an Abraxas condition.<br/>
/// Function-based deferred expressions are intentionally not parameters and therefore do not implement this contract.<br/>
/// </summary>
public interface IConditionParameter
{
    /// <summary>Gets the optional case-sensitive name used for condition-local lookup.<br/></summary>
    string? Name { get; }

    /// <summary>Gets the exact CLR type accepted by this parameter.<br/></summary>
    Type ValueType { get; }

    /// <summary>Gets or sets the value visible to the next condition execution.<br/></summary>
    object? Value { get; set; }

    /// <summary>Gets the value supplied when this parameter was created.<br/></summary>
    object? DefaultValue { get; }

    /// <summary>
    /// Restores the value supplied when this parameter was created.<br/>
    /// The condition structure and parameter identity remain unchanged.<br/>
    /// </summary>
    void Reset();
}

/// <summary>
/// Holds one strongly typed explicit condition parameter and its original reset value.<br/>
/// Repeated references share the existing once-per-execution snapshot behavior without exposing a storage-component type.<br/>
/// The caller must coordinate value or collection mutations that can overlap execution preparation; this holder does not add locking.<br/>
/// </summary>
/// <typeparam name="TValue">Exact selector or criterion value type.<br/></typeparam>
public sealed class ConditionParameter<TValue> : IConditionParameter
{
    private readonly TValue defaultValue;
    private readonly LibraDexParameter<TValue> executionParameter;

    /// <summary>
    /// Creates an Abraxas parameter and its matching LibraDex execution identity.<br/>
    /// The supplied value is retained independently as the parameter's reset target.<br/>
    /// </summary>
    /// <param name="value">Initial current value and future reset target.<br/></param>
    /// <param name="name">Optional case-sensitive lookup name.<br/></param>
    internal ConditionParameter(TValue value, string? name)
        : this(LibraDexParameter.Create(value, name), value)
    {
    }

    /// <summary>
    /// Retains the internal execution identity and the original reset target in one caller-owned Abraxas parameter.<br/>
    /// Conditions reference this holder directly, so no reverse identity map or separately allocated adapter is required.<br/>
    /// </summary>
    /// <param name="executionParameter">Existing execution parameter read by condition materialization.<br/></param>
    /// <param name="defaultValue">Value restored by <see cref="Reset"/>.<br/></param>
    internal ConditionParameter(
        LibraDexParameter<TValue> executionParameter,
        TValue defaultValue)
    {
        this.executionParameter = executionParameter ?? throw new ArgumentNullException(nameof(executionParameter));
        this.defaultValue = defaultValue;
    }

    /// <summary>Gets the optional case-sensitive name used for condition-local lookup.<br/></summary>
    public string? Name => executionParameter.Name;

    /// <summary>Gets the exact CLR type accepted by this parameter.<br/></summary>
    public Type ValueType => typeof(TValue);

    /// <summary>
    /// Gets or sets the strongly typed value visible to the next condition execution.<br/>
    /// An in-flight execution continues using the coherent value snapshot captured when that execution began.<br/>
    /// </summary>
    public TValue Value
    {
        // Keep the public boundary from adding a forwarding call in frequently updated typed parameters.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => executionParameter.Value;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => executionParameter.Value = value;
    }

    /// <summary>Gets the strongly typed value supplied when this parameter was created.<br/></summary>
    public TValue DefaultValue => defaultValue;

    object? IConditionParameter.Value
    {
        get => Value;
        set
        {
            if (value is TValue typed)
            {
                Value = typed;
                return;
            }

            if (value is null && default(TValue) is null)
            {
                Value = (TValue)value!;
                return;
            }

            throw new ArgumentException(
                $"Condition parameter '{Name ?? "<unnamed>"}' requires a value of type '{typeof(TValue).FullName}'.",
                nameof(value));
        }
    }

    object? IConditionParameter.DefaultValue => DefaultValue;

    /// <summary>
    /// Restores the strongly typed value supplied when this parameter was created.<br/>
    /// The parameter remains attached everywhere its identity is used.<br/>
    /// </summary>
    public void Reset()
        => Value = defaultValue;

    internal LibraDexParameter<TValue> ExecutionParameter => executionParameter;
}

/// <summary>
/// Creates caller-owned explicit parameters that may be reused by any compatible Abraxas condition.<br/>
/// A parameter appears in a completed condition's collection only after that condition structurally references it.<br/>
/// </summary>
public sealed class StoreParameterFactory
{
    /// <summary>
    /// Initializes the stateless store-level explicit parameter factory.<br/>
    /// Record stores expose this constructor indirectly through their <c>Parameters</c> property.<br/>
    /// </summary>
    internal StoreParameterFactory()
    {
    }

    /// <summary>
    /// Creates one strongly typed explicit parameter whose current value is captured once per condition execution.<br/>
    /// The optional name becomes available through every condition that structurally references this parameter.<br/>
    /// </summary>
    /// <typeparam name="TValue">Exact selector or criterion value type.<br/></typeparam>
    /// <param name="value">Initial value and reset value for the parameter.<br/></param>
    /// <param name="name">Optional case-sensitive condition-local lookup name.<br/></param>
    /// <returns>A caller-owned reusable condition parameter.<br/></returns>
    public ConditionParameter<TValue> Create<TValue>(TValue value, string? name = null)
        => ConditionParameterIdentity.Create(value, name);

    /// <summary>
    /// Creates one strongly typed enumerable parameter whose elements are frozen when each condition execution begins.<br/>
    /// Assigning a different enumerable changes the next execution, while reset restores the originally supplied enumerable reference.<br/>
    /// </summary>
    /// <typeparam name="TValue">Exact membership element type.<br/></typeparam>
    /// <param name="values">Initial enumerable and reset value for the parameter.<br/></param>
    /// <param name="name">Optional case-sensitive condition-local lookup name.<br/></param>
    /// <returns>A caller-owned reusable enumerable condition parameter.<br/></returns>
    public ConditionParameter<IEnumerable<TValue>> Create<TValue>(
        IEnumerable<TValue> values,
        string? name = null)
        => ConditionParameterIdentity.CreateSet(values, name);
}

/// <summary>
/// Exposes the explicit parameters structurally referenced by one Abraxas condition.<br/>
/// Entries are unique by parameter identity and ordered by their first left-to-right appearance in the condition.<br/>
/// </summary>
public sealed class ConditionParameterCollection
{
    private readonly List<IConditionParameter> ordered = new();
    private readonly Dictionary<string, IConditionParameter> named = new(StringComparer.Ordinal);

    internal ConditionParameterCollection()
    {
    }

    /// <summary>Gets the number of unique explicit parameters currently referenced by the condition.<br/></summary>
    public int Count => ordered.Count;

    /// <summary>
    /// Gets a named explicit parameter using exact ordinal matching.<br/>
    /// A missing name throws <see cref="KeyNotFoundException"/>; use <see cref="TryGet"/> for conditional access.<br/>
    /// </summary>
    /// <param name="name">Exact parameter name.<br/></param>
    public IConditionParameter this[string name] => Get(name);

    /// <summary>
    /// Creates one strongly typed parameter for use while authoring this condition.<br/>
    /// The parameter joins this collection at the position where the condition first structurally references it; creation alone does not add an unused entry.<br/>
    /// </summary>
    /// <typeparam name="TValue">Exact selector or criterion value type.<br/></typeparam>
    /// <param name="value">Initial value and reset value for the parameter.<br/></param>
    /// <param name="name">Optional case-sensitive lookup name.<br/></param>
    /// <returns>A strongly typed parameter accepted by the ordinary fluent condition operators.<br/></returns>
    public ConditionParameter<TValue> Create<TValue>(TValue value, string? name = null)
        => ConditionParameterIdentity.Create(value, name);

    /// <summary>
    /// Creates one strongly typed enumerable parameter for use while authoring this condition.<br/>
    /// The enumerable is execution-frozen by the existing parameter snapshot machinery and becomes visible here when first structurally referenced.<br/>
    /// </summary>
    /// <typeparam name="TValue">Exact membership element type.<br/></typeparam>
    /// <param name="values">Initial enumerable and reset value for the parameter.<br/></param>
    /// <param name="name">Optional case-sensitive lookup name.<br/></param>
    /// <returns>A strongly typed enumerable parameter accepted by membership operators.<br/></returns>
    public ConditionParameter<IEnumerable<TValue>> Create<TValue>(
        IEnumerable<TValue> values,
        string? name = null)
        => ConditionParameterIdentity.CreateSet(values, name);

    /// <summary>
    /// Gets one named parameter using exact ordinal matching.<br/>
    /// The returned non-generic contract permits convenient runtime inspection and mutation through <see cref="IConditionParameter.Value"/>.<br/>
    /// </summary>
    /// <param name="name">Exact non-empty parameter name.<br/></param>
    /// <returns>The named explicit parameter.<br/></returns>
    public IConditionParameter Get(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return named.TryGetValue(name, out var parameter)
            ? parameter
            : throw new KeyNotFoundException($"Condition parameter '{name}' was not found.");
    }

    /// <summary>
    /// Gets one named parameter while preserving its compile-time value type.<br/>
    /// A descriptive exception is thrown when the name exists with a different exact CLR type.<br/>
    /// </summary>
    /// <typeparam name="TValue">Exact parameter value type expected by the caller.<br/></typeparam>
    /// <param name="name">Exact non-empty parameter name.<br/></param>
    /// <returns>The named strongly typed parameter.<br/></returns>
    public ConditionParameter<TValue> Get<TValue>(string name)
    {
        var parameter = Get(name);
        return parameter as ConditionParameter<TValue>
            ?? throw new InvalidCastException(
                $"Condition parameter '{name}' has value type '{parameter.ValueType.FullName}', not '{typeof(TValue).FullName}'.");
    }

    /// <summary>
    /// Attempts to get one named parameter using exact ordinal matching.<br/>
    /// Unnamed parameters remain available through <see cref="Iterate"/> but cannot match this lookup.<br/>
    /// </summary>
    /// <param name="name">Exact non-empty parameter name.<br/></param>
    /// <param name="parameter">The matching parameter when found; otherwise <see langword="null"/>.<br/></param>
    /// <returns><see langword="true"/> when the condition contains the named explicit parameter.<br/></returns>
    public bool TryGet(string name, out IConditionParameter? parameter)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            parameter = null;
            return false;
        }

        return named.TryGetValue(name, out parameter);
    }

    /// <summary>
    /// Enumerates every unique explicit parameter in the order of its first left-to-right appearance in the condition.<br/>
    /// A completed condition has fixed structure, so this iterator yields directly without allocating a copied collection.<br/>
    /// </summary>
    /// <returns>The structurally ordered explicit parameter sequence.<br/></returns>
    public IEnumerable<IConditionParameter> Iterate()
    {
        for (var index = 0; index < ordered.Count; index++)
            yield return ordered[index];
    }

    /// <summary>
    /// Restores every explicit parameter to the value supplied when it was created.<br/>
    /// Function-based expressions are not collection entries and continue resolving independently at execution time.<br/>
    /// </summary>
    public void Reset()
    {
        foreach (var parameter in ordered)
            parameter.Reset();
    }

    /// <summary>
    /// Registers one structurally encountered explicit parameter unless its identity is already present.<br/>
    /// A distinct parameter using an existing name is rejected so named lookup always remains unambiguous.<br/>
    /// </summary>
    /// <param name="parameter">Explicit parameter encountered while walking the condition structure.<br/></param>
    internal void Register(IConditionParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        if (ordered.Any(existing => ReferenceEquals(existing, parameter)))
            return;

        if (parameter.Name is { } name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A condition parameter name cannot be whitespace.", nameof(parameter));
            if (named.TryGetValue(name, out var existing) && !ReferenceEquals(existing, parameter))
                throw new InvalidOperationException($"Condition parameter name '{name}' is duplicated.");
            named.Add(name, parameter);
        }

        ordered.Add(parameter);
    }

    /// <summary>
    /// Registers a structurally ordered parameter sequence while retaining identity deduplication and name validation.<br/>
    /// This is used when builders are cloned or nested as grouped condition operands.<br/>
    /// </summary>
    /// <param name="parameters">Parameters ordered by their first appearance in the source condition.<br/></param>
    internal void RegisterRange(IEnumerable<IConditionParameter> parameters)
    {
        foreach (var parameter in parameters)
            Register(parameter);
    }
}

internal static class ConditionParameterIdentity
{
    /// <summary>
    /// Creates one ordinary explicit parameter whose caller-owned identity is retained directly during structural discovery.<br/>
    /// </summary>
    internal static ConditionParameter<TValue> Create<TValue>(TValue value, string? name)
    {
        ValidateName(name);
        return new ConditionParameter<TValue>(value, name);
    }

    /// <summary>
    /// Creates one enumerable parameter using LibraDex's execution-time set snapshot semantics.<br/>
    /// </summary>
    internal static ConditionParameter<IEnumerable<TValue>> CreateSet<TValue>(IEnumerable<TValue> values, string? name)
    {
        ArgumentNullException.ThrowIfNull(values);
        ValidateName(name);
        var executionParameter = LibraDexParameter.Set(values, name);
        return new ConditionParameter<IEnumerable<TValue>>(executionParameter, values);
    }

    /// <summary>
    /// Validates the optional public lookup name at parameter creation rather than deferring failure until condition use.<br/>
    /// </summary>
    private static void ValidateName(string? name)
    {
        if (name is not null && string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A condition parameter name cannot be whitespace.", nameof(name));
    }
}

internal static class ConditionParameterDiscovery
{
    /// <summary>
    /// Walks one newly added condition fragment and registers only explicit parameter objects in structural field order.<br/>
    /// Delegates are never inspected or invoked, preserving function expressions as separate execution-time behavior.<br/>
    /// </summary>
    internal static void RegisterFromFragment(ConditionParameterCollection destination, object fragment)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(fragment);
        Visit(destination, fragment, new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    /// <summary>
    /// Recursively visits supported Abraxas condition nodes, selectors, arrays, and collections without following application objects or delegates.<br/>
    /// Reference tracking prevents cycles and repeated parameter identities are ignored by the destination collection.<br/>
    /// </summary>
    private static void Visit(
        ConditionParameterCollection destination,
        object? value,
        HashSet<object> visited)
    {
        if (value is null || value is string || value is Type || value is Delegate)
            return;
        if (value is IConditionParameter parameter)
        {
            destination.Register(parameter);
            return;
        }
        if (value is IExplicitConditionParameterSource source)
        {
            if (!visited.Add(value))
                return;
            if (source.ExplicitParameter is { } explicitParameter)
                destination.Register(explicitParameter);
            Visit(destination, source.NestedParameterSource, visited);
            return;
        }
        if (value is IConditionParameterContainer container)
        {
            destination.RegisterRange(container.ExplicitParameters.Iterate());
            return;
        }
        if (value is IEnumerable enumerable)
        {
            if (!visited.Add(value))
                return;
            foreach (var item in enumerable)
                Visit(destination, item, visited);
            return;
        }

        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || type.Assembly != typeof(ConditionParameterDiscovery).Assembly)
            return;
        if (!visited.Add(value))
            return;

        foreach (var field in GetInstanceFields(type))
            Visit(destination, field.GetValue(value), visited);
    }

    /// <summary>
    /// Enumerates instance fields from base type to derived type and by metadata declaration order within each type.<br/>
    /// This provides deterministic structural discovery without repetitive plumbing in every existing condition operation.<br/>
    /// </summary>
    private static IEnumerable<FieldInfo> GetInstanceFields(Type type)
    {
        var hierarchy = new Stack<Type>();
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
            hierarchy.Push(current);

        while (hierarchy.Count > 0)
        {
            foreach (var field in hierarchy.Pop()
                         .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                         .OrderBy(static field => field.MetadataToken))
            {
                if (!typeof(Delegate).IsAssignableFrom(field.FieldType))
                {
                    yield return field;
                }
            }
        }
    }
}

internal interface IExplicitConditionParameterSource
{
    IConditionParameter? ExplicitParameter { get; }

    object? NestedParameterSource { get; }
}

internal interface IConditionParameterContainer
{
    ConditionParameterCollection ExplicitParameters { get; }
}
