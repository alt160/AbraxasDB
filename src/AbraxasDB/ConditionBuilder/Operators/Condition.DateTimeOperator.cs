using System;
using System.Collections.Generic;
using System.Linq;
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
        /// Fluent DateTime condition operations over a selected property or calculated value.<br/>
        /// </summary>
        /// <remarks>
        /// Condition authoring is separate from execution; mutable builder instances should not be edited concurrently.<br/>
        /// </remarks>
        public class DateTimeOperator
        {
            private readonly Builder builder;
            private readonly ValueOrExpression<string> selectorExpression;

            #region Operation Fragments
            internal class GreaterThanOperation : LibraDexTemporalWholeValueOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<DateTime> param;

                public GreaterThanOperation(ValueOrExpression<string> selector, ValueOrExpression<DateTime> param)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.GreaterThan, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<DateTime> param)
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
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate > target;
                    };
                    return true;
                }
            }
            internal class GreaterThanEqualOperation : LibraDexTemporalWholeValueOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<DateTime> param;

                public GreaterThanEqualOperation(ValueOrExpression<string> selector, ValueOrExpression<DateTime> param)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.GreaterOrEqual, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<DateTime> param)
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
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate >= target;
                    };
                    return true;
                }
            }
            internal class LessThanOperation : LibraDexTemporalWholeValueOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<DateTime> param;

                public LessThanOperation(ValueOrExpression<string> selector, ValueOrExpression<DateTime> param)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.LessThan, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<DateTime> param)
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
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate < target;
                    };
                    return true;
                }
            }
            internal class LessThanEqualOperation : LibraDexTemporalWholeValueOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<DateTime> param;

                public LessThanEqualOperation(ValueOrExpression<string> selector, ValueOrExpression<DateTime> param)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.LessOrEqual, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<DateTime> param)
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
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate <= target;
                    };
                    return true;
                }
            }
            internal class EqualOperation : LibraDexTemporalWholeValueOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<DateTime> param;

                public EqualOperation(ValueOrExpression<string> selector, ValueOrExpression<DateTime> param)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.EqualTo, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<DateTime> param)
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
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate == target;
                    };
                    return true;
                }
            }
            internal class NotEqualOperation : LibraDexTemporalWholeValueOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<DateTime> param;

                public NotEqualOperation(ValueOrExpression<string> selector, ValueOrExpression<DateTime> param)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.NotEqualTo, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<DateTime> param)
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
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate != target;
                    };
                    return true;
                }
            }
            internal class BetweenOperation : LibraDexTemporalWholeValueOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<DateTime> startParam;
                private ValueOrExpression<DateTime> endParam;

                public BetweenOperation(ValueOrExpression<string> selector, ValueOrExpression<DateTime> startParam, ValueOrExpression<DateTime> endParam)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.Between, startParam, endParam)
                {
                    this.selector = selector;
                    this.startParam = startParam;
                    this.endParam = endParam;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<DateTime> startParam, ValueOrExpression<DateTime> endParam)
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
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate >= start && candidate <= end;
                    };
                    return true;
                }
            }
            internal class NotBetweenOperation : LibraDexTemporalWholeValueOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<DateTime> startParam;
                private ValueOrExpression<DateTime> endParam;

                public NotBetweenOperation(ValueOrExpression<string> selector, ValueOrExpression<DateTime> startParam, ValueOrExpression<DateTime> endParam)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.NotBetween, startParam, endParam)
                {
                    this.selector = selector;
                    this.startParam = startParam;
                    this.endParam = endParam;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<DateTime> startParam, ValueOrExpression<DateTime> endParam)
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
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate < start || candidate > end;
                    };
                    return true;
                }
            }
            internal class InOperation : LibraDexTemporalWholeValueOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private IEnumerable<ValueOrExpression<DateTime>> parameters;
                private ValueOrExpression<IEnumerable<DateTime>>? setParameter;


                public InOperation(ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<DateTime>> parameters)
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
                public InOperation(ValueOrExpression<string> selector, ValueOrExpression<IEnumerable<DateTime>> setParameter)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.In, setParameter)
                {
                    this.selector = selector;
                    this.parameters = Array.Empty<ValueOrExpression<DateTime>>();
                    this.setParameter = setParameter;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<DateTime>> parameters)
                {
                    builder.AddFragment(new InOperation(selector, parameters));
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ConditionParameter<IEnumerable<DateTime>> setParameter)
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
            internal class NotInOperation : LibraDexTemporalWholeValueOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private IEnumerable<ValueOrExpression<DateTime>> parameters;
                private ValueOrExpression<IEnumerable<DateTime>>? setParameter;


                public NotInOperation(ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<DateTime>> parameters)
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
                public NotInOperation(ValueOrExpression<string> selector, ValueOrExpression<IEnumerable<DateTime>> setParameter)
                    : base(selector, LibraDexTemporalWholeValueOperationKind.NotIn, setParameter)
                {
                    this.selector = selector;
                    this.parameters = Array.Empty<ValueOrExpression<DateTime>>();
                    this.setParameter = setParameter;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<DateTime>> parameters)
                {
                    builder.AddFragment(new NotInOperation(selector, parameters));
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ConditionParameter<IEnumerable<DateTime>> setParameter)
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
            internal class YearEqualOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<int> param;

                public YearEqualOperation(ValueOrExpression<string> selector, ValueOrExpression<int> param)
                    : base(selector, LibraDexTemporalStructuredOperationKind.YearEqual, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<int> param)
                {
                    builder.AddFragment(new YearEqualOperation(selector, param));
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;

                    var expected = builder.MaterializeParameter(param, propPath);
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate.Year == expected;
                    };
                    return true;
                }
            }
            internal class YearNotEqualOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<int> param;

                public YearNotEqualOperation(ValueOrExpression<string> selector, ValueOrExpression<int> param)
                    : base(selector, LibraDexTemporalStructuredOperationKind.YearNotEqual, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;

                    var expected = builder.MaterializeParameter(param, propPath);
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate.Year != expected;
                    };
                    return true;
                }
            }
            internal class YearInOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private IEnumerable<ValueOrExpression<int>> parameters;


                public YearInOperation(ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<int>> parameters)
                    : base(selector, LibraDexTemporalStructuredOperationKind.YearIn, parameters)
                {
                    this.selector = selector;
                    this.parameters = parameters;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<int>> parameters)
                {
                    builder.AddFragment(new YearInOperation(selector, parameters));
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;

                    var values = parameters?.Select(p => builder.MaterializeParameter(p, propPath)).ToHashSet() ?? new HashSet<int>();
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        return values.Contains(candidate.Year);
                    };
                    return true;
                }
            }
            internal class YearNotInOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private IEnumerable<ValueOrExpression<int>> parameters;


                public YearNotInOperation(ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<int>> parameters)
                    : base(selector, LibraDexTemporalStructuredOperationKind.YearNotIn, parameters)
                {
                    this.selector = selector;
                    this.parameters = parameters;
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;

                    var values = parameters?.Select(p => builder.MaterializeParameter(p, propPath)).ToHashSet() ?? new HashSet<int>();
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        return !values.Contains(candidate.Year);
                    };
                    return true;
                }
            }
            internal class YearBetweenOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<int> startParam;
                private ValueOrExpression<int> endParam;

                public YearBetweenOperation(ValueOrExpression<string> selector, ValueOrExpression<int> startParam, ValueOrExpression<int> endParam)
                    : base(selector, LibraDexTemporalStructuredOperationKind.YearBetween, startParam, endParam)
                {
                    this.selector = selector;
                    this.startParam = startParam;
                    this.endParam = endParam;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<int> startParam, ValueOrExpression<int> endParam)
                {
                    builder.AddFragment(new YearBetweenOperation(selector, startParam, endParam));
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;

                    var start = builder.MaterializeParameter(startParam, propPath);
                    var end = builder.MaterializeParameter(endParam, propPath);
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        var year = candidate.Year;
                        return year >= start && year <= end;
                    };
                    return true;
                }
            }
            internal class YearNotBetweenOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<int> startParam;
                private ValueOrExpression<int> endParam;

                public YearNotBetweenOperation(ValueOrExpression<string> selector, ValueOrExpression<int> startParam, ValueOrExpression<int> endParam)
                    : base(selector, LibraDexTemporalStructuredOperationKind.YearNotBetween, startParam, endParam)
                {
                    this.selector = selector;
                    this.startParam = startParam;
                    this.endParam = endParam;
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;

                    var start = builder.MaterializeParameter(startParam, propPath);
                    var end = builder.MaterializeParameter(endParam, propPath);
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        var year = candidate.Year;
                        return year < start || year > end;
                    };
                    return true;
                }
            }
            internal class YearOnOrAfterOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<int> param;

                public YearOnOrAfterOperation(ValueOrExpression<string> selector, ValueOrExpression<int> param)
                    : base(selector, LibraDexTemporalStructuredOperationKind.YearOnOrAfter, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<int> param)
                {
                    builder.AddFragment(new YearOnOrAfterOperation(selector, param));
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;

                    var threshold = builder.MaterializeParameter(param, propPath);
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate.Year >= threshold;
                    };
                    return true;
                }
            }
            internal class YearOnOrBeforeOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<int> param;

                public YearOnOrBeforeOperation(ValueOrExpression<string> selector, ValueOrExpression<int> param)
                    : base(selector, LibraDexTemporalStructuredOperationKind.YearOnOrBefore, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<int> param)
                {
                    builder.AddFragment(new YearOnOrBeforeOperation(selector, param));
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;

                    var threshold = builder.MaterializeParameter(param, propPath);
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate.Year <= threshold;
                    };
                    return true;
                }
            }
            internal class MonthEqualOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<int> param;

                public MonthEqualOperation(ValueOrExpression<string> selector, ValueOrExpression<int> param)
                    : base(selector, LibraDexTemporalStructuredOperationKind.MonthEqual, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<int> param)
                {
                    builder.AddFragment(new MonthEqualOperation(selector, param));
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;

                    var expected = builder.MaterializeParameter(param, propPath);
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate.Month == expected;
                    };
                    return true;
                }
            }
            internal class MonthInOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private IEnumerable<ValueOrExpression<int>> parameters;


                public MonthInOperation(ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<int>> parameters)
                    : base(selector, LibraDexTemporalStructuredOperationKind.MonthIn, parameters)
                {
                    this.selector = selector;
                    this.parameters = parameters;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<int>> parameters)
                {
                    builder.AddFragment(new MonthInOperation(selector, parameters));
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;

                    var values = parameters?.Select(p => builder.MaterializeParameter(p, propPath)).ToHashSet() ?? new HashSet<int>();
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        return values.Contains(candidate.Month);
                    };
                    return true;
                }
            }
            internal class MonthNotInOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private IEnumerable<ValueOrExpression<int>> parameters;


                public MonthNotInOperation(ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<int>> parameters)
                    : base(selector, LibraDexTemporalStructuredOperationKind.MonthNotIn, parameters)
                {
                    this.selector = selector;
                    this.parameters = parameters;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<int>> parameters)
                {
                    builder.AddFragment(new MonthNotInOperation(selector, parameters));
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;

                    var values = parameters?.Select(p => builder.MaterializeParameter(p, propPath)).ToHashSet() ?? new HashSet<int>();
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        return !values.Contains(candidate.Month);
                    };
                    return true;
                }
            }
            internal class MonthNotEqualOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<int> param;

                public MonthNotEqualOperation(ValueOrExpression<string> selector, ValueOrExpression<int> param)
                    : base(selector, LibraDexTemporalStructuredOperationKind.MonthNotEqual, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;

                    var expected = builder.MaterializeParameter(param, propPath);
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate.Month != expected;
                    };
                    return true;
                }
            }
            internal class MonthBetweenOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<int> startParam;
                private ValueOrExpression<int> endParam;

                public MonthBetweenOperation(ValueOrExpression<string> selector, ValueOrExpression<int> startParam, ValueOrExpression<int> endParam)
                    : base(selector, LibraDexTemporalStructuredOperationKind.MonthBetween, startParam, endParam)
                {
                    this.selector = selector;
                    this.startParam = startParam;
                    this.endParam = endParam;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<int> startParam, ValueOrExpression<int> endParam)
                {
                    builder.AddFragment(new MonthBetweenOperation(selector, startParam, endParam));
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;

                    var start = builder.MaterializeParameter(startParam, propPath);
                    var end = builder.MaterializeParameter(endParam, propPath);
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        var month = candidate.Month;
                        return month >= start && month <= end;
                    };
                    return true;
                }
            }
            internal class MonthNotBetweenOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<int> startParam;
                private ValueOrExpression<int> endParam;

                public MonthNotBetweenOperation(ValueOrExpression<string> selector, ValueOrExpression<int> startParam, ValueOrExpression<int> endParam)
                    : base(selector, LibraDexTemporalStructuredOperationKind.MonthNotBetween, startParam, endParam)
                {
                    this.selector = selector;
                    this.startParam = startParam;
                    this.endParam = endParam;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<int> startParam, ValueOrExpression<int> endParam)
                {
                    builder.AddFragment(new MonthNotBetweenOperation(selector, startParam, endParam));
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;

                    var start = builder.MaterializeParameter(startParam, propPath);
                    var end = builder.MaterializeParameter(endParam, propPath);
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        var month = candidate.Month;
                        return month < start || month > end;
                    };
                    return true;
                }
            }
            internal class DayEqualOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<int> param;

                public DayEqualOperation(ValueOrExpression<string> selector, ValueOrExpression<int> param)
                    : base(selector, LibraDexTemporalStructuredOperationKind.DayEqual, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<int> param)
                {
                    builder.AddFragment(new DayEqualOperation(selector, param));
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                    var expected = builder.MaterializeParameter(param, propPath);
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate.Day == expected;
                    };
                    return true;
                }
            }
            internal class DayNotEqualOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<int> param;

                public DayNotEqualOperation(ValueOrExpression<string> selector, ValueOrExpression<int> param)
                    : base(selector, LibraDexTemporalStructuredOperationKind.DayNotEqual, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                    var expected = builder.MaterializeParameter(param, propPath);
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        return candidate.Day != expected;
                    };
                    return true;
                }
            }
            internal class DayInOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private IEnumerable<ValueOrExpression<int>> parameters;


                public DayInOperation(ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<int>> parameters)
                    : base(selector, LibraDexTemporalStructuredOperationKind.DayIn, parameters)
                {
                    this.selector = selector;
                    this.parameters = parameters;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<int>> parameters)
                {
                    builder.AddFragment(new DayInOperation(selector, parameters));
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;

                    var values = parameters?.Select(p => builder.MaterializeParameter(p, propPath)).ToHashSet() ?? new HashSet<int>();
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        return values.Contains(candidate.Day);
                    };
                    return true;
                }
            }
            internal class DayNotInOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private IEnumerable<ValueOrExpression<int>> parameters;


                public DayNotInOperation(ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<int>> parameters)
                    : base(selector, LibraDexTemporalStructuredOperationKind.DayNotIn, parameters)
                {
                    this.selector = selector;
                    this.parameters = parameters;
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;

                    var values = parameters?.Select(p => builder.MaterializeParameter(p, propPath)).ToHashSet() ?? new HashSet<int>();
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        return !values.Contains(candidate.Day);
                    };
                    return true;
                }
            }
            internal class DayBetweenOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<int> startParam;
                private ValueOrExpression<int> endParam;

                public DayBetweenOperation(ValueOrExpression<string> selector, ValueOrExpression<int> startParam, ValueOrExpression<int> endParam)
                    : base(selector, LibraDexTemporalStructuredOperationKind.DayBetween, startParam, endParam)
                {
                    this.selector = selector;
                    this.startParam = startParam;
                    this.endParam = endParam;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<int> startParam, ValueOrExpression<int> endParam)
                {
                    builder.AddFragment(new DayBetweenOperation(selector, startParam, endParam));
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;

                    var start = builder.MaterializeParameter(startParam, propPath);
                    var end = builder.MaterializeParameter(endParam, propPath);
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        var day = candidate.Day;
                        return day >= start && day <= end;
                    };
                    return true;
                }
            }
            internal class DayNotBetweenOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<int> startParam;
                private ValueOrExpression<int> endParam;

                public DayNotBetweenOperation(ValueOrExpression<string> selector, ValueOrExpression<int> startParam, ValueOrExpression<int> endParam)
                    : base(selector, LibraDexTemporalStructuredOperationKind.DayNotBetween, startParam, endParam)
                {
                    this.selector = selector;
                    this.startParam = startParam;
                    this.endParam = endParam;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<int> startParam, ValueOrExpression<int> endParam)
                {
                    builder.AddFragment(new DayNotBetweenOperation(selector, startParam, endParam));
                }

                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;

                    var start = builder.MaterializeParameter(startParam, propPath);
                    var end = builder.MaterializeParameter(endParam, propPath);
                    evaluator = ib =>
                    {
                        if (!TryReadConditionValue<DateTime>(selector, propPath, ib, options, out var candidate)) return false;
                        var day = candidate.Day;
                        return day < start || day > end;
                    };
                    return true;
                }
            }
            internal class YearMonthOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<int> year;
                private ValueOrExpression<int> month;

                public YearMonthOperation(ValueOrExpression<string> selector, ValueOrExpression<int> year, ValueOrExpression<int> month)
                    : base(selector, LibraDexTemporalStructuredOperationKind.YearMonth, year, month)
                {
                    this.selector = selector;
                    this.year = year;
                    this.month = month;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<int> year, ValueOrExpression<int> month)
                {
                    builder.AddFragment(new YearMonthOperation(selector, year, month));
                }
                /// <summary>
                /// Builds the backend-neutral DateTime evaluator for the YearMonth condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                var targetYear = builder.MaterializeParameter(year, propPath, owner: this);
                var targetMonth = builder.MaterializeParameter(month, propPath, owner: this);
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate => candidate.Year == targetYear && candidate.Month == targetMonth, out evaluator);
                }
            }
            internal class YearMonthInOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private IEnumerable<ValueOrExpression<(int year, int month)>> parameters;


                public YearMonthInOperation(ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<(int year, int month)>> parameters)
                    : base(selector, LibraDexTemporalStructuredOperationKind.YearMonthIn, parameters)
                {
                    this.selector = selector;
                    this.parameters = parameters;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<(int year, int month)>> parameters)
                {
                    builder.AddFragment(new YearMonthInOperation(selector, parameters));
                }
                /// <summary>
                /// Builds the backend-neutral DateTime evaluator for the YearMonthIn condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                var accepted = parameters.Select(value => builder.MaterializeParameter(value, propPath, owner: this)).ToHashSet();
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate => accepted.Contains((candidate.Year, candidate.Month)), out evaluator);
                }
            }
            internal class YearMonthDayOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<int> year;
                private ValueOrExpression<int> month;
                private ValueOrExpression<int> day;

                public YearMonthDayOperation(ValueOrExpression<string> selector, ValueOrExpression<int> year, ValueOrExpression<int> month, ValueOrExpression<int> day)
                    : base(selector, LibraDexTemporalStructuredOperationKind.YearMonthDay, year, month, day)
                {
                    this.selector = selector;
                    this.year = year;
                    this.month = month;
                    this.day = day;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<int> year, ValueOrExpression<int> month, ValueOrExpression<int> day)
                {
                    builder.AddFragment(new YearMonthDayOperation(selector, year, month, day));
                }
                /// <summary>
                /// Builds the backend-neutral DateTime evaluator for the YearMonthDay condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                var targetYear = builder.MaterializeParameter(year, propPath, owner: this);
                var targetMonth = builder.MaterializeParameter(month, propPath, owner: this);
                var targetDay = builder.MaterializeParameter(day, propPath, owner: this);
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate => candidate.Year == targetYear && candidate.Month == targetMonth && candidate.Day == targetDay, out evaluator);
                }
            }
            internal class YearMonthDayInOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private IEnumerable<ValueOrExpression<(int year, int month, int day)>> parameters;


                public YearMonthDayInOperation(ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<(int year, int month, int day)>> parameters)
                    : base(selector, LibraDexTemporalStructuredOperationKind.YearMonthDayIn, parameters)
                {
                    this.selector = selector;
                    this.parameters = parameters;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, IEnumerable<ValueOrExpression<(int year, int month, int day)>> parameters)
                {
                    builder.AddFragment(new YearMonthDayInOperation(selector, parameters));
                }
                /// <summary>
                /// Builds the backend-neutral DateTime evaluator for the YearMonthDayIn condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                var accepted = parameters.Select(value => builder.MaterializeParameter(value, propPath, owner: this)).ToHashSet();
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate => accepted.Contains((candidate.Year, candidate.Month, candidate.Day)), out evaluator);
                }
            }
            internal class YearMonthsOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<int> year;
                private IEnumerable<ValueOrExpression<int>> month;

                public YearMonthsOperation(ValueOrExpression<string> selector, ValueOrExpression<int> year, IEnumerable<ValueOrExpression<int>> month)
                    : base(selector, LibraDexTemporalStructuredOperationKind.YearMonths, year, month)
                {
                    this.selector = selector;
                    this.year = year;
                    this.month = month;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<int> year, IEnumerable<ValueOrExpression<int>> month)
                {
                    builder.AddFragment(new YearMonthsOperation(selector, year, month));
                }
                /// <summary>
                /// Builds the backend-neutral DateTime evaluator for the YearMonths condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                var targetYear = builder.MaterializeParameter(year, propPath, owner: this);
                var acceptedMonths = month.Select(value => builder.MaterializeParameter(value, propPath, owner: this)).ToHashSet();
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate => candidate.Year == targetYear && acceptedMonths.Contains(candidate.Month), out evaluator);
                }
            }
            internal class MonthDayOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<int> month;
                private ValueOrExpression<int> day;

                public MonthDayOperation(ValueOrExpression<string> selector, ValueOrExpression<int> month, ValueOrExpression<int> day)
                    : base(selector, LibraDexTemporalStructuredOperationKind.MonthDay, month, day)
                {
                    this.selector = selector;
                    this.month = month;
                    this.day = day;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<int> month, ValueOrExpression<int> day)
                {
                    builder.AddFragment(new MonthDayOperation(selector, month, day));
                }
                /// <summary>
                /// Builds the backend-neutral DateTime evaluator for the MonthDay condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                var targetMonth = builder.MaterializeParameter(month, propPath, owner: this);
                var targetDay = builder.MaterializeParameter(day, propPath, owner: this);
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate => candidate.Month == targetMonth && candidate.Day == targetDay, out evaluator);
                }
            }
            internal class InQuarterOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<int> param;

                public InQuarterOperation(ValueOrExpression<string> selector, ValueOrExpression<int> param)
                    : base(selector, LibraDexTemporalStructuredOperationKind.InQuarter, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<int> param)
                {
                    builder.AddFragment(new InQuarterOperation(selector, param));
                }
                /// <summary>
                /// Builds the backend-neutral DateTime evaluator for the InQuarter condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                var quarter = builder.MaterializeParameter(param, propPath, owner: this);
                var startMonth = ((quarter - 1) * 3) + 1;
                var endMonth = startMonth + 2;
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate => candidate.Month >= startMonth && candidate.Month <= endMonth, out evaluator);
                }
            }
            internal class InQuarterRangeOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<int> q1;
                private ValueOrExpression<int> q2;

                public InQuarterRangeOperation(ValueOrExpression<string> selector, ValueOrExpression<int> q1, ValueOrExpression<int> q2)
                    : base(selector, LibraDexTemporalStructuredOperationKind.InQuarterRange, q1, q2)
                {
                    this.selector = selector;
                    this.q1 = q1;
                    this.q2 = q2;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<int> q1, ValueOrExpression<int> q2)
                {
                    builder.AddFragment(new InQuarterRangeOperation(selector, q1, q2));
                }
                /// <summary>
                /// Builds the backend-neutral DateTime evaluator for the InQuarterRange condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                var firstQuarter = builder.MaterializeParameter(q1, propPath, owner: this);
                var lastQuarter = builder.MaterializeParameter(q2, propPath, owner: this);
                var startMonth = ((firstQuarter - 1) * 3) + 1;
                var endMonth = lastQuarter * 3;
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate => candidate.Month >= startMonth && candidate.Month <= endMonth, out evaluator);
                }
            }
            internal class YearQuarterOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<int> year;
                private ValueOrExpression<int> q1;

                public YearQuarterOperation(ValueOrExpression<string> selector, ValueOrExpression<int> year, ValueOrExpression<int> q1)
                    : base(selector, LibraDexTemporalStructuredOperationKind.YearQuarter, year, q1)
                {
                    this.selector = selector;
                    this.year = year;
                    this.q1 = q1;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<int> year, ValueOrExpression<int> q1)
                {
                    builder.AddFragment(new YearQuarterOperation(selector, year, q1));
                }
                /// <summary>
                /// Builds the backend-neutral DateTime evaluator for the YearQuarter condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                var targetYear = builder.MaterializeParameter(year, propPath, owner: this);
                var quarter = builder.MaterializeParameter(q1, propPath, owner: this);
                var startMonth = ((quarter - 1) * 3) + 1;
                var endMonth = startMonth + 2;
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate => candidate.Year == targetYear && candidate.Month >= startMonth && candidate.Month <= endMonth, out evaluator);
                }
            }
            internal class AtQuarterStartOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;

                public AtQuarterStartOperation(ValueOrExpression<string> selector)
                    : base(selector, LibraDexTemporalStructuredOperationKind.AtQuarterStart)
                {
                    this.selector = selector;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector)
                {
                    builder.AddFragment(new AtQuarterStartOperation(selector));
                }
                /// <summary>
                /// Builds the backend-neutral DateTime evaluator for the AtQuarterStart condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate => candidate.Day == 1 && candidate.Month is 1 or 4 or 7 or 10, out evaluator);
                }
            }
            internal class AtQuarterEndOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;

                public AtQuarterEndOperation(ValueOrExpression<string> selector)
                    : base(selector, LibraDexTemporalStructuredOperationKind.AtQuarterEnd)
                {
                    this.selector = selector;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector)
                {
                    builder.AddFragment(new AtQuarterEndOperation(selector));
                }
                /// <summary>
                /// Builds the backend-neutral DateTime evaluator for the AtQuarterEnd condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate =>
                    (candidate.Month == 3 && candidate.Day == 31) ||
                    (candidate.Month == 6 && candidate.Day == 30) ||
                    (candidate.Month == 9 && candidate.Day == 30) ||
                    (candidate.Month == 12 && candidate.Day == 31), out evaluator);
                }
            }
            internal class AtHalfStartOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;

                public AtHalfStartOperation(ValueOrExpression<string> selector)
                    : base(selector, LibraDexTemporalStructuredOperationKind.AtHalfStart)
                {
                    this.selector = selector;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector)
                {
                    builder.AddFragment(new AtHalfStartOperation(selector));
                }
                /// <summary>
                /// Builds the backend-neutral DateTime evaluator for the AtHalfStart condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate => candidate.Day == 1 && candidate.Month is 1 or 7, out evaluator);
                }
            }
            internal class AtHalfEndOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;

                public AtHalfEndOperation(ValueOrExpression<string> selector)
                    : base(selector, LibraDexTemporalStructuredOperationKind.AtHalfEnd)
                {
                    this.selector = selector;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector)
                {
                    builder.AddFragment(new AtHalfEndOperation(selector));
                }
                /// <summary>
                /// Builds the backend-neutral DateTime evaluator for the AtHalfEnd condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate =>
                    (candidate.Month == 6 && candidate.Day == 30) ||
                    (candidate.Month == 12 && candidate.Day == 31), out evaluator);
                }
            }
            internal class AtFirstOfMonthOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;

                public AtFirstOfMonthOperation(ValueOrExpression<string> selector)
                    : base(selector, LibraDexTemporalStructuredOperationKind.AtFirstOfMonth)
                {
                    this.selector = selector;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector)
                {
                    builder.AddFragment(new AtFirstOfMonthOperation(selector));
                }
                /// <summary>
                /// Builds the backend-neutral DateTime evaluator for the AtFirstOfMonth condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate => candidate.Day == 1, out evaluator);
                }
            }
            internal class AtLastOfMonthOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;

                public AtLastOfMonthOperation(ValueOrExpression<string> selector)
                    : base(selector, LibraDexTemporalStructuredOperationKind.AtLastOfMonth)
                {
                    this.selector = selector;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector)
                {
                    builder.AddFragment(new AtLastOfMonthOperation(selector));
                }
                /// <summary>
                /// Builds the backend-neutral DateTime evaluator for the AtLastOfMonth condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate => candidate.Day == DateTime.DaysInMonth(candidate.Year, candidate.Month), out evaluator);
                }
            }
            internal class IsTodayOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;

                public IsTodayOperation(ValueOrExpression<string> selector)
                    : base(selector, LibraDexTemporalStructuredOperationKind.IsToday)
                {
                    this.selector = selector;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector)
                {
                    builder.AddFragment(new IsTodayOperation(selector));
                }
                /// <summary>
                /// Builds the backend-neutral DateTime evaluator for the IsToday condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                var from = builder.UtcNow.Date;
                var through = from.AddDays(1);
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate => candidate >= from && candidate < through, out evaluator);
                }
            }
            internal class IsYesterdayOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;

                public IsYesterdayOperation(ValueOrExpression<string> selector)
                    : base(selector, LibraDexTemporalStructuredOperationKind.IsYesterday)
                {
                    this.selector = selector;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector)
                {
                    builder.AddFragment(new IsYesterdayOperation(selector));
                }
                /// <summary>
                /// Builds the backend-neutral DateTime evaluator for the IsYesterday condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                var through = builder.UtcNow.Date;
                var from = through.AddDays(-1);
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate => candidate >= from && candidate < through, out evaluator);
                }
            }
            internal class InLastDaysOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<int> param;

                public InLastDaysOperation(ValueOrExpression<string> selector, ValueOrExpression<int> param)
                    : base(selector, LibraDexTemporalStructuredOperationKind.InLastDays, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<int> param)
                {
                    builder.AddFragment(new InLastDaysOperation(selector, param));
                }
                /// <summary>
                /// Builds the backend-neutral DateTime evaluator for the InLastDays condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                var days = builder.MaterializeParameter(param, propPath, owner: this);
                var through = builder.UtcNow;
                var from = through.AddDays(-days);
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate => candidate >= from && candidate <= through, out evaluator);
                }
            }
            internal class InLastHoursOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<int> param;

                public InLastHoursOperation(ValueOrExpression<string> selector, ValueOrExpression<int> param)
                    : base(selector, LibraDexTemporalStructuredOperationKind.InLastHours, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<int> param)
                {
                    builder.AddFragment(new InLastHoursOperation(selector, param));
                }
                /// <summary>
                /// Builds the backend-neutral DateTime evaluator for the InLastHours condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                var hours = builder.MaterializeParameter(param, propPath, owner: this);
                var through = builder.UtcNow;
                var from = through.AddHours(-hours);
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate => candidate >= from && candidate <= through, out evaluator);
                }
            }
            internal class InLastMinutesOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;
                private ValueOrExpression<int> param;

                public InLastMinutesOperation(ValueOrExpression<string> selector, ValueOrExpression<int> param)
                    : base(selector, LibraDexTemporalStructuredOperationKind.InLastMinutes, param)
                {
                    this.selector = selector;
                    this.param = param;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector, ValueOrExpression<int> param)
                {
                    builder.AddFragment(new InLastMinutesOperation(selector, param));
                }
                /// <summary>
                /// Builds the backend-neutral DateTime evaluator for the InLastMinutes condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                var minutes = builder.MaterializeParameter(param, propPath, owner: this);
                var through = builder.UtcNow;
                var from = through.AddMinutes(-minutes);
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate => candidate >= from && candidate <= through, out evaluator);
                }
            }
            internal class InWeekendOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;

                public InWeekendOperation(ValueOrExpression<string> selector)
                    : base(selector, LibraDexTemporalStructuredOperationKind.InWeekend)
                {
                    this.selector = selector;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector)
                {
                    builder.AddFragment(new InWeekendOperation(selector));
                }
                /// <summary>
                /// Builds the backend-neutral DateTime evaluator for the InWeekend condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate => candidate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday, out evaluator);
                }
            }
            internal class InWeekdayOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;

                public InWeekdayOperation(ValueOrExpression<string> selector)
                    : base(selector, LibraDexTemporalStructuredOperationKind.InWeekday)
                {
                    this.selector = selector;
                }

                public static void AddToBuilder(Builder builder, ValueOrExpression<string> selector)
                {
                    builder.AddFragment(new InWeekdayOperation(selector));
                }
                /// <summary>
                /// Builds the backend-neutral DateTime evaluator for the InWeekday condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate => candidate.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday, out evaluator);
                }
            }
            internal class InMorningOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;

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
                /// Builds the backend-neutral DateTime evaluator for the InMorning condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate => candidate.Hour is >= 5 and <= 11, out evaluator);
                }
            }
            internal class InAfternoonOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;

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
                /// Builds the backend-neutral DateTime evaluator for the InAfternoon condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate => candidate.Hour is >= 12 and <= 16, out evaluator);
                }
            }
            internal class InEveningOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;

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
                /// Builds the backend-neutral DateTime evaluator for the InEvening condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate => candidate.Hour is >= 17 and <= 21, out evaluator);
                }
            }
            internal class InNightOperation : LibraDexTemporalStructuredOperation<DateTime>,IFastPredicateSource
            {
                private ValueOrExpression<string> selector;

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
                /// Builds the backend-neutral DateTime evaluator for the InNight condition.<br/>
                /// Deferred operands and UTC clock boundaries are frozen once while the execution plan is built.<br/>
                /// </summary>
                /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
                /// <param name="options">Inheto options used to restore the DateTime property value.<br/></param>
                /// <param name="evaluator">Completed per-record evaluator when the property selector is supported.<br/></param>
                /// <returns>True when the selector resolves to a direct property path; otherwise false.<br/></returns>
                public bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<InhetoBinary, bool> evaluator)
                {
                    evaluator = default!;
                    if (!TryGetConditionSourcePropPath(builder, selector, out var propPath))
                        return false;
                return TryBuildTypedEvaluator<DateTime>(builder, selector, options, candidate => candidate.Hour >= 22 || candidate.Hour <= 4, out evaluator);
                }
            }


            #endregion

            /// <summary>
            /// Creates the DateTimeOperator stage over the supplied condition builder and selector.<br/>
            /// </summary>
            /// <param name="builder">Mutable builder that receives authored condition fragments.<br/></param>
            /// <param name="selectorExpression">Selected property path or calculated selector intent.<br/></param>
            /// <remarks>
            /// Construction retains the builder and selector; subsequent fluent operations append the actual predicate.<br/>
            /// </remarks>
            public DateTimeOperator(Builder builder, ValueOrExpression<string> selectorExpression)
            {
                this.builder = builder;
                this.selectorExpression = selectorExpression;
            }

            private ContinueOrEndCondition Execute(Action action)
            {
                action();
                return new ContinueOrEndCondition(builder);
            }



            // =====================================================================
            // Basic comparisons on full structured time (chronological ordering)
            // =====================================================================

            /// <summary>
            /// Adds a strict greater-than comparison against the selected value.<br/>
            /// </summary>
            /// <param name="value">Comparison operand. May be static, factory-backed, or parameter-bound; execution resolves its value.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition GreaterThan(ValueOrExpression<DateTime> value)
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
            public ContinueOrEndCondition LessThan(ValueOrExpression<DateTime> value)
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
            public ContinueOrEndCondition GreaterOrEqual(ValueOrExpression<DateTime> value)
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
            public ContinueOrEndCondition LessOrEqual(ValueOrExpression<DateTime> value)
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
            public ContinueOrEndCondition EqualTo(ValueOrExpression<DateTime> value)
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
            public ContinueOrEndCondition NotEqualTo(ValueOrExpression<DateTime> value)
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
            public ContinueOrEndCondition Between(ValueOrExpression<DateTime> startValue, ValueOrExpression<DateTime> endValue)
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
            public ContinueOrEndCondition NotBetween(ValueOrExpression<DateTime> startValue, ValueOrExpression<DateTime> endValue)
            {
                return Execute(() => NotBetweenOperation.AddToBuilder(builder, selectorExpression, startValue, endValue));
            }
            /// <summary>
            /// Adds membership of the selected value in the supplied collection.<br/>
            /// </summary>
            /// <param name="theseValues">Candidate values for membership testing.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsIn(IEnumerable<DateTime> theseValues)
            {
                return Execute(() => InOperation.AddToBuilder(builder, selectorExpression, theseValues.Select(v => (ValueOrExpression<DateTime>)v)));
            }
            /// <summary>
            /// Adds reusable DateTime membership using one execution-frozen deferred set.<br/>
            /// </summary>
            /// <param name="theseValues">Typed DateTime set parameter created by the record store.<br/></param>
            /// <returns>The fluent continuation for combining or completing the condition.<br/></returns>
            public ContinueOrEndCondition IsIn(ConditionParameter<IEnumerable<DateTime>> theseValues)
            {
                return Execute(() => InOperation.AddToBuilder(builder, selectorExpression, theseValues));
            }
            /// <summary>
            /// Adds non-membership of the selected value in the supplied collection.<br/>
            /// </summary>
            /// <param name="theseValues">Candidate values for membership testing.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsNotIn(IEnumerable<DateTime> theseValues)
            {
                return Execute(() => NotInOperation.AddToBuilder(builder, selectorExpression, theseValues.Select(v => (ValueOrExpression<DateTime>)v)));
            }
            /// <summary>
            /// Adds reusable negated DateTime membership using one execution-frozen deferred set.<br/>
            /// </summary>
            /// <param name="theseValues">Typed DateTime set parameter created by the record store.<br/></param>
            /// <returns>The fluent continuation for combining or completing the condition.<br/></returns>
            public ContinueOrEndCondition IsNotIn(ConditionParameter<IEnumerable<DateTime>> theseValues)
            {
                return Execute(() => NotInOperation.AddToBuilder(builder, selectorExpression, theseValues));
            }

            // =====================================================================
            // Year selectors (year is stored in bits 49..62)
            // =====================================================================

            /// <summary>
            /// Matches the specified calendar year.<br/>
            /// </summary>
            /// <param name="year">Calendar year.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition YearEqual(int year)
            {
                return Execute(() => YearEqualOperation.AddToBuilder(builder, selectorExpression, year));
            }
            /// <summary>
            /// Matches any of the specified calendar years.<br/>
            /// </summary>
            /// <param name="years">Calendar years to match.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition YearIn(params int[] years)
            {
                return Execute(() => YearInOperation.AddToBuilder(builder, selectorExpression, years.Select(__ => (ValueOrExpression<int>)__)));
            }
            /// <summary>
            /// Matches calendar years within the inclusive specified range.<br/>
            /// </summary>
            /// <param name="startYear">Inclusive lower calendar year.<br/></param>
            /// <param name="endYear">Inclusive upper calendar year.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition YearRange(int startYear, int endYear)
            {
                return Execute(() => YearBetweenOperation.AddToBuilder(builder, selectorExpression, startYear, endYear));
            }
            /// <summary>
            /// Matches calendar years greater than or equal to the specified year.<br/>
            /// </summary>
            /// <param name="year">Calendar year.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition OnOrAfterYear(int year)
            {
                return Execute(() => YearOnOrAfterOperation.AddToBuilder(builder, selectorExpression, year));
            }
            /// <summary>
            /// Matches calendar years less than or equal to the specified year.<br/>
            /// </summary>
            /// <param name="year">Calendar year.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition OnOrBeforeYear(int year)
            {
                return Execute(() => YearOnOrBeforeOperation.AddToBuilder(builder, selectorExpression, year));
            }

            // =====================================================================
            // Month selectors (bits 45..48, mask 0xF)
            // =====================================================================

            /// <summary>
            /// Matches the month component, independently of year.<br/>
            /// </summary>
            /// <param name="month">Month number, with January equal to 1.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition MonthEqual(int month)
            {
                return Execute(() => MonthEqualOperation.AddToBuilder(builder, selectorExpression, month));
            }
            /// <summary>
            /// Matches any of the specified month components, independently of year.<br/>
            /// </summary>
            /// <param name="months">Month numbers, with January equal to 1.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition MonthIn(params int[] months)
            {
                return Execute(() => MonthInOperation.AddToBuilder(builder, selectorExpression, months.Select(__ => (ValueOrExpression<int>)__)));
            }
            /// <summary>
            /// Excludes the specified month components, independently of year.<br/>
            /// </summary>
            /// <param name="months">Month numbers, with January equal to 1.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition MonthNotIn(params int[] months)
            {
                return Execute(() => MonthNotInOperation.AddToBuilder(builder, selectorExpression, months.Select(__ => (ValueOrExpression<int>)__)));
            }
            /// <summary>
            /// Matches month components within the inclusive specified range.<br/>
            /// </summary>
            /// <param name="startMonth">Inclusive lower month number.<br/></param>
            /// <param name="endMonth">Inclusive upper month number.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition MonthRange(int startMonth, int endMonth)
            {
                return Execute(() => MonthBetweenOperation.AddToBuilder(builder, selectorExpression, startMonth, endMonth));
            }
            /// <summary>
            /// Matches month components outside the inclusive specified range.<br/>
            /// </summary>
            /// <param name="startMonth">Inclusive lower month number.<br/></param>
            /// <param name="endMonth">Inclusive upper month number.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition MonthNotInRange(int startMonth, int endMonth)
            {
                return Execute(() => MonthNotBetweenOperation.AddToBuilder(builder, selectorExpression, startMonth, endMonth));
            }

            // =====================================================================
            // Day-of-month selectors (bits 40..44, mask 0x1F)
            // =====================================================================

            /// <summary>
            /// Matches the day-of-month component.<br/>
            /// </summary>
            /// <param name="day">Day of the month.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition DayEqual(int day)
            {
                return Execute(() => DayEqualOperation.AddToBuilder(builder, selectorExpression, day));
            }
            /// <summary>
            /// Matches any of the specified day-of-month components.<br/>
            /// </summary>
            /// <param name="days">Days of the month.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition DayIn(params int[] days)
            {
                return Execute(() => DayInOperation.AddToBuilder(builder, selectorExpression, days.Select(__ => (ValueOrExpression<int>)__)));
            }
            /// <summary>
            /// Matches day-of-month components within the inclusive specified range.<br/>
            /// </summary>
            /// <param name="startDay">Inclusive lower day-of-month bound.<br/></param>
            /// <param name="endDay">Inclusive upper day-of-month bound.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition DayRange(int startDay, int endDay)
            {
                return Execute(() => DayBetweenOperation.AddToBuilder(builder, selectorExpression, startDay, endDay));
            }
            /// <summary>
            /// Matches day-of-month components outside the inclusive specified range.<br/>
            /// </summary>
            /// <param name="startDay">Inclusive lower day-of-month bound.<br/></param>
            /// <param name="endDay">Inclusive upper day-of-month bound.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition NotDayRange(int startDay, int endDay)
            {
                return Execute(() => DayNotBetweenOperation.AddToBuilder(builder, selectorExpression, startDay, endDay));
            }

            // =====================================================================
            // Year-Month / Year-Month-Day combinations
            // =====================================================================

            /// <summary>
            /// Matches the specified calendar year and month.<br/>
            /// </summary>
            /// <param name="year">Calendar year.<br/></param>
            /// <param name="month">Month number, with January equal to 1.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition YearMonth(int year, int month)
            {
                return Execute(() => YearMonthOperation.AddToBuilder(builder, selectorExpression, year, month));
            }
            /// <summary>
            /// Matches any of the supplied calendar year/month pairs.<br/>
            /// </summary>
            /// <param name="list">Calendar year/month pairs to match.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition YearMonthIn(IEnumerable<(int year, int month)> list)
            {
                return Execute(() => YearMonthInOperation.AddToBuilder(builder, selectorExpression, list.Select(x => (ValueOrExpression<(int year, int month)>)x)));
            }
            /// <summary>
            /// Matches the specified calendar year, month, and day.<br/>
            /// </summary>
            /// <param name="year">Calendar year.<br/></param>
            /// <param name="month">Month number, with January equal to 1.<br/></param>
            /// <param name="day">Day of the month.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition YearMonthDay(int year, int month, int day)
            {
                return Execute(() => YearMonthDayOperation.AddToBuilder(builder, selectorExpression, year, month, day));
            }
            /// <summary>
            /// Matches any of the supplied calendar year/month/day triples.<br/>
            /// </summary>
            /// <param name="list">Calendar year/month/day triples to match.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition YearMonthDayIn(IEnumerable<(int year, int month, int day)> list)
            {
                return Execute(() => YearMonthDayInOperation.AddToBuilder(builder, selectorExpression, list.Select(x => (ValueOrExpression<(int year, int month, int day)>)x)));
            }
            /// <summary>
            /// Matches the specified year and any of the supplied months.<br/>
            /// </summary>
            /// <param name="year">Calendar year.<br/></param>
            /// <param name="months">Month numbers, with January equal to 1.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition YearInMonths(int year, params int[] months)
            {
                return Execute(() => YearMonthsOperation.AddToBuilder(builder, selectorExpression, year, months.Select(__ => (ValueOrExpression<int>)__)));
            }


            /// <summary>
            /// Matches the specified month and day, independently of year.<br/>
            /// </summary>
            /// <param name="month">Month number, with January equal to 1.<br/></param>
            /// <param name="day">Day of the month.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition SameDayEveryYear(int month, int day)
            {
                return Execute(() => MonthDayOperation.AddToBuilder(builder, selectorExpression, month, day));
            }

            // =====================================================================
            // Quarter logic (quarters derived from month)
            //   Quarter = ((Month - 1) / 3) + 1
            //   Month in:
            //     Q1: 1,2,3; Q2: 4,5,6; Q3: 7,8,9; Q4: 10,11,12
            // =====================================================================

            /// <summary>
            /// Matches the specified calendar quarter, independently of year.<br/>
            /// </summary>
            /// <param name="quarter">Calendar quarter from 1 through 4.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition InQuarter(int quarter)
            {
                return Execute(() => InQuarterOperation.AddToBuilder(builder, selectorExpression, quarter));
            }
            /// <summary>
            /// Matches calendar quarters within the inclusive specified range.<br/>
            /// </summary>
            /// <param name="startQuarter">Inclusive lower calendar quarter.<br/></param>
            /// <param name="endQuarter">Inclusive upper calendar quarter.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition InQuarterRange(int startQuarter, int endQuarter)
            {
                return Execute(() => InQuarterRangeOperation.AddToBuilder(builder, selectorExpression, startQuarter, endQuarter));
            }
            /// <summary>
            /// Matches the specified calendar year and quarter.<br/>
            /// </summary>
            /// <param name="year">Calendar year.<br/></param>
            /// <param name="quarter">Calendar quarter from 1 through 4.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition YearQuarter(int year, int quarter)
            {
                return Execute(() => YearQuarterOperation.AddToBuilder(builder, selectorExpression, year, quarter));
            }
            /// <summary>
            /// Matches the first calendar day of a quarter: January 1, April 1, July 1, or October 1.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsQuarterStart()
            {
                return Execute(() => AtQuarterStartOperation.AddToBuilder(builder, selectorExpression));
            }

            /// <summary>
            /// Matches the last calendar day of March, June, September, or December.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsQuarterEnd()
            {
                return Execute(() => AtQuarterEndOperation.AddToBuilder(builder, selectorExpression));
            }

            /// <summary>
            /// Matches January 1 or July 1.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsHalfYearStart()
            {
                return Execute(() => AtHalfStartOperation.AddToBuilder(builder, selectorExpression));
            }

            /// <summary>
            /// Matches June 30 or December 31.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsHalfYearEnd()
            {
                return Execute(() => AtHalfEndOperation.AddToBuilder(builder, selectorExpression));
            }

            /// <summary>
            /// Matches the first calendar day of any month.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsFirstOfMonth()
            {
                return Execute(() => AtFirstOfMonthOperation.AddToBuilder(builder, selectorExpression));
            }

            /// <summary>
            /// Matches the final calendar day of any month, including leap-year February.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsLastOfMonth()
            {
                return Execute(() => AtLastOfMonthOperation.AddToBuilder(builder, selectorExpression));
            }

            // =====================================================================
            // Semantic / relative time helpers (today, last N days, etc)
            // These assume you compute bounds in .NET and store packed values as params.
            // =====================================================================

            /// <summary>
            /// Matches the calendar date of the execution-frozen UTC clock.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// The clock is captured once for execution. DateTime comparisons use UTC date boundaries without implicitly converting each stored DateTime value to another time zone.<br/>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsToday()
            {
                return Execute(() => IsTodayOperation.AddToBuilder(builder, selectorExpression));
            }
            /// <summary>
            /// Matches the calendar date immediately before the execution-frozen UTC date.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// The clock is captured once for execution. DateTime comparisons use UTC date boundaries without implicitly converting each stored DateTime value to another time zone.<br/>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsYesterday()
            {
                return Execute(() => IsYesterdayOperation.AddToBuilder(builder, selectorExpression));
            }
            /// <summary>
            /// Matches the inclusive interval from the execution-frozen UTC time minus the requested days through that time.<br/>
            /// </summary>
            /// <param name="days">Number of days to subtract from the execution-frozen UTC boundary.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// The clock boundary is captured for execution, not separately for each record.<br/>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsInLastDays(int days)
            {
                return Execute(() => InLastDaysOperation.AddToBuilder(builder, selectorExpression, days));
            }
            /// <summary>
            /// Matches the inclusive interval from the execution-frozen UTC time minus the requested hours through that time.<br/>
            /// </summary>
            /// <param name="hours">Number of hours to subtract from the execution-frozen UTC time.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// The clock boundary is captured for execution, not separately for each record.<br/>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsInLastHours(int hours)
            {
                return Execute(() => InLastHoursOperation.AddToBuilder(builder, selectorExpression, hours));
            }
            /// <summary>
            /// Matches the inclusive interval from the execution-frozen UTC time minus the requested minutes through that time.<br/>
            /// </summary>
            /// <param name="minutes">Number of minutes to subtract from the execution-frozen UTC time.<br/></param>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// The clock boundary is captured for execution, not separately for each record.<br/>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsInLastMinutes(int minutes)
            {
                return Execute(() => InLastMinutesOperation.AddToBuilder(builder, selectorExpression, minutes));
            }
            /// <summary>
            /// Matches Saturday or Sunday.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsWeekend()
            {
                return Execute(() => InWeekendOperation.AddToBuilder(builder, selectorExpression));
            }
            /// <summary>
            /// Matches Monday through Friday.<br/>
            /// </summary>
            /// <returns>A fluent continuation over the same builder for combining or completing the condition.<br/></returns>
            /// <remarks>
            /// Appends a condition fragment; it does not enumerate or hydrate records.<br/>
            /// </remarks>
            public ContinueOrEndCondition IsWeekday()
            {
                return Execute(() => InWeekdayOperation.AddToBuilder(builder, selectorExpression));
            }

            // =====================================================================
            // Time-of-day (hour-based) semantics (hour bits 35..39, mask 0x1F)
            // =====================================================================

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

            /// <summary>Compares this DateTime property for equality with a textual right-hand property completed by <c>.AsDateTime</c>.<br/></summary>
            public DateTimePropertyComparisonTypeSelector PropEqualTo(string propPath) => new(builder, selectorExpression, propPath, PropertyComparisonKind.Equal);
            /// <summary>Compares this DateTime property for equality with a typed right-hand property.<br/></summary>
            public ContinueOrEndCondition PropEqualTo(Expression<Func<T, DateTime>> propSelector) => AddPropertyComparison<DateTime>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.Equal);
            /// <summary>Compares this DateTime property for inequality with a textual right-hand property completed by <c>.AsDateTime</c>.<br/></summary>
            public DateTimePropertyComparisonTypeSelector PropNotEqualTo(string propPath) => new(builder, selectorExpression, propPath, PropertyComparisonKind.NotEqual);
            /// <summary>Compares this DateTime property for inequality with a typed right-hand property.<br/></summary>
            public ContinueOrEndCondition PropNotEqualTo(Expression<Func<T, DateTime>> propSelector) => AddPropertyComparison<DateTime>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.NotEqual);
            /// <summary>Compares whether this DateTime property is greater than a textual right-hand property completed by <c>.AsDateTime</c>.<br/></summary>
            public DateTimePropertyComparisonTypeSelector PropGreaterThan(string propPath) => new(builder, selectorExpression, propPath, PropertyComparisonKind.Greater);
            /// <summary>Compares whether this DateTime property is greater than a typed right-hand property.<br/></summary>
            public ContinueOrEndCondition PropGreaterThan(Expression<Func<T, DateTime>> propSelector) => AddPropertyComparison<DateTime>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.Greater);
            /// <summary>Compares whether this DateTime property is greater than or equal to a textual right-hand property completed by <c>.AsDateTime</c>.<br/></summary>
            public DateTimePropertyComparisonTypeSelector PropGreaterOrEqual(string propPath) => new(builder, selectorExpression, propPath, PropertyComparisonKind.GreaterOrEqual);
            /// <summary>Compares whether this DateTime property is greater than or equal to a typed right-hand property.<br/></summary>
            public ContinueOrEndCondition PropGreaterOrEqual(Expression<Func<T, DateTime>> propSelector) => AddPropertyComparison<DateTime>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.GreaterOrEqual);
            /// <summary>Compares whether this DateTime property is less than a textual right-hand property completed by <c>.AsDateTime</c>.<br/></summary>
            public DateTimePropertyComparisonTypeSelector PropLessThan(string propPath) => new(builder, selectorExpression, propPath, PropertyComparisonKind.Less);
            /// <summary>Compares whether this DateTime property is less than a typed right-hand property.<br/></summary>
            public ContinueOrEndCondition PropLessThan(Expression<Func<T, DateTime>> propSelector) => AddPropertyComparison<DateTime>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.Less);
            /// <summary>Compares whether this DateTime property is less than or equal to a textual right-hand property completed by <c>.AsDateTime</c>.<br/></summary>
            public DateTimePropertyComparisonTypeSelector PropLessOrEqual(string propPath) => new(builder, selectorExpression, propPath, PropertyComparisonKind.LessOrEqual);
            /// <summary>Compares whether this DateTime property is less than or equal to a typed right-hand property.<br/></summary>
            public ContinueOrEndCondition PropLessOrEqual(Expression<Func<T, DateTime>> propSelector) => AddPropertyComparison<DateTime>(builder, selectorExpression, WrapPropertyExpression(propSelector), PropertyComparisonKind.LessOrEqual);
        }
    }

        #endregion
}

// =====================================================================
// Millisecond + nano-slice
// =====================================================================
