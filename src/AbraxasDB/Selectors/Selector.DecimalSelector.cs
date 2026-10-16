namespace AbraxasDB
{

    public partial class Selector<T>
    {
        // --------- Selector parts implementations ---------

        /// <summary>
        /// DecimalSelector: selector helper that emits calls to the DecimalOp scalar function.<br/>
        /// Use this for properties stored as ordered-bytes decimal blobs (Utils.ToOrderedBytes).<br/>
        /// </summary>
        public partial class DecimalSelector : IValue<decimal>,ISelectorStructure
        {
            private readonly Builder parentBuilder;
            private readonly ValueOrExpression<string> selectorExpression;
            private readonly IBackendNeutralSelectorDescriptor? semanticDescriptor;

            string? ISelectorStructure.SimplePropertyPath => parentBuilder.SimplePropertyPath;
            string? ISelectorStructure.TypedSourcePropertyPath => parentBuilder.TypedSourcePropertyPath;
            Type? ISelectorStructure.ResultType => typeof(decimal);
            IBackendNeutralSelectorDescriptor? ISelectorStructure.SemanticDescriptor => semanticDescriptor;

            internal DecimalSelector(Builder parentBuilder, ValueOrExpression<string> selectorExpression, bool addBase = true)
                : this(parentBuilder, selectorExpression, addBase, semanticDescriptor: null)
            {
            }

            /// <summary>
            /// The descriptor is frozen when an operation is authored so later mutation of the source selector cannot change semantic execution or durable condition capture.<br/>
            /// </summary>
            /// <param name="selectorExpression">Base selector expression used only by the retained renderer.<br/></param>
            /// <param name="addBase">Whether this instance should append its base selector part.<br/></param>
            /// <param name="semanticDescriptor">Optional immutable direct-or-computed decimal descriptor.<br/></param>
            /// <param name="parentBuilder">The owning selector builder from which this direct or computed decimal selector is constructed.<br/></param>
            internal DecimalSelector(
                Builder parentBuilder,
                ValueOrExpression<string> selectorExpression,
                bool addBase,
                IBackendNeutralSelectorDescriptor? semanticDescriptor)
            {
                this.parentBuilder = addBase || semanticDescriptor is not null
                    ? parentBuilder.AsComputed()
                    : parentBuilder;
                this.selectorExpression = selectorExpression;
                this.semanticDescriptor = semanticDescriptor;
                if (addBase) parentBuilder.MarkComputed();
            }

            /// <summary>
            /// A computed selector returns its already-frozen descriptor; an unmodified typed property becomes a normalized direct-property descriptor.<br/>
            /// </summary>
            /// <returns>The current semantic value descriptor, or null when preceding unsupported composition erased the typed source contract.<br/></returns>
            private IBackendNeutralSelectorDescriptor? GetSemanticValueDescriptor()
            {
                if (semanticDescriptor is not null)
                    return semanticDescriptor;

                var sourcePath = parentBuilder.TypedSourcePropertyPath;
                return string.IsNullOrWhiteSpace(sourcePath)
                    ? null
                    : new DirectPropertySelectorDescriptor(sourcePath);
            }

            /// <summary>
            /// Creates the immutable descriptor for one decimal-returning operation when every source operand has backend-neutral meaning.<br/>
            /// Unsupported preceding selector composition deliberately returns null so native execution cannot silently substitute the underlying property value.<br/>
            /// </summary>
            /// <param name="operationKind">Decimal operation being authored.<br/></param>
            /// <param name="rightSelector">Optional selector-valued right operand.<br/></param>
            /// <param name="literalOperand">Optional literal decimal right operand.<br/></param>
            /// <param name="integerOperand">Optional scale or integer exponent.<br/></param>
            /// <returns>A complete immutable descriptor, or null when either required selector source is not describable.<br/></returns>
            private DecimalOperationSelectorDescriptor? CreateOperationDescriptor(
                DecimalOperationKind operationKind,
                DecimalSelector? rightSelector = null,
                decimal? literalOperand = null,
                int? integerOperand = null)
            {
                var source = GetSemanticValueDescriptor();
                if (source is null)
                    return null;

                var right = rightSelector?.GetSemanticValueDescriptor();
                if (rightSelector is not null && right is null)
                    return null;

                return new DecimalOperationSelectorDescriptor(
                    source,
                    operationKind,
                    right,
                    literalOperand,
                    integerOperand);
            }

            // --------------------
            // Binary decimal ops
            // --------------------

            /// <summary>
            /// Add another decimal selector (property) as right-hand operand.<br/>
            /// Emits: DecimalOp(&lt;left&gt;, OP_ADD, &lt;rhsBase&gt;)<br/>
            /// </summary>
            public DecimalSelector Add(DecimalSelector rhs)
            {
                var descriptor = CreateOperationDescriptor(DecimalOperationKind.Add, rightSelector: rhs);
                parentBuilder.MarkComputed();
                return new DecimalSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            /// <summary>
            /// Add a decimal literal as right-hand operand.<br/>
            /// Emits: DecimalOp(&lt;left&gt;, OP_ADD, X'...') where X'...' is Utils.ToOrderedBytes(value).<br/>
            /// </summary>
            public DecimalSelector Add(decimal rhs)
            {
                var descriptor = CreateOperationDescriptor(DecimalOperationKind.Add, literalOperand: rhs);
                parentBuilder.MarkComputed();
                return new DecimalSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            /// <summary>
            /// Describes <c>left - right</c> over the selected decimal value.<br/>
            /// </summary>
            /// <param name="rhs">Selector supplying the right operand from the same record.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The expression retains decimal arithmetic rather than converting through binary floating point.<br/>
            /// </remarks>
            public DecimalSelector Subtract(DecimalSelector rhs)
            {
                var descriptor = CreateOperationDescriptor(DecimalOperationKind.Subtract, rightSelector: rhs);
                parentBuilder.MarkComputed();
                return new DecimalSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            /// <summary>
            /// Describes <c>left - right</c> over the selected decimal value.<br/>
            /// </summary>
            /// <param name="rhs">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The expression retains decimal arithmetic rather than converting through binary floating point.<br/>
            /// </remarks>
            public DecimalSelector Subtract(decimal rhs)
            {
                var descriptor = CreateOperationDescriptor(DecimalOperationKind.Subtract, literalOperand: rhs);
                parentBuilder.MarkComputed();
                return new DecimalSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            /// <summary>
            /// Describes <c>left * right</c> over the selected decimal value.<br/>
            /// </summary>
            /// <param name="rhs">Selector supplying the right operand from the same record.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The expression retains decimal arithmetic rather than converting through binary floating point.<br/>
            /// </remarks>
            public DecimalSelector Multiply(DecimalSelector rhs)
            {
                var descriptor = CreateOperationDescriptor(DecimalOperationKind.Multiply, rightSelector: rhs);
                parentBuilder.MarkComputed();
                return new DecimalSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            /// <summary>
            /// Describes <c>left * right</c> over the selected decimal value.<br/>
            /// </summary>
            /// <param name="rhs">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The expression retains decimal arithmetic rather than converting through binary floating point.<br/>
            /// </remarks>
            public DecimalSelector Multiply(decimal rhs)
            {
                var descriptor = CreateOperationDescriptor(DecimalOperationKind.Multiply, literalOperand: rhs);
                parentBuilder.MarkComputed();
                return new DecimalSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            /// <summary>
            /// Describes <c>left / right</c> over the selected decimal value.<br/>
            /// </summary>
            /// <param name="rhs">Selector supplying the right operand from the same record.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The expression retains decimal arithmetic rather than converting through binary floating point.<br/>
            /// </remarks>
            public DecimalSelector Divide(DecimalSelector rhs)
            {
                var descriptor = CreateOperationDescriptor(DecimalOperationKind.Divide, rightSelector: rhs);
                parentBuilder.MarkComputed();
                return new DecimalSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            /// <summary>
            /// Describes <c>left / right</c> over the selected decimal value.<br/>
            /// </summary>
            /// <param name="rhs">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The expression retains decimal arithmetic rather than converting through binary floating point.<br/>
            /// </remarks>
            public DecimalSelector Divide(decimal rhs)
            {
                var descriptor = CreateOperationDescriptor(DecimalOperationKind.Divide, literalOperand: rhs);
                parentBuilder.MarkComputed();
                return new DecimalSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            /// <summary>
            /// Describes <c>left % right</c> over the selected decimal value.<br/>
            /// </summary>
            /// <param name="rhs">Selector supplying the right operand from the same record.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The expression retains decimal arithmetic rather than converting through binary floating point.<br/>
            /// </remarks>
            public DecimalSelector Modulo(DecimalSelector rhs)
            {
                var descriptor = CreateOperationDescriptor(DecimalOperationKind.Modulo, rightSelector: rhs);
                parentBuilder.MarkComputed();
                return new DecimalSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            /// <summary>
            /// Describes <c>left % right</c> over the selected decimal value.<br/>
            /// </summary>
            /// <param name="rhs">Literal right operand captured by the expression.<br/></param>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The expression retains decimal arithmetic rather than converting through binary floating point.<br/>
            /// </remarks>
            public DecimalSelector Modulo(decimal rhs)
            {
                var descriptor = CreateOperationDescriptor(DecimalOperationKind.Modulo, literalOperand: rhs);
                parentBuilder.MarkComputed();
                return new DecimalSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            /// <summary>
            /// Describes reducing the current monetary value by a decimal rate without performing arithmetic during selector construction.<br/>
            /// Execution evaluates <c>value * (1 - rate)</c> with checked decimal arithmetic; negative and greater-than-one rates remain explicit developer-authored semantics rather than being silently clamped.<br/>
            /// </summary>
            /// <param name="rate">Decimal discount rate, where <c>0.15m</c> represents fifteen percent.<br/></param>
            /// <returns>A new immutable decimal selector containing the discount operation.<br/></returns>
            public DecimalSelector Discount(decimal rate)
            {
                var descriptor = CreateOperationDescriptor(DecimalOperationKind.Discount, literalOperand: rate);
                parentBuilder.MarkComputed();
                return new DecimalSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            /// <summary>
            /// Describes reducing the current monetary value by a reusable decimal rate selector.<br/>
            /// Both operands remain immutable semantic descriptors until a condition, reader, aggregate, or index execution path consumes the selector.<br/>
            /// </summary>
            /// <param name="rate">Selector that supplies the decimal discount rate for the same record.<br/></param>
            /// <returns>A new immutable decimal selector containing both value and rate intent.<br/></returns>
            public DecimalSelector Discount(DecimalSelector rate)
            {
                ArgumentNullException.ThrowIfNull(rate);
                var descriptor = CreateOperationDescriptor(DecimalOperationKind.Discount, rightSelector: rate);
                parentBuilder.MarkComputed();
                return new DecimalSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            // --------------------
            // Unary ops (no param blob: pass NULL as third arg)
            // --------------------

            /// <summary>
            /// Describes <c>-value</c> over the selected decimal value.<br/>
            /// </summary>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The expression retains decimal arithmetic rather than converting through binary floating point.<br/>
            /// </remarks>
            public DecimalSelector Negate()
            {
                var descriptor = CreateOperationDescriptor(DecimalOperationKind.Negate);
                parentBuilder.MarkComputed();
                return new DecimalSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            /// <summary>
            /// Describes <c>decimal.Abs(value)</c> over the selected decimal value.<br/>
            /// </summary>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The expression retains decimal arithmetic rather than converting through binary floating point.<br/>
            /// </remarks>
            public DecimalSelector Abs()
            {
                var descriptor = CreateOperationDescriptor(DecimalOperationKind.Absolute);
                parentBuilder.MarkComputed();
                return new DecimalSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            /// <summary>
            /// Describes <c>decimal.Floor(value)</c> over the selected decimal value.<br/>
            /// </summary>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The expression retains decimal arithmetic rather than converting through binary floating point.<br/>
            /// </remarks>
            public DecimalSelector Floor()
            {
                var descriptor = CreateOperationDescriptor(DecimalOperationKind.Floor);
                parentBuilder.MarkComputed();
                return new DecimalSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            /// <summary>
            /// Converts the current decimal value to a signed 64-bit integer by truncating toward zero.<br/>
            /// Values outside the <see cref="long"/> range produce no semantic value rather than wrapping.<br/>
            /// </summary>
            public IntegralSelector<long> AsLong
            {
                get
                {
                    var source = GetSemanticValueDescriptor();
                    parentBuilder.MarkTypedView();
                    var descriptor = source is null
                        ? null
                        : new BuiltInSelectorDescriptor(
                            source,
                            BuiltInSelectorOperationKind.DecimalToInt64,
                            typeof(decimal),
                            typeof(long));
                    return new IntegralSelector<long>(
                        parentBuilder,
                        selectorExpression,
                        addBase: false,
                        descriptor);
                }
            }

            /// <summary>
            /// Describes <c>decimal.Ceiling(value)</c> over the selected decimal value.<br/>
            /// </summary>
            /// <returns>A new selector representing the calculated value.<br/></returns>
            /// <remarks>
            /// No arithmetic or record read occurs while the selector is authored.<br/>
            /// The expression retains decimal arithmetic rather than converting through binary floating point.<br/>
            /// </remarks>
            public DecimalSelector Ceiling()
            {
                var descriptor = CreateOperationDescriptor(DecimalOperationKind.Ceiling);
                parentBuilder.MarkComputed();
                return new DecimalSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            /// <summary>
            /// Round to 'scale' using MidpointRounding.ToEven on the DB side.<br/>
            /// 'scale' is encoded as a 4-byte Int32 LE blob per DecimalOp contract.<br/>
            /// </summary>
            public DecimalSelector Round(int scale)
            {
                var descriptor = CreateOperationDescriptor(DecimalOperationKind.Round, integerOperand: scale);
                parentBuilder.MarkComputed();
                return new DecimalSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            /// <summary>
            /// Truncate to 'scale'.<br/>
            /// 'scale' encoded as 4-byte Int32 LE blob.<br/>
            /// </summary>
            public DecimalSelector Truncate(int scale)
            {
                var descriptor = CreateOperationDescriptor(DecimalOperationKind.Truncate, integerOperand: scale);
                parentBuilder.MarkComputed();
                return new DecimalSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            /// <summary>
            /// Sign(): returns INTEGER -1/0/1.<br/>
            /// Caller typically will receive a <c>NumericSelector&lt;long&gt;</c> to continue numeric operations in the builder.<br/>
            /// </summary>
            public NumericSelector<long> Sign()
            {
                var descriptor = CreateOperationDescriptor(DecimalOperationKind.Sign);
                parentBuilder.MarkComputed();
                return new IntegralSelector<long>(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            /// <summary>
            /// Integer-power: exponent can be negative (encoded as 4-byte Int32 LE).<br/>
            /// Emits DecimalOp(..., OP_POWINT, &lt;4-byte LE int blob&gt;).<br/>
            /// </summary>
            public DecimalSelector PowInt(int exponent)
            {
                var descriptor = CreateOperationDescriptor(DecimalOperationKind.PowerInteger, integerOperand: exponent);
                parentBuilder.MarkComputed();
                return new DecimalSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

        }

        /// <summary>
        /// The immutable tree retains its left source, optional selector or literal right operand, and optional scale or exponent so Fractal evaluation and durable condition replay share exactly one semantic identity.<br/>
        /// </summary>
        internal sealed class DecimalOperationSelectorDescriptor : IBackendNeutralSelectorDescriptor
        {
            internal IBackendNeutralSelectorDescriptor Source { get; }
            internal DecimalOperationKind Operation { get; }
            internal IBackendNeutralSelectorDescriptor? RightSource { get; }
            internal decimal? LiteralOperand { get; }
            internal int? IntegerOperand { get; }

            /// <summary>
            /// Creates one validated immutable decimal-operation node.<br/>
            /// Binary operations require exactly one selector or literal right operand, scale/exponent operations require one integer operand, and unary operations reject every inactive operand field.<br/>
            /// </summary>
            /// <param name="source">Direct property or preceding decimal operation that supplies the left value.<br/></param>
            /// <param name="operation">Decimal operation represented by this node.<br/></param>
            /// <param name="rightSource">Optional selector-valued right operand for binary arithmetic.<br/></param>
            /// <param name="literalOperand">Optional literal right operand for binary arithmetic.<br/></param>
            /// <param name="integerOperand">Optional scale or integer exponent.<br/></param>
            internal DecimalOperationSelectorDescriptor(
                IBackendNeutralSelectorDescriptor source,
                DecimalOperationKind operation,
                IBackendNeutralSelectorDescriptor? rightSource = null,
                decimal? literalOperand = null,
                int? integerOperand = null)
            {
                Source = source ?? throw new ArgumentNullException(nameof(source));
                Operation = operation;
                RightSource = rightSource;
                LiteralOperand = literalOperand;
                IntegerOperand = integerOperand;

                var binary = operation is
                    DecimalOperationKind.Add or
                    DecimalOperationKind.Subtract or
                    DecimalOperationKind.Multiply or
                    DecimalOperationKind.Divide or
                    DecimalOperationKind.Modulo or
                    DecimalOperationKind.Discount;
                var integer = operation is DecimalOperationKind.Round or DecimalOperationKind.Truncate or DecimalOperationKind.PowerInteger;
                if (binary)
                {
                    if ((rightSource is null) == (literalOperand is null) || integerOperand is not null)
                        throw new ArgumentException("A binary decimal operation requires exactly one selector or literal right operand.");
                }
                else if (integer)
                {
                    if (integerOperand is null || rightSource is not null || literalOperand is not null)
                        throw new ArgumentException("A scale or exponent decimal operation requires exactly one integer operand.");
                }
                else if (rightSource is not null || literalOperand is not null || integerOperand is not null)
                {
                    throw new ArgumentException("A unary decimal operation cannot contain an additional operand.");
                }
            }

            /// <summary>
            /// Resolves the normalized leftmost source property used for Inheto reads and developer-registered conversion context.<br/>
            /// Selector-valued right operands may reference another property, but the left source remains the condition operand's primary diagnostic and customization path.<br/>
            /// </summary>
            /// <param name="propertyPath">Normalized direct property path when the descriptor tree has a valid source.<br/></param>
            /// <returns><see langword="true"/> when the left source ultimately resolves to a direct property; otherwise <see langword="false"/>.<br/></returns>
            internal bool TryGetSourcePropertyPath(out string propertyPath)
                => TryGetSourcePropertyPath(Source, out propertyPath);

            /// <summary>
            /// Evaluates this immutable decimal-operation tree for one serialized record and converts its result to the exact numeric type requested by the consuming condition leaf.<br/>
            /// Decimal-returning operations support <see cref="decimal"/> and <see cref="DecimalOperationKind.Sign"/> supports <see cref="long"/>; every other requested type fails closed.<br/>
            /// </summary>
            /// <typeparam name="TValue">Exact condition value type expected by the numeric operator.<br/></typeparam>
            /// <param name="binary">Serialized Inheto record supplying direct-property values.<br/></param>
            /// <param name="options">Active developer deserialization registrations.<br/></param>
            /// <param name="value">Typed result when evaluation succeeds.<br/></param>
            /// <returns><see langword="true"/> when every source is present and the operation completes without invalid arithmetic or overflow; otherwise <see langword="false"/>.<br/></returns>
            internal bool TryRead<TValue>(
                Inheto.InhetoBinary binary,
                Inheto.DeserializationOptions options,
                out TValue value)
            {
                value = default!;
                if (!TryEvaluate(binary, options, out var decimalValue, out var signValue))
                    return false;

                if (Operation == DecimalOperationKind.Sign)
                {
                    if (typeof(TValue) != typeof(long))
                        return false;
                    value = (TValue)(object)signValue;
                    return true;
                }

                if (typeof(TValue) != typeof(decimal))
                    return false;
                value = (TValue)(object)decimalValue;
                return true;
            }

            /// <summary>
            /// </summary>
            /// <param name="binary">Serialized record supplying property values.<br/></param>
            /// <param name="options">Active developer deserialization registrations.<br/></param>
            /// <param name="decimalValue">Decimal result for decimal-returning operations.<br/></param>
            /// <param name="signValue">Signed integral result for <see cref="DecimalOperationKind.Sign"/>.<br/></param>
            /// <returns><see langword="true"/> when the operation produces a value; otherwise <see langword="false"/>.<br/></returns>
            private bool TryEvaluate(
                Inheto.InhetoBinary binary,
                Inheto.DeserializationOptions options,
                out decimal decimalValue,
                out long signValue)
            {
                decimalValue = default;
                signValue = default;
                if (!TryReadDecimal(Source, binary, options, out var left))
                    return false;

                try
                {
                    decimal right = default;
                    if (RightSource is not null)
                    {
                        if (!TryReadDecimal(RightSource, binary, options, out right))
                            return false;
                    }
                    else if (LiteralOperand is decimal literal)
                    {
                        right = literal;
                    }

                    switch (Operation)
                    {
                        case DecimalOperationKind.Add:
                            decimalValue = left + right;
                            return true;
                        case DecimalOperationKind.Subtract:
                            decimalValue = left - right;
                            return true;
                        case DecimalOperationKind.Multiply:
                            decimalValue = left * right;
                            return true;
                        case DecimalOperationKind.Discount:
                            decimalValue = left * (1m - right);
                            return true;
                        case DecimalOperationKind.Divide:
                            if (right == 0m) return false;
                            decimalValue = left / right;
                            return true;
                        case DecimalOperationKind.Modulo:
                            if (right == 0m) return false;
                            decimalValue = left % right;
                            return true;
                        case DecimalOperationKind.Negate:
                            decimalValue = -left;
                            return true;
                        case DecimalOperationKind.Absolute:
                            decimalValue = decimal.Abs(left);
                            return true;
                        case DecimalOperationKind.Floor:
                            decimalValue = decimal.Floor(left);
                            return true;
                        case DecimalOperationKind.Ceiling:
                            decimalValue = decimal.Ceiling(left);
                            return true;
                        case DecimalOperationKind.Round:
                            decimalValue = RoundDecimal(left, IntegerOperand!.Value);
                            return true;
                        case DecimalOperationKind.Truncate:
                            decimalValue = TruncateDecimal(left, IntegerOperand!.Value);
                            return true;
                        case DecimalOperationKind.Sign:
                            signValue = Math.Sign(left);
                            return true;
                        case DecimalOperationKind.PowerInteger:
                            var exponent = IntegerOperand.GetValueOrDefault();
                            if (left == 0m && exponent <= 0) return false;
                            decimalValue = PowDecimalInteger(left, exponent);
                            return true;
                        default:
                            return false;
                    }
                }
                catch (ArithmeticException)
                {
                    return false;
                }
                catch (ArgumentOutOfRangeException)
                {
                    return false;
                }
            }

            /// <summary>
            /// Reads one decimal value from a direct property or recursively evaluated decimal-operation descriptor.<br/>
            /// Missing and null Inheto properties fail before typed restoration so collection-null and absent-value semantics remain unchanged.<br/>
            /// </summary>
            /// <param name="descriptor">Direct or computed decimal source descriptor.<br/></param>
            /// <param name="binary">Serialized record supplying direct values.<br/></param>
            /// <param name="options">Active developer deserialization registrations.<br/></param>
            /// <param name="value">Restored or computed decimal value.<br/></param>
            /// <returns><see langword="true"/> when a valid decimal value is available; otherwise <see langword="false"/>.<br/></returns>
            private static bool TryReadDecimal(
                IBackendNeutralSelectorDescriptor descriptor,
                Inheto.InhetoBinary binary,
                Inheto.DeserializationOptions options,
                out decimal value)
            {
                value = default;
                if (descriptor is DirectPropertySelectorDescriptor direct)
                {
                    if (!Condition<T>.HasConditionValue(binary, direct.PropertyPath, options))
                        return false;
                    value = binary.ReadPropAs<decimal>(direct.PropertyPath, options);
                    return true;
                }

                if (descriptor is DecimalOperationSelectorDescriptor operation &&
                    operation.Operation != DecimalOperationKind.Sign &&
                    operation.TryRead(binary, options, out decimal computed))
                {
                    value = computed;
                    return true;
                }

                return false;
            }

            /// <summary>
            /// Walks the left edge of a direct-or-decimal descriptor tree to find its normalized source property.<br/>
            /// </summary>
            /// <param name="descriptor">Descriptor whose primary source is requested.<br/></param>
            /// <param name="propertyPath">Normalized source path when found.<br/></param>
            /// <returns><see langword="true"/> for direct and recursively computed decimal sources; otherwise <see langword="false"/>.<br/></returns>
            private static bool TryGetSourcePropertyPath(
                IBackendNeutralSelectorDescriptor descriptor,
                out string propertyPath)
            {
                if (descriptor is DirectPropertySelectorDescriptor direct)
                {
                    propertyPath = direct.PropertyPath;
                    return true;
                }

                if (descriptor is DecimalOperationSelectorDescriptor operation)
                    return TryGetSourcePropertyPath(operation.Source, out propertyPath);

                propertyPath = string.Empty;
                return false;
            }

            /// <summary>
            /// Raises ten to a non-negative integral power using decimal arithmetic so overflow matches the retained oracle.<br/>
            /// </summary>
            /// <param name="power">Non-negative exponent.<br/></param>
            /// <returns>The decimal power of ten.<br/></returns>
            private static decimal Pow10(int power)
            {
                decimal result = 1m;
                for (var index = 0; index < power; index++)
                    result *= 10m;
                return result;
            }

            /// <summary>
            /// </summary>
            /// <param name="value">Decimal value to round.<br/></param>
            /// <param name="scale">Digits after the decimal point, or a negative whole-number position.<br/></param>
            /// <returns>The rounded decimal value.<br/></returns>
            private static decimal RoundDecimal(decimal value, int scale)
            {
                if (scale >= 0)
                    return decimal.Round(value, scale, MidpointRounding.ToEven);

                var factor = Pow10(checked(-scale));
                return decimal.Round(value / factor, 0, MidpointRounding.ToEven) * factor;
            }

            /// <summary>
            /// </summary>
            /// <param name="value">Decimal value to truncate.<br/></param>
            /// <param name="scale">Digits after the decimal point, or a negative whole-number position.<br/></param>
            /// <returns>The truncated decimal value.<br/></returns>
            private static decimal TruncateDecimal(decimal value, int scale)
            {
                if (scale >= 0)
                {
                    var factor = Pow10(scale);
                    return decimal.Truncate(value * factor) / factor;
                }

                var wholeFactor = Pow10(checked(-scale));
                return decimal.Truncate(value / wholeFactor) * wholeFactor;
            }

            /// <summary>
            /// Raises one decimal to an integer exponent through exponentiation by squaring, including reciprocal results for negative exponents.<br/>
            /// </summary>
            /// <param name="value">Decimal base value.<br/></param>
            /// <param name="exponent">Signed integer exponent.<br/></param>
            /// <returns>The decimal power result.<br/></returns>
            private static decimal PowDecimalInteger(decimal value, int exponent)
            {
                if (exponent == 0)
                    return 1m;

                var negativeExponent = exponent < 0;
                long remaining = exponent < 0 ? -(long)exponent : exponent;
                decimal result = 1m;
                var factor = value;
                while (remaining > 0)
                {
                    if ((remaining & 1L) != 0)
                        result *= factor;
                    remaining >>= 1;
                    if (remaining != 0)
                        factor *= factor;
                }

                return negativeExponent ? 1m / result : result;
            }
        }
    }

    /// <summary>
    /// Identifies one backend-neutral decimal selector operation.<br/>
    /// </summary>
    internal enum DecimalOperationKind : long
    {
        Add = 0,        Subtract = 1,        Multiply = 2,        Divide = 3,        Modulo = 4,        Negate = 5,        Absolute = 6,        Floor = 7,        Ceiling = 8,        Round = 9,        Truncate = 10,        Sign = 11,        PowerInteger = 12,        Discount = 13
    }
}
