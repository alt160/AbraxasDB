using Inheto;

namespace AbraxasDB
{
    public static partial class Condition<T>
    {
        /// <summary>
        /// Tests whether an Inheto property is present and contains a non-null value before a typed comparison reads it.<br/>
        /// Missing and explicit-null values do not become CLR defaults such as zero, <c>false</c>, <see cref="Guid.Empty"/>, or default dates during comparison.<br/>
        /// Null-specific operators continue to use <see cref="InhetoBinary.ReadProp(string, Inheto.DeserializationOptions)"/> directly so they can intentionally match missing or null values.<br/>
        /// </summary>
        /// <param name="binary">The serialized Inheto record being evaluated.<br/></param>
        /// <param name="propertyPath">The normalized member path selected by the condition leaf.<br/></param>
        /// <param name="options">The active Inheto deserialization registrations.<br/></param>
        /// <returns><c>true</c> only when the property exists and contains a non-null value.<br/></returns>
        internal static bool HasConditionValue(
            InhetoBinary binary,
            string propertyPath,
            Inheto.DeserializationOptions options)
            => binary.PropExists(propertyPath, options) && binary.ReadProp(propertyPath, options) is not null;

        /// <summary>
        /// Represents one backend-neutral Boolean condition node evaluated against an Inheto payload.<br/>
        /// </summary>
        internal abstract class SemanticConditionNode
        {
            /// <summary>
            /// Evaluates this semantic condition against one serialized record payload.<br/>
            /// Implementations must preserve short-circuit Boolean behavior so residual scans do not perform unnecessary property reads.<br/>
            /// </summary>
            /// <param name="binary">The Inheto payload for the current Fractal record.<br/></param>
            /// <returns><c>true</c> when the record satisfies this node; otherwise <c>false</c>.<br/></returns>
            internal abstract bool Evaluate(InhetoBinary binary);
        }

        /// <summary>
        /// Wraps one already-materialized typed predicate as a semantic leaf.<br/>
        /// Operand expressions are frozen while the leaf is created, so per-record evaluation reads only the current payload.<br/>
        /// </summary>
        internal sealed class SemanticConditionLeaf : SemanticConditionNode
        {
            private readonly Func<InhetoBinary, bool> evaluator;

            /// <summary>
            /// Creates a semantic leaf around one typed payload evaluator.<br/>
            /// </summary>
            /// <param name="evaluator">The evaluator produced by the corresponding typed Abraxas condition operation.<br/></param>
            internal SemanticConditionLeaf(Func<InhetoBinary, bool> evaluator)
            {
                ArgumentNullException.ThrowIfNull(evaluator);
                this.evaluator = evaluator;
            }

            /// <inheritdoc/>
            internal override bool Evaluate(InhetoBinary binary) => evaluator(binary);
        }

        /// <summary>
        /// Composes two semantic condition nodes using short-circuit AND or OR behavior.<br/>
        /// </summary>
        internal sealed class SemanticConditionBinary : SemanticConditionNode
        {
            private readonly SemanticConditionNode left;
            private readonly SemanticConditionNode right;
            private readonly bool useOr;

            /// <summary>
            /// Creates one binary semantic condition node.<br/>
            /// </summary>
            /// <param name="left">The left condition node.<br/></param>
            /// <param name="right">The right condition node.<br/></param>
            /// <param name="useOr">When <c>true</c>, composes with OR; otherwise composes with AND.<br/></param>
            internal SemanticConditionBinary(SemanticConditionNode left, SemanticConditionNode right, bool useOr)
            {
                ArgumentNullException.ThrowIfNull(left);
                ArgumentNullException.ThrowIfNull(right);
                this.left = left;
                this.right = right;
                this.useOr = useOr;
            }

            /// <inheritdoc/>
            internal override bool Evaluate(InhetoBinary binary)
                => useOr
                    ? left.Evaluate(binary) || right.Evaluate(binary)
                    : left.Evaluate(binary) && right.Evaluate(binary);
        }

        /// <summary>
        /// Represents one backend-neutral condition node that can read both an Inheto payload and the record's optional external State metadata.<br/>
        /// Ordinary payload leaves ignore State; reserved State leaves avoid pretending that metadata was serialized into the developer object.<br/>
        /// </summary>
        internal abstract class RecordSemanticConditionNode
        {
            /// <summary>
            /// Evaluates this node against one record's payload and optional State value.<br/>
            /// </summary>
            /// <param name="binary">Current authoritative Inheto payload.<br/></param>
            /// <param name="state">Current developer-owned State, or <c>null</c> when unassigned.<br/></param>
            /// <returns><see langword="true"/> when the record satisfies this node.<br/></returns>
            internal abstract bool Evaluate(InhetoBinary binary, long? state);

            /// <summary>
            /// Gets whether this node or one of its descendants requires an external State lookup.<br/>
            /// </summary>
            internal abstract bool RequiresState { get; }
        }

        /// <summary>
        /// Wraps either an ordinary payload evaluator or a reserved nullable-State evaluator.<br/>
        /// </summary>
        internal sealed class RecordSemanticConditionLeaf : RecordSemanticConditionNode
        {
            private readonly Func<InhetoBinary, bool>? payloadEvaluator;
            private readonly Func<long?, bool>? stateEvaluator;

            /// <summary>
            /// Creates one ordinary payload-backed leaf.<br/>
            /// </summary>
            /// <param name="payloadEvaluator">Pre-materialized payload predicate.<br/></param>
            internal RecordSemanticConditionLeaf(Func<InhetoBinary, bool> payloadEvaluator)
            {
                ArgumentNullException.ThrowIfNull(payloadEvaluator);
                this.payloadEvaluator = payloadEvaluator;
            }

            /// <summary>
            /// Creates one reserved external-State leaf.<br/>
            /// </summary>
            /// <param name="stateEvaluator">Pre-materialized nullable-State predicate.<br/></param>
            internal RecordSemanticConditionLeaf(Func<long?, bool> stateEvaluator)
            {
                ArgumentNullException.ThrowIfNull(stateEvaluator);
                this.stateEvaluator = stateEvaluator;
            }

            /// <inheritdoc/>
            internal override bool Evaluate(InhetoBinary binary, long? state)
                => stateEvaluator is not null ? stateEvaluator(state) : payloadEvaluator!(binary);

            /// <inheritdoc/>
            internal override bool RequiresState => stateEvaluator is not null;
        }

        /// <summary>
        /// Composes two record-aware semantic nodes with the same short-circuit Boolean contract as ordinary payload evaluation.<br/>
        /// </summary>
        internal sealed class RecordSemanticConditionBinary : RecordSemanticConditionNode
        {
            private readonly RecordSemanticConditionNode left;
            private readonly RecordSemanticConditionNode right;
            private readonly bool useOr;

            /// <summary>
            /// Creates one record-aware Boolean composition.<br/>
            /// </summary>
            /// <param name="left">Left node.<br/></param>
            /// <param name="right">Right node.<br/></param>
            /// <param name="useOr">Whether to compose with OR instead of AND.<br/></param>
            internal RecordSemanticConditionBinary(
                RecordSemanticConditionNode left,
                RecordSemanticConditionNode right,
                bool useOr)
            {
                ArgumentNullException.ThrowIfNull(left);
                ArgumentNullException.ThrowIfNull(right);
                this.left = left;
                this.right = right;
                this.useOr = useOr;
            }

            /// <inheritdoc/>
            internal override bool Evaluate(InhetoBinary binary, long? state)
                => useOr
                    ? left.Evaluate(binary, state) || right.Evaluate(binary, state)
                    : left.Evaluate(binary, state) && right.Evaluate(binary, state);

            /// <inheritdoc/>
            internal override bool RequiresState => left.RequiresState || right.RequiresState;
        }

        public partial class Builder
        {
            /// <summary>
            /// Compiles the builder's typed condition fragments into a backend-neutral semantic evaluator.<br/>
            /// Nested groups are compiled recursively, AND binds more tightly than OR, and every typed operand is materialized once while its leaf is created.<br/>
            /// Unsupported selector or operator shapes return <c>false</c> so the execution planner can report an honest unsupported condition instead of silently changing results.<br/>
            /// </summary>
            /// <param name="options">The Inheto deserialization registrations used by typed property readers.<br/></param>
            /// <param name="evaluator">The compiled payload evaluator when this method returns <c>true</c>.<br/></param>
            /// <returns><c>true</c> when the complete condition can be evaluated from Inheto payloads; otherwise <c>false</c>.<br/></returns>
            internal bool TryBuildSemanticEvaluator(
                Inheto.DeserializationOptions options,
                out Func<InhetoBinary, bool> evaluator)
            {
                evaluator = default!;
                if (!TryBuildSemanticNode(this, options, out SemanticConditionNode? node) || node is null)
                    return false;

                evaluator = node.Evaluate;
                return true;
            }

            /// <summary>
            /// Compiles a complete condition into a record-aware evaluator that can combine ordinary Inheto properties with reserved external State.<br/>
            /// The returned <paramref name="requiresState"/> flag lets RecordStore avoid any State lookup for the overwhelmingly common payload-only condition.<br/>
            /// </summary>
            /// <param name="options">Inheto deserialization registrations used by ordinary typed property readers.<br/></param>
            /// <param name="evaluator">Compiled payload-plus-State evaluator when supported.<br/></param>
            /// <param name="requiresState">Whether at least one leaf reads the reserved State value.<br/></param>
            /// <returns><see langword="true"/> when every fragment has record-aware semantics.<br/></returns>
            internal bool TryBuildRecordSemanticEvaluator(
                Inheto.DeserializationOptions options,
                out Func<InhetoBinary, long?, bool> evaluator,
                out bool requiresState)
            {
                evaluator = default!;
                requiresState = false;
                if (!TryBuildRecordSemanticNode(this, options, out RecordSemanticConditionNode? node) || node is null)
                    return false;

                evaluator = node.Evaluate;
                requiresState = node.RequiresState;
                return true;
            }

            /// <summary>
            /// Parses one condition tree into record-aware leaves while preserving AND-before-OR precedence and nested parameter scope.<br/>
            /// </summary>
            /// <param name="source">Builder whose complete fragment sequence is compiled.<br/></param>
            /// <param name="options">Inheto options used by ordinary payload leaves.<br/></param>
            /// <param name="node">Compiled record-aware root when successful.<br/></param>
            /// <returns><see langword="true"/> when the complete tree is supported.<br/></returns>
            private static bool TryBuildRecordSemanticNode(
                Builder source,
                Inheto.DeserializationOptions options,
                out RecordSemanticConditionNode? node)
            {
                node = null;
                var operands = new List<RecordSemanticConditionNode>();
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

                    RecordSemanticConditionNode next;
                    if (fragment is GroupConditionFragment group)
                    {
                        using var groupContext = group.GroupBuilder.PushParameterContext(source.CurrentParameterContext);
                        if (!TryBuildRecordSemanticNode(group.GroupBuilder, options, out RecordSemanticConditionNode? grouped) || grouped is null)
                            return false;
                        next = grouped;
                    }
                    else if (fragment is IStatePredicateSource stateSource &&
                             stateSource.TryBuildStateEvaluator(source, out Func<long?, bool> stateEvaluator))
                    {
                        next = new RecordSemanticConditionLeaf(stateEvaluator);
                    }
                    else if (fragment is IFastPredicateSource payloadSource &&
                             payloadSource.TryBuildEvaluator(source, options, out Func<InhetoBinary, bool> payloadEvaluator))
                    {
                        next = new RecordSemanticConditionLeaf(payloadEvaluator);
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

                    operands[i] = new RecordSemanticConditionBinary(operands[i], operands[i + 1], useOr: false);
                    operands.RemoveAt(i + 1);
                    connectors.RemoveAt(i);
                }

                node = operands[0];
                for (var i = 0; i < connectors.Count; i++)
                    node = new RecordSemanticConditionBinary(node, operands[i + 1], useOr: true);

                return true;
            }

            /// <summary>
            /// Parses one builder into semantic operands and logical connectors without consulting any storage backend.<br/>
            /// Wrapper fragments are ignored, group fragments recurse into their cloned builders, and typed leaves must implement the current payload-evaluator contract.<br/>
            /// </summary>
            /// <param name="source">The builder whose fragment sequence should be parsed.<br/></param>
            /// <param name="options">The Inheto deserialization registrations used by typed leaf evaluators.<br/></param>
            /// <param name="node">The semantic root when the complete fragment sequence is supported.<br/></param>
            /// <returns><c>true</c> when the entire fragment sequence was composed; otherwise <c>false</c>.<br/></returns>
            private static bool TryBuildSemanticNode(
                Builder source,
                Inheto.DeserializationOptions options,
                out SemanticConditionNode? node)
            {
                node = null;
                var operands = new List<SemanticConditionNode>();
                var connectors = new List<bool>();
                bool? pendingUseOr = null;

                foreach (IConditionFragment fragment in source.fragments)
                {
                    if (fragment is WhereFragment or EndCondition or GroupStartFragment or GroupEndFragment)
                        continue;

                    if (fragment is AndOperation)
                    {
                        if (operands.Count == 0 || pendingUseOr.HasValue)
                            return false;
                        pendingUseOr = false;
                        continue;
                    }

                    if (fragment is OrOperation)
                    {
                        if (operands.Count == 0 || pendingUseOr.HasValue)
                            return false;
                        pendingUseOr = true;
                        continue;
                    }

                    SemanticConditionNode next;
                    if (fragment is GroupConditionFragment group)
                    {
                        using var groupContext = group.GroupBuilder.PushParameterContext(source.CurrentParameterContext);
                        if (!TryBuildSemanticNode(group.GroupBuilder, options, out SemanticConditionNode? grouped) || grouped is null)
                            return false;
                        next = grouped;
                    }
                    else if (fragment is IFastPredicateSource sourceLeaf && sourceLeaf.TryBuildEvaluator(source, options, out Func<InhetoBinary, bool> leafEvaluator))
                    {
                        next = new SemanticConditionLeaf(leafEvaluator);
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

                    operands[i] = new SemanticConditionBinary(operands[i], operands[i + 1], useOr: false);
                    operands.RemoveAt(i + 1);
                    connectors.RemoveAt(i);
                }

                node = operands[0];
                for (var i = 0; i < connectors.Count; i++)
                    node = new SemanticConditionBinary(node, operands[i + 1], useOr: true);

                return true;
            }
        }
    }
}
