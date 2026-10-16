using System.Numerics;

namespace AbraxasDB
{

    public partial class Selector<T>
    {
        /// <summary>
        /// A typed integral selector with checked arithmetic and explicit result-type promotion.<br/>
        /// </summary>
        /// <typeparam name="TInt">Exact numeric type carried by this selector.<br/></typeparam>
        /// <remarks>
        /// Selector authoring records expression intent; record values are read only when an operation consumes the selector.<br/>
        /// </remarks>
        public class IntegralSelector<TInt> : NumericSelector<TInt>,ISelector where TInt : INumber<TInt>, System.Numerics.IBinaryInteger<TInt>
        {
            internal IntegralSelector(Builder parentBuilder, ValueOrExpression<string> selectorExpression, bool addBase = true) : base(parentBuilder, selectorExpression, addBase) { }

            /// <summary>
            /// Creates an integral selector that carries immutable backend-neutral computed intent from an operation whose result is integral.<br/>
            /// </summary>
            /// <param name="parentBuilder">Selector builder retaining compatibility composition parts.<br/></param>
            /// <param name="selectorExpression">Base selector expression used by the retained renderer.<br/></param>
            /// <param name="addBase">Whether to append a base selector part.<br/></param>
            /// <param name="semanticDescriptor">Frozen backend-neutral computed selector descriptor.<br/></param>
            internal IntegralSelector(
                Builder parentBuilder,
                ValueOrExpression<string> selectorExpression,
                bool addBase,
                IBackendNeutralSelectorDescriptor? semanticDescriptor)
                : base(parentBuilder, selectorExpression, addBase, semanticDescriptor)
            {
            }

            // Arithmetic operations use explicit result promotion and immutable semantic trees.

            /// <summary>
            /// Adds a same-type literal without widening small integral values.<br/>
            /// For constructed <c>long</c> and <c>ulong</c> selectors, the historical exact overload remains the most-specific overload and produces the same corrected result type.<br/>
            /// </summary>
            public IntegralSelector<TInt> Add(TInt value)
                => ApplyIntegralLiteral<TInt>(NumericArithmeticOperationKind.Add, value);

            /// <summary>
            /// Describes <c>left + right</c> over the selected integral value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>IntegralSelector&lt;ulong&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public IntegralSelector<ulong> Add(ulong value)
                => ApplyIntegralLiteral<ulong>(NumericArithmeticOperationKind.Add, value);

            /// <summary>
            /// Describes <c>left + right</c> over the selected integral value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>IntegralSelector&lt;long&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public IntegralSelector<long> Add(long value)
                => ApplyIntegralLiteral<long>(NumericArithmeticOperationKind.Add, value);

            /// <summary>
            /// Describes <c>left + right</c> over the selected integral value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;double&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<double> Add(double value)
                => ApplyRealLiteral(NumericArithmeticOperationKind.Add, value);

            /// <summary>
            /// Describes <c>left + right</c> over the selected integral value.<br/>
            /// </summary>
            /// <param name="rhs">Selector supplying the right operand from the same record.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>IntegralSelector&lt;TInt&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public IntegralSelector<TInt> Add(IntegralSelector<TInt> rhs)
                => ApplyIntegralSelector(NumericArithmeticOperationKind.Add, rhs);

            /// <summary>
            /// Describes <c>left - right</c> over the selected integral value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>IntegralSelector&lt;TInt&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public IntegralSelector<TInt> Subtract(TInt value)
                => ApplyIntegralLiteral<TInt>(NumericArithmeticOperationKind.Subtract, value);

            /// <summary>
            /// Describes <c>left - right</c> over the selected integral value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>IntegralSelector&lt;ulong&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public IntegralSelector<ulong> Subtract(ulong value)
                => ApplyIntegralLiteral<ulong>(NumericArithmeticOperationKind.Subtract, value);

            /// <summary>
            /// Describes <c>left - right</c> over the selected integral value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>IntegralSelector&lt;long&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public IntegralSelector<long> Subtract(long value)
                => ApplyIntegralLiteral<long>(NumericArithmeticOperationKind.Subtract, value);

            /// <summary>
            /// Describes <c>left - right</c> over the selected integral value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;double&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<double> Subtract(double value)
                => ApplyRealLiteral(NumericArithmeticOperationKind.Subtract, value);

            /// <summary>
            /// Describes <c>left - right</c> over the selected integral value.<br/>
            /// </summary>
            /// <param name="rhs">Selector supplying the right operand from the same record.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>IntegralSelector&lt;TInt&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public IntegralSelector<TInt> Subtract(IntegralSelector<TInt> rhs)
                => ApplyIntegralSelector(NumericArithmeticOperationKind.Subtract, rhs);

            /// <summary>
            /// Describes <c>left * right</c> over the selected integral value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>IntegralSelector&lt;TInt&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public IntegralSelector<TInt> Multiply(TInt value)
                => ApplyIntegralLiteral<TInt>(NumericArithmeticOperationKind.Multiply, value);

            /// <summary>
            /// Describes <c>left * right</c> over the selected integral value.<br/>
            /// </summary>
            /// <param name="rhs">Selector supplying the right operand from the same record.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>IntegralSelector&lt;TInt&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public IntegralSelector<TInt> Multiply(IntegralSelector<TInt> rhs)
                => ApplyIntegralSelector(NumericArithmeticOperationKind.Multiply, rhs);

            /// <summary>
            /// Describes <c>left * right</c> over the selected integral value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>IntegralSelector&lt;ulong&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public IntegralSelector<ulong> Multiply(ulong value)
                => ApplyIntegralLiteral<ulong>(NumericArithmeticOperationKind.Multiply, value);

            /// <summary>
            /// Describes <c>left * right</c> over the selected integral value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;double&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<double> Multiply(double value)
                => ApplyRealLiteral(NumericArithmeticOperationKind.Multiply, value);

            /// <summary>
            /// Describes <c>left * right</c> over the selected integral value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>IntegralSelector&lt;long&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public IntegralSelector<long> Multiply(long value)
                => ApplyIntegralLiteral<long>(NumericArithmeticOperationKind.Multiply, value);

            /// <summary>
            /// Describes <c>left / right</c> over the selected integral value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>IntegralSelector&lt;TInt&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public IntegralSelector<TInt> Divide(TInt value)
                => ApplyIntegralLiteral<TInt>(NumericArithmeticOperationKind.Divide, value);

            /// <summary>
            /// Describes <c>left / right</c> over the selected integral value.<br/>
            /// </summary>
            /// <param name="rhs">Selector supplying the right operand from the same record.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>IntegralSelector&lt;TInt&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public IntegralSelector<TInt> Divide(IntegralSelector<TInt> rhs)
                => ApplyIntegralSelector(NumericArithmeticOperationKind.Divide, rhs);

            /// <summary>
            /// Describes <c>left / right</c> over the selected integral value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>IntegralSelector&lt;ulong&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public IntegralSelector<ulong> Divide(ulong value)
                => ApplyIntegralLiteral<ulong>(NumericArithmeticOperationKind.Divide, value);

            /// <summary>
            /// Describes <c>left / right</c> over the selected integral value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;double&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<double> Divide(double value)
                => ApplyRealLiteral(NumericArithmeticOperationKind.Divide, value);

            /// <summary>
            /// Describes <c>left / right</c> over the selected integral value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>IntegralSelector&lt;long&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public IntegralSelector<long> Divide(long value)
                => ApplyIntegralLiteral<long>(NumericArithmeticOperationKind.Divide, value);

            /// <summary>
            /// Describes <c>left % right</c> over the selected integral value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>IntegralSelector&lt;TInt&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public IntegralSelector<TInt> Modulo(TInt value)
                => ApplyIntegralLiteral<TInt>(NumericArithmeticOperationKind.Modulo, value);

            /// <summary>
            /// Describes <c>left % right</c> over the selected integral value.<br/>
            /// </summary>
            /// <param name="rhs">Selector supplying the right operand from the same record.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>IntegralSelector&lt;TInt&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public IntegralSelector<TInt> Modulo(IntegralSelector<TInt> rhs)
                => ApplyIntegralSelector(NumericArithmeticOperationKind.Modulo, rhs);

            /// <summary>
            /// Describes <c>left % right</c> over the selected integral value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>IntegralSelector&lt;ulong&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public IntegralSelector<ulong> Modulo(ulong value)
                => ApplyIntegralLiteral<ulong>(NumericArithmeticOperationKind.Modulo, value);

            /// <summary>
            /// Describes <c>left % right</c> over the selected integral value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>RealSelector&lt;double&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public RealSelector<double> Modulo(double value)
                => ApplyRealLiteral(NumericArithmeticOperationKind.Modulo, value);

            /// <summary>
            /// Describes <c>left % right</c> over the selected integral value.<br/>
            /// </summary>
            /// <param name="value">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>IntegralSelector&lt;long&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public IntegralSelector<long> Modulo(long value)
                => ApplyIntegralLiteral<long>(NumericArithmeticOperationKind.Modulo, value);

            /// <summary>
            /// Describes <c>-value</c> over the selected integral value.<br/>
            /// </summary>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The returned selector declares the operation's result domain: <c>IntegralSelector&lt;TInt&gt;</c>.<br/>
            /// Checked conversion, overflow, zero division or remainder, and non-finite output can make the expression produce no value during evaluation.<br/>
            /// </remarks>
            public IntegralSelector<TInt> Negate()
            {
                var source = GetSemanticValueDescriptor();
                parentBuilder.MarkComputed();

                var descriptor = source is null
                    ? null
                    : new NumericArithmeticSelectorDescriptor<TInt, TInt>(
                        source,
                        NumericArithmeticOperationKind.Negate);
                return new IntegralSelector<TInt>(
                    parentBuilder,
                    selectorExpression,
                    addBase: false,
                    descriptor);
            }

            /// <summary>
            /// Adds one integral literal operation, preserving the existing retained renderer while attaching corrected checked result semantics.<br/>
            /// </summary>
            private IntegralSelector<TResult> ApplyIntegralLiteral<TResult>(
                NumericArithmeticOperationKind operation,
                TResult value)
                where TResult : INumber<TResult>, IBinaryInteger<TResult>
            {
                var source = GetSemanticValueDescriptor();
                parentBuilder.MarkComputed();

                var descriptor = source is null
                    ? null
                    : new NumericArithmeticSelectorDescriptor<TInt, TResult>(
                        source,
                        operation,
                        value);
                return new IntegralSelector<TResult>(
                    parentBuilder,
                    selectorExpression,
                    addBase: false,
                    descriptor);
            }

            /// <summary>
            /// Adds one double literal operation and promotes the integral source to a real result.<br/>
            /// </summary>
            private RealSelector<double> ApplyRealLiteral(
                NumericArithmeticOperationKind operation,
                double value)
            {
                var source = GetSemanticValueDescriptor();
                parentBuilder.MarkComputed();
                var descriptor = source is null
                    ? null
                    : new NumericArithmeticSelectorDescriptor<TInt, double>(
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
            /// Adds one same-type selector operand while preserving the complete recursive right expression in semantic intent.<br/>
            /// </summary>
            private IntegralSelector<TInt> ApplyIntegralSelector(
                NumericArithmeticOperationKind operation,
                IntegralSelector<TInt> rhs)
            {
                ArgumentNullException.ThrowIfNull(rhs);
                var source = GetSemanticValueDescriptor();
                var rightSource = rhs.GetSemanticValueDescriptor();
                parentBuilder.MarkComputed();

                var descriptor = source is null || rightSource is null
                    ? null
                    : new NumericArithmeticSelectorDescriptor<TInt, TInt>(
                        source,
                        operation,
                        rightSource);
                return new IntegralSelector<TInt>(
                    parentBuilder,
                    selectorExpression,
                    addBase: false,
                    descriptor);
            }

            /// <summary>
            /// Applies a same-width bitwise AND mask to this integral selector.<br/>
            /// </summary>
            /// <param name="value">Mask expressed in the selector's exact integral type.<br/></param>
            public IntegralSelector<TInt> BitAnd(TInt value)
                => ApplyBitwiseLiteral(NumericArithmeticOperationKind.BitAnd, value);

            /// <summary>
            /// Applies bitwise AND between this selector and another same-type integral selector.<br/>
            /// </summary>
            /// <param name="rhs">Right selector whose complete immutable descriptor becomes the second operand.<br/></param>
            public IntegralSelector<TInt> BitAnd(IntegralSelector<TInt> rhs)
                => ApplyBitwiseSelector(NumericArithmeticOperationKind.BitAnd, rhs);

            /// <summary>
            /// Applies a same-width bitwise OR operand to this integral selector.<br/>
            /// </summary>
            /// <param name="value">Operand expressed in the selector's exact integral type.<br/></param>
            public IntegralSelector<TInt> BitOr(TInt value)
                => ApplyBitwiseLiteral(NumericArithmeticOperationKind.BitOr, value);

            /// <summary>
            /// Applies bitwise OR between this selector and another same-type integral selector.<br/>
            /// </summary>
            /// <param name="rhs">Right selector whose complete immutable descriptor becomes the second operand.<br/></param>
            public IntegralSelector<TInt> BitOr(IntegralSelector<TInt> rhs)
                => ApplyBitwiseSelector(NumericArithmeticOperationKind.BitOr, rhs);

            /// <summary>
            /// Applies a same-width bitwise XOR operand to this integral selector.<br/>
            /// </summary>
            /// <param name="value">Operand expressed in the selector's exact integral type.<br/></param>
            public IntegralSelector<TInt> BitXor(TInt value)
                => ApplyBitwiseLiteral(NumericArithmeticOperationKind.BitXor, value);

            /// <summary>
            /// Applies bitwise XOR between this selector and another same-type integral selector.<br/>
            /// </summary>
            /// <param name="rhs">Right selector whose complete immutable descriptor becomes the second operand.<br/></param>
            public IntegralSelector<TInt> BitXor(IntegralSelector<TInt> rhs)
                => ApplyBitwiseSelector(NumericArithmeticOperationKind.BitXor, rhs);

            /// <summary>
            /// Shifts this integral selector left while preserving its exact CLR width and signedness.<br/>
            /// </summary>
            /// <param name="bits">Non-negative shift count.<br/></param>
            public IntegralSelector<TInt> LeftShift(int bits)
            {
                if (bits < 0)
                    throw new ArgumentOutOfRangeException(nameof(bits), "Shift count must be non-negative.");
                return ApplyBitwiseLiteral(
                    NumericArithmeticOperationKind.LeftShift,
                    TInt.CreateChecked(bits));
            }

            /// <summary>
            /// Shifts this integral selector right using the CLR semantics of its exact signed or unsigned type.<br/>
            /// </summary>
            /// <param name="bits">Non-negative shift count.<br/></param>
            public IntegralSelector<TInt> RightShift(int bits)
            {
                if (bits < 0)
                    throw new ArgumentOutOfRangeException(nameof(bits), "Shift count must be non-negative.");
                return ApplyBitwiseLiteral(
                    NumericArithmeticOperationKind.RightShift,
                    TInt.CreateChecked(bits));
            }

            /// <summary>
            /// Complements every bit in this integral selector while preserving its exact CLR width and signedness.<br/>
            /// </summary>
            public IntegralSelector<TInt> BitNot()
            {
                var source = GetSemanticValueDescriptor();
                parentBuilder.MarkComputed();
                var descriptor = source is null
                    ? null
                    : new IntegralBitwiseSelectorDescriptor<TInt>(
                        source,
                        NumericArithmeticOperationKind.BitNot);
                return new IntegralSelector<TInt>(
                    parentBuilder,
                    selectorExpression,
                    addBase: false,
                    descriptor);
            }

            /// <summary>
            /// Creates one literal-operand integral bitwise descriptor and returns a fresh selector carrying that complete intent.<br/>
            /// </summary>
            private IntegralSelector<TInt> ApplyBitwiseLiteral(
                NumericArithmeticOperationKind operation,
                TInt value)
            {
                var source = GetSemanticValueDescriptor();
                parentBuilder.MarkComputed();
                var descriptor = source is null
                    ? null
                    : new IntegralBitwiseSelectorDescriptor<TInt>(source, operation, value);
                return new IntegralSelector<TInt>(
                    parentBuilder,
                    selectorExpression,
                    addBase: false,
                    descriptor);
            }

            /// <summary>
            /// Creates one selector-operand integral bitwise descriptor without collapsing either recursive operand tree.<br/>
            /// </summary>
            private IntegralSelector<TInt> ApplyBitwiseSelector(
                NumericArithmeticOperationKind operation,
                IntegralSelector<TInt> rhs)
            {
                ArgumentNullException.ThrowIfNull(rhs);
                var source = GetSemanticValueDescriptor();
                var rightSource = rhs.GetSemanticValueDescriptor();
                parentBuilder.MarkComputed();
                var descriptor = source is null || rightSource is null
                    ? null
                    : new IntegralBitwiseSelectorDescriptor<TInt>(source, operation, rightSource);
                return new IntegralSelector<TInt>(
                    parentBuilder,
                    selectorExpression,
                    addBase: false,
                    descriptor);
            }
        }
    }
}
