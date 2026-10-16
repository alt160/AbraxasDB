using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AbraxasDB
{

    public partial class Selector<T>
    {
        /// <summary>
        /// A composable String value selector for record-store operations.<br/>
        /// </summary>
        /// <remarks>
        /// Construction records expression intent; it does not execute a query or read record values.<br/>
        /// </remarks>
        public class StringSelector : IValue<string>,ISelectorStructure
        {
            private readonly Builder parentBuilder;
            private readonly ValueOrExpression<string> selectorExpression;
            private readonly bool isLiteral;
            private readonly IBackendNeutralSelectorDescriptor? semanticDescriptor;

            string? ISelectorStructure.SimplePropertyPath => parentBuilder.SimplePropertyPath;
            string? ISelectorStructure.TypedSourcePropertyPath => parentBuilder.TypedSourcePropertyPath;
            Type? ISelectorStructure.ResultType => typeof(string);
            IBackendNeutralSelectorDescriptor? ISelectorStructure.SemanticDescriptor => semanticDescriptor;

            internal StringSelector(Builder parentBuilder, ValueOrExpression<string> selectorExpression, bool addBase = true, bool isLiteral = false)
                : this(parentBuilder, selectorExpression, addBase, isLiteral, semanticDescriptor: null)
            {
            }

            /// <summary>
            /// The internal overload prevents descriptor implementation types from leaking through the public selector surface.<br/>
            /// </summary>
            /// <param name="parentBuilder">Selector builder retaining compatibility composition parts.<br/></param>
            /// <param name="selectorExpression">Base selector expression used by the retained renderer.<br/></param>
            /// <param name="addBase">Whether this instance appends its base selector part.<br/></param>
            /// <param name="isLiteral">Whether the base expression is a literal rather than a property selector.<br/></param>
            /// <param name="semanticDescriptor">Optional immutable direct-or-computed selector descriptor.<br/></param>
            internal StringSelector(
                Builder parentBuilder,
                ValueOrExpression<string> selectorExpression,
                bool addBase,
                bool isLiteral,
                IBackendNeutralSelectorDescriptor? semanticDescriptor)
            {
                this.parentBuilder = addBase || semanticDescriptor is not null
                    ? parentBuilder.AsComputed()
                    : parentBuilder;
                this.selectorExpression = selectorExpression;
                this.isLiteral = isLiteral;
                this.semanticDescriptor = semanticDescriptor;
                if (addBase) parentBuilder.MarkComputed();
            }

            private IBackendNeutralSelectorDescriptor? GetSemanticValueDescriptor()
            {
                if (semanticDescriptor is not null)
                    return semanticDescriptor;

                var propertyPath = ((ISelectorStructure)this).TypedSourcePropertyPath;
                return string.IsNullOrWhiteSpace(propertyPath)
                    ? null
                    : new DirectPropertySelectorDescriptor(propertyPath);
            }

            private StringSelector ApplyStringOp(
                StringOperationSelectorDescriptor? descriptor)
            {
                parentBuilder.MarkComputed();
                return new StringSelector(parentBuilder, selectorExpression, addBase: false, isLiteral, descriptor);
            }

            private IntegralSelector<long> ApplyLongOp(
                StringOperationSelectorDescriptor? descriptor)
            {
                parentBuilder.MarkComputed();
                return new IntegralSelector<long>(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            private BinarySelector ApplyBinaryOp(
                StringOperationSelectorDescriptor? descriptor)
            {
                parentBuilder.MarkComputed();
                return new BinarySelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            private StringOperationSelectorDescriptor? Describe(
                StringOperationKind operation,
                string? textOperand = null,
                string? secondTextOperand = null,
                long? integerOperand = null,
                long? secondIntegerOperand = null,
                char? characterOperand = null,
                bool ignoreCase = false,
                string? cultureName = null,
                Func<string, string?>? external = null)
            {
                var source = GetSemanticValueDescriptor();
                return source is null
                    ? null
                    : new StringOperationSelectorDescriptor(
                        source,
                        operation,
                        textOperand,
                        secondTextOperand,
                        integerOperand,
                        secondIntegerOperand,
                        characterOperand,
                        ignoreCase,
                        cultureName,
                        external);
            }

            /// <summary>
            /// Selects the whole match or one numbered capture produced by a caller-provided regular expression.<br/>
            /// The pattern, semantic options, timeout, group, and occurrence become durable selector intent, while compiled execution is recreated automatically after reopen.<br/>
            /// </summary>
            /// <param name="pattern">Regular expression applied to the selected string.<br/></param>
            /// <param name="groupNumber">Zero for the whole match or a declared positive capture-group number.<br/></param>
            /// <param name="occurrenceNumber">Zero-based non-overlapping match occurrence; omitted means the first match.<br/></param>
            /// <returns>A string selector containing the selected match value, or a missing value when the match, occurrence, or capture does not exist.<br/></returns>
            public StringSelector MatchValue(Regex pattern, int groupNumber = 0, int? occurrenceNumber = null)
            {
                ArgumentNullException.ThrowIfNull(pattern);
                ValidateMatchValueSelection(pattern, groupNumber, occurrenceNumber);
                return ApplyStringOp(DescribeMatchValue(pattern, groupNumber, occurrenceNumber));
            }

            /// <summary>
            /// Selects one named capture produced by a caller-provided regular expression.<br/>
            /// The capture name is validated immediately and stored as the pattern's stable numeric group identity so condition and index planning share one canonical selector fingerprint.<br/>
            /// </summary>
            /// <param name="pattern">Regular expression applied to the selected string.<br/></param>
            /// <param name="groupName">Non-empty capture-group name declared by <paramref name="pattern"/>.<br/></param>
            /// <param name="occurrenceNumber">Zero-based non-overlapping match occurrence; omitted means the first match.<br/></param>
            /// <returns>A string selector containing the selected capture value.<br/></returns>
            public StringSelector MatchValue(Regex pattern, string groupName, int? occurrenceNumber = null)
            {
                ArgumentNullException.ThrowIfNull(pattern);
                ArgumentException.ThrowIfNullOrWhiteSpace(groupName);
                int groupNumber = pattern.GroupNumberFromName(groupName);
                if (groupNumber < 0)
                    throw new ArgumentException($"Capture group '{groupName}' is not declared by the supplied pattern.", nameof(groupName));
                return MatchValue(pattern, groupNumber, occurrenceNumber);
            }

            /// <summary>
            /// Compiles one pattern once and selects its whole match or a numbered capture as a durable string transformation.<br/>
            /// Culture-invariant matching is the default; application-supplied semantic options remain part of selector identity while the compiled flag does not.<br/>
            /// </summary>
            /// <param name="pattern">Regular-expression pattern text.<br/></param>
            /// <param name="groupNumber">Zero for the whole match or a declared positive capture-group number.<br/></param>
            /// <param name="occurrenceNumber">Zero-based non-overlapping match occurrence; omitted means the first match.<br/></param>
            /// <param name="options">Semantic regular-expression options.<br/></param>
            /// <returns>A durable string selector containing the selected match value.<br/></returns>
            public StringSelector MatchValue(
                string pattern,
                int groupNumber = 0,
                int? occurrenceNumber = null,
                RegexOptions options = RegexOptions.CultureInvariant)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
                return MatchValue(StringOperationSelectorDescriptor.CreateRuntimeRegex(pattern, options, Regex.InfiniteMatchTimeout), groupNumber, occurrenceNumber);
            }

            /// <summary>
            /// Compiles one pattern once and selects a named capture as a durable string transformation.<br/>
            /// The developer-facing name is resolved immediately to the numeric group identity used by evaluation, persistence, and calculated-index matching.<br/>
            /// </summary>
            /// <param name="pattern">Regular-expression pattern text.<br/></param>
            /// <param name="groupName">Non-empty capture-group name declared by the pattern.<br/></param>
            /// <param name="occurrenceNumber">Zero-based non-overlapping match occurrence; omitted means the first match.<br/></param>
            /// <param name="options">Semantic regular-expression options.<br/></param>
            /// <returns>A durable string selector containing the selected named capture.<br/></returns>
            public StringSelector MatchValue(
                string pattern,
                string groupName,
                int? occurrenceNumber = null,
                RegexOptions options = RegexOptions.CultureInvariant)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
                Regex regex = StringOperationSelectorDescriptor.CreateRuntimeRegex(pattern, options, Regex.InfiniteMatchTimeout);
                return MatchValue(regex, groupName, occurrenceNumber);
            }

            private StringOperationSelectorDescriptor? DescribeMatchValue(
                Regex pattern,
                int groupNumber,
                int? occurrenceNumber)
            {
                IBackendNeutralSelectorDescriptor? source = GetSemanticValueDescriptor();
                return source is null
                    ? null
                    : StringOperationSelectorDescriptor.CreateMatchValue(
                        source,
                        pattern,
                        groupNumber,
                        occurrenceNumber ?? 0);
            }

            private static void ValidateMatchValueSelection(
                Regex pattern,
                int groupNumber,
                int? occurrenceNumber)
            {
                if (groupNumber < 0 || !pattern.GetGroupNumbers().Contains(groupNumber))
                    throw new ArgumentOutOfRangeException(nameof(groupNumber), groupNumber, "The capture group must be declared by the supplied pattern.");
                if (occurrenceNumber is < 0)
                    throw new ArgumentOutOfRangeException(nameof(occurrenceNumber), occurrenceNumber, "The match occurrence must be zero or greater.");
            }

            /// <summary>
            /// Gets the number of UTF-16 code units in the selected string, matching <see cref="string.Length"/>.<br/>
            /// Use <see cref="RuneCount"/> when the intended unit is a Unicode scalar value.<br/>
            /// </summary>
            public IntegralSelector<long> Length
                => ApplyLongOp(Describe(StringOperationKind.Length));

            /// <summary>
            /// Gets the number of Unicode scalar values in the selected string without allocating a rune array.<br/>
            /// Invalid UTF-16 code units follow the framework rune enumerator and count as replacement scalars.<br/>
            /// </summary>
            public IntegralSelector<long> RuneCount
                => ApplyLongOp(Describe(StringOperationKind.RuneCount));

            /// <summary>
            /// Selects a substring addressed by zero-based UTF-16 code-unit positions.<br/>
            /// </summary>
            /// <param name="start">Non-negative zero-based UTF-16 code-unit offset.<br/></param>
            /// <param name="length">Non-negative UTF-16 code-unit count.<br/></param>
            /// <returns>A selector representing the string operation; no record is read while authoring.<br/></returns>
            /// <remarks>
            /// These positions are UTF-16 code units, not Unicode scalar positions. Bounds are checked against each source string during evaluation.<br/>
            /// </remarks>
            public StringSelector Substring(long start, long length)
            {
                if (start < 0) throw new ArgumentOutOfRangeException(nameof(start), "start must be non-negative");
                if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "length must be non-negative");
                return ApplyStringOp(Describe(StringOperationKind.Substring, integerOperand: start, secondIntegerOperand: length));
            }

            /// <summary>
            /// Selects a substring addressed by zero-based UTF-16 code-unit positions.<br/>
            /// </summary>
            /// <param name="start">Non-negative zero-based UTF-16 code-unit offset.<br/></param>
            /// <returns>A selector representing the string operation; no record is read while authoring.<br/></returns>
            /// <remarks>
            /// These positions are UTF-16 code units, not Unicode scalar positions. Bounds are checked against each source string during evaluation.<br/>
            /// </remarks>
            public StringSelector Substring(long start)
            {
                if (start < 0) throw new ArgumentOutOfRangeException(nameof(start), "start must be non-negative");
                return ApplyStringOp(Describe(StringOperationKind.Substring, integerOperand: start));
            }

            /// <summary>
            /// Selects up to the requested number of UTF-16 code units from the beginning of the string.<br/>
            /// </summary>
            /// <param name="length">Non-negative maximum UTF-16 code-unit count.<br/></param>
            /// <returns>A selector representing the string operation; no record is read while authoring.<br/></returns>
            public StringSelector Left(long length)
            {
                if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "length must be non-negative");
                return ApplyStringOp(Describe(StringOperationKind.Left, integerOperand: length));
            }

            /// <summary>
            /// Selects up to the requested number of UTF-16 code units from the end of the string.<br/>
            /// </summary>
            /// <param name="length">Non-negative maximum UTF-16 code-unit count.<br/></param>
            /// <returns>A selector representing the string operation; no record is read while authoring.<br/></returns>
            public StringSelector Right(long length)
            {
                if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "length must be non-negative");
                return ApplyStringOp(Describe(StringOperationKind.Right, integerOperand: length));
            }

            /// <summary>
            /// Returns a substring addressed by zero-based Unicode scalar positions.<br/>
            /// The source is walked directly and no intermediate rune collection is allocated.<br/>
            /// </summary>
            /// <param name="start">Zero-based rune offset.<br/></param>
            /// <param name="length">Number of runes to return.<br/></param>
            public StringSelector RuneSubstring(long start, long length)
            {
                if (start < 0) throw new ArgumentOutOfRangeException(nameof(start), "start must be non-negative");
                if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "length must be non-negative");
                return ApplyStringOp(Describe(StringOperationKind.RuneSubstring, integerOperand: start, secondIntegerOperand: length));
            }

            /// <summary>
            /// Returns all Unicode scalar values beginning at a zero-based rune offset.<br/>
            /// The source is walked directly and no intermediate rune collection is allocated.<br/>
            /// </summary>
            /// <param name="start">Zero-based rune offset.<br/></param>
            public StringSelector RuneSubstring(long start)
            {
                if (start < 0) throw new ArgumentOutOfRangeException(nameof(start), "start must be non-negative");
                return ApplyStringOp(Describe(StringOperationKind.RuneSubstring, integerOperand: start));
            }

            /// <summary>
            /// Returns up to the requested number of Unicode scalar values from the beginning of the string.<br/>
            /// </summary>
            /// <param name="length">Maximum rune count to return.<br/></param>
            public StringSelector RuneLeft(long length)
            {
                if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "length must be non-negative");
                return ApplyStringOp(Describe(StringOperationKind.RuneLeft, integerOperand: length));
            }

            /// <summary>
            /// Returns up to the requested number of Unicode scalar values from the end of the string.<br/>
            /// </summary>
            /// <param name="length">Maximum rune count to return.<br/></param>
            public StringSelector RuneRight(long length)
            {
                if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), "length must be non-negative");
                return ApplyStringOp(Describe(StringOperationKind.RuneRight, integerOperand: length));
            }

            /// <summary>
            /// Selects the first matching UTF-16 position, or -1 when no match is present.<br/>
            /// </summary>
            /// <param name="value">Search text evaluated and captured once while the selector is authored.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case; false preserves case-sensitive matching.<br/></param>
            /// <param name="culture">Culture name for linguistic comparison; empty uses ordinal comparison.<br/></param>
            /// <returns>A selector representing the string operation; no record is read while authoring.<br/></returns>
            public IntegralSelector<long> IndexOf(ValueOrExpression<string> value, bool ignoreCase = false, string culture = "")
            {
                ArgumentNullException.ThrowIfNull(value);
                var capturedValue = value.Invoke();
                if (!ignoreCase && string.IsNullOrEmpty(culture))
                {
                    return ApplyLongOp(Describe(StringOperationKind.IndexOf, textOperand: capturedValue));
                }
                return ApplyLongOp(Describe(StringOperationKind.IndexOf, textOperand: capturedValue, ignoreCase: ignoreCase, cultureName: culture));
            }

            /// <summary>
            /// Selects the last matching UTF-16 position, or -1 when no match is present.<br/>
            /// </summary>
            /// <param name="value">Search text evaluated and captured once while the selector is authored.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case; false preserves case-sensitive matching.<br/></param>
            /// <param name="culture">Culture name for linguistic comparison; empty uses ordinal comparison.<br/></param>
            /// <returns>A selector representing the string operation; no record is read while authoring.<br/></returns>
            public IntegralSelector<long> LastIndexOf(ValueOrExpression<string> value, bool ignoreCase = false, string culture = "")
            {
                ArgumentNullException.ThrowIfNull(value);
                var capturedValue = value.Invoke();
                return ApplyLongOp(Describe(StringOperationKind.LastIndexOf, textOperand: capturedValue, ignoreCase: ignoreCase, cultureName: culture));
            }

            /// <summary>
            /// Lowercases ASCII letters while leaving other characters unchanged.<br/>
            /// </summary>
            /// <returns>A selector representing the string operation; no record is read while authoring.<br/></returns>
            public StringSelector AsciiLower() => ApplyStringOp(Describe(StringOperationKind.AsciiLower));

            /// <summary>
            /// Uppercases ASCII letters while leaving other characters unchanged.<br/>
            /// </summary>
            /// <returns>A selector representing the string operation; no record is read while authoring.<br/></returns>
            public StringSelector AsciiUpper() => ApplyStringOp(Describe(StringOperationKind.AsciiUpper));

            /// <summary>
            /// Lowercases the selected text using the invariant culture.<br/>
            /// </summary>
            /// <returns>A selector representing the string operation; no record is read while authoring.<br/></returns>
            public StringSelector LowerInvariant()
            {
                return ApplyStringOp(Describe(StringOperationKind.Lower));
            }

            /// <summary>
            /// Uppercases the selected text using the invariant culture.<br/>
            /// </summary>
            /// <returns>A selector representing the string operation; no record is read while authoring.<br/></returns>
            public StringSelector UpperInvariant()
            {
                return ApplyStringOp(Describe(StringOperationKind.Upper));
            }

            /// <summary>
            /// Lowercases the selected text using the specified culture.<br/>
            /// </summary>
            /// <param name="culture">Culture name; empty uses the invariant culture.<br/></param>
            /// <returns>A selector representing the string operation; no record is read while authoring.<br/></returns>
            public StringSelector Lower(string culture)
            {
                return ApplyStringOp(Describe(StringOperationKind.Lower, cultureName: culture));
            }

            /// <summary>
            /// Uppercases the selected text using the specified culture.<br/>
            /// </summary>
            /// <param name="culture">Culture name; empty uses the invariant culture.<br/></param>
            /// <returns>A selector representing the string operation; no record is read while authoring.<br/></returns>
            public StringSelector Upper(string culture)
            {
                return ApplyStringOp(Describe(StringOperationKind.Upper, cultureName: culture));
            }

            /// <summary>
            /// Canonically normalizes the selected text to Unicode Form C while preserving case.<br/>
            /// Already-normalized strings are returned unchanged so the common path does not allocate a replacement string.<br/>
            /// </summary>
            public StringSelector Normalize()
                => ApplyStringOp(Describe(StringOperationKind.Normalize));

            /// <summary>
            /// Selects culture-dependent collation-key bytes for the selected text.<br/>
            /// </summary>
            /// <param name="culture">Culture name; empty uses the invariant culture.<br/></param>
            /// <param name="options">Collation options used to generate the sort-key bytes.<br/></param>
            /// <returns>A selector representing the string operation; no record is read while authoring.<br/></returns>
            public BinarySelector SortKey(string culture = "", System.Globalization.CompareOptions options = System.Globalization.CompareOptions.IgnoreCase)
            {
                return ApplyBinaryOp(Describe(StringOperationKind.SortKey, integerOperand: (long)options, cultureName: culture));
            }

            /// <summary>
            /// Trims matching characters from both ends of the selected text.<br/>
            /// </summary>
            /// <returns>A selector representing the string operation; no record is read while authoring.<br/></returns>
            /// <remarks>
            /// Uses ordinary .NET Unicode whitespace semantics.<br/>
            /// </remarks>
            public StringSelector Trim() => ApplyStringOp(Describe(StringOperationKind.Trim));

            /// <summary>
            /// Trims matching characters from both ends of the selected text.<br/>
            /// </summary>
            /// <param name="trimChars">Character set evaluated and captured once; null uses Unicode whitespace and an empty set removes nothing.<br/></param>
            /// <returns>A selector representing the string operation; no record is read while authoring.<br/></returns>
            public StringSelector Trim(ValueOrExpression<string> trimChars)
            {
                var captured = trimChars.Invoke();
                return ApplyStringOp(Describe(StringOperationKind.Trim, textOperand: captured));
            }

            /// <summary>
            /// Trims matching characters from the beginning of the selected text.<br/>
            /// </summary>
            /// <returns>A selector representing the string operation; no record is read while authoring.<br/></returns>
            /// <remarks>
            /// Uses ordinary .NET Unicode whitespace semantics.<br/>
            /// </remarks>
            public StringSelector TrimStart() => ApplyStringOp(Describe(StringOperationKind.TrimStart));

            /// <summary>
            /// Trims matching characters from the beginning of the selected text.<br/>
            /// </summary>
            /// <param name="trimChars">Character set evaluated and captured once; null uses Unicode whitespace and an empty set removes nothing.<br/></param>
            /// <returns>A selector representing the string operation; no record is read while authoring.<br/></returns>
            public StringSelector TrimStart(ValueOrExpression<string> trimChars)
            {
                var captured = trimChars.Invoke();
                return ApplyStringOp(Describe(StringOperationKind.TrimStart, textOperand: captured));
            }

            /// <summary>
            /// Trims matching characters from the end of the selected text.<br/>
            /// </summary>
            /// <returns>A selector representing the string operation; no record is read while authoring.<br/></returns>
            /// <remarks>
            /// Uses ordinary .NET Unicode whitespace semantics.<br/>
            /// </remarks>
            public StringSelector TrimEnd() => ApplyStringOp(Describe(StringOperationKind.TrimEnd));

            /// <summary>
            /// Trims matching characters from the end of the selected text.<br/>
            /// </summary>
            /// <param name="trimChars">Character set evaluated and captured once; null uses Unicode whitespace and an empty set removes nothing.<br/></param>
            /// <returns>A selector representing the string operation; no record is read while authoring.<br/></returns>
            public StringSelector TrimEnd(ValueOrExpression<string> trimChars)
            {
                var captured = trimChars.Invoke();
                return ApplyStringOp(Describe(StringOperationKind.TrimEnd, textOperand: captured));
            }

            /// <summary>
            /// Trims ASCII whitespace from both ends of the selected text.<br/>
            /// Use <see cref="Trim()"/> for ordinary .NET Unicode whitespace semantics.<br/>
            /// </summary>
            public StringSelector TrimAscii() => ApplyStringOp(Describe(StringOperationKind.TrimAscii));

            /// <summary>
            /// Trims the supplied ASCII characters from both ends of the selected text.<br/>
            /// </summary>
            /// <param name="trimChars">ASCII character set captured when the selector is authored.<br/></param>
            public StringSelector TrimAscii(ValueOrExpression<string> trimChars)
            {
                var captured = trimChars.Invoke();
                return ApplyStringOp(Describe(StringOperationKind.TrimAscii, textOperand: captured));
            }

            /// <summary>
            /// Trims ASCII whitespace from the beginning of the selected text.<br/>
            /// </summary>
            public StringSelector TrimStartAscii() => ApplyStringOp(Describe(StringOperationKind.TrimStartAscii));

            /// <summary>
            /// Trims the supplied ASCII characters from the beginning of the selected text.<br/>
            /// </summary>
            /// <param name="trimChars">ASCII character set captured when the selector is authored.<br/></param>
            public StringSelector TrimStartAscii(ValueOrExpression<string> trimChars)
            {
                var captured = trimChars.Invoke();
                return ApplyStringOp(Describe(StringOperationKind.TrimStartAscii, textOperand: captured));
            }

            /// <summary>
            /// Trims ASCII whitespace from the end of the selected text.<br/>
            /// </summary>
            public StringSelector TrimEndAscii() => ApplyStringOp(Describe(StringOperationKind.TrimEndAscii));

            /// <summary>
            /// Trims the supplied ASCII characters from the end of the selected text.<br/>
            /// </summary>
            /// <param name="trimChars">ASCII character set captured when the selector is authored.<br/></param>
            public StringSelector TrimEndAscii(ValueOrExpression<string> trimChars)
            {
                var captured = trimChars.Invoke();
                return ApplyStringOp(Describe(StringOperationKind.TrimEndAscii, textOperand: captured));
            }

            /// <summary>
            /// Replaces occurrences of captured text within the selected string.<br/>
            /// </summary>
            /// <param name="oldValue">Text to replace, evaluated and captured once while authoring.<br/></param>
            /// <param name="newValue">Replacement text, evaluated and captured once while authoring.<br/></param>
            /// <returns>A selector representing the string operation; no record is read while authoring.<br/></returns>
            /// <remarks>
            /// Matching uses ordinal comparison, optionally ignoring case. An empty search string leaves the source unchanged.<br/>
            /// </remarks>
            public StringSelector Replace(ValueOrExpression<string> oldValue, ValueOrExpression<string> newValue)
            {
                var capturedOldValue = oldValue.Invoke();
                var capturedNewValue = newValue.Invoke();
                return ApplyStringOp(Describe(StringOperationKind.Replace, capturedOldValue, capturedNewValue));
            }

            /// <summary>
            /// Replaces occurrences of captured text within the selected string.<br/>
            /// </summary>
            /// <param name="oldValue">Text to replace, evaluated and captured once while authoring.<br/></param>
            /// <param name="newValue">Replacement text, evaluated and captured once while authoring.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case; false preserves case-sensitive matching.<br/></param>
            /// <returns>A selector representing the string operation; no record is read while authoring.<br/></returns>
            /// <remarks>
            /// Matching uses ordinal comparison, optionally ignoring case. An empty search string leaves the source unchanged.<br/>
            /// </remarks>
            public StringSelector Replace(ValueOrExpression<string> oldValue, ValueOrExpression<string> newValue, bool ignoreCase)
            {
                if (!ignoreCase) return Replace(oldValue, newValue);
                var capturedOldValue = oldValue.Invoke();
                var capturedNewValue = newValue.Invoke();
                return ApplyStringOp(Describe(StringOperationKind.Replace, capturedOldValue, capturedNewValue, ignoreCase: true));
            }

            /// <summary>
            /// Replaces text using ordinal or culture-aware comparison semantics.<br/>
            /// The old and replacement values are captured once when the selector is authored; per-record execution performs no deferred-value invocation.<br/>
            /// </summary>
            /// <param name="oldValue">Text to locate.<br/></param>
            /// <param name="newValue">Replacement text.<br/></param>
            /// <param name="ignoreCase">Whether comparison ignores case.<br/></param>
            /// <param name="culture">Optional culture name; an empty value selects ordinal comparison.<br/></param>
            public StringSelector Replace(
                ValueOrExpression<string> oldValue,
                ValueOrExpression<string> newValue,
                bool ignoreCase,
                string culture)
            {
                if (string.IsNullOrEmpty(culture))
                    return Replace(oldValue, newValue, ignoreCase);

                return ApplyStringOp(Describe(StringOperationKind.Replace, oldValue.Invoke(), newValue.Invoke(), ignoreCase: ignoreCase, cultureName: culture));
            }

            /// <summary>
            /// Pads the selected string on the left to the requested UTF-16 length.<br/>
            /// </summary>
            /// <param name="totalWidth">Result width in UTF-16 code units.<br/></param>
            /// <param name="paddingChar">Character used for padding.<br/></param>
            public StringSelector PadLeft(long totalWidth, char paddingChar = ' ')
            {
                if (totalWidth < 0 || totalWidth > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(totalWidth));
                return ApplyStringOp(Describe(StringOperationKind.PadLeft, integerOperand: totalWidth, characterOperand: paddingChar));
            }

            /// <summary>
            /// Pads the selected string on the right to the requested UTF-16 length.<br/>
            /// </summary>
            /// <param name="totalWidth">Result width in UTF-16 code units.<br/></param>
            /// <param name="paddingChar">Character used for padding.<br/></param>
            public StringSelector PadRight(long totalWidth, char paddingChar = ' ')
            {
                if (totalWidth < 0 || totalWidth > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(totalWidth));
                return ApplyStringOp(Describe(StringOperationKind.PadRight, integerOperand: totalWidth, characterOperand: paddingChar));
            }

            /// <summary>
            /// Inserts captured text at a zero-based UTF-16 position.<br/>
            /// </summary>
            /// <param name="startIndex">Zero-based UTF-16 insertion position.<br/></param>
            /// <param name="value">Text captured once when the selector is authored.<br/></param>
            public StringSelector Insert(long startIndex, ValueOrExpression<string> value)
            {
                if (startIndex < 0 || startIndex > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(startIndex));
                return ApplyStringOp(Describe(StringOperationKind.Insert, textOperand: value.Invoke(), integerOperand: startIndex));
            }

            /// <summary>
            /// Removes all UTF-16 code units beginning at a zero-based position.<br/>
            /// </summary>
            /// <param name="startIndex">Zero-based UTF-16 removal position.<br/></param>
            public StringSelector Remove(long startIndex)
            {
                if (startIndex < 0 || startIndex > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(startIndex));
                return ApplyStringOp(Describe(StringOperationKind.Remove, integerOperand: startIndex));
            }

            /// <summary>
            /// Removes a count of UTF-16 code units beginning at a zero-based position.<br/>
            /// </summary>
            /// <param name="startIndex">Zero-based UTF-16 removal position.<br/></param>
            /// <param name="count">Number of UTF-16 code units to remove.<br/></param>
            public StringSelector Remove(long startIndex, long count)
            {
                if (startIndex < 0 || startIndex > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(startIndex));
                if (count < 0 || count > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(count));
                return ApplyStringOp(Describe(StringOperationKind.Remove, integerOperand: startIndex, secondIntegerOperand: count));
            }

            /// <summary>
            /// Applies an application function to each present selected string during native Fractal/Inheto evaluation.<br/>
            /// Returning <see langword="null"/> makes the selector produce no condition value; exceptions remain visible to the caller.<br/>
            /// </summary>
            /// <param name="operation">Application function invoked once for each present candidate string.<br/></param>
            /// <returns>A string selector that can continue through ordinary string condition operators.<br/></returns>
            public StringSelector External(Func<string, string?> operation)
            {
                ArgumentNullException.ThrowIfNull(operation);
                return ApplyStringOp(Describe(StringOperationKind.External, external: operation));
            }

            /// <summary>
            /// Decodes hexadecimal or Base64 text into binary bytes.<br/>
            /// </summary>
            /// <param name="encodingKind">Hex to decode hexadecimal digits, or Base64 to decode Base64 text.<br/></param>
            /// <returns>A selector representing the string operation; no record is read while authoring.<br/></returns>
            public BinarySelector ToBinary(EncodingKind encodingKind)
            {
                return encodingKind switch
                {
                    EncodingKind.Hex => ApplyBinaryOp(Describe(StringOperationKind.FromHex)),
                    EncodingKind.Base64 => ApplyBinaryOp(Describe(StringOperationKind.FromBase64)),
                    _ => throw new ArgumentOutOfRangeException(nameof(encodingKind), "Unknown encoding kind.")
                };
            }

            /// <summary>
            /// Views the selected string as its natural UTF-8 bytes so byte-oriented selector operations can continue without first materializing a .NET string.<br/>
            /// A direct Inheto string source borrows its stored UTF-8 payload; consumers such as <see cref="BinarySelector.Slice(ulong, ulong)"/> copy only the requested bytes.<br/>
            /// The returned selector uses byte offsets rather than Unicode scalar or UTF-16 positions and can therefore split a multi-byte UTF-8 sequence when the caller requests such a boundary.<br/>
            /// The complete semantic descriptor remains eligible for an explicit calculated LibraDex index.<br/>
            /// </summary>
            public BinarySelector AsUtf8
                => ApplyBinaryOp(Describe(StringOperationKind.AsUtf8));

            /// <summary>
            /// Appends captured literal text to the selected string.<br/>
            /// The argument is evaluated once while the selector is authored and the resulting immutable operation can participate in conditions, ordering, and calculated indexes.<br/>
            /// </summary>
            /// <param name="value">Literal or deferred value captured once for concatenation.<br/></param>
            /// <returns>A string selector representing the concatenated value.<br/></returns>
            public StringSelector Concat(ValueOrExpression<string> value)
            {
                ArgumentNullException.ThrowIfNull(value);
                string frozenValue = value.Invoke() ?? string.Empty;
                var source = GetSemanticValueDescriptor();
                BuiltInSelectorDescriptor? descriptor = source is null
                    ? null
                    : new BuiltInSelectorDescriptor(
                        source,
                        BuiltInSelectorOperationKind.StringConcatLiteral,
                        typeof(string),
                        typeof(string),
                        textOperand: frozenValue);
                return new StringSelector(
                    parentBuilder.AsComputed(),
                    (ValueOrExpression<string>)frozenValue,
                    addBase: false,
                    isLiteral: true,
                    descriptor);
            }

            /// <summary>
            /// Appends literal text to the selected string.<br/>
            /// </summary>
            /// <param name="value">Literal text to append.<br/></param>
            /// <returns>A string selector representing the concatenated value.<br/></returns>
            public StringSelector Concat(string value) => Concat((ValueOrExpression<string>)value);

            /// <summary>
            /// Appends the value produced by another string selector.<br/>
            /// Missing or null values on either side are treated as empty strings, matching ordinary C# <see cref="string.Concat(string?, string?)"/> semantics.<br/>
            /// The complete selector tree remains durable and can participate in conditions, ordering, readers, and calculated indexes.<br/>
            /// </summary>
            /// <param name="value">String selector whose value is appended to this selector.<br/></param>
            /// <returns>A string selector representing both values in left-to-right order.<br/></returns>
            public StringSelector Concat(StringSelector value)
            {
                ArgumentNullException.ThrowIfNull(value);
                var source = GetSemanticValueDescriptor();
                var rightSource = value.GetSemanticValueDescriptor();
                var descriptor = source is null || rightSource is null
                    ? null
                    : new StringOperationSelectorDescriptor(
                        source,
                        StringOperationKind.ConcatSelector,
                        rightSource: rightSource);
                return new StringSelector(
                    parentBuilder.AsComputed(),
                    (ValueOrExpression<string>)string.Empty,
                    addBase: false,
                    isLiteral: true,
                    descriptor);
            }
        }

        /// <summary>
        /// Built-in nodes are immutable and serializable; external nodes retain a runtime delegate and deliberately have no durable or indexed identity.<br/>
        /// </summary>
        internal sealed class StringOperationSelectorDescriptor : IBackendNeutralSelectorDescriptor
        {
            internal IBackendNeutralSelectorDescriptor Source { get; }
            internal StringOperationKind Operation { get; }
            internal string? TextOperand { get; }
            internal string? SecondTextOperand { get; }
            internal long? IntegerOperand { get; }
            internal long? SecondIntegerOperand { get; }
            internal char? CharacterOperand { get; }
            internal bool IgnoreCase { get; }
            internal string CultureName { get; }
            internal Func<string, string?>? ExternalOperation { get; }
            internal IBackendNeutralSelectorDescriptor? RightSource { get; }
            internal RegexOptions MatchOptions { get; }
            internal long MatchTimeoutTicks { get; }
            private readonly CultureInfo culture;
            private readonly char[]? trimCharacters;
            private readonly Regex? matchRegex;

            internal StringOperationSelectorDescriptor(
                IBackendNeutralSelectorDescriptor source,
                StringOperationKind operation,
                string? textOperand = null,
                string? secondTextOperand = null,
                long? integerOperand = null,
                long? secondIntegerOperand = null,
                char? characterOperand = null,
                bool ignoreCase = false,
                string? cultureName = null,
                Func<string, string?>? externalOperation = null,
                RegexOptions matchOptions = RegexOptions.None,
                long matchTimeoutTicks = 0,
                IBackendNeutralSelectorDescriptor? rightSource = null)
            {
                Source = source ?? throw new ArgumentNullException(nameof(source));
                Operation = operation;
                TextOperand = textOperand;
                SecondTextOperand = secondTextOperand;
                IntegerOperand = integerOperand;
                SecondIntegerOperand = secondIntegerOperand;
                CharacterOperand = characterOperand;
                IgnoreCase = ignoreCase;
                CultureName = cultureName ?? string.Empty;
                ExternalOperation = externalOperation;
                RightSource = rightSource;
                MatchOptions = matchOptions & ~RegexOptions.Compiled;
                MatchTimeoutTicks = matchTimeoutTicks;
                culture = string.IsNullOrEmpty(CultureName)
                    ? CultureInfo.InvariantCulture
                    : CultureInfo.GetCultureInfo(CultureName);
                trimCharacters = operation is
                    StringOperationKind.TrimAscii or
                    StringOperationKind.TrimStartAscii or
                    StringOperationKind.TrimEndAscii or
                    StringOperationKind.Trim or
                    StringOperationKind.TrimStart or
                    StringOperationKind.TrimEnd
                        ? textOperand?.ToCharArray()
                        : null;
                matchRegex = operation == StringOperationKind.MatchValue
                    ? CreateRuntimeRegex(
                        textOperand ?? throw new ArgumentNullException(nameof(textOperand)),
                        MatchOptions,
                        TimeSpan.FromTicks(matchTimeoutTicks))
                    : null;
                if ((operation == StringOperationKind.ConcatSelector) != (rightSource is not null))
                {
                    throw new ArgumentException(
                        "Selector concatenation requires exactly one right-side selector, and other string operations cannot contain one.",
                        nameof(rightSource));
                }
            }

            /// <summary>
            /// Creates one immutable regex match-value descriptor with normalized performance-only options.<br/>
            /// Group and occurrence are stored in the existing integral operand slots, while pattern, semantic options, and timeout fully define durable selector identity.<br/>
            /// </summary>
            /// <param name="source">Durable source-string selector.<br/></param>
            /// <param name="pattern">Validated regular expression.<br/></param>
            /// <param name="groupNumber">Declared numeric capture group.<br/></param>
            /// <param name="occurrenceNumber">Zero-based match occurrence.<br/></param>
            /// <returns>A complete match-value selector descriptor.<br/></returns>
            internal static StringOperationSelectorDescriptor CreateMatchValue(
                IBackendNeutralSelectorDescriptor source,
                Regex pattern,
                int groupNumber,
                int occurrenceNumber)
            {
                ArgumentNullException.ThrowIfNull(pattern);
                return new StringOperationSelectorDescriptor(
                    source,
                    StringOperationKind.MatchValue,
                    textOperand: pattern.ToString(),
                    integerOperand: groupNumber,
                    secondIntegerOperand: occurrenceNumber,
                    matchOptions: pattern.Options,
                    matchTimeoutTicks: pattern.MatchTimeout.Ticks);
            }

            internal bool TryGetSourcePropertyPath(out string propertyPath)
            {
                if (Source is DirectPropertySelectorDescriptor direct)
                {
                    propertyPath = direct.PropertyPath;
                    return true;
                }
                if (Source is StringOperationSelectorDescriptor operation)
                    return operation.TryGetSourcePropertyPath(out propertyPath);
                if (Source is BuiltInSelectorDescriptor builtIn)
                    return builtIn.TryGetSourcePropertyPath(out propertyPath);
                propertyPath = string.Empty;
                return false;
            }

            internal bool TryRead<TValue>(Inheto.InhetoBinary binary, Inheto.DeserializationOptions options, out TValue value)
            {
                value = default!;
                if (TryReadUtf8TextSlice(binary, options, out bool utf8Handled, out value) || utf8Handled)
                    return utf8Handled && value is not null;

                if (Operation == StringOperationKind.AsUtf8 && typeof(TValue) == typeof(byte[]))
                {
                    if (TryReadUtf8Payload(binary, options, out ReadOnlyMemory<byte> payload))
                    {
                        value = (TValue)(object)payload.ToArray();
                        return true;
                    }
                    if (!TryReadSource(binary, options, out string utf8Source))
                        return false;
                    value = (TValue)(object)Encoding.UTF8.GetBytes(utf8Source);
                    return true;
                }

                bool hasSource = TryReadSource(binary, options, out var source);

                if (Operation == StringOperationKind.ConcatSelector)
                {
                    if (typeof(TValue) != typeof(string))
                        return false;
                    string right = TryReadStringSource(RightSource!, binary, options, out var rightValue)
                        ? rightValue
                        : string.Empty;
                    value = (TValue)(object)string.Concat(hasSource ? source : string.Empty, right);
                    return true;
                }

                if (!hasSource)
                    return false;

                if (Operation == StringOperationKind.External)
                {
                    var externalValue = ExternalOperation!(source);
                    if (externalValue is null || typeof(TValue) != typeof(string))
                        return false;
                    value = (TValue)(object)externalValue;
                    return true;
                }

                try
                {
                    if (Operation is StringOperationKind.Length or StringOperationKind.RuneCount or StringOperationKind.IndexOf or StringOperationKind.LastIndexOf)
                    {
                        if (typeof(TValue) != typeof(long)) return false;
                        long longResult = Operation switch
                        {
                            StringOperationKind.Length => source.Length,
                            StringOperationKind.RuneCount => CountRunes(source),
                            StringOperationKind.IndexOf => Find(source, TextOperand, last: false),
                            _ => Find(source, TextOperand, last: true)
                        };
                        value = (TValue)(object)longResult;
                        return true;
                    }

                    if (Operation is StringOperationKind.SortKey or StringOperationKind.FromHex or StringOperationKind.FromBase64)
                    {
                        if (typeof(TValue) != typeof(byte[])) return false;
                        byte[] bytes = Operation switch
                        {
                            StringOperationKind.SortKey => ResolveCulture().CompareInfo.GetSortKey(source, (CompareOptions)IntegerOperand.GetValueOrDefault()).KeyData,
                            StringOperationKind.FromHex => Convert.FromHexString(source),
                            _ => Convert.FromBase64String(source)
                        };
                        value = (TValue)(object)bytes;
                        return true;
                    }

                    if (typeof(TValue) != typeof(string)) return false;
                    string? stringResult = Operation switch
                    {
                        StringOperationKind.Substring => SecondIntegerOperand is long count
                            ? source.Substring(checked((int)IntegerOperand!.Value), checked((int)count))
                            : source.Substring(checked((int)IntegerOperand!.Value)),
                        StringOperationKind.Left => source[..Math.Min(source.Length, checked((int)IntegerOperand!.Value))],
                        StringOperationKind.Right => source[Math.Max(0, source.Length - checked((int)IntegerOperand!.Value))..],
                        StringOperationKind.RuneSubstring => RuneSlice(source, IntegerOperand!.Value, SecondIntegerOperand),
                        StringOperationKind.RuneLeft => RuneSlice(source, 0, IntegerOperand, clampLength: true),
                        StringOperationKind.RuneRight => RuneRight(source, IntegerOperand!.Value),
                        StringOperationKind.AsciiLower => ChangeAsciiCase(source, upper: false),
                        StringOperationKind.AsciiUpper => ChangeAsciiCase(source, upper: true),
                        StringOperationKind.Lower => source.ToLower(ResolveCulture()),
                        StringOperationKind.Upper => source.ToUpper(ResolveCulture()),
                        StringOperationKind.Normalize => source.IsNormalized(NormalizationForm.FormC) ? source : source.Normalize(NormalizationForm.FormC),
                        StringOperationKind.TrimAscii => Trim(source, start: true, end: true, asciiDefault: true),
                        StringOperationKind.TrimStartAscii => Trim(source, start: true, end: false, asciiDefault: true),
                        StringOperationKind.TrimEndAscii => Trim(source, start: false, end: true, asciiDefault: true),
                        StringOperationKind.Trim => Trim(source, start: true, end: true, asciiDefault: false),
                        StringOperationKind.TrimStart => Trim(source, start: true, end: false, asciiDefault: false),
                        StringOperationKind.TrimEnd => Trim(source, start: false, end: true, asciiDefault: false),
                        StringOperationKind.Replace => Replace(source),
                        StringOperationKind.PadLeft => source.PadLeft(checked((int)IntegerOperand!.Value), CharacterOperand!.Value),
                        StringOperationKind.PadRight => source.PadRight(checked((int)IntegerOperand!.Value), CharacterOperand!.Value),
                        StringOperationKind.Insert => TextOperand is null ? null : source.Insert(checked((int)IntegerOperand!.Value), TextOperand),
                        StringOperationKind.Remove => SecondIntegerOperand is long removeCount
                            ? source.Remove(checked((int)IntegerOperand!.Value), checked((int)removeCount))
                            : source.Remove(checked((int)IntegerOperand!.Value)),
                        StringOperationKind.MatchValue => SelectMatchValue(source),
                        _ => null
                    };
                    if (stringResult is null) return false;
                    value = (TValue)(object)stringResult;
                    return true;
                }
                catch (Exception exception) when (
                    exception is ArgumentException or
                    FormatException or
                    OverflowException or
                    RegexMatchTimeoutException)
                {
                    return false;
                }
            }

            /// <summary>
            /// Borrows the direct source string's stored UTF-8 payload for the explicit <c>AsUtf8</c> operation.<br/>
            /// This method deliberately refuses computed string sources because their UTF-8 bytes do not already exist in the authoritative payload.<br/>
            /// </summary>
            /// <param name="binary">Authoritative Inheto record payload.<br/></param>
            /// <param name="payload">Borrowed UTF-8 payload when the operation and source are eligible.<br/></param>
            /// <param name="options">Active deserialization registrations; aliased source paths are excluded from the borrowed direct-payload route.<br/></param>
            /// <returns><see langword="true"/> for a present direct string source; otherwise <see langword="false"/>.<br/></returns>
            internal bool TryReadUtf8Payload(
                Inheto.InhetoBinary binary,
                Inheto.DeserializationOptions options,
                out ReadOnlyMemory<byte> payload)
            {
                payload = default;
                return Operation == StringOperationKind.AsUtf8 &&
                    Source is DirectPropertySelectorDescriptor direct &&
                    !options.Aliases.Contains(direct.PropertyPath) &&
                    binary.TryReadStringUtf8Payload(direct.PropertyPath, out payload);
            }

            /// <summary>
            /// Executes direct-source text slicing over Inheto's borrowed UTF-8 payload and decodes only the selected byte range.<br/>
            /// Rune operations map directly to UTF-8 scalar boundaries; UTF-16 operations use the same lane unless a requested boundary splits a surrogate pair, in which case the caller falls back to exact CLR string semantics.<br/>
            /// </summary>
            private bool TryReadUtf8TextSlice<TValue>(
                Inheto.InhetoBinary binary,
                Inheto.DeserializationOptions options,
                out bool handled,
                out TValue value)
            {
                handled = false;
                value = default!;
                if (typeof(TValue) != typeof(string) ||
                    Source is not DirectPropertySelectorDescriptor direct ||
                    options.Aliases.Contains(direct.PropertyPath) ||
                    Operation is not (StringOperationKind.Substring or
                        StringOperationKind.Left or
                        StringOperationKind.Right or
                        StringOperationKind.RuneSubstring or
                        StringOperationKind.RuneLeft or
                        StringOperationKind.RuneRight))
                {
                    return false;
                }

                if (!binary.TryReadStringUtf8Payload(direct.PropertyPath, out ReadOnlyMemory<byte> payload))
                {
                    handled = true;
                    return false;
                }

                if (!TryGetUtf8SliceRange(payload.Span, out int offset, out int length, out bool requiresClrFallback))
                {
                    handled = !requiresClrFallback;
                    return false;
                }

                handled = true;
                value = (TValue)(object)Encoding.UTF8.GetString(payload.Span.Slice(offset, length));
                return true;
            }

            /// <summary>
            /// Resolves this immutable text-slice operation to one valid UTF-8 byte range without allocating boundary tables.<br/>
            /// </summary>
            private bool TryGetUtf8SliceRange(
                ReadOnlySpan<byte> source,
                out int offset,
                out int length,
                out bool requiresClrFallback)
            {
                offset = 0;
                length = 0;
                requiresClrFallback = false;
                bool runeUnits = Operation is StringOperationKind.RuneSubstring or StringOperationKind.RuneLeft or StringOperationKind.RuneRight;
                if (!TryCountUtf8Units(source, runeUnits, out long total))
                {
                    requiresClrFallback = true;
                    return false;
                }

                long start;
                long end;
                if (Operation is StringOperationKind.Left or StringOperationKind.RuneLeft)
                {
                    start = 0;
                    end = Math.Min(total, IntegerOperand!.Value);
                }
                else if (Operation is StringOperationKind.Right or StringOperationKind.RuneRight)
                {
                    end = total;
                    start = Math.Max(0, total - IntegerOperand!.Value);
                }
                else
                {
                    start = IntegerOperand!.Value;
                    if (start > total)
                        return false;
                    end = SecondIntegerOperand is long count ? checked(start + count) : total;
                    if (end > total)
                        return false;
                }

                bool hasStart = TryFindUtf8Boundary(source, start, runeUnits, out offset, out bool startSplit);
                bool hasEnd = TryFindUtf8Boundary(source, end, runeUnits, out int endOffset, out bool endSplit);
                if (!hasStart || !hasEnd)
                {
                    requiresClrFallback = startSplit || endSplit;
                    return false;
                }
                length = endOffset - offset;
                return true;
            }

            /// <summary>
            /// Counts either Unicode scalar values or their represented UTF-16 code units directly from valid UTF-8.<br/>
            /// </summary>
            private static bool TryCountUtf8Units(ReadOnlySpan<byte> source, bool runeUnits, out long count)
            {
                count = 0;
                int offset = 0;
                while (offset < source.Length)
                {
                    OperationStatus status = Rune.DecodeFromUtf8(source[offset..], out Rune rune, out int consumed);
                    if (status != OperationStatus.Done)
                        return false;
                    count += runeUnits ? 1 : rune.Utf16SequenceLength;
                    offset += consumed;
                }
                return true;
            }

            /// <summary>
            /// Finds one scalar-safe UTF-8 byte boundary addressed by rune or UTF-16-unit position.<br/>
            /// </summary>
            private static bool TryFindUtf8Boundary(
                ReadOnlySpan<byte> source,
                long target,
                bool runeUnits,
                out int byteOffset,
                out bool splitScalar)
            {
                byteOffset = 0;
                splitScalar = false;
                long current = 0;
                while (byteOffset < source.Length)
                {
                    if (current == target)
                        return true;
                    OperationStatus status = Rune.DecodeFromUtf8(source[byteOffset..], out Rune rune, out int consumed);
                    if (status != OperationStatus.Done)
                        return false;
                    long next = current + (runeUnits ? 1 : rune.Utf16SequenceLength);
                    if (target > current && target < next)
                    {
                        splitScalar = true;
                        return false;
                    }
                    current = next;
                    byteOffset += consumed;
                }
                return current == target;
            }

            private bool TryReadSource(Inheto.InhetoBinary binary, Inheto.DeserializationOptions options, out string value)
            {
                value = string.Empty;
                if (Source is DirectPropertySelectorDescriptor direct)
                {
                    if (!Condition<T>.HasConditionValue(binary, direct.PropertyPath, options)) return false;
                    string? selected = binary.ReadPropAs<string>(direct.PropertyPath, options);
                    if (selected is null)
                        return false;
                    value = selected;
                    return true;
                }
                if (Source is StringOperationSelectorDescriptor operation)
                    return operation.TryRead(binary, options, out value);
                return Source is BuiltInSelectorDescriptor builtIn && builtIn.TryRead(binary, options, out value);
            }

            /// <summary>
            /// Reads one arbitrary durable string-selector descriptor without reflection.<br/>
            /// This shared path gives selector concatenation the same recursive source support as other built-in string transformations.<br/>
            /// </summary>
            /// <param name="descriptor">Direct or computed string descriptor to evaluate.<br/></param>
            /// <param name="binary">Authoritative Inheto record payload.<br/></param>
            /// <param name="options">Active Inheto deserialization registrations.<br/></param>
            /// <param name="value">Selected string when present.<br/></param>
            /// <returns><see langword="true"/> when the descriptor produced a present string; otherwise <see langword="false"/>.<br/></returns>
            private static bool TryReadStringSource(
                IBackendNeutralSelectorDescriptor descriptor,
                Inheto.InhetoBinary binary,
                Inheto.DeserializationOptions options,
                out string value)
            {
                value = string.Empty;
                if (descriptor is DirectPropertySelectorDescriptor direct)
                {
                    if (!Condition<T>.HasConditionValue(binary, direct.PropertyPath, options))
                        return false;
                    string? selected = binary.ReadPropAs<string>(direct.PropertyPath, options);
                    if (selected is null)
                        return false;
                    value = selected;
                    return true;
                }
                if (descriptor is StringOperationSelectorDescriptor text)
                    return text.TryRead(binary, options, out value);
                return descriptor is BuiltInSelectorDescriptor builtIn &&
                    builtIn.TryRead(binary, options, out value);
            }

            private CultureInfo ResolveCulture() => culture;

            private long Find(string source, string? target, bool last)
            {
                if (target is null) throw new ArgumentNullException(nameof(target));
                if (string.IsNullOrEmpty(CultureName))
                {
                    var comparison = IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                    return last ? source.LastIndexOf(target, comparison) : source.IndexOf(target, comparison);
                }
                var options = IgnoreCase ? CompareOptions.IgnoreCase : CompareOptions.None;
                return last
                    ? ResolveCulture().CompareInfo.LastIndexOf(source, target, options)
                    : ResolveCulture().CompareInfo.IndexOf(source, target, options);
            }

            private string Trim(string source, bool start, bool end, bool asciiDefault)
            {
                if (TextOperand is { } chars)
                {
                    if (chars.Length == 0) return source;
                    if (start && end) return source.Trim(trimCharacters!);
                    return start ? source.TrimStart(trimCharacters!) : source.TrimEnd(trimCharacters!);
                }
                if (!asciiDefault)
                {
                    if (start && end) return source.Trim();
                    return start ? source.TrimStart() : source.TrimEnd();
                }
                if (start && end) return source.Trim(' ');
                return start ? source.TrimStart(' ') : source.TrimEnd(' ');
            }

            private string? Replace(string source)
            {
                if (TextOperand is null || SecondTextOperand is null) return null;
                if (TextOperand.Length == 0) return source;
                if (string.IsNullOrEmpty(CultureName))
                {
                    var comparison = IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                    return source.Replace(TextOperand, SecondTextOperand, comparison);
                }

                var compareInfo = ResolveCulture().CompareInfo;
                var options = IgnoreCase ? CompareOptions.IgnoreCase : CompareOptions.None;
                var first = compareInfo.IndexOf(source, TextOperand, options);
                if (first < 0) return source;
                var builder = new StringBuilder(source.Length);
                var offset = 0;
                while (first >= 0)
                {
                    builder.Append(source, offset, first - offset);
                    builder.Append(SecondTextOperand);
                    offset = first + TextOperand.Length;
                    first = compareInfo.IndexOf(source, TextOperand, offset, options);
                }
                builder.Append(source, offset, source.Length - offset);
                return builder.ToString();
            }

            private string? SelectMatchValue(string source)
            {
                Match match = matchRegex!.Match(source);
                long occurrence = SecondIntegerOperand!.Value;
                for (long index = 0; index < occurrence && match.Success; index++)
                    match = match.NextMatch();
                if (!match.Success)
                    return null;

                long groupNumber = IntegerOperand!.Value;
                if (groupNumber < 0 || groupNumber >= match.Groups.Count)
                    return null;
                Group group = match.Groups[checked((int)groupNumber)];
                return group.Success ? group.Value : null;
            }

            internal static Regex CreateRuntimeRegex(
                string pattern,
                RegexOptions options,
                TimeSpan timeout)
            {
                RegexOptions semanticOptions = options & ~RegexOptions.Compiled;
                RegexOptions runtimeOptions = (semanticOptions & RegexOptions.NonBacktracking) != 0
                    ? semanticOptions
                    : semanticOptions | RegexOptions.Compiled;
                return new Regex(pattern, runtimeOptions, timeout);
            }

            private static long CountRunes(string source)
            {
                long count = 0;
                foreach (var _ in source.EnumerateRunes()) count++;
                return count;
            }

            private static string RuneSlice(string source, long start, long? length, bool clampLength = false)
            {
                if (start < 0 || length is < 0) throw new ArgumentOutOfRangeException();
                long runeIndex = 0;
                var startOffset = -1;
                var endOffset = source.Length;
                var targetEnd = length is long count ? checked(start + count) : long.MaxValue;
                var utf16Offset = 0;
                foreach (var rune in source.EnumerateRunes())
                {
                    if (runeIndex == start) startOffset = utf16Offset;
                    if (runeIndex == targetEnd) { endOffset = utf16Offset; break; }
                    utf16Offset += rune.Utf16SequenceLength;
                    runeIndex++;
                }
                if (startOffset < 0)
                {
                    if (start == runeIndex) startOffset = source.Length;
                    else throw new ArgumentOutOfRangeException(nameof(start));
                }
                if (!clampLength && length is long requested && targetEnd > runeIndex) throw new ArgumentOutOfRangeException(nameof(length));
                return source.Substring(startOffset, endOffset - startOffset);
            }

            private static string RuneRight(string source, long length)
            {
                var count = CountRunes(source);
                return RuneSlice(source, Math.Max(0, count - length), null);
            }

            private static string ChangeAsciiCase(string source, bool upper)
            {
                var changed = false;
                for (var index = 0; index < source.Length; index++)
                {
                    var c = source[index];
                    if ((!upper && c is >= 'A' and <= 'Z') || (upper && c is >= 'a' and <= 'z'))
                    {
                        changed = true;
                        break;
                    }
                }
                if (!changed) return source;
                return string.Create(source.Length, (source, upper), static (span, state) =>
                {
                    for (var index = 0; index < state.source.Length; index++)
                    {
                        var c = state.source[index];
                        span[index] = state.upper && c is >= 'a' and <= 'z'
                            ? (char)(c - 32)
                            : !state.upper && c is >= 'A' and <= 'Z'
                                ? (char)(c + 32)
                                : c;
                    }
                });
            }
        }
    }

    internal enum StringOperationKind
    {
        Length,        RuneCount,        Substring,        Left,        Right,        RuneSubstring,        RuneLeft,        RuneRight,        IndexOf,        LastIndexOf,        AsciiLower,        AsciiUpper,        Lower,        Upper,        Normalize,        SortKey,        TrimAscii,        TrimStartAscii,        TrimEndAscii,        Trim,        TrimStart,        TrimEnd,        Replace,        PadLeft,        PadRight,        Insert,        Remove,        FromHex,        FromBase64,        AsUtf8,        MatchValue,        ConcatSelector,        External
    }
}
