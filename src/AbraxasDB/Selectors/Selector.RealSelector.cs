using System.Numerics;

namespace AbraxasDB
{

    public partial class Selector<T>
    {
        /// <summary>
        /// A typed real-number selector with composable arithmetic and explicit double promotion.<br/>
        /// </summary>
        /// <typeparam name="TReal">Exact numeric type carried by this selector.<br/></typeparam>
        /// <remarks>
        /// Selector authoring records expression intent; record values are read only when an operation consumes the selector.<br/>
        /// </remarks>
        public class RealSelector<TReal> : NumericSelector<TReal>,ISelector where TReal : INumber<TReal>
        {
            internal RealSelector(Builder parentBuilder, ValueOrExpression<string> selectorExpression, bool addBase = true) : base(parentBuilder, selectorExpression, addBase) { }

            /// <summary>
            /// This internal path is used by typed projections such as TimeSpan totals without widening the public construction surface.<br/>
            /// </summary>
            /// <param name="selectorExpression">Base selector expression used only by the retained renderer.<br/></param>
            /// <param name="addBase">Whether to append the base selector part.<br/></param>
            /// <param name="semanticDescriptor">Immutable direct-or-computed selector descriptor.<br/></param>
            /// <param name="parentBuilder">The owning selector builder from which this direct or computed real-number selector is constructed.<br/></param>
            internal RealSelector(
                Builder parentBuilder,
                ValueOrExpression<string> selectorExpression,
                bool addBase,
                IBackendNeutralSelectorDescriptor? semanticDescriptor)
                : base(parentBuilder, selectorExpression, addBase, semanticDescriptor)
            {
            }

            /// <summary>
            /// Describes <c>left + right</c> over the selected real-number value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;TReal&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<TReal> Add(TReal value)
                => ApplySameTypeLiteral(NumericArithmeticOperationKind.Add, value);

            /// <summary>
            /// Describes <c>left + right</c> over the selected real-number value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;TReal&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<TReal> Add(ulong value)
                => ApplyIntegralLiteral(NumericArithmeticOperationKind.Add, value);

            /// <summary>
            /// Describes <c>left + right</c> over the selected real-number value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;TReal&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<TReal> Add(long value)
                => ApplyIntegralLiteral(NumericArithmeticOperationKind.Add, value);

            /// <summary>
            /// Describes <c>left + right</c> over the selected real-number value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;double&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<double> Add(double value)
                => ApplyDoubleLiteral(NumericArithmeticOperationKind.Add, value);

            /// <summary>
            /// Describes <c>left + right</c> over the selected real-number value.<br/>
            /// </summary>
            /// <param name="rhs">Selector supplying the right operand from the same record.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;TReal&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<TReal> Add(RealSelector<TReal> rhs)
                => ApplySelector(NumericArithmeticOperationKind.Add, rhs);

            /// <summary>
            /// Describes <c>left - right</c> over the selected real-number value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;TReal&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<TReal> Subtract(TReal value)
                => ApplySameTypeLiteral(NumericArithmeticOperationKind.Subtract, value);

            /// <summary>
            /// Describes <c>left - right</c> over the selected real-number value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;TReal&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<TReal> Subtract(ulong value)
                => ApplyIntegralLiteral(NumericArithmeticOperationKind.Subtract, value);

            /// <summary>
            /// Describes <c>left - right</c> over the selected real-number value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;TReal&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<TReal> Subtract(long value)
                => ApplyIntegralLiteral(NumericArithmeticOperationKind.Subtract, value);

            /// <summary>
            /// Describes <c>left - right</c> over the selected real-number value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;double&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<double> Subtract(double value)
                => ApplyDoubleLiteral(NumericArithmeticOperationKind.Subtract, value);

            /// <summary>
            /// Describes <c>left - right</c> over the selected real-number value.<br/>
            /// </summary>
            /// <param name="rhs">Selector supplying the right operand from the same record.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;TReal&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<TReal> Subtract(RealSelector<TReal> rhs)
                => ApplySelector(NumericArithmeticOperationKind.Subtract, rhs);

            /// <summary>
            /// Describes <c>left * right</c> over the selected real-number value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;TReal&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<TReal> Multiply(TReal value)
                => ApplySameTypeLiteral(NumericArithmeticOperationKind.Multiply, value);

            /// <summary>
            /// Describes <c>left * right</c> over the selected real-number value.<br/>
            /// </summary>
            /// <param name="rhs">Selector supplying the right operand from the same record.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;TReal&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<TReal> Multiply(RealSelector<TReal> rhs)
                => ApplySelector(NumericArithmeticOperationKind.Multiply, rhs);

            /// <summary>
            /// Describes <c>left * right</c> over the selected real-number value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;TReal&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<TReal> Multiply(ulong value)
                => ApplyIntegralLiteral(NumericArithmeticOperationKind.Multiply, value);

            /// <summary>
            /// Describes <c>left * right</c> over the selected real-number value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;double&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<double> Multiply(double value)
                => ApplyDoubleLiteral(NumericArithmeticOperationKind.Multiply, value);

            /// <summary>
            /// Describes <c>left * right</c> over the selected real-number value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;TReal&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<TReal> Multiply(long value)
                => ApplyIntegralLiteral(NumericArithmeticOperationKind.Multiply, value);

            /// <summary>
            /// Describes <c>left / right</c> over the selected real-number value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;TReal&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<TReal> Divide(TReal value)
                => ApplySameTypeLiteral(NumericArithmeticOperationKind.Divide, value);

            /// <summary>
            /// Describes <c>left / right</c> over the selected real-number value.<br/>
            /// </summary>
            /// <param name="rhs">Selector supplying the right operand from the same record.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;TReal&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<TReal> Divide(RealSelector<TReal> rhs)
                => ApplySelector(NumericArithmeticOperationKind.Divide, rhs);

            /// <summary>
            /// Describes <c>left / right</c> over the selected real-number value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;TReal&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<TReal> Divide(ulong value)
                => ApplyIntegralLiteral(NumericArithmeticOperationKind.Divide, value);

            /// <summary>
            /// Describes <c>left / right</c> over the selected real-number value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;double&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<double> Divide(double value)
                => ApplyDoubleLiteral(NumericArithmeticOperationKind.Divide, value);

            /// <summary>
            /// Describes <c>left / right</c> over the selected real-number value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;TReal&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<TReal> Divide(long value)
                => ApplyIntegralLiteral(NumericArithmeticOperationKind.Divide, value);

            /// <summary>
            /// Describes <c>left % right</c> over the selected real-number value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;TReal&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<TReal> Modulo(TReal value)
                => ApplySameTypeLiteral(NumericArithmeticOperationKind.Modulo, value);

            /// <summary>
            /// Describes <c>left % right</c> over the selected real-number value.<br/>
            /// </summary>
            /// <param name="rhs">Selector supplying the right operand from the same record.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;TReal&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<TReal> Modulo(RealSelector<TReal> rhs)
                => ApplySelector(NumericArithmeticOperationKind.Modulo, rhs);

            /// <summary>
            /// Describes <c>left % right</c> over the selected real-number value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;TReal&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<TReal> Modulo(ulong value)
                => ApplyIntegralLiteral(NumericArithmeticOperationKind.Modulo, value);

            /// <summary>
            /// Describes <c>left % right</c> over the selected real-number value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;double&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<double> Modulo(double value)
                => ApplyDoubleLiteral(NumericArithmeticOperationKind.Modulo, value);

            /// <summary>
            /// Describes <c>left % right</c> over the selected real-number value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;TReal&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<TReal> Modulo(long value)
                => ApplyIntegralLiteral(NumericArithmeticOperationKind.Modulo, value);

            /// <summary>
            /// Describes reducing the current real value by a same-type rate without calculating or coercing it while the selector graph is built.<br/>
            /// Execution evaluates <c>value * (1 - rate)</c>; finite-result and checked-conversion rules remain identical to the other real arithmetic operations.<br/>
            /// </summary>
            /// <param name="rate">Discount rate expressed in the selector's real-number domain.<br/></param>
            /// <returns>A new immutable real selector containing the discount operation.<br/></returns>
            public RealSelector<TReal> Discount(TReal rate)
                => ApplySameTypeLiteral(NumericArithmeticOperationKind.Discount, rate);

            /// <summary>
            /// Describes reducing the current real value by a double rate, promoting narrower real sources into the double execution domain.<br/>
            /// No value is read or calculated until the selector is consumed.<br/>
            /// </summary>
            /// <param name="rate">Double discount rate.<br/></param>
            /// <returns>A new immutable double selector containing the promoted discount operation.<br/></returns>
            public RealSelector<double> Discount(double rate)
                => ApplyDoubleLiteral(NumericArithmeticOperationKind.Discount, rate);

            /// <summary>
            /// Describes reducing the current real value by a reusable same-type rate selector.<br/>
            /// The complete right expression is retained as semantic intent and is evaluated against the same record as the left expression.<br/>
            /// </summary>
            /// <param name="rate">Selector that supplies the discount rate.<br/></param>
            /// <returns>A new immutable real selector containing both value and rate intent.<br/></returns>
            public RealSelector<TReal> Discount(RealSelector<TReal> rate)
                => ApplySelector(NumericArithmeticOperationKind.Discount, rate);

            /// <summary>
            /// Begins a discount whose rate comes from a typed same-record numeric property.<br/>
            /// The authored left and right types remain semantic metadata until the developer chooses the checked <c>ResultAs...</c> execution domain.<br/>
            /// </summary>
            /// <typeparam name="TRight">Declared CLR type of the rate property.<br/></typeparam>
            /// <param name="property">Typed same-record property that supplies the discount rate.<br/></param>
            /// <returns>An immutable arithmetic stage awaiting its explicit result domain.<br/></returns>
            public NumericPropertyArithmeticSelector<TReal, TRight> PropDiscount<TRight>(
                System.Linq.Expressions.Expression<Func<T, TRight>> property)
                where TRight : INumber<TRight>
                => BeginPropertyArithmetic(property, NumericArithmeticOperationKind.Discount);

            /// <summary>
            /// Begins a discount whose rate comes from a textual same-record property path.<br/>
            /// Choose the rate property's authored <c>As...</c> type and then the checked <c>ResultAs...</c> execution domain; selector construction itself performs no read or conversion.<br/>
            /// </summary>
            /// <param name="propertyPath">Abraxas property path that supplies the discount rate.<br/></param>
            /// <returns>A property-type completion stage.<br/></returns>
            public NumericPropertyPathArithmeticSelector<TReal> PropDiscount(string propertyPath)
                => BeginPropertyArithmetic(propertyPath, NumericArithmeticOperationKind.Discount);

            /// <summary>
            /// Describes <c>-value</c> over the selected real-number value.<br/>
            /// </summary>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;TReal&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<TReal> Negate()
            {
                var source = GetSemanticValueDescriptor();
                parentBuilder.MarkComputed();
                var descriptor = source is null
                    ? null
                    : new NumericArithmeticSelectorDescriptor<TReal, TReal>(
                        source,
                        NumericArithmeticOperationKind.Negate);
                return new RealSelector<TReal>(
                    parentBuilder,
                    selectorExpression,
                    addBase: false,
                    descriptor);
            }

            /// <summary>
            /// Adds one literal already expressed in the selector's real result type.<br/>
            /// </summary>
            private RealSelector<TReal> ApplySameTypeLiteral(
                NumericArithmeticOperationKind operation,
                TReal value)
            {
                var source = GetSemanticValueDescriptor();
                parentBuilder.MarkComputed();
                var descriptor = source is null
                    ? null
                    : new NumericArithmeticSelectorDescriptor<TReal, TReal>(
                        source,
                        operation,
                        value);
                return new RealSelector<TReal>(
                    parentBuilder,
                    selectorExpression,
                    addBase: false,
                    descriptor);
            }

            /// <summary>
            /// Adds one integral literal while retaining the real source family and precision contract.<br/>
            /// </summary>
            private RealSelector<TReal> ApplyIntegralLiteral<TInteger>(
                NumericArithmeticOperationKind operation,
                TInteger value)
                where TInteger : INumber<TInteger>, IBinaryInteger<TInteger>
            {
                var promoted = TReal.CreateChecked(value);
                return ApplySameTypeLiteral(operation, promoted);
            }

            /// <summary>
            /// Adds one double literal and promotes a narrower real source to a double result.<br/>
            /// </summary>
            private RealSelector<double> ApplyDoubleLiteral(
                NumericArithmeticOperationKind operation,
                double value)
            {
                var source = GetSemanticValueDescriptor();
                parentBuilder.MarkComputed();
                var descriptor = source is null
                    ? null
                    : new NumericArithmeticSelectorDescriptor<TReal, double>(
                        source,
                        operation,
                        value);
                return new RealSelector<double>(
                    parentBuilder,
                    selectorExpression,
                    addBase: false,
                    descriptor);
            }

            /// <summary>
            /// Adds one same-type real selector while preserving its complete recursive expression as the right operand.<br/>
            /// </summary>
            private RealSelector<TReal> ApplySelector(
                NumericArithmeticOperationKind operation,
                RealSelector<TReal> rhs)
            {
                ArgumentNullException.ThrowIfNull(rhs);
                var source = GetSemanticValueDescriptor();
                var rightSource = rhs.GetSemanticValueDescriptor();
                parentBuilder.MarkComputed();
                var descriptor = source is null || rightSource is null
                    ? null
                    : new NumericArithmeticSelectorDescriptor<TReal, TReal>(
                        source,
                        operation,
                        rightSource);
                return new RealSelector<TReal>(
                    parentBuilder,
                    selectorExpression,
                    addBase: false,
                    descriptor);
            }

        }
    }
}
