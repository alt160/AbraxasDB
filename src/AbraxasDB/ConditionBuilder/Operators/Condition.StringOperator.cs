using FractalKVS;
using System.Buffers;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using LibraDex;

namespace AbraxasDB
{

    public static partial class Condition<T>
    {
        #region Operators

        /// <summary>
        /// Fluent string comparison, membership, and pattern-matching operations for a selected value.<br/>
        /// </summary>
        /// <remarks>
        /// Operations append fragments to a mutable condition builder; authoring does not enumerate records.<br/>
        /// </remarks>
        public class StringOperator
        {
            /// <summary>
            /// Identifies the string projection family used by condition/index matching.<br/>
            /// </summary>
            /// <remarks>
            /// This enum describes projection intent; declaring a value does not create or rebuild an index.<br/>
            /// </remarks>
            public enum IndexType
            {
                /// <summary>Ordinary text projection without a collation-key or folded-text transformation.<br/></summary>
                Default,
                /// <summary>Culture/case-aware collation-key projection for comparisons.<br/></summary>
                CollationKey,
                /// <summary>Case-folded text projection for textual matching.<br/></summary>
                FoldedText
            }
            private Builder builder;
            private ValueOrExpression<string> selectorExpression = null!;
            private MatchValueOperation matchValue = null!;

            /// <summary>
            /// Creates the StringOperator stage over the supplied condition builder and selector.<br/>
            /// </summary>
            /// <param name="builder">Mutable builder that receives authored predicates.<br/></param>
            /// <param name="selectorExpression">Selected property path or calculated selector intent.<br/></param>
            public StringOperator(Builder builder, ValueOrExpression<string> selectorExpression)
            {
                this.builder = builder;
                this.selectorExpression = selectorExpression;
            }

            private StringOperator(Builder builder, MatchValueOperation matchValue)
            {
                this.builder = builder;
                this.matchValue = matchValue;
            }

            private static System.Globalization.CultureInfo ResolveCulture(string culture)
            {
                if (string.IsNullOrEmpty(culture))
                    return System.Globalization.CultureInfo.InvariantCulture;
                return System.Globalization.CultureInfo.GetCultureInfo(culture);
            }

            /// <summary>
            /// Builds the regular-expression execution options for one Abraxas string condition.<br/>
            /// An omitted culture is Abraxas's invariant-culture contract, so regex execution must include <see cref="RegexOptions.CultureInvariant"/> rather than silently inheriting the process current culture.<br/>
            /// Explicit culture requests retain the existing managed-regex behavior because <see cref="Regex"/> does not expose a public constructor that accepts an arbitrary comparison culture.<br/>
            /// </summary>
            /// <param name="ignoreCase">Whether regex matching ignores case.<br/></param>
            /// <param name="culture">The developer-supplied culture name, or an empty value for invariant semantics.<br/></param>
            /// <returns>Compiled regex options matching the Abraxas condition contract.<br/></returns>
            private static RegexOptions ResolveRegexOptions(bool ignoreCase, string culture)
            {
                RegexOptions options = RegexOptions.Compiled;
                if (ignoreCase)
                    options |= RegexOptions.IgnoreCase;
                if (string.IsNullOrEmpty(culture))
                    options |= RegexOptions.CultureInvariant;
                return options;
            }

            private static IndexType ComparisonIndexType(bool ignoreCase)
            {
                return ignoreCase ? IndexType.CollationKey : IndexType.Default;
            }

            private static IndexType FoldedIndexType(bool ignoreCase)
            {
                return ignoreCase ? IndexType.FoldedText : IndexType.Default;
            }

            private const char PrefixUpperSentinel = '\uffff';

            #region Operations

            /// <summary>
            /// The descriptor retains the source selector, deferred regex, group, occurrence, culture, and projection form so semantic execution and a maintained-index resolver share one identity.<br/>
            /// </summary>
            internal sealed class MatchValueSelectorDescriptor : IBackendNeutralSelectorDescriptor
            {
                internal ValueOrExpression<string> SourceSelector { get; }
                internal ValueOrExpression<Regex> PatternExpression { get; }
                internal int GroupNumber { get; }
                internal int? OccurrenceNumber { get; }
                internal string Culture { get; }
                internal IndexType ProjectionType { get; }

                /// <summary>
                /// Initializes immutable backend-neutral regex-value extraction metadata.<br/>
                /// </summary>
                /// <param name="sourceSelector">The direct source-string selector.<br/></param>
                /// <param name="patternExpression">The static or deferred regular expression.<br/></param>
                /// <param name="groupNumber">The numeric capture group returned from the selected match.<br/></param>
                /// <param name="occurrenceNumber">The zero-based match occurrence, or null for the first match.<br/></param>
                /// <param name="culture">The requested comparison culture for consumers of the extracted value.<br/></param>
                /// <param name="projectionType">The requested derived string projection form.<br/></param>
                internal MatchValueSelectorDescriptor(
                    ValueOrExpression<string> sourceSelector,
                    ValueOrExpression<Regex> patternExpression,
                    int groupNumber,
                    int? occurrenceNumber,
                    string? culture,
                    IndexType projectionType)
                {
                    SourceSelector = sourceSelector;
                    PatternExpression = patternExpression;
                    GroupNumber = groupNumber;
                    OccurrenceNumber = occurrenceNumber;
                    Culture = culture ?? string.Empty;
                    ProjectionType = projectionType;
                }
            }

            private class MatchValueOperation
            {
                public MatchValueOperation(ValueOrExpression<string> selectorExpression, ValueOrExpression<Regex> patternExpression, int groupNumber = 0, int? occurrenceNumber = null, string? culture = "", IndexType indexType = IndexType.Default)
                {
                    Descriptor = new MatchValueSelectorDescriptor(
                        selectorExpression,
                        patternExpression,
                        groupNumber,
                        occurrenceNumber,
                        culture,
                        indexType);
                }
                internal MatchValueSelectorDescriptor Descriptor { get; }
            }

            internal static string[] StringOperatorSymbols = [">", ">=", "<", "<=", "!=", "==", "IS NULL", "IS NOT NULL", "IN (", "NOT IN (", "BETWEEN", "NOT BETWEEN", "LIKE", "NOT LIKE"];
            internal enum StringOperatorSymbolsEnum { Greater,GreaterOrEqual,Less,LessOrEqual,NotEqual,Equal,IsNull,IsNotNull,In,NotIn,Between,NotBetween,Like,NotLike,StartsWith,EndsWith,Contains,Matches,NotMatches,MatchesWith,InSet,NotInSet,NotContains }

            private enum Utf8PredicateResult : byte
            {
                NoMatch,
                Match,
                RequiresManaged
            }

            private class StringOperation : IConditionLeaf, IFastPredicateSource, ILibraDexDerivedConditionSource, ILibraDexAliasedConditionSource
            {
                private IndexType indexType = IndexType.Default;
                public StringOperation(StringOperatorSymbolsEnum @operator, MatchValueOperation matchValue, string culture, bool ignoreCase, IndexType indexType, params ValueOrExpression<string>[]? strings)
                {
                    this.matchValue = matchValue;
                    this.strings = strings;
                    this.symbol = @operator;
                    this.ignoreCase = ignoreCase;
                    this.culture = culture;
                    this.indexType = indexType;
                }
                public StringOperation(StringOperatorSymbolsEnum @operator, MatchValueOperation matchValue, string culture, bool ignoreCase, IndexType indexType, Func<IEnumerable<string>> stringList)
                {
                    this.matchValue = matchValue;
                    this.stringList = stringList;
                    this.symbol = @operator;
                    this.ignoreCase = ignoreCase;
                    this.culture = culture;
                    this.indexType = indexType;
                }
                /// <summary>
                /// Retains a whole-set operand for a derived string selector without evaluating it during construction or replay.<br/>
                /// Membership is materialized by the existing execution preparation, outside per-record evaluation.<br/>
                /// </summary>
                /// <param name="operator">Positive or negative string membership operation.<br/></param>
                /// <param name="matchValue">Derived string selector metadata.<br/></param>
                /// <param name="culture">Existing comparison culture name.<br/></param>
                /// <param name="ignoreCase">Existing case-comparison policy.<br/></param>
                /// <param name="indexType">Existing text-index representation.<br/></param>
                /// <param name="stringList">Non-null static, factory-backed or explicit-parameter enumerable operand.<br/></param>
                public StringOperation(StringOperatorSymbolsEnum @operator, MatchValueOperation matchValue, string culture, bool ignoreCase, IndexType indexType, ValueOrExpression<IEnumerable<string>> stringList)
                {
                    ArgumentNullException.ThrowIfNull(stringList);
                    this.matchValue = matchValue;
                    this.stringList = stringList;
                    this.symbol = @operator;
                    this.ignoreCase = ignoreCase;
                    this.culture = culture;
                    this.indexType = indexType;
                }
                public StringOperation(StringOperatorSymbolsEnum @operator, MatchValueOperation matchValue, string culture, bool ignoreCase, IndexType indexType, HashSet<string> strings)
                {
                    this.matchValue = matchValue;
                    this.stringSet = strings;
                    this.symbol = @operator;
                    this.ignoreCase = ignoreCase;
                    this.culture = culture;
                    this.indexType = indexType;
                }
                public StringOperation(StringOperatorSymbolsEnum @operator, MatchValueOperation matchValue, string culture, bool ignoreCase, IndexType indexType, Func<HashSet<string>> strings)
                {
                    this.matchValue = matchValue;
                    this.stringSet = strings;
                    this.symbol = @operator;
                    this.ignoreCase = ignoreCase;
                    this.culture = culture;
                    this.indexType = indexType;
                }
                public StringOperation(StringOperatorSymbolsEnum @operator, ValueOrExpression<string> selectorExpression, string culture, bool ignoreCase, IndexType indexType, params ValueOrExpression<string>[]? strings)
                {
                    this.selectorExpression = selectorExpression;
                    this.strings = strings;
                    this.symbol = @operator;
                    this.ignoreCase = ignoreCase;
                    this.culture = culture;
                    this.indexType = indexType;
                }
                public StringOperation(StringOperatorSymbolsEnum @operator, ValueOrExpression<string> selectorExpression, string culture, bool ignoreCase, IndexType indexType, Func<IEnumerable<string>> stringList)
                {
                    this.selectorExpression = selectorExpression;
                    this.stringList = stringList;
                    this.symbol = @operator;
                    this.ignoreCase = ignoreCase;
                    this.culture = culture;
                    this.indexType = indexType;
                }
                /// <summary>
                /// Retains a whole-set operand for a string selector without evaluating it during construction or replay.<br/>
                /// Explicit enumerable parameters keep their existing identity-wide execution snapshot; factories remain execution-time operands.<br/>
                /// </summary>
                /// <param name="operator">Positive or negative string membership operation.<br/></param>
                /// <param name="selectorExpression">String value selector.<br/></param>
                /// <param name="culture">Existing comparison culture name.<br/></param>
                /// <param name="ignoreCase">Existing case-comparison policy.<br/></param>
                /// <param name="indexType">Existing text-index representation.<br/></param>
                /// <param name="stringList">Non-null static, factory-backed or explicit-parameter enumerable operand.<br/></param>
                public StringOperation(StringOperatorSymbolsEnum @operator, ValueOrExpression<string> selectorExpression, string culture, bool ignoreCase, IndexType indexType, ValueOrExpression<IEnumerable<string>> stringList)
                {
                    ArgumentNullException.ThrowIfNull(stringList);
                    this.selectorExpression = selectorExpression;
                    this.stringList = stringList;
                    this.symbol = @operator;
                    this.ignoreCase = ignoreCase;
                    this.culture = culture;
                    this.indexType = indexType;
                }
                public StringOperation(StringOperatorSymbolsEnum @operator, ValueOrExpression<string> selectorExpression, string culture, bool ignoreCase, IndexType indexType, HashSet<string> strings)
                {
                    this.selectorExpression = selectorExpression;
                    this.stringSet = strings;
                    this.symbol = @operator;
                    this.ignoreCase = ignoreCase;
                    this.culture = culture;
                    this.indexType = indexType;
                }
                public StringOperation(StringOperatorSymbolsEnum @operator, ValueOrExpression<string> selectorExpression, string culture, bool ignoreCase, IndexType indexType, Func<HashSet<string>> strings)
                {
                    this.selectorExpression = selectorExpression;
                    this.stringSet = strings;
                    this.symbol = @operator;
                    this.ignoreCase = ignoreCase;
                    this.culture = culture;
                    this.indexType = indexType;
                }
                private bool ignoreCase;
                private string culture;
                private StringOperatorSymbolsEnum symbol;
                private ValueOrExpression<string> selectorExpression = null!;
                private MatchValueOperation matchValue = null!;
                private ValueOrExpression<IEnumerable<string>> stringList = null!;
                private IEnumerable<ValueOrExpression<string>>? strings;
                private ValueOrExpression<HashSet<string>> stringSet = null!;

                /// <summary>
                /// Builds the complete backend-neutral evaluator for a direct string property or a regex-derived string value.<br/>
                /// All static and deferred operands, regexes, cultures, and membership sets are frozen once while the execution plan is built.<br/>
                /// Missing, null, or failed regex extraction values satisfy only <c>IsNull</c>; other positive and negated comparisons remain filtered out.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the source property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the selector shape is supported.<br/></param>
                /// <returns>True when a direct or regex-derived property accessor and operation evaluator were built; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<Inheto.InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryBuildValueReader(builder, options, out var propPath, out var readValue))
                        return false;

                    var cultureInfo = ResolveCulture(culture);
                    var values = MaterializeValues(builder, propPath);
                    var normalizedValues = values.Select(value => NormalizeForSemanticComparison(value, cultureInfo)).ToArray();
                    bool useInvariantAsciiFoldedPattern =
                        ignoreCase &&
                        indexType == IndexType.FoldedText &&
                        cultureInfo.Equals(CultureInfo.InvariantCulture) &&
                        values.Length != 0 &&
                        IsAscii(values[0]) &&
                        symbol is StringOperatorSymbolsEnum.StartsWith or
                            StringOperatorSymbolsEnum.EndsWith or
                            StringOperatorSymbolsEnum.Contains or
                            StringOperatorSymbolsEnum.NotContains;
                    var membership = new HashSet<string>(
                        normalizedValues,
                        ignoreCase && indexType == IndexType.CollationKey
                            ? StringComparer.Create(cultureInfo, ignoreCase: true)
                            : StringComparer.Ordinal);
                    Regex? comparisonRegex = null;
                    if (symbol is StringOperatorSymbolsEnum.Matches or StringOperatorSymbolsEnum.NotMatches)
                    {
                        if (values.Length == 0)
                            return false;
                        var regexOptions = ResolveRegexOptions(ignoreCase, culture);
                        comparisonRegex = new Regex(values[0], regexOptions);
                    }
                    else if (symbol is StringOperatorSymbolsEnum.Like or StringOperatorSymbolsEnum.NotLike)
                    {
                        if (normalizedValues.Length == 0)
                            return false;
                        comparisonRegex = CompileWildcardRegex(normalizedValues[0]);
                    }

                    bool useDirectUtf8Presence =
                        TryExtractPropPath(builder, out string directUtf8Path) &&
                        symbol is StringOperatorSymbolsEnum.IsNull or StringOperatorSymbolsEnum.IsNotNull;
                    bool useDirectUtf8Pattern =
                        // ASCII no-case byte folding is valid only for the invariant culture contract.
                        (!ignoreCase || cultureInfo.Equals(CultureInfo.InvariantCulture)) &&
                        directUtf8Path.Length != 0 &&
                        values.Length != 0 &&
                        IsAscii(values[0]) &&
                        symbol is StringOperatorSymbolsEnum.StartsWith or
                            StringOperatorSymbolsEnum.EndsWith or
                            StringOperatorSymbolsEnum.Contains or
                            StringOperatorSymbolsEnum.NotContains;
                    byte[]? directUtf8Pattern = useDirectUtf8Pattern
                        ? Encoding.UTF8.GetBytes(values[0])
                        : null;
                    bool useDirectUtf8Regex =
                        directUtf8Path.Length != 0 &&
                        comparisonRegex is not null &&
                        symbol is StringOperatorSymbolsEnum.Matches or StringOperatorSymbolsEnum.NotMatches;

                    evaluator = binary =>
                    {
                        if (useDirectUtf8Presence)
                        {
                            bool present = binary.TryReadStringUtf8Payload(directUtf8Path, options, out _);
                            return symbol == StringOperatorSymbolsEnum.IsNotNull ? present : !present;
                        }

                        if (useDirectUtf8Pattern)
                        {
                            if (!binary.TryReadStringUtf8Payload(directUtf8Path, options, out ReadOnlyMemory<byte> utf8))
                                return false;

                            Utf8PredicateResult utf8Result = EvaluateUtf8Predicate(
                                utf8.Span,
                                directUtf8Pattern!,
                                symbol,
                                ignoreCase);
                            if (utf8Result != Utf8PredicateResult.RequiresManaged)
                                return utf8Result == Utf8PredicateResult.Match;
                        }

                        if (useDirectUtf8Regex)
                        {
                            if (!binary.TryReadStringUtf8Payload(directUtf8Path, options, out ReadOnlyMemory<byte> utf8))
                                return false;

                            bool matched = EvaluateUtf8Regex(utf8.Span, comparisonRegex!);
                            return symbol == StringOperatorSymbolsEnum.Matches ? matched : !matched;
                        }

                        var extracted = readValue(binary);
                        if (symbol == StringOperatorSymbolsEnum.IsNull)
                            return !extracted.HasValue;
                        if (symbol == StringOperatorSymbolsEnum.IsNotNull)
                            return extracted.HasValue;
                        if (!extracted.HasValue || extracted.Value is null)
                            return false;

                        if (useInvariantAsciiFoldedPattern && IsAscii(extracted.Value))
                        {
                            return symbol switch
                            {
                                StringOperatorSymbolsEnum.StartsWith => extracted.Value.StartsWith(values[0], StringComparison.OrdinalIgnoreCase),
                                StringOperatorSymbolsEnum.EndsWith => extracted.Value.EndsWith(values[0], StringComparison.OrdinalIgnoreCase),
                                StringOperatorSymbolsEnum.Contains => extracted.Value.Contains(values[0], StringComparison.OrdinalIgnoreCase),
                                StringOperatorSymbolsEnum.NotContains => !extracted.Value.Contains(values[0], StringComparison.OrdinalIgnoreCase),
                                _ => false
                            };
                        }

                        var candidate = NormalizeForSemanticComparison(extracted.Value, cultureInfo);
                        switch (symbol)
                        {
                            case StringOperatorSymbolsEnum.Greater:
                                return normalizedValues.Length != 0 && CompareSemantic(candidate, normalizedValues[0], cultureInfo) > 0;
                            case StringOperatorSymbolsEnum.GreaterOrEqual:
                                return normalizedValues.Length != 0 && CompareSemantic(candidate, normalizedValues[0], cultureInfo) >= 0;
                            case StringOperatorSymbolsEnum.Less:
                                return normalizedValues.Length != 0 && CompareSemantic(candidate, normalizedValues[0], cultureInfo) < 0;
                            case StringOperatorSymbolsEnum.LessOrEqual:
                                return normalizedValues.Length != 0 && CompareSemantic(candidate, normalizedValues[0], cultureInfo) <= 0;
                            case StringOperatorSymbolsEnum.Equal:
                                return normalizedValues.Length != 0 && CompareSemantic(candidate, normalizedValues[0], cultureInfo) == 0;
                            case StringOperatorSymbolsEnum.NotEqual:
                                return normalizedValues.Length != 0 && CompareSemantic(candidate, normalizedValues[0], cultureInfo) != 0;
                            case StringOperatorSymbolsEnum.In:
                            case StringOperatorSymbolsEnum.InSet:
                                return membership.Contains(candidate);
                            case StringOperatorSymbolsEnum.NotIn:
                            case StringOperatorSymbolsEnum.NotInSet:
                                return !membership.Contains(candidate);
                            case StringOperatorSymbolsEnum.Between:
                                return normalizedValues.Length >= 2 &&
                                    CompareSemantic(candidate, normalizedValues[0], cultureInfo) >= 0 &&
                                    CompareSemantic(candidate, normalizedValues[1], cultureInfo) <= 0;
                            case StringOperatorSymbolsEnum.NotBetween:
                                return normalizedValues.Length >= 2 &&
                                    (CompareSemantic(candidate, normalizedValues[0], cultureInfo) < 0 ||
                                     CompareSemantic(candidate, normalizedValues[1], cultureInfo) > 0);
                            case StringOperatorSymbolsEnum.StartsWith:
                                return normalizedValues.Length != 0 && candidate.StartsWith(normalizedValues[0], StringComparison.Ordinal);
                            case StringOperatorSymbolsEnum.EndsWith:
                                return normalizedValues.Length != 0 && candidate.EndsWith(normalizedValues[0], StringComparison.Ordinal);
                            case StringOperatorSymbolsEnum.Contains:
                                return normalizedValues.Length != 0 && candidate.Contains(normalizedValues[0], StringComparison.Ordinal);
                            case StringOperatorSymbolsEnum.NotContains:
                                return normalizedValues.Length != 0 && !candidate.Contains(normalizedValues[0], StringComparison.Ordinal);
                            case StringOperatorSymbolsEnum.Like:
                                return SemanticWorkerRegexScope.Resolve(comparisonRegex!).IsMatch(candidate);
                            case StringOperatorSymbolsEnum.NotLike:
                                return !SemanticWorkerRegexScope.Resolve(comparisonRegex!).IsMatch(candidate);
                            case StringOperatorSymbolsEnum.Matches:
                                return SemanticWorkerRegexScope.Resolve(comparisonRegex!).IsMatch(extracted.Value);
                            case StringOperatorSymbolsEnum.NotMatches:
                                return !SemanticWorkerRegexScope.Resolve(comparisonRegex!).IsMatch(extracted.Value);
                            default:
                                return false;
                        }
                    };
                    return true;
                }

                /// <summary>
                /// Evaluates one regular expression over a direct string property's borrowed UTF-8 payload without constructing a detached CLR string.<br/>
                /// Ordinary values decode into a bounded stack span; unusually long values rent one temporary character array and return it immediately after the synchronous regex call.<br/>
                /// The existing <see cref="Regex"/> instance remains the semantic authority for case, culture, timeout, direction, and all pattern behavior.<br/>
                /// </summary>
                /// <param name="utf8">Borrowed UTF-8 payload whose lifetime covers this synchronous evaluation only.<br/></param>
                /// <param name="regex">Execution-frozen regular expression created by the surrounding condition plan.<br/></param>
                /// <returns><see langword="true"/> when the complete decoded character span satisfies the regular expression; otherwise <see langword="false"/>.<br/></returns>
                private static bool EvaluateUtf8Regex(ReadOnlySpan<byte> utf8, Regex regex)
                {
                    const int stackCharacterLimit = 512;
                    int characterCount = Encoding.UTF8.GetCharCount(utf8);
                    if (characterCount <= stackCharacterLimit)
                    {
                        Span<char> characters = stackalloc char[characterCount];
                        int written = Encoding.UTF8.GetChars(utf8, characters);
                        return SemanticWorkerRegexScope.Resolve(regex).IsMatch(characters[..written]);
                    }

                    char[] rented = ArrayPool<char>.Shared.Rent(characterCount);
                    try
                    {
                        int written = Encoding.UTF8.GetChars(utf8, rented);
                        return SemanticWorkerRegexScope.Resolve(regex).IsMatch(rented.AsSpan(0, written));
                    }
                    finally
                    {
                        ArrayPool<char>.Shared.Return(rented);
                    }
                }

                /// <summary>
                /// Evaluates one simple string predicate directly over Inheto's borrowed UTF-8 payload without allocating a CLR string.<br/>
                /// Case-sensitive ASCII patterns are byte-exact for every valid UTF-8 candidate; invariant no-case patterns remain byte-native only when the complete candidate is ASCII.<br/>
                /// A non-ASCII no-case candidate returns <see cref="Utf8PredicateResult.RequiresManaged"/> so the established .NET culture-folding semantics remain authoritative.<br/>
                /// </summary>
                /// <param name="candidate">Borrowed UTF-8 bytes for the stored string value.<br/></param>
                /// <param name="pattern">Execution-frozen ASCII UTF-8 operand.<br/></param>
                /// <param name="operation">Supported StartsWith, EndsWith, Contains, or NotContains operation.<br/></param>
                /// <param name="foldAsciiCase">Whether ASCII letters compare without case.<br/></param>
                /// <returns>A definitive match/no-match result, or a managed-fallback marker when Unicode case semantics are required.<br/></returns>
                private static Utf8PredicateResult EvaluateUtf8Predicate(
                    ReadOnlySpan<byte> candidate,
                    ReadOnlySpan<byte> pattern,
                    StringOperatorSymbolsEnum operation,
                    bool foldAsciiCase)
                {
                    if (foldAsciiCase && !IsAscii(candidate))
                        return Utf8PredicateResult.RequiresManaged;

                    bool matched = operation switch
                    {
                        StringOperatorSymbolsEnum.StartsWith => StartsWith(candidate, pattern, foldAsciiCase),
                        StringOperatorSymbolsEnum.EndsWith => EndsWith(candidate, pattern, foldAsciiCase),
                        StringOperatorSymbolsEnum.Contains => Contains(candidate, pattern, foldAsciiCase),
                        StringOperatorSymbolsEnum.NotContains => !Contains(candidate, pattern, foldAsciiCase),
                        _ => false
                    };
                    return matched ? Utf8PredicateResult.Match : Utf8PredicateResult.NoMatch;
                }

                /// <summary>
                /// Determines whether every byte in one UTF-8 payload is ASCII.<br/>
                /// UTF-8 encodes every non-ASCII scalar with bytes whose high bit is set, so this scan is also a complete no-case fast-path eligibility test.<br/>
                /// </summary>
                /// <param name="value">Borrowed UTF-8 candidate bytes.<br/></param>
                /// <returns><see langword="true"/> when every byte is below 128; otherwise <see langword="false"/>.<br/></returns>
                private static bool IsAscii(ReadOnlySpan<byte> value)
                {
                    foreach (byte item in value)
                    {
                        if (item > 0x7f)
                            return false;
                    }

                    return true;
                }

                /// <summary>
                /// Tests an ASCII pattern against the beginning of one UTF-8 candidate.<br/>
                /// </summary>
                /// <param name="candidate">Candidate UTF-8 bytes.<br/></param>
                /// <param name="pattern">ASCII pattern bytes.<br/></param>
                /// <param name="foldAsciiCase">Whether ASCII letters compare without case.<br/></param>
                /// <returns><see langword="true"/> when the candidate begins with the pattern.<br/></returns>
                private static bool StartsWith(ReadOnlySpan<byte> candidate, ReadOnlySpan<byte> pattern, bool foldAsciiCase)
                    => candidate.Length >= pattern.Length && EqualsAscii(candidate[..pattern.Length], pattern, foldAsciiCase);

                /// <summary>
                /// Tests an ASCII pattern against the end of one UTF-8 candidate.<br/>
                /// </summary>
                /// <param name="candidate">Candidate UTF-8 bytes.<br/></param>
                /// <param name="pattern">ASCII pattern bytes.<br/></param>
                /// <param name="foldAsciiCase">Whether ASCII letters compare without case.<br/></param>
                /// <returns><see langword="true"/> when the candidate ends with the pattern.<br/></returns>
                private static bool EndsWith(ReadOnlySpan<byte> candidate, ReadOnlySpan<byte> pattern, bool foldAsciiCase)
                    => candidate.Length >= pattern.Length && EqualsAscii(candidate[^pattern.Length..], pattern, foldAsciiCase);

                /// <summary>
                /// Searches one UTF-8 candidate for an ASCII byte pattern without allocating or transcoding.<br/>
                /// </summary>
                /// <param name="candidate">Candidate UTF-8 bytes.<br/></param>
                /// <param name="pattern">ASCII pattern bytes.<br/></param>
                /// <param name="foldAsciiCase">Whether ASCII letters compare without case.<br/></param>
                /// <returns><see langword="true"/> when the candidate contains the pattern.<br/></returns>
                private static bool Contains(ReadOnlySpan<byte> candidate, ReadOnlySpan<byte> pattern, bool foldAsciiCase)
                {
                    if (pattern.IsEmpty)
                        return true;
                    if (candidate.Length < pattern.Length)
                        return false;

                    int last = candidate.Length - pattern.Length;
                    for (int offset = 0; offset <= last; offset++)
                    {
                        if (EqualsAscii(candidate.Slice(offset, pattern.Length), pattern, foldAsciiCase))
                            return true;
                    }

                    return false;
                }

                /// <summary>
                /// Compares equal-length ASCII byte spans with optional case folding.<br/>
                /// </summary>
                /// <param name="left">Candidate byte window.<br/></param>
                /// <param name="right">ASCII pattern bytes.<br/></param>
                /// <param name="foldAsciiCase">Whether ASCII letters compare without case.<br/></param>
                /// <returns><see langword="true"/> when all bytes compare equal.<br/></returns>
                private static bool EqualsAscii(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, bool foldAsciiCase)
                {
                    if (!foldAsciiCase)
                        return left.SequenceEqual(right);

                    for (int index = 0; index < left.Length; index++)
                    {
                        byte leftValue = left[index];
                        byte rightValue = right[index];
                        if (leftValue is >= (byte)'A' and <= (byte)'Z')
                            leftValue = (byte)(leftValue + ('a' - 'A'));
                        if (rightValue is >= (byte)'A' and <= (byte)'Z')
                            rightValue = (byte)(rightValue + ('a' - 'A'));
                        if (leftValue != rightValue)
                            return false;
                    }

                    return true;
                }

                /// <summary>
                /// Builds a direct-property or regex-derived value reader without hydrating the containing record object.<br/>
                /// Regex extraction uses the selected numeric capture group and zero-based match occurrence, converting extraction failures to a semantic null value.<br/>
                /// </summary>
                private bool TryBuildValueReader(
                    Builder builder,
                    Inheto.DeserializationOptions options,
                    out string propPath,
                    out Func<Inheto.InhetoBinary, (bool HasValue, string? Value)> readValue)
                {
                    readValue = default!;
                    if (selectorExpression is not null &&
                        Condition<T>.TryBuildConditionValueReader<string>(
                            builder,
                            selectorExpression,
                            options,
                            out propPath,
                            out var typedReader))
                    {
                        readValue = binary =>
                        {
                            var value = typedReader(binary);
                            return value.HasValue
                                ? (true, value.Value)
                                : (false, null);
                        };
                        return true;
                    }

                    propPath = string.Empty;
                    if (matchValue is null ||
                        !Condition<T>.TryExtractPropPath(matchValue.Descriptor.SourceSelector, out propPath))
                        return false;

                    var regex = builder.MaterializeParameter(
                        matchValue.Descriptor.PatternExpression,
                        propPath,
                        owner: matchValue.Descriptor);
                    var groupNumber = matchValue.Descriptor.GroupNumber;
                    var occurrenceNumber = matchValue.Descriptor.OccurrenceNumber ?? 0;
                    var regexPath = propPath;
                    Inheto.PropPathCheck pathCheck = Inheto.InhetoBinary.CheckPropPath<T, string>(regexPath);
                    if (!pathCheck.IsValid)
                        return false;
                    readValue = binary =>
                    {
                        try
                        {
                            Inheto.PropPathReadState state = binary.TryReadPropPath(
                                pathCheck,
                                out string? source,
                                options,
                                throwOnInvocationError: true);
                            source = state == Inheto.PropPathReadState.Value
                                ? source ?? string.Empty
                                : string.Empty;
                            var match = regex.Match(source);
                            for (var i = 0; i < occurrenceNumber && match.Success; i++)
                                match = match.NextMatch();
                            if (!match.Success || groupNumber < 0 || groupNumber >= match.Groups.Count)
                                return (false, null);

                            var group = match.Groups[groupNumber];
                            return group.Success ? (true, group.Value) : (false, null);
                        }
                        catch
                        {
                            return (false, null);
                        }
                    };
                    return occurrenceNumber >= 0;
                }

                /// <summary>
                /// Materializes every string operand once for the current execution plan and returns a stable array for per-record comparisons.<br/>
                /// </summary>
                private string[] MaterializeValues(Builder builder, string propPath)
                {
                    if (strings is not null)
                        return strings.Select(value => builder.MaterializeParameter(value, propPath, owner: this)).ToArray();
                    if (stringList is not null)
                        return builder.MaterializeParameter(stringList, propPath, owner: this)?.ToArray() ?? Array.Empty<string>();
                    if (stringSet is not null)
                        return builder.MaterializeParameter(stringSet, propPath, owner: this)?.ToArray() ?? Array.Empty<string>();
                    return Array.Empty<string>();
                }

                /// <summary>
                /// </summary>
                private string NormalizeForSemanticComparison(string value, CultureInfo cultureInfo)
                    => ignoreCase && indexType == IndexType.FoldedText ? value.ToLower(cultureInfo) : value;

                /// <summary>
                /// Determines whether one string can participate in the allocation-free invariant folded-pattern fast path.<br/>
                /// ASCII casing is identical under invariant lower-case-plus-ordinal and ordinal-ignore-case comparison, while any non-ASCII value deliberately falls back to the established culture-folding implementation.<br/>
                /// </summary>
                /// <param name="value">The condition operand or decoded source value to inspect.<br/></param>
                /// <returns><see langword="true"/> when every UTF-16 code unit is in the ASCII range; otherwise <see langword="false"/>.<br/></returns>
                private static bool IsAscii(string value)
                {
                    for (int index = 0; index < value.Length; index++)
                    {
                        if (value[index] > 0x7f)
                        {
                            return false;
                        }
                    }

                    return true;
                }

                /// <summary>
                /// Compiles the Abraxas wildcard grammar into one anchored regular expression for reuse by an execution plan.<br/>
                /// `*` spans zero or more characters and `?` spans exactly one character; a backslash escapes only wildcard syntax, so ordinary values such as `domain\username` and ordinary doubled backslashes retain their natural meaning.<br/>
                /// </summary>
                /// <param name="pattern">The already normalized logical wildcard pattern.<br/></param>
                /// <returns>A compiled, culture-invariant expression with whole-value semantics.<br/></returns>
                private static Regex CompileWildcardRegex(string pattern)
                {
                    ArgumentNullException.ThrowIfNull(pattern);
                    var expression = new StringBuilder(pattern.Length + 8);
                    expression.Append("\\A");
                    for (int index = 0; index < pattern.Length; index++)
                    {
                        char value = pattern[index];
                        if (value == '\\' && index + 1 < pattern.Length && pattern[index + 1] is '*' or '?')
                        {
                            expression.Append(Regex.Escape(pattern[++index].ToString()));
                            continue;
                        }


                        if (value == '\\' && index + 2 < pattern.Length &&
                            pattern[index + 1] == '\\' && pattern[index + 2] is '*' or '?')
                        {
                            expression.Append(Regex.Escape("\\"));
                            index++;
                            continue;
                        }

                        if (value == '*')
                            expression.Append("[\\s\\S]*");
                        else if (value == '?')
                            expression.Append("[\\s\\S]");
                        else
                            expression.Append(Regex.Escape(value.ToString()));
                    }
                    expression.Append("\\z");
                    return new Regex(expression.ToString(), RegexOptions.Compiled | RegexOptions.CultureInvariant);
                }

                /// <summary>
                /// Compares two already-normalized string values using the requested collation contract.<br/>
                /// </summary>
                private int CompareSemantic(string left, string right, CultureInfo cultureInfo)
                    => ignoreCase && indexType == IndexType.CollationKey
                        ? cultureInfo.CompareInfo.Compare(left, right, CompareOptions.IgnoreCase)
                        : string.CompareOrdinal(left, right);
                public bool TryGetLibraDexIndexName(Builder builder, out string indexName)
                {
                    indexName = string.Empty;
                    if (!TryExtractPropPath(builder, out var propPath))
                        return false;

                    indexName = propPath;
                    return true;
                }

                /// <summary>
                /// Resolves the maintained LibraDex index for either a direct string property or this leaf's backend-neutral regex-value descriptor.<br/>
                /// The RecordStore resolver owns registry matching and deferred regex materialization because only the store knows which derived projections are maintained.<br/>
                /// </summary>
                /// <param name="builder">The condition builder that owns execution-scoped materialization.<br/></param>
                /// <param name="resolver">The bound RecordStore's maintained-projection resolver.<br/></param>
                /// <param name="indexName">The direct property path or maintained projection index name.<br/></param>
                /// <returns><c>true</c> when an exact maintained index identity is available; otherwise <c>false</c>.<br/></returns>
                public bool TryGetLibraDexIndexName(Builder builder, LibraDexDerivedSelectorResolver resolver, out string indexName)
                {
                    if (TryResolveCalculatedIndex(builder, resolver, out indexName))
                        return true;

                    if (TryGetLibraDexIndexName(builder, out indexName))
                        return true;

                    if (selectorExpression?.SelectorDescriptor is Selector<T>.StringOperationSelectorDescriptor
                        {
                            Operation: StringOperationKind.MatchValue,
                            Source: DirectPropertySelectorDescriptor directRegexSource,
                            IntegerOperand: >= 0 and <= int.MaxValue
                        } calculatedRegex &&
                        calculatedRegex.SecondIntegerOperand is null or 0)
                    {
                        var calculatedValues = MaterializeValues(builder, directRegexSource.PropertyPath);
                        var calculatedGroupAlwaysHasAValue = calculatedRegex.IntegerOperand == 0;
                        var calculatedCompatible = symbol switch
                        {
                            StringOperatorSymbolsEnum.Equal =>
                                calculatedValues.Length == 1 &&
                                (calculatedGroupAlwaysHasAValue || calculatedValues[0].Length != 0),
                            StringOperatorSymbolsEnum.NotEqual =>
                                calculatedValues.Length == 1 &&
                                (calculatedGroupAlwaysHasAValue || calculatedValues[0].Length == 0),
                            StringOperatorSymbolsEnum.In or StringOperatorSymbolsEnum.InSet =>
                                calculatedValues.Length != 0 &&
                                (calculatedGroupAlwaysHasAValue || calculatedValues.All(static value => value.Length != 0)),
                            StringOperatorSymbolsEnum.NotIn or StringOperatorSymbolsEnum.NotInSet =>
                                calculatedGroupAlwaysHasAValue ||
                                (calculatedValues.Length != 0 && calculatedValues.Any(static value => value.Length == 0)),
                            _ => false
                        };
                        if (calculatedCompatible)
                        {
                            indexName = directRegexSource.PropertyPath;
                            return true;
                        }
                    }

                    if (resolver is not null &&
                        TryGetBinaryStringSliceDescriptor(out var binarySlice) &&
                        IsNativeBinaryStringSliceOperationSupported() &&
                        resolver(builder, binarySlice, out indexName))
                    {
                        return true;
                    }

                    if (resolver is not null &&
                        TryGetNormalizedStringDescriptor(out var normalized) &&
                        resolver(builder, normalized, out indexName))
                    {
                        return true;
                    }

                    if (matchValue is null)
                        return false;

                    if (resolver is not null &&
                        resolver(builder, matchValue.Descriptor, out indexName))
                    {
                        return true;
                    }

                    indexName = string.Empty;
                    if (matchValue.Descriptor.OccurrenceNumber is not null and not 0 ||
                        matchValue.Descriptor.GroupNumber < 0 ||
                        !Condition<T>.TryExtractPropPath(matchValue.Descriptor.SourceSelector, out var propPath))
                    {
                        return false;
                    }

                    var values = MaterializeValues(builder, propPath);
                    var groupAlwaysHasAValue = matchValue.Descriptor.GroupNumber == 0;
                    var compatible = symbol switch
                    {
                        StringOperatorSymbolsEnum.Equal =>
                            values.Length == 1 &&
                            (groupAlwaysHasAValue || values[0].Length != 0),
                        StringOperatorSymbolsEnum.NotEqual =>
                            values.Length == 1 &&
                            (groupAlwaysHasAValue || values[0].Length == 0),
                        StringOperatorSymbolsEnum.In or StringOperatorSymbolsEnum.InSet =>
                            values.Length != 0 &&
                            (groupAlwaysHasAValue || values.All(static value => value.Length != 0)),
                        StringOperatorSymbolsEnum.NotIn or StringOperatorSymbolsEnum.NotInSet =>
                            groupAlwaysHasAValue ||
                            (values.Length != 0 && values.Any(static value => value.Length == 0)),
                        _ => false
                    };
                    if (!compatible)
                        return false;

                    indexName = propPath;
                    return true;
                }

                public bool TryBuildLibraDexCondition(Builder builder, CatalogIdentityGroupIndexes indexes, out LibraDexConditionEndCondition condition, out string indexName)
                {
                    condition = default!;
                    indexName = string.Empty;
                    if (indexes is null ||
                        !TryExtractPropPath(builder, out var propPath))
                    {
                        return false;
                    }
                    return TryBuildLibraDexCondition(builder, indexes, propPath, propPath, out condition, out indexName);
                }

                /// <summary>
                /// Builds a direct string leaf against a separately named physical index while retaining the original property path for deferred operand context.<br/>
                /// The caller must already have proven that the physical alias is a current partial index whose membership predicate is implied by the complete query.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="indexes">Bound LibraDex identity group.<br/></param>
                /// <param name="targetIndexName">Authorized physical partial-index name.<br/></param>
                /// <param name="condition">Translated LibraDex condition when successful.<br/></param>
                /// <param name="indexName">Physical index identity actually used.<br/></param>
                /// <returns><c>true</c> when the selector is direct and the target is a compatible string index; otherwise <c>false</c>.<br/></returns>
                public bool TryBuildLibraDexCondition(
                    Builder builder,
                    CatalogIdentityGroupIndexes indexes,
                    string targetIndexName,
                    out LibraDexConditionEndCondition condition,
                    out string indexName)
                {
                    condition = default!;
                    indexName = string.Empty;
                    if (indexes is null ||
                        string.IsNullOrWhiteSpace(targetIndexName) ||
                        !TryExtractPropPath(builder, out var propPath))
                    {
                        return false;
                    }

                    return TryBuildLibraDexCondition(
                        builder,
                        indexes,
                        propPath,
                        targetIndexName,
                        out condition,
                        out indexName);
                }

                /// <summary>
                /// Builds a LibraDex condition over a direct string index or an exactly resolved maintained regex projection.<br/>
                /// Source-path metadata remains attached to deferred comparison operands while all LibraDex operations target the resolved projection name.<br/>
                /// </summary>
                /// <param name="builder">The condition builder that owns execution-scoped materialization.<br/></param>
                /// <param name="indexes">The bound LibraDex identity group.<br/></param>
                /// <param name="resolver">The RecordStore's maintained derived-projection resolver.<br/></param>
                /// <param name="condition">The translated LibraDex condition when successful.<br/></param>
                /// <param name="indexName">The resolved maintained index name when successful.<br/></param>
                /// <returns><c>true</c> when the selector and operation are supported by the maintained index; otherwise <c>false</c>.<br/></returns>
                public bool TryBuildLibraDexCondition(Builder builder, CatalogIdentityGroupIndexes indexes, LibraDexDerivedSelectorResolver resolver, out LibraDexConditionEndCondition condition, out string indexName)
                {
                    condition = default!;
                    indexName = string.Empty;
                    if (indexes is null)
                        return false;

                    if (TryResolveCalculatedIndex(builder, resolver, out var calculatedIndexName) &&
                        TryBuildLibraDexCondition(
                            builder,
                            indexes,
                            calculatedIndexName,
                            calculatedIndexName,
                            out condition,
                            out indexName))
                    {
                        return true;
                    }

                    if (TryExtractPropPath(builder, out var directPropPath))
                        return TryBuildLibraDexCondition(builder, indexes, directPropPath, directPropPath, out condition, out indexName);

                    if (selectorExpression?.SelectorDescriptor is Selector<T>.StringOperationSelectorDescriptor
                        {
                            Operation: StringOperationKind.MatchValue,
                            Source: DirectPropertySelectorDescriptor directRegexSource
                        })
                    {
                        return TryBuildLibraDexRegexCaptureCondition(
                            builder,
                            indexes,
                            directRegexSource.PropertyPath,
                            out condition,
                            out indexName);
                    }

                    if (TryBuildBinaryStringSliceCondition(builder, indexes, resolver, out condition, out indexName))
                        return true;

                    if (TryGetNormalizedStringDescriptor(out var normalized) &&
                        normalized.TryGetSourcePropertyPath(out var normalizedSourcePath) &&
                        resolver(builder, normalized, out var normalizedIndexName))
                    {
                        return TryBuildLibraDexCondition(
                            builder,
                            indexes,
                            normalizedSourcePath,
                            normalizedIndexName,
                            normalize: true,
                            out condition,
                            out indexName);
                    }

                    if (
                        matchValue is null ||
                        !Condition<T>.TryExtractPropPath(matchValue.Descriptor.SourceSelector, out var propPath))
                    {
                        return false;
                    }

                    if (resolver(builder, matchValue.Descriptor, out var resolvedIndexName))
                        return TryBuildLibraDexCondition(builder, indexes, propPath, resolvedIndexName, out condition, out indexName);

                    return TryBuildLibraDexRegexCaptureCondition(builder, indexes, propPath, out condition, out indexName);
                }

                /// <summary>
                /// Resolves the complete immutable string selector to a separately maintained calculated index.<br/>
                /// Direct source-path results remain assigned to existing exact, normalized, folded, sort-key, binary-slice, or regex routes.<br/>
                /// </summary>
                /// <param name="builder">Builder owning the frozen selector and deferred operands.<br/></param>
                /// <param name="resolver">Store-owned maintained-projection resolver.<br/></param>
                /// <param name="indexName">Exact separately maintained calculated index when available.<br/></param>
                /// <returns><see langword="true"/> when the descriptor matches a non-source index exactly.<br/></returns>
                private bool TryResolveCalculatedIndex(
                    Builder builder,
                    LibraDexDerivedSelectorResolver resolver,
                    out string indexName)
                {
                    indexName = string.Empty;
                    IBackendNeutralSelectorDescriptor? descriptor = selectorExpression?.SelectorDescriptor;
                    if (resolver is null || descriptor is null || !resolver(builder, descriptor, out var resolved))
                        return false;

                    if (descriptor is not NamedIndexSelectorDescriptor &&
                        Condition<T>.TryGetConditionSourcePropPath(selectorExpression!, out var sourcePath) &&
                        string.Equals(resolved, sourcePath, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    indexName = resolved;
                    return true;
                }

                /// <summary>
                /// Builds one deterministic encoded-string slice predicate over the already-maintained exact source blob index.<br/>
                /// Case-folded, culture-aware, negative, membership, ordering, suffix, and regex operations remain semantic residuals because LibraDex's raw binary string-slice grammar does not represent those contracts exactly.<br/>
                /// </summary>
                /// <param name="builder">The condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="indexes">The bound LibraDex identity group.<br/></param>
                /// <param name="resolver">The store-owned resolver that proves source-index ownership.<br/></param>
                /// <param name="condition">The completed native LibraDex condition when translation succeeds.<br/></param>
                /// <param name="indexName">The exact source blob index used by the predicate.<br/></param>
                /// <returns><c>true</c> when the selector, operation, index, and operands have an exact native representation; otherwise <c>false</c>.<br/></returns>
                private bool TryBuildBinaryStringSliceCondition(
                    Builder builder,
                    CatalogIdentityGroupIndexes indexes,
                    LibraDexDerivedSelectorResolver resolver,
                    out LibraDexConditionEndCondition condition,
                    out string indexName)
                {
                    condition = default!;
                    indexName = string.Empty;
                    if (resolver is null ||
                        !TryGetBinaryStringSliceDescriptor(out var slice) ||
                        !IsNativeBinaryStringSliceOperationSupported() ||
                        !Condition<T>.TryExtractPropPath(slice.SourceSelector, out var sourcePath) ||
                        !resolver(builder, slice, out var resolvedIndexName) ||
                        !indexes.TryGetInfo(resolvedIndexName, out var info) ||
                        info.KeyFamily != CatalogIndexKeyFamily.Blob ||
                        info.IdentityFamily != CatalogIndexIdentityFamily.Scalar)
                    {
                        return false;
                    }

                    var values = MaterializeValues(builder, sourcePath);
                    if (values.Length != 1)
                        return false;

                    var binary = indexes.Where(resolvedIndexName).AsBinary;
                    LibraDexBinaryStringSliceConditionOperator? op = slice.SliceType switch
                    {
                        BinarySliceKind.Utf8String => binary.SlicedAsUtf8String((int)slice.Offset, (int)slice.Length, Coercion.Text.DotNetReplacement),
                        BinarySliceKind.Utf16String => binary.SlicedAsUtf16String((int)slice.Offset, (int)slice.Length, Coercion.Text.DotNetReplacement),
                        BinarySliceKind.Utf32String => binary.SlicedAsUtf32String((int)slice.Offset, (int)slice.Length, Coercion.Text.DotNetReplacement),
                        BinarySliceKind.AsciiString => binary.SlicedAsAsciiString((int)slice.Offset, (int)slice.Length, Coercion.Text.DotNetReplacement),
                        BinarySliceKind.Latin1String => binary.SlicedAsLatin1String((int)slice.Offset, (int)slice.Length, Coercion.Text.DotNetReplacement),
                        BinarySliceKind.CustomEncodingString when slice.StableEncoding is not null =>
                            binary.SlicedAsEncodedString(
                                (int)slice.Offset,
                                (int)slice.Length,
                                slice.StableEncoding),
                        BinarySliceKind.CharUtf16 => binary.SlicedAsCharUtf16((int)slice.Offset),
                        BinarySliceKind.RuneUtf32 => binary.SlicedAsRuneUtf32((int)slice.Offset),
                        _ => null
                    };
                    if (op is null)
                        return false;

                    condition = symbol switch
                    {
                        StringOperatorSymbolsEnum.Equal => op.EqualTo(values[0]).EndCondition,
                        StringOperatorSymbolsEnum.StartsWith => op.StartsWith(values[0]).EndCondition,
                        StringOperatorSymbolsEnum.Contains => op.Contains(values[0]).EndCondition,
                        _ => default!
                    };
                    if (condition is null)
                        return false;

                    indexName = resolvedIndexName;
                    return true;
                }

                /// <summary>
                /// Validates and returns a deterministic encoded-string binary-slice descriptor attached to this operation's selector.<br/>
                /// Bounds must fit LibraDex's Int32-addressed predicate surface, and the fixed char/rune families must retain their exact two- or four-byte widths.<br/>
                /// Caller-supplied encodings participate only when they have been normalized to LibraDex's stable code-page and coercion contract; opaque runtime encoding instances remain semantic-only.<br/>
                /// </summary>
                /// <param name="slice">The validated deterministic string slice descriptor when available.<br/></param>
                /// <returns><c>true</c> when this selector is a supported encoded-string, UTF-16 char, or UTF-32 rune slice; otherwise <c>false</c>.<br/></returns>
                private bool TryGetBinaryStringSliceDescriptor(out BinarySliceSelectorDescriptor slice)
                {
                    slice = selectorExpression?.SelectorDescriptor as BinarySliceSelectorDescriptor ?? null!;
                    if (slice is null ||
                        slice.Offset is < 0 or > int.MaxValue ||
                        slice.Length is <= 0 or > int.MaxValue)
                    {
                        return false;
                    }

                    if (slice.SliceType == BinarySliceKind.CustomEncodingString)
                        return slice.StableEncoding is not null;

                    if (!Condition<T>.TryGetDeterministicTextBinarySliceShape(
                            slice.SliceType,
                            out var fixedByteWidth))
                    {
                        return false;
                    }

                    return fixedByteWidth == 0 || slice.Length == fixedByteWidth;
                }

                /// <summary>
                /// Tries to identify a direct string source followed by exactly one canonical Form-C normalization operation.<br/>
                /// Chained transforms remain semantic residuals until LibraDex declares the same ordered operation chain explicitly.<br/>
                /// </summary>
                /// <param name="descriptor">Receives the backend-neutral normalization descriptor when present.<br/></param>
                /// <returns><c>true</c> when the condition selector is exactly one built-in normalization over a direct property; otherwise <c>false</c>.<br/></returns>
                private bool TryGetNormalizedStringDescriptor(out Selector<T>.StringOperationSelectorDescriptor descriptor)
                {
                    var candidate = selectorExpression?.SelectorDescriptor as Selector<T>.StringOperationSelectorDescriptor;
                    if (candidate is not null &&
                        candidate.Operation == StringOperationKind.Normalize &&
                        candidate.Source is DirectPropertySelectorDescriptor)
                    {
                        descriptor = candidate;
                        return true;
                    }

                    descriptor = default!;
                    return false;
                }

                /// <summary>
                /// Reports whether the current string operation has an exact positive LibraDex typed-slice representation.<br/>
                /// Native string slices are ordinal and case-sensitive; all complements and richer comparison profiles remain on the semantic evaluator.<br/>
                /// </summary>
                /// <returns><c>true</c> for case-sensitive equality, starts-with, and contains operations; otherwise <c>false</c>.<br/></returns>
                private bool IsNativeBinaryStringSliceOperationSupported()
                    => !ignoreCase &&
                       symbol is StringOperatorSymbolsEnum.Equal or
                           StringOperatorSymbolsEnum.StartsWith or
                           StringOperatorSymbolsEnum.Contains;

                /// <summary>
                /// Applies the shared string-operation translation to one resolved LibraDex string index.<br/>
                /// </summary>
                private bool TryBuildLibraDexCondition(
                    Builder builder,
                    CatalogIdentityGroupIndexes indexes,
                    string propPath,
                    string resolvedIndexName,
                    out LibraDexConditionEndCondition condition,
                    out string indexName)
                    => TryBuildLibraDexCondition(builder, indexes, propPath, resolvedIndexName, normalize: false, out condition, out indexName);

                /// <summary>
                /// Applies the shared string-operation translation to one resolved LibraDex string index with optional canonical selector normalization.<br/>
                /// </summary>
                /// <param name="builder">The condition builder owning execution-scoped operands.<br/></param>
                /// <param name="indexes">The bound LibraDex identity group.<br/></param>
                /// <param name="propPath">The original source property path used for deferred operand context.<br/></param>
                /// <param name="resolvedIndexName">The exact source index name selected by the store-owned resolver.<br/></param>
                /// <param name="normalize">Whether Form-C normalization occurs before the string operator.<br/></param>
                /// <param name="condition">The native LibraDex condition when translation succeeds.<br/></param>
                /// <param name="indexName">The exact source index name used by the translated condition.<br/></param>
                /// <returns><c>true</c> when LibraDex can preserve the requested semantics; otherwise <c>false</c>.<br/></returns>
                private bool TryBuildLibraDexCondition(
                    Builder builder,
                    CatalogIdentityGroupIndexes indexes,
                    string propPath,
                    string resolvedIndexName,
                    bool normalize,
                    out LibraDexConditionEndCondition condition,
                    out string indexName)
                {
                    condition = default!;
                    indexName = resolvedIndexName;

                    if (!indexes.TryGetInfo(resolvedIndexName, out var info) ||
                        info.KeyFamily != CatalogIndexKeyFamily.String)
                    {
                        return false;
                    }

                    // LibraDex owns physical text-route selection.  A compatible folded,
                    // sort-key, normalized, or reversed projection receives first refusal;
                    // when it is absent, LibraDex materializes the same semantic leaf as a
                    // residual scan over the complete exact source-key index.  Rejecting the
                    // leaf here would incorrectly bypass that compact maintained-index route
                    // and force Abraxas to deserialize every authoritative Fractal record.

                    var values = MaterializeValues(builder, propPath);
                    var op = indexes.Where(resolvedIndexName).AsString;
                    if (normalize)
                        op = op.Normalized;
                    LibraDexConditionContinueOrEnd IncludeEmpty(
                        LibraDexConditionContinueOrEnd source,
                        bool emptyMatches)
                        => emptyMatches
                            ? source.OR.Where(resolvedIndexName).AsString.IsEmpty()
                            : source;

                    switch (symbol)
                    {
                        case StringOperatorSymbolsEnum.Greater:
                            if (values.Length != 1)
                                return false;
                            condition = IncludeEmpty(
                                op.GreaterThan(values[0], ignoreCase, culture),
                                CompareSemantic(string.Empty, values[0], ResolveCulture(culture)) > 0)
                                .EndCondition;
                            return true;
                        case StringOperatorSymbolsEnum.GreaterOrEqual:
                            if (values.Length != 1)
                                return false;
                            condition = IncludeEmpty(
                                op.GreaterOrEqual(values[0], ignoreCase, culture),
                                CompareSemantic(string.Empty, values[0], ResolveCulture(culture)) >= 0)
                                .EndCondition;
                            return true;
                        case StringOperatorSymbolsEnum.Less:
                            if (values.Length != 1)
                                return false;
                            condition = IncludeEmpty(
                                op.LessThan(values[0], ignoreCase, culture),
                                CompareSemantic(string.Empty, values[0], ResolveCulture(culture)) < 0)
                                .EndCondition;
                            return true;
                        case StringOperatorSymbolsEnum.LessOrEqual:
                            if (values.Length != 1)
                                return false;
                            condition = IncludeEmpty(
                                op.LessOrEqual(values[0], ignoreCase, culture),
                                CompareSemantic(string.Empty, values[0], ResolveCulture(culture)) <= 0)
                                .EndCondition;
                            return true;
                        case StringOperatorSymbolsEnum.Equal:
                            if (values.Length != 1)
                                return false;
                            condition = op.EqualTo(values[0], ignoreCase, culture).EndCondition;
                            return true;
                        case StringOperatorSymbolsEnum.NotEqual:
                            if (values.Length != 1)
                                return false;
                            condition = IncludeEmpty(
                                op.NotEqualTo(values[0], ignoreCase, culture),
                                CompareSemantic(string.Empty, values[0], ResolveCulture(culture)) != 0)
                                .AND.Where(resolvedIndexName).AsString.IsNotNull()
                                .EndCondition;
                            return true;
                        case StringOperatorSymbolsEnum.IsNull:
                            condition = op.IsNull().EndCondition;
                            return true;
                        case StringOperatorSymbolsEnum.IsNotNull:
                            condition = op.IsNotNull().EndCondition;
                            return true;
                        case StringOperatorSymbolsEnum.In:
                        case StringOperatorSymbolsEnum.InSet:
                            condition = IncludeEmpty(
                                op.In(values, ignoreCase, culture),
                                values.Any(value => CompareSemantic(string.Empty, value, ResolveCulture(culture)) == 0))
                                .EndCondition;
                            return true;
                        case StringOperatorSymbolsEnum.NotIn:
                        case StringOperatorSymbolsEnum.NotInSet:
                            condition = IncludeEmpty(
                                op.NotIn(values, ignoreCase, culture),
                                values.All(value => CompareSemantic(string.Empty, value, ResolveCulture(culture)) != 0))
                                .AND.Where(resolvedIndexName).AsString.IsNotNull()
                                .EndCondition;
                            return true;
                        case StringOperatorSymbolsEnum.Between:
                            if (values.Length != 2)
                                return false;
                            condition = IncludeEmpty(
                                op.Between(values[0], values[1], ignoreCase, culture),
                                CompareSemantic(string.Empty, values[0], ResolveCulture(culture)) >= 0 &&
                                CompareSemantic(string.Empty, values[1], ResolveCulture(culture)) <= 0)
                                .EndCondition;
                            return true;
                        case StringOperatorSymbolsEnum.NotBetween:
                            if (values.Length != 2)
                                return false;
                            condition = IncludeEmpty(
                                op.NotBetween(values[0], values[1], ignoreCase, culture),
                                CompareSemantic(string.Empty, values[0], ResolveCulture(culture)) < 0 ||
                                CompareSemantic(string.Empty, values[1], ResolveCulture(culture)) > 0)
                                .AND.Where(resolvedIndexName).AsString.IsNotNull()
                                .EndCondition;
                            return true;
                        case StringOperatorSymbolsEnum.StartsWith:
                            if (values.Length != 1)
                                return false;
                            condition = IncludeEmpty(
                                ignoreCase && indexType == IndexType.FoldedText
                                    ? op.StartsWithFoldedOrdinal(values[0], culture)
                                    : op.StartsWith(values[0], ignoreCase, culture),
                                values[0].Length == 0)
                                .EndCondition;
                            return true;
                        case StringOperatorSymbolsEnum.EndsWith:
                            if (values.Length != 1)
                                return false;
                            condition = IncludeEmpty(
                                ignoreCase && indexType == IndexType.FoldedText
                                    ? op.EndsWithFoldedOrdinal(values[0], culture)
                                    : op.EndsWith(values[0], ignoreCase, culture),
                                values[0].Length == 0)
                                .EndCondition;
                            return true;
                        case StringOperatorSymbolsEnum.Contains:
                            if (values.Length != 1)
                                return false;
                            condition = IncludeEmpty(
                                ignoreCase && indexType == IndexType.FoldedText
                                    ? op.ContainsFoldedOrdinal(values[0], culture)
                                    : op.Contains(values[0], ignoreCase, culture),
                                values[0].Length == 0)
                                .EndCondition;
                            return true;
                        case StringOperatorSymbolsEnum.NotContains:
                            if (values.Length != 1)
                                return false;
                            condition = IncludeEmpty(
                                ignoreCase && indexType == IndexType.FoldedText
                                    ? op.NotContainsFoldedOrdinal(values[0], culture)
                                    : op.NotContains(values[0], ignoreCase, culture),
                                values[0].Length != 0)
                                .AND.Where(resolvedIndexName).AsString.IsNotNull()
                                .EndCondition;
                            return true;
                        case StringOperatorSymbolsEnum.Like:
                            if (values.Length != 1)
                                return false;
                            condition = IncludeEmpty(
                                ignoreCase && indexType == IndexType.FoldedText
                                    ? op.LikeFoldedOrdinal(values[0], culture)
                                    : op.Like(values[0], ignoreCase, culture),
                                CompileWildcardRegex(NormalizeForSemanticComparison(values[0], ResolveCulture(culture))).IsMatch(string.Empty))
                                .EndCondition;
                            return true;
                        case StringOperatorSymbolsEnum.NotLike:
                            if (values.Length != 1)
                                return false;
                            condition = IncludeEmpty(
                                ignoreCase && indexType == IndexType.FoldedText
                                    ? op.NotLikeFoldedOrdinal(values[0], culture)
                                    : op.NotLike(values[0], ignoreCase, culture),
                                !CompileWildcardRegex(NormalizeForSemanticComparison(values[0], ResolveCulture(culture))).IsMatch(string.Empty))
                                .AND.Where(resolvedIndexName).AsString.IsNotNull()
                                .EndCondition;
                            return true;
                        case StringOperatorSymbolsEnum.Matches:
                            if (values.Length != 1)
                                return false;
                            var matchesRegex = new Regex(values[0], ResolveRegexOptions(ignoreCase, culture));
                            condition = IncludeEmpty(
                                op.Matches(matchesRegex),
                                matchesRegex.IsMatch(string.Empty))
                                .EndCondition;
                            return true;
                        case StringOperatorSymbolsEnum.NotMatches:
                            if (values.Length != 1)
                                return false;
                            var notMatchesRegex = new Regex(values[0], ResolveRegexOptions(ignoreCase, culture));
                            condition = IncludeEmpty(
                                op.NotMatches(notMatchesRegex),
                                !notMatchesRegex.IsMatch(string.Empty))
                                .AND.Where(resolvedIndexName).AsString.IsNotNull()
                                .EndCondition;
                            return true;
                        default:
                            return false;
                    }
                }

                /// <summary>
                /// Builds a native LibraDex match-value condition over the direct source-string index when no exactly maintained regex projection exists.<br/>
                /// LibraDex's <c>MatchesWith</c> and <c>MatchesInSet</c> predicates evaluate the first regex match against decoded index keys, avoiding Fractal payload hydration while preserving the caller-provided regex options and timeout.<br/>
                /// Numbered groups require an additional compatibility guard because LibraDex represents an unmatched group as an empty capture while Abraxas represents it as a missing derived value.<br/>
                /// Negated comparisons also require an explicit positive regex-match conjunct because LibraDex's standalone negated predicates accept candidates for which the regex does not match, whereas Abraxas derived-value comparisons reject missing extractions.<br/>
                /// Empty source strings are restored through LibraDex's dedicated empty-key state only when direct semantic evaluation proves that the complete Abraxas expression accepts the empty source.<br/>
                /// Maintained regex projections remain preferred by the caller because their extracted keys support indexed lookup and the broader derived-value operator family.<br/>
                /// </summary>
                /// <param name="builder">The condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="indexes">The bound LibraDex identity group containing the direct source-string index.<br/></param>
                /// <param name="propPath">The canonical Abraxas property path and direct LibraDex index name.<br/></param>
                /// <param name="condition">The semantically equivalent native LibraDex condition when successful.<br/></param>
                /// <param name="indexName">The direct source index name when successful.<br/></param>
                /// <returns><see langword="true"/> when native match-value execution is exactly equivalent; otherwise <see langword="false"/> so the planner can retain semantic fallback.<br/></returns>
                private bool TryBuildLibraDexRegexCaptureCondition(
                    Builder builder,
                    CatalogIdentityGroupIndexes indexes,
                    string propPath,
                    out LibraDexConditionEndCondition condition,
                    out string indexName)
                {
                    condition = default!;
                    indexName = propPath;
                    Regex regex;
                    int groupNumber;
                    int? occurrenceNumber;
                    if (matchValue is not null)
                    {
                        regex = builder.MaterializeParameter(
                            matchValue.Descriptor.PatternExpression,
                            propPath,
                            owner: matchValue.Descriptor);
                        groupNumber = matchValue.Descriptor.GroupNumber;
                        occurrenceNumber = matchValue.Descriptor.OccurrenceNumber;
                    }
                    else if (selectorExpression?.SelectorDescriptor is Selector<T>.StringOperationSelectorDescriptor
                    {
                        Operation: StringOperationKind.MatchValue,
                        Source: DirectPropertySelectorDescriptor directSource,
                        TextOperand: not null,
                        IntegerOperand: >= 0 and <= int.MaxValue,
                        SecondIntegerOperand: null or (>= 0 and <= int.MaxValue)
                    } calculatedRegex &&
                        string.Equals(directSource.PropertyPath, propPath, StringComparison.Ordinal))
                    {
                        regex = Selector<T>.StringOperationSelectorDescriptor.CreateRuntimeRegex(
                            calculatedRegex.TextOperand,
                            calculatedRegex.MatchOptions,
                            TimeSpan.FromTicks(calculatedRegex.MatchTimeoutTicks));
                        groupNumber = checked((int)calculatedRegex.IntegerOperand.Value);
                        occurrenceNumber = calculatedRegex.SecondIntegerOperand is long occurrence
                            ? checked((int)occurrence)
                            : null;
                    }
                    else
                    {
                        return false;
                    }

                    if (occurrenceNumber is not null and not 0 ||
                        groupNumber < 0 ||
                        !indexes.TryGetInfo(propPath, out var info) ||
                        info.KeyFamily != CatalogIndexKeyFamily.String)
                    {
                        return false;
                    }

                    var values = MaterializeValues(builder, propPath);
                    var groupAlwaysHasAValue = groupNumber == 0;
                    var op = indexes.Where(propPath).AsString;

                    LibraDexConditionContinueOrEnd IncludeEmpty(
                        LibraDexConditionContinueOrEnd source,
                        bool emptyMatches)
                        => emptyMatches
                            ? source.OR.Where(propPath).AsString.IsEmpty()
                            : source;

                    bool EmptySourceMatches()
                    {
                        var match = regex.Match(string.Empty);
                        if (!match.Success ||
                            groupNumber >= match.Groups.Count ||
                            !match.Groups[groupNumber].Success)
                        {
                            return false;
                        }

                        var extracted = groupNumber == 0
                            ? match.Value
                            : match.Groups[groupNumber].Value;
                        var cultureInfo = ResolveCulture(culture);
                        var candidate = NormalizeForSemanticComparison(extracted, cultureInfo);
                        var normalizedValues = values
                            .Select(value => NormalizeForSemanticComparison(value, cultureInfo))
                            .ToArray();
                        var membership = new HashSet<string>(
                            normalizedValues,
                            ignoreCase && indexType == IndexType.CollationKey
                                ? StringComparer.Create(cultureInfo, ignoreCase: true)
                                : StringComparer.Ordinal);

                        return symbol switch
                        {
                            StringOperatorSymbolsEnum.Equal =>
                                normalizedValues.Length == 1 &&
                                CompareSemantic(candidate, normalizedValues[0], cultureInfo) == 0,
                            StringOperatorSymbolsEnum.NotEqual =>
                                normalizedValues.Length == 1 &&
                                CompareSemantic(candidate, normalizedValues[0], cultureInfo) != 0,
                            StringOperatorSymbolsEnum.In or StringOperatorSymbolsEnum.InSet =>
                                membership.Contains(candidate),
                            StringOperatorSymbolsEnum.NotIn or StringOperatorSymbolsEnum.NotInSet =>
                                !membership.Contains(candidate),
                            _ => false
                        };
                    }

                    switch (symbol)
                    {
                        case StringOperatorSymbolsEnum.Equal:
                            if (values.Length != 1 ||
                                (!groupAlwaysHasAValue && values[0].Length == 0))
                            {
                                return false;
                            }

                            condition = IncludeEmpty(
                                op.MatchesWith(regex, values[0], groupNumber, ignoreCase, culture),
                                EmptySourceMatches())
                                .EndCondition;
                            return true;

                        case StringOperatorSymbolsEnum.NotEqual:
                            if (values.Length != 1 ||
                                (!groupAlwaysHasAValue && values[0].Length != 0))
                            {
                                return false;
                            }

                            condition = IncludeEmpty(
                                op.Matches(regex)
                                    .AND.Where(propPath).AsString.NotMatchesWith(regex, values[0], groupNumber, ignoreCase, culture),
                                EmptySourceMatches())
                                .EndCondition;
                            return true;

                        case StringOperatorSymbolsEnum.In:
                        case StringOperatorSymbolsEnum.InSet:
                            if (values.Length == 0 ||
                                (!groupAlwaysHasAValue && values.Any(static value => value.Length == 0)))
                            {
                                return false;
                            }

                            condition = IncludeEmpty(
                                op.MatchesInSet(regex, values, groupNumber, ignoreCase, culture),
                                EmptySourceMatches())
                                .EndCondition;
                            return true;

                        case StringOperatorSymbolsEnum.NotIn:
                        case StringOperatorSymbolsEnum.NotInSet:
                            if (values.Length == 0)
                            {
                                if (!groupAlwaysHasAValue)
                                    return false;

                                condition = IncludeEmpty(op.Matches(regex), EmptySourceMatches()).EndCondition;
                                return true;
                            }

                            if (!groupAlwaysHasAValue && values.All(static value => value.Length != 0))
                                return false;

                            condition = IncludeEmpty(
                                op.Matches(regex)
                                    .AND.Where(propPath).AsString.NotMatchesInSet(regex, values, groupNumber, ignoreCase, culture),
                                EmptySourceMatches())
                                .EndCondition;
                            return true;

                        default:
                            return false;
                    }
                }

                private bool TryExtractPropPath(Builder builder, out string propPath)
                {
                    propPath = string.Empty;
                    if (matchValue is not null)
                        return false;

                    return Condition<T>.TryExtractPropPath(builder, selectorExpression, out propPath);
                }
            }

            #endregion


            /// <summary>
            /// Adds a prefix test against the selected string.<br/>
            /// </summary>
            /// <param name="valueOrExpression">Static, deferred, or parameter-bound comparison text.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case.<br/></param>
            /// <param name="culture">Culture name used by applicable case/collation handling; empty selects the invariant culture contract.<br/></param>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            /// <remarks>
            /// The case policy and culture are retained with the condition for execution and matching maintained projections.<br/>
            /// </remarks>
            public ContinueOrEndCondition StartsWith(ValueOrExpression<string> valueOrExpression, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.StartsWith, matchValue, culture, ignoreCase, FoldedIndexType(ignoreCase), valueOrExpression));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.StartsWith, selectorExpression, culture, ignoreCase, FoldedIndexType(ignoreCase), valueOrExpression));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds a suffix test against the selected string.<br/>
            /// </summary>
            /// <param name="valueOrExpression">Static, deferred, or parameter-bound comparison text.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case.<br/></param>
            /// <param name="culture">Culture name used by applicable case/collation handling; empty selects the invariant culture contract.<br/></param>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            /// <remarks>
            /// The case policy and culture are retained with the condition for execution and matching maintained projections.<br/>
            /// </remarks>
            public ContinueOrEndCondition EndsWith(ValueOrExpression<string> valueOrExpression, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.EndsWith, matchValue, culture, ignoreCase, FoldedIndexType(ignoreCase), valueOrExpression));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.EndsWith, selectorExpression, culture, ignoreCase, FoldedIndexType(ignoreCase), valueOrExpression));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds a substring test against the selected string.<br/>
            /// </summary>
            /// <param name="valueOrExpression">Static, deferred, or parameter-bound comparison text.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case.<br/></param>
            /// <param name="culture">Culture name used by applicable case/collation handling; empty selects the invariant culture contract.<br/></param>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            /// <remarks>
            /// The case policy and culture are retained with the condition for execution and matching maintained projections.<br/>
            /// </remarks>
            public ContinueOrEndCondition Contains(ValueOrExpression<string> valueOrExpression, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.Contains, matchValue, culture, ignoreCase, FoldedIndexType(ignoreCase), valueOrExpression));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.Contains, selectorExpression, culture, ignoreCase, FoldedIndexType(ignoreCase), valueOrExpression));
                return new ContinueOrEndCondition(builder);
            }

            /// <summary>
            /// Selects present strings that do not contain the supplied literal value.<br/>
            /// The value is not interpreted as wildcard or regular-expression syntax, and null or missing values remain excluded.<br/>
            /// </summary>
            /// <param name="valueOrExpression">The static, deferred, or parameter-bound literal substring to exclude.<br/></param>
            /// <param name="ignoreCase">Whether comparison should ignore case.<br/></param>
            /// <param name="culture">The optional culture name governing folded comparison.<br/></param>
            /// <returns>The continuation used to compose or complete the condition.<br/></returns>
            public ContinueOrEndCondition NotContains(ValueOrExpression<string> valueOrExpression, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.NotContains, matchValue, culture, ignoreCase, FoldedIndexType(ignoreCase), valueOrExpression));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.NotContains, selectorExpression, culture, ignoreCase, FoldedIndexType(ignoreCase), valueOrExpression));
                return new ContinueOrEndCondition(builder);
            }

            /// <summary>
            /// Selects present strings that match the supplied wildcard pattern.<br/>
            /// `*` matches zero or more characters and `?` matches exactly one; `\*` and `\?` select literal wildcard characters while ordinary backslashes remain literal.<br/>
            /// Simple shapes are reduced by LibraDex to exact, prefix, suffix, or containment primitives when an index is available; fallback compiles the wildcard once per execution plan.<br/>
            /// </summary>
            /// <param name="pattern">The static, deferred, or parameter-bound wildcard pattern.<br/></param>
            /// <param name="ignoreCase">Whether comparison should ignore case.<br/></param>
            /// <param name="culture">The optional culture name governing folded comparison.<br/></param>
            /// <returns>The continuation used to compose or complete the condition.<br/></returns>
            public ContinueOrEndCondition Like(ValueOrExpression<string> pattern, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.Like, matchValue, culture, ignoreCase, FoldedIndexType(ignoreCase), pattern));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.Like, selectorExpression, culture, ignoreCase, FoldedIndexType(ignoreCase), pattern));
                return new ContinueOrEndCondition(builder);
            }

            /// <summary>
            /// Selects present strings that do not match the supplied wildcard pattern.<br/>
            /// Null and missing values remain excluded, matching the other negated string operators.<br/>
            /// </summary>
            /// <param name="pattern">The static, deferred, or parameter-bound wildcard pattern to exclude.<br/></param>
            /// <param name="ignoreCase">Whether comparison should ignore case.<br/></param>
            /// <param name="culture">The optional culture name governing folded comparison.<br/></param>
            /// <returns>The continuation used to compose or complete the condition.<br/></returns>
            public ContinueOrEndCondition NotLike(ValueOrExpression<string> pattern, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.NotLike, matchValue, culture, ignoreCase, FoldedIndexType(ignoreCase), pattern));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.NotLike, selectorExpression, culture, ignoreCase, FoldedIndexType(ignoreCase), pattern));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds string equality using the requested case policy.<br/>
            /// </summary>
            /// <param name="valueOrExpression">Static, deferred, or parameter-bound comparison text.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case.<br/></param>
            /// <param name="culture">Culture name used by applicable case/collation handling; empty selects the invariant culture contract.<br/></param>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            /// <remarks>
            /// Case-sensitive comparisons use ordinal text semantics. Case-insensitive collation uses the specified culture, or invariant culture when omitted.<br/>
            /// </remarks>
            public ContinueOrEndCondition EqualTo(ValueOrExpression<string> valueOrExpression, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.Equal, matchValue, culture, ignoreCase, ComparisonIndexType(ignoreCase), valueOrExpression));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.Equal, selectorExpression, culture, ignoreCase, ComparisonIndexType(ignoreCase), valueOrExpression));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds a null test for the selected string.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            public ContinueOrEndCondition IsNull()
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.IsNull, matchValue, "", false, IndexType.Default, default(ValueOrExpression<string>[])));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.IsNull, selectorExpression, "", false, IndexType.Default, default(ValueOrExpression<string>[])));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds a non-null test for the selected string.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            public ContinueOrEndCondition IsNotNull()
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.IsNotNull, matchValue, "", false, IndexType.Default, default(ValueOrExpression<string>[])));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.IsNotNull, selectorExpression, "", false, IndexType.Default, default(ValueOrExpression<string>[])));
                return new ContinueOrEndCondition(builder);
            }

            /// <summary>
            /// Selects present strings whose logical length is zero.<br/>
            /// This is semantically equivalent to exact equality with <see cref="string.Empty"/> while exposing the intent directly and retaining LibraDex's dedicated empty-key route.<br/>
            /// Null and missing values remain excluded.<br/>
            /// </summary>
            /// <returns>The continuation used to compose or complete the condition.<br/></returns>
            public ContinueOrEndCondition IsEmpty() => EqualTo(string.Empty);

            /// <summary>
            /// Selects present strings whose logical length is greater than zero.<br/>
            /// This is semantically equivalent to exact inequality with <see cref="string.Empty"/> and preserves the established exclusion of null and missing values.<br/>
            /// </summary>
            /// <returns>The continuation used to compose or complete the condition.<br/></returns>
            public ContinueOrEndCondition IsNotEmpty() => NotEqualTo(string.Empty);
            /// <summary>
            /// Adds string inequality using the requested case policy.<br/>
            /// </summary>
            /// <param name="valueOrExpression">Static, deferred, or parameter-bound comparison text.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case.<br/></param>
            /// <param name="culture">Culture name used by applicable case/collation handling; empty selects the invariant culture contract.<br/></param>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            /// <remarks>
            /// Case-sensitive comparisons use ordinal text semantics. Case-insensitive collation uses the specified culture, or invariant culture when omitted.<br/>
            /// </remarks>
            public ContinueOrEndCondition NotEqualTo(ValueOrExpression<string> valueOrExpression, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.NotEqual, matchValue, culture, ignoreCase, ComparisonIndexType(ignoreCase), valueOrExpression));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.NotEqual, selectorExpression, culture, ignoreCase, ComparisonIndexType(ignoreCase), valueOrExpression));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds an inclusive lower string-order bound.<br/>
            /// </summary>
            /// <param name="valueOrExpression">Static, deferred, or parameter-bound comparison text.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case.<br/></param>
            /// <param name="culture">Culture name used by applicable case/collation handling; empty selects the invariant culture contract.<br/></param>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            /// <remarks>
            /// Case-sensitive comparisons use ordinal text semantics. Case-insensitive collation uses the specified culture, or invariant culture when omitted.<br/>
            /// </remarks>
            public ContinueOrEndCondition AtOrAfter(ValueOrExpression<string> valueOrExpression, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.GreaterOrEqual, matchValue, culture, ignoreCase, ComparisonIndexType(ignoreCase), valueOrExpression));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.GreaterOrEqual, selectorExpression, culture, ignoreCase, ComparisonIndexType(ignoreCase), valueOrExpression));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds an inclusive upper string-order bound.<br/>
            /// </summary>
            /// <param name="valueOrExpression">Static, deferred, or parameter-bound comparison text.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case.<br/></param>
            /// <param name="culture">Culture name used by applicable case/collation handling; empty selects the invariant culture contract.<br/></param>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            /// <remarks>
            /// Case-sensitive comparisons use ordinal text semantics. Case-insensitive collation uses the specified culture, or invariant culture when omitted.<br/>
            /// </remarks>
            public ContinueOrEndCondition AtOrBefore(ValueOrExpression<string> valueOrExpression, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.LessOrEqual, matchValue, culture, ignoreCase, ComparisonIndexType(ignoreCase), valueOrExpression));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.LessOrEqual, selectorExpression, culture, ignoreCase, ComparisonIndexType(ignoreCase), valueOrExpression));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds a strict lower string-order bound.<br/>
            /// </summary>
            /// <param name="valueOrExpression">Static, deferred, or parameter-bound comparison text.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case.<br/></param>
            /// <param name="culture">Culture name used by applicable case/collation handling; empty selects the invariant culture contract.<br/></param>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            /// <remarks>
            /// Case-sensitive comparisons use ordinal text semantics. Case-insensitive collation uses the specified culture, or invariant culture when omitted.<br/>
            /// </remarks>
            public ContinueOrEndCondition After(ValueOrExpression<string> valueOrExpression, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.Greater, matchValue, culture, ignoreCase, ComparisonIndexType(ignoreCase), valueOrExpression));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.Greater, selectorExpression, culture, ignoreCase, ComparisonIndexType(ignoreCase), valueOrExpression));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds a strict upper string-order bound.<br/>
            /// </summary>
            /// <param name="valueOrExpression">Static, deferred, or parameter-bound comparison text.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case.<br/></param>
            /// <param name="culture">Culture name used by applicable case/collation handling; empty selects the invariant culture contract.<br/></param>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            /// <remarks>
            /// Case-sensitive comparisons use ordinal text semantics. Case-insensitive collation uses the specified culture, or invariant culture when omitted.<br/>
            /// </remarks>
            public ContinueOrEndCondition Before(ValueOrExpression<string> valueOrExpression, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.Less, matchValue, culture, ignoreCase, ComparisonIndexType(ignoreCase), valueOrExpression));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.Less, selectorExpression, culture, ignoreCase, ComparisonIndexType(ignoreCase), valueOrExpression));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds an inclusive lower-and-upper string-order bound.<br/>
            /// </summary>
            /// <param name="startValue">Inclusive lower string bound.<br/></param>
            /// <param name="endValue">Inclusive upper string bound.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case.<br/></param>
            /// <param name="culture">Culture name used by applicable case/collation handling; empty selects the invariant culture contract.<br/></param>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            /// <remarks>
            /// Case-sensitive comparisons use ordinal text semantics. Case-insensitive collation uses the specified culture, or invariant culture when omitted.<br/>
            /// </remarks>
            public ContinueOrEndCondition Between(ValueOrExpression<string> startValue, ValueOrExpression<string> endValue, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.Between, matchValue, culture, ignoreCase, ComparisonIndexType(ignoreCase), startValue, endValue));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.Between, selectorExpression, culture, ignoreCase, ComparisonIndexType(ignoreCase), startValue, endValue));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds a .NET regular-expression match test.<br/>
            /// </summary>
            /// <param name="pattern">Static, deferred, or parameter-bound .NET regular-expression pattern; not a glob.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case.<br/></param>
            /// <param name="culture">Culture name used by applicable case/collation handling; empty selects the invariant culture contract.<br/></param>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            /// <remarks>
            /// An empty culture enables CultureInvariant regex matching. An explicit culture retains managed Regex behavior; Regex does not accept an arbitrary CultureInfo constructor argument.<br/>
            /// </remarks>
            public ContinueOrEndCondition MatchesPattern(ValueOrExpression<string> pattern, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.Matches, matchValue, culture, ignoreCase, IndexType.Default, pattern));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.Matches, selectorExpression, culture, ignoreCase, IndexType.Default, pattern));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds the negation of a .NET regular-expression match test.<br/>
            /// </summary>
            /// <param name="pattern">Static, deferred, or parameter-bound .NET regular-expression pattern; not a glob.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case.<br/></param>
            /// <param name="culture">Culture name used by applicable case/collation handling; empty selects the invariant culture contract.<br/></param>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            /// <remarks>
            /// An empty culture enables CultureInvariant regex matching. An explicit culture retains managed Regex behavior; Regex does not accept an arbitrary CultureInfo constructor argument.<br/>
            /// </remarks>
            public ContinueOrEndCondition NotMatchesPattern(ValueOrExpression<string> pattern, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.NotMatches, matchValue, culture, ignoreCase, IndexType.Default, pattern));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.NotMatches, selectorExpression, culture, ignoreCase, IndexType.Default, pattern));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Selects the whole-match or numbered capture value produced by a regular expression so subsequent string operators compare the calculated value.<br/>
            /// Deferred patterns are materialized once per condition execution and retain the caller-provided <see cref="Regex"/> instance.<br/>
            /// </summary>
            /// <param name="pattern">Static, deferred, or parameter-bound regular expression.<br/></param>
            /// <param name="groupNumber">Zero for the whole match or a positive capture-group number.<br/></param>
            /// <param name="occurrenceNumber">Optional zero-based non-overlapping match occurrence; the first match is used when omitted.<br/></param>
            /// <returns>A string operator over the selected match value.<br/></returns>
            public StringOperator MatchValue(ValueOrExpression<Regex> pattern, int groupNumber = 0, int? occurrenceNumber = null)
            {
                return new StringOperator(builder, new MatchValueOperation(selectorExpression, pattern, groupNumber, occurrenceNumber));
            }

            /// <summary>
            /// Selects a named capture value from a caller-provided regular expression so subsequent string operators retain capture intent without a magic group number.<br/>
            /// The name is validated immediately and converted to the pattern's stable numeric group identity used by semantic evaluation and LibraDex projection matching.<br/>
            /// </summary>
            /// <param name="pattern">Caller-owned regular expression whose options and execution behavior are retained.<br/></param>
            /// <param name="groupName">Non-empty capture-group name declared by <paramref name="pattern"/>.<br/></param>
            /// <param name="occurrenceNumber">Optional zero-based non-overlapping match occurrence; the first match is used when omitted.<br/></param>
            /// <returns>A string operator over the selected named capture value.<br/></returns>
            public StringOperator MatchValue(Regex pattern, string groupName, int? occurrenceNumber = null)
            {
                ArgumentNullException.ThrowIfNull(pattern);
                return MatchValue(pattern, ResolveMatchGroupNumber(pattern, groupName), occurrenceNumber);
            }

            /// <summary>
            /// Compiles a string pattern once and selects its whole-match or numbered capture value for subsequent string comparison.<br/>
            /// Abraxas adds <see cref="RegexOptions.Compiled"/> to the requested options so repeated record evaluation does not repeatedly parse the pattern.<br/>
            /// </summary>
            /// <param name="pattern">Regular-expression pattern text.<br/></param>
            /// <param name="groupNumber">Zero for the whole match or a positive capture-group number.<br/></param>
            /// <param name="occurrenceNumber">Optional zero-based non-overlapping match occurrence; the first match is used when omitted.<br/></param>
            /// <param name="options">Regular-expression options; culture-invariant compiled execution is the default.<br/></param>
            /// <returns>A string operator over the selected match value.<br/></returns>
            public StringOperator MatchValue(string pattern, int groupNumber = 0, int? occurrenceNumber = null, RegexOptions options = RegexOptions.CultureInvariant)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
                return MatchValue(new Regex(pattern, options | RegexOptions.Compiled), groupNumber, occurrenceNumber);
            }

            /// <summary>
            /// Compiles a string pattern once and selects one named capture value for subsequent string comparison.<br/>
            /// The capture name remains the discoverable developer input while execution uses the validated numeric group identity.<br/>
            /// </summary>
            /// <param name="pattern">Regular-expression pattern text.<br/></param>
            /// <param name="groupName">Non-empty capture-group name declared by the compiled pattern.<br/></param>
            /// <param name="occurrenceNumber">Optional zero-based non-overlapping match occurrence; the first match is used when omitted.<br/></param>
            /// <param name="options">Regular-expression options; culture-invariant compiled execution is the default.<br/></param>
            /// <returns>A string operator over the selected named capture value.<br/></returns>
            public StringOperator MatchValue(string pattern, string groupName, int? occurrenceNumber = null, RegexOptions options = RegexOptions.CultureInvariant)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
                var compiled = new Regex(pattern, options | RegexOptions.Compiled);
                return MatchValue(compiled, ResolveMatchGroupNumber(compiled, groupName), occurrenceNumber);
            }

            private static int ResolveMatchGroupNumber(Regex pattern, string groupName)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(groupName);
                var groupNumber = pattern.GroupNumberFromName(groupName);
                if (groupNumber < 0)
                    throw new ArgumentException($"Capture group '{groupName}' is not declared by the supplied pattern.", nameof(groupName));

                return groupNumber;
            }
            /// <summary>
            /// Adds string membership in the supplied candidates.<br/>
            /// </summary>
            /// <param name="theseValues">Candidate strings for membership testing.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case.<br/></param>
            /// <param name="culture">Culture name used by applicable case/collation handling; empty selects the invariant culture contract.<br/></param>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            /// <remarks>
            /// Case-sensitive comparisons use ordinal text semantics. Case-insensitive collation uses the specified culture, or invariant culture when omitted.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsIn(IEnumerable<string> theseValues, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.In, matchValue, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValues.Select(x => (ValueOrExpression<string>)x).ToArray()));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.In, selectorExpression, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValues.Select(x => (ValueOrExpression<string>)x).ToArray()));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds string membership in the supplied candidates.<br/>
            /// </summary>
            /// <param name="theseValuesDeferred">Factory-backed candidate strings for execution-time materialization. Factories are resolved during execution, not while authoring the predicate.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case.<br/></param>
            /// <param name="culture">Culture name used by applicable case/collation handling; empty selects the invariant culture contract.<br/></param>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            /// <remarks>
            /// Case-sensitive comparisons use ordinal text semantics. Case-insensitive collation uses the specified culture, or invariant culture when omitted.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsIn(IEnumerable<Func<string>> theseValuesDeferred, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.In, matchValue, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValuesDeferred.Select(x => (ValueOrExpression<string>)x).ToArray()));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.In, selectorExpression, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValuesDeferred.Select(x => (ValueOrExpression<string>)x).ToArray()));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds string membership in the supplied candidates.<br/>
            /// </summary>
            /// <param name="theseValuesDeferred">Factory-backed candidate strings for execution-time materialization. Factories are resolved during execution, not while authoring the predicate.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case.<br/></param>
            /// <param name="culture">Culture name used by applicable case/collation handling; empty selects the invariant culture contract.<br/></param>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            /// <remarks>
            /// Case-sensitive comparisons use ordinal text semantics. Case-insensitive collation uses the specified culture, or invariant culture when omitted.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsIn(Func<IEnumerable<string>> theseValuesDeferred, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.In, matchValue, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValuesDeferred));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.In, selectorExpression, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValuesDeferred));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds reusable string membership using one execution-frozen deferred set.<br/>
            /// Culture and case behavior are applied after the parameter contents have been captured for the current execution.<br/>
            /// </summary>
            /// <param name="theseValues">Typed set parameter created by <see cref="RecordStore{T}.Parameter{TValue}(IEnumerable{TValue}, string?)"/>.<br/></param>
            /// <param name="ignoreCase">Whether comparison ignores case.<br/></param>
            /// <param name="culture">Optional comparison culture name; an empty value retains the existing invariant behavior.<br/></param>
            /// <returns>The fluent continuation for combining or completing the condition.<br/></returns>
            public ContinueOrEndCondition IsIn(ConditionParameter<IEnumerable<string>> theseValues, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.In, matchValue, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValues));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.In, selectorExpression, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValues));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds string non-membership in the supplied candidates.<br/>
            /// </summary>
            /// <param name="theseValues">Candidate strings for membership testing.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case.<br/></param>
            /// <param name="culture">Culture name used by applicable case/collation handling; empty selects the invariant culture contract.<br/></param>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            /// <remarks>
            /// Case-sensitive comparisons use ordinal text semantics. Case-insensitive collation uses the specified culture, or invariant culture when omitted.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsNotIn(IEnumerable<string> theseValues, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.NotIn, matchValue, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValues.Select(x => (ValueOrExpression<string>)x).ToArray()));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.NotIn, selectorExpression, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValues.Select(x => (ValueOrExpression<string>)x).ToArray()));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds string non-membership in the supplied candidates.<br/>
            /// </summary>
            /// <param name="theseValuesDeferred">Factory-backed candidate strings for execution-time materialization. Factories are resolved during execution, not while authoring the predicate.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case.<br/></param>
            /// <param name="culture">Culture name used by applicable case/collation handling; empty selects the invariant culture contract.<br/></param>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            /// <remarks>
            /// Case-sensitive comparisons use ordinal text semantics. Case-insensitive collation uses the specified culture, or invariant culture when omitted.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsNotIn(IEnumerable<Func<string>> theseValuesDeferred, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.NotIn, matchValue, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValuesDeferred.Select(x => (ValueOrExpression<string>)x).ToArray()));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.NotIn, selectorExpression, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValuesDeferred.Select(x => (ValueOrExpression<string>)x).ToArray()));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds string non-membership in the supplied candidates.<br/>
            /// </summary>
            /// <param name="theseValuesDeferred">Factory-backed candidate strings for execution-time materialization. Factories are resolved during execution, not while authoring the predicate.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case.<br/></param>
            /// <param name="culture">Culture name used by applicable case/collation handling; empty selects the invariant culture contract.<br/></param>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            /// <remarks>
            /// Case-sensitive comparisons use ordinal text semantics. Case-insensitive collation uses the specified culture, or invariant culture when omitted.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsNotIn(Func<IEnumerable<string>> theseValuesDeferred, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.NotIn, matchValue, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValuesDeferred));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.NotIn, selectorExpression, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValuesDeferred));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds reusable negated string membership using one execution-frozen deferred set.<br/>
            /// Missing and null values remain excluded while culture and case behavior match the ordinary string membership family.<br/>
            /// </summary>
            /// <param name="theseValues">Typed set parameter created by <see cref="RecordStore{T}.Parameter{TValue}(IEnumerable{TValue}, string?)"/>.<br/></param>
            /// <param name="ignoreCase">Whether comparison ignores case.<br/></param>
            /// <param name="culture">Optional comparison culture name; an empty value retains the existing invariant behavior.<br/></param>
            /// <returns>The fluent continuation for combining or completing the condition.<br/></returns>
            public ContinueOrEndCondition IsNotIn(ConditionParameter<IEnumerable<string>> theseValues, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.NotIn, matchValue, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValues));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.NotIn, selectorExpression, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValues));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds set-based membership in the supplied string candidates.<br/>
            /// </summary>
            /// <param name="theseValues">Candidate strings for membership testing.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case.<br/></param>
            /// <param name="culture">Culture name used by applicable case/collation handling; empty selects the invariant culture contract.<br/></param>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            /// <remarks>
            /// Case-sensitive comparisons use ordinal text semantics. Case-insensitive collation uses the specified culture, or invariant culture when omitted.<br/>
            /// </remarks>
            public ContinueOrEndCondition InSet(IEnumerable<string> theseValues, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.InSet, matchValue, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValues.Select(x => (ValueOrExpression<string>)x).ToArray()));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.InSet, selectorExpression, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValues.Select(x => (ValueOrExpression<string>)x).ToArray()));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds set-based membership in the supplied string candidates.<br/>
            /// </summary>
            /// <param name="theseValues">Candidate strings for membership testing.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case.<br/></param>
            /// <param name="culture">Culture name used by applicable case/collation handling; empty selects the invariant culture contract.<br/></param>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            /// <remarks>
            /// Case-sensitive comparisons use ordinal text semantics. Case-insensitive collation uses the specified culture, or invariant culture when omitted.<br/>
            /// </remarks>
            public ContinueOrEndCondition InSet(HashSet<string> theseValues, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.InSet, matchValue, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValues));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.InSet, selectorExpression, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValues));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds set-based membership in the supplied string candidates.<br/>
            /// </summary>
            /// <param name="theseValues">Candidate strings for membership testing. Factories are resolved during execution, not while authoring the predicate.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case.<br/></param>
            /// <param name="culture">Culture name used by applicable case/collation handling; empty selects the invariant culture contract.<br/></param>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            /// <remarks>
            /// Case-sensitive comparisons use ordinal text semantics. Case-insensitive collation uses the specified culture, or invariant culture when omitted.<br/>
            /// </remarks>
            public ContinueOrEndCondition InSet(Func<HashSet<string>> theseValues, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.InSet, matchValue, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValues));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.InSet, selectorExpression, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValues));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds set-based non-membership in the supplied string candidates.<br/>
            /// </summary>
            /// <param name="theseValues">Candidate strings for membership testing.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case.<br/></param>
            /// <param name="culture">Culture name used by applicable case/collation handling; empty selects the invariant culture contract.<br/></param>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            /// <remarks>
            /// Case-sensitive comparisons use ordinal text semantics. Case-insensitive collation uses the specified culture, or invariant culture when omitted.<br/>
            /// </remarks>
            public ContinueOrEndCondition NotInSet(IEnumerable<string> theseValues, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.NotInSet, matchValue, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValues.Select(x => (ValueOrExpression<string>)x).ToArray()));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.NotInSet, selectorExpression, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValues.Select(x => (ValueOrExpression<string>)x).ToArray()));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds set-based non-membership in the supplied string candidates.<br/>
            /// </summary>
            /// <param name="theseValues">Candidate strings for membership testing.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case.<br/></param>
            /// <param name="culture">Culture name used by applicable case/collation handling; empty selects the invariant culture contract.<br/></param>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            /// <remarks>
            /// Case-sensitive comparisons use ordinal text semantics. Case-insensitive collation uses the specified culture, or invariant culture when omitted.<br/>
            /// </remarks>
            public ContinueOrEndCondition NotInSet(HashSet<string> theseValues, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.NotInSet, matchValue, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValues));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.NotInSet, selectorExpression, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValues));
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds set-based non-membership in the supplied string candidates.<br/>
            /// </summary>
            /// <param name="theseValues">Candidate strings for membership testing. Factories are resolved during execution, not while authoring the predicate.<br/></param>
            /// <param name="ignoreCase">Whether matching ignores case.<br/></param>
            /// <param name="culture">Culture name used by applicable case/collation handling; empty selects the invariant culture contract.<br/></param>
            /// <returns>A fluent continuation over the same builder; no records are read by this call.<br/></returns>
            /// <remarks>
            /// Case-sensitive comparisons use ordinal text semantics. Case-insensitive collation uses the specified culture, or invariant culture when omitted.<br/>
            /// </remarks>
            public ContinueOrEndCondition NotInSet(Func<HashSet<string>> theseValues, bool ignoreCase = false, string culture = "")
            {
                if (selectorExpression is null)
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.NotInSet, matchValue, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValues));
                else
                    builder.AddFragment(new StringOperation(StringOperatorSymbolsEnum.NotInSet, selectorExpression, culture, ignoreCase, ComparisonIndexType(ignoreCase), theseValues));
                return new ContinueOrEndCondition(builder);
            }

            /// <summary>Compares this string property for equality with a textual right-hand property completed by <c>.AsString</c>.<br/>Optional collation settings apply symmetrically to both property values.<br/></summary>
            public StringPropertyComparisonTypeSelector PropEqualTo(string propPath, bool ignoreCase = false, string culture = "")
                => new(builder, selectorExpression, propPath, PropertyComparisonKind.Equal, culture, ignoreCase);
            /// <summary>Compares this string property for equality with a typed right-hand property using the requested collation.<br/></summary>
            public ContinueOrEndCondition PropEqualTo(Expression<Func<T, string>> propSelector, bool ignoreCase = false, string culture = "")
                => AddPropertyComparison<string>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.Equal, culture, ignoreCase);
            /// <summary>Compares this string property for inequality with a textual right-hand property completed by <c>.AsString</c>.<br/></summary>
            public StringPropertyComparisonTypeSelector PropNotEqualTo(string propPath, bool ignoreCase = false, string culture = "")
                => new(builder, selectorExpression, propPath, PropertyComparisonKind.NotEqual, culture, ignoreCase);
            /// <summary>Compares this string property for inequality with a typed right-hand property using the requested collation.<br/></summary>
            public ContinueOrEndCondition PropNotEqualTo(Expression<Func<T, string>> propSelector, bool ignoreCase = false, string culture = "")
                => AddPropertyComparison<string>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.NotEqual, culture, ignoreCase);
            /// <summary>Compares whether this string property sorts after a textual right-hand property completed by <c>.AsString</c>.<br/></summary>
            public StringPropertyComparisonTypeSelector PropGreaterThan(string propPath, bool ignoreCase = false, string culture = "")
                => new(builder, selectorExpression, propPath, PropertyComparisonKind.Greater, culture, ignoreCase);
            /// <summary>Compares whether this string property sorts after a typed right-hand property using the requested collation.<br/></summary>
            public ContinueOrEndCondition PropGreaterThan(Expression<Func<T, string>> propSelector, bool ignoreCase = false, string culture = "")
                => AddPropertyComparison<string>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.Greater, culture, ignoreCase);
            /// <summary>Compares whether this string property sorts at or after a textual right-hand property completed by <c>.AsString</c>.<br/></summary>
            public StringPropertyComparisonTypeSelector PropGreaterOrEqual(string propPath, bool ignoreCase = false, string culture = "")
                => new(builder, selectorExpression, propPath, PropertyComparisonKind.GreaterOrEqual, culture, ignoreCase);
            /// <summary>Compares whether this string property sorts at or after a typed right-hand property using the requested collation.<br/></summary>
            public ContinueOrEndCondition PropGreaterOrEqual(Expression<Func<T, string>> propSelector, bool ignoreCase = false, string culture = "")
                => AddPropertyComparison<string>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.GreaterOrEqual, culture, ignoreCase);
            /// <summary>Compares whether this string property sorts before a textual right-hand property completed by <c>.AsString</c>.<br/></summary>
            public StringPropertyComparisonTypeSelector PropLessThan(string propPath, bool ignoreCase = false, string culture = "")
                => new(builder, selectorExpression, propPath, PropertyComparisonKind.Less, culture, ignoreCase);
            /// <summary>Compares whether this string property sorts before a typed right-hand property using the requested collation.<br/></summary>
            public ContinueOrEndCondition PropLessThan(Expression<Func<T, string>> propSelector, bool ignoreCase = false, string culture = "")
                => AddPropertyComparison<string>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.Less, culture, ignoreCase);
            /// <summary>Compares whether this string property sorts at or before a textual right-hand property completed by <c>.AsString</c>.<br/></summary>
            public StringPropertyComparisonTypeSelector PropLessOrEqual(string propPath, bool ignoreCase = false, string culture = "")
                => new(builder, selectorExpression, propPath, PropertyComparisonKind.LessOrEqual, culture, ignoreCase);
            /// <summary>Compares whether this string property sorts at or before a typed right-hand property using the requested collation.<br/></summary>
            public ContinueOrEndCondition PropLessOrEqual(Expression<Func<T, string>> propSelector, bool ignoreCase = false, string culture = "")
                => AddPropertyComparison<string>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.LessOrEqual, culture, ignoreCase);

        }

        #endregion
    }
}
