using System.Linq.Expressions;
using System.Numerics;
using Inheto;

namespace AbraxasDB;

/// <summary>
/// Selects the divisor contract used by grouped variance and standard-deviation reductions.<br/>
/// </summary>
public enum AggregateVarianceMode
{
    /// <summary>Divides the accumulated squared deviation by the contributing value count.<br/></summary>
    Population = 0,
    /// <summary>Applies Bessel's correction and requires at least two contributing values.<br/></summary>
    Sample = 1
}

public partial class RecordStore<T>
{
    public sealed partial class AggregateFacade
    {
        public sealed partial class AggregateDefinition<TKey>
        {

            /// <summary>Counts structurally distinct non-null values produced inside each group.<br/></summary>
            public AggregateDefinition<TKey, ulong> AddDistinctCount<TValue>(Expression<Func<T, TValue>> value)
                where TValue : notnull
                => Add(owner.CreateDistinctCountReducer<TKey, TValue>(owner.CreateExpressionReader(value)));

            /// <summary>Counts structurally distinct non-null values produced by one reusable selector inside each group.<br/></summary>
            public AggregateDefinition<TKey, ulong> AddDistinctCount<TValue>(Selector<T>.IValue<TValue> value)
                where TValue : notnull
                => Add(owner.CreateDistinctCountReducer<TKey, TValue>(owner.CreateSelectorReader(value)));

            /// <summary>Counts missing or null values at one validated textual path inside each group.<br/></summary>
            public AggregateDefinition<TKey, ulong> AddNullCount<TValue>(string path)
                => Add(owner.CreatePresenceCountReducer<TKey, TValue>(owner.CreatePathReader<TValue>(path), countMissing: true));

            /// <summary>Counts non-null values at one validated textual path inside each group.<br/></summary>
            public AggregateDefinition<TKey, ulong> AddNonNullCount<TValue>(string path)
                => Add(owner.CreatePresenceCountReducer<TKey, TValue>(owner.CreatePathReader<TValue>(path), countMissing: false));

            /// <summary>Returns true when any record in the group satisfies the supplied predicate.<br/></summary>
            public AggregateDefinition<TKey, bool> AddAny(Expression<Func<T, bool>> predicate)
                => Add(owner.CreateBooleanReducer<TKey>(owner.CreateExpressionReader(predicate), all: false));

            /// <summary>Returns true when every record in the group satisfies the supplied predicate, including vacuous truth for an empty input.<br/></summary>
            public AggregateDefinition<TKey, bool> AddAll(Expression<Func<T, bool>> predicate)
                => Add(owner.CreateBooleanReducer<TKey>(owner.CreateExpressionReader(predicate), all: true));

            /// <summary>Computes population or sample variance with Welford's one-pass recurrence.<br/></summary>
            public AggregateDefinition<TKey, double?> AddVariance<TNumber>(
                Expression<Func<T, TNumber>> value,
                AggregateVarianceMode mode = AggregateVarianceMode.Population)
                where TNumber : struct, INumber<TNumber>
                => Add(owner.CreateVarianceReducer<TKey, TNumber>(owner.CreateExpressionReader(value), mode, standardDeviation: false));

            /// <summary>Computes population or sample standard deviation with Welford's one-pass recurrence.<br/></summary>
            public AggregateDefinition<TKey, double?> AddStandardDeviation<TNumber>(
                Expression<Func<T, TNumber>> value,
                AggregateVarianceMode mode = AggregateVarianceMode.Population)
                where TNumber : struct, INumber<TNumber>
                => Add(owner.CreateVarianceReducer<TKey, TNumber>(owner.CreateExpressionReader(value), mode, standardDeviation: true));

            /// <summary>Computes a weighted arithmetic mean without retaining contributing rows.<br/></summary>
            public AggregateDefinition<TKey, double?> AddWeightedAverage<TValue, TWeight>(
                Expression<Func<T, TValue>> value,
                Expression<Func<T, TWeight>> weight)
                where TValue : struct, INumber<TValue>
                where TWeight : struct, INumber<TWeight>
                => Add(owner.CreateWeightedAverageReducer<TKey, TValue, TWeight>(owner.CreateExpressionReader(value), owner.CreateExpressionReader(weight)));

            /// <summary>Returns the associated value from the first record holding the minimum comparison value.<br/></summary>
            public AggregateDefinition<TKey, TAssociated?> AddMinBy<TValue, TAssociated>(
                Expression<Func<T, TValue>> value,
                Expression<Func<T, TAssociated>> associated)
                where TValue : struct, IComparable<TValue>
                where TAssociated : struct
                => Add(owner.CreateAssociatedExtremumReducer<TKey, TValue, TAssociated>(owner.CreateExpressionReader(value), owner.CreateExpressionReader(associated), minimum: true));

            /// <summary>Returns the associated value from the first record holding the maximum comparison value.<br/></summary>
            public AggregateDefinition<TKey, TAssociated?> AddMaxBy<TValue, TAssociated>(
                Expression<Func<T, TValue>> value,
                Expression<Func<T, TAssociated>> associated)
                where TValue : struct, IComparable<TValue>
                where TAssociated : struct
                => Add(owner.CreateAssociatedExtremumReducer<TKey, TValue, TAssociated>(owner.CreateExpressionReader(value), owner.CreateExpressionReader(associated), minimum: false));
        }

        public sealed partial class AggregateDefinition<TKey, T1>
        {

            /// <summary>Counts structurally distinct non-null values produced inside each group.<br/></summary>
            public AggregateDefinition<TKey, T1, ulong> AddDistinctCount<TValue>(Expression<Func<T, TValue>> value)
                where TValue : notnull
                => Add(Owner.CreateDistinctCountReducer<TKey, TValue>(Owner.CreateExpressionReader(value)));

            /// <summary>Counts structurally distinct non-null values produced by one reusable selector inside each group.<br/></summary>
            public AggregateDefinition<TKey, T1, ulong> AddDistinctCount<TValue>(Selector<T>.IValue<TValue> value)
                where TValue : notnull
                => Add(Owner.CreateDistinctCountReducer<TKey, TValue>(Owner.CreateSelectorReader(value)));

            /// <summary>Counts missing or null values at one validated textual path inside each group.<br/></summary>
            public AggregateDefinition<TKey, T1, ulong> AddNullCount<TValue>(string path)
                => Add(Owner.CreatePresenceCountReducer<TKey, TValue>(Owner.CreatePathReader<TValue>(path), countMissing: true));

            /// <summary>Counts non-null values at one validated textual path inside each group.<br/></summary>
            public AggregateDefinition<TKey, T1, ulong> AddNonNullCount<TValue>(string path)
                => Add(Owner.CreatePresenceCountReducer<TKey, TValue>(Owner.CreatePathReader<TValue>(path), countMissing: false));

            /// <summary>Returns true when any record in the group satisfies the supplied predicate.<br/></summary>
            public AggregateDefinition<TKey, T1, bool> AddAny(Expression<Func<T, bool>> predicate)
                => Add(Owner.CreateBooleanReducer<TKey>(Owner.CreateExpressionReader(predicate), all: false));

            /// <summary>Returns true when every record in the group satisfies the supplied predicate, including vacuous truth for an empty input.<br/></summary>
            public AggregateDefinition<TKey, T1, bool> AddAll(Expression<Func<T, bool>> predicate)
                => Add(Owner.CreateBooleanReducer<TKey>(Owner.CreateExpressionReader(predicate), all: true));

            /// <summary>Computes population or sample variance with Welford's one-pass recurrence.<br/></summary>
            public AggregateDefinition<TKey, T1, double?> AddVariance<TNumber>(
                Expression<Func<T, TNumber>> value,
                AggregateVarianceMode mode = AggregateVarianceMode.Population)
                where TNumber : struct, INumber<TNumber>
                => Add(Owner.CreateVarianceReducer<TKey, TNumber>(Owner.CreateExpressionReader(value), mode, standardDeviation: false));

            /// <summary>Computes population or sample standard deviation with Welford's one-pass recurrence.<br/></summary>
            public AggregateDefinition<TKey, T1, double?> AddStandardDeviation<TNumber>(
                Expression<Func<T, TNumber>> value,
                AggregateVarianceMode mode = AggregateVarianceMode.Population)
                where TNumber : struct, INumber<TNumber>
                => Add(Owner.CreateVarianceReducer<TKey, TNumber>(Owner.CreateExpressionReader(value), mode, standardDeviation: true));

            /// <summary>Computes a weighted arithmetic mean without retaining contributing rows.<br/></summary>
            public AggregateDefinition<TKey, T1, double?> AddWeightedAverage<TValue, TWeight>(
                Expression<Func<T, TValue>> value,
                Expression<Func<T, TWeight>> weight)
                where TValue : struct, INumber<TValue>
                where TWeight : struct, INumber<TWeight>
                => Add(Owner.CreateWeightedAverageReducer<TKey, TValue, TWeight>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(weight)));

            /// <summary>Returns the associated value from the first record holding the minimum comparison value.<br/></summary>
            public AggregateDefinition<TKey, T1, TAssociated?> AddMinBy<TValue, TAssociated>(
                Expression<Func<T, TValue>> value,
                Expression<Func<T, TAssociated>> associated)
                where TValue : struct, IComparable<TValue>
                where TAssociated : struct
                => Add(Owner.CreateAssociatedExtremumReducer<TKey, TValue, TAssociated>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(associated), minimum: true));

            /// <summary>Returns the associated value from the first record holding the maximum comparison value.<br/></summary>
            public AggregateDefinition<TKey, T1, TAssociated?> AddMaxBy<TValue, TAssociated>(
                Expression<Func<T, TValue>> value,
                Expression<Func<T, TAssociated>> associated)
                where TValue : struct, IComparable<TValue>
                where TAssociated : struct
                => Add(Owner.CreateAssociatedExtremumReducer<TKey, TValue, TAssociated>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(associated), minimum: false));
        }

        public sealed partial class AggregateDefinition<TKey, T1, T2>
        {

            /// <summary>Counts structurally distinct non-null values produced inside each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, ulong> AddDistinctCount<TValue>(Expression<Func<T, TValue>> value)
                where TValue : notnull
                => Add(Owner.CreateDistinctCountReducer<TKey, TValue>(Owner.CreateExpressionReader(value)));

            /// <summary>Counts missing or null values at one validated textual path inside each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, ulong> AddNullCount<TValue>(string path)
                => Add(Owner.CreatePresenceCountReducer<TKey, TValue>(Owner.CreatePathReader<TValue>(path), countMissing: true));

            /// <summary>Counts non-null values at one validated textual path inside each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, ulong> AddNonNullCount<TValue>(string path)
                => Add(Owner.CreatePresenceCountReducer<TKey, TValue>(Owner.CreatePathReader<TValue>(path), countMissing: false));

            /// <summary>Returns true when any record in the group satisfies the supplied predicate.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, bool> AddAny(Expression<Func<T, bool>> predicate)
                => Add(Owner.CreateBooleanReducer<TKey>(Owner.CreateExpressionReader(predicate), all: false));

            /// <summary>Returns true when every record in the group satisfies the supplied predicate, including vacuous truth for an empty input.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, bool> AddAll(Expression<Func<T, bool>> predicate)
                => Add(Owner.CreateBooleanReducer<TKey>(Owner.CreateExpressionReader(predicate), all: true));

            /// <summary>Computes population or sample variance with Welford's one-pass recurrence.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, double?> AddVariance<TNumber>(
                Expression<Func<T, TNumber>> value,
                AggregateVarianceMode mode = AggregateVarianceMode.Population)
                where TNumber : struct, INumber<TNumber>
                => Add(Owner.CreateVarianceReducer<TKey, TNumber>(Owner.CreateExpressionReader(value), mode, standardDeviation: false));

            /// <summary>Computes population or sample standard deviation with Welford's one-pass recurrence.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, double?> AddStandardDeviation<TNumber>(
                Expression<Func<T, TNumber>> value,
                AggregateVarianceMode mode = AggregateVarianceMode.Population)
                where TNumber : struct, INumber<TNumber>
                => Add(Owner.CreateVarianceReducer<TKey, TNumber>(Owner.CreateExpressionReader(value), mode, standardDeviation: true));

            /// <summary>Computes a weighted arithmetic mean without retaining contributing rows.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, double?> AddWeightedAverage<TValue, TWeight>(
                Expression<Func<T, TValue>> value,
                Expression<Func<T, TWeight>> weight)
                where TValue : struct, INumber<TValue>
                where TWeight : struct, INumber<TWeight>
                => Add(Owner.CreateWeightedAverageReducer<TKey, TValue, TWeight>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(weight)));

            /// <summary>Returns the associated value from the first record holding the minimum comparison value.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, TAssociated?> AddMinBy<TValue, TAssociated>(
                Expression<Func<T, TValue>> value,
                Expression<Func<T, TAssociated>> associated)
                where TValue : struct, IComparable<TValue>
                where TAssociated : struct
                => Add(Owner.CreateAssociatedExtremumReducer<TKey, TValue, TAssociated>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(associated), minimum: true));

            /// <summary>Returns the associated value from the first record holding the maximum comparison value.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, TAssociated?> AddMaxBy<TValue, TAssociated>(
                Expression<Func<T, TValue>> value,
                Expression<Func<T, TAssociated>> associated)
                where TValue : struct, IComparable<TValue>
                where TAssociated : struct
                => Add(Owner.CreateAssociatedExtremumReducer<TKey, TValue, TAssociated>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(associated), minimum: false));
        }

        public sealed partial class AggregateDefinition<TKey, T1, T2, T3>
        {

            /// <summary>Counts structurally distinct non-null values produced inside each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, ulong> AddDistinctCount<TValue>(Expression<Func<T, TValue>> value)
                where TValue : notnull
                => Add(Owner.CreateDistinctCountReducer<TKey, TValue>(Owner.CreateExpressionReader(value)));

            /// <summary>Counts missing or null values at one validated textual path inside each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, ulong> AddNullCount<TValue>(string path)
                => Add(Owner.CreatePresenceCountReducer<TKey, TValue>(Owner.CreatePathReader<TValue>(path), countMissing: true));

            /// <summary>Counts non-null values at one validated textual path inside each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, ulong> AddNonNullCount<TValue>(string path)
                => Add(Owner.CreatePresenceCountReducer<TKey, TValue>(Owner.CreatePathReader<TValue>(path), countMissing: false));

            /// <summary>Returns true when any record in the group satisfies the supplied predicate.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, bool> AddAny(Expression<Func<T, bool>> predicate)
                => Add(Owner.CreateBooleanReducer<TKey>(Owner.CreateExpressionReader(predicate), all: false));

            /// <summary>Returns true when every record in the group satisfies the supplied predicate, including vacuous truth for an empty input.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, bool> AddAll(Expression<Func<T, bool>> predicate)
                => Add(Owner.CreateBooleanReducer<TKey>(Owner.CreateExpressionReader(predicate), all: true));

            /// <summary>Computes population or sample variance with Welford's one-pass recurrence.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, double?> AddVariance<TNumber>(
                Expression<Func<T, TNumber>> value,
                AggregateVarianceMode mode = AggregateVarianceMode.Population)
                where TNumber : struct, INumber<TNumber>
                => Add(Owner.CreateVarianceReducer<TKey, TNumber>(Owner.CreateExpressionReader(value), mode, standardDeviation: false));

            /// <summary>Computes population or sample standard deviation with Welford's one-pass recurrence.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, double?> AddStandardDeviation<TNumber>(
                Expression<Func<T, TNumber>> value,
                AggregateVarianceMode mode = AggregateVarianceMode.Population)
                where TNumber : struct, INumber<TNumber>
                => Add(Owner.CreateVarianceReducer<TKey, TNumber>(Owner.CreateExpressionReader(value), mode, standardDeviation: true));

            /// <summary>Computes a weighted arithmetic mean without retaining contributing rows.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, double?> AddWeightedAverage<TValue, TWeight>(
                Expression<Func<T, TValue>> value,
                Expression<Func<T, TWeight>> weight)
                where TValue : struct, INumber<TValue>
                where TWeight : struct, INumber<TWeight>
                => Add(Owner.CreateWeightedAverageReducer<TKey, TValue, TWeight>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(weight)));

            /// <summary>Returns the associated value from the first record holding the minimum comparison value.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, TAssociated?> AddMinBy<TValue, TAssociated>(
                Expression<Func<T, TValue>> value,
                Expression<Func<T, TAssociated>> associated)
                where TValue : struct, IComparable<TValue>
                where TAssociated : struct
                => Add(Owner.CreateAssociatedExtremumReducer<TKey, TValue, TAssociated>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(associated), minimum: true));

            /// <summary>Returns the associated value from the first record holding the maximum comparison value.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, TAssociated?> AddMaxBy<TValue, TAssociated>(
                Expression<Func<T, TValue>> value,
                Expression<Func<T, TAssociated>> associated)
                where TValue : struct, IComparable<TValue>
                where TAssociated : struct
                => Add(Owner.CreateAssociatedExtremumReducer<TKey, TValue, TAssociated>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(associated), minimum: false));
        }

        /// <summary>Represents a typed aggregate definition with 4 declared output values.<br/></summary>
        public sealed partial class AggregateDefinition<TKey, T1, T2, T3, T4>
            where TKey : notnull
        {

            /// <summary>Adds the number of records entering each group as the next result value.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, ulong> AddCount() => Add(CreateCountReducer<TKey>());

            /// <summary>Adds a representative validated property from the first record entering each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, TValue> AddProp<TValue>(string path) => Add(Owner.CreateFirstReducer<TKey, TValue>(Owner.CreatePathReader<TValue>(path)));

            /// <summary>Adds a representative expression result from the first record entering each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, TValue> AddCalc<TValue>(Expression<Func<T, TValue>> value) => Add(Owner.CreateFirstReducer<TKey, TValue>(Owner.CreateExpressionReader(value)));

            /// <summary>Adds a representative calculation over a typed source expression.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, TValue> AddCalc<TSource, TValue>(Expression<Func<T, TSource>> source, Func<TSource, TValue> calculation) => Add(Owner.CreateCalculatedFirstReducer<TKey, TSource, TValue>(Owner.CreateExpressionReader(source), calculation));

            /// <summary>Adds a representative calculation over a validated textual source path.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, TValue> AddCalc<TSource, TValue>(string path, Func<TSource, TValue> calculation) => Add(Owner.CreateCalculatedFirstReducer<TKey, TSource, TValue>(Owner.CreatePathReader<TSource>(path), calculation));

            /// <summary>Adds the checked numeric sum for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, TNumber> AddSum<TNumber>(Expression<Func<T, TNumber>> value) where TNumber : struct, INumber<TNumber> => Add(Owner.CreateSumReducer<TKey, TNumber>(Owner.CreateExpressionReader(value)));

            /// <summary>Adds the arithmetic mean as nullable Double for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, double?> AddAverage<TNumber>(Expression<Func<T, TNumber>> value) where TNumber : struct, INumber<TNumber> => Add(Owner.CreateAverageReducer<TKey, TNumber>(Owner.CreateExpressionReader(value)));

            /// <summary>Adds a Decimal-preserving arithmetic mean for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, decimal?> AddAverage(Expression<Func<T, decimal>> value) => Add(Owner.CreateDecimalAverageReducer<TKey>(Owner.CreateExpressionReader(value)));

            /// <summary>Adds the smallest non-null comparable value for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, TValue?> AddMin<TValue>(Expression<Func<T, TValue>> value) where TValue : struct, IComparable<TValue> => Add(Owner.CreateExtremumReducer<TKey, TValue>(Owner.CreateExpressionReader(value), true));

            /// <summary>Adds the largest non-null comparable value for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, TValue?> AddMax<TValue>(Expression<Func<T, TValue>> value) where TValue : struct, IComparable<TValue> => Add(Owner.CreateExtremumReducer<TKey, TValue>(Owner.CreateExpressionReader(value), false));

            /// <summary>Adds a custom streaming reduction over complete records with by-reference state.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, TResult> AddAggregate<TState, TResult>(Func<TState> seed, AggregateStep<TState> accumulate, AggregateFinish<TState, TResult> finish) => Add(Owner.CreateCustomReducer<TKey, TState, TResult>(seed, accumulate, finish));

            /// <summary>Adds a custom streaming reduction over a typed source expression with by-reference state.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, TResult> AddAggregate<TSource, TState, TResult>(Expression<Func<T, TSource>> source, Func<TState> seed, AggregateValueStep<TSource, TState> accumulate, AggregateFinish<TState, TResult> finish) => Add(Owner.CreateCustomValueReducer<TKey, TSource, TState, TResult>(Owner.CreateExpressionReader(source), seed, accumulate, finish));

            /// <summary>Adds a custom streaming reduction over a validated textual source path with by-reference state.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, TResult> AddAggregate<TSource, TState, TResult>(string path, Func<TState> seed, AggregateValueStep<TSource, TState> accumulate, AggregateFinish<TState, TResult> finish) => Add(Owner.CreateCustomValueReducer<TKey, TSource, TState, TResult>(Owner.CreatePathReader<TSource>(path), seed, accumulate, finish));

            /// <summary>Counts structurally distinct non-null values produced inside each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, ulong> AddDistinctCount<TValue>(Expression<Func<T, TValue>> value)
                where TValue : notnull
                => Add(Owner.CreateDistinctCountReducer<TKey, TValue>(Owner.CreateExpressionReader(value)));

            /// <summary>Counts missing or null values at one validated textual path inside each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, ulong> AddNullCount<TValue>(string path)
                => Add(Owner.CreatePresenceCountReducer<TKey, TValue>(Owner.CreatePathReader<TValue>(path), countMissing: true));

            /// <summary>Counts non-null values at one validated textual path inside each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, ulong> AddNonNullCount<TValue>(string path)
                => Add(Owner.CreatePresenceCountReducer<TKey, TValue>(Owner.CreatePathReader<TValue>(path), countMissing: false));

            /// <summary>Returns true when any record in the group satisfies the supplied predicate.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, bool> AddAny(Expression<Func<T, bool>> predicate)
                => Add(Owner.CreateBooleanReducer<TKey>(Owner.CreateExpressionReader(predicate), all: false));

            /// <summary>Returns true when every record in the group satisfies the supplied predicate, including vacuous truth for an empty input.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, bool> AddAll(Expression<Func<T, bool>> predicate)
                => Add(Owner.CreateBooleanReducer<TKey>(Owner.CreateExpressionReader(predicate), all: true));

            /// <summary>Computes population or sample variance with Welford's one-pass recurrence.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, double?> AddVariance<TNumber>(
                Expression<Func<T, TNumber>> value,
                AggregateVarianceMode mode = AggregateVarianceMode.Population)
                where TNumber : struct, INumber<TNumber>
                => Add(Owner.CreateVarianceReducer<TKey, TNumber>(Owner.CreateExpressionReader(value), mode, standardDeviation: false));

            /// <summary>Computes population or sample standard deviation with Welford's one-pass recurrence.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, double?> AddStandardDeviation<TNumber>(
                Expression<Func<T, TNumber>> value,
                AggregateVarianceMode mode = AggregateVarianceMode.Population)
                where TNumber : struct, INumber<TNumber>
                => Add(Owner.CreateVarianceReducer<TKey, TNumber>(Owner.CreateExpressionReader(value), mode, standardDeviation: true));

            /// <summary>Computes a weighted arithmetic mean without retaining contributing rows.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, double?> AddWeightedAverage<TValue, TWeight>(
                Expression<Func<T, TValue>> value,
                Expression<Func<T, TWeight>> weight)
                where TValue : struct, INumber<TValue>
                where TWeight : struct, INumber<TWeight>
                => Add(Owner.CreateWeightedAverageReducer<TKey, TValue, TWeight>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(weight)));

            /// <summary>Returns the associated value from the first record holding the minimum comparison value.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, TAssociated?> AddMinBy<TValue, TAssociated>(
                Expression<Func<T, TValue>> value,
                Expression<Func<T, TAssociated>> associated)
                where TValue : struct, IComparable<TValue>
                where TAssociated : struct
                => Add(Owner.CreateAssociatedExtremumReducer<TKey, TValue, TAssociated>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(associated), minimum: true));

            /// <summary>Returns the associated value from the first record holding the maximum comparison value.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, TAssociated?> AddMaxBy<TValue, TAssociated>(
                Expression<Func<T, TValue>> value,
                Expression<Func<T, TAssociated>> associated)
                where TValue : struct, IComparable<TValue>
                where TAssociated : struct
                => Add(Owner.CreateAssociatedExtremumReducer<TKey, TValue, TAssociated>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(associated), minimum: false));
            private AggregateDefinition<TKey, T1, T2, T3, T4, TValue> Add<TState, TValue>(AggregateReducer<TKey, TState, TValue> reducer)
                => new(Owner, Plan.Append(reducer, static (prior, value) => (prior.Value1, prior.Value2, prior.Value3, prior.Value4, value)));
        }

        /// <summary>Represents a typed aggregate definition with 5 declared output values.<br/></summary>
        public sealed partial class AggregateDefinition<TKey, T1, T2, T3, T4, T5> : AggregateDefinitionBase<TKey, (T1 Value1, T2 Value2, T3 Value3, T4 Value4, T5 Value5)>
            where TKey : notnull
        {
            internal AggregateDefinition(AggregateFacade owner, AggregatePlanBase<TKey, (T1 Value1, T2 Value2, T3 Value3, T4 Value4, T5 Value5)> plan) : base(owner, plan) { }

            /// <summary>Adds the number of records entering each group as the next result value.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, ulong> AddCount() => Add(CreateCountReducer<TKey>());

            /// <summary>Adds a representative validated property from the first record entering each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, TValue> AddProp<TValue>(string path) => Add(Owner.CreateFirstReducer<TKey, TValue>(Owner.CreatePathReader<TValue>(path)));

            /// <summary>Adds a representative expression result from the first record entering each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, TValue> AddCalc<TValue>(Expression<Func<T, TValue>> value) => Add(Owner.CreateFirstReducer<TKey, TValue>(Owner.CreateExpressionReader(value)));

            /// <summary>Adds a representative calculation over a typed source expression.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, TValue> AddCalc<TSource, TValue>(Expression<Func<T, TSource>> source, Func<TSource, TValue> calculation) => Add(Owner.CreateCalculatedFirstReducer<TKey, TSource, TValue>(Owner.CreateExpressionReader(source), calculation));

            /// <summary>Adds a representative calculation over a validated textual source path.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, TValue> AddCalc<TSource, TValue>(string path, Func<TSource, TValue> calculation) => Add(Owner.CreateCalculatedFirstReducer<TKey, TSource, TValue>(Owner.CreatePathReader<TSource>(path), calculation));

            /// <summary>Adds the checked numeric sum for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, TNumber> AddSum<TNumber>(Expression<Func<T, TNumber>> value) where TNumber : struct, INumber<TNumber> => Add(Owner.CreateSumReducer<TKey, TNumber>(Owner.CreateExpressionReader(value)));

            /// <summary>Adds the arithmetic mean as nullable Double for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, double?> AddAverage<TNumber>(Expression<Func<T, TNumber>> value) where TNumber : struct, INumber<TNumber> => Add(Owner.CreateAverageReducer<TKey, TNumber>(Owner.CreateExpressionReader(value)));

            /// <summary>Adds a Decimal-preserving arithmetic mean for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, decimal?> AddAverage(Expression<Func<T, decimal>> value) => Add(Owner.CreateDecimalAverageReducer<TKey>(Owner.CreateExpressionReader(value)));

            /// <summary>Adds the smallest non-null comparable value for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, TValue?> AddMin<TValue>(Expression<Func<T, TValue>> value) where TValue : struct, IComparable<TValue> => Add(Owner.CreateExtremumReducer<TKey, TValue>(Owner.CreateExpressionReader(value), true));

            /// <summary>Adds the largest non-null comparable value for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, TValue?> AddMax<TValue>(Expression<Func<T, TValue>> value) where TValue : struct, IComparable<TValue> => Add(Owner.CreateExtremumReducer<TKey, TValue>(Owner.CreateExpressionReader(value), false));

            /// <summary>Adds a custom streaming reduction over complete records with by-reference state.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, TResult> AddAggregate<TState, TResult>(Func<TState> seed, AggregateStep<TState> accumulate, AggregateFinish<TState, TResult> finish) => Add(Owner.CreateCustomReducer<TKey, TState, TResult>(seed, accumulate, finish));

            /// <summary>Adds a custom streaming reduction over a typed source expression with by-reference state.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, TResult> AddAggregate<TSource, TState, TResult>(Expression<Func<T, TSource>> source, Func<TState> seed, AggregateValueStep<TSource, TState> accumulate, AggregateFinish<TState, TResult> finish) => Add(Owner.CreateCustomValueReducer<TKey, TSource, TState, TResult>(Owner.CreateExpressionReader(source), seed, accumulate, finish));

            /// <summary>Adds a custom streaming reduction over a validated textual source path with by-reference state.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, TResult> AddAggregate<TSource, TState, TResult>(string path, Func<TState> seed, AggregateValueStep<TSource, TState> accumulate, AggregateFinish<TState, TResult> finish) => Add(Owner.CreateCustomValueReducer<TKey, TSource, TState, TResult>(Owner.CreatePathReader<TSource>(path), seed, accumulate, finish));

            /// <summary>Counts structurally distinct non-null values produced inside each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, ulong> AddDistinctCount<TValue>(Expression<Func<T, TValue>> value)
                where TValue : notnull
                => Add(Owner.CreateDistinctCountReducer<TKey, TValue>(Owner.CreateExpressionReader(value)));

            /// <summary>Counts missing or null values at one validated textual path inside each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, ulong> AddNullCount<TValue>(string path)
                => Add(Owner.CreatePresenceCountReducer<TKey, TValue>(Owner.CreatePathReader<TValue>(path), countMissing: true));

            /// <summary>Counts non-null values at one validated textual path inside each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, ulong> AddNonNullCount<TValue>(string path)
                => Add(Owner.CreatePresenceCountReducer<TKey, TValue>(Owner.CreatePathReader<TValue>(path), countMissing: false));

            /// <summary>Returns true when any record in the group satisfies the supplied predicate.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, bool> AddAny(Expression<Func<T, bool>> predicate)
                => Add(Owner.CreateBooleanReducer<TKey>(Owner.CreateExpressionReader(predicate), all: false));

            /// <summary>Returns true when every record in the group satisfies the supplied predicate, including vacuous truth for an empty input.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, bool> AddAll(Expression<Func<T, bool>> predicate)
                => Add(Owner.CreateBooleanReducer<TKey>(Owner.CreateExpressionReader(predicate), all: true));

            /// <summary>Computes population or sample variance with Welford's one-pass recurrence.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, double?> AddVariance<TNumber>(
                Expression<Func<T, TNumber>> value,
                AggregateVarianceMode mode = AggregateVarianceMode.Population)
                where TNumber : struct, INumber<TNumber>
                => Add(Owner.CreateVarianceReducer<TKey, TNumber>(Owner.CreateExpressionReader(value), mode, standardDeviation: false));

            /// <summary>Computes population or sample standard deviation with Welford's one-pass recurrence.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, double?> AddStandardDeviation<TNumber>(
                Expression<Func<T, TNumber>> value,
                AggregateVarianceMode mode = AggregateVarianceMode.Population)
                where TNumber : struct, INumber<TNumber>
                => Add(Owner.CreateVarianceReducer<TKey, TNumber>(Owner.CreateExpressionReader(value), mode, standardDeviation: true));

            /// <summary>Computes a weighted arithmetic mean without retaining contributing rows.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, double?> AddWeightedAverage<TValue, TWeight>(
                Expression<Func<T, TValue>> value,
                Expression<Func<T, TWeight>> weight)
                where TValue : struct, INumber<TValue>
                where TWeight : struct, INumber<TWeight>
                => Add(Owner.CreateWeightedAverageReducer<TKey, TValue, TWeight>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(weight)));

            /// <summary>Returns the associated value from the first record holding the minimum comparison value.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, TAssociated?> AddMinBy<TValue, TAssociated>(
                Expression<Func<T, TValue>> value,
                Expression<Func<T, TAssociated>> associated)
                where TValue : struct, IComparable<TValue>
                where TAssociated : struct
                => Add(Owner.CreateAssociatedExtremumReducer<TKey, TValue, TAssociated>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(associated), minimum: true));

            /// <summary>Returns the associated value from the first record holding the maximum comparison value.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, TAssociated?> AddMaxBy<TValue, TAssociated>(
                Expression<Func<T, TValue>> value,
                Expression<Func<T, TAssociated>> associated)
                where TValue : struct, IComparable<TValue>
                where TAssociated : struct
                => Add(Owner.CreateAssociatedExtremumReducer<TKey, TValue, TAssociated>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(associated), minimum: false));
            private AggregateDefinition<TKey, T1, T2, T3, T4, T5, TValue> Add<TState, TValue>(AggregateReducer<TKey, TState, TValue> reducer)
                => new(Owner, Plan.Append(reducer, static (prior, value) => (prior.Value1, prior.Value2, prior.Value3, prior.Value4, prior.Value5, value)));
        }

        /// <summary>Represents a typed aggregate definition with 6 declared output values.<br/></summary>
        public sealed partial class AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6> : AggregateDefinitionBase<TKey, (T1 Value1, T2 Value2, T3 Value3, T4 Value4, T5 Value5, T6 Value6)>
            where TKey : notnull
        {
            internal AggregateDefinition(AggregateFacade owner, AggregatePlanBase<TKey, (T1 Value1, T2 Value2, T3 Value3, T4 Value4, T5 Value5, T6 Value6)> plan) : base(owner, plan) { }

            /// <summary>Adds the number of records entering each group as the next result value.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, ulong> AddCount() => Add(CreateCountReducer<TKey>());

            /// <summary>Adds a representative validated property from the first record entering each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, TValue> AddProp<TValue>(string path) => Add(Owner.CreateFirstReducer<TKey, TValue>(Owner.CreatePathReader<TValue>(path)));

            /// <summary>Adds a representative expression result from the first record entering each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, TValue> AddCalc<TValue>(Expression<Func<T, TValue>> value) => Add(Owner.CreateFirstReducer<TKey, TValue>(Owner.CreateExpressionReader(value)));

            /// <summary>Adds a representative calculation over a typed source expression.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, TValue> AddCalc<TSource, TValue>(Expression<Func<T, TSource>> source, Func<TSource, TValue> calculation) => Add(Owner.CreateCalculatedFirstReducer<TKey, TSource, TValue>(Owner.CreateExpressionReader(source), calculation));

            /// <summary>Adds a representative calculation over a validated textual source path.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, TValue> AddCalc<TSource, TValue>(string path, Func<TSource, TValue> calculation) => Add(Owner.CreateCalculatedFirstReducer<TKey, TSource, TValue>(Owner.CreatePathReader<TSource>(path), calculation));

            /// <summary>Adds the checked numeric sum for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, TNumber> AddSum<TNumber>(Expression<Func<T, TNumber>> value) where TNumber : struct, INumber<TNumber> => Add(Owner.CreateSumReducer<TKey, TNumber>(Owner.CreateExpressionReader(value)));

            /// <summary>Adds the arithmetic mean as nullable Double for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, double?> AddAverage<TNumber>(Expression<Func<T, TNumber>> value) where TNumber : struct, INumber<TNumber> => Add(Owner.CreateAverageReducer<TKey, TNumber>(Owner.CreateExpressionReader(value)));

            /// <summary>Adds a Decimal-preserving arithmetic mean for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, decimal?> AddAverage(Expression<Func<T, decimal>> value) => Add(Owner.CreateDecimalAverageReducer<TKey>(Owner.CreateExpressionReader(value)));

            /// <summary>Adds the smallest non-null comparable value for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, TValue?> AddMin<TValue>(Expression<Func<T, TValue>> value) where TValue : struct, IComparable<TValue> => Add(Owner.CreateExtremumReducer<TKey, TValue>(Owner.CreateExpressionReader(value), true));

            /// <summary>Adds the largest non-null comparable value for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, TValue?> AddMax<TValue>(Expression<Func<T, TValue>> value) where TValue : struct, IComparable<TValue> => Add(Owner.CreateExtremumReducer<TKey, TValue>(Owner.CreateExpressionReader(value), false));

            /// <summary>Adds a custom streaming reduction over complete records with by-reference state.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, TResult> AddAggregate<TState, TResult>(Func<TState> seed, AggregateStep<TState> accumulate, AggregateFinish<TState, TResult> finish) => Add(Owner.CreateCustomReducer<TKey, TState, TResult>(seed, accumulate, finish));

            /// <summary>Adds a custom streaming reduction over a typed source expression with by-reference state.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, TResult> AddAggregate<TSource, TState, TResult>(Expression<Func<T, TSource>> source, Func<TState> seed, AggregateValueStep<TSource, TState> accumulate, AggregateFinish<TState, TResult> finish) => Add(Owner.CreateCustomValueReducer<TKey, TSource, TState, TResult>(Owner.CreateExpressionReader(source), seed, accumulate, finish));

            /// <summary>Adds a custom streaming reduction over a validated textual source path with by-reference state.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, TResult> AddAggregate<TSource, TState, TResult>(string path, Func<TState> seed, AggregateValueStep<TSource, TState> accumulate, AggregateFinish<TState, TResult> finish) => Add(Owner.CreateCustomValueReducer<TKey, TSource, TState, TResult>(Owner.CreatePathReader<TSource>(path), seed, accumulate, finish));

            /// <summary>Counts structurally distinct non-null values produced inside each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, ulong> AddDistinctCount<TValue>(Expression<Func<T, TValue>> value)
                where TValue : notnull
                => Add(Owner.CreateDistinctCountReducer<TKey, TValue>(Owner.CreateExpressionReader(value)));

            /// <summary>Counts missing or null values at one validated textual path inside each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, ulong> AddNullCount<TValue>(string path)
                => Add(Owner.CreatePresenceCountReducer<TKey, TValue>(Owner.CreatePathReader<TValue>(path), countMissing: true));

            /// <summary>Counts non-null values at one validated textual path inside each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, ulong> AddNonNullCount<TValue>(string path)
                => Add(Owner.CreatePresenceCountReducer<TKey, TValue>(Owner.CreatePathReader<TValue>(path), countMissing: false));

            /// <summary>Returns true when any record in the group satisfies the supplied predicate.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, bool> AddAny(Expression<Func<T, bool>> predicate)
                => Add(Owner.CreateBooleanReducer<TKey>(Owner.CreateExpressionReader(predicate), all: false));

            /// <summary>Returns true when every record in the group satisfies the supplied predicate, including vacuous truth for an empty input.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, bool> AddAll(Expression<Func<T, bool>> predicate)
                => Add(Owner.CreateBooleanReducer<TKey>(Owner.CreateExpressionReader(predicate), all: true));

            /// <summary>Computes population or sample variance with Welford's one-pass recurrence.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, double?> AddVariance<TNumber>(
                Expression<Func<T, TNumber>> value,
                AggregateVarianceMode mode = AggregateVarianceMode.Population)
                where TNumber : struct, INumber<TNumber>
                => Add(Owner.CreateVarianceReducer<TKey, TNumber>(Owner.CreateExpressionReader(value), mode, standardDeviation: false));

            /// <summary>Computes population or sample standard deviation with Welford's one-pass recurrence.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, double?> AddStandardDeviation<TNumber>(
                Expression<Func<T, TNumber>> value,
                AggregateVarianceMode mode = AggregateVarianceMode.Population)
                where TNumber : struct, INumber<TNumber>
                => Add(Owner.CreateVarianceReducer<TKey, TNumber>(Owner.CreateExpressionReader(value), mode, standardDeviation: true));

            /// <summary>Computes a weighted arithmetic mean without retaining contributing rows.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, double?> AddWeightedAverage<TValue, TWeight>(
                Expression<Func<T, TValue>> value,
                Expression<Func<T, TWeight>> weight)
                where TValue : struct, INumber<TValue>
                where TWeight : struct, INumber<TWeight>
                => Add(Owner.CreateWeightedAverageReducer<TKey, TValue, TWeight>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(weight)));

            /// <summary>Returns the associated value from the first record holding the minimum comparison value.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, TAssociated?> AddMinBy<TValue, TAssociated>(
                Expression<Func<T, TValue>> value,
                Expression<Func<T, TAssociated>> associated)
                where TValue : struct, IComparable<TValue>
                where TAssociated : struct
                => Add(Owner.CreateAssociatedExtremumReducer<TKey, TValue, TAssociated>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(associated), minimum: true));

            /// <summary>Returns the associated value from the first record holding the maximum comparison value.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, TAssociated?> AddMaxBy<TValue, TAssociated>(
                Expression<Func<T, TValue>> value,
                Expression<Func<T, TAssociated>> associated)
                where TValue : struct, IComparable<TValue>
                where TAssociated : struct
                => Add(Owner.CreateAssociatedExtremumReducer<TKey, TValue, TAssociated>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(associated), minimum: false));
            private AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, TValue> Add<TState, TValue>(AggregateReducer<TKey, TState, TValue> reducer)
                => new(Owner, Plan.Append(reducer, static (prior, value) => (prior.Value1, prior.Value2, prior.Value3, prior.Value4, prior.Value5, prior.Value6, value)));
        }

        /// <summary>Represents a completed seven-value aggregate result shape.<br/></summary>
        public sealed class AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, T7> : AggregateDefinitionBase<TKey, (T1 Value1, T2 Value2, T3 Value3, T4 Value4, T5 Value5, T6 Value6, T7 Value7)>
            where TKey : notnull
        {
            internal AggregateDefinition(AggregateFacade owner, AggregatePlanBase<TKey, (T1 Value1, T2 Value2, T3 Value3, T4 Value4, T5 Value5, T6 Value6, T7 Value7)> plan) : base(owner, plan) { }
        }

        /// <summary>Opens a typed aggregate reader for a 5-value tuple result definition.<br/></summary>
        public AggregateReader<(T1 Value1, T2 Value2, T3 Value3, T4 Value4, T5 Value5)> Open<TKey, T1, T2, T3, T4, T5>(AggregateDefinition<TKey, T1, T2, T3, T4, T5> definition)
            where TKey : notnull
            => OpenDefinition(definition);

        /// <summary>Opens a typed aggregate reader for a 6-value tuple result definition.<br/></summary>
        public AggregateReader<(T1 Value1, T2 Value2, T3 Value3, T4 Value4, T5 Value5, T6 Value6)> Open<TKey, T1, T2, T3, T4, T5, T6>(AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6> definition)
            where TKey : notnull
            => OpenDefinition(definition);

        /// <summary>Opens a typed aggregate reader for a 7-value tuple result definition.<br/></summary>
        public AggregateReader<(T1 Value1, T2 Value2, T3 Value3, T4 Value4, T5 Value5, T6 Value6, T7 Value7)> Open<TKey, T1, T2, T3, T4, T5, T6, T7>(AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, T7> definition)
            where TKey : notnull
            => OpenDefinition(definition);


        internal struct DistinctState<TValue> where TValue : notnull
        {
            internal HashSet<TValue>? Values;
            internal TValue OrderedValue;
            internal ulong OrderedCount;
            internal bool HasOrderedValue;
        }

        internal struct PresenceState
        {
            internal ulong Matching;
        }

        internal struct BooleanAggregateState
        {
            internal bool Value;
        }

        internal struct VarianceAggregateState
        {
            internal ulong Count;
            internal double Mean;
            internal double M2;
        }

        internal struct WeightedAverageState
        {
            internal double WeightedSum;
            internal double WeightSum;
        }

        internal struct AssociatedExtremumState<TValue, TAssociated>
            where TValue : struct, IComparable<TValue>
            where TAssociated : struct
        {
            internal bool HasValue;
            internal TValue Value;
            internal TAssociated Associated;
        }

        private AggregateReducer<TKey, DistinctState<TValue>, ulong> CreateDistinctCountReducer<TKey, TValue>(PayloadReader<TValue> read)
            where TKey : notnull
            where TValue : notnull
            => new(
                static () => default,
                (ref DistinctState<TValue> state, TKey _, InhetoBinary payload) =>
                {
                    ReadValue<TValue> value = read.Read(payload);
                    if (!value.HasValue || value.Value is null)
                        return;
                    state.Values ??= new HashSet<TValue>();
                    state.Values.Add(value.Value);
                },
                static (in DistinctState<TValue> state, TKey _) => state.Values is null
                    ? state.OrderedCount
                    : checked((ulong)state.Values.Count),
                prepareIndexed: () =>
                {
                    Func<ulong, ReadValue<TValue>>? indexed = PrepareIndexedValueReader(read);
                    return indexed is null
                        ? null
                        : (ref DistinctState<TValue> state, TKey _, ulong identity) =>
                        {
                            ReadValue<TValue> value = indexed(identity);
                            if (!value.HasValue || value.Value is null)
                                return;
                            state.Values ??= new HashSet<TValue>();
                            state.Values.Add(value.Value);
                        };
                },
                prepareComposite: index =>
                {
                    if (!TryGetCompositeValueOrdinal(read, index, out int ordinal))
                        return null;
                    if (ordinal == 1)
                    {
                        return (ref DistinctState<TValue> state, TKey _, AggregateCompositeValues values) =>
                        {
                            ReadValue<TValue> value = ReadCompositeValue<TValue>(values, ordinal);
                            if (!value.HasValue || value.Value is null ||
                                state.HasOrderedValue && EqualityComparer<TValue>.Default.Equals(state.OrderedValue, value.Value))
                            {
                                return;
                            }
                            state.OrderedValue = value.Value;
                            state.OrderedCount = checked(state.OrderedCount + 1);
                            state.HasOrderedValue = true;
                        };
                    }
                    return (ref DistinctState<TValue> state, TKey _, AggregateCompositeValues values) =>
                    {
                        ReadValue<TValue> value = ReadCompositeValue<TValue>(values, ordinal);
                        if (!value.HasValue || value.Value is null)
                            return;
                        state.Values ??= new HashSet<TValue>();
                        state.Values.Add(value.Value);
                    };
                });

        private AggregateReducer<TKey, PresenceState, ulong> CreatePresenceCountReducer<TKey, TValue>(
            PayloadReader<TValue> read,
            bool countMissing)
            where TKey : notnull
            => new(
                static () => default,
                (ref PresenceState state, TKey _, InhetoBinary payload) =>
                {
                    bool hasValue = read.Read(payload).HasValue;
                    if (hasValue != countMissing)
                        state.Matching = checked(state.Matching + 1);
                },
                static (in PresenceState state, TKey _) => state.Matching,
                prepareIndexed: () =>
                {
                    Func<ulong, ReadValue<TValue>>? indexed = PrepareIndexedValueReader(read);
                    return indexed is null
                        ? null
                        : (ref PresenceState state, TKey _, ulong identity) =>
                        {
                            bool hasValue = indexed(identity).HasValue;
                            if (hasValue != countMissing)
                                state.Matching = checked(state.Matching + 1);
                        };
                },
                prepareComposite: index =>
                {
                    if (!TryGetCompositeValueOrdinal(read, index, out int ordinal))
                        return null;
                    return (ref PresenceState state, TKey _, AggregateCompositeValues values) =>
                    {
                        bool hasValue = ReadCompositeValue<TValue>(values, ordinal).HasValue;
                        if (hasValue != countMissing)
                            state.Matching = checked(state.Matching + 1);
                    };
                });

        private AggregateReducer<TKey, BooleanAggregateState, bool> CreateBooleanReducer<TKey>(
            PayloadReader<bool> read,
            bool all)
            where TKey : notnull
            => new(
                () => new BooleanAggregateState { Value = all },
                (ref BooleanAggregateState state, TKey _, InhetoBinary payload) =>
                {
                    ReadValue<bool> value = read.Read(payload);
                    bool candidate = value.HasValue && value.Value;
                    state.Value = all ? state.Value && candidate : state.Value || candidate;
                },
                static (in BooleanAggregateState state, TKey _) => state.Value,
                prepareIndexed: () =>
                {
                    Func<ulong, ReadValue<bool>>? indexed = PrepareIndexedValueReader(read);
                    return indexed is null
                        ? null
                        : (ref BooleanAggregateState state, TKey _, ulong identity) =>
                        {
                            ReadValue<bool> value = indexed(identity);
                            bool candidate = value.HasValue && value.Value;
                            state.Value = all ? state.Value && candidate : state.Value || candidate;
                        };
                },
                prepareComposite: index =>
                {
                    if (!TryGetCompositeValueOrdinal(read, index, out int ordinal))
                        return null;
                    return (ref BooleanAggregateState state, TKey _, AggregateCompositeValues values) =>
                    {
                        ReadValue<bool> value = ReadCompositeValue<bool>(values, ordinal);
                        bool candidate = value.HasValue && value.Value;
                        state.Value = all ? state.Value && candidate : state.Value || candidate;
                    };
                });

        private AggregateReducer<TKey, VarianceAggregateState, double?> CreateVarianceReducer<TKey, TNumber>(
            PayloadReader<TNumber> read,
            AggregateVarianceMode mode,
            bool standardDeviation)
            where TKey : notnull
            where TNumber : struct, INumber<TNumber>
            => new(
                static () => default,
                (ref VarianceAggregateState state, TKey _, InhetoBinary payload) =>
                {
                    ReadValue<TNumber> value = read.Read(payload);
                    if (!value.HasValue)
                        return;
                    double number = double.CreateChecked(value.Value);
                    state.Count = checked(state.Count + 1);
                    double delta = number - state.Mean;
                    state.Mean += delta / state.Count;
                    state.M2 += delta * (number - state.Mean);
                },
                (in VarianceAggregateState state, TKey _) =>
                {
                    if (state.Count == 0 || mode == AggregateVarianceMode.Sample && state.Count < 2)
                        return null;
                    double variance = state.M2 / (mode == AggregateVarianceMode.Sample ? state.Count - 1 : state.Count);
                    return standardDeviation ? Math.Sqrt(variance) : variance;
                },
                prepareIndexed: () =>
                {
                    Func<ulong, ReadValue<TNumber>>? indexed = PrepareIndexedValueReader(read);
                    return indexed is null
                        ? null
                        : (ref VarianceAggregateState state, TKey _, ulong identity) =>
                        {
                            ReadValue<TNumber> value = indexed(identity);
                            if (!value.HasValue)
                                return;
                            double number = double.CreateChecked(value.Value);
                            state.Count = checked(state.Count + 1);
                            double delta = number - state.Mean;
                            state.Mean += delta / state.Count;
                            state.M2 += delta * (number - state.Mean);
                        };
                },
                prepareComposite: index =>
                {
                    if (!TryGetCompositeValueOrdinal(read, index, out int ordinal))
                        return null;
                    return (ref VarianceAggregateState state, TKey _, AggregateCompositeValues values) =>
                    {
                        ReadValue<TNumber> value = ReadCompositeValue<TNumber>(values, ordinal);
                        if (!value.HasValue)
                            return;
                        double number = double.CreateChecked(value.Value);
                        state.Count = checked(state.Count + 1);
                        double delta = number - state.Mean;
                        state.Mean += delta / state.Count;
                        state.M2 += delta * (number - state.Mean);
                    };
                });

        private AggregateReducer<TKey, WeightedAverageState, double?> CreateWeightedAverageReducer<TKey, TValue, TWeight>(
            PayloadReader<TValue> readValue,
            PayloadReader<TWeight> readWeight)
            where TKey : notnull
            where TValue : struct, INumber<TValue>
            where TWeight : struct, INumber<TWeight>
            => new(
                static () => default,
                (ref WeightedAverageState state, TKey _, InhetoBinary payload) =>
                {
                    ReadValue<TValue> value = readValue.Read(payload);
                    ReadValue<TWeight> weight = readWeight.Read(payload);
                    if (!value.HasValue || !weight.HasValue)
                        return;
                    double convertedWeight = double.CreateChecked(weight.Value);
                    state.WeightedSum += double.CreateChecked(value.Value) * convertedWeight;
                    state.WeightSum += convertedWeight;
                },
                static (in WeightedAverageState state, TKey _) =>
                    state.WeightSum == 0d ? null : state.WeightedSum / state.WeightSum,
                prepareIndexed: () =>
                {
                    Func<ulong, ReadValue<TValue>>? indexedValue = PrepareIndexedValueReader(readValue);
                    Func<ulong, ReadValue<TWeight>>? indexedWeight = PrepareIndexedValueReader(readWeight);
                    return indexedValue is null || indexedWeight is null
                        ? null
                        : (ref WeightedAverageState state, TKey _, ulong identity) =>
                        {
                            ReadValue<TValue> value = indexedValue(identity);
                            ReadValue<TWeight> weight = indexedWeight(identity);
                            if (!value.HasValue || !weight.HasValue)
                                return;
                            double convertedWeight = double.CreateChecked(weight.Value);
                            state.WeightedSum += double.CreateChecked(value.Value) * convertedWeight;
                            state.WeightSum += convertedWeight;
                        };
                },
                prepareComposite: index =>
                {
                    if (!TryGetCompositeValueOrdinal(readValue, index, out int valueOrdinal) ||
                        !TryGetCompositeValueOrdinal(readWeight, index, out int weightOrdinal))
                    {
                        return null;
                    }
                    return (ref WeightedAverageState state, TKey _, AggregateCompositeValues values) =>
                    {
                        ReadValue<TValue> value = ReadCompositeValue<TValue>(values, valueOrdinal);
                        ReadValue<TWeight> weight = ReadCompositeValue<TWeight>(values, weightOrdinal);
                        if (!value.HasValue || !weight.HasValue)
                            return;
                        double convertedWeight = double.CreateChecked(weight.Value);
                        state.WeightedSum += double.CreateChecked(value.Value) * convertedWeight;
                        state.WeightSum += convertedWeight;
                    };
                });

        private AggregateReducer<TKey, AssociatedExtremumState<TValue, TAssociated>, TAssociated?> CreateAssociatedExtremumReducer<TKey, TValue, TAssociated>(
            PayloadReader<TValue> readValue,
            PayloadReader<TAssociated> readAssociated,
            bool minimum)
            where TKey : notnull
            where TValue : struct, IComparable<TValue>
            where TAssociated : struct
            => new(
                static () => default,
                (ref AssociatedExtremumState<TValue, TAssociated> state, TKey _, InhetoBinary payload) =>
                {
                    ReadValue<TValue> value = readValue.Read(payload);
                    ReadValue<TAssociated> associated = readAssociated.Read(payload);
                    if (!value.HasValue || !associated.HasValue)
                        return;
                    if (!state.HasValue || (minimum ? value.Value.CompareTo(state.Value) < 0 : value.Value.CompareTo(state.Value) > 0))
                    {
                        state.HasValue = true;
                        state.Value = value.Value;
                        state.Associated = associated.Value;
                    }
                },
                static (in AssociatedExtremumState<TValue, TAssociated> state, TKey _) =>
                    state.HasValue ? state.Associated : null,
                prepareIndexed: () =>
                {
                    Func<ulong, ReadValue<TValue>>? indexedValue = PrepareIndexedValueReader(readValue);
                    Func<ulong, ReadValue<TAssociated>>? indexedAssociated = PrepareIndexedValueReader(readAssociated);
                    return indexedValue is null || indexedAssociated is null
                        ? null
                        : (ref AssociatedExtremumState<TValue, TAssociated> state, TKey _, ulong identity) =>
                        {
                            ReadValue<TValue> value = indexedValue(identity);
                            ReadValue<TAssociated> associated = indexedAssociated(identity);
                            if (!value.HasValue || !associated.HasValue)
                                return;
                            if (!state.HasValue || (minimum ? value.Value.CompareTo(state.Value) < 0 : value.Value.CompareTo(state.Value) > 0))
                            {
                                state.HasValue = true;
                                state.Value = value.Value;
                                state.Associated = associated.Value;
                            }
                        };
                });
    }
}
