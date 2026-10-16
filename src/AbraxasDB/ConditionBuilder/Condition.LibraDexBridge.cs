using LibraDex;

namespace AbraxasDB
{
    public static partial class Condition<T>
    {
        internal interface ILibraDexConditionSource
        {
            bool TryGetLibraDexIndexName(Builder builder, out string indexName);
            bool TryBuildLibraDexCondition(Builder builder, CatalogIdentityGroupIndexes indexes, out LibraDexConditionEndCondition condition, out string indexName);
        }

        /// <summary>
        /// Extends a direct condition leaf that can safely target a store-resolved physical alias while retaining its original property path for operand materialization.<br/>
        /// Partial-index planning uses this only after the store proves predicate implication and current source-USN readiness.<br/>
        /// </summary>
        internal interface ILibraDexAliasedConditionSource : ILibraDexConditionSource
        {
            /// <summary>
            /// Builds this leaf against an explicitly resolved physical index name.<br/>
            /// </summary>
            /// <param name="builder">Condition builder that owns operand materialization.<br/></param>
            /// <param name="indexes">Bound LibraDex identity group.<br/></param>
            /// <param name="targetIndexName">Physical index identity authorized by the store.<br/></param>
            /// <param name="condition">Completed LibraDex condition when successful.<br/></param>
            /// <param name="indexName">Physical index identity actually used.<br/></param>
            /// <returns><c>true</c> when the aliased leaf was translated; otherwise <c>false</c>.<br/></returns>
            bool TryBuildLibraDexCondition(
                Builder builder,
                CatalogIdentityGroupIndexes indexes,
                string targetIndexName,
                out LibraDexConditionEndCondition condition,
                out string indexName);
        }

        /// <summary>
        /// Resolves a logical direct property path to a separately named physical index after store-owned safety checks.<br/>
        /// </summary>
        internal delegate bool LibraDexIndexAliasResolver(string logicalIndexName, out string physicalIndexName);

        /// <summary>
        /// Resolves one backend-neutral derived selector to the name of an already-maintained LibraDex index.<br/>
        /// Implementations may materialize deferred selector configuration through the supplied builder after first proving that a compatible maintained projection could exist.<br/>
        /// </summary>
        /// <param name="builder">The condition builder that owns execution-scoped parameter materialization.<br/></param>
        /// <param name="descriptor">The backend-neutral derived-selector descriptor.<br/></param>
        /// <param name="indexName">The maintained LibraDex index name when resolution succeeds.<br/></param>
        /// <returns><c>true</c> when the descriptor exactly matches a maintained projection; otherwise <c>false</c>.<br/></returns>
        internal delegate bool LibraDexDerivedSelectorResolver(
            Builder builder,
            IBackendNeutralSelectorDescriptor descriptor,
            out string indexName);

        /// <summary>
        /// Extends a directly translatable condition leaf with store-owned derived-selector resolution.<br/>
        /// The separate interface preserves the existing low-friction contract for ordinary direct property leaves while allowing projection-aware leaves to receive the maintained-index registry resolver.<br/>
        /// </summary>
        internal interface ILibraDexDerivedConditionSource : ILibraDexConditionSource
        {
            bool TryGetLibraDexIndexName(Builder builder, LibraDexDerivedSelectorResolver resolver, out string indexName);
            bool TryBuildLibraDexCondition(Builder builder, CatalogIdentityGroupIndexes indexes, LibraDexDerivedSelectorResolver resolver, out LibraDexConditionEndCondition condition, out string indexName);
        }

        public partial class Builder
        {
            /// <summary>
            /// Selects one available directly mapped leaf from a top-level AND condition and builds its LibraDex candidate descriptor.<br/>
            /// OR and group fragments are rejected because safe candidate extraction for those shapes requires explicit union or complement planning.<br/>
            /// </summary>
            /// <param name="indexes">The bound LibraDex identity group.<br/></param>
            /// <param name="indexAvailable">Returns whether a reported grouped index is registered and ready.<br/></param>
            /// <param name="derivedSelectorResolver">Optional store-owned resolver that matches derived selectors to maintained projection indexes.<br/></param>
            /// <param name="aliasResolver">Optional store-owned resolver that maps a logical direct property path to a compatible physical index name.<br/></param>
            /// <param name="condition">The candidate-producing LibraDex condition when available.<br/></param>
            /// <returns><c>true</c> when one safe AND leaf can reduce the Fractal/Inheto residual scan; otherwise <c>false</c>.<br/></returns>
            internal bool TryBuildLibraDexAndCandidate(
                CatalogIdentityGroupIndexes indexes,
                Func<string, bool> indexAvailable,
                LibraDexDerivedSelectorResolver? derivedSelectorResolver,
                LibraDexIndexAliasResolver? aliasResolver,
                out LibraDexConditionEndCondition condition)
            {
                condition = default!;
                if (indexes is null || indexAvailable is null)
                    return false;

                for (var i = 0; i < fragments.Count; i++)
                {
                    if (fragments[i] is WhereFragment or EndCondition or AndOperation or ILibraDexConditionSource)
                        continue;

                    return false;
                }

                for (var i = 0; i < fragments.Count; i++)
                {
                    if (fragments[i] is not ILibraDexConditionSource source)
                        continue;
                    var resolved = source is ILibraDexDerivedConditionSource derived && derivedSelectorResolver is not null
                        ? derived.TryGetLibraDexIndexName(this, derivedSelectorResolver, out var indexName)
                        : source.TryGetLibraDexIndexName(this, out indexName);
                    if (!resolved)
                        continue;

                    if (indexAvailable(indexName) &&
                        (source is ILibraDexDerivedConditionSource resolvedSource && derivedSelectorResolver is not null
                            ? resolvedSource.TryBuildLibraDexCondition(this, indexes, derivedSelectorResolver, out condition, out _)
                            : source.TryBuildLibraDexCondition(this, indexes, out condition, out _)))
                    {
                        return true;
                    }

                    if (aliasResolver is not null &&
                        source is ILibraDexAliasedConditionSource aliased &&
                        aliasResolver(indexName, out var physicalIndexName) &&
                        aliased.TryBuildLibraDexCondition(
                            this,
                            indexes,
                            physicalIndexName,
                            out condition,
                            out _))
                    {
                        return true;
                    }
                }

                return false;
            }

            /// <summary>
            /// Selects one safe top-level-AND <see cref="DateTime.TimeOfDay"/> condition for Abraxas-owned base-index acceleration.<br/>
            /// The condition leaf uses Inheto's checked property-path split and materializes its operands through the ordinary execution-scoped cache.<br/>
            /// OR and grouped shapes fail closed because a single candidate source cannot represent their complete population safely.<br/>
            /// </summary>
            /// <param name="candidate">The canonical complete path, serialized DateTime source path, and inclusive TimeSpan bounds when available.<br/></param>
            /// <returns><see langword="true"/> when one eligible TimeOfDay range leaf can safely reduce candidates; otherwise <see langword="false"/>.<br/></returns>
            internal bool TryBuildTimeOfDayCandidate(out TimeOfDayCandidate candidate)
            {
                candidate = default;
                for (var i = 0; i < fragments.Count; i++)
                {
                    if (fragments[i] is WhereFragment or EndCondition or AndOperation or ILibraDexConditionSource)
                        continue;

                    return false;
                }

                for (var i = 0; i < fragments.Count; i++)
                {
                    if (fragments[i] is ITimeOfDayCandidateSource source &&
                        source.TryBuildTimeOfDayCandidate(this, out candidate))
                    {
                        return true;
                    }
                }

                return false;
            }

            /// <summary>
            /// Translates a complete Abraxas AND/OR/group tree when every leaf has an available maintained LibraDex index.<br/>
            /// A validation pass resolves every leaf before comparison operands are materialized; a second pass builds completed leaf conditions and composes them through LibraDex's native intersection/union planner while preserving Abraxas AND-before-OR precedence.<br/>
            /// Mixed indexed/residual trees return <c>false</c>: top-level AND may then use the existing candidate reducer, while any OR containing a residual branch must use the authoritative Fractal/Inheto semantic scan.<br/>
            /// </summary>
            /// <param name="indexes">The bound LibraDex identity group.<br/></param>
            /// <param name="indexAvailable">Tests whether a resolved maintained index belongs to the bound group.<br/></param>
            /// <param name="derivedSelectorResolver">Optional resolver for maintained derived projections.<br/></param>
            /// <param name="condition">The fully composed LibraDex condition when successful.<br/></param>
            /// <returns><c>true</c> when every leaf and logical connector can execute through LibraDex; otherwise <c>false</c>.<br/></returns>
            internal bool TryBuildLibraDexConditionTree(
                CatalogIdentityGroupIndexes indexes,
                Func<string, bool> indexAvailable,
                LibraDexDerivedSelectorResolver? derivedSelectorResolver,
                out LibraDexConditionEndCondition condition)
                => TryBuildLibraDexConditionTree(
                    indexes,
                    indexAvailable,
                    derivedSelectorResolver,
                    aliasResolver: null,
                    out condition);

            /// <summary>
            /// Translates a complete condition tree while allowing the owning RecordStore to authorize hidden physical aliases for direct leaves.<br/>
            /// Alias resolution is applied consistently during validation and materialization so reserved State can compose with ordinary property indexes without exposing its physical catalog name.<br/>
            /// </summary>
            /// <param name="indexes">Bound LibraDex identity group.<br/></param>
            /// <param name="indexAvailable">Tests directly named maintained indexes.<br/></param>
            /// <param name="derivedSelectorResolver">Optional maintained-derived selector resolver.<br/></param>
            /// <param name="aliasResolver">Optional logical-to-physical direct-index resolver owned by the store.<br/></param>
            /// <param name="condition">Fully composed LibraDex condition when successful.<br/></param>
            /// <returns><see langword="true"/> when every leaf resolved within this identity group.<br/></returns>
            internal bool TryBuildLibraDexConditionTree(
                CatalogIdentityGroupIndexes indexes,
                Func<string, bool> indexAvailable,
                LibraDexDerivedSelectorResolver? derivedSelectorResolver,
                LibraDexIndexAliasResolver? aliasResolver,
                out LibraDexConditionEndCondition condition)
            {
                condition = default!;
                if (indexes is null || indexAvailable is null ||
                    !TryValidateLibraDexConditionTree(this, indexAvailable, derivedSelectorResolver, aliasResolver))
                {
                    return false;
                }

                return TryBuildLibraDexConditionTreeCore(this, indexes, derivedSelectorResolver, aliasResolver, out condition);
            }

            /// <summary>
            /// Validates one Abraxas builder tree and proves that every direct or derived leaf resolves to an available maintained LibraDex index.<br/>
            /// This pass deliberately avoids ordinary comparison-operand materialization; a derived selector may materialize only the configuration required to identify its maintained projection.<br/>
            /// </summary>
            private static bool TryValidateLibraDexConditionTree(
                Builder source,
                Func<string, bool> indexAvailable,
                LibraDexDerivedSelectorResolver? derivedSelectorResolver,
                LibraDexIndexAliasResolver? aliasResolver)
            {
                var operandCount = 0;
                bool? pendingUseOr = null;
                foreach (IConditionFragment fragment in source.fragments)
                {
                    if (fragment is WhereFragment or EndCondition or GroupStartFragment or GroupEndFragment)
                        continue;

                    if (fragment is AndOperation or OrOperation)
                    {
                        if (operandCount == 0 || pendingUseOr.HasValue)
                            return false;
                        pendingUseOr = fragment is OrOperation;
                        continue;
                    }

                    bool valid;
                    if (fragment is GroupConditionFragment group)
                    {
                        using var groupContext = group.GroupBuilder.PushParameterContext(source.CurrentParameterContext);
                        valid = TryValidateLibraDexConditionTree(
                            group.GroupBuilder,
                            indexAvailable,
                            derivedSelectorResolver,
                            aliasResolver);
                    }
                    else if (fragment is ILibraDexConditionSource leaf)
                    {
                        valid = TryResolveLibraDexLeafIndex(
                            source,
                            leaf,
                            derivedSelectorResolver,
                            out var indexName) &&
                            (indexAvailable(indexName) ||
                             (aliasResolver is not null &&
                              leaf is ILibraDexAliasedConditionSource &&
                              aliasResolver(indexName, out _)));
                    }
                    else
                    {
                        return false;
                    }

                    if (!valid || (operandCount > 0 && !pendingUseOr.HasValue))
                        return false;

                    operandCount++;
                    pendingUseOr = null;
                }

                return operandCount != 0 && !pendingUseOr.HasValue;
            }

            /// <summary>
            /// Builds and composes a previously validated Abraxas tree into completed LibraDex condition fragments.<br/>
            /// Nested builders inherit the active parameter context, and completed conditions are reduced using AND precedence before the remaining OR connectors are applied.<br/>
            /// </summary>
            private static bool TryBuildLibraDexConditionTreeCore(
                Builder source,
                CatalogIdentityGroupIndexes indexes,
                LibraDexDerivedSelectorResolver? derivedSelectorResolver,
                LibraDexIndexAliasResolver? aliasResolver,
                out LibraDexConditionEndCondition condition)
            {
                condition = default!;
                var operands = new List<LibraDexConditionEndCondition>();
                var connectors = new List<bool>();
                bool? pendingUseOr = null;

                foreach (IConditionFragment fragment in source.fragments)
                {
                    if (fragment is WhereFragment or EndCondition or GroupStartFragment or GroupEndFragment)
                        continue;

                    if (fragment is AndOperation or OrOperation)
                    {
                        if (operands.Count == 0 || pendingUseOr.HasValue)
                            return false;
                        pendingUseOr = fragment is OrOperation;
                        continue;
                    }

                    LibraDexConditionEndCondition next = default!;
                    if (fragment is GroupConditionFragment group)
                    {
                        using var groupContext = group.GroupBuilder.PushParameterContext(source.CurrentParameterContext);
                        if (!TryBuildLibraDexConditionTreeCore(
                            group.GroupBuilder,
                            indexes,
                            derivedSelectorResolver,
                            aliasResolver,
                            out next))
                        {
                            return false;
                        }
                    }
                    else if (fragment is ILibraDexConditionSource leaf)
                    {
                        bool built = false;
                        if (aliasResolver is not null &&
                            leaf is ILibraDexAliasedConditionSource aliased &&
                            TryResolveLibraDexLeafIndex(source, leaf, derivedSelectorResolver, out var logicalIndexName) &&
                            aliasResolver(logicalIndexName, out var physicalIndexName))
                        {
                            built = aliased.TryBuildLibraDexCondition(
                                source,
                                indexes,
                                physicalIndexName,
                                out next,
                                out _);
                        }

                        if (!built && !TryBuildLibraDexLeafCondition(
                                source,
                                leaf,
                                indexes,
                                derivedSelectorResolver,
                                out next))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }

                    if (operands.Count > 0)
                    {
                        if (!pendingUseOr.HasValue)
                            return false;
                        connectors.Add(pendingUseOr.Value);
                    }

                    operands.Add(next);
                    pendingUseOr = null;
                }

                if (operands.Count == 0 || pendingUseOr.HasValue || connectors.Count != operands.Count - 1)
                    return false;

                for (var i = 0; i < connectors.Count;)
                {
                    if (connectors[i])
                    {
                        i++;
                        continue;
                    }

                    operands[i] = operands[i].AndAlso(operands[i + 1]);
                    operands.RemoveAt(i + 1);
                    connectors.RemoveAt(i);
                }

                condition = operands[0];
                for (var i = 0; i < connectors.Count; i++)
                    condition = condition.OrElse(operands[i + 1]);
                return true;
            }

            /// <summary>
            /// Resolves one direct or maintained-derived condition leaf to its LibraDex index name.<br/>
            /// </summary>
            private static bool TryResolveLibraDexLeafIndex(
                Builder builder,
                ILibraDexConditionSource source,
                LibraDexDerivedSelectorResolver? derivedSelectorResolver,
                out string indexName)
            {
                if (source is ILibraDexDerivedConditionSource derived && derivedSelectorResolver is not null)
                    return derived.TryGetLibraDexIndexName(builder, derivedSelectorResolver, out indexName);

                return source.TryGetLibraDexIndexName(builder, out indexName);
            }

            /// <summary>
            /// Builds one completed LibraDex condition from a direct or maintained-derived Abraxas leaf.<br/>
            /// </summary>
            private static bool TryBuildLibraDexLeafCondition(
                Builder builder,
                ILibraDexConditionSource source,
                CatalogIdentityGroupIndexes indexes,
                LibraDexDerivedSelectorResolver? derivedSelectorResolver,
                out LibraDexConditionEndCondition condition)
            {
                if (source is ILibraDexDerivedConditionSource derived && derivedSelectorResolver is not null)
                    return derived.TryBuildLibraDexCondition(builder, indexes, derivedSelectorResolver, out condition, out _);

                return source.TryBuildLibraDexCondition(builder, indexes, out condition, out _);
            }

            /// <summary>
            /// Visits every direct-condition leaf in this builder tree for opt-in execution-plan diagnostics.<br/>
            /// Direct index identity is read without ordinary comparison-operand materialization; maintained derived selectors use the same execution-scoped resolver/cache as planning so deferred selector configuration is not reinvoked.<br/>
            /// </summary>
            /// <param name="indexAvailable">Tests whether a resolved index belongs to the bound LibraDex identity group.<br/></param>
            /// <param name="derivedSelectorResolver">Optional maintained-derived projection resolver.<br/></param>
            /// <param name="visitor">Receives leaf type, resolved index name, and bound-group availability once per leaf.<br/></param>
            internal void VisitLibraDexLeafDiagnostics(
                Func<string, bool> indexAvailable,
                LibraDexDerivedSelectorResolver? derivedSelectorResolver,
                Action<Type, string?, bool> visitor)
            {
                ArgumentNullException.ThrowIfNull(indexAvailable);
                ArgumentNullException.ThrowIfNull(visitor);
                VisitLibraDexLeafDiagnosticsCore(this, indexAvailable, derivedSelectorResolver, visitor);
            }

            /// <summary>
            /// Recursively visits direct leaves inside one builder while preserving nested parameter context for maintained derived-selector resolution.<br/>
            /// </summary>
            private static void VisitLibraDexLeafDiagnosticsCore(
                Builder source,
                Func<string, bool> indexAvailable,
                LibraDexDerivedSelectorResolver? derivedSelectorResolver,
                Action<Type, string?, bool> visitor)
            {
                foreach (IConditionFragment fragment in source.fragments)
                {
                    if (fragment is GroupConditionFragment group)
                    {
                        using var groupContext = group.GroupBuilder.PushParameterContext(source.CurrentParameterContext);
                        VisitLibraDexLeafDiagnosticsCore(
                            group.GroupBuilder,
                            indexAvailable,
                            derivedSelectorResolver,
                            visitor);
                        continue;
                    }

                    if (fragment is not ILibraDexConditionSource leaf)
                        continue;

                    if (TryResolveLibraDexLeafIndex(source, leaf, derivedSelectorResolver, out var indexName))
                        visitor(leaf.GetType(), indexName, indexAvailable(indexName));
                    else
                        visitor(leaf.GetType(), null, false);
                }
            }
        }

        /// <summary>
        /// Identifies the backend-neutral whole-value operation represented by a temporal condition leaf.<br/>
        /// The operation kind is shared by DateOnly, DateTime, TimeOnly, and TimeSpan so their direct LibraDex adapters use one parameter-materialization and descriptor-building path.<br/>
        /// </summary>
        internal enum LibraDexTemporalWholeValueOperationKind
        {
            GreaterThan,
            GreaterOrEqual,
            LessThan,
            LessOrEqual,
            EqualTo,
            NotEqualTo,
            Between,
            NotBetween,
            In,
            NotIn
        }

        /// <summary>
        /// Provides the shared direct-LibraDex adapter for temporal whole-value comparison, range, and membership leaves.<br/>
        /// Deferred operands are materialized through the owning builder exactly once per execution plan and only after a direct property path and supported temporal CLR type have been established.<br/>
        /// </summary>
        /// <typeparam name="TValue">DateOnly, DateTime, TimeOnly, or TimeSpan operand type.<br/></typeparam>
        internal abstract class LibraDexTemporalWholeValueOperation<TValue> : IConditionLeaf, ILibraDexDerivedConditionSource, ITimeOfDayCandidateSource
            where TValue : struct
        {
            private readonly ValueOrExpression<string> selector;
            private readonly LibraDexTemporalWholeValueOperationKind operation;
            private readonly ValueOrExpression<TValue>[] operands;
            private readonly ValueOrExpression<IEnumerable<TValue>>? setOperand;

            /// <summary>
            /// Creates a one-operand temporal comparison adapter.<br/>
            /// </summary>
            /// <param name="selector">Abraxas selector that must resolve to a direct property path.<br/></param>
            /// <param name="operation">Whole-value comparison operation represented by the leaf.<br/></param>
            /// <param name="operand">Comparison operand, including a deferred developer expression when supplied.<br/></param>
            protected LibraDexTemporalWholeValueOperation(
                ValueOrExpression<string> selector,
                LibraDexTemporalWholeValueOperationKind operation,
                ValueOrExpression<TValue> operand)
            {
                this.selector = selector;
                this.operation = operation;
                operands = new[] { operand };
            }

            /// <summary>
            /// Creates a two-boundary temporal range adapter.<br/>
            /// </summary>
            /// <param name="selector">Abraxas selector that must resolve to a direct property path.<br/></param>
            /// <param name="operation">Inclusive range or excluded-range operation represented by the leaf.<br/></param>
            /// <param name="startOperand">Inclusive lower boundary operand.<br/></param>
            /// <param name="endOperand">Inclusive upper boundary operand.<br/></param>
            protected LibraDexTemporalWholeValueOperation(
                ValueOrExpression<string> selector,
                LibraDexTemporalWholeValueOperationKind operation,
                ValueOrExpression<TValue> startOperand,
                ValueOrExpression<TValue> endOperand)
            {
                this.selector = selector;
                this.operation = operation;
                operands = new[] { startOperand, endOperand };
            }

            /// <summary>
            /// Creates a temporal membership adapter and captures the operand descriptors once when the Abraxas leaf is constructed.<br/>
            /// Enumeration here captures only lightweight value-or-expression descriptors; developer expressions remain deferred until execution-plan materialization.<br/>
            /// </summary>
            /// <param name="selector">Abraxas selector that must resolve to a direct property path.<br/></param>
            /// <param name="operation">Positive or negative membership operation represented by the leaf.<br/></param>
            /// <param name="operands">Membership operand descriptors.<br/></param>
            protected LibraDexTemporalWholeValueOperation(
                ValueOrExpression<string> selector,
                LibraDexTemporalWholeValueOperationKind operation,
                System.Collections.Generic.IEnumerable<ValueOrExpression<TValue>> operands)
            {
                this.selector = selector;
                this.operation = operation;
                this.operands = operands is null
                    ? throw new ArgumentNullException(nameof(operands))
                    : new System.Collections.Generic.List<ValueOrExpression<TValue>>(operands).ToArray();
            }

            /// <summary>
            /// Creates a temporal membership adapter backed by one execution-frozen deferred set.<br/>
            /// The set remains one semantic operand: explicit enumerable parameters share one identity snapshot across leaves, while ordinary factories materialize during leaf preparation.<br/>
            /// </summary>
            /// <param name="selector">Abraxas selector that must resolve to a direct property path.<br/></param>
            /// <param name="operation">Positive or negative membership operation represented by the leaf.<br/></param>
            /// <param name="setParameter">Whole-set operand retained without invocation; explicit parameters share their snapshot key, while ordinary factories materialize during execution preparation.<br/></param>
            protected LibraDexTemporalWholeValueOperation(
                ValueOrExpression<string> selector,
                LibraDexTemporalWholeValueOperationKind operation,
                ValueOrExpression<IEnumerable<TValue>> setParameter)
            {
                ArgumentNullException.ThrowIfNull(setParameter);
                this.selector = selector;
                this.operation = operation;
                operands = Array.Empty<ValueOrExpression<TValue>>();
                setOperand = setParameter;
            }

            /// <summary>
            /// Reports the direct property-path index required by this temporal leaf without materializing any operand expressions.<br/>
            /// </summary>
            /// <param name="builder">The owning condition builder used to resolve the property-path selector without evaluating comparison operands.<br/></param>
            /// <param name="indexName">Resolved grouped LibraDex index name.<br/></param>
            /// <returns>True when the selector is a direct Abraxas property path; otherwise false.<br/></returns>
            public bool TryGetLibraDexIndexName(Builder builder, out string indexName)
            {
                indexName = string.Empty;
                if (!TryExtractPropPath(builder, selector, out var propPath))
                    return false;

                indexName = propPath;
                return true;
            }

            /// <summary>
            /// Recognizes a direct Inheto <see cref="DateTime.TimeOfDay"/> continuation whose inclusive TimeSpan range can use its serialized DateTime prefix as an Abraxas candidate source.<br/>
            /// Inheto remains the authority for canonicalization, source/CLR splitting, result typing, and admitted-member validation; this method adds only the storage-neutral intrinsic classification.<br/>
            /// Exact complete-path indexes are not resolved here and therefore remain preferred by the ordinary direct LibraDex planner.<br/>
            /// </summary>
            /// <param name="builder">The condition builder owning execution-scoped operand materialization.<br/></param>
            /// <param name="candidate">The recognized complete path, serialized DateTime source path, and inclusive bounds.<br/></param>
            /// <returns><see langword="true"/> only for a structurally checked TimeSpan Between operation over DateTime.TimeOfDay.<br/></returns>
            public bool TryBuildTimeOfDayCandidate(Builder builder, out TimeOfDayCandidate candidate)
            {
                candidate = default;
                if (typeof(TValue) != typeof(TimeSpan) ||
                    operation != LibraDexTemporalWholeValueOperationKind.Between ||
                    operands.Length != 2 ||
                    !TryExtractPropPath(builder, selector, out var propPath))
                {
                    return false;
                }

                Inheto.PropPathCheck path = Inheto.InhetoBinary.CheckPropPath<T, TimeSpan>(propPath);
                if (!path.IsValid ||
                    path.ContainsMethodInvocation ||
                    !path.RequiresClrContinuation ||
                    !string.Equals(path.ClrRemainderCandidate, ".TimeOfDay", StringComparison.Ordinal) ||
                    string.IsNullOrEmpty(path.SerializedPrefixCandidate))
                {
                    return false;
                }

                Inheto.PropPathCheck source = Inheto.InhetoBinary.CheckPropPath(
                    typeof(T),
                    path.SerializedPrefixCandidate,
                    typeof(DateTime));
                if (!source.IsValid ||
                    source.RequiresClrContinuation ||
                    !string.Equals(source.CanonicalPath, path.SerializedPrefixCandidate, StringComparison.Ordinal))
                {
                    return false;
                }

                TimeSpan start = (TimeSpan)(object)builder.MaterializeTemporalParameter(
                    selector,
                    operands[0],
                    path.CanonicalPath,
                    owner: this);
                TimeSpan end = (TimeSpan)(object)builder.MaterializeTemporalParameter(
                    selector,
                    operands[1],
                    path.CanonicalPath,
                    owner: this);
                candidate = new TimeOfDayCandidate(
                    path.CanonicalPath,
                    source.CanonicalPath,
                    start,
                    end);
                return true;
            }

            /// <summary>
            /// Resolves the maintained index required by a direct temporal leaf or an exact raw temporal binary slice.<br/>
            /// Derived discovery occurs only for operations whose malformed-value and undersized-slice behavior is identical in Abraxas and LibraDex.<br/>
            /// </summary>
            /// <param name="builder">The condition builder that owns execution-scoped selector materialization.<br/></param>
            /// <param name="resolver">The store-owned resolver that proves a compatible source blob index is maintained.<br/></param>
            /// <param name="indexName">The direct temporal index or resolved source blob index when successful.<br/></param>
            /// <returns><c>true</c> when this leaf can report an exact maintained index; otherwise <c>false</c>.<br/></returns>
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
                    TryGetTemporalSliceDescriptor(out var slice) &&
                    IsNativeDerivedOperationSupported(slice.SliceType) &&
                    resolver(builder, slice, out indexName))
                {
                    return true;
                }

                indexName = string.Empty;
                return false;
            }

            /// <summary>
            /// Builds the LibraDex candidate condition corresponding to this temporal whole-value leaf.<br/>
            /// Unsupported temporal CLR types or malformed operand counts return false so execution remains on the authoritative Fractal/Inheto semantic path.<br/>
            /// </summary>
            /// <param name="builder">Abraxas builder that owns execution-scoped deferred-parameter materialization.<br/></param>
            /// <param name="indexes">Bound LibraDex identity-group indexes.<br/></param>
            /// <param name="condition">Completed LibraDex condition when translation succeeds.<br/></param>
            /// <param name="indexName">Required grouped index name when the selector is a direct property path.<br/></param>
            /// <returns>True when the leaf was translated exactly; otherwise false.<br/></returns>
            public bool TryBuildLibraDexCondition(
                Builder builder,
                CatalogIdentityGroupIndexes indexes,
                out LibraDexConditionEndCondition condition,
                out string indexName)
            {
                condition = default!;
                indexName = string.Empty;
                if (indexes is null || !TryExtractPropPath(builder, selector, out var propPath))
                    return false;

                indexName = propPath;
                return TryBuildLibraDexConditionAgainstIndex(builder, indexes, propPath, propPath, out condition);
            }

            /// <summary>
            /// Builds one whole-value temporal predicate against an exact direct or calculated scalar index.<br/>
            /// Operand context is kept separate from the physical index name so deferred parameters remain execution scoped.<br/>
            /// </summary>
            /// <param name="builder">Builder owning deferred operand materialization.<br/></param>
            /// <param name="indexes">Identity group containing the resolved index.<br/></param>
            /// <param name="indexName">Exact LibraDex scalar index name.<br/></param>
            /// <param name="operandContext">Stable context supplied while materializing deferred operands.<br/></param>
            /// <param name="condition">Completed LibraDex condition when translation succeeds.<br/></param>
            /// <returns><see langword="true"/> when the temporal CLR type and operation are supported.<br/></returns>
            private bool TryBuildLibraDexConditionAgainstIndex(
                Builder builder,
                CatalogIdentityGroupIndexes indexes,
                string indexName,
                string operandContext,
                out LibraDexConditionEndCondition condition)
            {
                condition = default!;
                var valueSelector = indexes.Where(indexName);
                LibraDexConditionOperator<TValue>? typedOperator = null;
                if (typeof(TValue) == typeof(DateOnly))
                    typedOperator = (LibraDexConditionOperator<TValue>)(object)valueSelector.AsDateOnly;
                else if (typeof(TValue) == typeof(DateTime))
                    typedOperator = (LibraDexConditionOperator<TValue>)(object)valueSelector.AsDateTime;
                else if (typeof(TValue) == typeof(TimeOnly))
                    typedOperator = (LibraDexConditionOperator<TValue>)(object)valueSelector.AsTimeOnly;
                else if (typeof(TValue) == typeof(TimeSpan))
                    typedOperator = (LibraDexConditionOperator<TValue>)(object)valueSelector.AsTimeSpan;

                if (typedOperator is null)
                    return false;

                TValue ValueAt(int index)
                    => builder.MaterializeParameter(operands[index], operandContext, owner: this);

                LibraDexConditionContinueOrEnd? translated = operation switch
                {
                    LibraDexTemporalWholeValueOperationKind.GreaterThan when operands.Length == 1 => typedOperator.GreaterThan(ValueAt(0)),
                    LibraDexTemporalWholeValueOperationKind.GreaterOrEqual when operands.Length == 1 => typedOperator.GreaterOrEqual(ValueAt(0)),
                    LibraDexTemporalWholeValueOperationKind.LessThan when operands.Length == 1 => typedOperator.LessThan(ValueAt(0)),
                    LibraDexTemporalWholeValueOperationKind.LessOrEqual when operands.Length == 1 => typedOperator.LessOrEqual(ValueAt(0)),
                    LibraDexTemporalWholeValueOperationKind.EqualTo when operands.Length == 1 => typedOperator.EqualTo(ValueAt(0)),
                    LibraDexTemporalWholeValueOperationKind.NotEqualTo when operands.Length == 1 => typedOperator.NotEqualTo(ValueAt(0)),
                    LibraDexTemporalWholeValueOperationKind.Between when operands.Length == 2 => typedOperator.Between(ValueAt(0), ValueAt(1)),
                    LibraDexTemporalWholeValueOperationKind.NotBetween when operands.Length == 2 => typedOperator.NotBetween(ValueAt(0), ValueAt(1)),
                    LibraDexTemporalWholeValueOperationKind.In => typedOperator.In(Values()),
                    LibraDexTemporalWholeValueOperationKind.NotIn => ExcludeScalarNull(typedOperator.NotIn(Values())),
                    _ => null
                };

                if (translated is null)
                    return false;

                condition = translated.EndCondition;
                return true;

                TValue[] Values()
                {
                    if (setOperand is not null)
                    {
                        IEnumerable<TValue> source = builder.MaterializeParameter(setOperand, operandContext, owner: this);
                        return source as TValue[] ?? source.ToArray();
                    }

                    var values = new TValue[operands.Length];
                    for (var i = 0; i < values.Length; i++)
                        values[i] = ValueAt(i);

                    return values;
                }

                LibraDexConditionContinueOrEnd ExcludeScalarNull(LibraDexConditionContinueOrEnd nonMembership)
                {
                    var nonNullSelector = nonMembership.AND.Where(indexName);
                    if (typeof(TValue) == typeof(DateOnly))
                        return nonNullSelector.AsDateOnly.IsNotNull();
                    if (typeof(TValue) == typeof(DateTime))
                        return nonNullSelector.AsDateTime.IsNotNull();
                    if (typeof(TValue) == typeof(TimeOnly))
                        return nonNullSelector.AsTimeOnly.IsNotNull();
                    if (typeof(TValue) == typeof(TimeSpan))
                        return nonNullSelector.AsTimeSpan.IsNotNull();

                    throw new InvalidOperationException($"Unsupported temporal CLR type '{typeof(TValue).FullName}'.");
                }
            }

            /// <summary>
            /// Builds a direct temporal condition or adopts LibraDex's explicitly coerced temporal slice predicate over an exact source blob index.<br/>
            /// Raw DateTime, DateOnly, and TimeOnly slices use equality or bounded ranges; canonical LibraDex SDT slices use equality only because malformed packed fields can sort inside otherwise valid ranges without decoding.<br/>
            /// Raw and ordered TimeSpan representations plus validated raw DateTimeOffset pairs support positive ordering; complements, membership, structured components, and every other contract retain authoritative semantic fallback.<br/>
            /// Complements, membership, structured components, and every contract without exact invalid-slice behavior remain on the authoritative Fractal/Inheto semantic path.<br/>
            /// </summary>
            /// <param name="builder">The owning Abraxas condition builder.<br/></param>
            /// <param name="indexes">The bound LibraDex identity group.<br/></param>
            /// <param name="resolver">The store-owned resolver that proves source-index ownership.<br/></param>
            /// <param name="condition">The completed LibraDex condition when translation succeeds.<br/></param>
            /// <param name="indexName">The direct temporal or source blob index used by the condition.<br/></param>
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
                    !TryGetTemporalSliceDescriptor(out var slice) ||
                    !IsNativeDerivedOperationSupported(slice.SliceType) ||
                    !TryExtractPropPath(slice.SourceSelector, out var sourcePath) ||
                    !resolver(builder, slice, out var resolvedIndexName) ||
                    !indexes.TryGetInfo(resolvedIndexName, out var info) ||
                    info.KeyFamily != CatalogIndexKeyFamily.Blob ||
                    info.IdentityFamily != CatalogIndexIdentityFamily.Scalar)
                {
                    return false;
                }

                var binary = indexes.Where(resolvedIndexName).AsBinary;
                bool translated;
                if (slice.SliceType == BinarySliceKind.DateTimeTicks && typeof(TValue) == typeof(DateTime))
                {
                    translated = TryBuildDerivedSliceCondition(
                        builder,
                        sourcePath,
                        (LibraDexBinaryTypedSliceConditionOperator<TValue>)(object)binary.SlicedAsDateTime(
                            (int)slice.Offset,
                            Coercion.DateTime.DotNetTicks),
                        static value => value,
                        out condition);
                }
                else if (slice.SliceType == BinarySliceKind.DateOnly && typeof(TValue) == typeof(DateOnly))
                {
                    translated = TryBuildDerivedSliceCondition(
                        builder,
                        sourcePath,
                        (LibraDexBinaryTypedSliceConditionOperator<TValue>)(object)binary.SlicedAsDateOnly(
                            (int)slice.Offset,
                            Coercion.DateOnly.DotNetDayNumber),
                        static value => value,
                        out condition);
                }
                else if (slice.SliceType == BinarySliceKind.TimeOnly && typeof(TValue) == typeof(TimeOnly))
                {
                    translated = TryBuildDerivedSliceCondition(
                        builder,
                        sourcePath,
                        (LibraDexBinaryTypedSliceConditionOperator<TValue>)(object)binary.SlicedAsTimeOnly(
                            (int)slice.Offset,
                            Coercion.TimeOnly.DotNetTicks),
                        static value => value,
                        out condition);
                }
                else if (slice.SliceType == BinarySliceKind.TimeSpanTicks && typeof(TValue) == typeof(TimeSpan))
                {
                    translated = TryBuildDerivedSliceCondition(
                        builder,
                        sourcePath,
                        (LibraDexBinaryTypedSliceConditionOperator<TValue>)(object)binary.SlicedAsTimeSpan(
                            (int)slice.Offset,
                            Coercion.TimeSpan.DotNetTicks),
                        static value => value,
                        out condition);
                }
                else if (slice.SliceType == BinarySliceKind.DateTimeOffsetPair && typeof(TValue) == typeof(DateTime))
                {
                    translated = TryBuildDerivedSliceCondition(
                        builder,
                        sourcePath,
                        binary.SlicedAsDateTimeOffset(
                            (int)slice.Offset,
                            Coercion.DateTimeOffset.DotNetTicksAndOffset),
                        static value =>
                        {
                            var dateTime = (DateTime)(object)value;
                            return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
                        },
                        out condition);
                }
                else if (slice.SliceType == BinarySliceKind.DateTimeCalendarSdt && typeof(TValue) == typeof(DateTime))
                {
                    translated = TryBuildDerivedSliceCondition(
                        builder,
                        sourcePath,
                        (LibraDexBinaryTypedSliceConditionOperator<TValue>)(object)binary.SlicedAsDateTime(
                            (int)slice.Offset,
                            Coercion.DateTime.LibraDexCalendarSdt),
                        static value => value,
                        out condition);
                }
                else if (slice.SliceType == BinarySliceKind.DateTimePrecisionSdt && typeof(TValue) == typeof(DateTime))
                {
                    translated = TryBuildDerivedSliceCondition(
                        builder,
                        sourcePath,
                        (LibraDexBinaryTypedSliceConditionOperator<TValue>)(object)binary.SlicedAsDateTime(
                            (int)slice.Offset,
                            Coercion.DateTime.LibraDexPrecisionSdt),
                        static value => value,
                        out condition);
                }
                else if (slice.SliceType == BinarySliceKind.DateOnlyCalendarSdt && typeof(TValue) == typeof(DateOnly))
                {
                    translated = TryBuildDerivedSliceCondition(
                        builder,
                        sourcePath,
                        (LibraDexBinaryTypedSliceConditionOperator<TValue>)(object)binary.SlicedAsDateOnly(
                            (int)slice.Offset,
                            Coercion.DateOnly.LibraDexCalendarSdt),
                        static value => value,
                        out condition);
                }
                else if (slice.SliceType == BinarySliceKind.DateOnlyPrecisionSdt && typeof(TValue) == typeof(DateOnly))
                {
                    translated = TryBuildDerivedSliceCondition(
                        builder,
                        sourcePath,
                        (LibraDexBinaryTypedSliceConditionOperator<TValue>)(object)binary.SlicedAsDateOnly(
                            (int)slice.Offset,
                            Coercion.DateOnly.LibraDexPrecisionSdt),
                        static value => value,
                        out condition);
                }
                else if (slice.SliceType == BinarySliceKind.TimeOnlyCalendarSdt && typeof(TValue) == typeof(TimeOnly))
                {
                    translated = TryBuildDerivedSliceCondition(
                        builder,
                        sourcePath,
                        (LibraDexBinaryTypedSliceConditionOperator<TValue>)(object)binary.SlicedAsTimeOnly(
                            (int)slice.Offset,
                            Coercion.TimeOnly.LibraDexCalendarSdt),
                        static value => value,
                        out condition);
                }
                else if (slice.SliceType == BinarySliceKind.TimeOnlyPrecisionSdt && typeof(TValue) == typeof(TimeOnly))
                {
                    translated = TryBuildDerivedSliceCondition(
                        builder,
                        sourcePath,
                        (LibraDexBinaryTypedSliceConditionOperator<TValue>)(object)binary.SlicedAsTimeOnly(
                            (int)slice.Offset,
                            Coercion.TimeOnly.LibraDexPrecisionSdt),
                        static value => value,
                        out condition);
                }
                else if (slice.SliceType == BinarySliceKind.TimeSpanOrderedTicks && typeof(TValue) == typeof(TimeSpan))
                {
                    translated = TryBuildDerivedSliceCondition(
                        builder,
                        sourcePath,
                        (LibraDexBinaryTypedSliceConditionOperator<TValue>)(object)binary.SlicedAsTimeSpan(
                            (int)slice.Offset,
                            Coercion.TimeSpan.LibraDexOrderedTicks),
                        static value => value,
                        out condition);
                }
                else if ((slice.SliceType is BinarySliceKind.DateTimeOffsetCalendarSdtUtc or
                         BinarySliceKind.DateTimeOffsetPrecisionSdtUtc) &&
                         typeof(TValue) == typeof(DateTime))
                {
                    var encoding = slice.SliceType == BinarySliceKind.DateTimeOffsetCalendarSdtUtc
                        ? Coercion.DateTimeOffset.LibraDexCalendarSdtUtc
                        : Coercion.DateTimeOffset.LibraDexPrecisionSdtUtc;
                    translated = TryBuildDerivedSliceCondition(
                        builder,
                        sourcePath,
                        binary.SlicedAsDateTimeOffset((int)slice.Offset, encoding),
                        static value =>
                        {
                            var dateTime = (DateTime)(object)value;
                            return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
                        },
                        out condition);
                }
                else
                {
                    return false;
                }

                if (!translated)
                    return false;

                indexName = resolvedIndexName;
                return true;
            }

            /// <summary>
            /// Resolves the complete immutable temporal selector to a separately maintained calculated scalar index.<br/>
            /// A source-path result remains assigned to the existing direct or binary-slice translation.<br/>
            /// </summary>
            /// <param name="builder">Builder owning the frozen selector.<br/></param>
            /// <param name="resolver">Store-owned maintained-projection resolver.<br/></param>
            /// <param name="indexName">Exact calculated index name when available.<br/></param>
            /// <returns><see langword="true"/> only when a non-source exact descriptor match exists.<br/></returns>
            private bool TryResolveCalculatedIndex(
                Builder builder,
                LibraDexDerivedSelectorResolver resolver,
                out string indexName)
            {
                indexName = string.Empty;
                IBackendNeutralSelectorDescriptor? descriptor = selector.SelectorDescriptor;
                if (resolver is null || descriptor is null || !resolver(builder, descriptor, out var resolved))
                    return false;

                if (TryGetConditionSourcePropPath(builder, selector, out var sourcePath) &&
                    string.Equals(resolved, sourcePath, StringComparison.Ordinal))
                {
                    return false;
                }

                indexName = resolved;
                return true;
            }

            /// <summary>
            /// Validates and returns the deterministic temporal binary-slice descriptor attached to this leaf's selector.<br/>
            /// The selector's CLR result type, exact fixed width, and Int32-addressable offset must agree with <typeparamref name="TValue"/> before native execution is considered.<br/>
            /// </summary>
            /// <param name="slice">The validated temporal slice descriptor when available.<br/></param>
            /// <returns><c>true</c> when the selector is one of the supported raw or explicit LibraDex temporal layouts; otherwise <c>false</c>.<br/></returns>
            private bool TryGetTemporalSliceDescriptor(out BinarySliceSelectorDescriptor slice)
            {
                slice = selector.SelectorDescriptor as BinarySliceSelectorDescriptor ?? null!;
                return slice is not null &&
                    slice.Offset is >= 0 and <= int.MaxValue &&
                    slice.Length is > 0 and <= int.MaxValue &&
                    TryGetDeterministicTemporalBinarySliceShape(
                        slice.SliceType,
                        out var valueType,
                        out var fixedByteWidth) &&
                    valueType == typeof(TValue) &&
                    slice.Length == fixedByteWidth;
            }

            /// <summary>
            /// Reports whether the current whole-value operation has an exact positive LibraDex representation for the selected temporal layout.<br/>
            /// Equality is safe for every layout; bounded ranges are safe only for contiguous raw CLR domains plus TimeSpan and validated raw DateTimeOffset pairs.<br/>
            /// Packed SDT ranges and ordering remain fallback-only because noncanonical bit patterns can sort among canonical values without decoding.<br/>
            /// </summary>
            /// <param name="sliceKind">The validated raw temporal slice kind.<br/></param>
            /// <returns><c>true</c> when native execution preserves invalid and undersized slice semantics; otherwise <c>false</c>.<br/></returns>
            private bool IsNativeDerivedOperationSupported(BinarySliceKind sliceKind)
            {
                if (operation == LibraDexTemporalWholeValueOperationKind.EqualTo)
                    return true;

                if (operation == LibraDexTemporalWholeValueOperationKind.Between)
                {
                    return sliceKind is BinarySliceKind.DateTimeTicks or
                        BinarySliceKind.DateOnly or
                        BinarySliceKind.TimeOnly or
                        BinarySliceKind.TimeSpanTicks or
                        BinarySliceKind.TimeSpanOrderedTicks or
                        BinarySliceKind.DateTimeOffsetPair;
                }

                return (sliceKind is BinarySliceKind.TimeSpanTicks or
                    BinarySliceKind.TimeSpanOrderedTicks or
                    BinarySliceKind.DateTimeOffsetPair) &&
                    operation is (LibraDexTemporalWholeValueOperationKind.GreaterThan or
                        LibraDexTemporalWholeValueOperationKind.GreaterOrEqual or
                        LibraDexTemporalWholeValueOperationKind.LessThan or
                        LibraDexTemporalWholeValueOperationKind.LessOrEqual);
            }

            /// <summary>
            /// Materializes this leaf's positive operands once and applies them to one LibraDex typed binary-slice operator.<br/>
            /// The projector is identity for matching CLR types and converts Abraxas's UTC <see cref="DateTime"/> DateTimeOffset view into a zero-offset <see cref="DateTimeOffset"/> without changing ticks.<br/>
            /// </summary>
            /// <typeparam name="TSliceValue">The operand type required by the selected LibraDex slice coercion.<br/></typeparam>
            /// <param name="builder">The owning Abraxas condition builder.<br/></param>
            /// <param name="sourcePath">The direct source blob property used for deferred-operand materialization.<br/></param>
            /// <param name="typedOperator">The explicitly coerced LibraDex typed-slice operator.<br/></param>
            /// <param name="project">Converts the Abraxas temporal operand into the LibraDex slice operand type.<br/></param>
            /// <param name="condition">The completed LibraDex condition when translation succeeds.<br/></param>
            /// <returns><c>true</c> for a supported positive comparison with the required operand count; otherwise <c>false</c>.<br/></returns>
            private bool TryBuildDerivedSliceCondition<TSliceValue>(
                Builder builder,
                string sourcePath,
                LibraDexBinaryTypedSliceConditionOperator<TSliceValue> typedOperator,
                Func<TValue, TSliceValue> project,
                out LibraDexConditionEndCondition condition)
            {
                condition = default!;
                TSliceValue ValueAt(int index)
                    => project(builder.MaterializeTemporalParameter(
                        selector,
                        operands[index],
                        sourcePath,
                        owner: this));

                LibraDexConditionContinueOrEnd? translated = operation switch
                {
                    LibraDexTemporalWholeValueOperationKind.GreaterThan when operands.Length == 1 => typedOperator.GreaterThan(ValueAt(0)),
                    LibraDexTemporalWholeValueOperationKind.GreaterOrEqual when operands.Length == 1 => typedOperator.GreaterOrEqual(ValueAt(0)),
                    LibraDexTemporalWholeValueOperationKind.LessThan when operands.Length == 1 => typedOperator.LessThan(ValueAt(0)),
                    LibraDexTemporalWholeValueOperationKind.LessOrEqual when operands.Length == 1 => typedOperator.LessOrEqual(ValueAt(0)),
                    LibraDexTemporalWholeValueOperationKind.EqualTo when operands.Length == 1 => typedOperator.EqualTo(ValueAt(0)),
                    LibraDexTemporalWholeValueOperationKind.Between when operands.Length == 2 => typedOperator.Between(ValueAt(0), ValueAt(1)),
                    _ => null
                };
                if (translated is null)
                    return false;

                condition = translated.EndCondition;
                return true;
            }
        }

        /// <summary>
        /// Identifies one structured temporal predicate that can be translated to a LibraDex date/time descriptor.<br/>
        /// Calendar operations are shared by DateOnly and DateTime, while hour and day-part operations are also used by TimeOnly.<br/>
        /// </summary>
        internal enum LibraDexTemporalStructuredOperationKind
        {
            YearEqual,
            YearNotEqual,
            YearIn,
            YearNotIn,
            YearBetween,
            YearNotBetween,
            YearOnOrAfter,
            YearOnOrBefore,
            MonthEqual,
            MonthNotEqual,
            MonthIn,
            MonthNotIn,
            MonthBetween,
            MonthNotBetween,
            DayEqual,
            DayNotEqual,
            DayIn,
            DayNotIn,
            DayBetween,
            DayNotBetween,
            YearMonth,
            YearMonthIn,
            YearMonthDay,
            YearMonthDayIn,
            YearMonths,
            MonthDay,
            InQuarter,
            InQuarterRange,
            YearQuarter,
            AtQuarterStart,
            AtQuarterEnd,
            AtHalfStart,
            AtHalfEnd,
            AtFirstOfMonth,
            AtLastOfMonth,
            IsToday,
            IsYesterday,
            InLastDays,
            InLastHours,
            InLastMinutes,
            InWeekend,
            InWeekday,
            HourEqual,
            HourIn,
            HourNotIn,
            HourBetween,
            HourNotBetween,
            InMorning,
            InAfternoon,
            InEvening,
            InNight
        }

        /// <summary>
        /// Provides one direct-LibraDex adapter for structured DateOnly, DateTime, and TimeOnly leaves.<br/>
        /// Deferred integer and tuple operands remain descriptors until the execution plan is built, then each is materialized once through the owning Abraxas builder.<br/>
        /// Relative predicates use the builder's condition-scoped UTC clock and explicit lower and upper exact-value bounds, avoiding LibraDex's independent ambient-clock behavior.<br/>
        /// </summary>
        /// <typeparam name="TValue">DateOnly, DateTime, or TimeOnly indexed value type.<br/></typeparam>
        internal abstract class LibraDexTemporalStructuredOperation<TValue> : IConditionLeaf, ILibraDexConditionSource
            where TValue : struct
        {
            private readonly ValueOrExpression<string> selector;
            private readonly LibraDexTemporalStructuredOperationKind operation;
            private readonly ValueOrExpression<int>[] integerOperands;
            private readonly ValueOrExpression<(int year, int month)>[] yearMonthOperands;
            private readonly ValueOrExpression<(int year, int month, int day)>[] yearMonthDayOperands;
            private readonly ValueOrExpression<int>? leadingIntegerOperand;

            /// <summary>
            /// Creates a parameterless structured temporal adapter.<br/>
            /// </summary>
            protected LibraDexTemporalStructuredOperation(
                ValueOrExpression<string> selector,
                LibraDexTemporalStructuredOperationKind operation)
                : this(selector, operation, Array.Empty<ValueOrExpression<int>>())
            {
            }

            /// <summary>
            /// Creates a structured temporal adapter with one integer operand.<br/>
            /// </summary>
            protected LibraDexTemporalStructuredOperation(
                ValueOrExpression<string> selector,
                LibraDexTemporalStructuredOperationKind operation,
                ValueOrExpression<int> operand)
                : this(selector, operation, new[] { operand })
            {
            }

            /// <summary>
            /// Creates a structured temporal adapter with two integer operands.<br/>
            /// </summary>
            protected LibraDexTemporalStructuredOperation(
                ValueOrExpression<string> selector,
                LibraDexTemporalStructuredOperationKind operation,
                ValueOrExpression<int> firstOperand,
                ValueOrExpression<int> secondOperand)
                : this(selector, operation, new[] { firstOperand, secondOperand })
            {
            }

            /// <summary>
            /// Creates a structured temporal adapter with three integer operands.<br/>
            /// </summary>
            protected LibraDexTemporalStructuredOperation(
                ValueOrExpression<string> selector,
                LibraDexTemporalStructuredOperationKind operation,
                ValueOrExpression<int> firstOperand,
                ValueOrExpression<int> secondOperand,
                ValueOrExpression<int> thirdOperand)
                : this(selector, operation, new[] { firstOperand, secondOperand, thirdOperand })
            {
            }

            /// <summary>
            /// Creates a structured component-membership adapter and captures its lightweight operand descriptors once.<br/>
            /// </summary>
            protected LibraDexTemporalStructuredOperation(
                ValueOrExpression<string> selector,
                LibraDexTemporalStructuredOperationKind operation,
                System.Collections.Generic.IEnumerable<ValueOrExpression<int>> operands)
            {
                this.selector = selector;
                this.operation = operation;
                integerOperands = operands is null
                    ? throw new ArgumentNullException(nameof(operands))
                    : new System.Collections.Generic.List<ValueOrExpression<int>>(operands).ToArray();
                yearMonthOperands = Array.Empty<ValueOrExpression<(int year, int month)>>();
                yearMonthDayOperands = Array.Empty<ValueOrExpression<(int year, int month, int day)>>();
            }

            /// <summary>
            /// Creates a structured year/month membership adapter and captures its lightweight operand descriptors once.<br/>
            /// </summary>
            protected LibraDexTemporalStructuredOperation(
                ValueOrExpression<string> selector,
                LibraDexTemporalStructuredOperationKind operation,
                System.Collections.Generic.IEnumerable<ValueOrExpression<(int year, int month)>> operands)
            {
                this.selector = selector;
                this.operation = operation;
                integerOperands = Array.Empty<ValueOrExpression<int>>();
                yearMonthOperands = operands is null
                    ? throw new ArgumentNullException(nameof(operands))
                    : new System.Collections.Generic.List<ValueOrExpression<(int year, int month)>>(operands).ToArray();
                yearMonthDayOperands = Array.Empty<ValueOrExpression<(int year, int month, int day)>>();
            }

            /// <summary>
            /// Creates a structured year/month/day membership adapter and captures its lightweight operand descriptors once.<br/>
            /// </summary>
            protected LibraDexTemporalStructuredOperation(
                ValueOrExpression<string> selector,
                LibraDexTemporalStructuredOperationKind operation,
                System.Collections.Generic.IEnumerable<ValueOrExpression<(int year, int month, int day)>> operands)
            {
                this.selector = selector;
                this.operation = operation;
                integerOperands = Array.Empty<ValueOrExpression<int>>();
                yearMonthOperands = Array.Empty<ValueOrExpression<(int year, int month)>>();
                yearMonthDayOperands = operands is null
                    ? throw new ArgumentNullException(nameof(operands))
                    : new System.Collections.Generic.List<ValueOrExpression<(int year, int month, int day)>>(operands).ToArray();
            }

            /// <summary>
            /// Creates the one-year/several-month adapter while preserving separate deferred descriptors for the year and month collection.<br/>
            /// </summary>
            protected LibraDexTemporalStructuredOperation(
                ValueOrExpression<string> selector,
                LibraDexTemporalStructuredOperationKind operation,
                ValueOrExpression<int> leadingOperand,
                System.Collections.Generic.IEnumerable<ValueOrExpression<int>> operands)
                : this(selector, operation, operands)
            {
                leadingIntegerOperand = leadingOperand;
            }

            /// <summary>
            /// Reports the direct property-path index required by this structured temporal leaf without invoking deferred developer code.<br/>
            /// </summary>
            public bool TryGetLibraDexIndexName(Builder builder, out string indexName)
            {
                indexName = string.Empty;
                if (!TryExtractPropPath(builder, selector, out var propPath))
                    return false;

                indexName = propPath;
                return true;
            }

            /// <summary>
            /// Builds the exact LibraDex descriptor for this structured temporal leaf.<br/>
            /// Missing public negative day conveniences are represented by finite positive component complements; arbitrary TimeOnly hour sets are represented by minimal unions of contiguous exact ranges.<br/>
            /// </summary>
            public bool TryBuildLibraDexCondition(
                Builder builder,
                CatalogIdentityGroupIndexes indexes,
                out LibraDexConditionEndCondition condition,
                out string indexName)
            {
                condition = default!;
                indexName = string.Empty;
                if (indexes is null || !TryExtractPropPath(builder, selector, out var propPath))
                    return false;

                indexName = propPath;
                var valueSelector = indexes.Where(propPath);
                LibraDexDateConditionOperator<TValue>? typedOperator = null;
                if (typeof(TValue) == typeof(DateOnly))
                    typedOperator = (LibraDexDateConditionOperator<TValue>)(object)valueSelector.AsDateOnly;
                else if (typeof(TValue) == typeof(DateTime))
                    typedOperator = (LibraDexDateConditionOperator<TValue>)(object)valueSelector.AsDateTime;
                else if (typeof(TValue) == typeof(TimeOnly))
                    typedOperator = (LibraDexDateConditionOperator<TValue>)(object)valueSelector.AsTimeOnly;

                if (typedOperator is null)
                    return false;

                int IntegerAt(int index)
                    => builder.MaterializeParameter(integerOperands[index], propPath, owner: this);

                int[] Integers()
                {
                    var values = new int[integerOperands.Length];
                    for (var i = 0; i < values.Length; i++)
                        values[i] = IntegerAt(i);
                    return values;
                }

                (int year, int month)[] YearMonths()
                {
                    var values = new (int year, int month)[yearMonthOperands.Length];
                    for (var i = 0; i < values.Length; i++)
                        values[i] = builder.MaterializeParameter(yearMonthOperands[i], propPath, owner: this);
                    return values;
                }

                (int year, int month, int day)[] YearMonthDays()
                {
                    var values = new (int year, int month, int day)[yearMonthDayOperands.Length];
                    for (var i = 0; i < values.Length; i++)
                        values[i] = builder.MaterializeParameter(yearMonthDayOperands[i], propPath, owner: this);
                    return values;
                }

                int LeadingInteger()
                    => builder.MaterializeParameter(leadingIntegerOperand!, propPath, owner: this);

                LibraDexConditionContinueOrEnd? translated = operation switch
                {
                    LibraDexTemporalStructuredOperationKind.YearEqual => typedOperator.YearEqual(IntegerAt(0)),
                    LibraDexTemporalStructuredOperationKind.YearNotEqual => typedOperator.YearNotEqual(IntegerAt(0)),
                    LibraDexTemporalStructuredOperationKind.YearIn => typedOperator.YearIn(Integers()),
                    LibraDexTemporalStructuredOperationKind.YearNotIn => typedOperator.YearNotIn(Integers()),
                    LibraDexTemporalStructuredOperationKind.YearBetween => typedOperator.YearRange(IntegerAt(0), IntegerAt(1)),
                    LibraDexTemporalStructuredOperationKind.YearNotBetween => typedOperator.YearNotRange(IntegerAt(0), IntegerAt(1)),
                    LibraDexTemporalStructuredOperationKind.YearOnOrAfter => typedOperator.OnOrAfterYear(IntegerAt(0)),
                    LibraDexTemporalStructuredOperationKind.YearOnOrBefore => typedOperator.OnOrBeforeYear(IntegerAt(0)),
                    LibraDexTemporalStructuredOperationKind.MonthEqual => typedOperator.MonthEqual(IntegerAt(0)),
                    LibraDexTemporalStructuredOperationKind.MonthNotEqual => typedOperator.MonthNotIn(IntegerAt(0)),
                    LibraDexTemporalStructuredOperationKind.MonthIn => typedOperator.MonthIn(Integers()),
                    LibraDexTemporalStructuredOperationKind.MonthNotIn => typedOperator.MonthNotIn(Integers()),
                    LibraDexTemporalStructuredOperationKind.MonthBetween => typedOperator.MonthRange(IntegerAt(0), IntegerAt(1)),
                    LibraDexTemporalStructuredOperationKind.MonthNotBetween => typedOperator.MonthNotInRange(IntegerAt(0), IntegerAt(1)),
                    LibraDexTemporalStructuredOperationKind.DayEqual => typedOperator.DayEqual(IntegerAt(0)),
                    LibraDexTemporalStructuredOperationKind.DayNotEqual => typedOperator.DayIn(ComponentComplement(1, 31, IntegerAt(0))),
                    LibraDexTemporalStructuredOperationKind.DayIn => typedOperator.DayIn(Integers()),
                    LibraDexTemporalStructuredOperationKind.DayNotIn => typedOperator.DayIn(ComponentComplement(1, 31, Integers())),
                    LibraDexTemporalStructuredOperationKind.DayBetween => typedOperator.DayRange(IntegerAt(0), IntegerAt(1)),
                    LibraDexTemporalStructuredOperationKind.DayNotBetween => typedOperator.NotDayRange(IntegerAt(0), IntegerAt(1)),
                    LibraDexTemporalStructuredOperationKind.YearMonth => typedOperator.YearMonth(IntegerAt(0), IntegerAt(1)),
                    LibraDexTemporalStructuredOperationKind.YearMonthIn => typedOperator.YearMonthIn(YearMonths()),
                    LibraDexTemporalStructuredOperationKind.YearMonthDay => typedOperator.YearMonthDay(IntegerAt(0), IntegerAt(1), IntegerAt(2)),
                    LibraDexTemporalStructuredOperationKind.YearMonthDayIn => typedOperator.YearMonthDayIn(YearMonthDays()),
                    LibraDexTemporalStructuredOperationKind.YearMonths => typedOperator.YearInMonths(LeadingInteger(), Integers()),
                    LibraDexTemporalStructuredOperationKind.MonthDay => typedOperator.MonthDay(IntegerAt(0), IntegerAt(1)),
                    LibraDexTemporalStructuredOperationKind.InQuarter => typedOperator.InQuarter(IntegerAt(0)),
                    LibraDexTemporalStructuredOperationKind.InQuarterRange => typedOperator.InQuarterRange(IntegerAt(0), IntegerAt(1)),
                    LibraDexTemporalStructuredOperationKind.YearQuarter => typedOperator.YearQuarter(IntegerAt(0), IntegerAt(1)),
                    LibraDexTemporalStructuredOperationKind.AtQuarterStart => typedOperator.IsQuarterStart(),
                    LibraDexTemporalStructuredOperationKind.AtQuarterEnd => typedOperator.IsQuarterEnd(),
                    LibraDexTemporalStructuredOperationKind.AtHalfStart => typedOperator.IsHalfYearStart(),
                    LibraDexTemporalStructuredOperationKind.AtHalfEnd => typedOperator.IsHalfYearEnd(),
                    LibraDexTemporalStructuredOperationKind.AtFirstOfMonth => typedOperator.IsFirstOfMonth(),
                    LibraDexTemporalStructuredOperationKind.AtLastOfMonth => typedOperator.IsLastOfMonth(),
                    LibraDexTemporalStructuredOperationKind.InWeekend => typedOperator.IsWeekend(),
                    LibraDexTemporalStructuredOperationKind.InWeekday => typedOperator.IsWeekday(),
                    LibraDexTemporalStructuredOperationKind.InMorning => typedOperator.IsMorning(),
                    LibraDexTemporalStructuredOperationKind.InAfternoon => typedOperator.IsAfternoon(),
                    LibraDexTemporalStructuredOperationKind.InEvening => typedOperator.IsEvening(),
                    LibraDexTemporalStructuredOperationKind.InNight => typedOperator.IsNight(),
                    LibraDexTemporalStructuredOperationKind.IsToday => BuildToday(typedOperator, builder.UtcNow),
                    LibraDexTemporalStructuredOperationKind.IsYesterday => BuildYesterday(typedOperator, builder.UtcNow),
                    LibraDexTemporalStructuredOperationKind.InLastDays => BuildRecent(typedOperator, builder.UtcNow, TimeSpan.FromDays(IntegerAt(0))),
                    LibraDexTemporalStructuredOperationKind.InLastHours when typeof(TValue) == typeof(DateTime) => BuildRecent(typedOperator, builder.UtcNow, TimeSpan.FromHours(IntegerAt(0))),
                    LibraDexTemporalStructuredOperationKind.InLastMinutes when typeof(TValue) == typeof(DateTime) => BuildRecent(typedOperator, builder.UtcNow, TimeSpan.FromMinutes(IntegerAt(0))),
                    LibraDexTemporalStructuredOperationKind.HourEqual when typeof(TValue) == typeof(TimeOnly) => BuildHourSet(typedOperator, propPath, new[] { IntegerAt(0) }),
                    LibraDexTemporalStructuredOperationKind.HourIn when typeof(TValue) == typeof(TimeOnly) => BuildHourSet(typedOperator, propPath, Integers()),
                    LibraDexTemporalStructuredOperationKind.HourNotIn when typeof(TValue) == typeof(TimeOnly) => BuildHourSet(typedOperator, propPath, ComponentComplement(0, 23, Integers())),
                    LibraDexTemporalStructuredOperationKind.HourBetween when typeof(TValue) == typeof(TimeOnly) => BuildHourSet(typedOperator, propPath, Inclusive(IntegerAt(0), IntegerAt(1))),
                    LibraDexTemporalStructuredOperationKind.HourNotBetween when typeof(TValue) == typeof(TimeOnly) => BuildHourSet(typedOperator, propPath, ComponentComplement(0, 23, Inclusive(IntegerAt(0), IntegerAt(1)))),
                    _ => null
                };

                if (translated is null)
                    return false;

                condition = translated.EndCondition;
                return true;
            }

            private static int[] ComponentComplement(int minimum, int maximum, params int[] excluded)
            {
                var exclusion = new System.Collections.Generic.HashSet<int>(excluded);
                var values = new System.Collections.Generic.List<int>(maximum - minimum + 1);
                for (var value = minimum; value <= maximum; value++)
                    if (!exclusion.Contains(value))
                        values.Add(value);
                return values.ToArray();
            }

            private static int[] Inclusive(int start, int end)
            {
                if (end < start)
                    return Array.Empty<int>();
                var values = new int[end - start + 1];
                for (var i = 0; i < values.Length; i++)
                    values[i] = start + i;
                return values;
            }

            private static LibraDexConditionContinueOrEnd? BuildToday(
                LibraDexDateConditionOperator<TValue> typedOperator,
                DateTime utcNow)
            {
                if (typeof(TValue) == typeof(DateOnly))
                    return ((LibraDexDateConditionOperator<DateOnly>)(object)typedOperator).EqualTo(DateOnly.FromDateTime(utcNow));
                if (typeof(TValue) == typeof(DateTime))
                {
                    var start = DateTime.SpecifyKind(utcNow.Date, DateTimeKind.Utc);
                    return ((LibraDexDateConditionOperator<DateTime>)(object)typedOperator).Between(start, start.AddDays(1).AddTicks(-1));
                }
                return null;
            }

            private static LibraDexConditionContinueOrEnd? BuildYesterday(
                LibraDexDateConditionOperator<TValue> typedOperator,
                DateTime utcNow)
            {
                if (typeof(TValue) == typeof(DateOnly))
                    return ((LibraDexDateConditionOperator<DateOnly>)(object)typedOperator).EqualTo(DateOnly.FromDateTime(utcNow).AddDays(-1));
                if (typeof(TValue) == typeof(DateTime))
                {
                    var end = DateTime.SpecifyKind(utcNow.Date, DateTimeKind.Utc).AddTicks(-1);
                    return ((LibraDexDateConditionOperator<DateTime>)(object)typedOperator).Between(end.Date, end);
                }
                return null;
            }

            private static LibraDexConditionContinueOrEnd? BuildRecent(
                LibraDexDateConditionOperator<TValue> typedOperator,
                DateTime utcNow,
                TimeSpan interval)
            {
                if (typeof(TValue) == typeof(DateOnly))
                {
                    var end = DateOnly.FromDateTime(utcNow);
                    return ((LibraDexDateConditionOperator<DateOnly>)(object)typedOperator).Between(end.AddDays(-(int)interval.TotalDays), end);
                }
                if (typeof(TValue) == typeof(DateTime))
                    return ((LibraDexDateConditionOperator<DateTime>)(object)typedOperator).Between(utcNow - interval, utcNow);
                return null;
            }

            private static LibraDexConditionContinueOrEnd BuildHourSet(
                LibraDexDateConditionOperator<TValue> typedOperator,
                string indexName,
                int[] hours)
            {
                var accepted = new bool[24];
                for (var i = 0; i < hours.Length; i++)
                    if ((uint)hours[i] < 24U)
                        accepted[hours[i]] = true;

                LibraDexConditionContinueOrEnd? result = null;
                for (var start = 0; start < accepted.Length;)
                {
                    while (start < accepted.Length && !accepted[start])
                        start++;
                    if (start == accepted.Length)
                        break;

                    var end = start;
                    while (end + 1 < accepted.Length && accepted[end + 1])
                        end++;

                    var lower = new TimeOnly(start, 0);
                    var upper = end == 23 ? TimeOnly.MaxValue : new TimeOnly(end + 1, 0).Add(TimeSpan.FromTicks(-1));
                    result = result is null
                        ? ((LibraDexDateConditionOperator<TimeOnly>)(object)typedOperator).Between(lower, upper)
                        : result.OR.Where(indexName).AsTimeOnly.Between(lower, upper);
                    start = end + 1;
                }

                return result ?? ((LibraDexDateConditionOperator<TimeOnly>)(object)typedOperator).In(Array.Empty<TimeOnly>());
            }
        }

        /// <summary>
        /// Supplies an Abraxas-owned candidate description for a structurally recognized <see cref="DateTime.TimeOfDay"/> range.<br/>
        /// Implementations classify semantics only; the executing RecordStore decides whether an exact complete-path index, a base DateTime index, or authoritative Inheto scanning is available.<br/>
        /// </summary>
        internal interface ITimeOfDayCandidateSource
        {
            /// <summary>
            /// Materializes one execution-scoped candidate description without reading records or opening indexes.<br/>
            /// </summary>
            /// <param name="builder">The condition builder owning the current execution context.<br/></param>
            /// <param name="candidate">The recognized TimeOfDay candidate description.<br/></param>
            /// <returns><see langword="true"/> when the leaf has the required checked structure and supported operation.<br/></returns>
            bool TryBuildTimeOfDayCandidate(Builder builder, out TimeOfDayCandidate candidate);
        }

        /// <summary>
        /// Describes one inclusive TimeOfDay range whose complete PropPath may have an explicit index and whose serialized source may have a structured DateTime index.<br/>
        /// </summary>
        /// <param name="Path">Canonical complete executable property path, such as <c>.CreatedDate.TimeOfDay</c>.<br/></param>
        /// <param name="SourcePath">Canonical directly serialized DateTime path, such as <c>.CreatedDate</c>.<br/></param>
        /// <param name="Start">Inclusive lower TimeSpan boundary.<br/></param>
        /// <param name="End">Inclusive upper TimeSpan boundary.<br/></param>
        internal readonly record struct TimeOfDayCandidate(
            string Path,
            string SourcePath,
            TimeSpan Start,
            TimeSpan End);
    }
}
