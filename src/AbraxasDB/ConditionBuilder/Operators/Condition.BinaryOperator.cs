using System;
using System.Buffers.Binary;
using System.Collections.Generic;
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
		/// Fluent binary content comparison and typed byte-slice operations for a selected value.<br/>
		/// </summary>
		/// <remarks>
		/// Operations append fragments to a mutable condition builder; authoring does not enumerate records.<br/>
		/// </remarks>
		public class BinaryOperator
		{
			private readonly Builder builder;
			private readonly ValueOrExpression<string> selectorExpression;

			/// <summary>
			/// Creates the BinaryOperator stage over the supplied condition builder and selector.<br/>
			/// </summary>
			/// <param name="builder">Mutable builder that receives authored predicates.<br/></param>
			/// <param name="selectorExpression">Selected property path or calculated selector intent.<br/></param>
			public BinaryOperator(Builder builder, ValueOrExpression<string> selectorExpression)
			{
				this.builder = builder;
				this.selectorExpression = selectorExpression;
			}

            // ------------------------------------------------------------
            // Helper: construct the BinarySlice(...) expression wrapper
            // ------------------------------------------------------------
            private ValueOrExpression<string> MakeSliceSelector(
                BinarySliceKind sliceType,
                long offset,
                long length,
                Encoding? encoding = null,
                LibraDexTextEncoding? stableEncoding = null)
            {
                var effectiveEncoding = encoding ?? Encoding.UTF8;
                if (sliceType == BinarySliceKind.CustomEncodingString &&
                    stableEncoding is null)
                {
                    _ = LibraDexTextEncoding.TryFrom(effectiveEncoding, out stableEncoding);
                }

                return ((ValueOrExpression<string>)string.Empty).WithSelectorDescriptor(
                    new BinarySliceSelectorDescriptor(
                        selectorExpression,
                        sliceType,
                        offset,
                        length,
                        effectiveEncoding,
                        stableEncoding));
            }

            #region Whole-blob equality (no slice)

            internal sealed class BinaryEqualsOperation : IConditionLeaf,IFastPredicateSource,ILibraDexDerivedConditionSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly ValueOrExpression<byte[]> param;
                private readonly bool isEqual;
				private readonly string? parameterAlias;

				public BinaryEqualsOperation(
					ValueOrExpression<string> selector,
					ValueOrExpression<byte[]> param,
					bool isEqual,
					string? parameterAlias)
                {
                    this.selector = selector;
                    this.param = param;
                    this.isEqual = isEqual;
					this.parameterAlias = parameterAlias;
                }

				public static void AddToBuilder(
					Builder builder,
					ValueOrExpression<string> selector,
					ValueOrExpression<byte[]> param,
					bool isEqual,
					string? parameterAlias)
                {
					builder.AddFragment(new BinaryEqualsOperation(selector, param, isEqual,  parameterAlias));
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryBuildConditionValueReader<byte[]>(builder, selector, options, out var propPath, out var readValue))
                        return false;

                    var expected = builder.MaterializeParameter(param, propPath, parameterAlias);
                    evaluator = ib =>
                    {
                        var candidate = readValue(ib);
                        if (!candidate.HasValue)
                            return false;

                        var eq = StructuralEqual(candidate.Value, expected);
                        return isEqual ? eq : !eq;
                    };
                    return true;
                }

                /// <summary>
                /// Reports the direct property-path index required by this whole-blob comparison without evaluating the operand.<br/>
                /// Described binary slices deliberately fail this direct-index test because their source blob index must first be resolved by the owning store.<br/>
                /// </summary>
                /// <param name="builder">The owning condition builder used to resolve the whole-blob property-path selector.<br/></param>
                /// <param name="indexName">The direct property path when available.<br/></param>
                /// <returns><c>true</c> when the selector is a direct property path; otherwise <c>false</c>.</returns>
                public bool TryGetLibraDexIndexName(Builder builder, out string indexName)
                {
                    indexName = string.Empty;
                    if (!TryExtractPropPath(builder, selector, out var propPath))
                        return false;

                    indexName = propPath;
                    return true;
                }

                /// <summary>
                /// Resolves either a direct whole-blob selector or a backend-neutral byte-array slice to the maintained LibraDex source index that can execute it.<br/>
                /// Slice resolution remains store-owned so persistent and volatile catalog boundaries are respected without creating or implying a separate slice projection.<br/>
                /// </summary>
                /// <param name="builder">The condition builder that owns execution-scoped selector and operand materialization.<br/></param>
                /// <param name="resolver">The bound store's derived-selector resolver.<br/></param>
                /// <param name="indexName">The direct blob path or resolved source blob index name.<br/></param>
                /// <returns><c>true</c> when a compatible maintained blob index identity is available; otherwise <c>false</c>.<br/></returns>
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
                        selector.SelectorDescriptor is BinarySliceSelectorDescriptor slice &&
                        slice.SliceType == BinarySliceKind.ByteArray &&
                        isEqual &&
                        resolver(builder, slice, out indexName))
                    {
                        return true;
                    }

                    indexName = string.Empty;
                    return false;
                }

                /// <summary>
                /// Translates whole-blob equality or inequality into LibraDex's native binary condition descriptor.<br/>
                /// Persisted metadata is validated before the possibly deferred operand is materialized, and negative comparison explicitly excludes the null-key route to preserve Abraxas/Inheto condition semantics.<br/>
                /// </summary>
                /// <param name="builder">The owning Abraxas condition builder.<br/></param>
                /// <param name="indexes">The bound LibraDex identity group.<br/></param>
                /// <param name="condition">The translated completed condition when successful.<br/></param>
                /// <param name="indexName">The direct property-path index name when successful.<br/></param>
                /// <returns><c>true</c> when a compatible blob index and non-null operand are available; otherwise <c>false</c>.</returns>
                public bool TryBuildLibraDexCondition(
                    Builder builder,
                    CatalogIdentityGroupIndexes indexes,
                    out LibraDexConditionEndCondition condition,
                    out string indexName)
                {
                    condition = default!;
                    indexName = string.Empty;
                    if (indexes is null ||
                        !TryExtractPropPath(builder, selector, out var propPath) ||
                        !indexes.TryGetInfo(propPath, out var info) ||
                        info.KeyFamily != CatalogIndexKeyFamily.Blob ||
                        info.IdentityFamily != CatalogIndexIdentityFamily.Scalar)
                    {
                        return false;
                    }

                    var expected = builder.MaterializeParameter(param, propPath, parameterAlias);
                    if (expected is null)
                        return false;

                    indexName = propPath;
                    var op = indexes.Where(propPath).AsBinary;
                    condition = isEqual
                        ? op.EqualTo(expected).EndCondition
                        : op.NotEqualTo(expected).AND.Where(propPath).IsNotNull.EndCondition;
                    return true;
                }

                /// <summary>
                /// Translates direct whole-blob equality or an exactly described byte-array slice equality into LibraDex's native binary condition grammar.<br/>
                /// A slice executes against the already-maintained exact source blob index; no implicit derived index is created, and unsupported inequality or mismatched slice lengths remain on the authoritative semantic fallback.<br/>
                /// </summary>
                /// <param name="builder">The owning Abraxas condition builder.<br/></param>
                /// <param name="indexes">The bound LibraDex identity group.<br/></param>
                /// <param name="resolver">The store-owned resolver that proves the source blob index belongs to this execution catalog.<br/></param>
                /// <param name="condition">The translated completed LibraDex condition when successful.<br/></param>
                /// <param name="indexName">The exact source blob index used by the translated condition.<br/></param>
                /// <returns><c>true</c> when the direct or sliced selector can execute with identical semantics; otherwise <c>false</c>.<br/></returns>
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
                        !isEqual ||
                        selector.SelectorDescriptor is not BinarySliceSelectorDescriptor slice ||
                        slice.SliceType != BinarySliceKind.ByteArray ||
                        slice.Offset is < 0 or > int.MaxValue ||
                        slice.Length is <= 0 or > int.MaxValue ||
                        !TryExtractPropPath(slice.SourceSelector, out var sourcePath) ||
                        !resolver(builder, slice, out var resolvedIndexName) ||
                        !indexes.TryGetInfo(resolvedIndexName, out var info) ||
                        info.KeyFamily != CatalogIndexKeyFamily.Blob ||
                        info.IdentityFamily != CatalogIndexIdentityFamily.Scalar)
                    {
                        return false;
                    }

                    var expected = builder.MaterializeParameter(param, sourcePath, parameterAlias);
                    if (expected is null || expected.LongLength != slice.Length)
                        return false;

                    indexName = resolvedIndexName;
                    condition = indexes
                        .Where(resolvedIndexName)
                        .AsBinary
                        .SliceEqual((int)slice.Offset, expected)
                        .EndCondition;
                    return true;
                }

                /// <summary>
                /// Builds whole-value binary equality or inequality against one exact maintained blob index.<br/>
                /// Operand context remains independent from the generated physical name, while inequality explicitly excludes null to preserve authoritative selector semantics.<br/>
                /// </summary>
                /// <param name="builder">Builder owning execution-scoped operand materialization.<br/></param>
                /// <param name="indexes">Identity group containing the resolved blob index.<br/></param>
                /// <param name="indexName">Exact maintained LibraDex index name.<br/></param>
                /// <param name="operandContext">Stable context supplied to deferred operand materialization.<br/></param>
                /// <param name="condition">Completed native condition when the contract is compatible.<br/></param>
                /// <returns><see langword="true"/> when a scalar-identity blob index and non-null operand are available.<br/></returns>
                private bool TryBuildLibraDexConditionAgainstIndex(
                    Builder builder,
                    CatalogIdentityGroupIndexes indexes,
                    string indexName,
                    string operandContext,
                    out LibraDexConditionEndCondition condition)
                {
                    condition = default!;
                    if (!indexes.TryGetInfo(indexName, out var info) ||
                        info.KeyFamily != CatalogIndexKeyFamily.Blob ||
                        info.IdentityFamily != CatalogIndexIdentityFamily.Scalar)
                    {
                        return false;
                    }

                    byte[] expected = builder.MaterializeParameter(param, operandContext, parameterAlias);
                    if (expected is null)
                        return false;

                    var op = indexes.Where(indexName).AsBinary;
                    condition = isEqual
                        ? op.EqualTo(expected).EndCondition
                        : op.NotEqualTo(expected).AND.Where(indexName).IsNotNull.EndCondition;
                    return true;
                }

                /// <summary>
                /// Resolves this operation's complete immutable selector to a separately maintained calculated binary index.<br/>
                /// A resolver result equal to the direct source path remains assigned to direct or native slice execution.<br/>
                /// </summary>
                /// <param name="builder">Builder owning the frozen selector descriptor.<br/></param>
                /// <param name="resolver">Store-owned calculated projection resolver.<br/></param>
                /// <param name="indexName">Separately maintained calculated index name when available.<br/></param>
                /// <returns><see langword="true"/> only for an exact non-source calculated index match.<br/></returns>
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

                private static bool StructuralEqual(byte[]? left, byte[]? right)
                {
                    if (ReferenceEquals(left, right))
                        return true;
                    if (left is null || right is null)
                        return false;
                    if (left.Length != right.Length)
                        return false;

                    for (int i = 0; i < left.Length; i++)
                    {
                        if (left[i] != right[i])
                            return false;
                    }
                    return true;
                }
            }

            /// <summary>
            /// Adds binary content equality rather than array-reference comparison.<br/>
            /// </summary>
            /// <param name="valueOrExpression">Static, deferred, or parameter-bound comparison bytes.<br/></param>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            public ContinueOrEndCondition EqualTo(byte[] valueOrExpression)
            {
				BinaryEqualsOperation.AddToBuilder(builder, selectorExpression, valueOrExpression, isEqual: true,  null);
                return new ContinueOrEndCondition(builder);
            }
            /// <summary>
            /// Adds binary content equality rather than array-reference comparison.<br/>
            /// </summary>
            /// <param name="valueOrExpression">Factory supplying comparison bytes through execution-scoped materialization.<br/></param>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            public ContinueOrEndCondition EqualTo(Func<byte[]> valueOrExpression)
            {
				BinaryEqualsOperation.AddToBuilder(builder, selectorExpression, valueOrExpression, isEqual: true,  null);
                return new ContinueOrEndCondition(builder);
            }

            /// <summary>
            /// Adds binary content inequality rather than array-reference comparison.<br/>
            /// </summary>
            /// <param name="valueOrExpression">Static, deferred, or parameter-bound comparison bytes.<br/></param>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            public ContinueOrEndCondition NotEqualTo(ValueOrExpression<byte[]> valueOrExpression)
            {
				BinaryEqualsOperation.AddToBuilder(builder, selectorExpression, valueOrExpression, isEqual: false,  null);
                return new ContinueOrEndCondition(builder);
            }

            /// <summary>
            /// Compares the selected binary value with one reusable execution-snapshotted parameter.<br/>
            /// The parameter remains caller-owned and may be reassigned before the next condition execution.<br/>
            /// </summary>
            /// <param name="valueOrExpression">Typed binary parameter whose array reference is captured once for the active execution.<br/></param>
            /// <returns>The fluent continuation for combining or completing the condition.<br/></returns>
            public ContinueOrEndCondition EqualTo(ConditionParameter<byte[]> valueOrExpression)
            {
                ArgumentNullException.ThrowIfNull(valueOrExpression);
                BinaryEqualsOperation.AddToBuilder(
                    builder,
                    selectorExpression,
                    (ValueOrExpression<byte[]>)valueOrExpression,
                    isEqual: true,
                    null);
                return new ContinueOrEndCondition(builder);
            }

            /// <summary>
            /// Selects present binary values whose logical length is zero.<br/>
            /// The shared empty array introduces no per-condition payload allocation and maps to LibraDex's dedicated empty-binary route when indexed.<br/>
            /// Null and missing values remain distinct and excluded.<br/>
            /// </summary>
            /// <returns>The continuation used to compose or complete the condition.<br/></returns>
            public ContinueOrEndCondition IsEmpty() => EqualTo(Array.Empty<byte>());

            /// <summary>
            /// Selects present binary values whose logical length is greater than zero.<br/>
            /// The shared empty array introduces no per-condition payload allocation, and the existing inequality contract continues to exclude null and missing values.<br/>
            /// </summary>
            /// <returns>The continuation used to compose or complete the condition.<br/></returns>
            public ContinueOrEndCondition IsNotEmpty()
            {
                BinaryEqualsOperation.AddToBuilder(
                    builder,
                    selectorExpression,
                    (ValueOrExpression<byte[]>)Array.Empty<byte>(),
                    isEqual: false,
                    parameterAlias: null);
                return new ContinueOrEndCondition(builder);
            }

            /// <summary>
            /// Adds binary content equality rather than array-reference comparison.<br/>
            /// </summary>
            /// <param name="stream">Caller-owned stream whose entire logical contents are copied at execution-time parameter materialization, independently of Position.<br/></param>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// Keep the backing memory or stream available until execution has materialized the operand. No copy is made by this authoring call.<br/>
            /// </remarks>
            public ContinueOrEndCondition EqualTo(MemoryStream stream)
            {
                return EqualTo(new Func<byte[]>(() =>
                {
                    if (stream is null) return null!;
                    if (stream.TryGetBuffer(out ArraySegment<byte> seg))
                        return seg.AsSpan().ToArray();

                    return stream.ToArray();
                }));
            }

            /// <summary>
            /// Adds binary content equality rather than array-reference comparison.<br/>
            /// </summary>
            /// <param name="memory">Caller-owned memory whose selected range is copied at execution-time parameter materialization.<br/></param>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// Keep the backing memory or stream available until execution has materialized the operand. No copy is made by this authoring call.<br/>
            /// </remarks>
            public ContinueOrEndCondition EqualTo(Memory<byte> memory)
            {
                return EqualTo(new Func<byte[]>(() =>
                {
                    return memory.Span.ToArray();     // Copies the selected range only when execution materializes the operand.
                }));
            }

            /// <summary>
            /// Adds binary content equality rather than array-reference comparison.<br/>
            /// </summary>
            /// <param name="memory">Caller-owned memory whose selected range is copied at execution-time parameter materialization.<br/></param>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// Keep the backing memory or stream available until execution has materialized the operand. No copy is made by this authoring call.<br/>
            /// </remarks>
            public ContinueOrEndCondition EqualTo(ReadOnlyMemory<byte> memory)
            {
                return EqualTo(new Func<byte[]>(() =>
                {
                    return memory.Span.ToArray();
                }));
            }

            // -------------------------------------------------
            // NotEqual versions
            // -------------------------------------------------
            /// <summary>
            /// Adds binary content inequality rather than array-reference comparison.<br/>
            /// </summary>
            /// <param name="stream">Caller-owned stream whose entire logical contents are copied at execution-time parameter materialization, independently of Position.<br/></param>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// Keep the backing memory or stream available until execution has materialized the operand. No copy is made by this authoring call.<br/>
            /// </remarks>
            public ContinueOrEndCondition NotEqualTo(MemoryStream stream)
            {
                return NotEqualTo(new Func<byte[]>(() =>
                {
                    if (stream is null) return null!;
                    if (stream.TryGetBuffer(out ArraySegment<byte> seg))
                        return seg.AsSpan().ToArray();

                    return stream.ToArray();
                }));
            }

            /// <summary>
            /// Adds binary content inequality rather than array-reference comparison.<br/>
            /// </summary>
            /// <param name="memory">Caller-owned memory whose selected range is copied at execution-time parameter materialization.<br/></param>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// Keep the backing memory or stream available until execution has materialized the operand. No copy is made by this authoring call.<br/>
            /// </remarks>
            public ContinueOrEndCondition NotEqualTo(Memory<byte> memory)
            {
                return NotEqualTo(new Func<byte[]>(() =>
                {
                    return memory.Span.ToArray();
                }));
            }

            /// <summary>
            /// Adds binary content inequality rather than array-reference comparison.<br/>
            /// </summary>
            /// <param name="memory">Caller-owned memory whose selected range is copied at execution-time parameter materialization.<br/></param>
            /// <returns>A fluent continuation over the same builder.<br/></returns>
            /// <remarks>
            /// Keep the backing memory or stream available until execution has materialized the operand. No copy is made by this authoring call.<br/>
            /// </remarks>
            public ContinueOrEndCondition NotEqualTo(ReadOnlyMemory<byte> memory)
            {
                return NotEqualTo(new Func<byte[]>(() =>
                {
                    return memory.Span.ToArray();
                }));
            }

            /// <summary>Compares this binary property for content equality with a textual right-hand property completed by <c>.AsBinary</c>.<br/></summary>
            public BinaryPropertyComparisonTypeSelector PropEqualTo(string propPath)
                => new(builder, selectorExpression, propPath, PropertyComparisonKind.Equal);

            /// <summary>Compares this binary property for content equality with a typed right-hand property.<br/></summary>
            public ContinueOrEndCondition PropEqualTo(Expression<Func<T, byte[]>> propSelector)
                => AddPropertyComparison<byte[]>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.Equal);

            /// <summary>Compares this binary property for content inequality with a textual right-hand property completed by <c>.AsBinary</c>.<br/></summary>
            public BinaryPropertyComparisonTypeSelector PropNotEqualTo(string propPath)
                => new(builder, selectorExpression, propPath, PropertyComparisonKind.NotEqual);

            /// <summary>Compares this binary property for content inequality with a typed right-hand property.<br/></summary>
            public ContinueOrEndCondition PropNotEqualTo(Expression<Func<T, byte[]>> propSelector)
                => AddPropertyComparison<byte[]>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.NotEqual);


            #endregion

            // ------------------------------------------------------------
            // Slice-as-numeric (these go to NumericOperator)
            // Lengths assume your BinarySliceFunc layout (LE, fixed-size)
            // ------------------------------------------------------------

			/// <summary>
			/// Interprets one signed byte at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// </remarks>
			public NumericOperator<sbyte> SlicedAsSByte(long offset)
				=> new NumericOperator<sbyte>(builder,
					MakeSliceSelector(BinarySliceKind.Int8, offset, 1));

			/// <summary>
			/// Interprets one unsigned byte at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// </remarks>
			public NumericOperator<byte> SlicedAsByte(long offset)
				=> new NumericOperator<byte>(builder,
					MakeSliceSelector(BinarySliceKind.UInt8, offset, 1));

			/// <summary>
			/// Interprets two bytes in the runtime's raw Int16 layout at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// </remarks>
			public NumericOperator<short> SlicedAsInt16(long offset)
				=> new NumericOperator<short>(builder,
					MakeSliceSelector(BinarySliceKind.Int16, offset, 2));

			/// <summary>
			/// Interprets two bytes in the runtime's raw UInt16 layout at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// </remarks>
			public NumericOperator<ushort> SlicedAsUInt16(long offset)
				=> new NumericOperator<ushort>(builder,
					MakeSliceSelector(BinarySliceKind.UInt16, offset, 2));

			/// <summary>
			/// Interprets four bytes in the runtime's raw Int32 layout at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// </remarks>
			public NumericOperator<int> SlicedAsInt32(long offset)
				=> new NumericOperator<int>(builder,
					MakeSliceSelector(BinarySliceKind.Int32, offset, 4));

			/// <summary>
			/// Interprets four bytes in the runtime's raw UInt32 layout at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// </remarks>
			public NumericOperator<uint> SlicedAsUInt32(long offset)
				=> new NumericOperator<uint>(builder,
					MakeSliceSelector(BinarySliceKind.UInt32, offset, 4));

			/// <summary>
			/// Interprets eight bytes in the runtime's raw Int64 layout at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// </remarks>
			public NumericOperator<long> SlicedAsInt64(long offset)
				=> new NumericOperator<long>(builder,
					MakeSliceSelector(BinarySliceKind.Int64, offset, 8));

			/// <summary>
			/// Interprets eight bytes in the runtime's raw UInt64 layout at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// </remarks>
			public NumericOperator<ulong> SlicedAsUInt64(long offset)
				=> new NumericOperator<ulong>(builder,
					MakeSliceSelector(BinarySliceKind.UInt64, offset, 8));

			/// <summary>
			/// Interprets four bytes in the runtime's raw Single layout at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// </remarks>
			public NumericOperator<float> SlicedAsSingle(long offset)
				=> new NumericOperator<float>(builder,
					MakeSliceSelector(BinarySliceKind.Single, offset, 4));

			/// <summary>
			/// Interprets eight bytes in the runtime's raw Double layout at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// </remarks>
			public NumericOperator<double> SlicedAsDouble(long offset)
				=> new NumericOperator<double>(builder,
					MakeSliceSelector(BinarySliceKind.Double, offset, 8));

			/// <summary>
			/// Interprets 16 bytes in the runtime's raw Decimal layout at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// This is the raw CLR Decimal memory layout, not the four-Int32 sequence returned by decimal.GetBits.<br/>
			/// </remarks>
			public NumericOperator<decimal> SlicedAsDecimal(long offset)
				=> new NumericOperator<decimal>(builder,
					MakeSliceSelector(BinarySliceKind.Decimal, offset, 16));

            // If you wired Int128/UInt128/BigInteger into BinarySliceFunc, you can
            // route them either to NumericOperator (for equality / ordering where
            // serialize them to text for correct ordering). Here we assume numeric:
			/// <summary>
			/// Interprets 16 bytes in the runtime's raw Int128 layout at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// </remarks>
			public NumericOperator<Int128> SlicedAsInt128(long offset)
				=> new NumericOperator<Int128>(builder,
					MakeSliceSelector(BinarySliceKind.Int128, offset, 16));

			/// <summary>
			/// Interprets 16 bytes in the runtime's raw UInt128 layout at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// </remarks>
			public NumericOperator<UInt128> SlicedAsUInt128(long offset)
				=> new NumericOperator<UInt128>(builder,
					MakeSliceSelector(BinarySliceKind.UInt128, offset, 16));

			/// <summary>
			/// Interprets a signed, little-endian two's-complement integer of the specified byte length at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <param name="length">Positive number of source bytes to interpret, not a character count.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// </remarks>
			public NumericOperator<BigInteger> SlicedAsBigInteger(long offset, long length)
				=> new NumericOperator<BigInteger>(builder,
					MakeSliceSelector(BinarySliceKind.BigInteger, offset, length));

            // ------------------------------------------------------------
            // Slice-as-string (UTF-8 / Latin1 / UTF-16 / UTF-32 / etc.)
            // These go to StringOperator
            // ------------------------------------------------------------

			/// <summary>
			/// Interprets the specified UTF-8 byte range at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <param name="byteLength">Positive number of source bytes to interpret, not a character count.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// </remarks>
			public StringOperator SlicedAsUtf8String(long offset, long byteLength)
				=> new StringOperator(builder,
					MakeSliceSelector(BinarySliceKind.Utf8String, offset, byteLength));

			/// <summary>
			/// Interprets the specified Latin-1 byte range at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <param name="byteLength">Positive number of source bytes to interpret, not a character count.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// </remarks>
			public StringOperator SlicedAsLatin1String(long offset, long byteLength)
				=> new StringOperator(builder,
					MakeSliceSelector(BinarySliceKind.Latin1String, offset, byteLength));

			/// <summary>
			/// Interprets the specified little-endian UTF-16 byte range at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <param name="byteLength">Positive number of source bytes to interpret, not a character count.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// </remarks>
			public StringOperator SlicedAsUtf16String(long offset, long byteLength)
				=> new StringOperator(builder,
					MakeSliceSelector(BinarySliceKind.Utf16String, offset, byteLength));

			/// <summary>
			/// Interprets the specified little-endian UTF-32 byte range at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <param name="byteLength">Positive number of source bytes to interpret, not a character count.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// </remarks>
			public StringOperator SlicedAsUtf32String(long offset, long byteLength)
				=> new StringOperator(builder,
					MakeSliceSelector(BinarySliceKind.Utf32String, offset, byteLength));

            /// <summary>
            /// Interprets a byte slice using the supplied runtime encoding and its existing fallback behavior.<br/>
            /// Recognized code pages and standard fallbacks can be captured durably; custom implementations or fallbacks remain runtime-only.<br/>
            /// </summary>
            /// <param name="offset">Zero-based byte offset within the selected value.<br/></param>
            /// <param name="byteLength">Number of encoded bytes to interpret.<br/></param>
            /// <param name="encoding">The runtime encoding; the existing null behavior uses UTF-8.<br/></param>
            /// <returns>The ordinary string condition operator over the interpreted slice.<br/></returns>
			public StringOperator SlicedAsEncodedString(long offset, long byteLength, Encoding encoding)
				=> new StringOperator(builder,
					MakeSliceSelector(BinarySliceKind.CustomEncodingString, offset, byteLength, encoding));

            /// <summary>
            /// Interprets a bounded byte slice using a reproducible numeric code page and explicit fallback policy.<br/>
            /// The numeric code page and explicit malformed-input policy can be persisted and replayed without relying on a process-local <see cref="Encoding"/> implementation.<br/>
            /// </summary>
            /// <param name="offset">The zero-based byte offset within the selected binary value.<br/></param>
            /// <param name="byteLength">The positive number of bytes interpreted as encoded text.<br/></param>
            /// <param name="codePage">A registered numeric .NET code page; unsupported pages fail at authoring time.<br/></param>
            /// <param name="fallback">Malformed-input behavior; the default strict policy rejects malformed slices.<br/></param>
            /// <returns>A string operator bound to the immutable custom-encoding slice descriptor.<br/></returns>
            public StringOperator SlicedAsEncodedString(
                long offset,
                long byteLength,
                int codePage,
                BinaryTextFallback fallback = BinaryTextFallback.Strict)
            {
                Coercion.Text policy = fallback switch
                {
                    BinaryTextFallback.Strict => Coercion.Text.Strict,
                    BinaryTextFallback.Replacement => Coercion.Text.DotNetReplacement,
                    _ => throw new ArgumentOutOfRangeException(nameof(fallback), fallback, "Unknown binary text fallback policy.")
                };
                LibraDexTextEncoding encoding = LibraDexTextEncoding.ForCodePage(codePage, policy);
                return new StringOperator(
                    builder,
                    MakeSliceSelector(
                        BinarySliceKind.CustomEncodingString,
                        offset,
                        byteLength,
                        stableEncoding: encoding));
            }

            /// <summary>
            /// Interprets a bounded byte slice as ASCII text and exposes the ordinary Abraxas string-condition surface.<br/>
            /// </summary>
            /// <param name="offset">The zero-based byte offset within the selected binary value.<br/></param>
            /// <param name="byteLength">The positive number of bytes interpreted as ASCII text.<br/></param>
            /// <returns>A string operator bound to the immutable ASCII slice descriptor.<br/></returns>
            public StringOperator SlicedAsAsciiString(long offset, long byteLength)
                => new StringOperator(builder,
                    MakeSliceSelector(BinarySliceKind.AsciiString, offset, byteLength));

            // ------------------------------------------------------------
            // Slice-as-char / rune  (still string-ish; route to StringOperator)
            // ------------------------------------------------------------

			/// <summary>
			/// Interprets one two-byte character in the runtime's raw Char layout at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// </remarks>
			public StringOperator SlicedAsCharUtf16(long offset)
				=> new StringOperator(builder,
					MakeSliceSelector(BinarySliceKind.CharUtf16, offset, 2));

			/// <summary>
			/// Interprets one four-byte Unicode scalar in the runtime's raw Int32 layout at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// </remarks>
			public StringOperator SlicedAsRuneUtf32(long offset)
				=> new StringOperator(builder,
					MakeSliceSelector(BinarySliceKind.RuneUtf32, offset, 4));

            // ------------------------------------------------------------
            // Slice-as-Guid
            // You usually just want equality or pass into GuidOperator if desired.
            // For now route to StringOperator or NumericOperator depending on how
            // BinarySliceFunc returns it (BLOB vs TEXT vs packed numeric).
            // Here we assume BLOB GUID and use StringOperator for pattern helpers
            // only if you later add a Guid→string projection; otherwise equality
            // is typically done via NumericOperator or direct EqualTo on the blob.
            // ------------------------------------------------------------

			/// <summary>
			/// Interprets 16 bytes in the .NET Guid byte layout at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// The byte order matches Guid.ToByteArray, not canonical displayed hexadecimal order.<br/>
			/// </remarks>
			public GuidOperator SlicedAsGuid(long offset)
				=> new GuidOperator(builder,
					MakeSliceSelector(BinarySliceKind.Guid, offset, 16));

            // ------------------------------------------------------------
            // Date / Time slices → Date/Time operators
            // (BinarySliceFunc returns your structured packed longs)
            // ------------------------------------------------------------

			/// <summary>
			/// Interprets eight raw Int64 tick bytes as a UTC DateTime at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// </remarks>
			public DateTimeOperator SlicedAsDateTime(long offset)
				=> new DateTimeOperator(builder,
					MakeSliceSelector(BinarySliceKind.DateTimeTicks, offset, 8));

			/// <summary>
			/// Interprets four raw Int32 day-number bytes as a DateOnly at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// </remarks>
			public DateOnlyOperator SlicedAsDateOnly(long offset)
				=> new DateOnlyOperator(builder,
					MakeSliceSelector(BinarySliceKind.DateOnly, offset, 4));

			/// <summary>
			/// Interprets eight raw Int64 tick bytes as a TimeOnly at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// </remarks>
			public TimeOnlyOperator SlicedAsTimeOnly(long offset)
				=> new TimeOnlyOperator(builder,
					MakeSliceSelector(BinarySliceKind.TimeOnly, offset, 8));

			/// <summary>
			/// Interprets eight raw Int64 tick bytes as a TimeSpan at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// </remarks>
			public TimeSpanOperator SlicedAsTimeSpan(long offset)
				=> new TimeSpanOperator(builder,
					MakeSliceSelector(BinarySliceKind.TimeSpanTicks, offset, 8));

			/// <summary>
			/// Interprets 16 bytes containing raw Int64 local ticks followed by raw Int64 offset ticks, viewed as UTC DateTime at the selected binary offset.<br/>
			/// </summary>
			/// <param name="offset">Zero-based byte offset into the selected binary value.<br/></param>
			/// <returns>A typed condition-operator stage over the binary slice.<br/></returns>
			/// <remarks>
			/// The supplied representation is explicit; it is not inferred from the payload. Source bounds and value validity are evaluated when the condition is consumed.<br/>
			/// The result exposes UTC DateTime operators; the original offset is not the comparison result.<br/>
			/// </remarks>
			public DateTimeOperator SlicedAsDateTimeOffset(long offset)
				=> new DateTimeOperator(builder,
					MakeSliceSelector(BinarySliceKind.DateTimeOffsetPair, offset, 16));

            /// <summary>
            /// Interprets eight bytes at the selected offset as a <see cref="DateTime"/> using the explicitly selected raw .NET or LibraDex SDT representation.<br/>
            /// The representation is never inferred from the payload; the parameterless overload remains the low-friction raw .NET tick form.<br/>
            /// </summary>
            /// <param name="offset">The zero-based byte offset within the selected binary property.<br/></param>
            /// <param name="encoding">The exact physical DateTime representation stored at the selected offset.<br/></param>
            /// <returns>The ordinary DateTime condition operator over the decoded value.<br/></returns>
            public DateTimeOperator SlicedAsDateTime(long offset, BinaryDateTimeEncoding encoding)
                => new(
                    builder,
                    MakeSliceSelector(
                        encoding switch
                        {
                            BinaryDateTimeEncoding.CalendarSdt => BinarySliceKind.DateTimeCalendarSdt,
                            BinaryDateTimeEncoding.PrecisionSdt => BinarySliceKind.DateTimePrecisionSdt,
                            BinaryDateTimeEncoding.DotNetTicks => BinarySliceKind.DateTimeTicks,
                            _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Unknown binary DateTime encoding.")
                        },
                        offset,
                        sizeof(ulong)));

            /// <summary>
            /// Interprets bytes at the selected offset as a <see cref="DateOnly"/> using the explicitly selected raw .NET or LibraDex SDT representation.<br/>
            /// Raw .NET day numbers consume four bytes; both SDT representations consume one big-endian eight-byte scalar.<br/>
            /// </summary>
            /// <param name="offset">The zero-based byte offset within the selected binary property.<br/></param>
            /// <param name="encoding">The exact physical DateOnly representation stored at the selected offset.<br/></param>
            /// <returns>The ordinary DateOnly condition operator over the decoded value.<br/></returns>
            public DateOnlyOperator SlicedAsDateOnly(long offset, BinaryDateOnlyEncoding encoding)
                => new(
                    builder,
                    MakeSliceSelector(
                        encoding switch
                        {
                            BinaryDateOnlyEncoding.CalendarSdt => BinarySliceKind.DateOnlyCalendarSdt,
                            BinaryDateOnlyEncoding.PrecisionSdt => BinarySliceKind.DateOnlyPrecisionSdt,
                            BinaryDateOnlyEncoding.DotNetDayNumber => BinarySliceKind.DateOnly,
                            _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Unknown binary DateOnly encoding.")
                        },
                        offset,
                        encoding == BinaryDateOnlyEncoding.DotNetDayNumber ? sizeof(int) : sizeof(ulong)));

            /// <summary>
            /// Interprets eight bytes at the selected offset as a <see cref="TimeOnly"/> using the explicitly selected raw .NET or LibraDex SDT representation.<br/>
            /// </summary>
            /// <param name="offset">The zero-based byte offset within the selected binary property.<br/></param>
            /// <param name="encoding">The exact physical TimeOnly representation stored at the selected offset.<br/></param>
            /// <returns>The ordinary TimeOnly condition operator over the decoded value.<br/></returns>
            public TimeOnlyOperator SlicedAsTimeOnly(long offset, BinaryTimeOnlyEncoding encoding)
                => new(
                    builder,
                    MakeSliceSelector(
                        encoding switch
                        {
                            BinaryTimeOnlyEncoding.CalendarSdt => BinarySliceKind.TimeOnlyCalendarSdt,
                            BinaryTimeOnlyEncoding.PrecisionSdt => BinarySliceKind.TimeOnlyPrecisionSdt,
                            BinaryTimeOnlyEncoding.DotNetTicks => BinarySliceKind.TimeOnly,
                            _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Unknown binary TimeOnly encoding.")
                        },
                        offset,
                        sizeof(ulong)));

            /// <summary>
            /// Interprets eight bytes at the selected offset as a <see cref="TimeSpan"/> using raw little-endian .NET ticks or LibraDex's big-endian ordered-tick scalar.<br/>
            /// Ordered ticks preserve the complete signed duration order and therefore support all positive native comparisons without candidate materialization.<br/>
            /// </summary>
            /// <param name="offset">The zero-based byte offset within the selected binary property.<br/></param>
            /// <param name="encoding">The exact physical TimeSpan representation stored at the selected offset.<br/></param>
            /// <returns>The ordinary TimeSpan condition operator over the decoded value.<br/></returns>
            public TimeSpanOperator SlicedAsTimeSpan(long offset, BinaryTimeSpanEncoding encoding)
                => new(
                    builder,
                    MakeSliceSelector(
                        encoding switch
                        {
                            BinaryTimeSpanEncoding.OrderedTicks => BinarySliceKind.TimeSpanOrderedTicks,
                            BinaryTimeSpanEncoding.DotNetTicks => BinarySliceKind.TimeSpanTicks,
                            _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Unknown binary TimeSpan encoding.")
                        },
                        offset,
                        sizeof(ulong)));

            /// <summary>
            /// Interprets bytes at the selected offset as a UTC <see cref="DateTimeOffset"/> instant using the explicitly selected raw pair or LibraDex SDT representation.<br/>
            /// For compatibility with the established Abraxas surface, the returned <see cref="DateTimeOperator"/> exposes the represented UTC <see cref="DateTime"/>; SDT encodings intentionally do not retain the original offset.<br/>
            /// </summary>
            /// <param name="offset">The zero-based byte offset within the selected binary property.<br/></param>
            /// <param name="encoding">The exact physical DateTimeOffset representation stored at the selected offset.<br/></param>
            /// <returns>The ordinary DateTime condition operator over the represented UTC instant.<br/></returns>
            public DateTimeOperator SlicedAsDateTimeOffset(long offset, BinaryDateTimeOffsetEncoding encoding)
                => new(
                    builder,
                    MakeSliceSelector(
                        encoding switch
                        {
                            BinaryDateTimeOffsetEncoding.CalendarSdtUtc => BinarySliceKind.DateTimeOffsetCalendarSdtUtc,
                            BinaryDateTimeOffsetEncoding.PrecisionSdtUtc => BinarySliceKind.DateTimeOffsetPrecisionSdtUtc,
                            BinaryDateTimeOffsetEncoding.DotNetTicksAndOffset => BinarySliceKind.DateTimeOffsetPair,
                            _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "Unknown binary DateTimeOffset encoding.")
                        },
                        offset,
                        encoding == BinaryDateTimeOffsetEncoding.DotNetTicksAndOffset ? sizeof(long) * 2 : sizeof(ulong)));

            // ------------------------------------------------------------
            // Raw byte[] slice (still useful for equality or InSet-like logic)
            // This returns a "binary-as-blob" selector; you'll generally then use
            // EqualTo/NotEqualTo on this via Numeric/String operators or a future
            // dedicated BinaryEquals operator if you add one.
            // ------------------------------------------------------------

            // maybe for later
            //public NumericOperator SlicedAsByteArray(long offset, long length)
            //    => new NumericOperator(builder,
            //        MakeSliceSelector(BinarySliceKind.ByteArray, offset, length));
        }

        /// <summary>
        /// </summary>
        internal sealed class BinarySliceSelectorDescriptor : IBackendNeutralSelectorDescriptor
        {
            internal ValueOrExpression<string> SourceSelector { get; }
            internal BinarySliceKind SliceType { get; }
            internal long Offset { get; }
            internal long Length { get; }
            internal Encoding Encoding { get; }
            internal LibraDexTextEncoding? StableEncoding { get; }

            /// <summary>
            /// Initializes an immutable backend-neutral binary-slice description.<br/>
            /// </summary>
            /// <param name="sourceSelector">The unsliced source-property selector.<br/></param>
            /// <param name="sliceType">The deterministic conversion applied to the selected bytes.<br/></param>
            /// <param name="offset">The zero-based byte offset within the source blob.<br/></param>
            /// <param name="length">The requested byte length, including fixed widths supplied by public helpers.<br/></param>
            /// <param name="encoding">The encoding used only by custom-encoded string slices.<br/></param>
            /// <param name="stableEncoding">The reproducible LibraDex encoding contract when the custom encoding can be durably recreated.<br/></param>
            internal BinarySliceSelectorDescriptor(
                ValueOrExpression<string> sourceSelector,
                BinarySliceKind sliceType,
                long offset,
                long length,
                Encoding encoding,
                LibraDexTextEncoding? stableEncoding = null)
            {
                SourceSelector = sourceSelector;
                SliceType = sliceType;
                Offset = offset;
                Length = length;
                Encoding = encoding;
                StableEncoding = stableEncoding;
            }

            /// <summary>
            /// Attempts to convert the configured byte slice to the CLR value type expected by the consuming condition operator.<br/>
            /// </summary>
            /// <typeparam name="TValue">The CLR value type expected by the consuming condition operator.<br/></typeparam>
            /// <param name="source">The source binary property restored from Inheto.<br/></param>
            /// <param name="value">The converted value when successful.<br/></param>
            /// <returns>True when bounds, conversion, and requested result type are valid; otherwise false.<br/></returns>
            internal bool TryRead<TValue>(byte[]? source, out TValue value)
            {
                value = default!;
                if (source is null ||
                    source.Length == 0 ||
                    Offset < 0 ||
                    Offset > int.MaxValue ||
                    Length < 0 ||
                    Length > int.MaxValue)
                {
                    return false;
                }

                var offset = (int)Offset;
                var length = (int)Length;
                if (offset >= source.Length || (length > 0 && length > source.Length - offset))
                    return false;

                try
                {
                    var span = source.AsSpan();
                    object? candidate = SliceType switch
                    {
                        BinarySliceKind.Int8 => unchecked((sbyte)span[offset]),
                        BinarySliceKind.UInt8 => span[offset],
                        BinarySliceKind.Int16 => MemoryMarshal.Read<short>(span[offset..]),
                        BinarySliceKind.UInt16 => MemoryMarshal.Read<ushort>(span[offset..]),
                        BinarySliceKind.Int32 => MemoryMarshal.Read<int>(span[offset..]),
                        BinarySliceKind.UInt32 => MemoryMarshal.Read<uint>(span[offset..]),
                        BinarySliceKind.Int64 => MemoryMarshal.Read<long>(span[offset..]),
                        BinarySliceKind.UInt64 => MemoryMarshal.Read<ulong>(span[offset..]),
                        BinarySliceKind.Int128 => MemoryMarshal.Read<Int128>(span[offset..]),
                        BinarySliceKind.UInt128 => MemoryMarshal.Read<UInt128>(span[offset..]),
                        BinarySliceKind.BigInteger => new BigInteger(span.Slice(offset, length)),
                        BinarySliceKind.Single => MemoryMarshal.Read<float>(span[offset..]),
                        BinarySliceKind.Double => MemoryMarshal.Read<double>(span[offset..]),
                        BinarySliceKind.Decimal => MemoryMarshal.Read<decimal>(span[offset..]),
                        BinarySliceKind.CharUtf16 => MemoryMarshal.Read<char>(span[offset..]).ToString(),
                        BinarySliceKind.RuneUtf32 => new Rune(MemoryMarshal.Read<int>(span[offset..])).ToString(),
                        BinarySliceKind.Utf8String => System.Text.Encoding.UTF8.GetString(span.Slice(offset, length)),
                        BinarySliceKind.Utf16String => System.Text.Encoding.Unicode.GetString(span.Slice(offset, length)),
                        BinarySliceKind.Utf32String => System.Text.Encoding.UTF32.GetString(span.Slice(offset, length)),
                        BinarySliceKind.AsciiString => System.Text.Encoding.ASCII.GetString(span.Slice(offset, length)),
                        BinarySliceKind.Latin1String => System.Text.Encoding.Latin1.GetString(span.Slice(offset, length)),
                        BinarySliceKind.CustomEncodingString => StableEncoding is not null
                            ? StableEncoding.Decode(span.Slice(offset, length))
                            : Encoding.GetString(span.Slice(offset, length)),
                        BinarySliceKind.Guid => new Guid(span.Slice(offset, 16)),
                        BinarySliceKind.DateTimeTicks => new DateTime(MemoryMarshal.Read<long>(span[offset..]), DateTimeKind.Utc),
                        BinarySliceKind.DateOnly => DateOnly.FromDayNumber(MemoryMarshal.Read<int>(span[offset..])),
                        BinarySliceKind.TimeOnly => TimeOnly.FromTimeSpan(TimeSpan.FromTicks(MemoryMarshal.Read<long>(span[offset..]))),
                        BinarySliceKind.TimeSpanTicks => TimeSpan.FromTicks(MemoryMarshal.Read<long>(span[offset..])),
                        BinarySliceKind.DateTimeOffsetPair => new DateTimeOffset(
                            MemoryMarshal.Read<long>(span[offset..]),
                            TimeSpan.FromTicks(MemoryMarshal.Read<long>(span[(offset + 8)..]))).UtcDateTime,
                        BinarySliceKind.DateTimeCalendarSdt => LibraDexTemporalScalarCodec.DecodeDateTime(
                            BinaryPrimitives.ReadUInt64BigEndian(span[offset..]),
                            DateTimeKeyEncoding.CalendarSdt),
                        BinarySliceKind.DateTimePrecisionSdt => LibraDexTemporalScalarCodec.DecodeDateTime(
                            BinaryPrimitives.ReadUInt64BigEndian(span[offset..]),
                            DateTimeKeyEncoding.PrecisionSdt),
                        BinarySliceKind.DateOnlyCalendarSdt => LibraDexTemporalScalarCodec.DecodeDateOnly(
                            BinaryPrimitives.ReadUInt64BigEndian(span[offset..]),
                            DateTimeKeyEncoding.CalendarSdt),
                        BinarySliceKind.DateOnlyPrecisionSdt => LibraDexTemporalScalarCodec.DecodeDateOnly(
                            BinaryPrimitives.ReadUInt64BigEndian(span[offset..]),
                            DateTimeKeyEncoding.PrecisionSdt),
                        BinarySliceKind.TimeOnlyCalendarSdt => LibraDexTemporalScalarCodec.DecodeTimeOnly(
                            BinaryPrimitives.ReadUInt64BigEndian(span[offset..]),
                            DateTimeKeyEncoding.CalendarSdt),
                        BinarySliceKind.TimeOnlyPrecisionSdt => LibraDexTemporalScalarCodec.DecodeTimeOnly(
                            BinaryPrimitives.ReadUInt64BigEndian(span[offset..]),
                            DateTimeKeyEncoding.PrecisionSdt),
                        BinarySliceKind.TimeSpanOrderedTicks => LibraDexTemporalScalarCodec.DecodeTimeSpan(
                            BinaryPrimitives.ReadUInt64BigEndian(span[offset..])),
                        BinarySliceKind.DateTimeOffsetCalendarSdtUtc => LibraDexTemporalScalarCodec.DecodeDateTimeOffset(
                            BinaryPrimitives.ReadUInt64BigEndian(span[offset..]),
                            DateTimeKeyEncoding.CalendarSdt).UtcDateTime,
                        BinarySliceKind.DateTimeOffsetPrecisionSdtUtc => LibraDexTemporalScalarCodec.DecodeDateTimeOffset(
                            BinaryPrimitives.ReadUInt64BigEndian(span[offset..]),
                            DateTimeKeyEncoding.PrecisionSdt).UtcDateTime,
                        BinarySliceKind.ByteArray => span.Slice(offset, length).ToArray(),
                        _ => null
                    };

                    if (candidate is not TValue typed)
                        return false;

                    value = typed;
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            /// <summary>
            /// Evaluates the recursive binary source against one authoritative record and converts only the configured slice.<br/>
            /// A direct <c>AsUtf8</c> source borrows Inheto's natural string payload and copies only the requested byte range; other computed binary sources reuse their backend-neutral readers.<br/>
            /// </summary>
            /// <typeparam name="TValue">Exact CLR result type expected by the consuming selector or condition.<br/></typeparam>
            /// <param name="binary">Authoritative Inheto record payload.<br/></param>
            /// <param name="options">Active deserialization registrations.<br/></param>
            /// <param name="value">Converted slice when the recursive source and bounds are valid.<br/></param>
            /// <returns><see langword="true"/> when a present source produced the requested slice; otherwise <see langword="false"/>.<br/></returns>
            internal bool TryRead<TValue>(
                Inheto.InhetoBinary binary,
                Inheto.DeserializationOptions options,
                out TValue value)
            {
                value = default!;
                IBackendNeutralSelectorDescriptor? descriptor = SourceSelector.SelectorDescriptor;
                if (descriptor is Selector<T>.StringOperationSelectorDescriptor text &&
                    text.TryReadUtf8Payload(binary, options, out ReadOnlyMemory<byte> utf8))
                {
                    return TryRead(utf8.Span, out value);
                }

                byte[]? source = null;
                if (descriptor is DirectPropertySelectorDescriptor direct)
                {
                    if (!Condition<T>.HasConditionValue(binary, direct.PropertyPath, options))
                        return false;
                    source = binary.ReadPropAs<byte[]>(direct.PropertyPath, options);
                }
                else if (descriptor is Selector<T>.StringOperationSelectorDescriptor stringOperation)
                {
                    if (!stringOperation.TryRead(binary, options, out source))
                        return false;
                }
                else if (descriptor is Selector<T>.BuiltInSelectorDescriptor builtIn)
                {
                    if (!builtIn.TryRead(binary, options, out source))
                        return false;
                }
                else if (descriptor is BinarySliceSelectorDescriptor slice)
                {
                    if (!slice.TryRead(binary, options, out source))
                        return false;
                }
                else
                {
                    return false;
                }
                return TryRead(source, out value);
            }

            /// <summary>
            /// Converts a borrowed byte span without first copying the complete source payload.<br/>
            /// </summary>
            private bool TryRead<TValue>(ReadOnlySpan<byte> source, out TValue value)
            {
                value = default!;
                if (source.Length == 0 ||
                    Offset < 0 || Offset > int.MaxValue ||
                    Length < 0 || Length > int.MaxValue)
                    return false;

                int offset = (int)Offset;
                int length = (int)Length;
                if (offset >= source.Length || (length > 0 && length > source.Length - offset))
                    return false;
                object? candidate = SliceType == BinarySliceKind.ByteArray
                    ? source.Slice(offset, length).ToArray()
                    : null;
                if (candidate is not TValue typed)
                    return false;
                value = typed;
                return true;
            }
        }

        /// <summary>
        /// Resolves one deterministic numeric binary-slice kind to its exact CLR result type and fixed byte width.<br/>
        /// <see cref="BinarySliceKind.BigInteger"/> reports a zero fixed width because its public selector accepts an explicit positive byte length; every other accepted numeric kind requires the returned width exactly.<br/>
        /// This shared contract keeps semantic evaluation, LibraDex adoption, and durable selector validation from developing separate type maps.<br/>
        /// </summary>
        /// <param name="sliceKind">The Abraxas-owned binary-slice conversion kind.<br/></param>
        /// <param name="valueType">The exact CLR numeric type produced by the slice.<br/></param>
        /// <param name="fixedByteWidth">The required byte width, or zero for an explicitly sized <see cref="BigInteger"/> slice.<br/></param>
        /// <returns><c>true</c> when the slice kind is one of the deterministic numeric conversions; otherwise <c>false</c>.<br/></returns>
        internal static bool TryGetNumericBinarySliceShape(
            BinarySliceKind sliceKind,
            out Type valueType,
            out int fixedByteWidth)
        {
            (valueType, fixedByteWidth) = sliceKind switch
            {
                BinarySliceKind.Int8 => (typeof(sbyte), sizeof(byte)),
                BinarySliceKind.UInt8 => (typeof(byte), sizeof(byte)),
                BinarySliceKind.Int16 => (typeof(short), sizeof(short)),
                BinarySliceKind.UInt16 => (typeof(ushort), sizeof(ushort)),
                BinarySliceKind.Int32 => (typeof(int), sizeof(int)),
                BinarySliceKind.UInt32 => (typeof(uint), sizeof(uint)),
                BinarySliceKind.Int64 => (typeof(long), sizeof(long)),
                BinarySliceKind.UInt64 => (typeof(ulong), sizeof(ulong)),
                BinarySliceKind.Int128 => (typeof(Int128), 16),
                BinarySliceKind.UInt128 => (typeof(UInt128), 16),
                BinarySliceKind.BigInteger => (typeof(BigInteger), 0),
                BinarySliceKind.Single => (typeof(float), sizeof(float)),
                BinarySliceKind.Double => (typeof(double), sizeof(double)),
                BinarySliceKind.Decimal => (typeof(decimal), 16),
                _ => (typeof(void), 0)
            };
            return valueType != typeof(void);
        }

        /// <summary>
        /// Resolves one deterministic text binary-slice kind to its fixed byte-width contract.<br/>
        /// Encoded string fields report zero because their public selectors require an explicit positive byte length; UTF-16 char and UTF-32 rune slices report their exact fixed widths.<br/>
        /// Caller-supplied encodings are deliberately excluded because durable replay requires a separate stable encoding-identity contract.<br/>
        /// </summary>
        /// <param name="sliceKind">The Abraxas-owned binary-slice conversion kind.<br/></param>
        /// <param name="fixedByteWidth">The required byte width, or zero for an explicitly sized encoded string.<br/></param>
        /// <returns><c>true</c> when the slice kind is one of the deterministic built-in text conversions; otherwise <c>false</c>.<br/></returns>
        internal static bool TryGetDeterministicTextBinarySliceShape(
            BinarySliceKind sliceKind,
            out int fixedByteWidth)
        {
            fixedByteWidth = sliceKind switch
            {
                BinarySliceKind.Utf8String => 0,
                BinarySliceKind.Utf16String => 0,
                BinarySliceKind.Utf32String => 0,
                BinarySliceKind.AsciiString => 0,
                BinarySliceKind.Latin1String => 0,
                BinarySliceKind.CharUtf16 => sizeof(char),
                BinarySliceKind.RuneUtf32 => sizeof(int),
                _ => -1
            };
            return fixedByteWidth >= 0;
        }

        /// <summary>
        /// Resolves the deterministic Guid binary-slice kind to its exact fixed byte width.<br/>
        /// The representation is the established .NET <see cref="Guid"/> byte layout used by <c>Guid.TryWriteBytes</c> and <c>new Guid(ReadOnlySpan&lt;byte&gt;)</c>, not canonical network byte order.<br/>
        /// </summary>
        /// <param name="sliceKind">The Abraxas-owned binary-slice conversion kind.<br/></param>
        /// <param name="fixedByteWidth">The required 16-byte width when the kind is recognized; otherwise zero.<br/></param>
        /// <returns><c>true</c> only for the deterministic .NET-layout Guid conversion.<br/></returns>
        internal static bool TryGetDeterministicGuidBinarySliceShape(
            BinarySliceKind sliceKind,
            out int fixedByteWidth)
        {
            fixedByteWidth = sliceKind == BinarySliceKind.Guid ? 16 : 0;
            return fixedByteWidth != 0;
        }

        /// <summary>
        /// Resolves one Abraxas temporal binary-slice kind to its exact CLR result type and physical byte width.<br/>
        /// Historical raw .NET layouts and explicit LibraDex Calendar SDT, Precision SDT, UTC SDT, and ordered TimeSpan representations remain separate kinds so no payload encoding is inferred.<br/>
        /// The DateTimeOffset forms retain the established Abraxas UTC <see cref="DateTime"/> view even though LibraDex's native binary operator accepts <see cref="DateTimeOffset"/> operands.<br/>
        /// </summary>
        /// <param name="sliceKind">The Abraxas-owned binary-slice conversion kind.<br/></param>
        /// <param name="valueType">The CLR type produced by Abraxas semantic evaluation.<br/></param>
        /// <param name="fixedByteWidth">The exact required byte width.<br/></param>
        /// <returns><c>true</c> when the slice kind is one of the five deterministic raw temporal conversions; otherwise <c>false</c>.<br/></returns>
        internal static bool TryGetDeterministicTemporalBinarySliceShape(
            BinarySliceKind sliceKind,
            out Type valueType,
            out int fixedByteWidth)
        {
            (valueType, fixedByteWidth) = sliceKind switch
            {
                BinarySliceKind.DateTimeTicks => (typeof(DateTime), sizeof(long)),
                BinarySliceKind.DateOnly => (typeof(DateOnly), sizeof(int)),
                BinarySliceKind.TimeOnly => (typeof(TimeOnly), sizeof(long)),
                BinarySliceKind.TimeSpanTicks => (typeof(TimeSpan), sizeof(long)),
                BinarySliceKind.DateTimeOffsetPair => (typeof(DateTime), sizeof(long) * 2),
                BinarySliceKind.DateTimeCalendarSdt => (typeof(DateTime), sizeof(ulong)),
                BinarySliceKind.DateTimePrecisionSdt => (typeof(DateTime), sizeof(ulong)),
                BinarySliceKind.DateOnlyCalendarSdt => (typeof(DateOnly), sizeof(ulong)),
                BinarySliceKind.DateOnlyPrecisionSdt => (typeof(DateOnly), sizeof(ulong)),
                BinarySliceKind.TimeOnlyCalendarSdt => (typeof(TimeOnly), sizeof(ulong)),
                BinarySliceKind.TimeOnlyPrecisionSdt => (typeof(TimeOnly), sizeof(ulong)),
                BinarySliceKind.TimeSpanOrderedTicks => (typeof(TimeSpan), sizeof(ulong)),
                BinarySliceKind.DateTimeOffsetCalendarSdtUtc => (typeof(DateTime), sizeof(ulong)),
                BinarySliceKind.DateTimeOffsetPrecisionSdtUtc => (typeof(DateTime), sizeof(ulong)),
                _ => (typeof(void), 0)
            };
            return valueType != typeof(void);
        }

        #endregion
    }

    /// <summary>
    /// Identifies one backend-neutral conversion applied to a byte slice.<br/>
    /// </summary>
    internal enum BinarySliceKind : long
    {
        Int8 = 1,        UInt8 = 2,        Int16 = 3,        UInt16 = 4,        Int32 = 5,        UInt32 = 6,        Int64 = 7,        UInt64 = 8,        Int128 = 9,        UInt128 = 10,        BigInteger = 11,        Single = 12,        Double = 13,        Decimal = 14,        CharUtf16 = 20,        RuneUtf32 = 21,        Utf8String = 22,        Utf16String = 23,        Utf32String = 24,        AsciiString = 25,        Latin1String = 26,        CustomEncodingString = 27,        Guid = 30,        DateTimeTicks = 40,        DateOnly = 41,        TimeOnly = 42,        TimeSpanTicks = 43,        DateTimeOffsetPair = 44,        DateTimeCalendarSdt = 60,        DateTimePrecisionSdt = 61,        DateOnlyCalendarSdt = 62,        DateOnlyPrecisionSdt = 63,        TimeOnlyCalendarSdt = 64,        TimeOnlyPrecisionSdt = 65,        TimeSpanOrderedTicks = 66,        DateTimeOffsetCalendarSdtUtc = 67,        DateTimeOffsetPrecisionSdtUtc = 68,        ByteArray = 50
    }
}
