using System;
using System.Linq.Expressions;
using System.Numerics;

namespace AbraxasDB
{

    public static partial class Condition<T>
    {
        /// <summary>
        /// Provides typed property, State, identity, and calculated-selector entry points for the current condition builder.<br/>
        /// </summary>
        public class Clause
        {
            internal Builder builder;

            /// <summary>
            /// Binds this fluent condition stage to the supplied mutable builder and source, when present.<br/>
            /// </summary>
            /// <param name="builder">Mutable condition builder shared by this stage's fluent operations.<br/></param>
            public Clause(Builder builder)
            {
                this.builder = builder;
            }

            /// <summary>
            /// Gets the explicit parameter collection shared by every stage of this condition.<br/>
            /// Parameters may be created here before use and become ordered collection entries when first referenced by the condition structure.<br/>
            /// </summary>
            public ConditionParameterCollection Parameters => builder.Parameters;

            /// <summary>
            /// Wraps one public selector for condition operators using its backend-neutral semantic descriptor.<br/>
            /// Direct property selectors capture their normalized source path; computed selectors must carry complete operation metadata so execution never depends on generated backend text.<br/>
            /// </summary>
            /// <param name="selector">The public selector supplied by the typed property factory or developer.<br/></param>
            /// <returns>A condition selector wrapper with direct-property metadata when structurally available.<br/></returns>
            internal static ValueOrExpression<string> WrapSelector(ISelector selector)
            {
                ArgumentNullException.ThrowIfNull(selector);
                var wrapped = (ValueOrExpression<string>)(() => string.Empty);
                if (selector is ISelectorStructure structure)
                {
                    if (structure.SemanticDescriptor is { } semanticDescriptor)
                    {
                        wrapped.WithSelectorDescriptor(semanticDescriptor);
                    }
                    else if (!string.IsNullOrWhiteSpace(structure.TypedSourcePropertyPath))
                    {
                        wrapped.WithSelectorDescriptor(
                            new DirectPropertySelectorDescriptor(structure.TypedSourcePropertyPath));
                    }
                }

                if (wrapped.SelectorDescriptor is null)
                {
                    throw new NotSupportedException(
                        $"Selector type '{selector.GetType().FullName}' does not expose complete backend-neutral condition semantics.");
                }

                return wrapped;
            }

            /// <summary>
            /// Wraps one engine-owned logical value path in the same immutable direct-selector descriptor used by developer object properties.<br/>
            /// The deferred text factory remains inert because semantic evaluation and LibraDex planning consume only the normalized descriptor.<br/>
            /// </summary>
            /// <param name="path">Logical engine-owned selector name.<br/></param>
            /// <returns>A descriptor-backed selector wrapper.<br/></returns>
            private static ValueOrExpression<string> WrapEnginePath(string path)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(path);
                return ((ValueOrExpression<string>)(() => string.Empty))
                    .WithSelectorDescriptor(new DirectPropertySelectorDescriptor(path));
            }

            #region Prop Expressions
            /// <summary>
            /// Selects the expression-identified property and returns its typed calculation/comparison stage without executing a query.<br/>
            /// </summary>
            /// <param name="propSelector">Member expression identifying the selected source property.<br/></param>
            /// <returns>A typed fluent condition stage bound to the same builder.<br/></returns>
            public BinaryCalc Prop(Expression<Func<T, ReadOnlyMemory<byte>>> propSelector)
            {
                var sel = new Selector<T>().Prop(propSelector);
				return new BinaryCalc(builder, sel);
            }
			/// <summary>
			/// Selects the expression-identified property and returns its typed calculation/comparison stage without executing a query.<br/>
			/// </summary>
			/// <param name="propSelector">Member expression identifying the selected source property.<br/></param>
			/// <returns>A typed fluent condition stage bound to the same builder.<br/></returns>
			public BinaryCalc Prop(Expression<Func<T, Memory<byte>>> propSelector)
            {
                var sel = new Selector<T>().Prop(propSelector);
				return new BinaryCalc(builder, sel);
            }
			/// <summary>
			/// Selects the expression-identified property and returns its typed calculation/comparison stage without executing a query.<br/>
			/// </summary>
			/// <param name="propSelector">Member expression identifying the selected source property.<br/></param>
			/// <returns>A typed fluent condition stage bound to the same builder.<br/></returns>
			public BinaryCalc Prop(Expression<Func<T, MemoryStream>> propSelector)
            {
                var sel = new Selector<T>().Prop(propSelector);
				return new BinaryCalc(builder, sel);
            }
			/// <summary>
			/// Selects the expression-identified property and returns its typed calculation/comparison stage without executing a query.<br/>
			/// </summary>
			/// <param name="propSelector">Member expression identifying the selected source property.<br/></param>
			/// <returns>A typed fluent condition stage bound to the same builder.<br/></returns>
			public BinaryCalc Prop(Expression<Func<T, byte[]>> propSelector)
            {
				var sel = new Selector<T>().Prop(propSelector);
				return new BinaryCalc(builder, sel);
            }
			/// <summary>
			/// Selects the expression-identified property and returns its typed calculation/comparison stage without executing a query.<br/>
			/// </summary>
			/// <param name="propSelector">Member expression identifying the selected source property.<br/></param>
			/// <returns>A typed fluent condition stage bound to the same builder.<br/></returns>
			public BooleanOperator Prop(Expression<Func<T, bool>> propSelector)
            {

				var sel = new Selector<T>().Prop(propSelector);
				return new BooleanOperator(builder, WrapSelector(sel));
            }
			/// <summary>
			/// Selects the expression-identified property and returns its typed calculation/comparison stage without executing a query.<br/>
			/// </summary>
			/// <param name="propSelector">Member expression identifying the selected source property.<br/></param>
			/// <returns>A typed fluent condition stage bound to the same builder.<br/></returns>
			public TimeOnlyCalc Prop(Expression<Func<T, TimeOnly>> propSelector)
            {

				var sel = new Selector<T>().Prop(propSelector);
				return new TimeOnlyCalc(builder, sel);
            }
			/// <summary>
			/// Selects the expression-identified property and returns its typed calculation/comparison stage without executing a query.<br/>
			/// </summary>
			/// <param name="propSelector">Member expression identifying the selected source property.<br/></param>
			/// <returns>A typed fluent condition stage bound to the same builder.<br/></returns>
			public DateOnlyCalc Prop(Expression<Func<T, DateOnly>> propSelector)
            {

				var sel = new Selector<T>().Prop(propSelector);
				return new DateOnlyCalc(builder, sel);
            }
			/// <summary>
			/// Selects the expression-identified property and returns its typed calculation/comparison stage without executing a query.<br/>
			/// </summary>
			/// <param name="propSelector">Member expression identifying the selected source property.<br/></param>
			/// <returns>A typed fluent condition stage bound to the same builder.<br/></returns>
			public DateTimeCalc Prop(Expression<Func<T, DateTime>> propSelector)
            {

				var sel = new Selector<T>().Prop(propSelector);
				return new DateTimeCalc(builder, sel);
            }
			/// <summary>
			/// Selects the expression-identified property and returns its typed calculation/comparison stage without executing a query.<br/>
			/// </summary>
			/// <param name="propSelector">Member expression identifying the selected source property.<br/></param>
			/// <returns>A typed fluent condition stage bound to the same builder.<br/></returns>
			public TimeSpanCalc Prop(Expression<Func<T, TimeSpan>> propSelector)
            {

				var sel = new Selector<T>().Prop(propSelector);
				return new TimeSpanCalc(builder, sel);
            }
			/// <summary>
			/// Selects the expression-identified property and returns its typed calculation/comparison stage without executing a query.<br/>
			/// </summary>
			/// <param name="propSelector">Member expression identifying the selected source property.<br/></param>
			/// <returns>A typed fluent condition stage bound to the same builder.<br/></returns>
			public GuidCalc Prop(Expression<Func<T, Guid>> propSelector)
            {

				var sel = new Selector<T>().Prop(propSelector);
				return new GuidCalc(builder, sel);
            }
			/// <summary>
			/// Selects the expression-identified property and returns its typed calculation/comparison stage without executing a query.<br/>
			/// </summary>
			/// <param name="propSelector">Member expression identifying the selected source property.<br/></param>
			/// <returns>A typed fluent condition stage bound to the same builder.<br/></returns>
			public StringCalc Prop(Expression<Func<T, string>> propSelector)
            {

				var sel = new Selector<T>().Prop(propSelector);
				return new StringCalc(builder, sel);
            }

			/// <summary>
			/// Selects a Decimal property through the exact decimal selector factory rather than the generic numeric binary-integer probe.<br/>
			/// This preserves typed-lambda discovery for Decimal comparisons and cross-property coercion validation.<br/>
			/// </summary>
			/// <param name="propSelector">Direct Decimal property expression rooted at the record type.<br/></param>
			/// <returns>A Decimal numeric condition operator bound to the selected property.<br/></returns>
			public DecimalCalc Prop(Expression<Func<T, decimal>> propSelector)
            {
				var sel = new Selector<T>().Prop(propSelector);
				return new DecimalCalc(builder, sel);
            }
			/// <summary>
			/// Selects the expression-identified property and returns its typed calculation/comparison stage without executing a query.<br/>
			/// </summary>
			/// <typeparam name="Num">Exact numeric CLR property or calculated result type.<br/></typeparam>
			/// <param name="propSelector">Member expression identifying the selected source property.<br/></param>
			/// <returns>A typed fluent condition stage bound to the same builder.<br/></returns>
			public NumericOperator<Num> Prop<Num>(Expression<Func<T, Num>> propSelector) where Num : INumber<Num>
            {
				var sel = new Selector<T>().Prop(propSelector);
				return new NumericOperator<Num>(builder, WrapSelector(sel));
            }

            /// <summary>Selects an SByte property and exposes inline integral calculations before comparison.<br/></summary>
            public IntegralCalc<sbyte> Prop(Expression<Func<T, sbyte>> propSelector) => new(builder, new Selector<T>().Prop(propSelector));
            /// <summary>Selects a Byte property and exposes inline integral calculations before comparison.<br/></summary>
            public IntegralCalc<byte> Prop(Expression<Func<T, byte>> propSelector) => new(builder, new Selector<T>().Prop(propSelector));
            /// <summary>Selects an Int16 property and exposes inline integral calculations before comparison.<br/></summary>
            public IntegralCalc<short> Prop(Expression<Func<T, short>> propSelector) => new(builder, new Selector<T>().Prop(propSelector));
            /// <summary>Selects a UInt16 property and exposes inline integral calculations before comparison.<br/></summary>
            public IntegralCalc<ushort> Prop(Expression<Func<T, ushort>> propSelector) => new(builder, new Selector<T>().Prop(propSelector));
            /// <summary>Selects an Int32 property and exposes inline integral calculations before comparison.<br/></summary>
            public IntegralCalc<int> Prop(Expression<Func<T, int>> propSelector) => new(builder, new Selector<T>().Prop(propSelector));
            /// <summary>Selects a UInt32 property and exposes inline integral calculations before comparison.<br/></summary>
            public IntegralCalc<uint> Prop(Expression<Func<T, uint>> propSelector) => new(builder, new Selector<T>().Prop(propSelector));
            /// <summary>Selects an Int64 property and exposes inline integral calculations before comparison.<br/></summary>
            public IntegralCalc<long> Prop(Expression<Func<T, long>> propSelector) => new(builder, new Selector<T>().Prop(propSelector));
            /// <summary>Selects a UInt64 property and exposes inline integral calculations before comparison.<br/></summary>
            public IntegralCalc<ulong> Prop(Expression<Func<T, ulong>> propSelector) => new(builder, new Selector<T>().Prop(propSelector));
            /// <summary>Selects an Int128 property and exposes inline integral calculations before comparison.<br/></summary>
            public IntegralCalc<Int128> Prop(Expression<Func<T, Int128>> propSelector) => new(builder, new Selector<T>().Prop(propSelector));
            /// <summary>Selects a UInt128 property and exposes inline integral calculations before comparison.<br/></summary>
            public IntegralCalc<UInt128> Prop(Expression<Func<T, UInt128>> propSelector) => new(builder, new Selector<T>().Prop(propSelector));
            /// <summary>Selects a BigInteger property and exposes inline integral calculations before comparison.<br/></summary>
            public IntegralCalc<BigInteger> Prop(Expression<Func<T, BigInteger>> propSelector) => new(builder, new Selector<T>().Prop(propSelector));
            /// <summary>Selects a Half property and exposes inline real-number calculations before comparison.<br/></summary>
            public RealCalc<Half> Prop(Expression<Func<T, Half>> propSelector) => new(builder, new Selector<T>().Prop(propSelector));
            /// <summary>Selects a Single property and exposes inline real-number calculations before comparison.<br/></summary>
            public RealCalc<float> Prop(Expression<Func<T, float>> propSelector) => new(builder, new Selector<T>().Prop(propSelector));
            /// <summary>Selects a Double property and exposes inline real-number calculations before comparison.<br/></summary>
            public RealCalc<double> Prop(Expression<Func<T, double>> propSelector) => new(builder, new Selector<T>().Prop(propSelector));
            #endregion

			/// <summary>
			/// Selects a textual property path and exposes explicit typed views before comparison; no record is read during authoring.<br/>
			/// </summary>
			/// <param name="propPath">Property path identifying the condition source.<br/></param>
			/// <returns>A typed fluent condition stage bound to the same builder.<br/></returns>
			public ValueTypeSelector PropPath(string propPath)
            {
				var sel = new Selector<T>().PropPath(propPath);
				return new ValueTypeSelector(builder, sel);
			}

            /// <summary>
            /// Selects a property path whose value is materialized once immediately before each condition execution is planned.<br/>
            /// The same condition can therefore be reused after changing a bound <see cref="ConditionParameter{TValue}"/> or any state read by the supplied expression.<br/>
            /// The resolved path is canonicalized and validated against <typeparamref name="T"/> before any record is evaluated, and an available compatible LibraDex index remains eligible for that execution.<br/>
            /// </summary>
            /// <param name="propPath">Immediate, expression-backed, or typed-parameter property path.<br/></param>
            /// <returns>An untyped selector exposing the ordinary <c>As...</c>, null, and empty condition families.<br/></returns>
            public ValueTypeSelector PropPath(ValueOrExpression<string> propPath)
            {
                ArgumentNullException.ThrowIfNull(propPath);
                var selector = ((ValueOrExpression<string>)string.Empty)
                    .WithSelectorDescriptor(new DeferredPropertySelectorDescriptor(propPath));
                return new ValueTypeSelector(builder, selector);
            }

            /// <summary>
            /// Selects a property path by invoking one caller expression exactly once while each condition execution is prepared.<br/>
            /// This overload keeps deferred path selection directly discoverable from the fluent <c>Where.PropPath(...)</c> surface.<br/>
            /// </summary>
            /// <param name="propPathFactory">Expression returning the property path to use for the next execution.<br/></param>
            /// <returns>An untyped selector exposing the ordinary <c>As...</c>, null, and empty condition families.<br/></returns>
            public ValueTypeSelector PropPath(Func<string> propPathFactory)
            {
                ArgumentNullException.ThrowIfNull(propPathFactory);
                return PropPath(new ValueOrExpression<string>(propPathFactory));
            }

            /// <summary>
            /// Selects one exact case-sensitive semantic index name as the left-hand condition operand.<br/>
            /// RecordStore prefers the matching LibraDex index and falls back to the registered projection over authoritative payloads only when physical acceleration is unavailable.<br/>
            /// </summary>
            /// <param name="semanticName">Function or expression index name previously registered with the executing store.<br/></param>
            /// <returns>An untyped value selector exposing the ordinary <c>As...</c>, null, and empty condition families.<br/></returns>
            public ValueTypeSelector Index(string semanticName)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(semanticName);
                if (System.Text.Encoding.UTF8.GetByteCount(semanticName) > 256)
                    throw new ArgumentOutOfRangeException(nameof(semanticName), "Semantic index names cannot exceed 256 UTF-8 bytes.");
                var wrapped = ((ValueOrExpression<string>)(() => string.Empty))
                    .WithSelectorDescriptor(new NamedIndexSelectorDescriptor(semanticName));
                return new ValueTypeSelector(builder, wrapped);
            }

            /// <summary>
            /// Adapts an assembly-owned selector union to the untyped condition stage used by grouping drill-down reconstruction.<br/>
            /// Public callers use the strongly typed selector overloads so comparison operators remain discoverable without casts.<br/>
            /// </summary>
            /// <param name="selector">Direct or computed selector retained by Abraxas.<br/></param>
            /// <returns>An untyped value stage for assembly-owned runtime type dispatch.<br/></returns>
            internal ValueTypeSelector Calc(ISelector selector)
            {
                ArgumentNullException.ThrowIfNull(selector);
                return new ValueTypeSelector(builder, WrapSelector(selector));
            }

            // Typed selector overloads to preserve fluent operator typing when caller passes a typed Selector<T>.
            /// <summary>
            /// Uses a reusable direct or calculated selector as the typed left operand of a condition; comparison is authored after this stage.<br/>
            /// </summary>
            /// <param name="selector">Typed direct or computed selector whose semantic structure is retained by the condition.<br/></param>
            /// <returns>A typed fluent condition stage bound to the same builder.<br/></returns>
            public StringCalc Calc(Selector<T>.StringSelector selector)
            {
                ArgumentNullException.ThrowIfNull(selector);
                return new StringCalc(builder, selector);
            }

            /// <summary>
            /// Uses a reusable direct or calculated selector as the typed left operand of a condition; comparison is authored after this stage.<br/>
            /// </summary>
            /// <param name="selector">Typed direct or computed selector whose semantic structure is retained by the condition.<br/></param>
            /// <returns>A typed fluent condition stage bound to the same builder.<br/></returns>
            public BooleanOperator Calc(Selector<T>.BooleanSelector selector)
            {
                ArgumentNullException.ThrowIfNull(selector);
                return new BooleanOperator(builder, WrapSelector(selector));
            }

            /// <summary>
            /// Uses a reusable direct or calculated selector as the typed left operand of a condition; comparison is authored after this stage.<br/>
            /// </summary>
            /// <param name="selector">Typed direct or computed selector whose semantic structure is retained by the condition.<br/></param>
            /// <returns>A typed fluent condition stage bound to the same builder.<br/></returns>
            public BinaryCalc Calc(Selector<T>.BinarySelector selector)
            {
                ArgumentNullException.ThrowIfNull(selector);
                return new BinaryCalc(builder, selector);
            }

            /// <summary>
            /// Uses a reusable direct or calculated selector as the typed left operand of a condition; comparison is authored after this stage.<br/>
            /// </summary>
            /// <param name="selector">Typed direct or computed selector whose semantic structure is retained by the condition.<br/></param>
            /// <returns>A typed fluent condition stage bound to the same builder.<br/></returns>
            public GuidCalc Calc(Selector<T>.GuidSelector selector)
            {
                ArgumentNullException.ThrowIfNull(selector);
                return new GuidCalc(builder, selector);
            }

            /// <summary>
            /// Uses a reusable direct or calculated selector as the typed left operand of a condition; comparison is authored after this stage.<br/>
            /// </summary>
            /// <param name="selector">Typed direct or computed selector whose semantic structure is retained by the condition.<br/></param>
            /// <returns>A typed fluent condition stage bound to the same builder.<br/></returns>
            public DateTimeCalc Calc(Selector<T>.DateTimeSelector selector)
            {
                ArgumentNullException.ThrowIfNull(selector);
                return new DateTimeCalc(builder, selector);
            }

            /// <summary>
            /// Uses a reusable direct or calculated selector as the typed left operand of a condition; comparison is authored after this stage.<br/>
            /// </summary>
            /// <param name="selector">Typed direct or computed selector whose semantic structure is retained by the condition.<br/></param>
            /// <returns>A typed fluent condition stage bound to the same builder.<br/></returns>
            public DateOnlyCalc Calc(Selector<T>.DateOnlySelector selector)
            {
                ArgumentNullException.ThrowIfNull(selector);
                return new DateOnlyCalc(builder, selector);
            }

            /// <summary>
            /// Uses a reusable direct or calculated selector as the typed left operand of a condition; comparison is authored after this stage.<br/>
            /// </summary>
            /// <param name="selector">Typed direct or computed selector whose semantic structure is retained by the condition.<br/></param>
            /// <returns>A typed fluent condition stage bound to the same builder.<br/></returns>
            public TimeOnlyCalc Calc(Selector<T>.TimeOnlySelector selector)
            {
                ArgumentNullException.ThrowIfNull(selector);
                return new TimeOnlyCalc(builder, selector);
            }

            /// <summary>
            /// Uses a reusable direct or calculated selector as the typed left operand of a condition; comparison is authored after this stage.<br/>
            /// </summary>
            /// <param name="selector">Typed direct or computed selector whose semantic structure is retained by the condition.<br/></param>
            /// <returns>A typed fluent condition stage bound to the same builder.<br/></returns>
            public TimeSpanCalc Calc(Selector<T>.TimeSpanSelector selector)
            {
                ArgumentNullException.ThrowIfNull(selector);
                return new TimeSpanCalc(builder, selector);
            }

            /// <summary>
            /// Uses a decimal selector, including a backend-neutral arithmetic or rounding descriptor, as the typed left operand of a numeric condition.<br/>
            /// The overload preserves decimal operator discovery without requiring the developer to fall back through <see cref="ValueTypeSelector.AsDecimal"/>.<br/>
            /// </summary>
            /// <param name="selector">Direct or computed decimal selector whose immutable structure is frozen into the condition.<br/></param>
            /// <returns>A decimal numeric condition operator bound to the selector's current semantic descriptor.<br/></returns>
            public DecimalCalc Calc(Selector<T>.DecimalSelector selector)
            {
                ArgumentNullException.ThrowIfNull(selector);
                return new DecimalCalc(builder, selector);
            }

            /// <summary>
            /// Attaches a reusable integral selector to this condition while preserving its exact semantic descriptor and numeric result type.<br/>
            /// Subsequent arithmetic remains condition-local; comparison and execution use the same planner as the reusable selector form.<br/>
            /// </summary>
            public IntegralCalc<TInt> Calc<TInt>(Selector<T>.IntegralSelector<TInt> selector)
                where TInt : INumber<TInt>, IBinaryInteger<TInt>
                => new(builder, selector);

            /// <summary>
            /// Attaches a reusable real-number selector to this condition while preserving its exact semantic descriptor and result type.<br/>
            /// Subsequent transformations compose onto that immutable descriptor before ordinary numeric comparison.<br/>
            /// </summary>
            public RealCalc<TReal> Calc<TReal>(Selector<T>.RealSelector<TReal> selector)
                where TReal : INumber<TReal>
                => new(builder, selector);

            /// <summary>
            /// Uses a reusable direct or calculated selector as the typed left operand of a condition; comparison is authored after this stage.<br/>
            /// </summary>
            /// <typeparam name="Num">Exact numeric CLR property or calculated result type.<br/></typeparam>
            /// <param name="selector">Typed direct or computed selector whose semantic structure is retained by the condition.<br/></param>
            /// <returns>A typed fluent condition stage bound to the same builder.<br/></returns>
            public NumericOperator<Num> Calc<Num>(Selector<T>.NumericSelector<Num> selector) where Num : INumber<Num>
            {
                ArgumentNullException.ThrowIfNull(selector);
                return new NumericOperator<Num>(builder, WrapSelector(selector));
            }

            /// <summary>
            /// Uses a runtime-only application selector as a condition operand while retaining an explicit developer-selected operator family.<br/>
            /// Call the ordinary <c>As...</c> member matching <typeparamref name="TValue"/> before choosing a comparison; this deliberate boundary prevents arbitrary generic results from becoming a LINQ-like condition surface.<br/>
            /// </summary>
            /// <typeparam name="TValue">Exact supported scalar type returned by the external operation.<br/></typeparam>
            /// <param name="selector">Runtime-only selector produced by <c>Selector.External(...)</c>.<br/></param>
            /// <returns>An explicit value-type stage bound to the runtime selector.<br/></returns>
            public ValueTypeSelector Calc<TValue>(Selector<T>.ExternalSelector<TValue> selector)
            {
                ArgumentNullException.ThrowIfNull(selector);
                return new ValueTypeSelector(builder, WrapSelector(selector));
            }

			/// <summary>
			/// Selects the authoritative Abraxas record identity as an unsigned 64-bit comparison operand, independently of object properties.<br/>
			/// </summary>
			public NumericOperator<ulong> ID { get { return new NumericOperator<ulong>(builder, "ID"); } }
			/// <summary>
			/// Selects developer-owned persistent State as a signed 64-bit comparison operand, independently of serialized object properties.<br/>
			/// </summary>
			public NumericOperator<long> State { get { return new NumericOperator<long>(builder, WrapEnginePath("State")); } }

            /// <summary>
            /// Selects the UTC clock used by clock-relative conditions created from this clause.<br/>
            /// The provider is stored on the condition builder, preserved by condition cloning, and sampled once when each semantic execution plan begins.<br/>
            /// Omitting this call retains the default <see cref="TimeProvider.System"/> behavior.<br/>
            /// </summary>
            /// <param name="timeProvider">Provider that supplies UTC time for today, yesterday, and recent-interval operations.<br/></param>
            /// <returns>This clause so property selection can continue fluently without an additional builder object.<br/></returns>
            public Clause UsingTimeProvider(TimeProvider timeProvider)
            {
                ArgumentNullException.ThrowIfNull(timeProvider);
                builder.Clock = timeProvider;
                return this;
            }

        }
    }
}
