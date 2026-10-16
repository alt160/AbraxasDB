namespace AbraxasDB
{

    public static partial class Condition<T>
    {
    }
    /// <summary>
    /// Carries a captured value, deferred factory, or bound parameter without requiring separate fluent overloads for each form.<br/>
    /// </summary>
    /// <typeparam name="TVal">Exact CLR operand value type.<br/></typeparam>
    public class ValueOrExpression<TVal> : IValueOrExpression, IExplicitConditionParameterSource
    {
        private Func<TVal> factory;
        private readonly LibraDex.LibraDexParameter<TVal>? parameter;
        internal IBackendNeutralSelectorDescriptor? SelectorDescriptor { get; private set; }
        internal bool IsStaticValue => isStaticValue;
        internal TVal? StaticValue => staticVal;
        internal LibraDex.LibraDexParameter<TVal>? BoundParameter => parameter;
        internal IConditionParameter? ExplicitParameter { get; private set; }
        internal string? BindingName { get; private set; }
        internal bool IsExplicitBinding { get; private set; }

        /// <summary>
        /// Creates a deferred operand wrapper without evaluating the supplied factory.<br/>
        /// </summary>
        /// <param name="Factory">Non-null value factory invoked when this wrapper is read directly or materialized by a consumer.<br/></param>
        public ValueOrExpression(Func<TVal> Factory)
        {
            this.factory = Factory ?? throw new ArgumentNullException(nameof(Factory));
        }
        private ValueOrExpression(ConditionParameter<TVal> parameter)
        {
            ArgumentNullException.ThrowIfNull(parameter);
            this.parameter = parameter.ExecutionParameter;
            factory = () => parameter.Value;
            BindingName = parameter.Name;
            ExplicitParameter = parameter;
        }
        /// <summary>
        /// Captures a supplied value as an immediate condition operand.<br/>
        /// </summary>
        /// <param name="value">Immediate value retained by the wrapper.<br/></param>
        /// <returns>A typed value-or-expression operand.<br/></returns>
        public static implicit operator ValueOrExpression<TVal>(TVal value) => new(() => value) { isStaticValue = true, staticVal = value };
        /// <summary>
        /// Wraps a deferred value factory without evaluating it during conversion.<br/>
        /// </summary>
        /// <param name="value">Non-null factory read later by the operand consumer.<br/></param>
        /// <returns>A typed value-or-expression operand.<br/></returns>
        public static implicit operator ValueOrExpression<TVal>(Func<TVal> value) => new(value);
        /// <summary>
        /// Binds a caller-owned Abraxas parameter without reading or copying its current value.<br/>
        /// Authoring retains the same public identity and the internal execution key; materialization continues reading that key directly once per execution.<br/>
        /// </summary>
        /// <param name="value">Non-null typed parameter whose identity and optional name are retained.<br/></param>
        /// <returns>A condition operand backed by the existing parameter snapshot machinery.<br/></returns>
        public static implicit operator ValueOrExpression<TVal>(ConditionParameter<TVal> value) => new(value);

        IConditionParameter? IExplicitConditionParameterSource.ExplicitParameter => ExplicitParameter;
        object? IExplicitConditionParameterSource.NestedParameterSource => SelectorDescriptor;

        /// <summary>
        /// Creates an intentionally unbound condition value that becomes one required parameter when the condition is converted to a serializable descriptor.<br/>
        /// The optional name is retained for inspection and named rehydration; the zero-based position is assigned from deterministic left-to-right condition traversal.<br/>
        /// Invoking the wrapper before descriptor rehydration throws so an incomplete condition cannot appear to execute successfully.<br/>
        /// </summary>
        /// <param name="name">Optional unique parameter name used by inspection and named rehydration.<br/></param>
        /// <returns>An unbound value wrapper accepted anywhere the existing condition surface accepts <see cref="ValueOrExpression{TVal}"/>.<br/></returns>
        public static ValueOrExpression<TVal> Parameter(string? name = null)
        {
            if (name is not null && string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A condition parameter name cannot be whitespace.", nameof(name));

            return new ValueOrExpression<TVal>(
                () => throw new InvalidOperationException(
                    $"Condition parameter '{name ?? "<unnamed>"}' must be supplied by descriptor rehydration before execution."))
            {
                BindingName = name,
                IsExplicitBinding = true
            };
        }

        /// <summary>
        /// Assigns an optional durable binding-contract name to this deferred wrapper without evaluating it.<br/>
        /// Naming is useful when a serialized condition is inspected or rehydrated by name, while positional rehydration remains available when no name is supplied.<br/>
        /// </summary>
        /// <param name="name">Unique non-empty parameter name within the serialized condition.<br/></param>
        /// <returns>This wrapper so existing fluent condition construction remains allocation-minimal.<br/></returns>
        public ValueOrExpression<TVal> Named(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            BindingName = name;
            return this;
        }
        /// <summary>
        /// Reads the current typed operand by invoking its deferred factory or reading its bound parameter; this direct call does not use an execution-plan cache.<br/>
        /// </summary>
        /// <returns>The current typed value.<br/></returns>
        public TVal Invoke() => parameter is null ? factory() : parameter.Value;
        /// <summary>
        /// Reads the current operand as object by invoking its deferred factory or reading its bound parameter.<br/>
        /// </summary>
        /// <returns>The current value, boxed when required by the value type.<br/></returns>
        public object? GetValue() => parameter is null ? factory() : parameter.Value;
        private bool isStaticValue = false;
        private TVal? staticVal;
        /// <summary>
        /// Returns a captured static value's string representation or the deferred factory's own representation without invoking that factory.<br/>
        /// </summary>
        /// <returns>The static value or factory representation, possibly null.<br/></returns>
        public override string? ToString()
        {
            if (isStaticValue) return staticVal?.ToString();
            return factory.ToString();
        }
        /// <summary>
        /// Returns the captured static value's hash or the deferred factory delegate's hash without invoking that factory.<br/>
        /// </summary>
        /// <returns>The captured value or factory delegate hash code.<br/></returns>
        /// <exception cref="NullReferenceException">The captured static value is a null reference; this retained implementation calls the value directly.<br/></exception>
        public override int GetHashCode()
        {
            if (isStaticValue) return staticVal.GetHashCode();
            return factory.GetHashCode();
        }
        /// <summary>
        /// Compares the captured static value, or the deferred factory delegate, directly with the supplied object; it does not evaluate a deferred value or unwrap another operand.<br/>
        /// </summary>
        /// <param name="obj">Object compared with the captured value or factory delegate.<br/></param>
        /// <returns>The captured value or factory delegate's equality result.<br/></returns>
        /// <exception cref="NullReferenceException">The captured static value is a null reference; this retained implementation calls the value directly.<br/></exception>
        public override bool Equals(object? obj)
        {
            if (isStaticValue) return staticVal.Equals(obj);
            return factory.Equals(obj);
        }

        /// <summary>
        /// Attaches immutable backend-neutral selector metadata without evaluating or replacing the existing deferred operand factory.<br/>
        /// </summary>
        /// <param name="descriptor">The descriptor that identifies the source selector and derivation operation.<br/></param>
        /// <returns>This value-or-expression instance so existing builder construction remains allocation-minimal and source-compatible.<br/></returns>
        internal ValueOrExpression<TVal> WithSelectorDescriptor(IBackendNeutralSelectorDescriptor descriptor)
        {
            SelectorDescriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            return this;
        }
    }

    /// <summary>
    /// Concrete descriptors retain the source selector plus the operation-specific arguments needed by either backend.<br/>
    /// </summary>
    internal interface IBackendNeutralSelectorDescriptor
    {
    }

    /// <summary>
    /// Builds a runtime-only selector evaluator without exposing application delegates to durable condition or index serialization.<br/>
    /// </summary>
    /// <typeparam name="TRecord">Exact record type accepted by the selector source.<br/></typeparam>
    internal interface IRuntimeSelectorDescriptor<TRecord> : IBackendNeutralSelectorDescriptor
    {
        /// <summary>
        /// Creates one exact typed evaluator for the active Inheto conversion registrations.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact scalar result requested by the consuming terminal.<br/></typeparam>
        /// <param name="options">Active Inheto deserialization registrations.<br/></param>
        /// <param name="propPath">Underlying source path used for diagnostics and customization scope.<br/></param>
        /// <param name="reader">Compiled allocation-stable per-payload evaluator.<br/></param>
        /// <returns><see langword="true"/> only when the requested result type exactly matches this runtime selector.<br/></returns>
        bool TryBuild<TValue>(
            Inheto.DeserializationOptions options,
            out string propPath,
            out Func<Inheto.InhetoBinary, (bool HasValue, TValue Value)> reader);
    }

    /// <summary>
    /// Semantic evaluation, durable condition capture, and LibraDex planning consume the normalized property path directly from this structural descriptor.<br/>
    /// </summary>
    internal sealed class DirectPropertySelectorDescriptor : IBackendNeutralSelectorDescriptor
    {
        /// <summary>
        /// Creates one immutable direct-property descriptor.<br/>
        /// </summary>
        /// <param name="propertyPath">The normalized Abraxas property path selected when the condition operand was constructed.<br/></param>
        internal DirectPropertySelectorDescriptor(string propertyPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(propertyPath);
            PropertyPath = new PropPath(propertyPath).Path;
        }

        /// <summary>
        /// Gets the normalized property path captured before any later mutation of the source selector builder.<br/>
        /// </summary>
        internal string PropertyPath { get; }
    }

    /// <summary>
    /// Retains one caller-owned property-path binding until a condition execution is prepared.<br/>
    /// The executing condition builder materializes and canonicalizes the path once per execution, using the same coherent parameter snapshot and owner cache as ordinary deferred criteria.<br/>
    /// Runtime-deferred selectors are intentionally not durable condition metadata because an application delegate or mutable parameter is not a stable persisted selector identity.<br/>
    /// </summary>
    internal sealed class DeferredPropertySelectorDescriptor : IBackendNeutralSelectorDescriptor
    {
        /// <summary>
        /// Creates a runtime property selector whose path is resolved immediately before condition planning.<br/>
        /// </summary>
        /// <param name="propertyPath">Immediate, expression-backed, or typed-parameter path binding.<br/></param>
        internal DeferredPropertySelectorDescriptor(ValueOrExpression<string> propertyPath)
        {
            PropertyPath = propertyPath ?? throw new ArgumentNullException(nameof(propertyPath));
        }

        /// <summary>Gets the unresolved property-path binding owned by this selector.<br/></summary>
        internal ValueOrExpression<string> PropertyPath { get; }
    }

    /// <summary>
    /// Identifies one developer-facing semantic index operand independently from its generated LibraDex definition name.<br/>
    /// The exact case-sensitive name is durable condition intent; RecordStore resolves it to physical index reality or a registered projection reader at execution time.<br/>
    /// </summary>
    internal sealed class NamedIndexSelectorDescriptor : IBackendNeutralSelectorDescriptor
    {
        /// <summary>
        /// Creates one immutable semantic-index descriptor.<br/>
        /// </summary>
        /// <param name="semanticName">Exact function or expression index name used by conditions and selectors.<br/></param>
        internal NamedIndexSelectorDescriptor(string semanticName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(semanticName);
            SemanticName = semanticName;
        }

        /// <summary>Gets the exact ordinal semantic name supplied by the developer.<br/></summary>
        internal string SemanticName { get; }
    }
}
