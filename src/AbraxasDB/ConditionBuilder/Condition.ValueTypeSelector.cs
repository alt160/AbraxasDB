using System.Numerics;

namespace AbraxasDB
{

    public static partial class Condition<T>
    {
		/// <summary>
		/// Provides explicit typed comparison and calculation views over an untyped condition source path.<br/>
		/// </summary>
		public class ValueTypeSelector
		{
			private Builder builder;
			private ValueOrExpression<string> selectorExpression;
			private Selector<T>.ValueTypeSelector calcSelector;
			/// <summary>
			/// Binds this fluent condition stage to the supplied mutable builder and source, when present.<br/>
			/// </summary>
			/// <param name="builder">Mutable condition builder shared by this stage's fluent operations.<br/></param>
			/// <param name="selectorExpression">Immediate or deferred source selector retained until condition materialization.<br/></param>
			public ValueTypeSelector(Builder builder, ValueOrExpression<string> selectorExpression)
			{
				this.builder = builder;
				this.selectorExpression = selectorExpression;
				calcSelector = new Selector<T>.ValueTypeSelector(new Selector<T>.Builder(), selectorExpression, addBase: false);
			}

			/// <summary>
			/// Preserves an untyped reusable selector so a textual property path can continue through the same calculated operations as a typed property lambda.<br/>
			/// </summary>
			/// <param name="builder">Condition builder that receives the eventual comparison.<br/></param>
			/// <param name="selector">Untyped selector retaining the direct or computed semantic source.<br/></param>
			internal ValueTypeSelector(Builder builder, Selector<T>.ValueTypeSelector selector)
			{
				this.builder = builder;
				calcSelector = selector ?? throw new ArgumentNullException(nameof(selector));
				selectorExpression = Clause.WrapSelector(selector);
			}

			/// <summary>
			/// Creates one typed condition view while preserving immutable backend-neutral source-property metadata.<br/>
			/// The typed operator consumes the existing immutable source descriptor directly and introduces no transport-specific expression wrapper.<br/>
			/// A pre-existing derived-selector descriptor is propagated unchanged because choosing the consuming CLR operator type does not add another value transformation.<br/>
			/// </summary>
			/// <param name="targetType">The CLR type selected by the fluent <c>As...</c> property.<br/></param>
			/// <returns>The existing selector wrapper whose backend-neutral descriptor already owns the source identity.<br/></returns>
			private ValueOrExpression<string> CreateTypedView(Type targetType)
			{
				ArgumentNullException.ThrowIfNull(targetType);
				return selectorExpression;
			}

			/// <summary>
			/// Gets the Byte comparison/calculation view over the current source; choosing a view does not execute or hydrate a record.<br/>
			/// </summary>
			public IntegralCalc<byte> AsByte => new(builder, calcSelector.AsByte);
			/// <summary>
			/// Gets the SByte comparison/calculation view over the current source; choosing a view does not execute or hydrate a record.<br/>
			/// </summary>
			public IntegralCalc<sbyte> AsSByte => new(builder, calcSelector.AsSByte);
			/// <summary>
			/// Gets the Int16 comparison/calculation view over the current source; choosing a view does not execute or hydrate a record.<br/>
			/// </summary>
			public IntegralCalc<short> AsInt16 => new(builder, calcSelector.AsInt16);
			/// <summary>
			/// Gets the UInt16 comparison/calculation view over the current source; choosing a view does not execute or hydrate a record.<br/>
			/// </summary>
			public IntegralCalc<ushort> AsUInt16 => new(builder, calcSelector.AsUInt16);
			/// <summary>
			/// Gets the Int32 comparison/calculation view over the current source; choosing a view does not execute or hydrate a record.<br/>
			/// </summary>
			public IntegralCalc<int> AsInt32 => new(builder, calcSelector.AsInt32);
			/// <summary>
			/// Gets the UInt32 comparison/calculation view over the current source; choosing a view does not execute or hydrate a record.<br/>
			/// </summary>
			public IntegralCalc<uint> AsUInt32 => new(builder, calcSelector.AsUInt32);
			/// <summary>
			/// Gets the Int64 comparison/calculation view over the current source; choosing a view does not execute or hydrate a record.<br/>
			/// </summary>
			public IntegralCalc<long> AsInt64 => new(builder, calcSelector.AsInt64);
			/// <summary>
			/// Gets the UInt64 comparison/calculation view over the current source; choosing a view does not execute or hydrate a record.<br/>
			/// </summary>
			public IntegralCalc<ulong> AsUInt64 => new(builder, calcSelector.AsUInt64);
			/// <summary>
			/// Gets the Int128 comparison/calculation view over the current source; choosing a view does not execute or hydrate a record.<br/>
			/// </summary>
			public IntegralCalc<Int128> AsInt128 => new(builder, calcSelector.AsInt128);
			/// <summary>
			/// Gets the UInt128 comparison/calculation view over the current source; choosing a view does not execute or hydrate a record.<br/>
			/// </summary>
			public IntegralCalc<UInt128> AsUInt128 => new(builder, calcSelector.AsUInt128);
			/// <summary>
			/// Gets the Half comparison/calculation view over the current source; choosing a view does not execute or hydrate a record.<br/>
			/// </summary>
			public RealCalc<Half> AsHalf => new(builder, calcSelector.AsHalf);
			/// <summary>
			/// Gets the Single comparison/calculation view over the current source; choosing a view does not execute or hydrate a record.<br/>
			/// </summary>
			public RealCalc<Single> AsSingle => new(builder, calcSelector.AsSingle);
			/// <summary>
			/// Gets the Double comparison/calculation view over the current source; choosing a view does not execute or hydrate a record.<br/>
			/// </summary>
			public RealCalc<Double> AsDouble => new(builder, calcSelector.AsDouble);
			/// <summary>
			/// Gets the Decimal comparison/calculation view over the current source; choosing a view does not execute or hydrate a record.<br/>
			/// </summary>
			public DecimalCalc AsDecimal => new(builder, calcSelector.AsDecimal);
			/// <summary>
			/// Gets the BigInteger comparison/calculation view over the current source; choosing a view does not execute or hydrate a record.<br/>
			/// </summary>
			public IntegralCalc<BigInteger> AsBigInteger => new(builder, calcSelector.AsBigInteger);
			/// <summary>
			/// Gets the Binary comparison/calculation view over the current source; choosing a view does not execute or hydrate a record.<br/>
			/// </summary>
			public BinaryCalc AsBinary => new(builder, calcSelector.AsBinary);
			/// <summary>Selects the canonical <see cref="DateTime"/> condition family.<br/></summary>
			public DateTimeCalc AsDateTime => new(builder, calcSelector.AsDateTime);
			/// <summary>Preserves the established abbreviated DateTime selector name without migration ceremony.<br/></summary>
			public DateTimeCalc AsDate => AsDateTime;
			/// <summary>
			/// Gets the DateOnly comparison/calculation view over the current source; choosing a view does not execute or hydrate a record.<br/>
			/// </summary>
			public DateOnlyCalc AsDateOnly => new(builder, calcSelector.AsDateOnly);
			/// <summary>
			/// Gets the TimeOnly comparison/calculation view over the current source; choosing a view does not execute or hydrate a record.<br/>
			/// </summary>
			public TimeOnlyCalc AsTimeOnly => new(builder, calcSelector.AsTimeOnly);
			/// <summary>
			/// Gets the TimeSpan comparison/calculation view over the current source; choosing a view does not execute or hydrate a record.<br/>
			/// </summary>
			public TimeSpanCalc AsTimeSpan => new(builder, calcSelector.AsTimeSpan);
			/// <summary>
			/// Gets the Guid comparison/calculation view over the current source; choosing a view does not execute or hydrate a record.<br/>
			/// </summary>
			public GuidCalc AsGuid => new(builder, calcSelector.AsGuid);
			/// <summary>
			/// Gets the String comparison/calculation view over the current source; choosing a view does not execute or hydrate a record.<br/>
			/// </summary>
			public StringCalc AsString => new(builder, calcSelector.AsString);
			/// <summary>
			/// Gets the Boolean comparison/calculation view over the current source; choosing a view does not execute or hydrate a record.<br/>
			/// </summary>
			public BooleanOperator AsBoolean => new(builder, CreateTypedView(typeof(bool)));
			/// <summary>
			/// Gets the type-neutral null predicate stage for the current source.<br/>
			/// </summary>
			public IsNullOperator IsNull => new(builder, selectorExpression);
			/// <summary>
			/// Gets the type-neutral non-null predicate stage for the current source.<br/>
			/// </summary>
			public IsNotNullOperator IsNotNull => new(builder, selectorExpression);

			/// <summary>
			/// Selects a present string or binary value whose logical length is zero without requiring the caller to restate its value family.<br/>
			/// Direct property paths use LibraDex's dedicated empty-key route when a compatible index exists and otherwise retain authoritative Inheto evaluation.<br/>
			/// Null and missing values are distinct from empty values and do not satisfy this predicate.<br/>
			/// </summary>
			public IsEmptyOperator IsEmpty => new(builder, selectorExpression);

			/// <summary>
			/// Selects a present string or binary value whose logical length is greater than zero without requiring the caller to restate its value family.<br/>
			/// Direct property paths use LibraDex's dedicated non-empty key-state condition when a compatible index exists and otherwise retain authoritative Inheto evaluation.<br/>
			/// Null and missing values remain excluded rather than being treated as non-empty values.<br/>
			/// </summary>
			public IsNotEmptyOperator IsNotEmpty => new(builder, selectorExpression);

			/// <summary>
			/// Selects equality operators for an application-defined CLR value whose deterministic binary codec is registered on the executing <see cref="RecordStore{T}"/>.<br/>
			/// </summary>
			/// <typeparam name="TValue">The exact application CLR type registered with the executing record store.<br/></typeparam>
			/// <returns>A custom typed equality operator for the selected property path.<br/></returns>
			public CustomOperator<TValue> AsCustom<TValue>() => new(builder, selectorExpression);

        }
    }
}
