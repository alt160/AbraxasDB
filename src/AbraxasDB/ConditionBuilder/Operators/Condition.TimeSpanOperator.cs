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
        /// Fluent TimeSpan condition operations over a selected property or calculated value.<br/>
        /// </summary>
        /// <remarks>
        /// Condition authoring is separate from execution; mutable builder instances should not be edited concurrently.<br/>
        /// </remarks>
        public class TimeSpanOperator
        {
            private readonly Builder builder;
            private readonly ValueOrExpression<string> selectorExpression;

            #region Operation Fragments

            internal sealed class GreaterThanOperation : LibraDexTemporalWholeValueOperation<TimeSpan>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly ValueOrExpression<TimeSpan> param;

                public GreaterThanOperation(ValueOrExpression<string> selector, ValueOrExpression<TimeSpan> param)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.GreaterThan, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<TimeSpan> param)
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
                        if (!TryReadConditionValue<TimeSpan>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate > target;
                    };
                    return true;
                }
            }

            internal sealed class GreaterThanEqualOperation : LibraDexTemporalWholeValueOperation<TimeSpan>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly ValueOrExpression<TimeSpan> param;

                public GreaterThanEqualOperation(ValueOrExpression<string> selector, ValueOrExpression<TimeSpan> param)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.GreaterOrEqual, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<TimeSpan> param)
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
                        if (!TryReadConditionValue<TimeSpan>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate >= target;
                    };
                    return true;
                }
            }

            internal sealed class LessThanOperation : LibraDexTemporalWholeValueOperation<TimeSpan>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly ValueOrExpression<TimeSpan> param;

                public LessThanOperation(ValueOrExpression<string> selector, ValueOrExpression<TimeSpan> param)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.LessThan, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<TimeSpan> param)
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
                        if (!TryReadConditionValue<TimeSpan>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate < target;
                    };
                    return true;
                }
            }

            internal sealed class LessThanEqualOperation : LibraDexTemporalWholeValueOperation<TimeSpan>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly ValueOrExpression<TimeSpan> param;

                public LessThanEqualOperation(ValueOrExpression<string> selector, ValueOrExpression<TimeSpan> param)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.LessOrEqual, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<TimeSpan> param)
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
                        if (!TryReadConditionValue<TimeSpan>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate <= target;
                    };
                    return true;
                }
            }

            internal sealed class EqualOperation : LibraDexTemporalWholeValueOperation<TimeSpan>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly ValueOrExpression<TimeSpan> param;

                public EqualOperation(ValueOrExpression<string> selector, ValueOrExpression<TimeSpan> param)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.EqualTo, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<TimeSpan> param)
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
                        if (!TryReadConditionValue<TimeSpan>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate == target;
                    };
                    return true;
                }
            }

            internal sealed class NotEqualOperation : LibraDexTemporalWholeValueOperation<TimeSpan>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly ValueOrExpression<TimeSpan> param;

                public NotEqualOperation(ValueOrExpression<string> selector, ValueOrExpression<TimeSpan> param)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.NotEqualTo, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<TimeSpan> param)
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
                        if (!TryReadConditionValue<TimeSpan>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate != target;
                    };
                    return true;
                }
            }

            internal sealed class BetweenOperation : LibraDexTemporalWholeValueOperation<TimeSpan>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly ValueOrExpression<TimeSpan> startParam;
                private readonly ValueOrExpression<TimeSpan> endParam;

                public BetweenOperation(ValueOrExpression<string> selector, ValueOrExpression<TimeSpan> startParam, ValueOrExpression<TimeSpan> endParam)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.Between, startParam, endParam)
                {
                    this.selector = selector;
                    this.startParam = startParam;
                    this.endParam = endParam;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<TimeSpan> startParam, ValueOrExpression<TimeSpan> endParam)
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
                        if (!TryReadConditionValue<TimeSpan>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate >= start && candidate <= end;
                    };
                    return true;
                }
            }

            internal sealed class NotBetweenOperation : LibraDexTemporalWholeValueOperation<TimeSpan>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly ValueOrExpression<TimeSpan> startParam;
                private readonly ValueOrExpression<TimeSpan> endParam;

                public NotBetweenOperation(ValueOrExpression<string> selector, ValueOrExpression<TimeSpan> startParam, ValueOrExpression<TimeSpan> endParam)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.NotBetween, startParam, endParam)
                {
                    this.selector = selector;
                    this.startParam = startParam;
                    this.endParam = endParam;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<TimeSpan> startParam, ValueOrExpression<TimeSpan> endParam)
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
                        if (!TryReadConditionValue<TimeSpan>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate < start || candidate > end;
                    };
                    return true;
                }
            }

            internal sealed class InOperation : LibraDexTemporalWholeValueOperation<TimeSpan>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly IEnumerable<ValueOrExpression<TimeSpan>> parameters;
                private readonly ValueOrExpression<IEnumerable<TimeSpan>>? setParameter;

                public InOperation(ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<TimeSpan>> parameters)
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
                public InOperation(ValueOrExpression<string> selector, ValueOrExpression<IEnumerable<TimeSpan>> setParameter)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.In, setParameter)
                {
                    this.selector = selector;
                    parameters = Array.Empty<ValueOrExpression<TimeSpan>>();
                    this.setParameter = setParameter;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<TimeSpan>> parameters)
                {
                    builder.AddFragment(new InOperation(selector, parameters));
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ConditionParameter<IEnumerable<TimeSpan>> setParameter)
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

            internal sealed class NotInOperation : LibraDexTemporalWholeValueOperation<TimeSpan>,IFastPredicateSource
            {
                private readonly ValueOrExpression<string> selector;
                private readonly IEnumerable<ValueOrExpression<TimeSpan>> parameters;
                private readonly ValueOrExpression<IEnumerable<TimeSpan>>? setParameter;

                public NotInOperation(ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<TimeSpan>> parameters)
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
                public NotInOperation(ValueOrExpression<string> selector, ValueOrExpression<IEnumerable<TimeSpan>> setParameter)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.NotIn, setParameter)
                {
                    this.selector = selector;
                    parameters = Array.Empty<ValueOrExpression<TimeSpan>>();
                    this.setParameter = setParameter;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<TimeSpan>> parameters)
                {
                    builder.AddFragment(new NotInOperation(selector, parameters));
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ConditionParameter<IEnumerable<TimeSpan>> setParameter)
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


            #endregion

            /// <summary>
            /// Creates the TimeSpanOperator stage over the supplied condition builder and selector.<br/>
            /// </summary>
            /// <param name="builder">Mutable builder that receives authored condition fragments.<br/></param>
            /// <param name="selectorExpression">Selected property path or calculated selector intent.<br/></param>
            /// <remarks>
            /// Construction retains the builder and selector; subsequent fluent operations append the actual predicate.<br/>
            /// </remarks>
            public TimeSpanOperator(Builder builder, ValueOrExpression<string> selectorExpression )
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
            public ContinueOrEndCondition GreaterThan(ValueOrExpression<TimeSpan> value)
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
            public ContinueOrEndCondition LessThan(ValueOrExpression<TimeSpan> value)
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
            public ContinueOrEndCondition GreaterOrEqual(ValueOrExpression<TimeSpan> value)
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
            public ContinueOrEndCondition LessOrEqual(ValueOrExpression<TimeSpan> value)
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
            public ContinueOrEndCondition EqualTo(ValueOrExpression<TimeSpan> value)
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
            public ContinueOrEndCondition NotEqualTo(ValueOrExpression<TimeSpan> value)
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
            public ContinueOrEndCondition Between(ValueOrExpression<TimeSpan> startValue, ValueOrExpression<TimeSpan> endValue)
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
            public ContinueOrEndCondition NotBetween(ValueOrExpression<TimeSpan> startValue, ValueOrExpression<TimeSpan> endValue)
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
            public ContinueOrEndCondition IsIn(IEnumerable<TimeSpan> values)
            {
                return Execute(() => InOperation.AddToBuilder(builder, selectorExpression, values.Select(v => (ValueOrExpression<TimeSpan>)v)));
            }

            /// <summary>
            /// Adds reusable TimeSpan membership using one execution-frozen deferred set.<br/>
            /// </summary>
            /// <param name="values">Typed TimeSpan set parameter created by the record store.<br/></param>
            /// <returns>The fluent continuation for combining or completing the condition.<br/></returns>
            public ContinueOrEndCondition IsIn(ConditionParameter<IEnumerable<TimeSpan>> values)
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
            public ContinueOrEndCondition IsNotIn(IEnumerable<TimeSpan> values)
            {
                return Execute(() => NotInOperation.AddToBuilder(builder, selectorExpression, values.Select(v => (ValueOrExpression<TimeSpan>)v)));
            }

            /// <summary>
            /// Adds reusable negated TimeSpan membership using one execution-frozen deferred set.<br/>
            /// </summary>
            /// <param name="values">Typed TimeSpan set parameter created by the record store.<br/></param>
            /// <returns>The fluent continuation for combining or completing the condition.<br/></returns>
            public ContinueOrEndCondition IsNotIn(ConditionParameter<IEnumerable<TimeSpan>> values)
            {
                return Execute(() => NotInOperation.AddToBuilder(builder, selectorExpression, values));
            }

            /// <summary>
            /// Direct and deterministic binary-slice sources receive immutable backend-neutral metadata for Fractal evaluation and durable replay.<br/>
            /// </summary>
            /// <param name="projection">Total-unit projection being requested.<br/></param>
            /// <returns>A numeric operator over the projected double value.<br/></returns>
            private NumericOperator<double> CreateTotalProjection(TimeSpanProjectionKind projection)
            {
                var projected = (ValueOrExpression<string>)string.Empty;
                if (selectorExpression.SelectorDescriptor is { } source)
                {
                    projected.WithSelectorDescriptor(
                        new Selector<T>.TimeSpanProjectionSelectorDescriptor(
                            source,
                            projection));
                }

                return new NumericOperator<double>(builder, projected);
            }

            /// <summary>
            /// Selects the complete TimeSpan duration in fractional hours for numeric comparison.<br/>
            /// </summary>
            /// <returns>A Double condition-operator stage over the projected duration.<br/></returns>
            public NumericOperator<double> TotalHours
                => CreateTotalProjection(TimeSpanProjectionKind.TotalHours);
            /// <summary>
            /// Selects the complete TimeSpan duration in fractional minutes for numeric comparison.<br/>
            /// </summary>
            /// <returns>A Double condition-operator stage over the projected duration.<br/></returns>
            public NumericOperator<double> TotalMinutes
                => CreateTotalProjection(TimeSpanProjectionKind.TotalMinutes);
            /// <summary>
            /// Selects the complete TimeSpan duration in fractional seconds for numeric comparison.<br/>
            /// </summary>
            /// <returns>A Double condition-operator stage over the projected duration.<br/></returns>
            public NumericOperator<double> TotalSeconds
                => CreateTotalProjection(TimeSpanProjectionKind.TotalSeconds);
            /// <summary>
            /// Selects the complete TimeSpan duration in fractional milliseconds for numeric comparison.<br/>
            /// </summary>
            /// <returns>A Double condition-operator stage over the projected duration.<br/></returns>
            public NumericOperator<double> TotalMilliseconds
                => CreateTotalProjection(TimeSpanProjectionKind.TotalMilliseconds);

            /// <summary>Compares this TimeSpan property for equality with a textual right-hand property completed by <c>.AsTimeSpan</c>.<br/></summary>
            public TimeSpanPropertyComparisonTypeSelector PropEqualTo(string propPath) => new(builder, selectorExpression, propPath, PropertyComparisonKind.Equal);
            /// <summary>Compares this TimeSpan property for equality with a typed right-hand property.<br/></summary>
            public ContinueOrEndCondition PropEqualTo(Expression<Func<T, TimeSpan>> propSelector) => AddPropertyComparison<TimeSpan>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.Equal);
            /// <summary>Compares this TimeSpan property for inequality with a textual right-hand property completed by <c>.AsTimeSpan</c>.<br/></summary>
            public TimeSpanPropertyComparisonTypeSelector PropNotEqualTo(string propPath) => new(builder, selectorExpression, propPath, PropertyComparisonKind.NotEqual);
            /// <summary>Compares this TimeSpan property for inequality with a typed right-hand property.<br/></summary>
            public ContinueOrEndCondition PropNotEqualTo(Expression<Func<T, TimeSpan>> propSelector) => AddPropertyComparison<TimeSpan>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.NotEqual);
            /// <summary>Compares whether this TimeSpan property is greater than a textual right-hand property completed by <c>.AsTimeSpan</c>.<br/></summary>
            public TimeSpanPropertyComparisonTypeSelector PropGreaterThan(string propPath) => new(builder, selectorExpression, propPath, PropertyComparisonKind.Greater);
            /// <summary>Compares whether this TimeSpan property is greater than a typed right-hand property.<br/></summary>
            public ContinueOrEndCondition PropGreaterThan(Expression<Func<T, TimeSpan>> propSelector) => AddPropertyComparison<TimeSpan>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.Greater);
            /// <summary>Compares whether this TimeSpan property is greater than or equal to a textual right-hand property completed by <c>.AsTimeSpan</c>.<br/></summary>
            public TimeSpanPropertyComparisonTypeSelector PropGreaterOrEqual(string propPath) => new(builder, selectorExpression, propPath, PropertyComparisonKind.GreaterOrEqual);
            /// <summary>Compares whether this TimeSpan property is greater than or equal to a typed right-hand property.<br/></summary>
            public ContinueOrEndCondition PropGreaterOrEqual(Expression<Func<T, TimeSpan>> propSelector) => AddPropertyComparison<TimeSpan>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.GreaterOrEqual);
            /// <summary>Compares whether this TimeSpan property is less than a textual right-hand property completed by <c>.AsTimeSpan</c>.<br/></summary>
            public TimeSpanPropertyComparisonTypeSelector PropLessThan(string propPath) => new(builder, selectorExpression, propPath, PropertyComparisonKind.Less);
            /// <summary>Compares whether this TimeSpan property is less than a typed right-hand property.<br/></summary>
            public ContinueOrEndCondition PropLessThan(Expression<Func<T, TimeSpan>> propSelector) => AddPropertyComparison<TimeSpan>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.Less);
            /// <summary>Compares whether this TimeSpan property is less than or equal to a textual right-hand property completed by <c>.AsTimeSpan</c>.<br/></summary>
            public TimeSpanPropertyComparisonTypeSelector PropLessOrEqual(string propPath) => new(builder, selectorExpression, propPath, PropertyComparisonKind.LessOrEqual);
            /// <summary>Compares whether this TimeSpan property is less than or equal to a typed right-hand property.<br/></summary>
            public ContinueOrEndCondition PropLessOrEqual(Expression<Func<T, TimeSpan>> propSelector) => AddPropertyComparison<TimeSpan>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.LessOrEqual);
        }

        #endregion
    }
}
