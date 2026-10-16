using System;
using System.Linq.Expressions;
using System.Text;
using Inheto;
using LibraDex;

namespace AbraxasDB
{

    public static partial class Condition<T>
    {
        #region Operators

        /// <summary>
        /// Fluent TimeOnly condition operations over a selected property or calculated value.<br/>
        /// </summary>
        /// <remarks>
        /// Condition authoring is separate from execution; mutable builder instances should not be edited concurrently.<br/>
        /// </remarks>
        public class TimeOnlyOperator
        {
            private readonly Builder builder;
            private readonly ValueOrExpression<string> selectorExpression;

            #region Operation Fragments

            // ==========================================================
            // Basic comparisons on full structured time (H/M/S/ms/ns)
            // ==========================================================

            internal sealed class GreaterThanOperation : LibraDexTemporalWholeValueOperation<TimeOnly>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly ValueOrExpression<TimeOnly> param;

                public GreaterThanOperation(ValueOrExpression<string> selector, ValueOrExpression<TimeOnly> param)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.GreaterThan, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<TimeOnly> param)
                {
                    builder.AddFragment(new GreaterThanOperation(selector, param));
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                    var target = builder.MaterializeTemporalParameter(selector, param, propPath);
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<TimeOnly>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate > target;
                    };
                    return true;
                }
            }

            internal sealed class GreaterThanEqualOperation : LibraDexTemporalWholeValueOperation<TimeOnly>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly ValueOrExpression<TimeOnly> param;

                public GreaterThanEqualOperation(ValueOrExpression<string> selector, ValueOrExpression<TimeOnly> param)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.GreaterOrEqual, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<TimeOnly> param)
                {
                    builder.AddFragment(new GreaterThanEqualOperation(selector, param));
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                    var target = builder.MaterializeTemporalParameter(selector, param, propPath);
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<TimeOnly>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate >= target;
                    };
                    return true;
                }
            }

            internal sealed class LessThanOperation : LibraDexTemporalWholeValueOperation<TimeOnly>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly ValueOrExpression<TimeOnly> param;

                public LessThanOperation(ValueOrExpression<string> selector, ValueOrExpression<TimeOnly> param)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.LessThan, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<TimeOnly> param)
                {
                    builder.AddFragment(new LessThanOperation(selector, param));
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                    var target = builder.MaterializeTemporalParameter(selector, param, propPath);
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<TimeOnly>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate < target;
                    };
                    return true;
                }
            }

            internal sealed class LessThanEqualOperation : LibraDexTemporalWholeValueOperation<TimeOnly>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly ValueOrExpression<TimeOnly> param;

                public LessThanEqualOperation(ValueOrExpression<string> selector, ValueOrExpression<TimeOnly> param)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.LessOrEqual, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<TimeOnly> param)
                {
                    builder.AddFragment(new LessThanEqualOperation(selector, param));
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                    var target = builder.MaterializeTemporalParameter(selector, param, propPath);
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<TimeOnly>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate <= target;
                    };
                    return true;
                }
            }

            internal sealed class EqualOperation : LibraDexTemporalWholeValueOperation<TimeOnly>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly ValueOrExpression<TimeOnly> param;

                public EqualOperation(ValueOrExpression<string> selector, ValueOrExpression<TimeOnly> param)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.EqualTo, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<TimeOnly> param)
                {
                    builder.AddFragment(new EqualOperation(selector, param));
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                    var target = builder.MaterializeTemporalParameter(selector, param, propPath);
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<TimeOnly>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate == target;
                    };
                    return true;
                }
            }

            internal sealed class NotEqualOperation : LibraDexTemporalWholeValueOperation<TimeOnly>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly ValueOrExpression<TimeOnly> param;

                public NotEqualOperation(ValueOrExpression<string> selector, ValueOrExpression<TimeOnly> param)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.NotEqualTo, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<TimeOnly> param)
                {
                    builder.AddFragment(new NotEqualOperation(selector, param));
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                    var target = builder.MaterializeTemporalParameter(selector, param, propPath);
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<TimeOnly>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate != target;
                    };
                    return true;
                }
            }

            internal sealed class BetweenOperation : LibraDexTemporalWholeValueOperation<TimeOnly>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly ValueOrExpression<TimeOnly> startParam;
                private readonly ValueOrExpression<TimeOnly> endParam;

                public BetweenOperation(ValueOrExpression<string> selector, ValueOrExpression<TimeOnly> startParam, ValueOrExpression<TimeOnly> endParam)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.Between, startParam, endParam)
                {
                    this.selector = selector;
                    this.startParam = startParam;
                    this.endParam = endParam;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<TimeOnly> startParam, ValueOrExpression<TimeOnly> endParam)
                {
                    builder.AddFragment(new BetweenOperation(selector, startParam, endParam));
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                    var start = builder.MaterializeTemporalParameter(selector, startParam, propPath);
                    var end = builder.MaterializeTemporalParameter(selector, endParam, propPath);
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<TimeOnly>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate >= start && candidate <= end;
                    };
                    return true;
                }
            }

            internal sealed class NotBetweenOperation : LibraDexTemporalWholeValueOperation<TimeOnly>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly ValueOrExpression<TimeOnly> startParam;
                private readonly ValueOrExpression<TimeOnly> endParam;

                public NotBetweenOperation(ValueOrExpression<string> selector, ValueOrExpression<TimeOnly> startParam, ValueOrExpression<TimeOnly> endParam)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.NotBetween, startParam, endParam)
                {
                    this.selector = selector;
                    this.startParam = startParam;
                    this.endParam = endParam;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<TimeOnly> startParam, ValueOrExpression<TimeOnly> endParam)
                {
                    builder.AddFragment(new NotBetweenOperation(selector, startParam, endParam));
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                    var start = builder.MaterializeTemporalParameter(selector, startParam, propPath);
                    var end = builder.MaterializeTemporalParameter(selector, endParam, propPath);
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<TimeOnly>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate < start || candidate > end;
                    };
                    return true;
                }
            }

            internal sealed class InOperation : LibraDexTemporalWholeValueOperation<TimeOnly>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly IEnumerable<ValueOrExpression<TimeOnly>> parameters;
                private readonly ValueOrExpression<IEnumerable<TimeOnly>>? setParameter;

                public InOperation(ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<TimeOnly>> parameters)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.In, parameters)
                {
                    this.selector = selector;
                    this.parameters = parameters;
                }

                /// <summary>
                /// Retains one whole-set membership operand without invoking or enumerating it during construction or descriptor replay.<br/>
                /// Existing execution preparation materializes the set before record evaluation and retains explicit enumerable parameter snapshot identity.<br/>
                /// </summary>
                /// <param name="selector">Temporal value selector used by semantic and indexed execution.<br/></param>
                /// <param name="setParameter">Static, factory-backed or explicit-parameter enumerable operand.<br/></param>
                public InOperation(ValueOrExpression<string> selector, ValueOrExpression<IEnumerable<TimeOnly>> setParameter)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.In, setParameter)
                {
                    this.selector = selector;
                    parameters = Array.Empty<ValueOrExpression<TimeOnly>>();
                    this.setParameter = setParameter;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<TimeOnly>> parameters)
                {
                    builder.AddFragment(new InOperation(selector, parameters));
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ConditionParameter<IEnumerable<TimeOnly>> setParameter)
                {
                    builder.AddFragment(new InOperation(selector, setParameter));
                }

                /// <summary>
                /// Builds the backend-neutral membership evaluator and freezes every deferred operand once for this execution plan.<br/>
                /// Present values are tested through a typed hash set while missing and null values remain filtered out.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped parameter materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the typed record value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the selector is a property path.<br/></param>
                /// <returns>True when semantic evaluation can be built; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    return setParameter is null
                        ? TryBuildMembershipEvaluator(builder, selector, parameters, options, negate: false, owner: this, out evaluator)
                        : TryBuildMembershipEvaluator(builder, selector, setParameter, options, negate: false, owner: this, out evaluator);
                }
            }

            internal sealed class NotInOperation : LibraDexTemporalWholeValueOperation<TimeOnly>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly IEnumerable<ValueOrExpression<TimeOnly>> parameters;
                private readonly ValueOrExpression<IEnumerable<TimeOnly>>? setParameter;

                public NotInOperation(ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<TimeOnly>> parameters)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.NotIn, parameters)
                {
                    this.selector = selector;
                    this.parameters = parameters;
                }

                /// <summary>
                /// Retains one whole-set membership operand without invoking or enumerating it during construction or descriptor replay.<br/>
                /// Existing execution preparation materializes the set before record evaluation and retains explicit enumerable parameter snapshot identity.<br/>
                /// </summary>
                /// <param name="selector">Temporal value selector used by semantic and indexed execution.<br/></param>
                /// <param name="setParameter">Static, factory-backed or explicit-parameter enumerable operand.<br/></param>
                public NotInOperation(ValueOrExpression<string> selector, ValueOrExpression<IEnumerable<TimeOnly>> setParameter)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.NotIn, setParameter)
                {
                    this.selector = selector;
                    parameters = Array.Empty<ValueOrExpression<TimeOnly>>();
                    this.setParameter = setParameter;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<TimeOnly>> parameters)
                {
                    builder.AddFragment(new NotInOperation(selector, parameters));
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ConditionParameter<IEnumerable<TimeOnly>> setParameter)
                {
                    builder.AddFragment(new NotInOperation(selector, setParameter));
                }

                /// <summary>
                /// Builds the backend-neutral negated membership evaluator and freezes every deferred operand once for this execution plan.<br/>
                /// Present values are tested through a typed hash set while missing and null values remain filtered out.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped parameter materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the typed record value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the selector is a property path.<br/></param>
                /// <returns>True when semantic evaluation can be built; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    return setParameter is null
                        ? TryBuildMembershipEvaluator(builder, selector, parameters, options, negate: true, owner: this, out evaluator)
                        : TryBuildMembershipEvaluator(builder, selector, setParameter, options, negate: true, owner: this, out evaluator);
                }
            }

            // ==========================================================
            // Hour selectors (bits 36..40, mask 0x1F)
            // ==========================================================

            internal sealed class HourEqualOperation : LibraDexTemporalStructuredOperation<TimeOnly>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly ValueOrExpression<int> param;

                public HourEqualOperation(ValueOrExpression<string> selector, ValueOrExpression<int> param)
                    : base(selector, LibraDexTemporalStructuredOperationKind.HourEqual, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<int> param)
                {
                    builder.AddFragment(new HourEqualOperation(selector, param));
                }
                /// <summary>
                /// Builds the backend-neutral TimeOnly evaluator for the HourEqual condition.<br/>
                /// Every deferred hour operand is frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the TimeOnly property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                var target = builder.MaterializeParameter(param, propPath, owner: this);
                return TryBuildTypedEvaluator<TimeOnly>(builder, selector, options, candidate => candidate.Hour == target, out evaluator);
                }
            }

            internal sealed class HourInOperation : LibraDexTemporalStructuredOperation<TimeOnly>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly IEnumerable<ValueOrExpression<int>> parameters;

                public HourInOperation(ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<int>> parameters)
                    : base(selector, LibraDexTemporalStructuredOperationKind.HourIn, parameters)
                {
                    this.selector = selector;
                    this.parameters = parameters;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<int>> parameters)
                {
                    builder.AddFragment(new HourInOperation(selector, parameters));
                }
                /// <summary>
                /// Builds the backend-neutral TimeOnly evaluator for the HourIn condition.<br/>
                /// Every deferred hour operand is frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the TimeOnly property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                var accepted = parameters.Select(value => builder.MaterializeParameter(value, propPath, owner: this)).ToHashSet();
                return TryBuildTypedEvaluator<TimeOnly>(builder, selector, options, candidate => accepted.Contains(candidate.Hour), out evaluator);
                }
            }

            internal sealed class HourNotInOperation : LibraDexTemporalStructuredOperation<TimeOnly>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly IEnumerable<ValueOrExpression<int>> parameters;

                public HourNotInOperation(ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<int>> parameters)
                    : base(selector, LibraDexTemporalStructuredOperationKind.HourNotIn, parameters)
                {
                    this.selector = selector;
                    this.parameters = parameters;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<int>> parameters)
                {
                    builder.AddFragment(new HourNotInOperation(selector, parameters));
                }
                /// <summary>
                /// Builds the backend-neutral TimeOnly evaluator for the HourNotIn condition.<br/>
                /// Every deferred hour operand is frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the TimeOnly property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                var excluded = parameters.Select(value => builder.MaterializeParameter(value, propPath, owner: this)).ToHashSet();
                return TryBuildTypedEvaluator<TimeOnly>(builder, selector, options, candidate => !excluded.Contains(candidate.Hour), out evaluator);
                }
            }

            internal sealed class HourBetweenOperation : LibraDexTemporalStructuredOperation<TimeOnly>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly ValueOrExpression<int> startParam;
                private readonly ValueOrExpression<int> endParam;

                public HourBetweenOperation(ValueOrExpression<string> selector, ValueOrExpression<int> startParam, ValueOrExpression<int> endParam)
                    : base(selector, LibraDexTemporalStructuredOperationKind.HourBetween, startParam, endParam)
                {
                    this.selector = selector;
                    this.startParam = startParam;
                    this.endParam = endParam;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<int> startParam, ValueOrExpression<int> endParam)
                {
                    builder.AddFragment(new HourBetweenOperation(selector, startParam, endParam));
                }
                /// <summary>
                /// Builds the backend-neutral TimeOnly evaluator for the HourBetween condition.<br/>
                /// Every deferred hour operand is frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the TimeOnly property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                var start = builder.MaterializeParameter(startParam, propPath, owner: this);
                var end = builder.MaterializeParameter(endParam, propPath, owner: this);
                return TryBuildTypedEvaluator<TimeOnly>(builder, selector, options, candidate => candidate.Hour >= start && candidate.Hour <= end, out evaluator);
                }
            }

            internal sealed class HourNotBetweenOperation : LibraDexTemporalStructuredOperation<TimeOnly>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly ValueOrExpression<int> startParam;
                private readonly ValueOrExpression<int> endParam;

                public HourNotBetweenOperation(ValueOrExpression<string> selector, ValueOrExpression<int> startParam, ValueOrExpression<int> endParam)
                    : base(selector, LibraDexTemporalStructuredOperationKind.HourNotBetween, startParam, endParam)
                {
                    this.selector = selector;
                    this.startParam = startParam;
                    this.endParam = endParam;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<int> startParam, ValueOrExpression<int> endParam)
                {
                    builder.AddFragment(new HourNotBetweenOperation(selector, startParam, endParam));
                }
                /// <summary>
                /// Builds the backend-neutral TimeOnly evaluator for the HourNotBetween condition.<br/>
                /// Every deferred hour operand is frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the TimeOnly property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                var start = builder.MaterializeParameter(startParam, propPath, owner: this);
                var end = builder.MaterializeParameter(endParam, propPath, owner: this);
                return TryBuildTypedEvaluator<TimeOnly>(builder, selector, options, candidate => candidate.Hour < start || candidate.Hour > end, out evaluator);
                }
            }

            // ==========================================================
            // Semantic hour ranges: morning / afternoon / evening / night
            // (same logic as your DateOperator time-of-day helpers)
            // ==========================================================

            internal sealed class InMorningOperation : LibraDexTemporalStructuredOperation<TimeOnly>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;

                public InMorningOperation(ValueOrExpression<string> selector)
                    : base(selector, LibraDexTemporalStructuredOperationKind.InMorning)
                {
                    this.selector = selector;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector)
                {
                    builder.AddFragment(new InMorningOperation(selector));
                }
                /// <summary>
                /// Builds the backend-neutral TimeOnly evaluator for the InMorning condition.<br/>
                /// Every deferred hour operand is frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the TimeOnly property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                return TryBuildTypedEvaluator<TimeOnly>(builder, selector, options, candidate => candidate.Hour is >= 5 and <= 11, out evaluator);
                }
            }

            internal sealed class InAfternoonOperation : LibraDexTemporalStructuredOperation<TimeOnly>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;

                public InAfternoonOperation(ValueOrExpression<string> selector)
                    : base(selector, LibraDexTemporalStructuredOperationKind.InAfternoon)
                {
                    this.selector = selector;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector)
                {
                    builder.AddFragment(new InAfternoonOperation(selector));
                }
                /// <summary>
                /// Builds the backend-neutral TimeOnly evaluator for the InAfternoon condition.<br/>
                /// Every deferred hour operand is frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the TimeOnly property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                return TryBuildTypedEvaluator<TimeOnly>(builder, selector, options, candidate => candidate.Hour is >= 12 and <= 16, out evaluator);
                }
            }

            internal sealed class InEveningOperation : LibraDexTemporalStructuredOperation<TimeOnly>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;

                public InEveningOperation(ValueOrExpression<string> selector)
                    : base(selector, LibraDexTemporalStructuredOperationKind.InEvening)
                {
                    this.selector = selector;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector)
                {
                    builder.AddFragment(new InEveningOperation(selector));
                }
                /// <summary>
                /// Builds the backend-neutral TimeOnly evaluator for the InEvening condition.<br/>
                /// Every deferred hour operand is frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the TimeOnly property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                return TryBuildTypedEvaluator<TimeOnly>(builder, selector, options, candidate => candidate.Hour is >= 17 and <= 21, out evaluator);
                }
            }

            internal sealed class InNightOperation : LibraDexTemporalStructuredOperation<TimeOnly>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;

                public InNightOperation(ValueOrExpression<string> selector)
                    : base(selector, LibraDexTemporalStructuredOperationKind.InNight)
                {
                    this.selector = selector;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector)
                {
                    builder.AddFragment(new InNightOperation(selector));
                }
                /// <summary>
                /// Builds the backend-neutral TimeOnly evaluator for the InNight condition.<br/>
                /// Every deferred hour operand is frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the TimeOnly property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                return TryBuildTypedEvaluator<TimeOnly>(builder, selector, options, candidate => candidate.Hour >= 22 || candidate.Hour <= 4, out evaluator);
                }
            }

            #endregion

            /// <summary>
            /// Creates the TimeOnlyOperator stage over the supplied condition builder and selector.<br/>
            /// </summary>
            /// <param name="builder">Mutable builder that receives authored condition fragments.<br/></param>
            /// <param name="selectorExpression">Selected property path or calculated selector intent.<br/></param>
            /// <remarks>
            /// Construction retains the builder and selector; subsequent fluent operations append the actual predicate.<br/>
            /// </remarks>
            public TimeOnlyOperator(Builder builder, ValueOrExpression<string> selectorExpression )
            {
                this.builder = builder;
                this.selectorExpression = selectorExpression;
             }

            private ContinueOrEndCondition Execute(Action action)
            {
                action();
                return new ContinueOrEndCondition(builder);
            }

            // ==========================================================
            // Public fluent surface
            // ==========================================================

            // Full structured time comparisons
            /// <summary>
            /// Adds a strict greater-than comparison against the selected value.<br/>
            /// </summary>
            /// <param name="value">Comparison operand. May be static, factory-backed, or parameter-bound; execution resolves its value.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition GreaterThan(ValueOrExpression<TimeOnly> value)
            {
                return Execute(() => GreaterThanOperation.AddToBuilder(builder, selectorExpression, value));
            }

            /// <summary>
            /// Adds a strict less-than comparison against the selected value.<br/>
            /// </summary>
            /// <param name="value">Comparison operand. May be static, factory-backed, or parameter-bound; execution resolves its value.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition LessThan(ValueOrExpression<TimeOnly> value)
            {
                return Execute(() => LessThanOperation.AddToBuilder(builder, selectorExpression, value));
            }

            /// <summary>
            /// Adds a greater-than-or-equal comparison against the selected value.<br/>
            /// </summary>
            /// <param name="value">Comparison operand. May be static, factory-backed, or parameter-bound; execution resolves its value.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition GreaterOrEqual(ValueOrExpression<TimeOnly> value)
            {
                return Execute(() => GreaterThanEqualOperation.AddToBuilder(builder, selectorExpression, value));
            }

            /// <summary>
            /// Adds a less-than-or-equal comparison against the selected value.<br/>
            /// </summary>
            /// <param name="value">Comparison operand. May be static, factory-backed, or parameter-bound; execution resolves its value.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition LessOrEqual(ValueOrExpression<TimeOnly> value)
            {
                return Execute(() => LessThanEqualOperation.AddToBuilder(builder, selectorExpression, value));
            }

            /// <summary>
            /// Adds equality against the selected value.<br/>
            /// </summary>
            /// <param name="value">Comparison operand. May be static, factory-backed, or parameter-bound; execution resolves its value.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition EqualTo(ValueOrExpression<TimeOnly> value)
            {
                return Execute(() => EqualOperation.AddToBuilder(builder, selectorExpression, value));
            }

            /// <summary>
            /// Adds inequality against the selected value.<br/>
            /// </summary>
            /// <param name="value">Comparison operand. May be static, factory-backed, or parameter-bound; execution resolves its value.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition NotEqualTo(ValueOrExpression<TimeOnly> value)
            {
                return Execute(() => NotEqualOperation.AddToBuilder(builder, selectorExpression, value));
            }

            /// <summary>
            /// Adds an inclusive lower-and-upper-bound comparison against the selected value.<br/>
            /// </summary>
            /// <param name="startValue">Inclusive lower bound. May be static, factory-backed, or parameter-bound; execution resolves its value.<br/></param>
            /// <param name="endValue">Inclusive upper bound. May be static, factory-backed, or parameter-bound; execution resolves its value.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition Between(ValueOrExpression<TimeOnly> startValue, ValueOrExpression<TimeOnly> endValue)
            {
                return Execute(() => BetweenOperation.AddToBuilder(builder, selectorExpression, startValue, endValue));
            }

            /// <summary>
            /// Adds the complement of an inclusive lower-and-upper-bound comparison.<br/>
            /// </summary>
            /// <param name="startValue">Inclusive lower bound. May be static, factory-backed, or parameter-bound; execution resolves its value.<br/></param>
            /// <param name="endValue">Inclusive upper bound. May be static, factory-backed, or parameter-bound; execution resolves its value.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition NotBetween(ValueOrExpression<TimeOnly> startValue, ValueOrExpression<TimeOnly> endValue)
            {
                return Execute(() => NotBetweenOperation.AddToBuilder(builder, selectorExpression, startValue, endValue));
            }

            /// <summary>
            /// Adds membership of the selected value in the supplied collection.<br/>
            /// </summary>
            /// <param name="values">Candidate values for membership testing.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsIn(IEnumerable<TimeOnly> values)
            {
                return Execute(() => InOperation.AddToBuilder(builder, selectorExpression, values.Select(v => (ValueOrExpression<TimeOnly>)v)));
            }

            /// <summary>
            /// Adds reusable TimeOnly membership using one execution-frozen deferred set.<br/>
            /// </summary>
            /// <param name="values">Typed TimeOnly set parameter created by the record store.<br/></param>
            /// <returns>The fluent continuation for combining or completing the condition.<br/></returns>
            public ContinueOrEndCondition IsIn(ConditionParameter<IEnumerable<TimeOnly>> values)
            {
                return Execute(() => InOperation.AddToBuilder(builder, selectorExpression, values));
            }

            /// <summary>
            /// Adds non-membership of the selected value in the supplied collection.<br/>
            /// </summary>
            /// <param name="values">Candidate values for membership testing.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsNotIn(IEnumerable<TimeOnly> values)
            {
                return Execute(() => NotInOperation.AddToBuilder(builder, selectorExpression, values.Select(v => (ValueOrExpression<TimeOnly>)v)));
            }

            /// <summary>
            /// Adds reusable negated TimeOnly membership using one execution-frozen deferred set.<br/>
            /// </summary>
            /// <param name="values">Typed TimeOnly set parameter created by the record store.<br/></param>
            /// <returns>The fluent continuation for combining or completing the condition.<br/></returns>
            public ContinueOrEndCondition IsNotIn(ConditionParameter<IEnumerable<TimeOnly>> values)
            {
                return Execute(() => NotInOperation.AddToBuilder(builder, selectorExpression, values));
            }

            // Hour-specific helpers
            /// <summary>
            /// Matches the specified hour-of-day component.<br/>
            /// </summary>
            /// <param name="hour">Hour of the day, from 0 through 23.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition HourEqual(int hour)
            {
                return Execute(() => HourEqualOperation.AddToBuilder(builder, selectorExpression, hour));
            }

            /// <summary>
            /// Matches any of the specified hour-of-day components.<br/>
            /// </summary>
            /// <param name="hours">Hours of the day, from 0 through 23.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition HourIn(params int[] hours)
            {
                return Execute(() => HourInOperation.AddToBuilder(builder, selectorExpression, hours.Select(h => (ValueOrExpression<int>)h)));
            }

            /// <summary>
            /// Excludes the specified hour-of-day components.<br/>
            /// </summary>
            /// <param name="hours">Hours of the day, from 0 through 23.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition HourNotIn(params int[] hours)
            {
                return Execute(() => HourNotInOperation.AddToBuilder(builder, selectorExpression, hours.Select(h => (ValueOrExpression<int>)h)));
            }

            /// <summary>
            /// Matches hour-of-day components within the inclusive specified range.<br/>
            /// </summary>
            /// <param name="startHour">Inclusive lower hour-of-day bound.<br/></param>
            /// <param name="endHour">Inclusive upper hour-of-day bound.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition HourRange(int startHour, int endHour)
            {
                return Execute(() => HourBetweenOperation.AddToBuilder(builder, selectorExpression, startHour, endHour));
            }

            /// <summary>
            /// Matches hour-of-day components outside the inclusive specified range.<br/>
            /// </summary>
            /// <param name="startHour">Inclusive lower hour-of-day bound.<br/></param>
            /// <param name="endHour">Inclusive upper hour-of-day bound.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition HourNotInRange(int startHour, int endHour)
            {
                return Execute(() => HourNotBetweenOperation.AddToBuilder(builder, selectorExpression, startHour, endHour));
            }

            // Semantic time-of-day segments
            /// <summary>
            /// Matches hours 5 through 11, inclusive.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Tests the stored hour component without performing time-zone conversion.<br/>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsMorning()
            {
                return Execute(() => InMorningOperation.AddToBuilder(builder, selectorExpression));
            }

            /// <summary>
            /// Matches hours 12 through 16, inclusive.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Tests the stored hour component without performing time-zone conversion.<br/>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsAfternoon()
            {
                return Execute(() => InAfternoonOperation.AddToBuilder(builder, selectorExpression));
            }

            /// <summary>
            /// Matches hours 17 through 21, inclusive.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Tests the stored hour component without performing time-zone conversion.<br/>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsEvening()
            {
                return Execute(() => InEveningOperation.AddToBuilder(builder, selectorExpression));
            }

            /// <summary>
            /// Matches hours 22 through 23 or 0 through 4.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Tests the stored hour component without performing time-zone conversion.<br/>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsNight()
            {
                return Execute(() => InNightOperation.AddToBuilder(builder, selectorExpression));
            }

            /// <summary>Compares this TimeOnly property for equality with a textual right-hand property completed by <c>.AsTimeOnly</c>.<br/></summary>
            public TimeOnlyPropertyComparisonTypeSelector PropEqualTo(string propPath) => new(builder, selectorExpression, propPath, PropertyComparisonKind.Equal);
            /// <summary>Compares this TimeOnly property for equality with a typed right-hand property.<br/></summary>
            public ContinueOrEndCondition PropEqualTo(Expression<Func<T, TimeOnly>> propSelector) => AddPropertyComparison<TimeOnly>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.Equal);
            /// <summary>Compares this TimeOnly property for inequality with a textual right-hand property completed by <c>.AsTimeOnly</c>.<br/></summary>
            public TimeOnlyPropertyComparisonTypeSelector PropNotEqualTo(string propPath) => new(builder, selectorExpression, propPath, PropertyComparisonKind.NotEqual);
            /// <summary>Compares this TimeOnly property for inequality with a typed right-hand property.<br/></summary>
            public ContinueOrEndCondition PropNotEqualTo(Expression<Func<T, TimeOnly>> propSelector) => AddPropertyComparison<TimeOnly>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.NotEqual);
            /// <summary>Compares whether this TimeOnly property is greater than a textual right-hand property completed by <c>.AsTimeOnly</c>.<br/></summary>
            public TimeOnlyPropertyComparisonTypeSelector PropGreaterThan(string propPath) => new(builder, selectorExpression, propPath, PropertyComparisonKind.Greater);
            /// <summary>Compares whether this TimeOnly property is greater than a typed right-hand property.<br/></summary>
            public ContinueOrEndCondition PropGreaterThan(Expression<Func<T, TimeOnly>> propSelector) => AddPropertyComparison<TimeOnly>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.Greater);
            /// <summary>Compares whether this TimeOnly property is greater than or equal to a textual right-hand property completed by <c>.AsTimeOnly</c>.<br/></summary>
            public TimeOnlyPropertyComparisonTypeSelector PropGreaterOrEqual(string propPath) => new(builder, selectorExpression, propPath, PropertyComparisonKind.GreaterOrEqual);
            /// <summary>Compares whether this TimeOnly property is greater than or equal to a typed right-hand property.<br/></summary>
            public ContinueOrEndCondition PropGreaterOrEqual(Expression<Func<T, TimeOnly>> propSelector) => AddPropertyComparison<TimeOnly>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.GreaterOrEqual);
            /// <summary>Compares whether this TimeOnly property is less than a textual right-hand property completed by <c>.AsTimeOnly</c>.<br/></summary>
            public TimeOnlyPropertyComparisonTypeSelector PropLessThan(string propPath) => new(builder, selectorExpression, propPath, PropertyComparisonKind.Less);
            /// <summary>Compares whether this TimeOnly property is less than a typed right-hand property.<br/></summary>
            public ContinueOrEndCondition PropLessThan(Expression<Func<T, TimeOnly>> propSelector) => AddPropertyComparison<TimeOnly>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.Less);
            /// <summary>Compares whether this TimeOnly property is less than or equal to a textual right-hand property completed by <c>.AsTimeOnly</c>.<br/></summary>
            public TimeOnlyPropertyComparisonTypeSelector PropLessOrEqual(string propPath) => new(builder, selectorExpression, propPath, PropertyComparisonKind.LessOrEqual);
            /// <summary>Compares whether this TimeOnly property is less than or equal to a typed right-hand property.<br/></summary>
            public ContinueOrEndCondition PropLessOrEqual(Expression<Func<T, TimeOnly>> propSelector) => AddPropertyComparison<TimeOnly>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.LessOrEqual);
        }

        #endregion
    }
}
