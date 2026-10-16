namespace AbraxasDB
{

    public partial class Selector<T>
    {
        /// <summary>
        /// A property-path selector awaiting an explicit scalar view.<br/>
        /// </summary>
        /// <remarks>
        /// Construction records expression intent; it does not execute a query or read record values.<br/>
        /// </remarks>
        public class ValueTypeSelector : ISelector,ISelectorStructure
        {
            private readonly Builder parentBuilder;
            private readonly ValueOrExpression<string> selectorExpression;
            private readonly IBackendNeutralSelectorDescriptor? semanticDescriptor;

            string? ISelectorStructure.SimplePropertyPath => parentBuilder.SimplePropertyPath;
            string? ISelectorStructure.TypedSourcePropertyPath => parentBuilder.TypedSourcePropertyPath;
            Type? ISelectorStructure.ResultType => null;

            internal ValueTypeSelector(Builder parentBuilder, ValueOrExpression<string> selectorExpression, bool addBase = true)
            {
                this.parentBuilder = addBase ? parentBuilder.AsComputed() : parentBuilder;
                this.selectorExpression = selectorExpression;
                semanticDescriptor = selectorExpression.SelectorDescriptor;
            }

            /// <summary>
            /// Chooses the Byte scalar view for the selected property path.<br/>
            /// </summary>
            /// <returns>A selector exposing operations for the chosen value family.<br/></returns>
            /// <remarks>
            /// Choosing a view does not read or convert a stored value; the consuming operation applies its scalar read contract.<br/>
            /// </remarks>
            public IntegralSelector<byte> AsByte => new(parentBuilder.AsTypedView(), selectorExpression, false, semanticDescriptor);
            /// <summary>
            /// Chooses the SByte scalar view for the selected property path.<br/>
            /// </summary>
            /// <returns>A selector exposing operations for the chosen value family.<br/></returns>
            /// <remarks>
            /// Choosing a view does not read or convert a stored value; the consuming operation applies its scalar read contract.<br/>
            /// </remarks>
            public IntegralSelector<sbyte> AsSByte => new(parentBuilder.AsTypedView(), selectorExpression, false, semanticDescriptor);
            /// <summary>
            /// Chooses the Int16 scalar view for the selected property path.<br/>
            /// </summary>
            /// <returns>A selector exposing operations for the chosen value family.<br/></returns>
            /// <remarks>
            /// Choosing a view does not read or convert a stored value; the consuming operation applies its scalar read contract.<br/>
            /// </remarks>
            public IntegralSelector<short> AsInt16 => new(parentBuilder.AsTypedView(), selectorExpression, false, semanticDescriptor);
            /// <summary>
            /// Chooses the UInt16 scalar view for the selected property path.<br/>
            /// </summary>
            /// <returns>A selector exposing operations for the chosen value family.<br/></returns>
            /// <remarks>
            /// Choosing a view does not read or convert a stored value; the consuming operation applies its scalar read contract.<br/>
            /// </remarks>
            public IntegralSelector<ushort> AsUInt16 => new(parentBuilder.AsTypedView(), selectorExpression, false, semanticDescriptor);
            /// <summary>
            /// Chooses the Int32 scalar view for the selected property path.<br/>
            /// </summary>
            /// <returns>A selector exposing operations for the chosen value family.<br/></returns>
            /// <remarks>
            /// Choosing a view does not read or convert a stored value; the consuming operation applies its scalar read contract.<br/>
            /// </remarks>
            public IntegralSelector<int> AsInt32 => new(parentBuilder.AsTypedView(), selectorExpression, false, semanticDescriptor);
            /// <summary>
            /// Chooses the UInt32 scalar view for the selected property path.<br/>
            /// </summary>
            /// <returns>A selector exposing operations for the chosen value family.<br/></returns>
            /// <remarks>
            /// Choosing a view does not read or convert a stored value; the consuming operation applies its scalar read contract.<br/>
            /// </remarks>
            public IntegralSelector<uint> AsUInt32 => new(parentBuilder.AsTypedView(), selectorExpression, false, semanticDescriptor);
            /// <summary>
            /// Chooses the Int64 scalar view for the selected property path.<br/>
            /// </summary>
            /// <returns>A selector exposing operations for the chosen value family.<br/></returns>
            /// <remarks>
            /// Choosing a view does not read or convert a stored value; the consuming operation applies its scalar read contract.<br/>
            /// </remarks>
            public IntegralSelector<long> AsInt64 => new(parentBuilder.AsTypedView(), selectorExpression, false, semanticDescriptor);
            /// <summary>
            /// Chooses the UInt64 scalar view for the selected property path.<br/>
            /// </summary>
            /// <returns>A selector exposing operations for the chosen value family.<br/></returns>
            /// <remarks>
            /// Choosing a view does not read or convert a stored value; the consuming operation applies its scalar read contract.<br/>
            /// </remarks>
            public IntegralSelector<ulong> AsUInt64 => new(parentBuilder.AsTypedView(), selectorExpression, false, semanticDescriptor);
            /// <summary>
            /// Chooses the Int128 scalar view for the selected property path.<br/>
            /// </summary>
            /// <returns>A selector exposing operations for the chosen value family.<br/></returns>
            /// <remarks>
            /// Choosing a view does not read or convert a stored value; the consuming operation applies its scalar read contract.<br/>
            /// </remarks>
            public IntegralSelector<Int128> AsInt128 => new(parentBuilder.AsTypedView(), selectorExpression, false, semanticDescriptor);
            /// <summary>
            /// Chooses the UInt128 scalar view for the selected property path.<br/>
            /// </summary>
            /// <returns>A selector exposing operations for the chosen value family.<br/></returns>
            /// <remarks>
            /// Choosing a view does not read or convert a stored value; the consuming operation applies its scalar read contract.<br/>
            /// </remarks>
            public IntegralSelector<UInt128> AsUInt128 => new(parentBuilder.AsTypedView(), selectorExpression, false, semanticDescriptor);
            /// <summary>
            /// Chooses the BigInteger scalar view for the selected property path.<br/>
            /// </summary>
            /// <returns>A selector exposing operations for the chosen value family.<br/></returns>
            /// <remarks>
            /// Choosing a view does not read or convert a stored value; the consuming operation applies its scalar read contract.<br/>
            /// </remarks>
            public IntegralSelector<System.Numerics.BigInteger> AsBigInteger => new(parentBuilder.AsTypedView(), selectorExpression, false, semanticDescriptor);
            /// <summary>
            /// Chooses the Half scalar view for the selected property path.<br/>
            /// </summary>
            /// <returns>A selector exposing operations for the chosen value family.<br/></returns>
            /// <remarks>
            /// Choosing a view does not read or convert a stored value; the consuming operation applies its scalar read contract.<br/>
            /// </remarks>
            public RealSelector<Half> AsHalf => new(parentBuilder.AsTypedView(), selectorExpression, false, semanticDescriptor);
            /// <summary>
            /// Chooses the Single scalar view for the selected property path.<br/>
            /// </summary>
            /// <returns>A selector exposing operations for the chosen value family.<br/></returns>
            /// <remarks>
            /// Choosing a view does not read or convert a stored value; the consuming operation applies its scalar read contract.<br/>
            /// </remarks>
            public RealSelector<Single> AsSingle => new(parentBuilder.AsTypedView(), selectorExpression, false, semanticDescriptor);
            /// <summary>
            /// Chooses the Double scalar view for the selected property path.<br/>
            /// </summary>
            /// <returns>A selector exposing operations for the chosen value family.<br/></returns>
            /// <remarks>
            /// Choosing a view does not read or convert a stored value; the consuming operation applies its scalar read contract.<br/>
            /// </remarks>
            public RealSelector<Double> AsDouble => new(parentBuilder.AsTypedView(), selectorExpression, false, semanticDescriptor);
            /// <summary>
            /// Chooses the Decimal scalar view for the selected property path.<br/>
            /// </summary>
            /// <returns>A selector exposing operations for the chosen value family.<br/></returns>
            /// <remarks>
            /// Choosing a view does not read or convert a stored value; the consuming operation applies its scalar read contract.<br/>
            /// </remarks>
            public DecimalSelector AsDecimal => new(parentBuilder.AsTypedView(), selectorExpression, false, semanticDescriptor);
            /// <summary>
            /// Chooses the Binary scalar view for the selected property path.<br/>
            /// </summary>
            /// <returns>A selector exposing operations for the chosen value family.<br/></returns>
            /// <remarks>
            /// Choosing a view does not read or convert a stored value; the consuming operation applies its scalar read contract.<br/>
            /// </remarks>
            public BinarySelector AsBinary => new BinarySelector(parentBuilder.AsTypedView(), selectorExpression, addBase: false, semanticDescriptor);
            /// <summary>Selects the canonical <see cref="DateTime"/> manipulation family.<br/></summary>
            public DateTimeSelector AsDateTime => new DateTimeSelector(parentBuilder.AsTypedView(), selectorExpression, addBase: false);
            /// <summary>
            /// Chooses the DateOnly scalar view for the selected property path.<br/>
            /// </summary>
            /// <returns>A selector exposing operations for the chosen value family.<br/></returns>
            /// <remarks>
            /// Choosing a view does not read or convert a stored value; the consuming operation applies its scalar read contract.<br/>
            /// </remarks>
            public DateOnlySelector AsDateOnly => new DateOnlySelector(parentBuilder.AsTypedView(), selectorExpression, addBase: false);
            /// <summary>
            /// Chooses the TimeOnly scalar view for the selected property path.<br/>
            /// </summary>
            /// <returns>A selector exposing operations for the chosen value family.<br/></returns>
            /// <remarks>
            /// Choosing a view does not read or convert a stored value; the consuming operation applies its scalar read contract.<br/>
            /// </remarks>
            public TimeOnlySelector AsTimeOnly => new TimeOnlySelector(parentBuilder.AsTypedView(), selectorExpression, addBase: false);
            /// <summary>
            /// Chooses the TimeSpan scalar view for the selected property path.<br/>
            /// </summary>
            /// <returns>A selector exposing operations for the chosen value family.<br/></returns>
            /// <remarks>
            /// Choosing a view does not read or convert a stored value; the consuming operation applies its scalar read contract.<br/>
            /// </remarks>
            public TimeSpanSelector AsTimeSpan => new TimeSpanSelector(parentBuilder.AsTypedView(), selectorExpression, addBase: false);
            /// <summary>
            /// Chooses the Guid scalar view for the selected property path.<br/>
            /// </summary>
            /// <returns>A selector exposing operations for the chosen value family.<br/></returns>
            /// <remarks>
            /// Choosing a view does not read or convert a stored value; the consuming operation applies its scalar read contract.<br/>
            /// </remarks>
            public GuidSelector AsGuid => new GuidSelector(parentBuilder.AsTypedView(), selectorExpression, addBase: false, semanticDescriptor);
            /// <summary>
            /// Chooses the String scalar view for the selected property path.<br/>
            /// </summary>
            /// <returns>A selector exposing operations for the chosen value family.<br/></returns>
            /// <remarks>
            /// Choosing a view does not read or convert a stored value; the consuming operation applies its scalar read contract.<br/>
            /// </remarks>
            public StringSelector AsString => new StringSelector(parentBuilder.AsTypedView(), selectorExpression, addBase: false, isLiteral: false, semanticDescriptor);
            /// <summary>
            /// Chooses the Boolean scalar view for the selected property path.<br/>
            /// </summary>
            /// <returns>A selector exposing operations for the chosen value family.<br/></returns>
            /// <remarks>
            /// Choosing a view does not read or convert a stored value; the consuming operation applies its scalar read contract.<br/>
            /// </remarks>
            public BooleanSelector AsBoolean => new BooleanSelector(parentBuilder.AsTypedView(), selectorExpression, addBase: false);

            /// <summary>
            /// Treats the selected property path as its exact declared enum type for runtime-only projection.<br/>
            /// No numeric coercion is performed; property-path validation and Inheto reads retain the enum's CLR identity so mismatched enum types fail before row evaluation.<br/>
            /// </summary>
            /// <typeparam name="TEnum">Exact enum type declared by the selected member.<br/></typeparam>
            /// <returns>A typed enum selector suitable for <c>External</c> projection.<br/></returns>
            public EnumSelector<TEnum> AsEnum<TEnum>() where TEnum : struct, Enum
                => new(parentBuilder.AsTypedView(), selectorExpression, semanticDescriptor);
        }

        /// <summary>
        /// Represents one exact enum-valued property path without broadening enum values into the durable scalar/index contract.<br/>
        /// The selector intentionally exposes only the common typed selector structure and runtime external projection boundary.<br/>
        /// </summary>
        /// <typeparam name="TEnum">Exact enum value type produced by the selected member.<br/></typeparam>
        public sealed class EnumSelector<TEnum> : IValue<TEnum>, ISelectorStructure where TEnum : struct, Enum
        {
            private readonly Builder parentBuilder;
            private readonly IBackendNeutralSelectorDescriptor? semanticDescriptor;

            string? ISelectorStructure.SimplePropertyPath => parentBuilder.SimplePropertyPath;
            string? ISelectorStructure.TypedSourcePropertyPath => parentBuilder.TypedSourcePropertyPath;
            Type? ISelectorStructure.ResultType => typeof(TEnum);
            IBackendNeutralSelectorDescriptor? ISelectorStructure.SemanticDescriptor => semanticDescriptor;

            /// <summary>
            /// Creates an exact enum view over an already-authored property-path selector.<br/>
            /// The retained descriptor is immutable and the builder is marked only as a typed view, leaving durable index eligibility unchanged.<br/>
            /// </summary>
            /// <param name="parentBuilder">Selector builder carrying the source property path.<br/></param>
            /// <param name="selectorExpression">Original selector expression retained for parity with the typed selector family.<br/></param>
            /// <param name="semanticDescriptor">Immutable executable source descriptor.<br/></param>
            internal EnumSelector(
                Builder parentBuilder,
                ValueOrExpression<string> selectorExpression,
                IBackendNeutralSelectorDescriptor? semanticDescriptor)
            {
                this.parentBuilder = parentBuilder;
                this.semanticDescriptor = semanticDescriptor ?? selectorExpression.SelectorDescriptor;
            }

            /// <summary>
            /// Converts the exact enum value to its canonical .NET display text through a durable Abraxas selector operation.<br/>
            /// Named values are precomputed once for the closed enum type, while previously unseen flag combinations are cached after their first conversion.<br/>
            /// The resulting selector can be persisted, replayed, indexed, and used as an index-backed virtual property without application executable code.<br/>
            /// </summary>
            /// <returns>A durable string selector matching <see cref="Enum.ToString()"/> semantics for <typeparamref name="TEnum"/>.<br/></returns>
            public StringSelector AsString
            {
                get
                {
                    parentBuilder.MarkComputed();
                    IBackendNeutralSelectorDescriptor? sourceDescriptor = semanticDescriptor;
                    if (sourceDescriptor is null &&
                        !string.IsNullOrWhiteSpace(((ISelectorStructure)this).TypedSourcePropertyPath))
                    {
                        sourceDescriptor = new DirectPropertySelectorDescriptor(
                            ((ISelectorStructure)this).TypedSourcePropertyPath!);
                    }
                    BuiltInSelectorDescriptor? descriptor = sourceDescriptor is null
                        ? null
                        : new BuiltInSelectorDescriptor(
                            sourceDescriptor,
                            BuiltInSelectorOperationKind.EnumToString,
                            typeof(TEnum),
                            typeof(string));
                    return new StringSelector(
                        parentBuilder,
                        (ValueOrExpression<string>)string.Empty,
                        addBase: false,
                        isLiteral: false,
                        descriptor);
                }
            }
        }
    }
}
