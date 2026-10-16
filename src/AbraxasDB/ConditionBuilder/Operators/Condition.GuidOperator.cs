using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using Inheto;
using LibraDex;

namespace AbraxasDB
{

    public static partial class Condition<T>
    {
        #region Operators

        /// <summary>
        /// Fluent Guid condition operations over a selected property or calculated value.<br/>
        /// </summary>
        /// <remarks>
        /// Condition authoring is separate from execution; mutable builder instances should not be edited concurrently.<br/>
        /// </remarks>
        public class GuidOperator
        {
            private readonly Builder builder;
            private readonly ValueOrExpression<string> selectorExpression;

            #region Operations

            internal class GuidCompareOperation : IConditionLeaf,IFastPredicateSource,ILibraDexDerivedConditionSource
            {
                private ValueOrExpression<string> selector;
                private IValueOrExpression param;
                private GuidComparisonKind compareType;

                public GuidCompareOperation(ValueOrExpression<string> selector, IValueOrExpression param, GuidComparisonKind compareType)
                {
                    this.selector = selector;
                    this.param = param;
                    this.compareType = compareType;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, IValueOrExpression param, GuidComparisonKind compareType)
                {
                    builder.AddFragment(new GuidCompareOperation(selector, param, compareType));
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;

                    if (compareType is GuidComparisonKind.Equal or GuidComparisonKind.NotEqual)
                    {
                        if (!TryGetGuidValue(builder, this, param, propPath, out var target))
                            return false;

                        evaluator = ib =>
                        {
                            if (!TryReadConditionValue<Guid>(selector, propPath, ib, options, out var candidate)) return false;
                            var equal = candidate == target;
                            return compareType == GuidComparisonKind.Equal ? equal : !equal;
                        };
                        return true;
                    }

                    if (!TryBuildGuidPatternMask(builder, this, param, propPath, compareType, out var mask, out var targetBits))
                        return false;

                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<Guid>(selector, propPath, ib, options, out var candidate)) return false;
                        Span<byte> bytes = stackalloc byte[16];
                        if (!candidate.TryWriteBytes(bytes))
                            return false;

                        var candidateBits = MemoryMarshal.Read<UInt128>(bytes);
                        return (candidateBits & mask) == targetBits;
                    };
                    return true;
                }

                public bool TryGetLibraDexIndexName(Builder builder, out string indexName)
                {
                    indexName = string.Empty;
                    if (!TryExtractPropPath(builder, selector, out var propPath))
                        return false;

                    indexName = propPath;
                    return true;
                }

                /// <summary>
                /// Resolves the maintained index required by a direct Guid comparison or an exact derived Guid slice.<br/>
                /// Derived adoption is limited to positive equality because missing or undersized source blobs must remain non-matches rather than enter through complement semantics.<br/>
                /// </summary>
                /// <param name="builder">The condition builder that owns execution-scoped selector materialization.<br/></param>
                /// <param name="resolver">The store-owned resolver that proves a compatible source blob index is maintained.<br/></param>
                /// <param name="indexName">The direct Guid index or resolved source blob index when successful.<br/></param>
                /// <returns><c>true</c> when this operation can report an exact maintained index; otherwise <c>false</c>.<br/></returns>
                public bool TryGetLibraDexIndexName(
                    Builder builder,
                    LibraDexDerivedSelectorResolver resolver,
                    out string indexName)
                {
                    if (TryResolveCalculatedIndex(builder, resolver, out indexName))
                        return true;

                    if (TryGetLibraDexIndexName(builder, out indexName))
                        return true;

                    if (compareType == GuidComparisonKind.Equal &&
                        resolver is not null &&
                        TryGetGuidSliceDescriptor(out var slice) &&
                        resolver(builder, slice, out indexName))
                    {
                        return true;
                    }

                    indexName = string.Empty;
                    return false;
                }

                public bool TryBuildLibraDexCondition(Builder builder, CatalogIdentityGroupIndexes indexes, out LibraDexConditionEndCondition condition, out string indexName)
                {
                    condition = default!;
                    indexName = string.Empty;
                    if (indexes is null ||
                        !TryExtractPropPath(builder, selector, out var propPath))
                    {
                        return false;
                    }

                    indexName = propPath;
                    return TryBuildLibraDexConditionAgainstIndex(builder, indexes, propPath, propPath, out condition);
                }

                /// <summary>
                /// Builds exact Guid equality or inequality against one resolved direct or calculated Guid index.<br/>
                /// </summary>
                /// <param name="builder">Builder owning deferred operand materialization.<br/></param>
                /// <param name="indexes">Identity group containing the resolved index.<br/></param>
                /// <param name="indexName">Exact LibraDex Guid index name.<br/></param>
                /// <param name="operandContext">Stable operand customization context.<br/></param>
                /// <param name="condition">Completed LibraDex condition when supported.<br/></param>
                /// <returns><see langword="true"/> for exact equality or inequality; otherwise <see langword="false"/>.<br/></returns>
                private bool TryBuildLibraDexConditionAgainstIndex(
                    Builder builder,
                    CatalogIdentityGroupIndexes indexes,
                    string indexName,
                    string operandContext,
                    out LibraDexConditionEndCondition condition)
                {
                    condition = default!;
                    if (!TryGetGuidValue(builder, this, param, operandContext, out var target))
                        return false;

                    var op = indexes.Where(indexName).AsGuid;
                    if (compareType == GuidComparisonKind.Equal)
                    {
                        condition = op.EqualTo(target).EndCondition;
                        return true;
                    }

                    if (compareType == GuidComparisonKind.NotEqual)
                    {
                        condition = op.NotEqualTo(target).EndCondition;
                        return true;
                    }

                    return false;
                }

                /// <summary>
                /// Builds a direct Guid condition or adopts LibraDex's native 16-byte Guid slice predicate over an exact source blob index.<br/>
                /// Text-pattern operations and inequality remain on the semantic path because they do not have the same invalid-slice or canonical-text contract.<br/>
                /// </summary>
                /// <param name="builder">The owning Abraxas condition builder.<br/></param>
                /// <param name="indexes">The bound LibraDex identity group.<br/></param>
                /// <param name="resolver">The store-owned resolver that proves source-index ownership.<br/></param>
                /// <param name="condition">The completed LibraDex condition when translation succeeds.<br/></param>
                /// <param name="indexName">The direct Guid or source blob index used by the condition.<br/></param>
                /// <returns><c>true</c> when the condition has an exact native representation; otherwise <c>false</c>.<br/></returns>
                public bool TryBuildLibraDexCondition(
                    Builder builder,
                    CatalogIdentityGroupIndexes indexes,
                    LibraDexDerivedSelectorResolver resolver,
                    out LibraDexConditionEndCondition condition,
                    out string indexName)
                {
                    if (indexes is not null &&
                        TryResolveCalculatedIndex(builder, resolver, out var calculatedIndexName) &&
                        TryBuildLibraDexConditionAgainstIndex(
                            builder,
                            indexes,
                            calculatedIndexName,
                            calculatedIndexName,
                            out condition))
                    {
                        indexName = calculatedIndexName;
                        return true;
                    }

                    if (indexes is not null && TryBuildLibraDexCondition(builder, indexes, out condition, out indexName))
                        return true;

                    condition = default!;
                    indexName = string.Empty;
                    if (indexes is null ||
                        resolver is null ||
                        compareType != GuidComparisonKind.Equal ||
                        !TryGetGuidSliceDescriptor(out var slice) ||
                        !TryExtractPropPath(slice.SourceSelector, out var sourcePath) ||
                        !resolver(builder, slice, out var resolvedIndexName) ||
                        !indexes.TryGetInfo(resolvedIndexName, out var info) ||
                        info.KeyFamily != CatalogIndexKeyFamily.Blob ||
                        info.IdentityFamily != CatalogIndexIdentityFamily.Scalar ||
                        !TryGetGuidValue(builder, this, param, sourcePath, out var target))
                    {
                        return false;
                    }

                    condition = indexes
                        .Where(resolvedIndexName)
                        .AsBinary
                        .SlicedAsGuid((int)slice.Offset)
                        .EqualTo(target)
                        .EndCondition;
                    indexName = resolvedIndexName;
                    return true;
                }

                /// <summary>
                /// Resolves the complete immutable Guid selector to a separately maintained calculated Guid index.<br/>
                /// </summary>
                /// <param name="builder">Builder owning the frozen selector.<br/></param>
                /// <param name="resolver">Store-owned maintained-projection resolver.<br/></param>
                /// <param name="indexName">Exact calculated index name when available.<br/></param>
                /// <returns><see langword="true"/> only for a non-source exact descriptor match.<br/></returns>
                private bool TryResolveCalculatedIndex(
                    Builder builder,
                    LibraDexDerivedSelectorResolver resolver,
                    out string indexName)
                {
                    indexName = string.Empty;
                    IBackendNeutralSelectorDescriptor? descriptor = selector.SelectorDescriptor;
                    if (resolver is null || descriptor is null || !resolver(builder, descriptor, out var resolved))
                        return false;

                    if (descriptor is not NamedIndexSelectorDescriptor &&
                        TryGetConditionSourcePropPath(builder, selector, out var sourcePath) &&
                        string.Equals(resolved, sourcePath, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    indexName = resolved;
                    return true;
                }

                /// <summary>
                /// Validates and returns the deterministic Guid binary-slice descriptor attached to this operation's selector.<br/>
                /// The result type, exact 16-byte width, and Int32-addressable offset must all match before native execution is considered.<br/>
                /// </summary>
                /// <param name="slice">The validated Guid slice descriptor when available.<br/></param>
                /// <returns><c>true</c> when the selector is a well-formed .NET-layout Guid slice; otherwise <c>false</c>.<br/></returns>
                private bool TryGetGuidSliceDescriptor(out BinarySliceSelectorDescriptor slice)
                {
                    slice = selector.SelectorDescriptor as BinarySliceSelectorDescriptor ?? null!;
                    return slice is not null &&
                        slice.Offset is >= 0 and <= int.MaxValue &&
                        slice.Length == 16 &&
                        TryGetDeterministicGuidBinarySliceShape(slice.SliceType, out var fixedByteWidth) &&
                        fixedByteWidth == slice.Length;
                }

                private static bool TryGetGuidValue(Builder builder, object owner, IValueOrExpression value, string propPath, out Guid guid)
                {
                    guid = default;
                    switch (value)
                    {
                        case ValueOrExpression<Guid> g:
                            guid = builder.MaterializeParameter(g, propPath, owner: owner);
                            return true;
                        case ValueOrExpression<string> s:
                            var raw = builder.MaterializeParameter(s, propPath, owner: owner);
                            return Guid.TryParse(raw, out guid);
                        default:
                            return false;
                    }
                }

                /// <summary>
                /// Materializes and compiles one deterministic Guid partial-match operand into a reusable nibble mask and target.<br/>
                /// The compiled values are created once per execution plan so record evaluation performs only typed Guid restoration, a 16-byte stack write, and one masked UInt128 comparison.<br/>
                /// </summary>
                /// <param name="builder">The condition builder that owns execution-scoped parameter materialization.<br/></param>
                /// <param name="owner">The condition leaf used to isolate parameter caching.<br/></param>
                /// <param name="value">The string-pattern operand.<br/></param>
                /// <param name="propPath">The direct property path or derived selector's source path.<br/></param>
                /// <param name="mode">The requested Guid partial-match operation.<br/></param>
                /// <param name="mask">The compiled UInt128 nibble mask when successful.<br/></param>
                /// <param name="target">The compiled UInt128 target when successful.<br/></param>
                /// <returns>True when the operation and normalized pattern are valid; otherwise false.<br/></returns>
                private static bool TryBuildGuidPatternMask(
                    Builder builder,
                    object owner,
                    IValueOrExpression value,
                    string propPath,
                    GuidComparisonKind mode,
                    out UInt128 mask,
                    out UInt128 target)
                {
                    mask = 0;
                    target = 0;
                    if (value is not ValueOrExpression<string> expression)
                        return false;

                    var raw = builder.MaterializeParameter(expression, propPath, owner: owner);
                    if (string.IsNullOrEmpty(raw))
                        return false;

                    Span<char> cleaned = stackalloc char[32];
                    var count = 0;
                    foreach (var character in raw)
                    {
                        if ((Uri.IsHexDigit(character) || character is 'x' or 'X') && count < cleaned.Length)
                            cleaned[count++] = char.ToLowerInvariant(character);
                    }

                    var core = new string(cleaned[..count]);
                    var missing = 32 - core.Length;
                    var pattern = mode switch
                    {
                        GuidComparisonKind.StartsWith => core + new string('x', missing),
                        GuidComparisonKind.EndsWith => new string('x', missing) + core,
                        GuidComparisonKind.Contains =>
                            new string('x', missing / 2) + core + new string('x', missing - (missing / 2)),
                        GuidComparisonKind.MatchesPattern when core.Length == 32 => core,
                        _ => string.Empty
                    };
                    if (pattern.Length != 32)
                        return false;

                    for (var index = 0; index < pattern.Length; index++)
                    {
                        mask <<= 4;
                        target <<= 4;
                        var character = pattern[index];
                        if (character == 'x')
                            continue;

                        mask |= 0xF;
                        target |= character <= '9'
                            ? (UInt128)(character - '0')
                            : (UInt128)(10 + character - 'a');
                    }

                    return true;
                }
            }

            #endregion


            /// <summary>
            /// Creates the GuidOperator stage over the supplied condition builder and selector.<br/>
            /// </summary>
            /// <param name="builder">Mutable builder that receives authored condition fragments.<br/></param>
            /// <param name="selectorExpression">Selected property path or calculated selector intent.<br/></param>
            /// <remarks>
            /// Construction retains the builder and selector; subsequent fluent operations append the actual predicate.<br/>
            /// </remarks>
            public GuidOperator(Builder builder, ValueOrExpression<string> selectorExpression)
            {
                this.builder = builder;
                this.selectorExpression = selectorExpression;
            }

            /// <summary>
            /// AddOperation for Guid typed parameters (used for exact equality comparisons).<br/>
            /// Passes a GUID value which the builder will normalize to a 16-byte BLOB parameter.<br/>
            /// </summary>
            private ContinueOrEndCondition AddOperation(GuidComparisonKind compareType, ValueOrExpression<Guid> value)
            {
                GuidCompareOperation.AddToBuilder(builder, selectorExpression, value, compareType);
                return new ContinueOrEndCondition(builder);
            }

            /// <summary>
            /// AddOperation for string/pattern parameters (used for StartsWith, EndsWith, Contains, MatchesPattern).<br/>
            /// Preserves the supplied pattern as text so the backend-neutral Guid matcher can normalize it consistently.<br/>
            /// </summary>
            private ContinueOrEndCondition AddOperation(GuidComparisonKind compareType, ValueOrExpression<string> value)
            {
                GuidCompareOperation.AddToBuilder(builder, selectorExpression, value, compareType);
                return new ContinueOrEndCondition(builder);
            }

            /// <summary>
            /// Matches leading hexadecimal GUID nibbles, with x used as a wildcard.<br/>
            /// </summary>
            /// <param name="valueOrExpression">GUID text or hexadecimal/wildcard pattern operand. May be static, factory-backed, or parameter-bound; execution resolves its value.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Matching uses the displayed 32-digit GUID order. Hexadecimal digits and x/X are retained; other characters such as hyphens are discarded.<br/>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition StartsWith(ValueOrExpression<string> valueOrExpression)
                => AddOperation(GuidComparisonKind.StartsWith, valueOrExpression);

            /// <summary>
            /// Matches trailing hexadecimal GUID nibbles, with x used as a wildcard.<br/>
            /// </summary>
            /// <param name="valueOrExpression">GUID text or hexadecimal/wildcard pattern operand. May be static, factory-backed, or parameter-bound; execution resolves its value.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Matching uses the displayed 32-digit GUID order. Hexadecimal digits and x/X are retained; other characters such as hyphens are discarded.<br/>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition EndsWith(ValueOrExpression<string> valueOrExpression)
                => AddOperation(GuidComparisonKind.EndsWith, valueOrExpression);

            /// <summary>
            /// Matches a centered hexadecimal GUID nibble pattern, padded with wildcard nibbles.<br/>
            /// </summary>
            /// <param name="valueOrExpression">GUID text or hexadecimal/wildcard pattern operand. May be static, factory-backed, or parameter-bound; execution resolves its value.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Matching uses the displayed 32-digit GUID order. Hexadecimal digits and x/X are retained; other characters such as hyphens are discarded.<br/>
            /// This historical operation centers the pattern; it is not an arbitrary-position substring search.<br/>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition Contains(ValueOrExpression<string> valueOrExpression)
                => AddOperation(GuidComparisonKind.Contains, valueOrExpression);

            /// <summary>
            /// Matches a 32-nibble GUID pattern in which x is a wildcard.<br/>
            /// </summary>
            /// <param name="valueOrExpression">GUID text or hexadecimal/wildcard pattern operand. May be static, factory-backed, or parameter-bound; execution resolves its value.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Matching uses the displayed 32-digit GUID order. Hexadecimal digits and x/X are retained; other characters such as hyphens are discarded.<br/>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition MatchesPattern(ValueOrExpression<string> valueOrExpression)
                => AddOperation(GuidComparisonKind.MatchesPattern, valueOrExpression);

            /// <summary>
            /// Adds equality against the selected value.<br/>
            /// </summary>
            /// <param name="valueOrExpression">GUID comparison operand. May be static, factory-backed, or parameter-bound; execution resolves its value.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition EqualTo(ValueOrExpression<Guid> valueOrExpression)
                => AddOperation(GuidComparisonKind.Equal, valueOrExpression);
            /// <summary>
            /// Adds equality against the selected value.<br/>
            /// </summary>
            /// <param name="valueOrExpression">GUID text or hexadecimal/wildcard pattern operand. May be static, factory-backed, or parameter-bound; execution resolves its value.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// The supplied text must parse as a GUID; an invalid operand cannot produce a matching equality or inequality predicate.<br/>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition EqualTo(ValueOrExpression<string> valueOrExpression)
                => AddOperation(GuidComparisonKind.Equal, valueOrExpression);

            /// <summary>
            /// Adds inequality against the selected value.<br/>
            /// </summary>
            /// <param name="valueOrExpression">GUID comparison operand. May be static, factory-backed, or parameter-bound; execution resolves its value.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition NotEqualTo(ValueOrExpression<Guid> valueOrExpression)
                => AddOperation(GuidComparisonKind.NotEqual, valueOrExpression);
            /// <summary>
            /// Adds inequality against the selected value.<br/>
            /// </summary>
            /// <param name="valueOrExpression">GUID text or hexadecimal/wildcard pattern operand. May be static, factory-backed, or parameter-bound; execution resolves its value.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// The supplied text must parse as a GUID; an invalid operand cannot produce a matching equality or inequality predicate.<br/>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition NotEqualTo(ValueOrExpression<string> valueOrExpression)
                => AddOperation(GuidComparisonKind.NotEqual, valueOrExpression);

            /// <summary>Compares this Guid property for equality with a textual right-hand property completed by <c>.AsGuid</c>.<br/></summary>
            public GuidPropertyComparisonTypeSelector PropEqualTo(string propPath)
                => new(builder, selectorExpression, propPath, PropertyComparisonKind.Equal);

            /// <summary>Compares this Guid property for equality with a typed right-hand property.<br/></summary>
            public ContinueOrEndCondition PropEqualTo(Expression<Func<T, Guid>> propSelector)
                => AddPropertyComparison<Guid>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.Equal);

            /// <summary>Compares this Guid property for inequality with a textual right-hand property completed by <c>.AsGuid</c>.<br/></summary>
            public GuidPropertyComparisonTypeSelector PropNotEqualTo(string propPath)
                => new(builder, selectorExpression, propPath, PropertyComparisonKind.NotEqual);

            /// <summary>Compares this Guid property for inequality with a typed right-hand property.<br/></summary>
            public ContinueOrEndCondition PropNotEqualTo(Expression<Func<T, Guid>> propSelector)
                => AddPropertyComparison<Guid>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.NotEqual);

        }

        #endregion
    }

    /// <summary>
    /// Identifies one backend-neutral Guid comparison semantic.<br/>
    /// </summary>
    internal enum GuidComparisonKind
    {
        Equal = 0,        NotEqual = 1,        StartsWith = 2,        EndsWith = 3,        Contains = 4,        MatchesPattern = 5
    }
}
