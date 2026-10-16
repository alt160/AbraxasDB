using System.Linq.Expressions;
using System.Numerics;

namespace AbraxasDB
{
    public partial class Selector<T>
    {
        /// <summary>
        /// Base for numeric property views and composable calculated numeric selectors.<br/>
        /// </summary>
        /// <typeparam name="TNum">Exact numeric value type declared by the selector.<br/></typeparam>
        /// <remarks>
        /// Selector authoring records expression intent; record values are read only when an operation consumes the selector.<br/>
        /// </remarks>
        public abstract partial class NumericSelector<TNum>
            where TNum : INumber<TNum>
        {
            /// <summary>
            /// Begins addition against a same-record numeric property whose exact CLR type is supplied by the typed lambda.<br/>
            /// Choose one <c>ResultAs...</c> member to state the checked arithmetic domain when the semantic expression is consumed.<br/>
            /// </summary>
            /// <typeparam name="TRight">Declared CLR type of the right property.<br/></typeparam>
            /// <param name="property">Typed same-record right property.<br/></param>
            /// <returns>An immutable semantic arithmetic stage awaiting its explicit result domain.<br/></returns>
            public NumericPropertyArithmeticSelector<TNum, TRight> PropAdd<TRight>(Expression<Func<T, TRight>> property)
                where TRight : INumber<TRight>
                => BeginPropertyArithmetic(property, NumericArithmeticOperationKind.Add);

            /// <summary>
            /// Begins textual same-record numeric property addition.<br/>
            /// First choose the right property's authored <c>As...</c> type, then choose the checked <c>ResultAs...</c> arithmetic domain.<br/>
            /// </summary>
            /// <param name="propertyPath">Abraxas property path for the right operand.<br/></param>
            /// <returns>A property-type completion stage.<br/></returns>
            public NumericPropertyPathArithmeticSelector<TNum> PropAdd(string propertyPath)
                => BeginPropertyArithmetic(propertyPath, NumericArithmeticOperationKind.Add);

            /// <summary>
            /// Begins subtraction against a typed same-record numeric property without converting either value during selector construction.<br/>
            /// </summary>
            /// <typeparam name="TRight">Declared CLR type of the right property.<br/></typeparam>
            /// <param name="property">Typed same-record right property.<br/></param>
            /// <returns>An immutable semantic arithmetic stage awaiting its result domain.<br/></returns>
            public NumericPropertyArithmeticSelector<TNum, TRight> PropSubtract<TRight>(Expression<Func<T, TRight>> property)
                where TRight : INumber<TRight>
                => BeginPropertyArithmetic(property, NumericArithmeticOperationKind.Subtract);

            /// <summary>
            /// Begins textual same-record numeric property subtraction.<br/>
            /// </summary>
            /// <param name="propertyPath">Abraxas property path for the right operand.<br/></param>
            /// <returns>A property-type completion stage.<br/></returns>
            public NumericPropertyPathArithmeticSelector<TNum> PropSubtract(string propertyPath)
                => BeginPropertyArithmetic(propertyPath, NumericArithmeticOperationKind.Subtract);

            /// <summary>
            /// Begins multiplication by a typed same-record numeric property while retaining both authored operand types.<br/>
            /// </summary>
            /// <typeparam name="TRight">Declared CLR type of the right property.<br/></typeparam>
            /// <param name="property">Typed same-record right property.<br/></param>
            /// <returns>An immutable semantic arithmetic stage awaiting its result domain.<br/></returns>
            public NumericPropertyArithmeticSelector<TNum, TRight> PropMultiply<TRight>(Expression<Func<T, TRight>> property)
                where TRight : INumber<TRight>
                => BeginPropertyArithmetic(property, NumericArithmeticOperationKind.Multiply);

            /// <summary>
            /// Begins textual same-record numeric property multiplication.<br/>
            /// </summary>
            /// <param name="propertyPath">Abraxas property path for the right operand.<br/></param>
            /// <returns>A property-type completion stage.<br/></returns>
            public NumericPropertyPathArithmeticSelector<TNum> PropMultiply(string propertyPath)
                => BeginPropertyArithmetic(propertyPath, NumericArithmeticOperationKind.Multiply);

            /// <summary>
            /// Begins division by a typed same-record numeric property while deferring checked conversion until record evaluation.<br/>
            /// </summary>
            /// <typeparam name="TRight">Declared CLR type of the divisor property.<br/></typeparam>
            /// <param name="property">Typed same-record divisor property.<br/></param>
            /// <returns>An immutable semantic arithmetic stage awaiting its result domain.<br/></returns>
            public NumericPropertyArithmeticSelector<TNum, TRight> PropDivide<TRight>(Expression<Func<T, TRight>> property)
                where TRight : INumber<TRight>
                => BeginPropertyArithmetic(property, NumericArithmeticOperationKind.Divide);

            /// <summary>
            /// Begins textual same-record numeric property division.<br/>
            /// </summary>
            /// <param name="propertyPath">Abraxas property path for the divisor.<br/></param>
            /// <returns>A property-type completion stage.<br/></returns>
            public NumericPropertyPathArithmeticSelector<TNum> PropDivide(string propertyPath)
                => BeginPropertyArithmetic(propertyPath, NumericArithmeticOperationKind.Divide);

            /// <summary>
            /// Begins remainder arithmetic against a typed same-record numeric property.<br/>
            /// </summary>
            /// <typeparam name="TRight">Declared CLR type of the right property.<br/></typeparam>
            /// <param name="property">Typed same-record right property.<br/></param>
            /// <returns>An immutable semantic arithmetic stage awaiting its result domain.<br/></returns>
            public NumericPropertyArithmeticSelector<TNum, TRight> PropModulo<TRight>(Expression<Func<T, TRight>> property)
                where TRight : INumber<TRight>
                => BeginPropertyArithmetic(property, NumericArithmeticOperationKind.Modulo);

            /// <summary>
            /// Begins textual same-record numeric property remainder arithmetic.<br/>
            /// </summary>
            /// <param name="propertyPath">Abraxas property path for the right operand.<br/></param>
            /// <returns>A property-type completion stage.<br/></returns>
            public NumericPropertyPathArithmeticSelector<TNum> PropModulo(string propertyPath)
                => BeginPropertyArithmetic(propertyPath, NumericArithmeticOperationKind.Modulo);

            private protected NumericPropertyArithmeticSelector<TNum, TRight> BeginPropertyArithmetic<TRight>(
                Expression<Func<T, TRight>> property,
                NumericArithmeticOperationKind operation)
                where TRight : INumber<TRight>
            {
                ArgumentNullException.ThrowIfNull(property);
                IBackendNeutralSelectorDescriptor source = GetSemanticValueDescriptor()
                    ?? throw new NotSupportedException("Numeric property arithmetic requires complete left semantic intent.");
                IBackendNeutralSelectorDescriptor right = RequireSemanticDescriptor(new Selector<T>().Prop(property));
                return new NumericPropertyArithmeticSelector<TNum, TRight>(
                    parentBuilder,
                    selectorExpression,
                    source,
                    right,
                    operation);
            }

            private protected NumericPropertyPathArithmeticSelector<TNum> BeginPropertyArithmetic(
                string propertyPath,
                NumericArithmeticOperationKind operation)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(propertyPath);
                IBackendNeutralSelectorDescriptor source = GetSemanticValueDescriptor()
                    ?? throw new NotSupportedException("Numeric property arithmetic requires complete left semantic intent.");
                return new NumericPropertyPathArithmeticSelector<TNum>(
                    parentBuilder,
                    selectorExpression,
                    source,
                    propertyPath,
                    operation);
            }
        }

        public partial class DecimalSelector
        {
            /// <summary>
            /// Begins decimal-left arithmetic against a typed same-record numeric property.<br/>
            /// Both authored operand types remain semantic metadata until an explicit <c>ResultAs...</c> domain is selected and evaluated.<br/>
            /// </summary>
            /// <typeparam name="TRight">Declared CLR type of the right property.<br/></typeparam>
            /// <param name="property">Typed same-record right property.<br/></param>
            /// <param name="operation">Arithmetic operation retained by the immutable descriptor.<br/></param>
            /// <returns>An arithmetic result-domain completion stage.<br/></returns>
            private NumericPropertyArithmeticSelector<decimal, TRight> BeginPropertyArithmetic<TRight>(
                Expression<Func<T, TRight>> property,
                NumericArithmeticOperationKind operation)
                where TRight : INumber<TRight>
            {
                ArgumentNullException.ThrowIfNull(property);
                IBackendNeutralSelectorDescriptor source = GetSemanticValueDescriptor()
                    ?? throw new NotSupportedException("Decimal property arithmetic requires complete left semantic intent.");
                return new NumericPropertyArithmeticSelector<decimal, TRight>(
                    parentBuilder,
                    selectorExpression,
                    source,
                    RequireSemanticDescriptor(new Selector<T>().Prop(property)),
                    operation);
            }

            private NumericPropertyPathArithmeticSelector<decimal> BeginPropertyArithmetic(
                string propertyPath,
                NumericArithmeticOperationKind operation)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(propertyPath);
                IBackendNeutralSelectorDescriptor source = GetSemanticValueDescriptor()
                    ?? throw new NotSupportedException("Decimal property arithmetic requires complete left semantic intent.");
                return new NumericPropertyPathArithmeticSelector<decimal>(
                    parentBuilder,
                    selectorExpression,
                    source,
                    propertyPath,
                    operation);
            }

            /// <summary>Begins decimal-left addition against a typed numeric property while preserving both authored operand types.<br/></summary>
            /// <typeparam name="TRight">Declared right property type.<br/></typeparam>
            /// <param name="property">Typed same-record right property.<br/></param>
            /// <returns>A semantic stage requiring an explicit <c>ResultAs...</c> domain.<br/></returns>
            public NumericPropertyArithmeticSelector<decimal, TRight> PropAdd<TRight>(Expression<Func<T, TRight>> property) where TRight : INumber<TRight> => BeginPropertyArithmetic(property, NumericArithmeticOperationKind.Add);

            /// <summary>Begins decimal-left addition against a textual same-record property path.<br/></summary>
            /// <param name="propertyPath">Abraxas right-property path.<br/></param>
            /// <returns>A stage that first records the right property's authored numeric type.<br/></returns>
            public NumericPropertyPathArithmeticSelector<decimal> PropAdd(string propertyPath) => BeginPropertyArithmetic(propertyPath, NumericArithmeticOperationKind.Add);

            /// <summary>Begins decimal-left subtraction against a typed numeric property without reading or converting values during construction.<br/></summary>
            /// <typeparam name="TRight">Declared right property type.<br/></typeparam>
            /// <param name="property">Typed same-record right property.<br/></param>
            /// <returns>A semantic stage requiring an explicit result domain.<br/></returns>
            public NumericPropertyArithmeticSelector<decimal, TRight> PropSubtract<TRight>(Expression<Func<T, TRight>> property) where TRight : INumber<TRight> => BeginPropertyArithmetic(property, NumericArithmeticOperationKind.Subtract);

            /// <summary>Begins decimal-left subtraction against a textual same-record property path.<br/></summary>
            /// <param name="propertyPath">Abraxas right-property path.<br/></param>
            /// <returns>A right-type completion stage.<br/></returns>
            public NumericPropertyPathArithmeticSelector<decimal> PropSubtract(string propertyPath) => BeginPropertyArithmetic(propertyPath, NumericArithmeticOperationKind.Subtract);

            /// <summary>Begins decimal-left multiplication by a typed numeric property while retaining independent operand types.<br/></summary>
            /// <typeparam name="TRight">Declared right property type.<br/></typeparam>
            /// <param name="property">Typed same-record right property.<br/></param>
            /// <returns>A semantic stage requiring an explicit result domain.<br/></returns>
            public NumericPropertyArithmeticSelector<decimal, TRight> PropMultiply<TRight>(Expression<Func<T, TRight>> property) where TRight : INumber<TRight> => BeginPropertyArithmetic(property, NumericArithmeticOperationKind.Multiply);

            /// <summary>Begins decimal-left multiplication by a textual same-record property path.<br/></summary>
            /// <param name="propertyPath">Abraxas right-property path.<br/></param>
            /// <returns>A right-type completion stage.<br/></returns>
            public NumericPropertyPathArithmeticSelector<decimal> PropMultiply(string propertyPath) => BeginPropertyArithmetic(propertyPath, NumericArithmeticOperationKind.Multiply);

            /// <summary>Begins decimal-left division by a typed numeric property; zero handling remains an execution-time semantic result.<br/></summary>
            /// <typeparam name="TRight">Declared divisor property type.<br/></typeparam>
            /// <param name="property">Typed same-record divisor property.<br/></param>
            /// <returns>A semantic stage requiring an explicit result domain.<br/></returns>
            public NumericPropertyArithmeticSelector<decimal, TRight> PropDivide<TRight>(Expression<Func<T, TRight>> property) where TRight : INumber<TRight> => BeginPropertyArithmetic(property, NumericArithmeticOperationKind.Divide);

            /// <summary>Begins decimal-left division by a textual same-record property path.<br/></summary>
            /// <param name="propertyPath">Abraxas divisor-property path.<br/></param>
            /// <returns>A right-type completion stage.<br/></returns>
            public NumericPropertyPathArithmeticSelector<decimal> PropDivide(string propertyPath) => BeginPropertyArithmetic(propertyPath, NumericArithmeticOperationKind.Divide);

            /// <summary>Begins decimal-left remainder arithmetic against a typed numeric property.<br/></summary>
            /// <typeparam name="TRight">Declared right property type.<br/></typeparam>
            /// <param name="property">Typed same-record right property.<br/></param>
            /// <returns>A semantic stage requiring an explicit result domain.<br/></returns>
            public NumericPropertyArithmeticSelector<decimal, TRight> PropModulo<TRight>(Expression<Func<T, TRight>> property) where TRight : INumber<TRight> => BeginPropertyArithmetic(property, NumericArithmeticOperationKind.Modulo);

            /// <summary>Begins decimal-left remainder arithmetic against a textual same-record property path.<br/></summary>
            /// <param name="propertyPath">Abraxas right-property path.<br/></param>
            /// <returns>A right-type completion stage.<br/></returns>
            public NumericPropertyPathArithmeticSelector<decimal> PropModulo(string propertyPath) => BeginPropertyArithmetic(propertyPath, NumericArithmeticOperationKind.Modulo);

            /// <summary>
            /// Begins reducing the current decimal value by a typed same-record numeric rate property.<br/>
            /// The resulting semantic stage requires an explicit <c>ResultAs...</c> domain so cross-type rate coercion remains a visible developer choice at execution.<br/>
            /// </summary>
            /// <typeparam name="TRight">Declared CLR type of the rate property.<br/></typeparam>
            /// <param name="property">Typed same-record property that supplies the discount rate.<br/></param>
            /// <returns>An immutable arithmetic stage awaiting its explicit result domain.<br/></returns>
            public NumericPropertyArithmeticSelector<decimal, TRight> PropDiscount<TRight>(Expression<Func<T, TRight>> property)
                where TRight : INumber<TRight>
                => BeginPropertyArithmetic(property, NumericArithmeticOperationKind.Discount);

            /// <summary>
            /// Begins reducing the current decimal value by a rate selected through a textual property path.<br/>
            /// The next <c>As...</c> member records the authored rate type and the subsequent <c>ResultAs...</c> member records the checked execution domain.<br/>
            /// </summary>
            /// <param name="propertyPath">Abraxas property path that supplies the discount rate.<br/></param>
            /// <returns>A property-type completion stage.<br/></returns>
            public NumericPropertyPathArithmeticSelector<decimal> PropDiscount(string propertyPath)
                => BeginPropertyArithmetic(propertyPath, NumericArithmeticOperationKind.Discount);
        }

        /// <summary>
        /// Textual right-property type completion for numeric property arithmetic.<br/>
        /// The chosen <c>As...</c> member records the right operand's authored type without selecting or converting the result domain.<br/>
        /// </summary>
        /// <typeparam name="TLeft">Declared left operand type.<br/></typeparam>
        public sealed class NumericPropertyPathArithmeticSelector<TLeft>
            where TLeft : INumber<TLeft>
        {
            private readonly Builder parentBuilder;
            private readonly ValueOrExpression<string> selectorExpression;
            private readonly IBackendNeutralSelectorDescriptor source;
            private readonly string propertyPath;
            private readonly NumericArithmeticOperationKind operation;

            internal NumericPropertyPathArithmeticSelector(
                Builder parentBuilder,
                ValueOrExpression<string> selectorExpression,
                IBackendNeutralSelectorDescriptor source,
                string propertyPath,
                NumericArithmeticOperationKind operation)
            {
                this.parentBuilder = parentBuilder;
                this.selectorExpression = selectorExpression;
                this.source = source;
                this.propertyPath = propertyPath;
                this.operation = operation;
            }

            private NumericPropertyArithmeticSelector<TLeft, TRight> As<TRight>(ISelector right)
                where TRight : INumber<TRight>
                => new(parentBuilder, selectorExpression, source, RequireSemanticDescriptor(right), operation);

            /// <summary>
            /// Declares the right property's Byte type before choosing an arithmetic result domain.<br/>
            /// </summary>
            /// <returns>An arithmetic stage awaiting an explicit ResultAs member.<br/></returns>
            /// <remarks>
            /// This is type completion for a textual property path, not an immediate conversion of a stored value.<br/>
            /// </remarks>
            public NumericPropertyArithmeticSelector<TLeft, byte> AsByte => As<byte>(new Selector<T>().PropPath(propertyPath).AsByte);
            /// <summary>
            /// Declares the right property's SByte type before choosing an arithmetic result domain.<br/>
            /// </summary>
            /// <returns>An arithmetic stage awaiting an explicit ResultAs member.<br/></returns>
            /// <remarks>
            /// This is type completion for a textual property path, not an immediate conversion of a stored value.<br/>
            /// </remarks>
            public NumericPropertyArithmeticSelector<TLeft, sbyte> AsSByte => As<sbyte>(new Selector<T>().PropPath(propertyPath).AsSByte);
            /// <summary>
            /// Declares the right property's Int16 type before choosing an arithmetic result domain.<br/>
            /// </summary>
            /// <returns>An arithmetic stage awaiting an explicit ResultAs member.<br/></returns>
            /// <remarks>
            /// This is type completion for a textual property path, not an immediate conversion of a stored value.<br/>
            /// </remarks>
            public NumericPropertyArithmeticSelector<TLeft, short> AsInt16 => As<short>(new Selector<T>().PropPath(propertyPath).AsInt16);
            /// <summary>
            /// Declares the right property's UInt16 type before choosing an arithmetic result domain.<br/>
            /// </summary>
            /// <returns>An arithmetic stage awaiting an explicit ResultAs member.<br/></returns>
            /// <remarks>
            /// This is type completion for a textual property path, not an immediate conversion of a stored value.<br/>
            /// </remarks>
            public NumericPropertyArithmeticSelector<TLeft, ushort> AsUInt16 => As<ushort>(new Selector<T>().PropPath(propertyPath).AsUInt16);
            /// <summary>
            /// Declares the right property's Int32 type before choosing an arithmetic result domain.<br/>
            /// </summary>
            /// <returns>An arithmetic stage awaiting an explicit ResultAs member.<br/></returns>
            /// <remarks>
            /// This is type completion for a textual property path, not an immediate conversion of a stored value.<br/>
            /// </remarks>
            public NumericPropertyArithmeticSelector<TLeft, int> AsInt32 => As<int>(new Selector<T>().PropPath(propertyPath).AsInt32);
            /// <summary>
            /// Declares the right property's UInt32 type before choosing an arithmetic result domain.<br/>
            /// </summary>
            /// <returns>An arithmetic stage awaiting an explicit ResultAs member.<br/></returns>
            /// <remarks>
            /// This is type completion for a textual property path, not an immediate conversion of a stored value.<br/>
            /// </remarks>
            public NumericPropertyArithmeticSelector<TLeft, uint> AsUInt32 => As<uint>(new Selector<T>().PropPath(propertyPath).AsUInt32);
            /// <summary>
            /// Declares the right property's Int64 type before choosing an arithmetic result domain.<br/>
            /// </summary>
            /// <returns>An arithmetic stage awaiting an explicit ResultAs member.<br/></returns>
            /// <remarks>
            /// This is type completion for a textual property path, not an immediate conversion of a stored value.<br/>
            /// </remarks>
            public NumericPropertyArithmeticSelector<TLeft, long> AsInt64 => As<long>(new Selector<T>().PropPath(propertyPath).AsInt64);
            /// <summary>
            /// Declares the right property's UInt64 type before choosing an arithmetic result domain.<br/>
            /// </summary>
            /// <returns>An arithmetic stage awaiting an explicit ResultAs member.<br/></returns>
            /// <remarks>
            /// This is type completion for a textual property path, not an immediate conversion of a stored value.<br/>
            /// </remarks>
            public NumericPropertyArithmeticSelector<TLeft, ulong> AsUInt64 => As<ulong>(new Selector<T>().PropPath(propertyPath).AsUInt64);
            /// <summary>
            /// Declares the right property's Int128 type before choosing an arithmetic result domain.<br/>
            /// </summary>
            /// <returns>An arithmetic stage awaiting an explicit ResultAs member.<br/></returns>
            /// <remarks>
            /// This is type completion for a textual property path, not an immediate conversion of a stored value.<br/>
            /// </remarks>
            public NumericPropertyArithmeticSelector<TLeft, Int128> AsInt128 => As<Int128>(new Selector<T>().PropPath(propertyPath).AsInt128);
            /// <summary>
            /// Declares the right property's UInt128 type before choosing an arithmetic result domain.<br/>
            /// </summary>
            /// <returns>An arithmetic stage awaiting an explicit ResultAs member.<br/></returns>
            /// <remarks>
            /// This is type completion for a textual property path, not an immediate conversion of a stored value.<br/>
            /// </remarks>
            public NumericPropertyArithmeticSelector<TLeft, UInt128> AsUInt128 => As<UInt128>(new Selector<T>().PropPath(propertyPath).AsUInt128);
            /// <summary>
            /// Declares the right property's BigInteger type before choosing an arithmetic result domain.<br/>
            /// </summary>
            /// <returns>An arithmetic stage awaiting an explicit ResultAs member.<br/></returns>
            /// <remarks>
            /// This is type completion for a textual property path, not an immediate conversion of a stored value.<br/>
            /// </remarks>
            public NumericPropertyArithmeticSelector<TLeft, BigInteger> AsBigInteger => As<BigInteger>(new Selector<T>().PropPath(propertyPath).AsBigInteger);
            /// <summary>
            /// Declares the right property's Half type before choosing an arithmetic result domain.<br/>
            /// </summary>
            /// <returns>An arithmetic stage awaiting an explicit ResultAs member.<br/></returns>
            /// <remarks>
            /// This is type completion for a textual property path, not an immediate conversion of a stored value.<br/>
            /// </remarks>
            public NumericPropertyArithmeticSelector<TLeft, Half> AsHalf => As<Half>(new Selector<T>().PropPath(propertyPath).AsHalf);
            /// <summary>
            /// Declares the right property's Single type before choosing an arithmetic result domain.<br/>
            /// </summary>
            /// <returns>An arithmetic stage awaiting an explicit ResultAs member.<br/></returns>
            /// <remarks>
            /// This is type completion for a textual property path, not an immediate conversion of a stored value.<br/>
            /// </remarks>
            public NumericPropertyArithmeticSelector<TLeft, float> AsSingle => As<float>(new Selector<T>().PropPath(propertyPath).AsSingle);
            /// <summary>
            /// Declares the right property's Double type before choosing an arithmetic result domain.<br/>
            /// </summary>
            /// <returns>An arithmetic stage awaiting an explicit ResultAs member.<br/></returns>
            /// <remarks>
            /// This is type completion for a textual property path, not an immediate conversion of a stored value.<br/>
            /// </remarks>
            public NumericPropertyArithmeticSelector<TLeft, double> AsDouble => As<double>(new Selector<T>().PropPath(propertyPath).AsDouble);
            /// <summary>
            /// Declares the right property's Decimal type before choosing an arithmetic result domain.<br/>
            /// </summary>
            /// <returns>An arithmetic stage awaiting an explicit ResultAs member.<br/></returns>
            /// <remarks>
            /// This is type completion for a textual property path, not an immediate conversion of a stored value.<br/>
            /// </remarks>
            public NumericPropertyArithmeticSelector<TLeft, decimal> AsDecimal => As<decimal>(new Selector<T>().PropPath(propertyPath).AsDecimal);
        }

        /// <summary>
        /// Immutable numeric property arithmetic awaiting an explicit checked result domain.<br/>
        /// Selecting <c>ResultAs...</c> freezes the execution type but performs no record read or value conversion until the selector is consumed.<br/>
        /// </summary>
        /// <typeparam name="TLeft">Exact authored left operand type.<br/></typeparam>
        /// <typeparam name="TRight">Exact authored right operand type.<br/></typeparam>
        public sealed class NumericPropertyArithmeticSelector<TLeft, TRight>
            where TLeft : INumber<TLeft>
            where TRight : INumber<TRight>
        {
            private readonly Builder parentBuilder;
            private readonly ValueOrExpression<string> selectorExpression;
            private readonly IBackendNeutralSelectorDescriptor source;
            private readonly IBackendNeutralSelectorDescriptor right;
            private readonly NumericArithmeticOperationKind operation;

            internal NumericPropertyArithmeticSelector(
                Builder parentBuilder,
                ValueOrExpression<string> selectorExpression,
                IBackendNeutralSelectorDescriptor source,
                IBackendNeutralSelectorDescriptor right,
                NumericArithmeticOperationKind operation)
            {
                this.parentBuilder = parentBuilder;
                this.selectorExpression = selectorExpression;
                this.source = source;
                this.right = right;
                this.operation = operation;
            }

            private IBackendNeutralSelectorDescriptor Describe<TResult>() where TResult : INumber<TResult>
            {
                parentBuilder.MarkComputed();
                return new NumericPropertyArithmeticSelectorDescriptor<TLeft, TRight, TResult>(source, operation, right);
            }

            private IntegralSelector<TResult> AsIntegral<TResult>() where TResult : INumber<TResult>, IBinaryInteger<TResult>
                => new(parentBuilder, selectorExpression, addBase: false, Describe<TResult>());

            private RealSelector<TResult> AsReal<TResult>() where TResult : INumber<TResult>
                => new(parentBuilder, selectorExpression, addBase: false, Describe<TResult>());

            /// <summary>
            /// Completes property-to-property arithmetic in the explicit Byte result domain.<br/>
            /// </summary>
            /// <returns>A typed selector for the completed arithmetic expression.<br/></returns>
            /// <remarks>
            /// Both operands retain their authored types and are converted with checked generic math when evaluated; this member performs no record read or value conversion.<br/>
            /// </remarks>
            public IntegralSelector<byte> ResultAsByte => AsIntegral<byte>();
            /// <summary>
            /// Completes property-to-property arithmetic in the explicit SByte result domain.<br/>
            /// </summary>
            /// <returns>A typed selector for the completed arithmetic expression.<br/></returns>
            /// <remarks>
            /// Both operands retain their authored types and are converted with checked generic math when evaluated; this member performs no record read or value conversion.<br/>
            /// </remarks>
            public IntegralSelector<sbyte> ResultAsSByte => AsIntegral<sbyte>();
            /// <summary>
            /// Completes property-to-property arithmetic in the explicit Int16 result domain.<br/>
            /// </summary>
            /// <returns>A typed selector for the completed arithmetic expression.<br/></returns>
            /// <remarks>
            /// Both operands retain their authored types and are converted with checked generic math when evaluated; this member performs no record read or value conversion.<br/>
            /// </remarks>
            public IntegralSelector<short> ResultAsInt16 => AsIntegral<short>();
            /// <summary>
            /// Completes property-to-property arithmetic in the explicit UInt16 result domain.<br/>
            /// </summary>
            /// <returns>A typed selector for the completed arithmetic expression.<br/></returns>
            /// <remarks>
            /// Both operands retain their authored types and are converted with checked generic math when evaluated; this member performs no record read or value conversion.<br/>
            /// </remarks>
            public IntegralSelector<ushort> ResultAsUInt16 => AsIntegral<ushort>();
            /// <summary>
            /// Completes property-to-property arithmetic in the explicit Int32 result domain.<br/>
            /// </summary>
            /// <returns>A typed selector for the completed arithmetic expression.<br/></returns>
            /// <remarks>
            /// Both operands retain their authored types and are converted with checked generic math when evaluated; this member performs no record read or value conversion.<br/>
            /// </remarks>
            public IntegralSelector<int> ResultAsInt32 => AsIntegral<int>();
            /// <summary>
            /// Completes property-to-property arithmetic in the explicit UInt32 result domain.<br/>
            /// </summary>
            /// <returns>A typed selector for the completed arithmetic expression.<br/></returns>
            /// <remarks>
            /// Both operands retain their authored types and are converted with checked generic math when evaluated; this member performs no record read or value conversion.<br/>
            /// </remarks>
            public IntegralSelector<uint> ResultAsUInt32 => AsIntegral<uint>();
            /// <summary>
            /// Completes property-to-property arithmetic in the explicit Int64 result domain.<br/>
            /// </summary>
            /// <returns>A typed selector for the completed arithmetic expression.<br/></returns>
            /// <remarks>
            /// Both operands retain their authored types and are converted with checked generic math when evaluated; this member performs no record read or value conversion.<br/>
            /// </remarks>
            public IntegralSelector<long> ResultAsInt64 => AsIntegral<long>();
            /// <summary>
            /// Completes property-to-property arithmetic in the explicit UInt64 result domain.<br/>
            /// </summary>
            /// <returns>A typed selector for the completed arithmetic expression.<br/></returns>
            /// <remarks>
            /// Both operands retain their authored types and are converted with checked generic math when evaluated; this member performs no record read or value conversion.<br/>
            /// </remarks>
            public IntegralSelector<ulong> ResultAsUInt64 => AsIntegral<ulong>();
            /// <summary>
            /// Completes property-to-property arithmetic in the explicit Int128 result domain.<br/>
            /// </summary>
            /// <returns>A typed selector for the completed arithmetic expression.<br/></returns>
            /// <remarks>
            /// Both operands retain their authored types and are converted with checked generic math when evaluated; this member performs no record read or value conversion.<br/>
            /// </remarks>
            public IntegralSelector<Int128> ResultAsInt128 => AsIntegral<Int128>();
            /// <summary>
            /// Completes property-to-property arithmetic in the explicit UInt128 result domain.<br/>
            /// </summary>
            /// <returns>A typed selector for the completed arithmetic expression.<br/></returns>
            /// <remarks>
            /// Both operands retain their authored types and are converted with checked generic math when evaluated; this member performs no record read or value conversion.<br/>
            /// </remarks>
            public IntegralSelector<UInt128> ResultAsUInt128 => AsIntegral<UInt128>();
            /// <summary>
            /// Completes property-to-property arithmetic in the explicit BigInteger result domain.<br/>
            /// </summary>
            /// <returns>A typed selector for the completed arithmetic expression.<br/></returns>
            /// <remarks>
            /// Both operands retain their authored types and are converted with checked generic math when evaluated; this member performs no record read or value conversion.<br/>
            /// </remarks>
            public IntegralSelector<BigInteger> ResultAsBigInteger => AsIntegral<BigInteger>();
            /// <summary>
            /// Completes property-to-property arithmetic in the explicit Half result domain.<br/>
            /// </summary>
            /// <returns>A typed selector for the completed arithmetic expression.<br/></returns>
            /// <remarks>
            /// Both operands retain their authored types and are converted with checked generic math when evaluated; this member performs no record read or value conversion.<br/>
            /// </remarks>
            public RealSelector<Half> ResultAsHalf => AsReal<Half>();
            /// <summary>
            /// Completes property-to-property arithmetic in the explicit Single result domain.<br/>
            /// </summary>
            /// <returns>A typed selector for the completed arithmetic expression.<br/></returns>
            /// <remarks>
            /// Both operands retain their authored types and are converted with checked generic math when evaluated; this member performs no record read or value conversion.<br/>
            /// </remarks>
            public RealSelector<float> ResultAsSingle => AsReal<float>();
            /// <summary>
            /// Completes property-to-property arithmetic in the explicit Double result domain.<br/>
            /// </summary>
            /// <returns>A typed selector for the completed arithmetic expression.<br/></returns>
            /// <remarks>
            /// Both operands retain their authored types and are converted with checked generic math when evaluated; this member performs no record read or value conversion.<br/>
            /// </remarks>
            public RealSelector<double> ResultAsDouble => AsReal<double>();
            /// <summary>
            /// Completes property-to-property arithmetic in the explicit Decimal result domain.<br/>
            /// </summary>
            /// <returns>A typed selector for the completed arithmetic expression.<br/></returns>
            /// <remarks>
            /// Both operands retain their authored types and are converted with checked generic math when evaluated; this member performs no record read or value conversion.<br/>
            /// </remarks>
            public DecimalSelector ResultAsDecimal
                => new(parentBuilder, selectorExpression, addBase: false, Describe<decimal>());
        }
    }
}
