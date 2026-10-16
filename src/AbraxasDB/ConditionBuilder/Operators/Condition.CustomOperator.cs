using System.Collections.Generic;
using Inheto;
using LibraDex;

namespace AbraxasDB
{
    public static partial class Condition<T>
    {
        /// <summary>
        /// Provides equality conditions for an application-defined CLR value type registered with the executing record store's binary codec registry.<br/>
        /// The typed value remains visible at the Abraxas surface while Inheto and LibraDex consume the developer-supplied deterministic byte representation.<br/>
        /// </summary>
        /// <typeparam name="TValue">The exact registered application CLR type.<br/></typeparam>
        public sealed class CustomOperator<TValue>
        {
            private readonly Builder builder;
            private readonly ValueOrExpression<string> selectorExpression;

            /// <summary>
            /// Creates a custom typed operator for one selected property path.<br/>
            /// </summary>
            /// <param name="builder">The owning Abraxas condition builder.<br/></param>
            /// <param name="selectorExpression">The selected property-path expression.<br/></param>
            internal CustomOperator(Builder builder, ValueOrExpression<string> selectorExpression)
            {
                this.builder = builder;
                this.selectorExpression = selectorExpression;
            }

            /// <summary>
            /// Adds an equality comparison using the codec registered for <typeparamref name="TValue"/> on the executing record store.<br/>
            /// </summary>
            /// <param name="valueOrExpression">The static or deferred typed operand.<br/></param>
            /// <returns>A continuation that can end the condition or add another logical clause.<br/></returns>
            public ContinueOrEndCondition EqualTo(ValueOrExpression<TValue> valueOrExpression)
                => AddOperation(valueOrExpression, negate: false);

            /// <summary>
            /// Adds an inequality comparison using the codec registered for <typeparamref name="TValue"/> on the executing record store.<br/>
            /// </summary>
            /// <param name="valueOrExpression">The static or deferred typed operand.<br/></param>
            /// <returns>A continuation that can end the condition or add another logical clause.<br/></returns>
            public ContinueOrEndCondition NotEqualTo(ValueOrExpression<TValue> valueOrExpression)
                => AddOperation(valueOrExpression, negate: true);

            /// <summary>
            /// Appends one custom typed equality leaf while retaining the selected property path for parameter overrides and materialization events.<br/>
            /// </summary>
            /// <param name="valueOrExpression">The static or deferred typed operand.<br/></param>
            /// <param name="negate">Whether the equality result should be negated.<br/></param>
            /// <returns>A continuation for the owning condition builder.<br/></returns>
            private ContinueOrEndCondition AddOperation(ValueOrExpression<TValue> valueOrExpression, bool negate)
            {
                builder.AddFragment(new CustomOperation<TValue>(selectorExpression, valueOrExpression, negate));
                return new ContinueOrEndCondition(builder);
            }
        }

        /// <summary>
        /// Represents one backend-neutral custom typed equality leaf and its optional LibraDex binary-index translation.<br/>
        /// </summary>
        /// <typeparam name="TValue">The exact registered application CLR type.<br/></typeparam>
        private sealed class CustomOperation<TValue> : IConditionLeaf,IFastPredicateSource,ILibraDexConditionSource
        {
            private readonly ValueOrExpression<string> selector;
            private readonly ValueOrExpression<TValue> parameter;
            private readonly bool negate;

            /// <summary>
            /// Creates one custom typed equality operation.<br/>
            /// </summary>
            /// <param name="selector">The selected property-path expression.<br/></param>
            /// <param name="parameter">The static or deferred typed operand.<br/></param>
            /// <param name="negate">Whether the equality result should be negated.<br/></param>
            internal CustomOperation(ValueOrExpression<string> selector, ValueOrExpression<TValue> parameter, bool negate)
            {
                this.selector = selector;
                this.parameter = parameter;
                this.negate = negate;
            }

            /// <summary>
            /// Builds one typed residual evaluator over an Inheto payload.<br/>
            /// The operand is materialized once per execution plan and missing or explicit-null properties do not compare as CLR default values.<br/>
            /// </summary>
            /// <param name="builder">The owning condition builder and current execution context.<br/></param>
            /// <param name="options">The active Inheto deserialization registrations.<br/></param>
            /// <param name="evaluator">The materialized payload evaluator when supported.<br/></param>
            /// <returns><c>true</c> when a direct property path can be evaluated; otherwise <c>false</c>.<br/></returns>
            public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
            {
                evaluator = default!;
                if (!TryExtractPropPath(builder, selector, out var propPath))
                    return false;

                var target = builder.MaterializeParameter(parameter, propPath, owner: this);
                Inheto.PropPathCheck pathCheck = InhetoBinary.CheckPropPath<T, TValue>(propPath);
                if (!pathCheck.IsValid)
                    return false;
                evaluator = binary =>
                {
                    Inheto.PropPathReadState state = binary.TryReadPropPath(
                        pathCheck,
                        out TValue? candidate,
                        options,
                        throwOnInvocationError: true);
                    if (state != Inheto.PropPathReadState.Value)
                        return false;

                    var equal = EqualityComparer<TValue>.Default.Equals(candidate, target);
                    return negate ? !equal : equal;
                };
                return true;
            }

            /// <summary>
            /// Reports the selected LibraDex grouped-index name without invoking the developer-supplied operand expression.<br/>
            /// </summary>
            /// <param name="builder">The owning condition builder used to resolve the property-path selector without evaluating the custom operand.<br/></param>
            /// <param name="indexName">The property-path index name when directly selectable.<br/></param>
            /// <returns><c>true</c> when a direct property path was extracted; otherwise <c>false</c>.<br/></returns>
            public bool TryGetLibraDexIndexName(Builder builder, out string indexName)
            {
                indexName = string.Empty;
                if (!TryExtractPropPath(builder, selector, out var propPath))
                    return false;

                indexName = propPath;
                return true;
            }

            /// <summary>
            /// Translates the typed equality leaf into a LibraDex fixed-width binary-key condition.<br/>
            /// The active record store performs the same checked encoding used by Inheto persistence and index maintenance.<br/>
            /// </summary>
            /// <param name="builder">The owning condition builder and current execution context.<br/></param>
            /// <param name="indexes">The active LibraDex identity group.<br/></param>
            /// <param name="condition">The translated LibraDex condition when supported.<br/></param>
            /// <param name="indexName">The selected grouped-index name.<br/></param>
            /// <returns><c>true</c> when the store has a codec for <typeparamref name="TValue"/> and the condition was translated; otherwise <c>false</c>.<br/></returns>
            public bool TryBuildLibraDexCondition(Builder builder, CatalogIdentityGroupIndexes indexes, out LibraDexConditionEndCondition condition, out string indexName)
            {
                condition = default!;
                indexName = string.Empty;
                if (indexes is null || !TryExtractPropPath(builder, selector, out var propPath))
                    return false;

                var store = builder.CurrentParameterContext?.RecordStore;
                if (store is null)
                    return false;

                var target = builder.MaterializeParameter(parameter, propPath, owner: this);
                if (!store.TryEncodeCustomIndexKey(target, propPath, out var encoded))
                    return false;

                indexName = propPath;
                var op = indexes.Where(propPath).AsBinary;
                condition = negate ? op.NotEqualTo(encoded).EndCondition : op.EqualTo(encoded).EndCondition;
                return true;
            }
        }
    }
}
