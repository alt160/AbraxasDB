using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Inheto;
using LibraDex;

namespace AbraxasDB
{

    public static partial class Condition<T>
    {
        #region Operators

        /// <summary>
        /// A fluent null-test stage that matches explicit null values and missing or null source paths.<br/>
        /// </summary>
        /// <remarks>
        /// Condition authoring is separate from execution; mutable builder instances should not be edited concurrently.<br/>
        /// </remarks>
        public class IsNullOperator : ContinueOrEndCondition
        {
            private new readonly Builder builder;
            private readonly ValueOrExpression<string> selectorExpression;
            internal class IsNullOperation : IConditionLeaf,IFastPredicateSource,ILibraDexConditionSource
            {
                public IsNullOperation(ValueOrExpression<string> selectorExpression)
                {
                    this.selectorExpression = selectorExpression;
                }
                private ValueOrExpression<string> selectorExpression;
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryExtractPropPath(builder, selectorExpression, out var propPath))
                        return false;

                    Inheto.PropPathCheck pathCheck = InhetoBinary.CheckPropPath<T, object>(propPath);
                    if (!pathCheck.IsValid)
                        return false;
                    evaluator = ib =>
                    {
                        Inheto.PropPathReadState state = ib.TryReadPropPath(
                            pathCheck,
                            out object? _,
                            options,
                            throwOnInvocationError: true);
                        return state is Inheto.PropPathReadState.Null or
                            Inheto.PropPathReadState.SourceMissing or
                            Inheto.PropPathReadState.SourceNull;
                    };
                    return true;
                }

                /// <summary>
                /// Reports the direct property-path index required by this type-neutral null predicate.<br/>
                /// No value-family choice or developer operand is needed because LibraDex resolves its maintained null route from the opened index contract.<br/>
                /// </summary>
                /// <param name="builder">The owning condition builder used to resolve the property-path selector.<br/></param>
                /// <param name="indexName">The resolved grouped LibraDex index name.<br/></param>
                /// <returns><see langword="true"/> when the selector is a direct Abraxas property path; otherwise <see langword="false"/>.<br/></returns>
                public bool TryGetLibraDexIndexName(Builder builder, out string indexName)
                {
                    indexName = string.Empty;
                    if (!TryExtractPropPath(builder, selectorExpression, out var propPath))
                        return false;

                    indexName = propPath;
                    return true;
                }

                /// <summary>
                /// Builds a family-neutral LibraDex null-route condition for the selected property index.<br/>
                /// LibraDex chooses scalar-null versus string/binary null-key storage only after resolving the opened index, keeping that storage distinction out of Abraxas.<br/>
                /// </summary>
                /// <param name="builder">The Abraxas builder that owns this condition leaf.<br/></param>
                /// <param name="indexes">The bound LibraDex identity-group indexes.<br/></param>
                /// <param name="condition">The completed LibraDex condition when translation succeeds.<br/></param>
                /// <param name="indexName">The resolved grouped LibraDex index name.<br/></param>
                /// <returns><see langword="true"/> when a direct property path was translated; otherwise <see langword="false"/>.<br/></returns>
                public bool TryBuildLibraDexCondition(
                    Builder builder,
                    CatalogIdentityGroupIndexes indexes,
                    out LibraDexConditionEndCondition condition,
                    out string indexName)
                {
                    condition = default!;
                    indexName = string.Empty;
                    if (indexes is null || !TryExtractPropPath(builder, selectorExpression, out var propPath))
                        return false;

                    indexName = propPath;
                    condition = indexes.Where(propPath).IsNull.EndCondition;
                    return true;
                }
            }
            /// <summary>
            /// Creates the IsNullOperator stage over the supplied condition builder and selector.<br/>
            /// </summary>
            /// <param name="builder">Mutable builder that receives authored condition fragments.<br/></param>
            /// <param name="selectorExpression">Selected property path or calculated selector intent.<br/></param>
            /// <remarks>
            /// Construction appends the null-test fragment immediately to the supplied builder.<br/>
            /// </remarks>
            public IsNullOperator(Builder builder, ValueOrExpression<string> selectorExpression) : base(builder)
            {
                this.builder = builder;
                this.selectorExpression = selectorExpression;
                builder.AddFragment(new IsNullOperation(selectorExpression));
            }


        }
        /// <summary>
        /// A fluent non-null-test stage that requires a present value; empty strings, empty bytes, zero, and false remain values.<br/>
        /// </summary>
        /// <remarks>
        /// Condition authoring is separate from execution; mutable builder instances should not be edited concurrently.<br/>
        /// </remarks>
        public class IsNotNullOperator : ContinueOrEndCondition
        {
            private new readonly Builder builder;
            private readonly ValueOrExpression<string> selectorExpression;
            internal class IsNotNullOperation : IConditionLeaf,IFastPredicateSource,ILibraDexConditionSource
            {
                public IsNotNullOperation(ValueOrExpression<string> selectorExpression)
                {
                    this.selectorExpression = selectorExpression;
                }
                private ValueOrExpression<string> selectorExpression;
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryExtractPropPath(builder, selectorExpression, out var propPath))
                        return false;

                    Inheto.PropPathCheck pathCheck = InhetoBinary.CheckPropPath<T, object>(propPath);
                    if (!pathCheck.IsValid)
                        return false;
                    evaluator = ib =>
                    {
                        Inheto.PropPathReadState state = ib.TryReadPropPath(
                            pathCheck,
                            out object? _,
                            options,
                            throwOnInvocationError: true);
                        return state == Inheto.PropPathReadState.Value;
                    };
                    return true;
                }

                /// <summary>
                /// Reports the direct property-path index required by this type-neutral non-null predicate.<br/>
                /// The operation has no deferred operand and does not need an Abraxas value-family hint.<br/>
                /// </summary>
                /// <param name="builder">The owning condition builder used to resolve the property-path selector.<br/></param>
                /// <param name="indexName">The resolved grouped LibraDex index name.<br/></param>
                /// <returns><see langword="true"/> when the selector is a direct Abraxas property path; otherwise <see langword="false"/>.<br/></returns>
                public bool TryGetLibraDexIndexName(Builder builder, out string indexName)
                {
                    indexName = string.Empty;
                    if (!TryExtractPropPath(builder, selectorExpression, out var propPath))
                        return false;

                    indexName = propPath;
                    return true;
                }

                /// <summary>
                /// Builds a family-neutral LibraDex non-null-route condition for the selected property index.<br/>
                /// Empty string, empty binary, false, and numeric zero remain ordinary non-null values because LibraDex resolves the physical null route from index metadata.<br/>
                /// </summary>
                /// <param name="builder">The Abraxas builder that owns this condition leaf.<br/></param>
                /// <param name="indexes">The bound LibraDex identity-group indexes.<br/></param>
                /// <param name="condition">The completed LibraDex condition when translation succeeds.<br/></param>
                /// <param name="indexName">The resolved grouped LibraDex index name.<br/></param>
                /// <returns><see langword="true"/> when a direct property path was translated; otherwise <see langword="false"/>.<br/></returns>
                public bool TryBuildLibraDexCondition(
                    Builder builder,
                    CatalogIdentityGroupIndexes indexes,
                    out LibraDexConditionEndCondition condition,
                    out string indexName)
                {
                    condition = default!;
                    indexName = string.Empty;
                    if (indexes is null || !TryExtractPropPath(builder, selectorExpression, out var propPath))
                        return false;

                    indexName = propPath;
                    condition = indexes.Where(propPath).IsNotNull.EndCondition;
                    return true;
                }
            }
            /// <summary>
            /// Creates the IsNotNullOperator stage over the supplied condition builder and selector.<br/>
            /// </summary>
            /// <param name="builder">Mutable builder that receives authored condition fragments.<br/></param>
            /// <param name="selectorExpression">Selected property path or calculated selector intent.<br/></param>
            /// <remarks>
            /// Construction appends the null-test fragment immediately to the supplied builder.<br/>
            /// </remarks>
            public IsNotNullOperator(Builder builder, ValueOrExpression<string> selectorExpression) : base(builder)
            {
                this.builder = builder;
                this.selectorExpression = selectorExpression;
                builder.AddFragment(new IsNotNullOperation(selectorExpression));
            }


        }
        /// <summary>
        /// Fluent Boolean condition operations over a selected property or calculated value.<br/>
        /// </summary>
        /// <remarks>
        /// Condition authoring is separate from execution; mutable builder instances should not be edited concurrently.<br/>
        /// </remarks>
        public class BooleanOperator
        {
            private readonly Builder builder;
            private readonly ValueOrExpression<string> selectorExpression;
            internal class BooleanOperation : IConditionLeaf,IFastPredicateSource,ILibraDexDerivedConditionSource
            {
                public BooleanOperation(ValueOrExpression<string> selectorExpression, ValueOrExpression<bool> value)
                {
                    this.value = value;
                    this.selectorExpression = selectorExpression;
                }
                private ValueOrExpression<bool> value;
                private ValueOrExpression<string> selectorExpression;
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryExtractPropPath(builder, selectorExpression, out var propPath))
                        return false;
                    var target = builder.MaterializeParameter(value, propPath, owner: this);
                    Inheto.PropPathCheck pathCheck = InhetoBinary.CheckPropPath<T, bool>(propPath);
                    if (!pathCheck.IsValid)
                        return false;
                    evaluator = ib =>
                    {
                        Inheto.PropPathReadState state = ib.TryReadPropPath(
                            pathCheck,
                            out bool candidate,
                            options,
                            throwOnInvocationError: true);
                        if (state != Inheto.PropPathReadState.Value)
                            return false;
                        return candidate == target;
                    };
                    return true;
                }
                public bool TryGetLibraDexIndexName(Builder builder, out string indexName)
                {
                    indexName = string.Empty;
                    if (!TryExtractPropPath(builder, selectorExpression, out var propPath))
                        return false;

                    indexName = propPath;
                    return true;
                }

                /// <summary>
                /// Resolves a separately maintained calculated Boolean selector before falling back to its direct source path.<br/>
                /// </summary>
                /// <param name="builder">Builder owning the frozen selector.<br/></param>
                /// <param name="resolver">Store-owned maintained-projection resolver.<br/></param>
                /// <param name="indexName">Exact calculated or direct index name when available.<br/></param>
                /// <returns><see langword="true"/> when an exact maintained index identity is available.<br/></returns>
                public bool TryGetLibraDexIndexName(
                    Builder builder,
                    LibraDexDerivedSelectorResolver resolver,
                    out string indexName)
                {
                    if (TryResolveCalculatedIndex(builder, resolver, out indexName))
                        return true;
                    return TryGetLibraDexIndexName(builder, out indexName);
                }

                public bool TryBuildLibraDexCondition(Builder builder, CatalogIdentityGroupIndexes indexes, out LibraDexConditionEndCondition condition, out string indexName)
                {
                    condition = default!;
                    indexName = string.Empty;
                    if (indexes is null || !TryExtractPropPath(builder, selectorExpression, out var propPath))
                        return false;

                    indexName = propPath;

                    condition = indexes.Where(propPath).AsBoolean.EqualTo(builder.MaterializeParameter(value, propPath, owner: this)).EndCondition;
                    return true;
                }

                /// <summary>
                /// Builds Boolean equality against a separately maintained calculated index, then retains the established direct-property path as fallback.<br/>
                /// </summary>
                /// <param name="builder">Builder owning deferred operand materialization.<br/></param>
                /// <param name="indexes">Identity group containing the resolved index.<br/></param>
                /// <param name="resolver">Store-owned maintained-projection resolver.<br/></param>
                /// <param name="condition">Completed LibraDex condition when translation succeeds.<br/></param>
                /// <param name="indexName">Exact index name used by the condition.<br/></param>
                /// <returns><see langword="true"/> when either calculated or direct translation succeeds.<br/></returns>
                public bool TryBuildLibraDexCondition(
                    Builder builder,
                    CatalogIdentityGroupIndexes indexes,
                    LibraDexDerivedSelectorResolver resolver,
                    out LibraDexConditionEndCondition condition,
                    out string indexName)
                {
                    if (indexes is not null && TryResolveCalculatedIndex(builder, resolver, out var calculatedIndexName))
                    {
                        condition = indexes
                            .Where(calculatedIndexName)
                            .AsBoolean
                            .EqualTo(builder.MaterializeParameter(value, calculatedIndexName, owner: this))
                            .EndCondition;
                        indexName = calculatedIndexName;
                        return true;
                    }

                    if (indexes is not null)
                        return TryBuildLibraDexCondition(builder, indexes, out condition, out indexName);

                    condition = default!;
                    indexName = string.Empty;
                    return false;
                }

                /// <summary>
                /// Resolves the complete immutable Boolean selector to a non-source calculated index.<br/>
                /// </summary>
                /// <param name="builder">Builder owning the selector.<br/></param>
                /// <param name="resolver">Store-owned maintained-projection resolver.<br/></param>
                /// <param name="indexName">Exact calculated index name when available.<br/></param>
                /// <returns><see langword="true"/> only for a non-source exact descriptor match.<br/></returns>
                private bool TryResolveCalculatedIndex(
                    Builder builder,
                    LibraDexDerivedSelectorResolver resolver,
                    out string indexName)
                {
                    indexName = string.Empty;
                    IBackendNeutralSelectorDescriptor? descriptor = selectorExpression.SelectorDescriptor;
                    if (resolver is null || descriptor is null || !resolver(builder, descriptor, out var resolved))
                        return false;

                    if (descriptor is not NamedIndexSelectorDescriptor &&
                        TryGetConditionSourcePropPath(builder, selectorExpression, out var sourcePath) &&
                        string.Equals(resolved, sourcePath, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    indexName = resolved;
                    return true;
                }
            }
            /// <summary>
            /// Creates the BooleanOperator stage over the supplied condition builder and selector.<br/>
            /// </summary>
            /// <param name="builder">Mutable builder that receives authored condition fragments.<br/></param>
            /// <param name="selectorExpression">Selected property path or calculated selector intent.<br/></param>
            /// <remarks>
            /// Construction retains the builder and selector; subsequent fluent operations append the actual predicate.<br/>
            /// </remarks>
            public BooleanOperator(Builder builder, ValueOrExpression<string> selectorExpression)
            {
                this.builder = builder;
                this.selectorExpression = selectorExpression;
            }

            private ContinueOrEndCondition AddBooleanOperation(ValueOrExpression<bool> value)
            {
                builder.AddFragment(new BooleanOperation(selectorExpression, value));
                return new ContinueOrEndCondition(builder);
            }

            /// <summary>
            /// Adds equality with Boolean true.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsTrue => AddBooleanOperation(true);

            /// <summary>
            /// Adds equality with Boolean false.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsFalse => AddBooleanOperation(false);

            /// <summary>
            /// Adds equality against the selected value.<br/>
            /// </summary>
            /// <param name="value">Comparison operand. May be static, factory-backed, or parameter-bound; execution resolves its value.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition EqualTo(ValueOrExpression<bool> value) => AddBooleanOperation(value);

            /// <summary>Compares this Boolean property for equality with a textual right-hand property completed by <c>.AsBoolean</c>.<br/></summary>
            public BooleanPropertyComparisonTypeSelector PropEqualTo(string propPath)
                => new(builder, selectorExpression, propPath, PropertyComparisonKind.Equal);

            /// <summary>Compares this Boolean property for equality with a typed right-hand property.<br/></summary>
            public ContinueOrEndCondition PropEqualTo(Expression<Func<T, bool>> propSelector)
                => AddPropertyComparison<bool>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.Equal);

            /// <summary>Compares this Boolean property for inequality with a textual right-hand property completed by <c>.AsBoolean</c>.<br/></summary>
            public BooleanPropertyComparisonTypeSelector PropNotEqualTo(string propPath)
                => new(builder, selectorExpression, propPath, PropertyComparisonKind.NotEqual);

            /// <summary>Compares this Boolean property for inequality with a typed right-hand property.<br/></summary>
            public ContinueOrEndCondition PropNotEqualTo(Expression<Func<T, bool>> propSelector)
                => AddPropertyComparison<bool>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.NotEqual);
        }

        /// <summary>
        /// Completes a family-neutral empty-value condition selected directly from a property path.<br/>
        /// Empty means a present zero-length string or binary value; null, missing, numeric zero, false, empty collections, and custom default values are not conflated with it.<br/>
        /// </summary>
        public sealed class IsEmptyOperator : ContinueOrEndCondition
        {
            /// <summary>
            /// Adds the empty-value predicate to the owning condition builder.<br/>
            /// </summary>
            /// <param name="builder">Builder receiving the completed predicate leaf.<br/></param>
            /// <param name="selectorExpression">Backend-neutral selector whose value is inspected.<br/></param>
            public IsEmptyOperator(Builder builder, ValueOrExpression<string> selectorExpression)
                : base(builder)
            {
                builder.AddFragment(new EmptyStateOperation(selectorExpression, excludeEmpty: false));
            }
        }

        /// <summary>
        /// Completes a family-neutral non-empty-value condition selected directly from a property path.<br/>
        /// Only present strings or binary values with positive logical length satisfy the predicate; null and missing values remain excluded.<br/>
        /// </summary>
        public sealed class IsNotEmptyOperator : ContinueOrEndCondition
        {
            /// <summary>
            /// Adds the non-empty-value predicate to the owning condition builder.<br/>
            /// </summary>
            /// <param name="builder">Builder receiving the completed predicate leaf.<br/></param>
            /// <param name="selectorExpression">Backend-neutral selector whose value is inspected.<br/></param>
            public IsNotEmptyOperator(Builder builder, ValueOrExpression<string> selectorExpression)
                : base(builder)
            {
                builder.AddFragment(new EmptyStateOperation(selectorExpression, excludeEmpty: true));
            }
        }

        /// <summary>
        /// Implements the shared direct-property empty and non-empty predicates for semantic evaluation, durable replay, and LibraDex translation.<br/>
        /// The operation remains value-family neutral until an actual runtime value or maintained index contract identifies string versus binary storage.<br/>
        /// </summary>
        internal sealed class EmptyStateOperation : IConditionLeaf, IFastPredicateSource, ILibraDexConditionSource
        {
            private readonly ValueOrExpression<string> selectorExpression;
            private readonly bool excludeEmpty;

            /// <summary>
            /// Creates one empty-state predicate without allocating an empty comparison operand.<br/>
            /// </summary>
            /// <param name="selectorExpression">Direct property selector retained by the condition.<br/></param>
            /// <param name="excludeEmpty"><see langword="true"/> for non-empty selection; <see langword="false"/> for empty selection.<br/></param>
            internal EmptyStateOperation(ValueOrExpression<string> selectorExpression, bool excludeEmpty)
            {
                this.selectorExpression = selectorExpression;
                this.excludeEmpty = excludeEmpty;
            }

            /// <summary>
            /// Builds the authoritative Inheto evaluator for a direct string or binary property.<br/>
            /// Missing and null values fail both polarities, while unsupported present CLR values produce a clear semantic error instead of silently behaving like non-empty data.<br/>
            /// </summary>
            /// <param name="builder">Condition builder owning this execution plan.<br/></param>
            /// <param name="options">Inheto restoration options for the bound store.<br/></param>
            /// <param name="evaluator">Completed per-record predicate when the selector is a direct property path.<br/></param>
            /// <returns><see langword="true"/> for a direct property selector; otherwise <see langword="false"/>.<br/></returns>
            public bool TryBuildEvaluator(
                Builder builder,
                Inheto.DeserializationOptions options,
                out Func<InhetoBinary, bool> evaluator)
            {
                evaluator = default!;
                if (!TryExtractPropPath(builder, selectorExpression, out var propPath))
                    return false;

                if (!TryResolvePropertyType(propPath, out var propertyType))
                {
                    throw new InvalidOperationException(
                        $"Condition property '{propPath}' could not be resolved on record type '{typeof(T).FullName}'.");
                }

                propertyType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
                if (propertyType == typeof(string) &&
                    TryBuildConditionValueReader<string>(builder, selectorExpression, options, out _, out var readString))
                {
                    evaluator = binary =>
                    {
                        var candidate = readString(binary);
                        if (!candidate.HasValue || candidate.Value is null)
                            return false;
                        bool isEmpty = candidate.Value.Length == 0;
                        return excludeEmpty ? !isEmpty : isEmpty;
                    };
                    return true;
                }

                if (propertyType == typeof(byte[]) &&
                    TryBuildConditionValueReader<byte[]>(builder, selectorExpression, options, out _, out var readBytes))
                {
                    evaluator = binary =>
                    {
                        var candidate = readBytes(binary);
                        if (!candidate.HasValue || candidate.Value is null)
                            return false;
                        bool isEmpty = candidate.Value.Length == 0;
                        return excludeEmpty ? !isEmpty : isEmpty;
                    };
                    return true;
                }

                throw new InvalidOperationException(
                    $"Condition property '{propPath}' has CLR type '{propertyType.FullName}', but IsEmpty and IsNotEmpty support only string and byte-array values.");
            }

            /// <summary>
            /// Resolves the final CLR member type for one simple dotted property path against the condition's record type.<br/>
            /// Resolution occurs once while the semantic execution plan is built so record evaluation can use an allocation-conscious typed Inheto reader.<br/>
            /// </summary>
            /// <param name="propPath">Normalized direct property path such as <c>.Region</c> or <c>.Payload.Bytes</c>.<br/></param>
            /// <param name="propertyType">Resolved final property or field type when successful.<br/></param>
            /// <returns><see langword="true"/> when every path segment resolves; otherwise <see langword="false"/>.<br/></returns>
            private static bool TryResolvePropertyType(string propPath, out Type propertyType)
            {
                propertyType = typeof(object);
                if (string.IsNullOrWhiteSpace(propPath))
                    return false;

                Type current = typeof(T);
                ReadOnlySpan<char> remaining = propPath.AsSpan().Trim();
                if (remaining.Length > 0 && remaining[0] == '.')
                    remaining = remaining[1..];

                while (remaining.Length > 0)
                {
                    int separator = remaining.IndexOf('.');
                    ReadOnlySpan<char> segment = separator < 0 ? remaining : remaining[..separator];
                    if (segment.Length == 0)
                        return false;

                    string memberName = segment.ToString();
                    PropertyInfo? property = current.GetProperty(
                        memberName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (property is not null)
                    {
                        current = property.PropertyType;
                    }
                    else
                    {
                        FieldInfo? field = current.GetField(
                            memberName,
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (field is null)
                            return false;
                        current = field.FieldType;
                    }

                    current = Nullable.GetUnderlyingType(current) ?? current;
                    if (separator < 0)
                        break;
                    remaining = remaining[(separator + 1)..];
                }

                propertyType = current;
                return true;
            }

            /// <summary>
            /// Reports the direct property-path index requested by this empty-state predicate.<br/>
            /// </summary>
            /// <param name="builder">The owning condition builder used to resolve the property-path selector.<br/></param>
            /// <param name="indexName">Normalized property path when available.<br/></param>
            /// <returns><see langword="true"/> only for a direct property selector.<br/></returns>
            public bool TryGetLibraDexIndexName(Builder builder, out string indexName)
            {
                indexName = string.Empty;
                if (!TryExtractPropPath(builder, selectorExpression, out var propPath))
                    return false;

                indexName = propPath;
                return true;
            }

            /// <summary>
            /// Builds the matching LibraDex empty-key-state condition after consulting the maintained index family.<br/>
            /// String and blob indexes use their dedicated empty routes; all scalar, temporal, GUID, composite, and custom shapes decline translation.<br/>
            /// </summary>
            /// <param name="builder">Condition builder owning this execution plan.<br/></param>
            /// <param name="indexes">Bound LibraDex identity-group indexes.<br/></param>
            /// <param name="condition">Completed native condition when a compatible maintained index exists.<br/></param>
            /// <param name="indexName">Exact maintained index name used by the condition.<br/></param>
            /// <returns><see langword="true"/> for a compatible string or binary index; otherwise <see langword="false"/>.<br/></returns>
            public bool TryBuildLibraDexCondition(
                Builder builder,
                CatalogIdentityGroupIndexes indexes,
                out LibraDexConditionEndCondition condition,
                out string indexName)
            {
                condition = default!;
                indexName = string.Empty;
                if (indexes is null ||
                    !TryExtractPropPath(builder, selectorExpression, out var propPath) ||
                    !indexes.TryGetInfo(propPath, out var info) ||
                    info.IdentityFamily != CatalogIndexIdentityFamily.Scalar)
                {
                    return false;
                }

                indexName = propPath;
                if (info.KeyFamily == CatalogIndexKeyFamily.String)
                {
                    var op = indexes.Where(propPath).AsString;
                    condition = excludeEmpty
                        ? op.IsNotEmpty().AND.Where(propPath).IsNotNull.EndCondition
                        : op.IsEmpty().EndCondition;
                    return true;
                }

                if (info.KeyFamily == CatalogIndexKeyFamily.Blob &&
                    Type.GetType(info.KeyTypeName, throwOnError: false) == typeof(byte[]))
                {
                    var op = indexes.Where(propPath).AsBinary;
                    condition = excludeEmpty
                        ? op.IsNotEmpty().AND.Where(propPath).IsNotNull.EndCondition
                        : op.IsEmpty().EndCondition;
                    return true;
                }

                indexName = string.Empty;
                return false;
            }
        }

        #endregion
    }
}
