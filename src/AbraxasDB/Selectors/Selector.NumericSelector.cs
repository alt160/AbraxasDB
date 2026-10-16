using System.Numerics;

namespace AbraxasDB
{

    public partial class Selector<T>
    {
        public abstract partial class NumericSelector<TNum> : IValue<TNum>,ISelectorStructure where TNum : INumber<TNum>
        {
            private protected readonly Builder parentBuilder;
            private protected readonly ValueOrExpression<string> selectorExpression;
            private protected readonly IBackendNeutralSelectorDescriptor? semanticDescriptor;

            string? ISelectorStructure.SimplePropertyPath => parentBuilder.SimplePropertyPath;
            string? ISelectorStructure.TypedSourcePropertyPath => parentBuilder.TypedSourcePropertyPath;
            Type? ISelectorStructure.ResultType => typeof(TNum);
            IBackendNeutralSelectorDescriptor? ISelectorStructure.SemanticDescriptor => semanticDescriptor;

            /// <summary>
            /// Creates one direct numeric selector that continues to expose the builder's typed source path without leaking internal semantic descriptor types through the public inheritance surface.<br/>
            /// </summary>
            /// <param name="parentBuilder">Selector builder that owns retained composition parts.<br/></param>
            /// <param name="selectorExpression">Base selector expression used by the retained renderer.<br/></param>
            /// <param name="addBase">Whether this instance should append its base selector part.<br/></param>
            private protected NumericSelector(
                Builder parentBuilder,
                ValueOrExpression<string> selectorExpression,
                bool addBase = true)
                : this(parentBuilder, selectorExpression, addBase, semanticDescriptor: null)
            {
            }

            /// <summary>
            /// Creates one numeric selector with an assembly-owned backend-neutral computed descriptor.<br/>
            /// Restricting this overload to in-assembly derived selectors prevents the internal descriptor contract from leaking through the public inheritance surface.<br/>
            /// </summary>
            /// <param name="parentBuilder">Selector builder that owns retained composition parts.<br/></param>
            /// <param name="selectorExpression">Base selector expression used by the retained renderer.<br/></param>
            /// <param name="addBase">Whether this instance should append its base selector part.<br/></param>
            /// <param name="semanticDescriptor">Immutable computed selector descriptor.<br/></param>
            private protected NumericSelector(
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
            /// Computed selectors reuse their existing descriptor, while direct typed property selectors are frozen into a normalized property descriptor.<br/>
            /// </summary>
            /// <returns>The direct or computed descriptor when this selector has portable value semantics; otherwise <see langword="null"/>.<br/></returns>
            private protected IBackendNeutralSelectorDescriptor? GetSemanticValueDescriptor()
            {
                if (semanticDescriptor is not null)
                    return semanticDescriptor;

                var propertyPath = ((ISelectorStructure)this).TypedSourcePropertyPath;
                return string.IsNullOrWhiteSpace(propertyPath)
                    ? null
                    : new DirectPropertySelectorDescriptor(propertyPath);
            }

            private enum NumericEncodingType : long
            {
                SByte = 1,                Byte = 2,                Int16 = 3,                UInt16 = 4,                Int32 = 5,                UInt32 = 6,                Int64 = 7,                UInt64 = 8,                Int128 = 9,                UInt128 = 10,                BigInteger = 11,                Half = 12,                Single = 13,                Double = 14,                Decimal = 15
            }

            private static readonly NumericEncodingType? EncodingTypeCode = TryMapNumericEncodingType(typeof(TNum));

            private static NumericEncodingType? TryMapNumericEncodingType(Type t)
            {
                if (t == typeof(sbyte)) return NumericEncodingType.SByte;
                if (t == typeof(byte)) return NumericEncodingType.Byte;
                if (t == typeof(short)) return NumericEncodingType.Int16;
                if (t == typeof(ushort)) return NumericEncodingType.UInt16;
                if (t == typeof(int)) return NumericEncodingType.Int32;
                if (t == typeof(uint)) return NumericEncodingType.UInt32;
                if (t == typeof(long) || t == typeof(nint)) return NumericEncodingType.Int64;
                if (t == typeof(ulong) || t == typeof(nuint)) return NumericEncodingType.UInt64;
                if (t == typeof(Int128)) return NumericEncodingType.Int128;
                if (t == typeof(UInt128)) return NumericEncodingType.UInt128;
                if (t == typeof(System.Numerics.BigInteger)) return NumericEncodingType.BigInteger;
                if (t == typeof(Half)) return NumericEncodingType.Half;
                if (t == typeof(float)) return NumericEncodingType.Single;
                if (t == typeof(double)) return NumericEncodingType.Double;
                if (t == typeof(decimal)) return NumericEncodingType.Decimal;
                return null;
            }

            private static NumericEncodingType RequireEncodingType()
            {
                if (EncodingTypeCode is null)
                    throw new NotSupportedException($"Numeric encoding is not supported for type '{typeof(TNum).FullName}'.");
                return EncodingTypeCode.Value;
            }

            /// <summary>
            /// Encodes the selected numeric value's binary representation as hexadecimal text.<br/>
            /// </summary>
            /// <param name="bigEndian">Whether to use big-endian rather than the default little-endian numeric byte order.<br/></param>
            /// <returns>A string selector for the encoded numeric bytes.<br/></returns>
            /// <remarks>
            /// This encodes the numeric representation, not the characters of its decimal display. Unsupported numeric types are rejected while authoring.<br/>
            /// </remarks>
            public StringSelector ToHex(bool bigEndian = false)
            {
                _ = RequireEncodingType();
                var source = GetSemanticValueDescriptor();
                parentBuilder.MarkComputed();
                var descriptor = source is null
                    ? null
                    : new BuiltInSelectorDescriptor(
                        source,
                        BuiltInSelectorOperationKind.NumericToHex,
                        typeof(TNum),
                        typeof(string),
                        firstIntegerOperand: bigEndian ? 1L : 0L);
                return new StringSelector(
                    parentBuilder,
                    selectorExpression,
                    addBase: false,
                    isLiteral: false,
                    descriptor);
            }

            /// <summary>
            /// Encodes the selected numeric value's binary representation as Base64 text.<br/>
            /// </summary>
            /// <param name="bigEndian">Whether to use big-endian rather than the default little-endian numeric byte order.<br/></param>
            /// <returns>A string selector for the encoded numeric bytes.<br/></returns>
            /// <remarks>
            /// This encodes the numeric representation, not the characters of its decimal display. Unsupported numeric types are rejected while authoring.<br/>
            /// </remarks>
            public StringSelector ToBase64(bool bigEndian = false)
            {
                _ = RequireEncodingType();
                var source = GetSemanticValueDescriptor();
                parentBuilder.MarkComputed();
                var descriptor = source is null
                    ? null
                    : new BuiltInSelectorDescriptor(
                        source,
                        BuiltInSelectorOperationKind.NumericToBase64,
                        typeof(TNum),
                        typeof(string),
                        firstIntegerOperand: bigEndian ? 1L : 0L);
                return new StringSelector(
                    parentBuilder,
                    selectorExpression,
                    addBase: false,
                    isLiteral: false,
                    descriptor);
            }

            /// <summary>
            /// Convert to a DecimalSelector for high-precision operations.
            /// </summary>
            public DecimalSelector AsDecimal
            {
                get
                {
                    var source = GetSemanticValueDescriptor();
                    parentBuilder.MarkTypedView();
                    var descriptor = source is null
                        ? null
                        : new BuiltInSelectorDescriptor(
                            source,
                            BuiltInSelectorOperationKind.NumericToDecimal,
                            typeof(TNum),
                            typeof(decimal));
                    return new DecimalSelector(
                        parentBuilder,
                        selectorExpression,
                        addBase: false,
                        descriptor);
                }
            }

        }

        /// <summary>
        /// Non-generic inspection contract for one immutable integral or real arithmetic selector node.<br/>
        /// The concrete generic node retains allocation-free typed evaluation, while durable capture and display can inspect the source/result CLR types and optional operand without reflection over per-record execution.<br/>
        /// </summary>
        internal abstract class NumericArithmeticSelectorDescriptor : IBackendNeutralSelectorDescriptor
        {
            internal IBackendNeutralSelectorDescriptor Source { get; }
            internal NumericArithmeticOperationKind Operation { get; }
            internal IBackendNeutralSelectorDescriptor? RightSource { get; }
            internal Type SourceType { get; }
            internal Type RightType { get; }
            internal Type ResultType { get; }
            internal abstract bool HasLiteralOperand { get; }
            internal abstract object? BoxedLiteralOperand { get; }

            /// <summary>
            /// Creates the common immutable arithmetic node state.<br/>
            /// Binary operations require exactly one selector or literal operand; negation rejects both.<br/>
            /// </summary>
            /// <param name="source">Direct or computed numeric source descriptor.<br/></param>
            /// <param name="operation">Arithmetic operation represented by this node.<br/></param>
            /// <param name="rightSource">Optional selector-valued right operand.<br/></param>
            /// <param name="sourceType">Exact CLR type produced by the source.<br/></param>
            /// <param name="rightType">Exact declared CLR type read from the right operand.<br/></param>
            /// <param name="resultType">Exact CLR type produced by this operation.<br/></param>
            /// <param name="hasLiteralOperand">Whether the concrete node owns a literal right operand.<br/></param>
            protected NumericArithmeticSelectorDescriptor(
                IBackendNeutralSelectorDescriptor source,
                NumericArithmeticOperationKind operation,
                IBackendNeutralSelectorDescriptor? rightSource,
                Type sourceType,
                Type rightType,
                Type resultType,
                bool hasLiteralOperand)
            {
                Source = source ?? throw new ArgumentNullException(nameof(source));
                Operation = operation;
                RightSource = rightSource;
                SourceType = sourceType ?? throw new ArgumentNullException(nameof(sourceType));
                RightType = rightType ?? throw new ArgumentNullException(nameof(rightType));
                ResultType = resultType ?? throw new ArgumentNullException(nameof(resultType));

                if (!Enum.IsDefined(operation))
                    throw new ArgumentOutOfRangeException(nameof(operation));

                var unary = operation is NumericArithmeticOperationKind.Negate or NumericArithmeticOperationKind.BitNot;
                if (unary)
                {
                    if (rightSource is not null || hasLiteralOperand)
                        throw new ArgumentException("Numeric negation cannot contain a right operand.");
                }
                else if ((rightSource is null) == !hasLiteralOperand)
                {
                    throw new ArgumentException(
                        "A binary numeric arithmetic operation requires exactly one selector or literal right operand.");
                }
            }

            /// <summary>
            /// Resolves the normalized leftmost property path used to read this expression from one Inheto record.<br/>
            /// </summary>
            /// <param name="propertyPath">Normalized source path when available.<br/></param>
            /// <returns><see langword="true"/> when the source tree ultimately reaches a direct property or supported binary projection.<br/></returns>
            internal bool TryGetSourcePropertyPath(out string propertyPath)
                => TryGetSourcePropertyPath(Source, out propertyPath);

            /// <summary>
            /// Reads this arithmetic result as the exact value type required by the consuming condition.<br/>
            /// The typed-reader interface avoids reflection, delegate compilation, and result boxing on the per-record path.<br/>
            /// </summary>
            /// <typeparam name="TValue">Exact expected arithmetic result type.<br/></typeparam>
            /// <param name="binary">Serialized Inheto record supplying source values.<br/></param>
            /// <param name="options">Active developer deserialization registrations.<br/></param>
            /// <param name="value">Computed value when successful.<br/></param>
            /// <returns><see langword="true"/> when every source is present and arithmetic completes without invalid conversion, division by zero, non-finite output, or overflow.<br/></returns>
            internal bool TryRead<TValue>(
                Inheto.InhetoBinary binary,
                Inheto.DeserializationOptions options,
                out TValue value)
            {
                if (this is ITypedNumericSelectorReader<TValue> typed)
                    return typed.TryRead(binary, options, out value);

                value = default!;
                return false;
            }

            /// <summary>
            /// Walks one supported selector tree to its primary normalized property path.<br/>
            /// </summary>
            private static bool TryGetSourcePropertyPath(
                IBackendNeutralSelectorDescriptor descriptor,
                out string propertyPath)
            {
                if (descriptor is DirectPropertySelectorDescriptor direct)
                {
                    propertyPath = direct.PropertyPath;
                    return true;
                }

                if (descriptor is NumericArithmeticSelectorDescriptor arithmetic)
                    return TryGetSourcePropertyPath(arithmetic.Source, out propertyPath);

                if (descriptor is TimeSpanProjectionSelectorDescriptor timeSpan)
                    return timeSpan.TryGetSourcePropertyPath(out propertyPath);

                if (descriptor is TemporalArithmeticSelectorDescriptor temporal)
                    return temporal.TryGetSourcePropertyPath(out propertyPath);

                if (descriptor is BuiltInSelectorDescriptor builtIn)
                    return builtIn.TryGetSourcePropertyPath(out propertyPath);

                if (descriptor is Condition<T>.BinarySliceSelectorDescriptor slice &&
                    Condition<T>.TryExtractPropPath(slice.SourceSelector, out propertyPath))
                {
                    return true;
                }

                propertyPath = string.Empty;
                return false;
            }
        }

        /// <summary>
        /// Immutable typed arithmetic node used by integral and real selectors.<br/>
        /// Source conversion and arithmetic use checked generic-math operators; invalid signed/unsigned conversion, integral overflow, zero division/modulo, and non-finite real output produce no condition value.<br/>
        /// </summary>
        /// <typeparam name="TSource">Exact CLR type produced by the left source.<br/></typeparam>
        /// <typeparam name="TResult">Exact promoted CLR type produced by this operation.<br/></typeparam>
        internal sealed class NumericArithmeticSelectorDescriptor<TSource, TResult> :
            NumericArithmeticSelectorDescriptor,ITypedNumericSelectorReader<TResult>
            where TSource : INumber<TSource>
            where TResult : INumber<TResult>
        {
            private readonly bool hasLiteralOperand;
            private readonly TResult literalOperand = default!;

            internal override bool HasLiteralOperand => hasLiteralOperand;
            internal override object? BoxedLiteralOperand => hasLiteralOperand ? literalOperand : null;

            /// <summary>
            /// Creates one unary or selector-operand arithmetic node.<br/>
            /// </summary>
            /// <param name="source">Left numeric source.<br/></param>
            /// <param name="operation">Arithmetic operation.<br/></param>
            /// <param name="rightSource">Optional selector-valued right operand.<br/></param>
            internal NumericArithmeticSelectorDescriptor(
                IBackendNeutralSelectorDescriptor source,
                NumericArithmeticOperationKind operation,
                IBackendNeutralSelectorDescriptor? rightSource = null)
                : base(
                    source,
                    operation,
                    rightSource,
                    typeof(TSource),
                    typeof(TResult),
                    typeof(TResult),
                    hasLiteralOperand: false)
            {
            }

            /// <summary>
            /// Creates one binary literal-operand arithmetic node.<br/>
            /// </summary>
            /// <param name="source">Left numeric source.<br/></param>
            /// <param name="operation">Binary arithmetic operation.<br/></param>
            /// <param name="literalOperand">Already promoted right operand.<br/></param>
            internal NumericArithmeticSelectorDescriptor(
                IBackendNeutralSelectorDescriptor source,
                NumericArithmeticOperationKind operation,
                TResult literalOperand)
                : base(
                    source,
                    operation,
                    rightSource: null,
                    typeof(TSource),
                    typeof(TResult),
                    typeof(TResult),
                    hasLiteralOperand: true)
            {
                hasLiteralOperand = true;
                this.literalOperand = literalOperand;
            }

            /// <summary>
            /// Evaluates this node without reflection or result boxing.<br/>
            /// </summary>
            public bool TryRead(
                Inheto.InhetoBinary binary,
                Inheto.DeserializationOptions options,
                out TResult value)
            {
                value = default!;
                if (!TryReadNumeric(Source, binary, options, out TSource sourceValue))
                    return false;

                try
                {
                    var left = TResult.CreateChecked(sourceValue);
                    TResult right = default!;
                    if (RightSource is not null)
                    {
                        if (!TryReadNumeric(RightSource, binary, options, out right))
                            return false;
                    }
                    else if (hasLiteralOperand)
                    {
                        right = literalOperand;
                    }

                    value = Operation switch
                    {
                        NumericArithmeticOperationKind.Add => checked(left + right),
                        NumericArithmeticOperationKind.Subtract => checked(left - right),
                        NumericArithmeticOperationKind.Multiply => checked(left * right),
                        NumericArithmeticOperationKind.Discount => checked(left * (TResult.One - right)),
                        NumericArithmeticOperationKind.Divide when right != TResult.Zero => checked(left / right),
                        NumericArithmeticOperationKind.Modulo when right != TResult.Zero => checked(left % right),
                        NumericArithmeticOperationKind.Negate => checked(-left),
                        _ => throw new ArithmeticException("Invalid numeric arithmetic operation or zero divisor.")
                    };

                    return TResult.IsFinite(value);
                }
                catch (ArithmeticException)
                {
                    value = default!;
                    return false;
                }
                catch (NotSupportedException)
                {
                    value = default!;
                    return false;
                }
            }

            /// <summary>
            /// Reads one direct, sliced, projected, or recursively computed numeric source as its exact requested CLR type.<br/>
            /// </summary>
            internal static bool TryReadNumeric<TValue>(
                IBackendNeutralSelectorDescriptor descriptor,
                Inheto.InhetoBinary binary,
                Inheto.DeserializationOptions options,
                out TValue value)
                where TValue : INumber<TValue>
            {
                value = default!;
                if (descriptor is DirectPropertySelectorDescriptor direct)
                {
                    if (!Condition<T>.HasConditionValue(binary, direct.PropertyPath, options))
                        return false;
                    value = binary.ReadPropAs<TValue>(direct.PropertyPath, options)!;
                    return true;
                }

                if (descriptor is ITypedNumericSelectorReader<TValue> numeric)
                    return numeric.TryRead(binary, options, out value);

                if (descriptor is TimeSpanProjectionSelectorDescriptor timeSpan)
                    return timeSpan.TryRead(binary, options, out value);

                if (descriptor is TemporalArithmeticSelectorDescriptor temporal)
                    return temporal.TryRead(binary, options, out value);

                if (descriptor is BuiltInSelectorDescriptor builtIn)
                    return builtIn.TryRead(binary, options, out value);

                if (descriptor is Condition<T>.BinarySliceSelectorDescriptor slice &&
                    Condition<T>.TryExtractPropPath(slice.SourceSelector, out var sourcePath) &&
                    Condition<T>.HasConditionValue(binary, sourcePath, options))
                {
                    var source = binary.ReadPropAs<byte[]>(sourcePath, options);
                    return slice.TryRead(source, out value);
                }

                return false;
            }
        }

        /// <summary>
        /// Immutable numeric property-to-property arithmetic node whose left, right, and result CLR types remain independently declared.<br/>
        /// Both operands are read in their authored types and converted with checked generic math only when the expression is consumed as <typeparamref name="TResult"/>.<br/>
        /// </summary>
        /// <typeparam name="TSource">Exact authored left property or expression type.<br/></typeparam>
        /// <typeparam name="TRight">Exact authored right property or expression type.<br/></typeparam>
        /// <typeparam name="TResult">Explicit result/coercion domain selected by the developer.<br/></typeparam>
        internal sealed class NumericPropertyArithmeticSelectorDescriptor<TSource, TRight, TResult> :
            NumericArithmeticSelectorDescriptor,
            ITypedNumericSelectorReader<TResult>
            where TSource : INumber<TSource>
            where TRight : INumber<TRight>
            where TResult : INumber<TResult>
        {
            internal override bool HasLiteralOperand => false;
            internal override object? BoxedLiteralOperand => null;

            /// <summary>
            /// Creates one selector-to-selector numeric operation with an explicit consumption domain.<br/>
            /// No value conversion occurs here; the descriptor freezes only semantic structure and declared types.<br/>
            /// </summary>
            /// <param name="source">Immutable left semantic descriptor.<br/></param>
            /// <param name="operation">Arithmetic operation to apply after checked conversion.<br/></param>
            /// <param name="rightSource">Immutable right semantic descriptor.<br/></param>
            internal NumericPropertyArithmeticSelectorDescriptor(
                IBackendNeutralSelectorDescriptor source,
                NumericArithmeticOperationKind operation,
                IBackendNeutralSelectorDescriptor rightSource)
                : base(
                    source,
                    operation,
                    rightSource ?? throw new ArgumentNullException(nameof(rightSource)),
                    typeof(TSource),
                    typeof(TRight),
                    typeof(TResult),
                    hasLiteralOperand: false)
            {
            }

            /// <summary>
            /// Reads each operand in its authored CLR type, performs checked conversion into the chosen result domain, and executes the arithmetic operation.<br/>
            /// Missing values, invalid narrowing, overflow, zero division/modulo, and non-finite results produce no semantic value.<br/>
            /// </summary>
            /// <param name="binary">Authoritative Inheto record payload.<br/></param>
            /// <param name="options">Active developer deserialization registrations.<br/></param>
            /// <param name="value">Computed result when both reads, conversions, and arithmetic succeed.<br/></param>
            /// <returns><see langword="true"/> when a valid result was produced; otherwise <see langword="false"/>.<br/></returns>
            public bool TryRead(
                Inheto.InhetoBinary binary,
                Inheto.DeserializationOptions options,
                out TResult value)
            {
                value = default!;
                if (!NumericArithmeticSelectorDescriptor<TSource, TResult>.TryReadNumeric(
                        Source,
                        binary,
                        options,
                        out TSource sourceValue) ||
                    RightSource is null ||
                    !NumericArithmeticSelectorDescriptor<TSource, TResult>.TryReadNumeric(
                        RightSource,
                        binary,
                        options,
                        out TRight rightValue))
                {
                    return false;
                }

                try
                {
                    TResult left = TResult.CreateChecked(sourceValue);
                    TResult right = TResult.CreateChecked(rightValue);
                    value = Operation switch
                    {
                        NumericArithmeticOperationKind.Add => checked(left + right),
                        NumericArithmeticOperationKind.Subtract => checked(left - right),
                        NumericArithmeticOperationKind.Multiply => checked(left * right),
                        NumericArithmeticOperationKind.Discount => checked(left * (TResult.One - right)),
                        NumericArithmeticOperationKind.Divide when right != TResult.Zero => checked(left / right),
                        NumericArithmeticOperationKind.Modulo when right != TResult.Zero => checked(left % right),
                        _ => throw new ArithmeticException("Invalid numeric property operation or zero divisor.")
                    };
                    return TResult.IsFinite(value);
                }
                catch (Exception exception) when (
                    exception is ArithmeticException or
                    NotSupportedException)
                {
                    value = default!;
                    return false;
                }
            }
        }

        /// <summary>
        /// Immutable typed bitwise node used only by integral selectors.<br/>
        /// The node shares the durable numeric-operation grammar while retaining an <see cref="IBinaryInteger{TSelf}"/> constraint for allocation-free per-record execution.<br/>
        /// </summary>
        /// <typeparam name="TInteger">Exact integral CLR source and result type.<br/></typeparam>
        internal sealed class IntegralBitwiseSelectorDescriptor<TInteger> :
            NumericArithmeticSelectorDescriptor,ITypedNumericSelectorReader<TInteger>
            where TInteger : INumber<TInteger>, IBinaryInteger<TInteger>
        {
            private readonly bool hasLiteralOperand;
            private readonly TInteger literalOperand = default!;

            internal override bool HasLiteralOperand => hasLiteralOperand;
            internal override object? BoxedLiteralOperand => hasLiteralOperand ? literalOperand : null;

            /// <summary>
            /// Creates a unary or selector-operand integral bitwise node.<br/>
            /// </summary>
            /// <param name="source">Left integral source descriptor.<br/></param>
            /// <param name="operation">Bitwise operation represented by this node.<br/></param>
            /// <param name="rightSource">Optional same-type selector used by a binary operation.<br/></param>
            internal IntegralBitwiseSelectorDescriptor(
                IBackendNeutralSelectorDescriptor source,
                NumericArithmeticOperationKind operation,
                IBackendNeutralSelectorDescriptor? rightSource = null)
                : base(source, operation, rightSource, typeof(TInteger), typeof(TInteger), typeof(TInteger), hasLiteralOperand: false)
            {
                ValidateBitwiseOperation(operation);
            }

            /// <summary>
            /// Creates a binary or shift integral bitwise node with one already converted same-type literal.<br/>
            /// </summary>
            /// <param name="source">Left integral source descriptor.<br/></param>
            /// <param name="operation">Bitwise operation represented by this node.<br/></param>
            /// <param name="literalOperand">Same-type bit mask, operand, or non-negative shift count.<br/></param>
            internal IntegralBitwiseSelectorDescriptor(
                IBackendNeutralSelectorDescriptor source,
                NumericArithmeticOperationKind operation,
                TInteger literalOperand)
                : base(source, operation, rightSource: null, typeof(TInteger), typeof(TInteger), typeof(TInteger), hasLiteralOperand: true)
            {
                ValidateBitwiseOperation(operation);
                hasLiteralOperand = true;
                this.literalOperand = literalOperand;
            }

            /// <summary>
            /// Evaluates one integral bitwise node using the exact CLR width and signedness selected by the developer.<br/>
            /// Missing values and invalid shift conversion return no condition value; no reflection, boxing, or heap allocation occurs on the record path.<br/>
            /// </summary>
            public bool TryRead(
                Inheto.InhetoBinary binary,
                Inheto.DeserializationOptions options,
                out TInteger value)
            {
                value = default!;
                if (!NumericArithmeticSelectorDescriptor<TInteger, TInteger>.TryReadNumeric(
                        Source,
                        binary,
                        options,
                        out TInteger left))
                {
                    return false;
                }

                TInteger right = default!;
                if (RightSource is not null)
                {
                    if (!NumericArithmeticSelectorDescriptor<TInteger, TInteger>.TryReadNumeric(
                            RightSource,
                            binary,
                            options,
                            out right))
                    {
                        return false;
                    }
                }
                else if (hasLiteralOperand)
                {
                    right = literalOperand;
                }

                try
                {
                    value = Operation switch
                    {
                        NumericArithmeticOperationKind.BitAnd => left & right,
                        NumericArithmeticOperationKind.BitOr => left | right,
                        NumericArithmeticOperationKind.BitXor => left ^ right,
                        NumericArithmeticOperationKind.LeftShift => left << int.CreateChecked(right),
                        NumericArithmeticOperationKind.RightShift => left >> int.CreateChecked(right),
                        NumericArithmeticOperationKind.BitNot => ~left,
                        _ => throw new InvalidOperationException("The integral descriptor contains a non-bitwise operation.")
                    };
                    return true;
                }
                catch (ArithmeticException)
                {
                    value = default!;
                    return false;
                }
            }

            private static void ValidateBitwiseOperation(NumericArithmeticOperationKind operation)
            {
                if (operation is < NumericArithmeticOperationKind.BitAnd or > NumericArithmeticOperationKind.BitNot)
                    throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }
    }

    /// <summary>
    /// Typed reader implemented by immutable numeric selector descriptors so condition scans avoid reflection and result boxing.<br/>
    /// </summary>
    internal interface ITypedNumericSelectorReader<TValue>
    {
        bool TryRead(
            Inheto.InhetoBinary binary,
            Inheto.DeserializationOptions options,
            out TValue value);
    }

    /// <summary>
    /// Backend-neutral arithmetic operations shared by integral and real selector expressions.<br/>
    /// </summary>
    internal enum NumericArithmeticOperationKind
    {
        Add = 0,        Subtract = 1,        Multiply = 2,        Divide = 3,        Modulo = 4,        Negate = 5,        BitAnd = 6,        BitOr = 7,        BitXor = 8,        LeftShift = 9,        RightShift = 10,        BitNot = 11,        Discount = 12
    }
}
