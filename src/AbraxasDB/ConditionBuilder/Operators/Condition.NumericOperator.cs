using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Inheto;
using LibraDex;

namespace AbraxasDB
{

    public static partial class Condition<T>
    {

        #region Operators

        /// <summary>
        /// Fluent Numeric condition operations over a selected property or calculated value.<br/>
        /// </summary>
        /// <typeparam name="Num">Exact numeric type used by this condition family.<br/></typeparam>
        /// <remarks>
        /// Condition authoring is separate from execution; mutable builder instances should not be edited concurrently.<br/>
        /// </remarks>
        public class NumericOperator<Num> where Num : INumber<Num>
        {
            private readonly Builder builder;
            private readonly ValueOrExpression<string> selectorExpression;

            #region Operations
            internal static string[] NumericOperatorSymbols = [">", ">=", "<", "<=", "!=", "==", "IN (", "NOT IN (", "BETWEEN", "NOT BETWEEN", "&", "& ~", "|", "^", "~", ">>", "<<"];
            internal enum NumericOperatorSymbolsEnum { Greater,GreaterOrEqual,Less,LessOrEqual,NotEqual,Equal,In,NotIn,Between,NotBetween,BitAnd,BitNotAnd,BitOr,BitXOr,BitNot,ShiftR,ShiftL }
            internal class NumericOperation : IConditionLeaf,IFastPredicateSource,IStatePredicateSource,ILibraDexDerivedConditionSource,ILibraDexAliasedConditionSource
            {
                public NumericOperation(NumericOperatorSymbolsEnum @operator, ValueOrExpression<string> selectorExpression, params ValueOrExpression<Num>[] @params)
                {
                    this.selectorExpression = selectorExpression;
                    this.@params = @params;
                    this.symbol = @operator;
                }
                /// <summary>
                /// Retains one whole-set operand without evaluating its factory or enumerating its values during authoring or descriptor replay.<br/>
                /// Existing execution preparation freezes membership before record evaluation; explicit parameters retain their shared snapshot identity.<br/>
                /// </summary>
                /// <param name="operator">Positive or negative numeric membership operation.<br/></param>
                /// <param name="selectorExpression">Numeric value selector whose path is resolved during execution preparation.<br/></param>
                /// <param name="setParameter">Non-null static, factory-backed or explicit-parameter enumerable operand.<br/></param>
                public NumericOperation(NumericOperatorSymbolsEnum @operator, ValueOrExpression<string> selectorExpression, ValueOrExpression<IEnumerable<Num>> setParameter)
                {
                    ArgumentNullException.ThrowIfNull(setParameter);
                    this.selectorExpression = selectorExpression;
                    @params = Array.Empty<ValueOrExpression<Num>>();
                    setValues = setParameter;
                    symbol = @operator;
                }
                private NumericOperatorSymbolsEnum symbol;
                private ValueOrExpression<string> selectorExpression;
                private ValueOrExpression<Num>[] @params;
                private ValueOrExpression<IEnumerable<Num>>? setValues;

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryBuildConditionValueReader<Num>(builder, selectorExpression, options, out var propPath, out var readValue))
                        return false;
                    if (typeof(Num) == typeof(long) && IsReservedStatePath(propPath))
                        return false;

                    if (!TryBuildValuePredicate(builder, propPath, out var predicate))
                        return false;

                    evaluator = binary =>
                    {
                        var candidate = readValue(binary);
                        return candidate.HasValue && predicate(candidate.Value);
                    };
                    return true;
                }

                /// <summary>
                /// Builds the reserved external-State evaluator used when a complete Abraxas condition requires payload residual evaluation.<br/>
                /// State is not serialized into the Inheto object, so the record-store planner supplies its independently persisted LibraDex value.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns deferred-operand materialization.<br/></param>
                /// <param name="evaluator">Nullable-State predicate when this leaf targets the reserved <c>State</c> selector.<br/></param>
                /// <returns><see langword="true"/> only when this operation is the signed 64-bit reserved State leaf.<br/></returns>
                public bool TryBuildStateEvaluator(Builder builder, out Func<long?, bool> evaluator)
                {
                    evaluator = default!;
                    if (typeof(Num) != typeof(long) ||
                        !TryExtractPropPath(builder, selectorExpression, out var propPath) ||
                        !IsReservedStatePath(propPath) ||
                        !TryBuildValuePredicate(builder, propPath, out var predicate))
                    {
                        return false;
                    }

                    evaluator = state => state.HasValue && predicate(Num.CreateChecked(state.Value));
                    return true;
                }

                /// <summary>
                /// Recognizes the reserved State selector with the same optional leading-dot convention accepted by ordinary Abraxas property paths.<br/>
                /// </summary>
                /// <param name="path">Logical condition selector path.<br/></param>
                /// <returns><see langword="true"/> for <c>State</c> or <c>.State</c> using ordinal comparison.<br/></returns>
                private static bool IsReservedStatePath(string path)
                    => path.AsSpan().TrimStart('.').Equals("State", StringComparison.Ordinal);

                /// <summary>
                /// Materializes this numeric operation into a reusable typed value predicate.<br/>
                /// Both ordinary Inheto-backed properties and reserved external State use this one operator implementation so comparison, membership, and bitwise semantics cannot drift.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operands.<br/></param>
                /// <param name="operandContext">Logical selector name used for parameter customization and diagnostics.<br/></param>
                /// <param name="predicate">Typed predicate when the numeric operation is supported.<br/></param>
                /// <returns><see langword="true"/> when the operation was materialized; otherwise <see langword="false"/>.<br/></returns>
                private bool TryBuildValuePredicate(Builder builder, string operandContext, out Func<Num, bool> predicate)
                {
                    predicate = default!;

                    // Only support basic comparisons and set membership here.
                    switch (symbol)
                    {
                        case NumericOperatorSymbolsEnum.Equal:
                        case NumericOperatorSymbolsEnum.NotEqual:
                        case NumericOperatorSymbolsEnum.LessOrEqual:
                        case NumericOperatorSymbolsEnum.Less:
                        case NumericOperatorSymbolsEnum.GreaterOrEqual:
                        case NumericOperatorSymbolsEnum.Greater:
                            {
                                var target = builder.MaterializeParameter(@params[0], operandContext, owner: this);
                                predicate = candidate =>
                                {
                                    var cmp = Comparer<Num>.Default.Compare(candidate, target);
                                    return symbol switch
                                    {
                                        NumericOperatorSymbolsEnum.Equal => cmp == 0,
                                        NumericOperatorSymbolsEnum.NotEqual => cmp != 0,
                                        NumericOperatorSymbolsEnum.LessOrEqual => cmp <= 0,
                                        NumericOperatorSymbolsEnum.Less => cmp < 0,
                                        NumericOperatorSymbolsEnum.GreaterOrEqual => cmp >= 0,
                                        NumericOperatorSymbolsEnum.Greater => cmp > 0,
                                        _ => false
                                    };
                                };
                                return true;
                            }
                        case NumericOperatorSymbolsEnum.Between:
                        case NumericOperatorSymbolsEnum.NotBetween:
                            {
                                var left = builder.MaterializeParameter(@params[0], operandContext, owner: this);
                                var right = builder.MaterializeParameter(@params[1], operandContext, owner: this);
                                predicate = candidate =>
                                {
                                    var cmpLeft = Comparer<Num>.Default.Compare(candidate, left);
                                    var cmpRight = Comparer<Num>.Default.Compare(candidate, right);
                                    var inRange = cmpLeft >= 0 && cmpRight <= 0;
                                    return symbol == NumericOperatorSymbolsEnum.Between ? inRange : !inRange;
                                };
                                return true;
                            }
                        case NumericOperatorSymbolsEnum.In:
                        case NumericOperatorSymbolsEnum.NotIn:
                            {
                                var values = MaterializeMembershipValues(builder, operandContext);
                                var set = new HashSet<Num>(values);
                                predicate = candidate =>
                                {
                                    var contains = set.Contains(candidate);
                                    return symbol == NumericOperatorSymbolsEnum.In ? contains : !contains;
                                };
                                return true;
                            }
                        case NumericOperatorSymbolsEnum.BitAnd:
                        case NumericOperatorSymbolsEnum.BitNotAnd:
                        case NumericOperatorSymbolsEnum.BitOr:
                        case NumericOperatorSymbolsEnum.BitXOr:
                        case NumericOperatorSymbolsEnum.ShiftR:
                        case NumericOperatorSymbolsEnum.ShiftL:
                            {
                                var operation = BinaryIntegerOperatorCache.GetBinary(symbol);
                                if (operation is null || @params.Length != 2)
                                    return false;

                                var operand = builder.MaterializeParameter(@params[0], operandContext, owner: this);
                                var expected = builder.MaterializeParameter(@params[1], operandContext, owner: this);
                                predicate = candidate => EqualityComparer<Num>.Default.Equals(operation(candidate, operand), expected);
                                return true;
                            }
                        case NumericOperatorSymbolsEnum.BitNot:
                            {
                                var operation = BinaryIntegerOperatorCache.BitNot;
                                if (operation is null || @params.Length != 1)
                                    return false;

                                var expected = builder.MaterializeParameter(@params[0], operandContext, owner: this);
                                predicate = candidate => EqualityComparer<Num>.Default.Equals(operation(candidate), expected);
                                return true;
                            }
                        default:
                            return false;
                    }
                }

                /// <summary>
                /// Materializes one numeric membership source into a stable typed array for the current execution plan.<br/>
                /// Explicit set parameters use the builder's identity-wide parameter snapshot; ordinary scalar collections retain their existing per-element materialization behavior.<br/>
                /// </summary>
                /// <param name="builder">Condition builder owning execution-scoped parameter state.<br/></param>
                /// <param name="operandContext">Logical property path used by parameter hooks and diagnostics.<br/></param>
                /// <returns>The complete numeric membership set frozen for this execution.<br/></returns>
                private Num[] MaterializeMembershipValues(Builder builder, string operandContext)
                {
                    if (setValues is not null)
                    {
                        IEnumerable<Num> values = builder.MaterializeParameter(setValues, operandContext, owner: this);
                        return values as Num[] ?? values.ToArray();
                    }

                    var captured = new Num[@params.Length];
                    for (var index = 0; index < captured.Length; index++)
                        captured[index] = builder.MaterializeParameter(@params[index], operandContext, owner: this);
                    return captured;
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
                /// Resolves either a direct numeric property or a deterministic numeric binary slice to the maintained LibraDex index that can execute it.<br/>
                /// Binary slices target their exact source blob index rather than implying a separately maintained numeric projection.<br/>
                /// </summary>
                /// <param name="builder">The condition builder that owns execution-scoped materialization.<br/></param>
                /// <param name="resolver">The bound store's owner-aware derived-selector resolver.<br/></param>
                /// <param name="indexName">The direct numeric index or resolved source blob index.<br/></param>
                /// <returns><c>true</c> when this operation can report a compatible maintained index; otherwise <c>false</c>.<br/></returns>
                public bool TryGetLibraDexIndexName(
                    Builder builder,
                    LibraDexDerivedSelectorResolver resolver,
                    out string indexName)
                {
                    if (TryResolveCalculatedIndex(builder, resolver, out indexName))
                        return true;

                    if (TryGetLibraDexIndexName(builder, out indexName))
                        return true;

                    if (resolver is not null &&
                        TryGetNumericSliceDescriptor(out var slice) &&
                        IsNativeSliceOperationSupported(slice.SliceType) &&
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
                    if (indexes is null || !TryExtractPropPath(builder, selectorExpression, out var propPath))
                        return false;

                    indexName = propPath;
                    return TryBuildLibraDexConditionAgainstIndex(builder, indexes, propPath, propPath, out condition);
                }

                /// <summary>
                /// Builds a direct numeric leaf against a store-authorized physical alias while preserving its logical selector as operand context.<br/>
                /// Abraxas uses this for reserved State and for any later external numeric metadata whose physical index name must remain hidden.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operands.<br/></param>
                /// <param name="indexes">Bound LibraDex identity group.<br/></param>
                /// <param name="targetIndexName">Store-authorized physical LibraDex index name.<br/></param>
                /// <param name="condition">Completed typed LibraDex condition when supported.<br/></param>
                /// <param name="indexName">The physical index name used by the condition.<br/></param>
                /// <returns><see langword="true"/> when the aliased numeric operation translated successfully.<br/></returns>
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
                        !TryExtractPropPath(builder, selectorExpression, out var logicalName))
                    {
                        return false;
                    }

                    indexName = targetIndexName;
                    return TryBuildLibraDexConditionAgainstIndex(
                        builder,
                        indexes,
                        targetIndexName,
                        logicalName,
                        out condition);
                }

                /// <summary>
                /// Builds the current numeric comparison against one exact typed scalar index while keeping operand materialization context independent from its physical name.<br/>
                /// This shared boundary lets direct properties and durable calculated selectors use the same LibraDex operation mapping.<br/>
                /// </summary>
                /// <param name="builder">Builder owning execution-scoped deferred operands.<br/></param>
                /// <param name="indexes">Identity group containing the resolved index.<br/></param>
                /// <param name="indexName">Exact LibraDex index name to query.<br/></param>
                /// <param name="operandContext">Stable context name supplied to deferred operand materialization.<br/></param>
                /// <param name="condition">Completed LibraDex condition when the numeric operation is supported.<br/></param>
                /// <returns><see langword="true"/> when the exact numeric type and operation were translated.<br/></returns>
                private bool TryBuildLibraDexConditionAgainstIndex(
                    Builder builder,
                    CatalogIdentityGroupIndexes indexes,
                    string indexName,
                    string operandContext,
                    out LibraDexConditionEndCondition condition)
                {
                    condition = default!;
                    var selector = indexes.Where(indexName);
                    if (typeof(Num) == typeof(byte))
                        return TryBuildIntegralLibraDexCondition(builder, operandContext, selector.AsByte, out condition);
                    if (typeof(Num) == typeof(sbyte))
                        return TryBuildIntegralLibraDexCondition(builder, operandContext, selector.AsSByte, out condition);
                    if (typeof(Num) == typeof(short))
                        return TryBuildIntegralLibraDexCondition(builder, operandContext, selector.AsInt16, out condition);
                    if (typeof(Num) == typeof(ushort))
                        return TryBuildIntegralLibraDexCondition(builder, operandContext, selector.AsUInt16, out condition);
                    if (typeof(Num) == typeof(int))
                        return TryBuildIntegralLibraDexCondition(builder, operandContext, selector.AsInt32, out condition);
                    if (typeof(Num) == typeof(uint))
                        return TryBuildIntegralLibraDexCondition(builder, operandContext, selector.AsUInt32, out condition);
                    if (typeof(Num) == typeof(long))
                        return TryBuildIntegralLibraDexCondition(builder, operandContext, selector.AsInt64, out condition);
                    if (typeof(Num) == typeof(ulong))
                        return TryBuildIntegralLibraDexCondition(builder, operandContext, selector.AsUInt64, out condition);
                    if (typeof(Num) == typeof(Int128))
                        return TryBuildTypedLibraDexCondition(builder, operandContext, selector.AsInt128, out condition);
                    if (typeof(Num) == typeof(UInt128))
                        return TryBuildTypedLibraDexCondition(builder, operandContext, selector.AsUInt128, out condition);

                    if (typeof(Num) == typeof(float))
                        return TryBuildTypedLibraDexCondition(builder, operandContext, selector.AsSingle, out condition);
                    if (typeof(Num) == typeof(double))
                        return TryBuildTypedLibraDexCondition(builder, operandContext, selector.AsDouble, out condition);
                    if (typeof(Num) == typeof(decimal))
                        return TryBuildTypedLibraDexCondition(builder, operandContext, selector.AsDecimal, out condition);
                    if (typeof(Num) == typeof(BigInteger))
                        return TryBuildTypedLibraDexCondition(builder, operandContext, selector.AsBigInt, out condition);

                    return false;
                }

                /// <summary>
                /// Builds a direct numeric condition or adopts LibraDex's native typed-slice predicate over an exact source blob index.<br/>
                /// Only positive comparison, inclusive range, and supported integral bitmask forms are translated because LibraDex's typed-slice grammar intentionally has no null-safe complement for missing or undersized slices.<br/>
                /// </summary>
                /// <param name="builder">The owning Abraxas condition builder.<br/></param>
                /// <param name="indexes">The bound LibraDex identity group.<br/></param>
                /// <param name="resolver">The store-owned resolver that proves source-index ownership.<br/></param>
                /// <param name="condition">The completed LibraDex condition when translation succeeds.<br/></param>
                /// <param name="indexName">The direct numeric or source blob index used by the condition.<br/></param>
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
                        !TryGetNumericSliceDescriptor(out var slice) ||
                        !IsNativeSliceOperationSupported(slice.SliceType) ||
                        !TryExtractPropPath(slice.SourceSelector, out var sourcePath) ||
                        !resolver(builder, slice, out var resolvedIndexName) ||
                        !indexes.TryGetInfo(resolvedIndexName, out var info) ||
                        info.KeyFamily != CatalogIndexKeyFamily.Blob ||
                        info.IdentityFamily != CatalogIndexIdentityFamily.Scalar)
                    {
                        return false;
                    }

                    var binary = indexes.Where(resolvedIndexName).AsBinary;
                    LibraDexBinaryTypedSliceConditionOperator<Num>? op = slice.SliceType switch
                    {
                        BinarySliceKind.Int8 => (LibraDexBinaryTypedSliceConditionOperator<Num>)(object)binary.SlicedAsSByte((int)slice.Offset),
                        BinarySliceKind.UInt8 => (LibraDexBinaryTypedSliceConditionOperator<Num>)(object)binary.SlicedAsByte((int)slice.Offset),
                        BinarySliceKind.Int16 => (LibraDexBinaryTypedSliceConditionOperator<Num>)(object)binary.SlicedAsInt16((int)slice.Offset),
                        BinarySliceKind.UInt16 => (LibraDexBinaryTypedSliceConditionOperator<Num>)(object)binary.SlicedAsUInt16((int)slice.Offset),
                        BinarySliceKind.Int32 => (LibraDexBinaryTypedSliceConditionOperator<Num>)(object)binary.SlicedAsInt32((int)slice.Offset),
                        BinarySliceKind.UInt32 => (LibraDexBinaryTypedSliceConditionOperator<Num>)(object)binary.SlicedAsUInt32((int)slice.Offset),
                        BinarySliceKind.Int64 => (LibraDexBinaryTypedSliceConditionOperator<Num>)(object)binary.SlicedAsInt64((int)slice.Offset),
                        BinarySliceKind.UInt64 => (LibraDexBinaryTypedSliceConditionOperator<Num>)(object)binary.SlicedAsUInt64((int)slice.Offset),
                        BinarySliceKind.Int128 => (LibraDexBinaryTypedSliceConditionOperator<Num>)(object)binary.SlicedAsInt128((int)slice.Offset),
                        BinarySliceKind.UInt128 => (LibraDexBinaryTypedSliceConditionOperator<Num>)(object)binary.SlicedAsUInt128((int)slice.Offset),
                        BinarySliceKind.BigInteger => (LibraDexBinaryTypedSliceConditionOperator<Num>)(object)binary.SlicedAsBigInt((int)slice.Offset, (int)slice.Length),
                        BinarySliceKind.Single => (LibraDexBinaryTypedSliceConditionOperator<Num>)(object)binary.SlicedAsSingle((int)slice.Offset),
                        BinarySliceKind.Double => (LibraDexBinaryTypedSliceConditionOperator<Num>)(object)binary.SlicedAsDouble((int)slice.Offset),
                        BinarySliceKind.Decimal => (LibraDexBinaryTypedSliceConditionOperator<Num>)(object)binary.SlicedAsDecimal((int)slice.Offset),
                        _ => null
                    };
                    if (op is null)
                        return false;

                    Num ValueAt(int operandIndex)
                        => builder.MaterializeParameter(@params[operandIndex], sourcePath, owner: this);

                    switch (symbol)
                    {
                        case NumericOperatorSymbolsEnum.Equal:
                            if (@params.Length != 1) return false;
                            condition = op.EqualTo(ValueAt(0)).EndCondition;
                            break;
                        case NumericOperatorSymbolsEnum.LessOrEqual:
                            if (@params.Length != 1) return false;
                            condition = op.LessOrEqual(ValueAt(0)).EndCondition;
                            break;
                        case NumericOperatorSymbolsEnum.Less:
                            if (@params.Length != 1) return false;
                            condition = op.LessThan(ValueAt(0)).EndCondition;
                            break;
                        case NumericOperatorSymbolsEnum.GreaterOrEqual:
                            if (@params.Length != 1) return false;
                            condition = op.GreaterOrEqual(ValueAt(0)).EndCondition;
                            break;
                        case NumericOperatorSymbolsEnum.Greater:
                            if (@params.Length != 1) return false;
                            condition = op.GreaterThan(ValueAt(0)).EndCondition;
                            break;
                        case NumericOperatorSymbolsEnum.Between:
                            if (@params.Length != 2) return false;
                            condition = op.Between(ValueAt(0), ValueAt(1)).EndCondition;
                            break;
                        case NumericOperatorSymbolsEnum.BitAnd:
                        case NumericOperatorSymbolsEnum.BitNotAnd:
                            if (@params.Length != 2 || !IsIntegralSlice(slice.SliceType))
                                return false;
                            var mask = ValueAt(0);
                            if (symbol == NumericOperatorSymbolsEnum.BitNotAnd)
                                mask = ComplementIntegral(mask);
                            condition = op.BitAnd(mask, ValueAt(1)).EndCondition;
                            break;
                        default:
                            return false;
                    }

                    indexName = resolvedIndexName;
                    return true;
                }

                /// <summary>
                /// Resolves this operation's complete immutable selector to a separately maintained calculated index.<br/>
                /// A resolver result equal to the direct source path is left to the established direct or specialized projection paths.<br/>
                /// </summary>
                /// <param name="builder">Builder owning the frozen condition selector.<br/></param>
                /// <param name="resolver">Store-owned calculated projection resolver.<br/></param>
                /// <param name="indexName">Separately maintained calculated index name when available.<br/></param>
                /// <returns><see langword="true"/> only for an exact non-source index match.<br/></returns>
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

                /// <summary>
                /// Validates and returns the numeric binary-slice descriptor attached to this operation's selector.<br/>
                /// The descriptor CLR result type, fixed or explicit length, and Int32-addressable bounds must all match the generic numeric operator before native execution is considered.<br/>
                /// </summary>
                /// <param name="slice">The validated numeric slice descriptor when available.<br/></param>
                /// <returns><c>true</c> when the selector is a well-formed numeric slice for <typeparamref name="Num"/>; otherwise <c>false</c>.<br/></returns>
                private bool TryGetNumericSliceDescriptor(out BinarySliceSelectorDescriptor slice)
                {
                    slice = selectorExpression.SelectorDescriptor as BinarySliceSelectorDescriptor ?? null!;
                    return slice is not null &&
                        slice.Offset is >= 0 and <= int.MaxValue &&
                        slice.Length is > 0 and <= int.MaxValue &&
                        TryGetNumericBinarySliceShape(slice.SliceType, out var valueType, out var fixedByteWidth) &&
                        valueType == typeof(Num) &&
                        (fixedByteWidth == 0 || slice.Length == fixedByteWidth);
                }

                /// <summary>
                /// Reports whether the current Abraxas numeric operation has an exact positive LibraDex typed-slice representation.<br/>
                /// Negative/complement operations remain semantic residuals because invalid slices must stay filtered out rather than enter an identity-universe complement.<br/>
                /// </summary>
                /// <param name="sliceKind">The validated numeric slice kind.<br/></param>
                /// <returns><c>true</c> for exact positive comparisons, ranges, and supported integral bitmasks; otherwise <c>false</c>.<br/></returns>
                private bool IsNativeSliceOperationSupported(BinarySliceKind sliceKind)
                    => symbol is NumericOperatorSymbolsEnum.Equal or
                        NumericOperatorSymbolsEnum.LessOrEqual or
                        NumericOperatorSymbolsEnum.Less or
                        NumericOperatorSymbolsEnum.GreaterOrEqual or
                        NumericOperatorSymbolsEnum.Greater or
                        NumericOperatorSymbolsEnum.Between ||
                       (symbol is NumericOperatorSymbolsEnum.BitAnd or NumericOperatorSymbolsEnum.BitNotAnd) &&
                        IsIntegralSlice(sliceKind);

                /// <summary>
                /// Identifies fixed-width integral slice kinds supported by LibraDex's typed bitmask predicate.<br/>
                /// Floating-point, decimal, and variable-width BigInteger slices are excluded even though they support ordered comparisons.<br/>
                /// </summary>
                /// <param name="sliceKind">The numeric slice kind to classify.<br/></param>
                /// <returns><c>true</c> for signed or unsigned fixed-width integers; otherwise <c>false</c>.<br/></returns>
                private static bool IsIntegralSlice(BinarySliceKind sliceKind)
                    => sliceKind is BinarySliceKind.Int8 or
                        BinarySliceKind.UInt8 or
                        BinarySliceKind.Int16 or
                        BinarySliceKind.UInt16 or
                        BinarySliceKind.Int32 or
                        BinarySliceKind.UInt32 or
                        BinarySliceKind.Int64 or
                        BinarySliceKind.UInt64 or
                        BinarySliceKind.Int128 or
                        BinarySliceKind.UInt128;

                /// <summary>
                /// Builds one LibraDex numeric leaf after the public explicit numeric selector has established the exact operand type.<br/>
                /// Abraxas keeps its generic numeric operator for low-friction caller syntax, while this boundary deliberately maps that syntax onto LibraDex's explicit <c>AsInt32</c>, <c>AsUInt64</c>, and related projections.<br/>
                /// Operand expressions are materialized only after the exact selector is chosen so unsupported CLR numeric types fail without invoking developer code.<br/>
                /// </summary>
                /// <typeparam name="TNumeric">The exact numeric operand type selected by the LibraDex public condition surface.<br/></typeparam>
                /// <param name="builder">The Abraxas builder that owns execution-scoped parameter customization state.<br/></param>
                /// <param name="propPath">The indexed property path associated with each operand.<br/></param>
                /// <param name="op">The typed LibraDex numeric condition operator.<br/></param>
                /// <param name="condition">The completed LibraDex condition when the current Abraxas operator is supported.<br/></param>
                /// <returns><c>true</c> when the operator and operand count were translated; otherwise <c>false</c>.<br/></returns>
                private bool TryBuildTypedLibraDexCondition<TNumeric>(
                    Builder builder,
                    string propPath,
                    LibraDexConditionOperator<TNumeric> op,
                    out LibraDexConditionEndCondition condition)
                {
                    condition = default!;

                    TNumeric ValueAt(int index)
                        => (TNumeric)(object)builder.MaterializeParameter(@params[index], propPath, owner: this)!;

                    switch (symbol)
                    {
                        case NumericOperatorSymbolsEnum.Equal:
                            if (@params.Length != 1) return false;
                            condition = op.EqualTo(ValueAt(0)).EndCondition;
                            return true;
                        case NumericOperatorSymbolsEnum.NotEqual:
                            if (@params.Length != 1) return false;
                            condition = op.NotEqualTo(ValueAt(0)).EndCondition;
                            return true;
                        case NumericOperatorSymbolsEnum.LessOrEqual:
                            if (@params.Length != 1) return false;
                            condition = op.LessOrEqual(ValueAt(0)).EndCondition;
                            return true;
                        case NumericOperatorSymbolsEnum.Less:
                            if (@params.Length != 1) return false;
                            condition = op.LessThan(ValueAt(0)).EndCondition;
                            return true;
                        case NumericOperatorSymbolsEnum.GreaterOrEqual:
                            if (@params.Length != 1) return false;
                            condition = op.GreaterOrEqual(ValueAt(0)).EndCondition;
                            return true;
                        case NumericOperatorSymbolsEnum.Greater:
                            if (@params.Length != 1) return false;
                            condition = op.GreaterThan(ValueAt(0)).EndCondition;
                            return true;
                        case NumericOperatorSymbolsEnum.Between:
                            if (@params.Length != 2) return false;
                            condition = op.Between(ValueAt(0), ValueAt(1)).EndCondition;
                            return true;
                        case NumericOperatorSymbolsEnum.NotBetween:
                            if (@params.Length != 2) return false;
                            condition = op.NotBetween(ValueAt(0), ValueAt(1)).EndCondition;
                            return true;
                        case NumericOperatorSymbolsEnum.In:
                            condition = op.In(Values()).EndCondition;
                            return true;
                        case NumericOperatorSymbolsEnum.NotIn:
                            condition = op.NotIn(Values()).AND.Where(propPath).IsNotNull.EndCondition;
                            return true;
                        default:
                            return false;
                    }

                    TNumeric[] Values()
                    {
                        Num[] values = MaterializeMembershipValues(builder, propPath);
                        return (TNumeric[])(object)values;
                    }
                }

                /// <summary>
                /// Builds an integral LibraDex numeric leaf, including the two Abraxas bitmask forms that have an exact LibraDex primitive representation.<br/>
                /// <c>BitAnd</c> maps directly, while <c>BitNotAnd(mask, expected)</c> maps to <c>BitAnd(~mask, expected)</c> without changing its truth table.<br/>
                /// All non-bitmask operations remain on the shared numeric translation path so membership, ranges, and comparisons keep one implementation.<br/>
                /// </summary>
                /// <typeparam name="TNumeric">One fixed-width integral CLR type supported by LibraDex bitmask execution.<br/></typeparam>
                /// <param name="builder">The Abraxas builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="propPath">The direct indexed property path.<br/></param>
                /// <param name="op">The typed LibraDex numeric condition operator.<br/></param>
                /// <param name="condition">The completed direct condition when the current operation is supported.<br/></param>
                /// <returns><c>true</c> when direct translation succeeded; otherwise <c>false</c>.<br/></returns>
                private bool TryBuildIntegralLibraDexCondition<TNumeric>(
                    Builder builder,
                    string propPath,
                    LibraDexNumericConditionOperator<TNumeric> op,
                    out LibraDexConditionEndCondition condition)
                {
                    condition = default!;
                    if (symbol is not NumericOperatorSymbolsEnum.BitAnd and not NumericOperatorSymbolsEnum.BitNotAnd)
                        return TryBuildTypedLibraDexCondition(builder, propPath, op, out condition);
                    if (@params.Length != 2)
                        return false;

                    var mask = (TNumeric)(object)builder.MaterializeParameter(@params[0], propPath, owner: this)!;
                    var expected = (TNumeric)(object)builder.MaterializeParameter(@params[1], propPath, owner: this)!;
                    if (symbol == NumericOperatorSymbolsEnum.BitNotAnd)
                        mask = ComplementIntegral(mask);

                    condition = op.BitAnd(mask, expected).EndCondition;
                    return true;
                }

                /// <summary>
                /// Complements one fixed-width integral operand without widening its bit domain.<br/>
                /// The explicit CLR cases preserve byte, signed-byte, and short-width masks exactly as Abraxas and LibraDex store them.<br/>
                /// </summary>
                /// <typeparam name="TNumeric">One fixed-width integral CLR type supported by LibraDex bitmask execution.<br/></typeparam>
                /// <param name="value">The mask whose bits are inverted.<br/></param>
                /// <returns>The same-width complemented mask.<br/></returns>
                private static TNumeric ComplementIntegral<TNumeric>(TNumeric value)
                {
                    object boxed = value!;
                    if (typeof(TNumeric) == typeof(byte)) return (TNumeric)(object)unchecked((byte)~(byte)boxed);
                    if (typeof(TNumeric) == typeof(sbyte)) return (TNumeric)(object)unchecked((sbyte)~(sbyte)boxed);
                    if (typeof(TNumeric) == typeof(short)) return (TNumeric)(object)unchecked((short)~(short)boxed);
                    if (typeof(TNumeric) == typeof(ushort)) return (TNumeric)(object)unchecked((ushort)~(ushort)boxed);
                    if (typeof(TNumeric) == typeof(int)) return (TNumeric)(object)~(int)boxed;
                    if (typeof(TNumeric) == typeof(uint)) return (TNumeric)(object)~(uint)boxed;
                    if (typeof(TNumeric) == typeof(long)) return (TNumeric)(object)~(long)boxed;
                    if (typeof(TNumeric) == typeof(ulong)) return (TNumeric)(object)~(ulong)boxed;
                    if (typeof(TNumeric) == typeof(Int128)) return (TNumeric)(object)~(Int128)boxed;
                    if (typeof(TNumeric) == typeof(UInt128)) return (TNumeric)(object)~(UInt128)boxed;
                    throw new NotSupportedException($"Integral complement is not supported for CLR type '{typeof(TNumeric).FullName}'.");
                }

                /// <summary>
                /// Caches compiled integral operators once per closed Abraxas numeric type so Fractal scans do not use reflection, boxing, or dynamic dispatch per record.<br/>
                /// Small integral operands are widened to Int32 for operator binding and narrowed back with unchecked CLR conversion, matching ordinary C# fixed-width behavior.<br/>
                /// Floating-point and Decimal numeric builders deliberately receive no bitwise delegates because those CLR types have no integral operator contract.<br/>
                /// </summary>
                private static class BinaryIntegerOperatorCache
                {
                    internal static readonly Func<Num, Num, Num>? BitAnd = CreateBinary(ExpressionType.And);
                    internal static readonly Func<Num, Num, Num>? BitNotAnd = CreateBitNotAnd();
                    internal static readonly Func<Num, Num, Num>? BitOr = CreateBinary(ExpressionType.Or);
                    internal static readonly Func<Num, Num, Num>? BitXOr = CreateBinary(ExpressionType.ExclusiveOr);
                    internal static readonly Func<Num, Num>? BitNot = CreateUnaryNot();
                    internal static readonly Func<Num, Num, Num>? ShiftR = CreateShift(ExpressionType.RightShift);
                    internal static readonly Func<Num, Num, Num>? ShiftL = CreateShift(ExpressionType.LeftShift);

                    /// <summary>
                    /// Selects the cached binary evaluator for one public Abraxas bitwise or shift operation.<br/>
                    /// </summary>
                    /// <param name="operation">The Abraxas numeric operation kind.<br/></param>
                    /// <returns>The compiled evaluator, or <c>null</c> when the closed numeric type is not integral.<br/></returns>
                    internal static Func<Num, Num, Num>? GetBinary(NumericOperatorSymbolsEnum operation)
                        => operation switch
                        {
                            NumericOperatorSymbolsEnum.BitAnd => BitAnd,
                            NumericOperatorSymbolsEnum.BitNotAnd => BitNotAnd,
                            NumericOperatorSymbolsEnum.BitOr => BitOr,
                            NumericOperatorSymbolsEnum.BitXOr => BitXOr,
                            NumericOperatorSymbolsEnum.ShiftR => ShiftR,
                            NumericOperatorSymbolsEnum.ShiftL => ShiftL,
                            _ => null
                        };

                    /// <summary>
                    /// Compiles one two-operand integral expression for the closed numeric type.<br/>
                    /// </summary>
                    /// <param name="expressionType">The bitwise expression-node kind to compile.<br/></param>
                    /// <returns>A strongly typed cached delegate, or <c>null</c> for a non-integral numeric type.<br/></returns>
                    private static Func<Num, Num, Num>? CreateBinary(ExpressionType expressionType)
                    {
                        if (!TryGetOperatorType(out var operatorType))
                            return null;

                        var left = Expression.Parameter(typeof(Num), "left");
                        var right = Expression.Parameter(typeof(Num), "right");
                        var body = Expression.MakeBinary(
                            expressionType,
                            ConvertOperand(left, operatorType),
                            ConvertOperand(right, operatorType));
                        return Expression.Lambda<Func<Num, Num, Num>>(ConvertResult(body), left, right).Compile();
                    }

                    /// <summary>
                    /// Compiles the Abraxas <c>value &amp; ~mask</c> operation as one cached integral delegate.<br/>
                    /// </summary>
                    /// <returns>A strongly typed cached delegate, or <c>null</c> for a non-integral numeric type.<br/></returns>
                    private static Func<Num, Num, Num>? CreateBitNotAnd()
                    {
                        if (!TryGetOperatorType(out var operatorType))
                            return null;

                        var left = Expression.Parameter(typeof(Num), "left");
                        var right = Expression.Parameter(typeof(Num), "right");
                        var body = Expression.And(
                            ConvertOperand(left, operatorType),
                            Expression.Not(ConvertOperand(right, operatorType)));
                        return Expression.Lambda<Func<Num, Num, Num>>(ConvertResult(body), left, right).Compile();
                    }

                    /// <summary>
                    /// Compiles the unary integral complement operation for the closed numeric type.<br/>
                    /// </summary>
                    /// <returns>A strongly typed cached delegate, or <c>null</c> for a non-integral numeric type.<br/></returns>
                    private static Func<Num, Num>? CreateUnaryNot()
                    {
                        if (!TryGetOperatorType(out var operatorType))
                            return null;

                        var value = Expression.Parameter(typeof(Num), "value");
                        var body = Expression.Not(ConvertOperand(value, operatorType));
                        return Expression.Lambda<Func<Num, Num>>(ConvertResult(body), value).Compile();
                    }

                    /// <summary>
                    /// Compiles a left or right shift whose public Abraxas shift operand uses the same numeric type as the indexed value.<br/>
                    /// The shift count is converted to Int32 once per predicate evaluation, matching the CLR operator contract used by integral types.<br/>
                    /// </summary>
                    /// <param name="expressionType">Left-shift or right-shift expression kind.<br/></param>
                    /// <returns>A strongly typed cached delegate, or <c>null</c> for a non-integral numeric type.<br/></returns>
                    private static Func<Num, Num, Num>? CreateShift(ExpressionType expressionType)
                    {
                        if (!TryGetOperatorType(out var operatorType))
                            return null;

                        var value = Expression.Parameter(typeof(Num), "value");
                        var shift = Expression.Parameter(typeof(Num), "shift");
                        var body = Expression.MakeBinary(
                            expressionType,
                            ConvertOperand(value, operatorType),
                            Expression.Convert(shift, typeof(int)));
                        return Expression.Lambda<Func<Num, Num, Num>>(ConvertResult(body), value, shift).Compile();
                    }

                    /// <summary>
                    /// Resolves the CLR operator-binding type for the closed numeric builder.<br/>
                    /// Byte, SByte, Int16, and UInt16 use Int32 promotion; wider integral and BigInteger types bind directly.<br/>
                    /// </summary>
                    /// <param name="operatorType">The type used to construct expression-tree operator nodes.<br/></param>
                    /// <returns><c>true</c> for integral numeric types; otherwise <c>false</c>.<br/></returns>
                    private static bool TryGetOperatorType(out Type operatorType)
                    {
                        var numericType = typeof(Num);
                        if (numericType == typeof(byte) ||
                            numericType == typeof(sbyte) ||
                            numericType == typeof(short) ||
                            numericType == typeof(ushort))
                        {
                            operatorType = typeof(int);
                            return true;
                        }

                        if (numericType == typeof(int) ||
                            numericType == typeof(uint) ||
                            numericType == typeof(long) ||
                            numericType == typeof(ulong) ||
                            numericType == typeof(Int128) ||
                            numericType == typeof(UInt128) ||
                            numericType == typeof(BigInteger))
                        {
                            operatorType = numericType;
                            return true;
                        }

                        operatorType = null!;
                        return false;
                    }

                    /// <summary>
                    /// Converts an operand to the CLR type required for operator binding only when small-integral promotion requires it.<br/>
                    /// </summary>
                    /// <param name="operand">The source expression.<br/></param>
                    /// <param name="operatorType">The resolved operator-binding type.<br/></param>
                    /// <returns>The original or converted operand expression.<br/></returns>
                    private static Expression ConvertOperand(Expression operand, Type operatorType)
                        => operand.Type == operatorType ? operand : Expression.Convert(operand, operatorType);

                    /// <summary>
                    /// Narrows an operator result back to the public Abraxas numeric type when CLR small-integral promotion widened it.<br/>
                    /// </summary>
                    /// <param name="result">The compiled operator result expression.<br/></param>
                    /// <returns>The original or narrowed result expression.<br/></returns>
                    private static Expression ConvertResult(Expression result)
                        => result.Type == typeof(Num) ? result : Expression.Convert(result, typeof(Num));
                }

            }

            #endregion

			/// <summary>
			/// Creates the NumericOperator stage over the supplied condition builder and selector.<br/>
			/// </summary>
			/// <param name="builder">Mutable builder that receives authored condition fragments.<br/></param>
			/// <param name="selectorExpression">Selected property path or calculated selector intent.<br/></param>
			/// <remarks>
			/// Construction retains the builder and selector; subsequent fluent operations append the actual predicate.<br/>
			/// </remarks>
			public NumericOperator(Builder builder, ValueOrExpression<string> selectorExpression)
			{
				this.builder = builder;
				this.selectorExpression = selectorExpression;
			}

			private ContinueOrEndCondition AddOperation(NumericOperatorSymbolsEnum symbol, params ValueOrExpression<Num>[] parameters)
			{
				builder.AddFragment(new NumericOperation(symbol, selectorExpression, parameters));
				return new ContinueOrEndCondition(builder);
			}

			private ContinueOrEndCondition AddOperation(NumericOperatorSymbolsEnum symbol, IEnumerable<ValueOrExpression<Num>> parameters)
				=> AddOperation(symbol, parameters.ToArray());

			private ContinueOrEndCondition AddOperation(NumericOperatorSymbolsEnum symbol, ConditionParameter<IEnumerable<Num>> setParameter)
			{
				builder.AddFragment(new NumericOperation(symbol, selectorExpression, setParameter));
				return new ContinueOrEndCondition(builder);
			}

			/// <summary>
			/// Adds a strict greater-than comparison against the selected value.<br/>
			/// </summary>
			/// <param name="valueOrExpression">Comparison operand. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition GreaterThan(Func<Num> valueOrExpression)
				=> AddOperation(NumericOperatorSymbolsEnum.Greater, valueOrExpression);

			/// <summary>
			/// Adds a greater-than comparison using an existing static, deferred, or intentionally unbound Abraxas value wrapper.<br/>
			/// Passing the wrapper through unchanged preserves descriptor binding metadata without evaluating deferred developer code.<br/>
			/// </summary>
			/// <param name="valueOrExpression">Typed comparison value or serialized-condition parameter placeholder.<br/></param>
			/// <returns>The fluent continuation for combining or completing the condition.<br/></returns>
			public ContinueOrEndCondition GreaterThan(ValueOrExpression<Num> valueOrExpression)
				=> AddOperation(NumericOperatorSymbolsEnum.Greater, valueOrExpression);

			/// <summary>
			/// Adds a strict greater-than comparison against the selected value.<br/>
			/// </summary>
			/// <param name="valueOrExpression">Comparison operand.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition GreaterThan(Num valueOrExpression)
				=> AddOperation(NumericOperatorSymbolsEnum.Greater, valueOrExpression);

			/// <summary>
			/// Adds a strict less-than comparison against the selected value.<br/>
			/// </summary>
			/// <param name="valueOrExpression">Comparison operand. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition LessThan(Func<Num> valueOrExpression)
				=> AddOperation(NumericOperatorSymbolsEnum.Less, valueOrExpression);

			/// <summary>
			/// Adds a less-than comparison using an existing static, deferred, or intentionally unbound Abraxas value wrapper.<br/>
			/// Passing the wrapper through unchanged preserves descriptor binding metadata without evaluating deferred developer code.<br/>
			/// </summary>
			/// <param name="valueOrExpression">Typed comparison value or serialized-condition parameter placeholder.<br/></param>
			/// <returns>The fluent continuation for combining or completing the condition.<br/></returns>
			public ContinueOrEndCondition LessThan(ValueOrExpression<Num> valueOrExpression)
				=> AddOperation(NumericOperatorSymbolsEnum.Less, valueOrExpression);

			/// <summary>
			/// Adds a strict less-than comparison against the selected value.<br/>
			/// </summary>
			/// <param name="valueOrExpression">Comparison operand.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition LessThan(Num valueOrExpression)
				=> AddOperation(NumericOperatorSymbolsEnum.Less, valueOrExpression);

			/// <summary>
			/// Adds a greater-than-or-equal comparison against the selected value.<br/>
			/// </summary>
			/// <param name="valueOrExpression">Comparison operand. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition GreaterOrEqual(Func<Num> valueOrExpression)
				=> AddOperation(NumericOperatorSymbolsEnum.GreaterOrEqual, valueOrExpression);

			/// <summary>
			/// Adds a greater-than-or-equal comparison using an existing static, deferred, or intentionally unbound Abraxas value wrapper.<br/>
			/// Passing the wrapper through unchanged preserves descriptor binding metadata without evaluating deferred developer code.<br/>
			/// </summary>
			/// <param name="valueOrExpression">Typed comparison value or serialized-condition parameter placeholder.<br/></param>
			/// <returns>The fluent continuation for combining or completing the condition.<br/></returns>
			public ContinueOrEndCondition GreaterOrEqual(ValueOrExpression<Num> valueOrExpression)
				=> AddOperation(NumericOperatorSymbolsEnum.GreaterOrEqual, valueOrExpression);

			/// <summary>
			/// Adds a greater-than-or-equal comparison against the selected value.<br/>
			/// </summary>
			/// <param name="valueOrExpression">Comparison operand.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition GreaterOrEqual(Num valueOrExpression)
				=> AddOperation(NumericOperatorSymbolsEnum.GreaterOrEqual, valueOrExpression);

			/// <summary>
			/// Adds a less-than-or-equal comparison against the selected value.<br/>
			/// </summary>
			/// <param name="valueOrExpression">Comparison operand. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition LessOrEqual(Func<Num> valueOrExpression)
				=> AddOperation(NumericOperatorSymbolsEnum.LessOrEqual, valueOrExpression);

			/// <summary>
			/// Adds a less-than-or-equal comparison using an existing static, deferred, or intentionally unbound Abraxas value wrapper.<br/>
			/// Passing the wrapper through unchanged preserves descriptor binding metadata without evaluating deferred developer code.<br/>
			/// </summary>
			/// <param name="valueOrExpression">Typed comparison value or serialized-condition parameter placeholder.<br/></param>
			/// <returns>The fluent continuation for combining or completing the condition.<br/></returns>
			public ContinueOrEndCondition LessOrEqual(ValueOrExpression<Num> valueOrExpression)
				=> AddOperation(NumericOperatorSymbolsEnum.LessOrEqual, valueOrExpression);

			/// <summary>
			/// Adds a less-than-or-equal comparison against the selected value.<br/>
			/// </summary>
			/// <param name="valueOrExpression">Comparison operand.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition LessOrEqual(Num valueOrExpression)
				=> AddOperation(NumericOperatorSymbolsEnum.LessOrEqual, valueOrExpression);

			/// <summary>
			/// Adds equality against the selected value.<br/>
			/// </summary>
			/// <param name="valueOrExpression">Comparison operand. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition EqualTo(Func<Num> valueOrExpression)
				=> AddOperation(NumericOperatorSymbolsEnum.Equal, valueOrExpression);

			/// <summary>
			/// Adds an equality comparison using an existing static, deferred, or intentionally unbound Abraxas value wrapper.<br/>
			/// Passing the wrapper through unchanged preserves descriptor binding metadata without evaluating deferred developer code.<br/>
			/// </summary>
			/// <param name="valueOrExpression">Typed comparison value or serialized-condition parameter placeholder.<br/></param>
			/// <returns>The fluent continuation for combining or completing the condition.<br/></returns>
			public ContinueOrEndCondition EqualTo(ValueOrExpression<Num> valueOrExpression)
				=> AddOperation(NumericOperatorSymbolsEnum.Equal, valueOrExpression);

			/// <summary>
			/// Adds equality against the selected value.<br/>
			/// </summary>
			/// <param name="valueOrExpression">Comparison operand.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition EqualTo(Num valueOrExpression)
				=> AddOperation(NumericOperatorSymbolsEnum.Equal, valueOrExpression);

			/// <summary>
			/// Adds inequality against the selected value.<br/>
			/// </summary>
			/// <param name="valueOrExpression">Comparison operand. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition NotEqualTo(Func<Num> valueOrExpression)
				=> AddOperation(NumericOperatorSymbolsEnum.NotEqual, valueOrExpression);

			/// <summary>
			/// Adds an inequality comparison using an existing static, deferred, or intentionally unbound Abraxas value wrapper.<br/>
			/// Passing the wrapper through unchanged preserves descriptor binding metadata without evaluating deferred developer code.<br/>
			/// </summary>
			/// <param name="valueOrExpression">Typed comparison value or serialized-condition parameter placeholder.<br/></param>
			/// <returns>The fluent continuation for combining or completing the condition.<br/></returns>
			public ContinueOrEndCondition NotEqualTo(ValueOrExpression<Num> valueOrExpression)
				=> AddOperation(NumericOperatorSymbolsEnum.NotEqual, valueOrExpression);

			/// <summary>
			/// Adds inequality against the selected value.<br/>
			/// </summary>
			/// <param name="valueOrExpression">Comparison operand.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition NotEqualTo(Num valueOrExpression)
				=> AddOperation(NumericOperatorSymbolsEnum.NotEqual, valueOrExpression);

			/// <summary>
			/// Adds an inclusive range comparison using existing static, deferred, or intentionally unbound Abraxas value wrappers.<br/>
			/// Each wrapper retains its own optional binding name and receives its deterministic left-to-right descriptor position.<br/>
			/// </summary>
			/// <param name="startValue">Typed inclusive lower bound or serialized-condition parameter placeholder.<br/></param>
			/// <param name="endValue">Typed inclusive upper bound or serialized-condition parameter placeholder.<br/></param>
			/// <returns>The fluent continuation for combining or completing the condition.<br/></returns>
			public ContinueOrEndCondition Between(
				ValueOrExpression<Num> startValue,
				ValueOrExpression<Num> endValue)
				=> AddOperation(NumericOperatorSymbolsEnum.Between, startValue, endValue);

			/// <summary>
			/// Adds an inclusive lower-and-upper-bound comparison against the selected value.<br/>
			/// </summary>
			/// <param name="startValue">Inclusive lower bound. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <param name="endValue">Inclusive upper bound. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition Between(Func<Num> startValue, Func<Num> endValue)
				=> AddOperation(NumericOperatorSymbolsEnum.Between, startValue, endValue);

			/// <summary>
			/// Adds an inclusive lower-and-upper-bound comparison against the selected value.<br/>
			/// </summary>
			/// <param name="startValue">Inclusive lower bound. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <param name="endValue">Inclusive upper bound.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition Between(Func<Num> startValue, Num endValue)
				=> AddOperation(NumericOperatorSymbolsEnum.Between, startValue, endValue);

			/// <summary>
			/// Adds an inclusive lower-and-upper-bound comparison against the selected value.<br/>
			/// </summary>
			/// <param name="startValue">Inclusive lower bound.<br/></param>
			/// <param name="endValue">Inclusive upper bound. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition Between(Num startValue, Func<Num> endValue)
				=> AddOperation(NumericOperatorSymbolsEnum.Between, startValue, endValue);

			/// <summary>
			/// Adds an inclusive lower-and-upper-bound comparison against the selected value.<br/>
			/// </summary>
			/// <param name="startValue">Inclusive lower bound.<br/></param>
			/// <param name="endValue">Inclusive upper bound.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition Between(Num startValue, Num endValue)
				=> AddOperation(NumericOperatorSymbolsEnum.Between, startValue, endValue);

			/// <summary>
			/// Adds the complement of an inclusive lower-and-upper-bound comparison.<br/>
			/// </summary>
			/// <param name="startValue">Inclusive lower bound. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <param name="endValue">Inclusive upper bound. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition NotBetween(Func<Num> startValue, Func<Num> endValue)
				=> AddOperation(NumericOperatorSymbolsEnum.NotBetween, startValue, endValue);

			/// <summary>
			/// Adds an inclusive negated-range comparison using existing static, deferred, or intentionally unbound Abraxas value wrappers.<br/>
			/// Each wrapper retains its own optional binding name and receives its deterministic left-to-right descriptor position.<br/>
			/// </summary>
			/// <param name="startValue">Typed inclusive lower bound or serialized-condition parameter placeholder.<br/></param>
			/// <param name="endValue">Typed inclusive upper bound or serialized-condition parameter placeholder.<br/></param>
			/// <returns>The fluent continuation for combining or completing the condition.<br/></returns>
			public ContinueOrEndCondition NotBetween(
				ValueOrExpression<Num> startValue,
				ValueOrExpression<Num> endValue)
				=> AddOperation(NumericOperatorSymbolsEnum.NotBetween, startValue, endValue);

			/// <summary>
			/// Adds the complement of an inclusive lower-and-upper-bound comparison.<br/>
			/// </summary>
			/// <param name="startValue">Inclusive lower bound. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <param name="endValue">Inclusive upper bound.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition NotBetween(Func<Num> startValue, Num endValue)
				=> AddOperation(NumericOperatorSymbolsEnum.NotBetween, startValue, endValue);

			/// <summary>
			/// Adds the complement of an inclusive lower-and-upper-bound comparison.<br/>
			/// </summary>
			/// <param name="startValue">Inclusive lower bound.<br/></param>
			/// <param name="endValue">Inclusive upper bound. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition NotBetween(Num startValue, Func<Num> endValue)
				=> AddOperation(NumericOperatorSymbolsEnum.NotBetween, startValue, endValue);

			/// <summary>
			/// Adds the complement of an inclusive lower-and-upper-bound comparison.<br/>
			/// </summary>
			/// <param name="startValue">Inclusive lower bound.<br/></param>
			/// <param name="endValue">Inclusive upper bound.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition NotBetween(Num startValue, Num endValue)
				=> AddOperation(NumericOperatorSymbolsEnum.NotBetween, startValue, endValue);

			/// <summary>
			/// Adds membership of the selected value in the supplied collection.<br/>
			/// </summary>
			/// <param name="theseValues">Candidate values for membership testing. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition IsIn(IEnumerable<Func<Num>> theseValues)
				=> AddOperation(NumericOperatorSymbolsEnum.In, theseValues.Select(x => (ValueOrExpression<Num>)x));

			/// <summary>
			/// Adds membership of the selected value in the supplied collection.<br/>
			/// </summary>
			/// <param name="theseValues">Candidate values for membership testing.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition IsIn(IEnumerable<Num> theseValues)
				=> AddOperation(NumericOperatorSymbolsEnum.In, theseValues.Select(x => (ValueOrExpression<Num>)x));

			/// <summary>
			/// Adds reusable numeric membership using one execution-frozen deferred set.<br/>
			/// Changing the parameter value before a later execution reuses the authored condition without rebuilding its semantic structure.<br/>
			/// </summary>
			/// <param name="theseValues">Typed set parameter created by <see cref="RecordStore{T}.Parameter{TValue}(IEnumerable{TValue}, string?)"/>.<br/></param>
			/// <returns>The fluent continuation for combining or completing the condition.<br/></returns>
			public ContinueOrEndCondition IsIn(ConditionParameter<IEnumerable<Num>> theseValues)
				=> AddOperation(NumericOperatorSymbolsEnum.In, theseValues);

			/// <summary>
			/// Adds non-membership of the selected value in the supplied collection.<br/>
			/// </summary>
			/// <param name="theseValues">Candidate values for membership testing. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition IsNotIn(IEnumerable<Func<Num>> theseValues)
				=> AddOperation(NumericOperatorSymbolsEnum.NotIn, theseValues.Select(x => (ValueOrExpression<Num>)x));

			/// <summary>
			/// Adds non-membership of the selected value in the supplied collection.<br/>
			/// </summary>
			/// <param name="theseValues">Candidate values for membership testing.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition IsNotIn(IEnumerable<Num> theseValues)
				=> AddOperation(NumericOperatorSymbolsEnum.NotIn, theseValues.Select(x => (ValueOrExpression<Num>)x));

			/// <summary>
			/// Adds reusable negated numeric membership using one execution-frozen deferred set.<br/>
			/// Null or missing indexed values retain the existing Abraxas negated-membership semantics and remain excluded.<br/>
			/// </summary>
			/// <param name="theseValues">Typed set parameter created by <see cref="RecordStore{T}.Parameter{TValue}(IEnumerable{TValue}, string?)"/>.<br/></param>
			/// <returns>The fluent continuation for combining or completing the condition.<br/></returns>
			public ContinueOrEndCondition IsNotIn(ConditionParameter<IEnumerable<Num>> theseValues)
				=> AddOperation(NumericOperatorSymbolsEnum.NotIn, theseValues);

			/// <summary>
			/// Adds the integral predicate <c>(value &amp; mask) == expected</c>.<br/>
			/// </summary>
			/// <param name="bitMask">Integral bit mask. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <param name="equalTo">Expected result of the bitwise operation. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitAnd(Func<Num> bitMask, Func<Num> equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.BitAnd, bitMask, equalTo);

			/// <summary>
			/// Adds the integral predicate <c>(value &amp; mask) == expected</c>.<br/>
			/// </summary>
			/// <param name="bitMask">Integral bit mask. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <param name="equalTo">Expected result of the bitwise operation.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitAnd(Func<Num> bitMask, Num equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.BitAnd, bitMask, equalTo);

			/// <summary>
			/// Adds the integral predicate <c>(value &amp; mask) == expected</c>.<br/>
			/// </summary>
			/// <param name="bitMask">Integral bit mask.<br/></param>
			/// <param name="equalTo">Expected result of the bitwise operation. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitAnd(Num bitMask, Func<Num> equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.BitAnd, bitMask, equalTo);

			/// <summary>
			/// Adds the integral predicate <c>(value &amp; mask) == expected</c>.<br/>
			/// </summary>
			/// <param name="bitMask">Integral bit mask.<br/></param>
			/// <param name="equalTo">Expected result of the bitwise operation.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitAnd(Num bitMask, Num equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.BitAnd, bitMask, equalTo);

			/// <summary>
			/// Adds the integral predicate <c>(value &amp; ~mask) == expected</c>.<br/>
			/// </summary>
			/// <param name="bitMask">Integral bit mask. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <param name="equalTo">Expected result of the bitwise operation. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitNotAnd(Func<Num> bitMask, Func<Num> equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.BitNotAnd, bitMask, equalTo);

			/// <summary>
			/// Adds the integral predicate <c>(value &amp; ~mask) == expected</c>.<br/>
			/// </summary>
			/// <param name="bitMask">Integral bit mask. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <param name="equalTo">Expected result of the bitwise operation.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitNotAnd(Func<Num> bitMask, Num equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.BitNotAnd, bitMask, equalTo);

			/// <summary>
			/// Adds the integral predicate <c>(value &amp; ~mask) == expected</c>.<br/>
			/// </summary>
			/// <param name="bitMask">Integral bit mask.<br/></param>
			/// <param name="equalTo">Expected result of the bitwise operation. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitNotAnd(Num bitMask, Func<Num> equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.BitNotAnd, bitMask, equalTo);

			/// <summary>
			/// Adds the integral predicate <c>(value &amp; ~mask) == expected</c>.<br/>
			/// </summary>
			/// <param name="bitMask">Integral bit mask.<br/></param>
			/// <param name="equalTo">Expected result of the bitwise operation.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitNotAnd(Num bitMask, Num equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.BitNotAnd, bitMask, equalTo);

			/// <summary>
			/// Adds the integral predicate <c>(value | mask) == expected</c>.<br/>
			/// </summary>
			/// <param name="bitMask">Integral bit mask. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <param name="equalTo">Expected result of the bitwise operation. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitOr(Func<Num> bitMask, Func<Num> equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.BitOr, bitMask, equalTo);

			/// <summary>
			/// Adds the integral predicate <c>(value | mask) == expected</c>.<br/>
			/// </summary>
			/// <param name="bitMask">Integral bit mask. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <param name="equalTo">Expected result of the bitwise operation.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitOr(Func<Num> bitMask, Num equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.BitOr, bitMask, equalTo);

			/// <summary>
			/// Adds the integral predicate <c>(value | mask) == expected</c>.<br/>
			/// </summary>
			/// <param name="bitMask">Integral bit mask.<br/></param>
			/// <param name="equalTo">Expected result of the bitwise operation. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitOr(Num bitMask, Func<Num> equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.BitOr, bitMask, equalTo);

			/// <summary>
			/// Adds the integral predicate <c>(value | mask) == expected</c>.<br/>
			/// </summary>
			/// <param name="bitMask">Integral bit mask.<br/></param>
			/// <param name="equalTo">Expected result of the bitwise operation.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitOr(Num bitMask, Num equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.BitOr, bitMask, equalTo);

			/// <summary>
			/// Adds the integral predicate <c>(value ^ mask) == expected</c>.<br/>
			/// </summary>
			/// <param name="bitMask">Integral bit mask. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <param name="equalTo">Expected result of the bitwise operation. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitXOr(Func<Num> bitMask, Func<Num> equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.BitXOr, bitMask, equalTo);

			/// <summary>
			/// Adds the integral predicate <c>(value ^ mask) == expected</c>.<br/>
			/// </summary>
			/// <param name="bitMask">Integral bit mask. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <param name="equalTo">Expected result of the bitwise operation.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitXOr(Func<Num> bitMask, Num equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.BitXOr, bitMask, equalTo);

			/// <summary>
			/// Adds the integral predicate <c>(value ^ mask) == expected</c>.<br/>
			/// </summary>
			/// <param name="bitMask">Integral bit mask.<br/></param>
			/// <param name="equalTo">Expected result of the bitwise operation. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitXOr(Num bitMask, Func<Num> equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.BitXOr, bitMask, equalTo);

			/// <summary>
			/// Adds the integral predicate <c>(value ^ mask) == expected</c>.<br/>
			/// </summary>
			/// <param name="bitMask">Integral bit mask.<br/></param>
			/// <param name="equalTo">Expected result of the bitwise operation.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitXOr(Num bitMask, Num equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.BitXOr, bitMask, equalTo);

			/// <summary>
			/// Adds the integral predicate <c>(~value) == expected</c>.<br/>
			/// </summary>
			/// <param name="equalTo">Expected result of the bitwise operation. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitNot(Func<Num> equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.BitNot, equalTo);

			/// <summary>
			/// Adds the integral predicate <c>(~value) == expected</c>.<br/>
			/// </summary>
			/// <param name="equalTo">Expected result of the bitwise operation.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitNot(Num equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.BitNot, equalTo);

			/// <summary>
			/// Adds the integral predicate <c>(value &gt;&gt; shift) == expected</c>.<br/>
			/// </summary>
			/// <param name="shiftBy">Integral shift count, converted to Int32 for execution. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <param name="equalTo">Expected result of the bitwise operation. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// The shift operand is converted to Int32 and follows the underlying CLR integral shift behavior.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitShiftR(Func<Num> shiftBy, Func<Num> equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.ShiftR, shiftBy, equalTo);

			/// <summary>
			/// Adds the integral predicate <c>(value &gt;&gt; shift) == expected</c>.<br/>
			/// </summary>
			/// <param name="shiftBy">Integral shift count, converted to Int32 for execution. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <param name="equalTo">Expected result of the bitwise operation.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// The shift operand is converted to Int32 and follows the underlying CLR integral shift behavior.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitShiftR(Func<Num> shiftBy, Num equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.ShiftR, shiftBy, equalTo);

			/// <summary>
			/// Adds the integral predicate <c>(value &gt;&gt; shift) == expected</c>.<br/>
			/// </summary>
			/// <param name="shiftBy">Integral shift count, converted to Int32 for execution.<br/></param>
			/// <param name="equalTo">Expected result of the bitwise operation. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// The shift operand is converted to Int32 and follows the underlying CLR integral shift behavior.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitShiftR(Num shiftBy, Func<Num> equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.ShiftR, shiftBy, equalTo);

			/// <summary>
			/// Adds the integral predicate <c>(value &gt;&gt; shift) == expected</c>.<br/>
			/// </summary>
			/// <param name="shiftBy">Integral shift count, converted to Int32 for execution.<br/></param>
			/// <param name="equalTo">Expected result of the bitwise operation.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// The shift operand is converted to Int32 and follows the underlying CLR integral shift behavior.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitShiftR(Num shiftBy, Num equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.ShiftR, shiftBy, equalTo);

			/// <summary>
			/// Adds the integral predicate <c>(value &lt;&lt; shift) == expected</c>.<br/>
			/// </summary>
			/// <param name="shiftBy">Integral shift count, converted to Int32 for execution. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <param name="equalTo">Expected result of the bitwise operation. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// The shift operand is converted to Int32 and follows the underlying CLR integral shift behavior.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitShiftL(Func<Num> shiftBy, Func<Num> equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.ShiftL, shiftBy, equalTo);

			/// <summary>
			/// Adds the integral predicate <c>(value &lt;&lt; shift) == expected</c>.<br/>
			/// </summary>
			/// <param name="shiftBy">Integral shift count, converted to Int32 for execution. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <param name="equalTo">Expected result of the bitwise operation.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// The shift operand is converted to Int32 and follows the underlying CLR integral shift behavior.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitShiftL(Func<Num> shiftBy, Num equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.ShiftL, shiftBy, equalTo);

			/// <summary>
			/// Adds the integral predicate <c>(value &lt;&lt; shift) == expected</c>.<br/>
			/// </summary>
			/// <param name="shiftBy">Integral shift count, converted to Int32 for execution.<br/></param>
			/// <param name="equalTo">Expected result of the bitwise operation. The factory or element factories are evaluated through execution-scoped parameter materialization.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// The shift operand is converted to Int32 and follows the underlying CLR integral shift behavior.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitShiftL(Num shiftBy, Func<Num> equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.ShiftL, shiftBy, equalTo);

			/// <summary>
			/// Adds the integral predicate <c>(value &lt;&lt; shift) == expected</c>.<br/>
			/// </summary>
			/// <param name="shiftBy">Integral shift count, converted to Int32 for execution.<br/></param>
			/// <param name="equalTo">Expected result of the bitwise operation.<br/></param>
			/// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
			/// <remarks>
			/// Bitwise operators require an integral execution type; non-integral numeric types do not acquire integral bitwise semantics.<br/>
			/// The shift operand is converted to Int32 and follows the underlying CLR integral shift behavior.<br/>
			/// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
			/// </remarks>
			public ContinueOrEndCondition BitShiftL(Num shiftBy, Num equalTo)
				=> AddOperation(NumericOperatorSymbolsEnum.ShiftL, shiftBy, equalTo);

			/// <summary>
			/// Compares this numeric property to a textual right-hand property after the caller selects its declared numeric type.<br/>
			/// Abraxas widens both values only when one common numeric domain can represent every value of both declared types without loss.<br/>
			/// </summary>
			public NumericPropertyComparisonTypeSelector<Num> PropEqualTo(string propPath)
				=> new(builder, selectorExpression, propPath, PropertyComparisonKind.Equal);

			/// <summary>Compares this numeric property for equality with a typed right-hand property using lossless numeric promotion.<br/></summary>
			public ContinueOrEndCondition PropEqualTo<TRight>(Expression<Func<T, TRight>> propSelector) where TRight : INumber<TRight>
				=> AddNumericPropertyComparison<Num, TRight>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.Equal);

			/// <summary>Compares this numeric property for inequality with a textual right-hand property whose declared type is selected next.<br/></summary>
			public NumericPropertyComparisonTypeSelector<Num> PropNotEqualTo(string propPath)
				=> new(builder, selectorExpression, propPath, PropertyComparisonKind.NotEqual);

			/// <summary>Compares this numeric property for inequality with a typed right-hand property using lossless numeric promotion.<br/></summary>
			public ContinueOrEndCondition PropNotEqualTo<TRight>(Expression<Func<T, TRight>> propSelector) where TRight : INumber<TRight>
				=> AddNumericPropertyComparison<Num, TRight>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.NotEqual);

			/// <summary>Compares whether this numeric property is greater than a textual right-hand property whose declared type is selected next.<br/></summary>
			public NumericPropertyComparisonTypeSelector<Num> PropGreaterThan(string propPath)
				=> new(builder, selectorExpression, propPath, PropertyComparisonKind.Greater);

			/// <summary>Compares whether this numeric property is greater than a typed right-hand property using lossless numeric promotion.<br/></summary>
			public ContinueOrEndCondition PropGreaterThan<TRight>(Expression<Func<T, TRight>> propSelector) where TRight : INumber<TRight>
				=> AddNumericPropertyComparison<Num, TRight>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.Greater);

			/// <summary>Compares whether this numeric property is greater than or equal to a textual right-hand property whose declared type is selected next.<br/></summary>
			public NumericPropertyComparisonTypeSelector<Num> PropGreaterOrEqual(string propPath)
				=> new(builder, selectorExpression, propPath, PropertyComparisonKind.GreaterOrEqual);

			/// <summary>Compares whether this numeric property is greater than or equal to a typed right-hand property using lossless numeric promotion.<br/></summary>
			public ContinueOrEndCondition PropGreaterOrEqual<TRight>(Expression<Func<T, TRight>> propSelector) where TRight : INumber<TRight>
				=> AddNumericPropertyComparison<Num, TRight>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.GreaterOrEqual);

			/// <summary>Compares whether this numeric property is less than a textual right-hand property whose declared type is selected next.<br/></summary>
			public NumericPropertyComparisonTypeSelector<Num> PropLessThan(string propPath)
				=> new(builder, selectorExpression, propPath, PropertyComparisonKind.Less);

			/// <summary>Compares whether this numeric property is less than a typed right-hand property using lossless numeric promotion.<br/></summary>
			public ContinueOrEndCondition PropLessThan<TRight>(Expression<Func<T, TRight>> propSelector) where TRight : INumber<TRight>
				=> AddNumericPropertyComparison<Num, TRight>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.Less);

			/// <summary>Compares whether this numeric property is less than or equal to a textual right-hand property whose declared type is selected next.<br/></summary>
			public NumericPropertyComparisonTypeSelector<Num> PropLessOrEqual(string propPath)
				=> new(builder, selectorExpression, propPath, PropertyComparisonKind.LessOrEqual);

			/// <summary>Compares whether this numeric property is less than or equal to a typed right-hand property using lossless numeric promotion.<br/></summary>
			public ContinueOrEndCondition PropLessOrEqual<TRight>(Expression<Func<T, TRight>> propSelector) where TRight : INumber<TRight>
				=> AddNumericPropertyComparison<Num, TRight>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.LessOrEqual);

            /// <summary>
            /// Compares the selected property's bitwise AND result against the numeric type's zero.<br/>
            /// The operand callback remains deferred until normal execution preparation; construction does not invoke it.<br/>
            /// This one-argument shorthand does not mean a nonzero/any-bit test or an omitted comparison.<br/>
            /// </summary>
            /// <param name="bitMask">Non-null deferred mask used by the existing integral evaluator.<br/></param>
            /// <returns>The fluent continuation for combining or completing the zero-comparison condition.<br/></returns>
            /// <exception cref="ArgumentNullException">The operand callback is null.<br/></exception>
            public ContinueOrEndCondition BitAnd(Func<Num> bitMask)
                => AddOperation(NumericOperatorSymbolsEnum.BitAnd, bitMask, Num.Zero);

            /// <summary>
            /// Compares the selected property's bitwise AND result against the numeric type's zero.<br/>
            /// Captures the supplied operand and Num.Zero using the existing numeric condition representation.<br/>
            /// This one-argument shorthand does not mean a nonzero/any-bit test or an omitted comparison.<br/>
            /// </summary>
            /// <param name="bitMask">Immediate mask used by the existing integral evaluator.<br/></param>
            /// <returns>The fluent continuation for combining or completing the zero-comparison condition.<br/></returns>
            public ContinueOrEndCondition BitAnd(Num bitMask)
                => AddOperation(NumericOperatorSymbolsEnum.BitAnd, bitMask, Num.Zero);

            /// <summary>
            /// Compares the selected property's bitwise AND with the complemented mask result against the numeric type's zero.<br/>
            /// The operand callback remains deferred until normal execution preparation; construction does not invoke it.<br/>
            /// This one-argument shorthand does not mean a nonzero/any-bit test or an omitted comparison.<br/>
            /// </summary>
            /// <param name="bitMask">Non-null deferred mask used by the existing integral evaluator.<br/></param>
            /// <returns>The fluent continuation for combining or completing the zero-comparison condition.<br/></returns>
            /// <exception cref="ArgumentNullException">The operand callback is null.<br/></exception>
            public ContinueOrEndCondition BitNotAnd(Func<Num> bitMask)
                => AddOperation(NumericOperatorSymbolsEnum.BitNotAnd, bitMask, Num.Zero);

            /// <summary>
            /// Compares the selected property's bitwise AND with the complemented mask result against the numeric type's zero.<br/>
            /// Captures the supplied operand and Num.Zero using the existing numeric condition representation.<br/>
            /// This one-argument shorthand does not mean a nonzero/any-bit test or an omitted comparison.<br/>
            /// </summary>
            /// <param name="bitMask">Immediate mask used by the existing integral evaluator.<br/></param>
            /// <returns>The fluent continuation for combining or completing the zero-comparison condition.<br/></returns>
            public ContinueOrEndCondition BitNotAnd(Num bitMask)
                => AddOperation(NumericOperatorSymbolsEnum.BitNotAnd, bitMask, Num.Zero);

            /// <summary>
            /// Compares the selected property's bitwise OR result against the numeric type's zero.<br/>
            /// The operand callback remains deferred until normal execution preparation; construction does not invoke it.<br/>
            /// This one-argument shorthand does not mean a nonzero/any-bit test or an omitted comparison.<br/>
            /// </summary>
            /// <param name="bitMask">Non-null deferred mask used by the existing integral evaluator.<br/></param>
            /// <returns>The fluent continuation for combining or completing the zero-comparison condition.<br/></returns>
            /// <exception cref="ArgumentNullException">The operand callback is null.<br/></exception>
            public ContinueOrEndCondition BitOr(Func<Num> bitMask)
                => AddOperation(NumericOperatorSymbolsEnum.BitOr, bitMask, Num.Zero);

            /// <summary>
            /// Compares the selected property's bitwise OR result against the numeric type's zero.<br/>
            /// Captures the supplied operand and Num.Zero using the existing numeric condition representation.<br/>
            /// This one-argument shorthand does not mean a nonzero/any-bit test or an omitted comparison.<br/>
            /// </summary>
            /// <param name="bitMask">Immediate mask used by the existing integral evaluator.<br/></param>
            /// <returns>The fluent continuation for combining or completing the zero-comparison condition.<br/></returns>
            public ContinueOrEndCondition BitOr(Num bitMask)
                => AddOperation(NumericOperatorSymbolsEnum.BitOr, bitMask, Num.Zero);

            /// <summary>
            /// Compares the selected property's bitwise exclusive OR result against the numeric type's zero.<br/>
            /// The operand callback remains deferred until normal execution preparation; construction does not invoke it.<br/>
            /// This one-argument shorthand does not mean a nonzero/any-bit test or an omitted comparison.<br/>
            /// </summary>
            /// <param name="bitMask">Non-null deferred mask used by the existing integral evaluator.<br/></param>
            /// <returns>The fluent continuation for combining or completing the zero-comparison condition.<br/></returns>
            /// <exception cref="ArgumentNullException">The operand callback is null.<br/></exception>
            public ContinueOrEndCondition BitXOr(Func<Num> bitMask)
                => AddOperation(NumericOperatorSymbolsEnum.BitXOr, bitMask, Num.Zero);

            /// <summary>
            /// Compares the selected property's bitwise exclusive OR result against the numeric type's zero.<br/>
            /// Captures the supplied operand and Num.Zero using the existing numeric condition representation.<br/>
            /// This one-argument shorthand does not mean a nonzero/any-bit test or an omitted comparison.<br/>
            /// </summary>
            /// <param name="bitMask">Immediate mask used by the existing integral evaluator.<br/></param>
            /// <returns>The fluent continuation for combining or completing the zero-comparison condition.<br/></returns>
            public ContinueOrEndCondition BitXOr(Num bitMask)
                => AddOperation(NumericOperatorSymbolsEnum.BitXOr, bitMask, Num.Zero);

            /// <summary>
            /// Compares the selected property's right shift result against the numeric type's zero.<br/>
            /// The operand callback remains deferred until normal execution preparation; construction does not invoke it.<br/>
            /// This one-argument shorthand does not mean a nonzero/any-bit test or an omitted comparison.<br/>
            /// </summary>
            /// <param name="shiftBy">Non-null deferred shift count used by the existing integral evaluator.<br/></param>
            /// <returns>The fluent continuation for combining or completing the zero-comparison condition.<br/></returns>
            /// <exception cref="ArgumentNullException">The operand callback is null.<br/></exception>
            public ContinueOrEndCondition BitShiftR(Func<Num> shiftBy)
                => AddOperation(NumericOperatorSymbolsEnum.ShiftR, shiftBy, Num.Zero);

            /// <summary>
            /// Compares the selected property's right shift result against the numeric type's zero.<br/>
            /// Captures the supplied operand and Num.Zero using the existing numeric condition representation.<br/>
            /// This one-argument shorthand does not mean a nonzero/any-bit test or an omitted comparison.<br/>
            /// </summary>
            /// <param name="shiftBy">Immediate shift count used by the existing integral evaluator.<br/></param>
            /// <returns>The fluent continuation for combining or completing the zero-comparison condition.<br/></returns>
            public ContinueOrEndCondition BitShiftR(Num shiftBy)
                => AddOperation(NumericOperatorSymbolsEnum.ShiftR, shiftBy, Num.Zero);

            /// <summary>
            /// Compares the selected property's left shift result against the numeric type's zero.<br/>
            /// The operand callback remains deferred until normal execution preparation; construction does not invoke it.<br/>
            /// This one-argument shorthand does not mean a nonzero/any-bit test or an omitted comparison.<br/>
            /// </summary>
            /// <param name="shiftBy">Non-null deferred shift count used by the existing integral evaluator.<br/></param>
            /// <returns>The fluent continuation for combining or completing the zero-comparison condition.<br/></returns>
            /// <exception cref="ArgumentNullException">The operand callback is null.<br/></exception>
            public ContinueOrEndCondition BitShiftL(Func<Num> shiftBy)
                => AddOperation(NumericOperatorSymbolsEnum.ShiftL, shiftBy, Num.Zero);

            /// <summary>
            /// Compares the selected property's left shift result against the numeric type's zero.<br/>
            /// Captures the supplied operand and Num.Zero using the existing numeric condition representation.<br/>
            /// This one-argument shorthand does not mean a nonzero/any-bit test or an omitted comparison.<br/>
            /// </summary>
            /// <param name="shiftBy">Immediate shift count used by the existing integral evaluator.<br/></param>
            /// <returns>The fluent continuation for combining or completing the zero-comparison condition.<br/></returns>
            public ContinueOrEndCondition BitShiftL(Num shiftBy)
                => AddOperation(NumericOperatorSymbolsEnum.ShiftL, shiftBy, Num.Zero);
        }

        #endregion
    }
}
