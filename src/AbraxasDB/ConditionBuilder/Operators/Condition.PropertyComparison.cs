using System.Globalization;
using System.Linq.Expressions;
using System.Numerics;
using Inheto;

namespace AbraxasDB
{
    public static partial class Condition<T>
    {
        internal enum PropertyComparisonKind
        {
            Equal,
            NotEqual,
            Greater,
            GreaterOrEqual,
            Less,
            LessOrEqual
        }

        internal interface IPropertyComparisonSource
        {
            PropertyComparisonKind Comparison { get; }
            ValueOrExpression<string> LeftSelector { get; }
            ValueOrExpression<string> RightSelector { get; }
            Type LeftType { get; }
            Type RightType { get; }
            Type ComparisonType { get; }
            string Culture { get; }
            bool IgnoreCase { get; }
        }

        internal sealed class PropertyComparisonOperation<TValue> : IConditionLeaf, IFastPredicateSource, IPropertyComparisonSource
        {
            private readonly PropertyComparisonKind comparison;
            private readonly ValueOrExpression<string> leftSelector;
            private readonly ValueOrExpression<string> rightSelector;
            private readonly string culture;
            private readonly bool ignoreCase;

            internal PropertyComparisonOperation(
                PropertyComparisonKind comparison,
                ValueOrExpression<string> leftSelector,
                ValueOrExpression<string> rightSelector,
                string culture = "",
                bool ignoreCase = false)
            {
                this.comparison = comparison;
                this.leftSelector = leftSelector;
                this.rightSelector = rightSelector;
                this.culture = culture ?? string.Empty;
                this.ignoreCase = ignoreCase;
            }

            PropertyComparisonKind IPropertyComparisonSource.Comparison => comparison;
            ValueOrExpression<string> IPropertyComparisonSource.LeftSelector => leftSelector;
            ValueOrExpression<string> IPropertyComparisonSource.RightSelector => rightSelector;
            Type IPropertyComparisonSource.LeftType => typeof(TValue);
            Type IPropertyComparisonSource.RightType => typeof(TValue);
            Type IPropertyComparisonSource.ComparisonType => typeof(TValue);
            string IPropertyComparisonSource.Culture => culture;
            bool IPropertyComparisonSource.IgnoreCase => ignoreCase;

            public bool TryBuildEvaluator(
                Builder builder,
                Inheto.DeserializationOptions options,
                out Func<InhetoBinary, bool> evaluator)
            {
                evaluator = default!;
                if (!TryBuildConditionValueReader<TValue>(builder, leftSelector, options, out _, out var readLeft) ||
                    !TryBuildConditionValueReader<TValue>(builder, rightSelector, options, out _, out var readRight))
                {
                    return false;
                }

                var cultureInfo = string.IsNullOrEmpty(culture)
                    ? CultureInfo.InvariantCulture
                    : CultureInfo.GetCultureInfo(culture);
                evaluator = binary =>
                {
                    var left = readLeft(binary);
                    if (!left.HasValue)
                        return false;
                    var right = readRight(binary);
                    if (!right.HasValue)
                        return false;

                    int cmp;
                    if (typeof(TValue) == typeof(string))
                    {
                        var leftText = (string)(object)left.Value!;
                        var rightText = (string)(object)right.Value!;
                        cmp = ignoreCase
                            ? cultureInfo.CompareInfo.Compare(leftText, rightText, CompareOptions.IgnoreCase)
                            : string.CompareOrdinal(leftText, rightText);
                    }
                    else if (typeof(TValue) == typeof(byte[]))
                    {
                        var equal = ((byte[])(object)left.Value!).AsSpan().SequenceEqual((byte[])(object)right.Value!);
                        cmp = equal ? 0 : 1;
                    }
                    else
                    {
                        cmp = Comparer<TValue>.Default.Compare(left.Value, right.Value);
                    }

                    return Compare(cmp, comparison);
                };
                return true;
            }
        }

        internal sealed class NumericPropertyComparisonOperation<TLeft, TRight, TCompare> : IConditionLeaf, IFastPredicateSource, IPropertyComparisonSource
            where TLeft : INumberBase<TLeft>
            where TRight : INumberBase<TRight>
            where TCompare : INumber<TCompare>
        {
            private readonly PropertyComparisonKind comparison;
            private readonly ValueOrExpression<string> leftSelector;
            private readonly ValueOrExpression<string> rightSelector;

            internal NumericPropertyComparisonOperation(
                PropertyComparisonKind comparison,
                ValueOrExpression<string> leftSelector,
                ValueOrExpression<string> rightSelector)
            {
                this.comparison = comparison;
                this.leftSelector = leftSelector;
                this.rightSelector = rightSelector;
            }

            PropertyComparisonKind IPropertyComparisonSource.Comparison => comparison;
            ValueOrExpression<string> IPropertyComparisonSource.LeftSelector => leftSelector;
            ValueOrExpression<string> IPropertyComparisonSource.RightSelector => rightSelector;
            Type IPropertyComparisonSource.LeftType => typeof(TLeft);
            Type IPropertyComparisonSource.RightType => typeof(TRight);
            Type IPropertyComparisonSource.ComparisonType => typeof(TCompare);
            string IPropertyComparisonSource.Culture => string.Empty;
            bool IPropertyComparisonSource.IgnoreCase => false;

            public bool TryBuildEvaluator(
                Builder builder,
                Inheto.DeserializationOptions options,
                out Func<InhetoBinary, bool> evaluator)
            {
                evaluator = default!;
                if (!TryBuildConditionValueReader<TLeft>(builder, leftSelector, options, out _, out var readLeft) ||
                    !TryBuildConditionValueReader<TRight>(builder, rightSelector, options, out _, out var readRight))
                {
                    return false;
                }

                evaluator = binary =>
                {
                    var left = readLeft(binary);
                    if (!left.HasValue)
                        return false;
                    var right = readRight(binary);
                    if (!right.HasValue)
                        return false;

                    var leftValue = TCompare.CreateChecked(left.Value);
                    var rightValue = TCompare.CreateChecked(right.Value);
                    return Compare(leftValue.CompareTo(rightValue), comparison);
                };
                return true;
            }
        }

        /// <summary>
        /// Selects the declared type of a textual right-hand numeric property before completing a cross-property comparison.<br/>
        /// The selected type is retained independently from the left type so Abraxas can choose one provably lossless comparison domain and reject ambiguous or lossy combinations.<br/>
        /// </summary>
        /// <typeparam name="TLeft">Declared numeric type of the left property.<br/></typeparam>
        public sealed class NumericPropertyComparisonTypeSelector<TLeft> where TLeft : INumber<TLeft>
        {
            private readonly Builder builder;
            private readonly ValueOrExpression<string> leftSelector;
            private readonly ValueOrExpression<string> rightSelector;
            private readonly PropertyComparisonKind comparison;

            internal NumericPropertyComparisonTypeSelector(
                Builder builder,
                ValueOrExpression<string> leftSelector,
                string rightPath,
                PropertyComparisonKind comparison)
            {
                this.builder = builder;
                this.leftSelector = leftSelector;
                rightSelector = WrapPropertyPath(rightPath);
                this.comparison = comparison;
            }

            /// <summary>
            /// Declares the right property's Byte type and appends the property-to-property comparison.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// The left and right selectors are evaluated against the same record when consumed; accessing this member does not read or hydrate a record.<br/>
            /// </remarks>
            public ContinueOrEndCondition AsByte => AddNumericPropertyComparison<TLeft, byte>(builder, leftSelector, rightSelector, comparison);
            /// <summary>
            /// Declares the right property's SByte type and appends the property-to-property comparison.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// The left and right selectors are evaluated against the same record when consumed; accessing this member does not read or hydrate a record.<br/>
            /// </remarks>
            public ContinueOrEndCondition AsSByte => AddNumericPropertyComparison<TLeft, sbyte>(builder, leftSelector, rightSelector, comparison);
            /// <summary>
            /// Declares the right property's Int16 type and appends the property-to-property comparison.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// The left and right selectors are evaluated against the same record when consumed; accessing this member does not read or hydrate a record.<br/>
            /// </remarks>
            public ContinueOrEndCondition AsInt16 => AddNumericPropertyComparison<TLeft, short>(builder, leftSelector, rightSelector, comparison);
            /// <summary>
            /// Declares the right property's UInt16 type and appends the property-to-property comparison.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// The left and right selectors are evaluated against the same record when consumed; accessing this member does not read or hydrate a record.<br/>
            /// </remarks>
            public ContinueOrEndCondition AsUInt16 => AddNumericPropertyComparison<TLeft, ushort>(builder, leftSelector, rightSelector, comparison);
            /// <summary>
            /// Declares the right property's Int32 type and appends the property-to-property comparison.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// The left and right selectors are evaluated against the same record when consumed; accessing this member does not read or hydrate a record.<br/>
            /// </remarks>
            public ContinueOrEndCondition AsInt32 => AddNumericPropertyComparison<TLeft, int>(builder, leftSelector, rightSelector, comparison);
            /// <summary>
            /// Declares the right property's UInt32 type and appends the property-to-property comparison.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// The left and right selectors are evaluated against the same record when consumed; accessing this member does not read or hydrate a record.<br/>
            /// </remarks>
            public ContinueOrEndCondition AsUInt32 => AddNumericPropertyComparison<TLeft, uint>(builder, leftSelector, rightSelector, comparison);
            /// <summary>
            /// Declares the right property's Int64 type and appends the property-to-property comparison.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// The left and right selectors are evaluated against the same record when consumed; accessing this member does not read or hydrate a record.<br/>
            /// </remarks>
            public ContinueOrEndCondition AsInt64 => AddNumericPropertyComparison<TLeft, long>(builder, leftSelector, rightSelector, comparison);
            /// <summary>
            /// Declares the right property's UInt64 type and appends the property-to-property comparison.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// The left and right selectors are evaluated against the same record when consumed; accessing this member does not read or hydrate a record.<br/>
            /// </remarks>
            public ContinueOrEndCondition AsUInt64 => AddNumericPropertyComparison<TLeft, ulong>(builder, leftSelector, rightSelector, comparison);
            /// <summary>
            /// Declares the right property's Int128 type and appends the property-to-property comparison.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// The left and right selectors are evaluated against the same record when consumed; accessing this member does not read or hydrate a record.<br/>
            /// </remarks>
            public ContinueOrEndCondition AsInt128 => AddNumericPropertyComparison<TLeft, Int128>(builder, leftSelector, rightSelector, comparison);
            /// <summary>
            /// Declares the right property's UInt128 type and appends the property-to-property comparison.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// The left and right selectors are evaluated against the same record when consumed; accessing this member does not read or hydrate a record.<br/>
            /// </remarks>
            public ContinueOrEndCondition AsUInt128 => AddNumericPropertyComparison<TLeft, UInt128>(builder, leftSelector, rightSelector, comparison);
            /// <summary>
            /// Declares the right property's BigInteger type and appends the property-to-property comparison.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// The left and right selectors are evaluated against the same record when consumed; accessing this member does not read or hydrate a record.<br/>
            /// </remarks>
            public ContinueOrEndCondition AsBigInteger => AddNumericPropertyComparison<TLeft, BigInteger>(builder, leftSelector, rightSelector, comparison);
            /// <summary>
            /// Declares the right property's Half type and appends the property-to-property comparison.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// The left and right selectors are evaluated against the same record when consumed; accessing this member does not read or hydrate a record.<br/>
            /// </remarks>
            public ContinueOrEndCondition AsHalf => AddNumericPropertyComparison<TLeft, Half>(builder, leftSelector, rightSelector, comparison);
            /// <summary>
            /// Declares the right property's Single type and appends the property-to-property comparison.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// The left and right selectors are evaluated against the same record when consumed; accessing this member does not read or hydrate a record.<br/>
            /// </remarks>
            public ContinueOrEndCondition AsSingle => AddNumericPropertyComparison<TLeft, float>(builder, leftSelector, rightSelector, comparison);
            /// <summary>
            /// Declares the right property's Double type and appends the property-to-property comparison.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// The left and right selectors are evaluated against the same record when consumed; accessing this member does not read or hydrate a record.<br/>
            /// </remarks>
            public ContinueOrEndCondition AsDouble => AddNumericPropertyComparison<TLeft, double>(builder, leftSelector, rightSelector, comparison);
            /// <summary>
            /// Declares the right property's Decimal type and appends the property-to-property comparison.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// The left and right selectors are evaluated against the same record when consumed; accessing this member does not read or hydrate a record.<br/>
            /// </remarks>
            public ContinueOrEndCondition AsDecimal => AddNumericPropertyComparison<TLeft, decimal>(builder, leftSelector, rightSelector, comparison);
        }

        internal sealed class PropertyComparisonTypeSelector<TValue>
        {
            private readonly Builder builder;
            private readonly ValueOrExpression<string> leftSelector;
            private readonly ValueOrExpression<string> rightSelector;
            private readonly PropertyComparisonKind comparison;
            private readonly string culture;
            private readonly bool ignoreCase;

            internal PropertyComparisonTypeSelector(
                Builder builder,
                ValueOrExpression<string> leftSelector,
                string rightPath,
                PropertyComparisonKind comparison,
                string culture = "",
                bool ignoreCase = false)
            {
                this.builder = builder;
                this.leftSelector = leftSelector;
                rightSelector = WrapPropertyPath(rightPath);
                this.comparison = comparison;
                this.culture = culture;
                this.ignoreCase = ignoreCase;
            }

            internal ContinueOrEndCondition Complete()
                => AddPropertyComparison<TValue>(builder, leftSelector, rightSelector, comparison, culture, ignoreCase);

            public ContinueOrEndCondition AsBoolean => EnsureType<bool>();
            public ContinueOrEndCondition AsBinary => EnsureType<byte[]>();
            public ContinueOrEndCondition AsDateTime => EnsureType<DateTime>();
            public ContinueOrEndCondition AsDateOnly => EnsureType<DateOnly>();
            public ContinueOrEndCondition AsTimeOnly => EnsureType<TimeOnly>();
            public ContinueOrEndCondition AsTimeSpan => EnsureType<TimeSpan>();
            public ContinueOrEndCondition AsGuid => EnsureType<Guid>();
            public ContinueOrEndCondition AsString => EnsureType<string>();

            private ContinueOrEndCondition EnsureType<TSelected>()
            {
                if (typeof(TSelected) != typeof(TValue))
                    throw new InvalidOperationException($"The right property must be declared as {typeof(TValue).Name} for this comparison.");
                return Complete();
            }
        }

        /// <summary>Selects the Boolean type of a textual right-hand property and completes the comparison.<br/></summary>
        public sealed class BooleanPropertyComparisonTypeSelector
        {
            private readonly PropertyComparisonTypeSelector<bool> selector;
            internal BooleanPropertyComparisonTypeSelector(Builder builder, ValueOrExpression<string> left, string right, PropertyComparisonKind comparison)
                => selector = new(builder, left, right, comparison);
            /// <summary>
            /// Declares the right property's Boolean type and appends the property-to-property comparison.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// The left and right selectors are evaluated against the same record when consumed; accessing this member does not read or hydrate a record.<br/>
            /// </remarks>
            public ContinueOrEndCondition AsBoolean => selector.AsBoolean;
        }

        /// <summary>Selects the binary type of a textual right-hand property and completes a content comparison.<br/></summary>
        public sealed class BinaryPropertyComparisonTypeSelector
        {
            private readonly PropertyComparisonTypeSelector<byte[]> selector;
            internal BinaryPropertyComparisonTypeSelector(Builder builder, ValueOrExpression<string> left, string right, PropertyComparisonKind comparison)
                => selector = new(builder, left, right, comparison);
            /// <summary>
            /// Declares the right property's Binary type and appends the property-to-property comparison.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// The left and right selectors are evaluated against the same record when consumed; accessing this member does not read or hydrate a record.<br/>
            /// </remarks>
            public ContinueOrEndCondition AsBinary => selector.AsBinary;
        }

        /// <summary>Selects the DateTime type of a textual right-hand property and completes the comparison.<br/></summary>
        public sealed class DateTimePropertyComparisonTypeSelector
        {
            private readonly PropertyComparisonTypeSelector<DateTime> selector;
            internal DateTimePropertyComparisonTypeSelector(Builder builder, ValueOrExpression<string> left, string right, PropertyComparisonKind comparison)
                => selector = new(builder, left, right, comparison);
            /// <summary>
            /// Declares the right property's DateTime type and appends the property-to-property comparison.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// The left and right selectors are evaluated against the same record when consumed; accessing this member does not read or hydrate a record.<br/>
            /// </remarks>
            public ContinueOrEndCondition AsDateTime => selector.AsDateTime;
            /// <summary>
            /// Completes the right property as DateTime; compatibility alias of AsDateTime.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// The left and right selectors are evaluated against the same record when consumed; accessing this member does not read or hydrate a record.<br/>
            /// </remarks>
            public ContinueOrEndCondition AsDate => AsDateTime;
        }

        /// <summary>Selects the DateOnly type of a textual right-hand property and completes the comparison.<br/></summary>
        public sealed class DateOnlyPropertyComparisonTypeSelector
        {
            private readonly PropertyComparisonTypeSelector<DateOnly> selector;
            internal DateOnlyPropertyComparisonTypeSelector(Builder builder, ValueOrExpression<string> left, string right, PropertyComparisonKind comparison)
                => selector = new(builder, left, right, comparison);
            /// <summary>
            /// Declares the right property's DateOnly type and appends the property-to-property comparison.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// The left and right selectors are evaluated against the same record when consumed; accessing this member does not read or hydrate a record.<br/>
            /// </remarks>
            public ContinueOrEndCondition AsDateOnly => selector.AsDateOnly;
        }

        /// <summary>Selects the TimeOnly type of a textual right-hand property and completes the comparison.<br/></summary>
        public sealed class TimeOnlyPropertyComparisonTypeSelector
        {
            private readonly PropertyComparisonTypeSelector<TimeOnly> selector;
            internal TimeOnlyPropertyComparisonTypeSelector(Builder builder, ValueOrExpression<string> left, string right, PropertyComparisonKind comparison)
                => selector = new(builder, left, right, comparison);
            /// <summary>
            /// Declares the right property's TimeOnly type and appends the property-to-property comparison.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// The left and right selectors are evaluated against the same record when consumed; accessing this member does not read or hydrate a record.<br/>
            /// </remarks>
            public ContinueOrEndCondition AsTimeOnly => selector.AsTimeOnly;
        }

        /// <summary>Selects the TimeSpan type of a textual right-hand property and completes the comparison.<br/></summary>
        public sealed class TimeSpanPropertyComparisonTypeSelector
        {
            private readonly PropertyComparisonTypeSelector<TimeSpan> selector;
            internal TimeSpanPropertyComparisonTypeSelector(Builder builder, ValueOrExpression<string> left, string right, PropertyComparisonKind comparison)
                => selector = new(builder, left, right, comparison);
            /// <summary>
            /// Declares the right property's TimeSpan type and appends the property-to-property comparison.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// The left and right selectors are evaluated against the same record when consumed; accessing this member does not read or hydrate a record.<br/>
            /// </remarks>
            public ContinueOrEndCondition AsTimeSpan => selector.AsTimeSpan;
        }

        /// <summary>Selects the Guid type of a textual right-hand property and completes an equality comparison.<br/></summary>
        public sealed class GuidPropertyComparisonTypeSelector
        {
            private readonly PropertyComparisonTypeSelector<Guid> selector;
            internal GuidPropertyComparisonTypeSelector(Builder builder, ValueOrExpression<string> left, string right, PropertyComparisonKind comparison)
                => selector = new(builder, left, right, comparison);
            /// <summary>
            /// Declares the right property's Guid type and appends the property-to-property comparison.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// The left and right selectors are evaluated against the same record when consumed; accessing this member does not read or hydrate a record.<br/>
            /// </remarks>
            public ContinueOrEndCondition AsGuid => selector.AsGuid;
        }

        /// <summary>Selects the string type of a textual right-hand property and completes the comparison with its frozen collation contract.<br/></summary>
        public sealed class StringPropertyComparisonTypeSelector
        {
            private readonly PropertyComparisonTypeSelector<string> selector;
            internal StringPropertyComparisonTypeSelector(Builder builder, ValueOrExpression<string> left, string right, PropertyComparisonKind comparison, string culture, bool ignoreCase)
                => selector = new(builder, left, right, comparison, culture, ignoreCase);
            /// <summary>
            /// Declares the right property's String type and appends the property-to-property comparison.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// The left and right selectors are evaluated against the same record when consumed; accessing this member does not read or hydrate a record.<br/>
            /// </remarks>
            public ContinueOrEndCondition AsString => selector.AsString;
        }

        internal static ContinueOrEndCondition AddPropertyComparison<TValue>(
            Builder builder,
            ValueOrExpression<string> leftSelector,
            ValueOrExpression<string> rightSelector,
            PropertyComparisonKind comparison,
            string culture = "",
            bool ignoreCase = false)
        {
            builder.AddFragment(new PropertyComparisonOperation<TValue>(
                comparison,
                leftSelector,
                rightSelector,
                culture,
                ignoreCase));
            return new ContinueOrEndCondition(builder);
        }

        internal static ContinueOrEndCondition AddNumericPropertyComparison<TLeft, TRight>(
            Builder builder,
            ValueOrExpression<string> leftSelector,
            ValueOrExpression<string> rightSelector,
            PropertyComparisonKind comparison)
            where TLeft : INumber<TLeft>
            where TRight : INumber<TRight>
        {
            var compareType = NumericPropertyPromotion.Resolve(typeof(TLeft), typeof(TRight));
            var operationType = typeof(Condition<>).GetNestedType(
                    "NumericPropertyComparisonOperation`3",
                    System.Reflection.BindingFlags.NonPublic)!
                .MakeGenericType(typeof(T), typeof(TLeft), typeof(TRight), compareType);
            var operation = (IConditionFragment?)Activator.CreateInstance(
                operationType,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                binder: null,
                args: new object[] { comparison, leftSelector, rightSelector },
                culture: CultureInfo.InvariantCulture)
                ?? throw new InvalidOperationException("The numeric property comparison could not be created.");
            builder.AddFragment(operation);
            return new ContinueOrEndCondition(builder);
        }

        internal static ValueOrExpression<string> WrapPropertyPath(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            return ((ValueOrExpression<string>)(() => string.Empty))
                .WithSelectorDescriptor(new DirectPropertySelectorDescriptor(path));
        }

        internal static ValueOrExpression<string> WrapPropertyExpression<TValue>(Expression<Func<T, TValue>> expression)
        {
            ArgumentNullException.ThrowIfNull(expression);
            return WrapPropertyPath(expression.GetPropertyPath());
        }

        private static bool Compare(int value, PropertyComparisonKind comparison)
            => comparison switch
            {
                PropertyComparisonKind.Equal => value == 0,
                PropertyComparisonKind.NotEqual => value != 0,
                PropertyComparisonKind.Greater => value > 0,
                PropertyComparisonKind.GreaterOrEqual => value >= 0,
                PropertyComparisonKind.Less => value < 0,
                PropertyComparisonKind.LessOrEqual => value <= 0,
                _ => false
            };

        internal static class NumericPropertyPromotion
        {
            private static readonly Type[] IntegerTypes =
            [
                typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
                typeof(long), typeof(ulong), typeof(Int128), typeof(UInt128), typeof(BigInteger)
            ];

            internal static Type Resolve(Type left, Type right)
            {
                if (left == right)
                    return left;

                bool leftInteger = IsInteger(left);
                bool rightInteger = IsInteger(right);
                if (leftInteger && rightInteger)
                    return ResolveInteger(left, right);

                if (left == typeof(decimal) || right == typeof(decimal))
                {
                    var other = left == typeof(decimal) ? right : left;
                    if (other == typeof(decimal) || IsIntegerThroughUInt64(other))
                        return typeof(decimal);
                    throw Unsupported(left, right);
                }

                if (IsBinaryFloat(left) || IsBinaryFloat(right))
                    return ResolveBinaryFloat(left, right);

                throw Unsupported(left, right);
            }

            internal static bool IsSupported(Type type)
                => IsInteger(type) || IsBinaryFloat(type) || type == typeof(decimal);

            private static Type ResolveInteger(Type left, Type right)
            {
                GetRange(left, out var leftMin, out var leftMax);
                GetRange(right, out var rightMin, out var rightMax);
                var min = BigInteger.Min(leftMin, rightMin);
                var max = BigInteger.Max(leftMax, rightMax);
                foreach (var candidate in IntegerTypes)
                {
                    GetRange(candidate, out var candidateMin, out var candidateMax);
                    if (candidateMin <= min && candidateMax >= max)
                        return candidate;
                }
                return typeof(BigInteger);
            }

            private static Type ResolveBinaryFloat(Type left, Type right)
            {
                if (left == typeof(double) || right == typeof(double))
                {
                    var other = left == typeof(double) ? right : left;
                    if (IsBinaryFloat(other) || FitsBinaryInteger(other, 53))
                        return typeof(double);
                    throw Unsupported(left, right);
                }
                if (left == typeof(float) || right == typeof(float))
                {
                    var other = left == typeof(float) ? right : left;
                    if (other == typeof(Half) || other == typeof(float) || FitsBinaryInteger(other, 24))
                        return typeof(float);
                    throw Unsupported(left, right);
                }

                var halfOther = left == typeof(Half) ? right : left;
                if (halfOther == typeof(Half) || FitsBinaryInteger(halfOther, 11))
                    return typeof(Half);
                throw Unsupported(left, right);
            }

            private static bool FitsBinaryInteger(Type type, int significantBits)
            {
                if (!IsInteger(type) || type == typeof(BigInteger))
                    return false;
                GetRange(type, out var min, out var max);
                var limit = BigInteger.One << significantBits;
                return min >= -limit && max <= limit;
            }

            private static bool IsInteger(Type type) => IntegerTypes.Contains(type);
            private static bool IsBinaryFloat(Type type) => type == typeof(Half) || type == typeof(float) || type == typeof(double);
            private static bool IsIntegerThroughUInt64(Type type)
                => type == typeof(sbyte) || type == typeof(byte) || type == typeof(short) || type == typeof(ushort) ||
                   type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);

            private static void GetRange(Type type, out BigInteger min, out BigInteger max)
            {
                if (type == typeof(sbyte)) { min = sbyte.MinValue; max = sbyte.MaxValue; return; }
                if (type == typeof(byte)) { min = byte.MinValue; max = byte.MaxValue; return; }
                if (type == typeof(short)) { min = short.MinValue; max = short.MaxValue; return; }
                if (type == typeof(ushort)) { min = ushort.MinValue; max = ushort.MaxValue; return; }
                if (type == typeof(int)) { min = int.MinValue; max = int.MaxValue; return; }
                if (type == typeof(uint)) { min = uint.MinValue; max = uint.MaxValue; return; }
                if (type == typeof(long)) { min = long.MinValue; max = long.MaxValue; return; }
                if (type == typeof(ulong)) { min = ulong.MinValue; max = ulong.MaxValue; return; }
                if (type == typeof(Int128)) { min = BigInteger.Parse(Int128.MinValue.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture); max = BigInteger.Parse(Int128.MaxValue.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture); return; }
                if (type == typeof(UInt128)) { min = BigInteger.Zero; max = BigInteger.Parse(UInt128.MaxValue.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture); return; }
                if (type == typeof(BigInteger)) { min = BigInteger.MinusOne << 4096; max = -(min + BigInteger.One); return; }
                throw new NotSupportedException($"Numeric type '{type.FullName}' is not supported for property comparison.");
            }

            private static NotSupportedException Unsupported(Type left, Type right)
                => new($"Property comparison between '{left.Name}' and '{right.Name}' has no provably lossless common numeric domain. Select explicit compatible types or normalize one operand in the model.");
        }
    }
}
