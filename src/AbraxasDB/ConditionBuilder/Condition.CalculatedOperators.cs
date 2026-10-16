using System.Globalization;
using System.Linq.Expressions;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace AbraxasDB
{
    public static partial class Condition<T>
    {
        /// <summary>
        /// Continues a condition-local string calculation while retaining the complete reusable selector descriptor.<br/>
        /// Comparisons are inherited from <see cref="StringOperator"/>; transformations return another condition-attached typed stage.<br/>
        /// </summary>
        public sealed class StringCalc : StringOperator
        {
            private readonly Builder conditionBuilder;
            internal Selector<T>.StringSelector SemanticSelector { get; }

            internal StringCalc(Builder builder, Selector<T>.StringSelector selector)
                : base(builder, Clause.WrapSelector(selector))
            {
                conditionBuilder = builder;
                SemanticSelector = selector;
            }

            private StringCalc Next(Selector<T>.StringSelector selector) => new(conditionBuilder, selector);
            private IntegralCalc<long> Next(Selector<T>.IntegralSelector<long> selector) => new(conditionBuilder, selector);
            private BinaryCalc Next(Selector<T>.BinarySelector selector) => new(conditionBuilder, selector);

            /// <summary>Returns the selected regex match or capture as the next inline string value.<br/></summary>
            public StringCalc MatchValue(Regex pattern, int groupNumber = 0, int? occurrenceNumber = null) => Next(SemanticSelector.MatchValue(pattern, groupNumber, occurrenceNumber));
            /// <summary>Returns the selected named regex capture as the next inline string value.<br/></summary>
            public new StringCalc MatchValue(Regex pattern, string groupName, int? occurrenceNumber = null) => Next(SemanticSelector.MatchValue(pattern, groupName, occurrenceNumber));
            /// <summary>Compiles the textual regex and returns its selected numeric capture as the next inline string value.<br/></summary>
            public new StringCalc MatchValue(string pattern, int groupNumber = 0, int? occurrenceNumber = null, RegexOptions options = RegexOptions.CultureInvariant) => Next(SemanticSelector.MatchValue(pattern, groupNumber, occurrenceNumber, options));
            /// <summary>Compiles the textual regex and returns its selected named capture as the next inline string value.<br/></summary>
            public new StringCalc MatchValue(string pattern, string groupName, int? occurrenceNumber = null, RegexOptions options = RegexOptions.CultureInvariant) => Next(SemanticSelector.MatchValue(pattern, groupName, occurrenceNumber, options));
            /// <summary>Selects a UTF-16 substring before the condition comparison is authored.<br/></summary>
            public StringCalc Substring(long start, long length) => Next(SemanticSelector.Substring(start, length));
            /// <summary>Selects the UTF-16 suffix beginning at the supplied offset.<br/></summary>
            public StringCalc Substring(long start) => Next(SemanticSelector.Substring(start));
            /// <summary>Selects at most the requested number of leading UTF-16 code units.<br/></summary>
            public StringCalc Left(long length) => Next(SemanticSelector.Left(length));
            /// <summary>Selects at most the requested number of trailing UTF-16 code units.<br/></summary>
            public StringCalc Right(long length) => Next(SemanticSelector.Right(length));
            /// <summary>Selects a Unicode-scalar substring before comparison.<br/></summary>
            public StringCalc RuneSubstring(long start, long length) => Next(SemanticSelector.RuneSubstring(start, length));
            /// <summary>Selects the Unicode-scalar suffix beginning at the supplied offset.<br/></summary>
            public StringCalc RuneSubstring(long start) => Next(SemanticSelector.RuneSubstring(start));
            /// <summary>Selects at most the requested number of leading Unicode scalars.<br/></summary>
            public StringCalc RuneLeft(long length) => Next(SemanticSelector.RuneLeft(length));
            /// <summary>Selects at most the requested number of trailing Unicode scalars.<br/></summary>
            public StringCalc RuneRight(long length) => Next(SemanticSelector.RuneRight(length));
            /// <summary>Calculates the first matching position and continues as an Int64 condition value.<br/></summary>
            public IntegralCalc<long> IndexOf(ValueOrExpression<string> value, bool ignoreCase = false, string culture = "") => Next(SemanticSelector.IndexOf(value, ignoreCase, culture));
            /// <summary>Calculates the last matching position and continues as an Int64 condition value.<br/></summary>
            public IntegralCalc<long> LastIndexOf(ValueOrExpression<string> value, bool ignoreCase = false, string culture = "") => Next(SemanticSelector.LastIndexOf(value, ignoreCase, culture));
            /// <summary>Applies allocation-conscious ASCII lower casing before comparison.<br/></summary>
            public StringCalc AsciiLower() => Next(SemanticSelector.AsciiLower());
            /// <summary>Applies allocation-conscious ASCII upper casing before comparison.<br/></summary>
            public StringCalc AsciiUpper() => Next(SemanticSelector.AsciiUpper());
            /// <summary>Applies invariant Unicode lower casing before comparison.<br/></summary>
            public StringCalc LowerInvariant() => Next(SemanticSelector.LowerInvariant());
            /// <summary>Applies invariant Unicode upper casing before comparison.<br/></summary>
            public StringCalc UpperInvariant() => Next(SemanticSelector.UpperInvariant());
            /// <summary>Applies culture-specific lower casing before comparison.<br/></summary>
            public StringCalc Lower(string culture) => Next(SemanticSelector.Lower(culture));
            /// <summary>Applies culture-specific upper casing before comparison.<br/></summary>
            public StringCalc Upper(string culture) => Next(SemanticSelector.Upper(culture));
            /// <summary>Normalizes the selected text before comparison.<br/></summary>
            public StringCalc Normalize() => Next(SemanticSelector.Normalize());
            /// <summary>Produces the requested culture sort key as the next binary condition value.<br/></summary>
            public BinaryCalc SortKey(string culture = "", CompareOptions options = CompareOptions.IgnoreCase) => Next(SemanticSelector.SortKey(culture, options));
            /// <summary>Trims ordinary whitespace before comparison.<br/></summary>
            public StringCalc Trim() => Next(SemanticSelector.Trim());
            /// <summary>Trims the supplied character set from both ends before comparison.<br/></summary>
            public StringCalc Trim(ValueOrExpression<string> trimChars) => Next(SemanticSelector.Trim(trimChars));
            /// <summary>Trims ordinary whitespace from the start before comparison.<br/></summary>
            public StringCalc TrimStart() => Next(SemanticSelector.TrimStart());
            /// <summary>Trims the supplied character set from the start before comparison.<br/></summary>
            public StringCalc TrimStart(ValueOrExpression<string> trimChars) => Next(SemanticSelector.TrimStart(trimChars));
            /// <summary>Trims ordinary whitespace from the end before comparison.<br/></summary>
            public StringCalc TrimEnd() => Next(SemanticSelector.TrimEnd());
            /// <summary>Trims the supplied character set from the end before comparison.<br/></summary>
            public StringCalc TrimEnd(ValueOrExpression<string> trimChars) => Next(SemanticSelector.TrimEnd(trimChars));
            /// <summary>Applies ASCII whitespace trimming before comparison.<br/></summary>
            public StringCalc TrimAscii() => Next(SemanticSelector.TrimAscii());
            /// <summary>Applies ASCII character-set trimming before comparison.<br/></summary>
            public StringCalc TrimAscii(ValueOrExpression<string> trimChars) => Next(SemanticSelector.TrimAscii(trimChars));
            /// <summary>Applies ASCII start trimming before comparison.<br/></summary>
            public StringCalc TrimStartAscii() => Next(SemanticSelector.TrimStartAscii());
            /// <summary>Applies ASCII character-set start trimming before comparison.<br/></summary>
            public StringCalc TrimStartAscii(ValueOrExpression<string> trimChars) => Next(SemanticSelector.TrimStartAscii(trimChars));
            /// <summary>Applies ASCII end trimming before comparison.<br/></summary>
            public StringCalc TrimEndAscii() => Next(SemanticSelector.TrimEndAscii());
            /// <summary>Applies ASCII character-set end trimming before comparison.<br/></summary>
            public StringCalc TrimEndAscii(ValueOrExpression<string> trimChars) => Next(SemanticSelector.TrimEndAscii(trimChars));
            /// <summary>Replaces every ordinal occurrence before the condition comparison is authored.<br/></summary>
            public StringCalc Replace(ValueOrExpression<string> oldValue, ValueOrExpression<string> newValue) => Next(SemanticSelector.Replace(oldValue, newValue));
            /// <summary>Replaces occurrences using the requested invariant case policy before comparison.<br/></summary>
            public StringCalc Replace(ValueOrExpression<string> oldValue, ValueOrExpression<string> newValue, bool ignoreCase) => Next(SemanticSelector.Replace(oldValue, newValue, ignoreCase));
            /// <summary>Replaces occurrences using the requested case and culture policy before comparison.<br/></summary>
            public StringCalc Replace(ValueOrExpression<string> oldValue, ValueOrExpression<string> newValue, bool ignoreCase, string culture) => Next(SemanticSelector.Replace(oldValue, newValue, ignoreCase, culture));
            /// <summary>Pads the selected text on the left before comparison.<br/></summary>
            public StringCalc PadLeft(long totalWidth, char paddingChar = ' ') => Next(SemanticSelector.PadLeft(totalWidth, paddingChar));
            /// <summary>Pads the selected text on the right before comparison.<br/></summary>
            public StringCalc PadRight(long totalWidth, char paddingChar = ' ') => Next(SemanticSelector.PadRight(totalWidth, paddingChar));
            /// <summary>Inserts text at the supplied UTF-16 offset before comparison.<br/></summary>
            public StringCalc Insert(long startIndex, ValueOrExpression<string> value) => Next(SemanticSelector.Insert(startIndex, value));
            /// <summary>Removes the suffix beginning at the supplied UTF-16 offset before comparison.<br/></summary>
            public StringCalc Remove(long startIndex) => Next(SemanticSelector.Remove(startIndex));
            /// <summary>Removes the requested UTF-16 range before comparison.<br/></summary>
            public StringCalc Remove(long startIndex, long count) => Next(SemanticSelector.Remove(startIndex, count));
            /// <summary>Applies a runtime-only application transformation before comparison.<br/></summary>
            public StringCalc External(Func<string, string?> operation) => Next(SemanticSelector.External(operation));
            /// <summary>Encodes the selected text and continues as a binary condition value.<br/></summary>
            public BinaryCalc ToBinary(EncodingKind encodingKind) => Next(SemanticSelector.ToBinary(encodingKind));
            /// <summary>Concatenates a literal or deferred value before comparison.<br/></summary>
            public StringCalc Concat(ValueOrExpression<string> value) => Next(SemanticSelector.Concat(value));
            /// <summary>Concatenates another reusable string selector before comparison.<br/></summary>
            public StringCalc Concat(Selector<T>.StringSelector value) => Next(SemanticSelector.Concat(value));
            /// <summary>Concatenates another inline string calculation before comparison.<br/></summary>
            public StringCalc Concat(StringCalc value) => Next(SemanticSelector.Concat(value.SemanticSelector));
            /// <summary>Returns the UTF-16 length as an Int64 condition value.<br/></summary>
            public IntegralCalc<long> Length => Next(SemanticSelector.Length);
            /// <summary>Returns the Unicode-scalar count as an Int64 condition value.<br/></summary>
            public IntegralCalc<long> RuneCount => Next(SemanticSelector.RuneCount);
            /// <summary>Returns the UTF-8 encoding as a binary condition value.<br/></summary>
            public BinaryCalc AsUtf8 => Next(SemanticSelector.AsUtf8);
        }

        /// <summary>Continues a condition-local binary calculation and inherits binary comparisons.<br/></summary>
        public sealed class BinaryCalc : BinaryOperator
        {
            private readonly Builder conditionBuilder;
            internal Selector<T>.BinarySelector SemanticSelector { get; }
            internal BinaryCalc(Builder builder, Selector<T>.BinarySelector selector) : base(builder, Clause.WrapSelector(selector)) { conditionBuilder = builder; SemanticSelector = selector; }
            /// <summary>Selects a byte range before comparison.<br/></summary>
            public BinaryCalc Slice(ulong start, ulong length) => new(conditionBuilder, SemanticSelector.Slice(start, length));
            /// <summary>Returns one indexed byte as an integral condition value.<br/></summary>
            public IntegralCalc<byte> GetByte(ulong index) => new(conditionBuilder, SemanticSelector.GetByte(index));
            /// <summary>Converts the selected bytes to hexadecimal text before comparison.<br/></summary>
            public StringCalc ToHex() => new(conditionBuilder, SemanticSelector.ToHex());
            /// <summary>Converts the selected bytes to Base64 text before comparison.<br/></summary>
            public StringCalc ToBase64() => new(conditionBuilder, SemanticSelector.ToBase64());
        }

        /// <summary>Continues a condition-local Guid calculation and inherits Guid comparisons.<br/></summary>
        public sealed class GuidCalc : GuidOperator
        {
            private readonly Builder conditionBuilder;
            internal Selector<T>.GuidSelector SemanticSelector { get; }
            internal GuidCalc(Builder builder, Selector<T>.GuidSelector selector) : base(builder, Clause.WrapSelector(selector)) { conditionBuilder = builder; SemanticSelector = selector; }
            /// <summary>Formats the Guid and continues as a string condition value.<br/></summary>
            public StringCalc AsString(GuidStringFormat format = GuidStringFormat.Hyphenated) => new(conditionBuilder, SemanticSelector.AsString(format));
            /// <summary>Selects one canonical Guid segment as a binary condition value.<br/></summary>
            public BinaryCalc Segment(int segment) => new(conditionBuilder, SemanticSelector.Segment(segment));
            /// <summary>Selects a byte range from the canonical Guid representation.<br/></summary>
            public BinaryCalc Slice(int offset, int length) => new(conditionBuilder, SemanticSelector.Slice(offset, length));
            /// <summary>Returns the canonical Guid bytes as a binary condition value.<br/></summary>
            public BinaryCalc AsBinary => new(conditionBuilder, SemanticSelector.AsBinary);
        }

        /// <summary>Continues a condition-local integral calculation and inherits numeric comparisons.<br/></summary>
        public sealed class IntegralCalc<TInt> : NumericOperator<TInt> where TInt : INumber<TInt>, IBinaryInteger<TInt>
        {
            private readonly Builder conditionBuilder;
            internal Selector<T>.IntegralSelector<TInt> SemanticSelector { get; }
            internal IntegralCalc(Builder builder, Selector<T>.IntegralSelector<TInt> selector) : base(builder, Clause.WrapSelector(selector)) { conditionBuilder = builder; SemanticSelector = selector; }
            private IntegralCalc<TResult> Integral<TResult>(Selector<T>.IntegralSelector<TResult> selector) where TResult : INumber<TResult>, IBinaryInteger<TResult> => new(conditionBuilder, selector);
            private RealCalc<TResult> Real<TResult>(Selector<T>.RealSelector<TResult> selector) where TResult : INumber<TResult> => new(conditionBuilder, selector);
            /// <summary>Adds a same-type literal before comparison.<br/></summary>
            public IntegralCalc<TInt> Add(TInt value) => Integral(SemanticSelector.Add(value));
            /// <summary>Adds an unsigned 64-bit literal using selector promotion rules.<br/></summary>
            public IntegralCalc<ulong> Add(ulong value) => Integral(SemanticSelector.Add(value));
            /// <summary>Adds a signed 64-bit literal using selector promotion rules.<br/></summary>
            public IntegralCalc<long> Add(long value) => Integral(SemanticSelector.Add(value));
            /// <summary>Adds a Double literal and continues as a real condition value.<br/></summary>
            public RealCalc<double> Add(double value) => Real(SemanticSelector.Add(value));
            /// <summary>Subtracts a same-type literal before comparison.<br/></summary>
            public IntegralCalc<TInt> Subtract(TInt value) => Integral(SemanticSelector.Subtract(value));
            /// <summary>Subtracts an unsigned 64-bit literal using selector promotion rules.<br/></summary>
            public IntegralCalc<ulong> Subtract(ulong value) => Integral(SemanticSelector.Subtract(value));
            /// <summary>Subtracts a signed 64-bit literal using selector promotion rules.<br/></summary>
            public IntegralCalc<long> Subtract(long value) => Integral(SemanticSelector.Subtract(value));
            /// <summary>Subtracts a Double literal and continues as a real condition value.<br/></summary>
            public RealCalc<double> Subtract(double value) => Real(SemanticSelector.Subtract(value));
            /// <summary>Multiplies by a same-type literal before comparison.<br/></summary>
            public IntegralCalc<TInt> Multiply(TInt value) => Integral(SemanticSelector.Multiply(value));
            /// <summary>Multiplies by an unsigned 64-bit literal using selector promotion rules.<br/></summary>
            public IntegralCalc<ulong> Multiply(ulong value) => Integral(SemanticSelector.Multiply(value));
            /// <summary>Multiplies by a signed 64-bit literal using selector promotion rules.<br/></summary>
            public IntegralCalc<long> Multiply(long value) => Integral(SemanticSelector.Multiply(value));
            /// <summary>Multiplies by a Double literal and continues as a real condition value.<br/></summary>
            public RealCalc<double> Multiply(double value) => Real(SemanticSelector.Multiply(value));
            /// <summary>Divides by a same-type literal before comparison.<br/></summary>
            public IntegralCalc<TInt> Divide(TInt value) => Integral(SemanticSelector.Divide(value));
            /// <summary>Divides by an unsigned 64-bit literal using selector promotion rules.<br/></summary>
            public IntegralCalc<ulong> Divide(ulong value) => Integral(SemanticSelector.Divide(value));
            /// <summary>Divides by a signed 64-bit literal using selector promotion rules.<br/></summary>
            public IntegralCalc<long> Divide(long value) => Integral(SemanticSelector.Divide(value));
            /// <summary>Divides by a Double literal and continues as a real condition value.<br/></summary>
            public RealCalc<double> Divide(double value) => Real(SemanticSelector.Divide(value));
            /// <summary>Calculates same-type remainder before comparison.<br/></summary>
            public IntegralCalc<TInt> Modulo(TInt value) => Integral(SemanticSelector.Modulo(value));
            /// <summary>Calculates remainder with an unsigned 64-bit literal using selector promotion rules.<br/></summary>
            public IntegralCalc<ulong> Modulo(ulong value) => Integral(SemanticSelector.Modulo(value));
            /// <summary>Calculates remainder with a signed 64-bit literal using selector promotion rules.<br/></summary>
            public IntegralCalc<long> Modulo(long value) => Integral(SemanticSelector.Modulo(value));
            /// <summary>Calculates remainder with a Double literal and continues as a real condition value.<br/></summary>
            public RealCalc<double> Modulo(double value) => Real(SemanticSelector.Modulo(value));
            /// <summary>Negates the integral value before comparison.<br/></summary>
            public IntegralCalc<TInt> Negate() => Integral(SemanticSelector.Negate());
            /// <summary>Applies a same-width bitwise AND mask before comparison; intentionally hides the numeric terminal's zero-comparison shorthand.<br/></summary>
            public new IntegralCalc<TInt> BitAnd(TInt value) => Integral(SemanticSelector.BitAnd(value));
            /// <summary>Applies a same-width bitwise OR operand before comparison; intentionally hides the numeric terminal's zero-comparison shorthand.<br/></summary>
            public new IntegralCalc<TInt> BitOr(TInt value) => Integral(SemanticSelector.BitOr(value));
            /// <summary>Applies a same-width bitwise XOR operand before comparison.<br/></summary>
            public IntegralCalc<TInt> BitXor(TInt value) => Integral(SemanticSelector.BitXor(value));
            /// <summary>Shifts the value left before comparison.<br/></summary>
            public IntegralCalc<TInt> LeftShift(int bits) => Integral(SemanticSelector.LeftShift(bits));
            /// <summary>Shifts the value right before comparison.<br/></summary>
            public IntegralCalc<TInt> RightShift(int bits) => Integral(SemanticSelector.RightShift(bits));
            /// <summary>Complements every bit before comparison.<br/></summary>
            public IntegralCalc<TInt> BitNot() => Integral(SemanticSelector.BitNot());
            /// <summary>Formats the numeric value as hexadecimal text before comparison.<br/></summary>
            public StringCalc ToHex(bool bigEndian = false) => new(conditionBuilder, SemanticSelector.ToHex(bigEndian));
            /// <summary>Formats the numeric value as Base64 text before comparison.<br/></summary>
            public StringCalc ToBase64(bool bigEndian = false) => new(conditionBuilder, SemanticSelector.ToBase64(bigEndian));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<TInt, TRight> PropAdd<TRight>(Expression<Func<T, TRight>> property) where TRight : INumber<TRight> => new(conditionBuilder, SemanticSelector.PropAdd(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyPathCalc<TInt> PropAdd(string propertyPath) => new(conditionBuilder, SemanticSelector.PropAdd(propertyPath));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<TInt, TRight> PropSubtract<TRight>(Expression<Func<T, TRight>> property) where TRight : INumber<TRight> => new(conditionBuilder, SemanticSelector.PropSubtract(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyPathCalc<TInt> PropSubtract(string propertyPath) => new(conditionBuilder, SemanticSelector.PropSubtract(propertyPath));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<TInt, TRight> PropMultiply<TRight>(Expression<Func<T, TRight>> property) where TRight : INumber<TRight> => new(conditionBuilder, SemanticSelector.PropMultiply(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyPathCalc<TInt> PropMultiply(string propertyPath) => new(conditionBuilder, SemanticSelector.PropMultiply(propertyPath));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<TInt, TRight> PropDivide<TRight>(Expression<Func<T, TRight>> property) where TRight : INumber<TRight> => new(conditionBuilder, SemanticSelector.PropDivide(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyPathCalc<TInt> PropDivide(string propertyPath) => new(conditionBuilder, SemanticSelector.PropDivide(propertyPath));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<TInt, TRight> PropModulo<TRight>(Expression<Func<T, TRight>> property) where TRight : INumber<TRight> => new(conditionBuilder, SemanticSelector.PropModulo(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyPathCalc<TInt> PropModulo(string propertyPath) => new(conditionBuilder, SemanticSelector.PropModulo(propertyPath));
        }

        /// <summary>Continues a condition-local real-number calculation and inherits numeric comparisons.<br/></summary>
        public sealed class RealCalc<TReal> : NumericOperator<TReal> where TReal : INumber<TReal>
        {
            private readonly Builder conditionBuilder;
            internal Selector<T>.RealSelector<TReal> SemanticSelector { get; }
            internal RealCalc(Builder builder, Selector<T>.RealSelector<TReal> selector) : base(builder, Clause.WrapSelector(selector)) { conditionBuilder = builder; SemanticSelector = selector; }
            private RealCalc<TResult> Next<TResult>(Selector<T>.RealSelector<TResult> selector) where TResult : INumber<TResult> => new(conditionBuilder, selector);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<TReal> Add(TReal value) => Next(SemanticSelector.Add(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<TReal> Add(ulong value) => Next(SemanticSelector.Add(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<TReal> Add(long value) => Next(SemanticSelector.Add(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<double> Add(double value) => Next(SemanticSelector.Add(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<TReal> Subtract(TReal value) => Next(SemanticSelector.Subtract(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<TReal> Subtract(ulong value) => Next(SemanticSelector.Subtract(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<TReal> Subtract(long value) => Next(SemanticSelector.Subtract(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<double> Subtract(double value) => Next(SemanticSelector.Subtract(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<TReal> Multiply(TReal value) => Next(SemanticSelector.Multiply(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<TReal> Multiply(ulong value) => Next(SemanticSelector.Multiply(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<TReal> Multiply(long value) => Next(SemanticSelector.Multiply(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<double> Multiply(double value) => Next(SemanticSelector.Multiply(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<TReal> Divide(TReal value) => Next(SemanticSelector.Divide(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<TReal> Divide(ulong value) => Next(SemanticSelector.Divide(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<TReal> Divide(long value) => Next(SemanticSelector.Divide(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<double> Divide(double value) => Next(SemanticSelector.Divide(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<TReal> Modulo(TReal value) => Next(SemanticSelector.Modulo(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<TReal> Modulo(ulong value) => Next(SemanticSelector.Modulo(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<TReal> Modulo(long value) => Next(SemanticSelector.Modulo(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<double> Modulo(double value) => Next(SemanticSelector.Modulo(value));
            /// <summary>
            /// Adds semantic intent to reduce the current real value by a same-domain rate.<br/>
            /// Execution later evaluates <c>value * (1 - rate)</c>; condition construction performs no calculation or coercion.<br/>
            /// </summary>
            /// <param name="rate">Discount rate in the current real-number domain.<br/></param>
            /// <returns>The condition-local real calculation continuation.<br/></returns>
            public RealCalc<TReal> Discount(TReal rate) => Next(SemanticSelector.Discount(rate));
            /// <summary>
            /// Adds semantic intent to reduce the current real value by a double rate and promotes narrower real sources to the double execution domain.<br/>
            /// </summary>
            /// <param name="rate">Double discount rate.<br/></param>
            /// <returns>The promoted condition-local double calculation continuation.<br/></returns>
            public RealCalc<double> Discount(double rate) => Next(SemanticSelector.Discount(rate));
            /// <summary>
            /// Adds semantic intent to reduce the current real value by a reusable same-type selector.<br/>
            /// Both selector graphs remain deferred until condition execution.<br/>
            /// </summary>
            /// <param name="rate">Selector supplying the per-record discount rate.<br/></param>
            /// <returns>The condition-local real calculation continuation.<br/></returns>
            public RealCalc<TReal> Discount(Selector<T>.RealSelector<TReal> rate) => Next(SemanticSelector.Discount(rate));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<TReal> Negate() => Next(SemanticSelector.Negate());
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public StringCalc ToHex(bool bigEndian = false) => new(conditionBuilder, SemanticSelector.ToHex(bigEndian));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public StringCalc ToBase64(bool bigEndian = false) => new(conditionBuilder, SemanticSelector.ToBase64(bigEndian));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<TReal, TRight> PropAdd<TRight>(Expression<Func<T, TRight>> property) where TRight : INumber<TRight> => new(conditionBuilder, SemanticSelector.PropAdd(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyPathCalc<TReal> PropAdd(string propertyPath) => new(conditionBuilder, SemanticSelector.PropAdd(propertyPath));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<TReal, TRight> PropSubtract<TRight>(Expression<Func<T, TRight>> property) where TRight : INumber<TRight> => new(conditionBuilder, SemanticSelector.PropSubtract(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyPathCalc<TReal> PropSubtract(string propertyPath) => new(conditionBuilder, SemanticSelector.PropSubtract(propertyPath));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<TReal, TRight> PropMultiply<TRight>(Expression<Func<T, TRight>> property) where TRight : INumber<TRight> => new(conditionBuilder, SemanticSelector.PropMultiply(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyPathCalc<TReal> PropMultiply(string propertyPath) => new(conditionBuilder, SemanticSelector.PropMultiply(propertyPath));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<TReal, TRight> PropDivide<TRight>(Expression<Func<T, TRight>> property) where TRight : INumber<TRight> => new(conditionBuilder, SemanticSelector.PropDivide(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyPathCalc<TReal> PropDivide(string propertyPath) => new(conditionBuilder, SemanticSelector.PropDivide(propertyPath));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<TReal, TRight> PropModulo<TRight>(Expression<Func<T, TRight>> property) where TRight : INumber<TRight> => new(conditionBuilder, SemanticSelector.PropModulo(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyPathCalc<TReal> PropModulo(string propertyPath) => new(conditionBuilder, SemanticSelector.PropModulo(propertyPath));
            /// <summary>
            /// Begins a condition-local discount whose rate comes from a typed same-record numeric property.<br/>
            /// The returned stage requires an explicit <c>ResultAs...</c> domain before comparison.<br/>
            /// </summary>
            /// <typeparam name="TRight">Declared rate-property type.<br/></typeparam>
            /// <param name="property">Typed same-record rate property.<br/></param>
            /// <returns>A cross-property result-domain completion stage.<br/></returns>
            public NumericPropertyCalc<TReal, TRight> PropDiscount<TRight>(Expression<Func<T, TRight>> property) where TRight : INumber<TRight> => new(conditionBuilder, SemanticSelector.PropDiscount(property));
            /// <summary>
            /// Begins a condition-local discount whose rate comes from a textual property path.<br/>
            /// The developer next selects the authored rate type and checked result domain.<br/>
            /// </summary>
            /// <param name="propertyPath">Abraxas path of the same-record rate property.<br/></param>
            /// <returns>A rate-property type completion stage.<br/></returns>
            public NumericPropertyPathCalc<TReal> PropDiscount(string propertyPath) => new(conditionBuilder, SemanticSelector.PropDiscount(propertyPath));
        }

        /// <summary>Continues a condition-local Decimal calculation and inherits Decimal comparisons.<br/></summary>
        public sealed class DecimalCalc : NumericOperator<decimal>
        {
            private readonly Builder conditionBuilder;
            internal Selector<T>.DecimalSelector SemanticSelector { get; }
            internal DecimalCalc(Builder builder, Selector<T>.DecimalSelector selector) : base(builder, Clause.WrapSelector(selector)) { conditionBuilder = builder; SemanticSelector = selector; }
            private DecimalCalc Next(Selector<T>.DecimalSelector selector) => new(conditionBuilder, selector);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DecimalCalc Add(decimal value) => Next(SemanticSelector.Add(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DecimalCalc Add(Selector<T>.DecimalSelector selector) => Next(SemanticSelector.Add(selector));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DecimalCalc Subtract(decimal value) => Next(SemanticSelector.Subtract(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DecimalCalc Subtract(Selector<T>.DecimalSelector selector) => Next(SemanticSelector.Subtract(selector));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DecimalCalc Multiply(decimal value) => Next(SemanticSelector.Multiply(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DecimalCalc Multiply(Selector<T>.DecimalSelector selector) => Next(SemanticSelector.Multiply(selector));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DecimalCalc Divide(decimal value) => Next(SemanticSelector.Divide(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DecimalCalc Divide(Selector<T>.DecimalSelector selector) => Next(SemanticSelector.Divide(selector));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DecimalCalc Modulo(decimal value) => Next(SemanticSelector.Modulo(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DecimalCalc Modulo(Selector<T>.DecimalSelector selector) => Next(SemanticSelector.Modulo(selector));
            /// <summary>
            /// Adds semantic intent to reduce the current decimal value by a literal rate.<br/>
            /// Execution later evaluates <c>value * (1 - rate)</c> with checked decimal arithmetic; construction performs no calculation.<br/>
            /// </summary>
            /// <param name="rate">Decimal discount rate, where <c>0.15m</c> represents fifteen percent.<br/></param>
            /// <returns>The condition-local decimal calculation continuation.<br/></returns>
            public DecimalCalc Discount(decimal rate) => Next(SemanticSelector.Discount(rate));
            /// <summary>
            /// Adds semantic intent to reduce the current decimal value by a reusable decimal selector.<br/>
            /// Both operands remain immutable and deferred until condition execution.<br/>
            /// </summary>
            /// <param name="rate">Selector supplying the per-record discount rate.<br/></param>
            /// <returns>The condition-local decimal calculation continuation.<br/></returns>
            public DecimalCalc Discount(Selector<T>.DecimalSelector rate) => Next(SemanticSelector.Discount(rate));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DecimalCalc Negate() => Next(SemanticSelector.Negate());
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DecimalCalc Abs() => Next(SemanticSelector.Abs());
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DecimalCalc Floor() => Next(SemanticSelector.Floor());
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DecimalCalc Ceiling() => Next(SemanticSelector.Ceiling());
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DecimalCalc Round(int scale) => Next(SemanticSelector.Round(scale));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DecimalCalc Truncate(int scale) => Next(SemanticSelector.Truncate(scale));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<long> Sign() => new(conditionBuilder, (Selector<T>.IntegralSelector<long>)SemanticSelector.Sign());
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DecimalCalc PowInt(int exponent) => Next(SemanticSelector.PowInt(exponent));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<long> AsLong => new(conditionBuilder, SemanticSelector.AsLong);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<decimal, TRight> PropAdd<TRight>(Expression<Func<T, TRight>> property) where TRight : INumber<TRight> => new(conditionBuilder, SemanticSelector.PropAdd(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyPathCalc<decimal> PropAdd(string propertyPath) => new(conditionBuilder, SemanticSelector.PropAdd(propertyPath));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<decimal, TRight> PropSubtract<TRight>(Expression<Func<T, TRight>> property) where TRight : INumber<TRight> => new(conditionBuilder, SemanticSelector.PropSubtract(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyPathCalc<decimal> PropSubtract(string propertyPath) => new(conditionBuilder, SemanticSelector.PropSubtract(propertyPath));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<decimal, TRight> PropMultiply<TRight>(Expression<Func<T, TRight>> property) where TRight : INumber<TRight> => new(conditionBuilder, SemanticSelector.PropMultiply(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyPathCalc<decimal> PropMultiply(string propertyPath) => new(conditionBuilder, SemanticSelector.PropMultiply(propertyPath));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<decimal, TRight> PropDivide<TRight>(Expression<Func<T, TRight>> property) where TRight : INumber<TRight> => new(conditionBuilder, SemanticSelector.PropDivide(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyPathCalc<decimal> PropDivide(string propertyPath) => new(conditionBuilder, SemanticSelector.PropDivide(propertyPath));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<decimal, TRight> PropModulo<TRight>(Expression<Func<T, TRight>> property) where TRight : INumber<TRight> => new(conditionBuilder, SemanticSelector.PropModulo(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyPathCalc<decimal> PropModulo(string propertyPath) => new(conditionBuilder, SemanticSelector.PropModulo(propertyPath));
            /// <summary>
            /// Begins a condition-local decimal discount whose rate comes from a typed same-record numeric property.<br/>
            /// Cross-type conversion remains deferred and requires an explicit <c>ResultAs...</c> execution domain.<br/>
            /// </summary>
            /// <typeparam name="TRight">Declared rate-property type.<br/></typeparam>
            /// <param name="property">Typed same-record rate property.<br/></param>
            /// <returns>A cross-property result-domain completion stage.<br/></returns>
            public NumericPropertyCalc<decimal, TRight> PropDiscount<TRight>(Expression<Func<T, TRight>> property) where TRight : INumber<TRight> => new(conditionBuilder, SemanticSelector.PropDiscount(property));
            /// <summary>
            /// Begins a condition-local decimal discount whose rate comes from a textual property path.<br/>
            /// The developer next selects the authored rate type and checked result domain.<br/>
            /// </summary>
            /// <param name="propertyPath">Abraxas path of the same-record rate property.<br/></param>
            /// <returns>A rate-property type completion stage.<br/></returns>
            public NumericPropertyPathCalc<decimal> PropDiscount(string propertyPath) => new(conditionBuilder, SemanticSelector.PropDiscount(propertyPath));
        }

        /// <summary>Completes the explicit result domain for typed cross-property numeric arithmetic inside a condition.<br/></summary>
        public sealed class NumericPropertyCalc<TLeft, TRight> where TLeft : INumber<TLeft> where TRight : INumber<TRight>
        {
            private readonly Builder conditionBuilder;
            private readonly Selector<T>.NumericPropertyArithmeticSelector<TLeft, TRight> selector;
            internal NumericPropertyCalc(Builder builder, Selector<T>.NumericPropertyArithmeticSelector<TLeft, TRight> selector) { conditionBuilder = builder; this.selector = selector; }
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<byte> ResultAsByte => new(conditionBuilder, selector.ResultAsByte);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<sbyte> ResultAsSByte => new(conditionBuilder, selector.ResultAsSByte);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<short> ResultAsInt16 => new(conditionBuilder, selector.ResultAsInt16);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<ushort> ResultAsUInt16 => new(conditionBuilder, selector.ResultAsUInt16);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<int> ResultAsInt32 => new(conditionBuilder, selector.ResultAsInt32);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<uint> ResultAsUInt32 => new(conditionBuilder, selector.ResultAsUInt32);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<long> ResultAsInt64 => new(conditionBuilder, selector.ResultAsInt64);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<ulong> ResultAsUInt64 => new(conditionBuilder, selector.ResultAsUInt64);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<Int128> ResultAsInt128 => new(conditionBuilder, selector.ResultAsInt128);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<UInt128> ResultAsUInt128 => new(conditionBuilder, selector.ResultAsUInt128);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<BigInteger> ResultAsBigInteger => new(conditionBuilder, selector.ResultAsBigInteger);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<Half> ResultAsHalf => new(conditionBuilder, selector.ResultAsHalf);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<float> ResultAsSingle => new(conditionBuilder, selector.ResultAsSingle);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<double> ResultAsDouble => new(conditionBuilder, selector.ResultAsDouble);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DecimalCalc ResultAsDecimal => new(conditionBuilder, selector.ResultAsDecimal);
        }

        /// <summary>Completes the right property type for textual cross-property numeric arithmetic inside a condition.<br/></summary>
        public sealed class NumericPropertyPathCalc<TLeft> where TLeft : INumber<TLeft>
        {
            private readonly Builder conditionBuilder;
            private readonly Selector<T>.NumericPropertyPathArithmeticSelector<TLeft> selector;
            internal NumericPropertyPathCalc(Builder builder, Selector<T>.NumericPropertyPathArithmeticSelector<TLeft> selector) { conditionBuilder = builder; this.selector = selector; }
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<TLeft, byte> AsByte => new(conditionBuilder, selector.AsByte);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<TLeft, sbyte> AsSByte => new(conditionBuilder, selector.AsSByte);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<TLeft, short> AsInt16 => new(conditionBuilder, selector.AsInt16);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<TLeft, ushort> AsUInt16 => new(conditionBuilder, selector.AsUInt16);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<TLeft, int> AsInt32 => new(conditionBuilder, selector.AsInt32);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<TLeft, uint> AsUInt32 => new(conditionBuilder, selector.AsUInt32);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<TLeft, long> AsInt64 => new(conditionBuilder, selector.AsInt64);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<TLeft, ulong> AsUInt64 => new(conditionBuilder, selector.AsUInt64);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<TLeft, Int128> AsInt128 => new(conditionBuilder, selector.AsInt128);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<TLeft, UInt128> AsUInt128 => new(conditionBuilder, selector.AsUInt128);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<TLeft, BigInteger> AsBigInteger => new(conditionBuilder, selector.AsBigInteger);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<TLeft, Half> AsHalf => new(conditionBuilder, selector.AsHalf);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<TLeft, float> AsSingle => new(conditionBuilder, selector.AsSingle);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<TLeft, double> AsDouble => new(conditionBuilder, selector.AsDouble);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public NumericPropertyCalc<TLeft, decimal> AsDecimal => new(conditionBuilder, selector.AsDecimal);
        }

        /// <summary>Continues a condition-local DateTime calculation and inherits DateTime comparisons.<br/></summary>
        public sealed class DateTimeCalc : DateTimeOperator
        {
            private readonly Builder conditionBuilder;
            internal Selector<T>.DateTimeSelector SemanticSelector { get; }
            internal DateTimeCalc(Builder builder, Selector<T>.DateTimeSelector selector) : base(builder, Clause.WrapSelector(selector)) { conditionBuilder = builder; SemanticSelector = selector; }
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<long> Year => new(conditionBuilder, SemanticSelector.Year);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<long> Month => new(conditionBuilder, SemanticSelector.Month);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<long> Day => new(conditionBuilder, SemanticSelector.Day);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<long> Hour => new(conditionBuilder, SemanticSelector.Hour);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<long> Minute => new(conditionBuilder, SemanticSelector.Minute);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<long> Second => new(conditionBuilder, SemanticSelector.Second);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<long> Millisecond => new(conditionBuilder, SemanticSelector.Millisecond);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<long> NanoSlice => new(conditionBuilder, SemanticSelector.NanoSlice);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<long> DayOfWeek => new(conditionBuilder, SemanticSelector.DayOfWeek);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DateTimeCalc Add(TimeSpan value) => new(conditionBuilder, SemanticSelector.Add(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DateTimeCalc Add(Selector<T>.TimeSpanSelector selector) => new(conditionBuilder, SemanticSelector.Add(selector));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DateTimeCalc Subtract(TimeSpan value) => new(conditionBuilder, SemanticSelector.Subtract(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DateTimeCalc Subtract(Selector<T>.TimeSpanSelector selector) => new(conditionBuilder, SemanticSelector.Subtract(selector));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeSpanCalc Subtract(Selector<T>.DateTimeSelector selector) => new(conditionBuilder, SemanticSelector.Subtract(selector));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DateTimeCalc PropAdd(Expression<Func<T, TimeSpan>> property) => new(conditionBuilder, SemanticSelector.PropAdd(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DateTimeDurationPropertyCalc PropAdd(string propertyPath) => new(conditionBuilder, SemanticSelector.PropAdd(propertyPath));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeSpanCalc PropSubtract(Expression<Func<T, DateTime>> property) => new(conditionBuilder, SemanticSelector.PropSubtract(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DateTimeCalc PropSubtract(Expression<Func<T, TimeSpan>> property) => new(conditionBuilder, SemanticSelector.PropSubtract(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public DateTimePropertyCalc PropSubtract(string propertyPath) => new(conditionBuilder, SemanticSelector.PropSubtract(propertyPath));
        }

        /// <summary>Continues a condition-local DateOnly calculation and inherits DateOnly comparisons.<br/></summary>
        public sealed class DateOnlyCalc : DateOnlyOperator
        {
            private readonly Builder conditionBuilder;
            internal Selector<T>.DateOnlySelector SemanticSelector { get; }
            internal DateOnlyCalc(Builder builder, Selector<T>.DateOnlySelector selector) : base(builder, Clause.WrapSelector(selector)) { conditionBuilder = builder; SemanticSelector = selector; }
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<long> Year => new(conditionBuilder, SemanticSelector.Year);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<long> Month => new(conditionBuilder, SemanticSelector.Month);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<long> Day => new(conditionBuilder, SemanticSelector.Day);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<long> DayOfWeek => new(conditionBuilder, SemanticSelector.DayOfWeek);
        }

        /// <summary>Continues a condition-local TimeOnly calculation and inherits TimeOnly comparisons.<br/></summary>
        public sealed class TimeOnlyCalc : TimeOnlyOperator
        {
            private readonly Builder conditionBuilder;
            internal Selector<T>.TimeOnlySelector SemanticSelector { get; }
            internal TimeOnlyCalc(Builder builder, Selector<T>.TimeOnlySelector selector) : base(builder, Clause.WrapSelector(selector)) { conditionBuilder = builder; SemanticSelector = selector; }
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<long> Hour => new(conditionBuilder, SemanticSelector.Hour);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<long> Minute => new(conditionBuilder, SemanticSelector.Minute);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<long> Second => new(conditionBuilder, SemanticSelector.Second);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<long> Millisecond => new(conditionBuilder, SemanticSelector.Millisecond);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<long> NanoSlice => new(conditionBuilder, SemanticSelector.NanoSlice);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeOnlyCalc Add(TimeSpan value) => new(conditionBuilder, SemanticSelector.Add(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeOnlyCalc Add(Selector<T>.TimeSpanSelector selector) => new(conditionBuilder, SemanticSelector.Add(selector));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeOnlyCalc Subtract(TimeSpan value) => new(conditionBuilder, SemanticSelector.Subtract(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeOnlyCalc Subtract(Selector<T>.TimeSpanSelector selector) => new(conditionBuilder, SemanticSelector.Subtract(selector));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeSpanCalc Subtract(Selector<T>.TimeOnlySelector selector) => new(conditionBuilder, SemanticSelector.Subtract(selector));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeOnlyCalc PropAdd(Expression<Func<T, TimeSpan>> property) => new(conditionBuilder, SemanticSelector.PropAdd(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeOnlyDurationPropertyCalc PropAdd(string propertyPath) => new(conditionBuilder, SemanticSelector.PropAdd(propertyPath));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeSpanCalc PropSubtract(Expression<Func<T, TimeOnly>> property) => new(conditionBuilder, SemanticSelector.PropSubtract(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeOnlyCalc PropSubtract(Expression<Func<T, TimeSpan>> property) => new(conditionBuilder, SemanticSelector.PropSubtract(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeOnlyPropertyCalc PropSubtract(string propertyPath) => new(conditionBuilder, SemanticSelector.PropSubtract(propertyPath));
        }

        /// <summary>Continues a condition-local TimeSpan calculation and inherits TimeSpan comparisons.<br/></summary>
        public sealed class TimeSpanCalc : TimeSpanOperator
        {
            private readonly Builder conditionBuilder;
            internal Selector<T>.TimeSpanSelector SemanticSelector { get; }
            internal TimeSpanCalc(Builder builder, Selector<T>.TimeSpanSelector selector) : base(builder, Clause.WrapSelector(selector)) { conditionBuilder = builder; SemanticSelector = selector; }
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeSpanCalc Add(TimeSpan value) => new(conditionBuilder, SemanticSelector.Add(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeSpanCalc Add(Selector<T>.TimeSpanSelector selector) => new(conditionBuilder, SemanticSelector.Add(selector));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeSpanCalc Subtract(TimeSpan value) => new(conditionBuilder, SemanticSelector.Subtract(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeSpanCalc Subtract(Selector<T>.TimeSpanSelector selector) => new(conditionBuilder, SemanticSelector.Subtract(selector));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeSpanCalc Multiply(double value) => new(conditionBuilder, SemanticSelector.Multiply(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeSpanCalc Multiply(Selector<T>.RealSelector<double> selector) => new(conditionBuilder, SemanticSelector.Multiply(selector));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeSpanCalc Divide(double value) => new(conditionBuilder, SemanticSelector.Divide(value));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeSpanCalc Divide(Selector<T>.RealSelector<double> selector) => new(conditionBuilder, SemanticSelector.Divide(selector));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<double> Divide(Selector<T>.TimeSpanSelector selector) => new(conditionBuilder, SemanticSelector.Divide(selector));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeSpanCalc PropAdd(Expression<Func<T, TimeSpan>> property) => new(conditionBuilder, SemanticSelector.PropAdd(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeSpanPropertyCalc PropAdd(string propertyPath) => new(conditionBuilder, SemanticSelector.PropAdd(propertyPath));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeSpanCalc PropSubtract(Expression<Func<T, TimeSpan>> property) => new(conditionBuilder, SemanticSelector.PropSubtract(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeSpanPropertyCalc PropSubtract(string propertyPath) => new(conditionBuilder, SemanticSelector.PropSubtract(propertyPath));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeSpanCalc PropMultiply(Expression<Func<T, double>> property) => new(conditionBuilder, SemanticSelector.PropMultiply(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeSpanScalarPropertyCalc PropMultiply(string propertyPath) => new(conditionBuilder, SemanticSelector.PropMultiply(propertyPath));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeSpanCalc PropDivide(Expression<Func<T, double>> property) => new(conditionBuilder, SemanticSelector.PropDivide(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<double> PropDivide(Expression<Func<T, TimeSpan>> property) => new(conditionBuilder, SemanticSelector.PropDivide(property));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public TimeSpanDividePropertyCalc PropDivide(string propertyPath) => new(conditionBuilder, SemanticSelector.PropDivide(propertyPath));
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public IntegralCalc<long> Ticks => new(conditionBuilder, SemanticSelector.Ticks);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public RealCalc<double> TotalDays => new(conditionBuilder, SemanticSelector.TotalDays);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public new RealCalc<double> TotalHours => new(conditionBuilder, SemanticSelector.TotalHours);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public new RealCalc<double> TotalMinutes => new(conditionBuilder, SemanticSelector.TotalMinutes);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public new RealCalc<double> TotalSeconds => new(conditionBuilder, SemanticSelector.TotalSeconds);
            /// <summary>
            /// Continues this condition-local calculation through the requested typed transformation while preserving its immutable selector descriptor.<br/>
            /// Value conversion and evaluation remain deferred until condition execution.<br/>
            /// </summary>
            public new RealCalc<double> TotalMilliseconds => new(conditionBuilder, SemanticSelector.TotalMilliseconds);
        }

        /// <summary>Completes the textual right type for DateTime plus TimeSpan arithmetic.<br/></summary>
        public sealed class DateTimeDurationPropertyCalc
        {
            private readonly Builder conditionBuilder;
            private readonly Selector<T>.DateTimeDurationPropertyArithmeticSelector selector;
            internal DateTimeDurationPropertyCalc(Builder builder, Selector<T>.DateTimeDurationPropertyArithmeticSelector selector) { conditionBuilder = builder; this.selector = selector; }
            /// <summary>Declares the textual right property as TimeSpan and continues with a DateTime condition value.<br/></summary>
            public DateTimeCalc AsTimeSpan => new(conditionBuilder, selector.AsTimeSpan);
        }

        /// <summary>Completes the textual right type for DateTime subtraction.<br/></summary>
        public sealed class DateTimePropertyCalc
        {
            private readonly Builder conditionBuilder;
            private readonly Selector<T>.DateTimePropertyArithmeticSelector selector;
            internal DateTimePropertyCalc(Builder builder, Selector<T>.DateTimePropertyArithmeticSelector selector) { conditionBuilder = builder; this.selector = selector; }
            /// <summary>Declares a DateTime right property and continues with the resulting TimeSpan condition value.<br/></summary>
            public TimeSpanCalc AsDateTime => new(conditionBuilder, selector.AsDateTime);
            /// <summary>Declares a TimeSpan right property and continues with the resulting DateTime condition value.<br/></summary>
            public DateTimeCalc AsTimeSpan => new(conditionBuilder, selector.AsTimeSpan);
        }

        /// <summary>Completes the textual right type for TimeOnly plus TimeSpan arithmetic.<br/></summary>
        public sealed class TimeOnlyDurationPropertyCalc
        {
            private readonly Builder conditionBuilder;
            private readonly Selector<T>.TimeOnlyDurationPropertyArithmeticSelector selector;
            internal TimeOnlyDurationPropertyCalc(Builder builder, Selector<T>.TimeOnlyDurationPropertyArithmeticSelector selector) { conditionBuilder = builder; this.selector = selector; }
            /// <summary>Declares the textual right property as TimeSpan and continues with a TimeOnly condition value.<br/></summary>
            public TimeOnlyCalc AsTimeSpan => new(conditionBuilder, selector.AsTimeSpan);
        }

        /// <summary>Completes the textual right type for TimeOnly subtraction.<br/></summary>
        public sealed class TimeOnlyPropertyCalc
        {
            private readonly Builder conditionBuilder;
            private readonly Selector<T>.TimeOnlyPropertyArithmeticSelector selector;
            internal TimeOnlyPropertyCalc(Builder builder, Selector<T>.TimeOnlyPropertyArithmeticSelector selector) { conditionBuilder = builder; this.selector = selector; }
            /// <summary>Declares a TimeOnly right property and continues with the resulting TimeSpan condition value.<br/></summary>
            public TimeSpanCalc AsTimeOnly => new(conditionBuilder, selector.AsTimeOnly);
            /// <summary>Declares a TimeSpan right property and continues with the resulting TimeOnly condition value.<br/></summary>
            public TimeOnlyCalc AsTimeSpan => new(conditionBuilder, selector.AsTimeSpan);
        }

        /// <summary>Completes a textual TimeSpan right property for duration arithmetic.<br/></summary>
        public sealed class TimeSpanPropertyCalc
        {
            private readonly Builder conditionBuilder;
            private readonly Selector<T>.TimeSpanPropertyArithmeticSelector selector;
            internal TimeSpanPropertyCalc(Builder builder, Selector<T>.TimeSpanPropertyArithmeticSelector selector) { conditionBuilder = builder; this.selector = selector; }
            /// <summary>Declares the textual right property as TimeSpan and continues with a TimeSpan condition value.<br/></summary>
            public TimeSpanCalc AsTimeSpan => new(conditionBuilder, selector.AsTimeSpan);
        }

        /// <summary>Completes a textual Double right property for TimeSpan scalar arithmetic.<br/></summary>
        public sealed class TimeSpanScalarPropertyCalc
        {
            private readonly Builder conditionBuilder;
            private readonly Selector<T>.TimeSpanScalarPropertyArithmeticSelector selector;
            internal TimeSpanScalarPropertyCalc(Builder builder, Selector<T>.TimeSpanScalarPropertyArithmeticSelector selector) { conditionBuilder = builder; this.selector = selector; }
            /// <summary>Declares the textual right property as Double and continues with a TimeSpan condition value.<br/></summary>
            public TimeSpanCalc AsDouble => new(conditionBuilder, selector.AsDouble);
        }

        /// <summary>Completes a textual scalar-or-duration divisor for TimeSpan arithmetic.<br/></summary>
        public sealed class TimeSpanDividePropertyCalc
        {
            private readonly Builder conditionBuilder;
            private readonly Selector<T>.TimeSpanDividePropertyArithmeticSelector selector;
            internal TimeSpanDividePropertyCalc(Builder builder, Selector<T>.TimeSpanDividePropertyArithmeticSelector selector) { conditionBuilder = builder; this.selector = selector; }
            /// <summary>Declares a Double divisor and continues with a TimeSpan condition value.<br/></summary>
            public TimeSpanCalc AsDouble => new(conditionBuilder, selector.AsDouble);
            /// <summary>Declares a TimeSpan divisor and continues with the resulting Double ratio condition value.<br/></summary>
            public RealCalc<double> AsTimeSpan => new(conditionBuilder, selector.AsTimeSpan);
        }
    }
}
