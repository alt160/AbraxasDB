using System.Linq.Expressions;
using System.Numerics;
using LibraDex;

namespace AbraxasDB;

public partial class RecordStore<T>
{
    /// <summary>
    /// Gets the strongly typed aggregate surface over all projected values.<br/>
    /// Aggregate calls execute immediately; conditions select records before the supplied value selector runs.<br/>
    /// </summary>
    public AggregateFacade Aggregates => new(this, distinctValues: false);

    /// <summary>
    /// Gets the strongly typed aggregate surface over distinct projected values.<br/>
    /// Distinctness applies to the value selected inside the aggregate; it never deduplicates records by an unrelated key.<br/>
    /// </summary>
    public AggregateFacade Distinct => new(this, distinctValues: true);

    /// <summary>
    /// Provides one coherent aggregate grammar over ordinary or distinct projected values.<br/>
    /// Instances are immutable lightweight facades; retaining one does not retain query results or mutable execution state.<br/>
    /// </summary>
    public sealed partial class AggregateFacade
    {
        private readonly RecordStore<T> store;
        private readonly bool distinctValues;

        /// <summary>
        /// Initializes an aggregate facade bound to one record store and value multiplicity policy.<br/>
        /// </summary>
        /// <param name="store">The owning record store.<br/></param>
        /// <param name="distinctValues">Whether duplicate projected values are ignored.<br/></param>
        internal AggregateFacade(RecordStore<T> store, bool distinctValues)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.distinctValues = distinctValues;
        }

        /// <summary>
        /// Counts records selected by an optional condition.<br/>
        /// On the <see cref="RecordStore{T}.Distinct"/> facade this overload still counts records because no value projection exists to deduplicate.<br/>
        /// </summary>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>The number of selected records.<br/></returns>
        public ulong Count(Condition<T>.EndCondition? where = null)
        {
            if (where is null)
                return store.CountAggregateRecordIdentities();
            return store.CountWhere(where);
        }

        /// <summary>
        /// Counts non-null projected values selected by an optional condition.<br/>
        /// The distinct facade counts unique projected values using <see cref="EqualityComparer{T}.Default"/>.<br/>
        /// </summary>
        /// <typeparam name="TValue">The projected value type.<br/></typeparam>
        /// <param name="selector">The value selector.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>The contributing value count after the facade's multiplicity policy.<br/></returns>
        public ulong Count<TValue>(
            Expression<Func<T, TValue?>> selector,
            Condition<T>.EndCondition? where = null)
            where TValue : struct
        {
            ArgumentNullException.ThrowIfNull(selector);
            if (store.TryCountProjectedByLibraDex<TValue>(selector, where, distinctValues, out ulong indexedCount))
                return indexedCount;
            return CountProjectedValues(selector.Compile(), where);
        }

        /// <summary>
        /// Counts projected values selected by an optional condition.<br/>
        /// The distinct facade counts unique projected values using <see cref="EqualityComparer{T}.Default"/>.<br/>
        /// </summary>
        /// <typeparam name="TValue">The projected value type.<br/></typeparam>
        /// <param name="selector">The value selector.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>The contributing value count after the facade's multiplicity policy.<br/></returns>
        public ulong Count<TValue>(
            Expression<Func<T, TValue>> selector,
            Condition<T>.EndCondition? where = null)
            where TValue : notnull
        {
            ArgumentNullException.ThrowIfNull(selector);
            if (store.TryCountProjectedByLibraDex<TValue>(selector, where, distinctValues, out ulong indexedCount))
                return indexedCount;
            Func<T, TValue> compiled = selector.Compile();
            return CountProjectedValues(record => compiled(record), where);
        }

        /// <summary>
        /// Counts unique non-null projected values selected by an optional condition.<br/>
        /// This convenience is equivalent to calling <c>store.Distinct.Count(selector, where)</c> regardless of which facade receives it.<br/>
        /// </summary>
        /// <typeparam name="TValue">The projected value type.<br/></typeparam>
        /// <param name="selector">The value selector.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>The number of unique contributing values.<br/></returns>
        public ulong DistinctCount<TValue>(
            Expression<Func<T, TValue?>> selector,
            Condition<T>.EndCondition? where = null)
            where TValue : struct
            => new AggregateFacade(store, distinctValues: true).Count(selector, where);

        /// <summary>
        /// Counts unique projected values selected by an optional condition.<br/>
        /// </summary>
        /// <typeparam name="TValue">The non-null projected value type.<br/></typeparam>
        /// <param name="selector">The value selector.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>The number of unique projected values.<br/></returns>
        public ulong DistinctCount<TValue>(
            Expression<Func<T, TValue>> selector,
            Condition<T>.EndCondition? where = null)
            where TValue : notnull
            => new AggregateFacade(store, distinctValues: true).Count(selector, where);

        /// <summary>
        /// Sums projected numeric values with checked arithmetic in the selected numeric type.<br/>
        /// Dedicated narrow-number overloads widen their accumulator to match the documented low-friction result contract.<br/>
        /// </summary>
        /// <typeparam name="TNumber">The selected numeric type and sum result type.<br/></typeparam>
        /// <param name="selector">The numeric value selector.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>The checked sum, or numeric zero when no value contributes.<br/></returns>
        public TNumber Sum<TNumber>(
            Expression<Func<T, TNumber>> selector,
            Condition<T>.EndCondition? where = null)
            where TNumber : struct, INumber<TNumber>
            => Compute<TNumber, TNumber, double>(Lift(selector), where, captureValues: false).Sum;

        /// <inheritdoc cref="Sum{TNumber}(Expression{Func{T, TNumber}}, Condition{T}.EndCondition?)"/>
        public long Sum(Expression<Func<T, sbyte>> selector, Condition<T>.EndCondition? where = null)
            => Compute<sbyte, long, double>(Lift(selector), where, captureValues: false).Sum;

        /// <inheritdoc cref="Sum{TNumber}(Expression{Func{T, TNumber}}, Condition{T}.EndCondition?)"/>
        public ulong Sum(Expression<Func<T, byte>> selector, Condition<T>.EndCondition? where = null)
            => Compute<byte, ulong, double>(Lift(selector), where, captureValues: false).Sum;

        /// <inheritdoc cref="Sum{TNumber}(Expression{Func{T, TNumber}}, Condition{T}.EndCondition?)"/>
        public long Sum(Expression<Func<T, short>> selector, Condition<T>.EndCondition? where = null)
            => Compute<short, long, double>(Lift(selector), where, captureValues: false).Sum;

        /// <inheritdoc cref="Sum{TNumber}(Expression{Func{T, TNumber}}, Condition{T}.EndCondition?)"/>
        public ulong Sum(Expression<Func<T, ushort>> selector, Condition<T>.EndCondition? where = null)
            => Compute<ushort, ulong, double>(Lift(selector), where, captureValues: false).Sum;

        /// <inheritdoc cref="Sum{TNumber}(Expression{Func{T, TNumber}}, Condition{T}.EndCondition?)"/>
        public long Sum(Expression<Func<T, int>> selector, Condition<T>.EndCondition? where = null)
            => Compute<int, long, double>(Lift(selector), where, captureValues: false).Sum;

        /// <inheritdoc cref="Sum{TNumber}(Expression{Func{T, TNumber}}, Condition{T}.EndCondition?)"/>
        public ulong Sum(Expression<Func<T, uint>> selector, Condition<T>.EndCondition? where = null)
            => Compute<uint, ulong, double>(Lift(selector), where, captureValues: false).Sum;

        /// <inheritdoc cref="Sum{TNumber}(Expression{Func{T, TNumber}}, Condition{T}.EndCondition?)"/>
        public double Sum(Expression<Func<T, float>> selector, Condition<T>.EndCondition? where = null)
            => Compute<float, double, double>(Lift(selector), where, captureValues: false).Sum;

        /// <summary>
        /// Computes the arithmetic average of projected numeric values using a running mean.<br/>
        /// Integral, binary floating-point, and other non-Decimal numeric selectors return <see cref="double"/> so fractional results are never truncated.<br/>
        /// </summary>
        /// <typeparam name="TNumber">The selected numeric type.<br/></typeparam>
        /// <param name="selector">The numeric value selector.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>The arithmetic average, or null when no value contributes.<br/></returns>
        public double? Average<TNumber>(
            Expression<Func<T, TNumber>> selector,
            Condition<T>.EndCondition? where = null)
            where TNumber : struct, INumber<TNumber>
            => Compute<TNumber, TNumber, double>(Lift(selector), where, captureValues: false).Average;

        /// <summary>
        /// Computes a Decimal-preserving arithmetic average of projected Decimal values.<br/>
        /// </summary>
        /// <param name="selector">The Decimal value selector.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>The arithmetic average, or null when no value contributes.<br/></returns>
        public decimal? Average(
            Expression<Func<T, decimal>> selector,
            Condition<T>.EndCondition? where = null)
            => Compute<decimal, decimal, decimal>(Lift(selector), where, captureValues: false).Average;

        /// <summary>
        /// Returns the smallest projected value selected by an optional condition.<br/>
        /// </summary>
        /// <typeparam name="TValue">The comparable projected value type.<br/></typeparam>
        /// <param name="selector">The value selector.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>The minimum value, or null when no value contributes.<br/></returns>
        public TValue? Min<TValue>(
            Expression<Func<T, TValue>> selector,
            Condition<T>.EndCondition? where = null)
            where TValue : struct, INumber<TValue>
            => Compute<TValue, TValue, double>(Lift(selector), where, captureValues: false).Minimum;

        /// <summary>
        /// Returns the largest projected value selected by an optional condition.<br/>
        /// </summary>
        /// <typeparam name="TValue">The comparable projected value type.<br/></typeparam>
        /// <param name="selector">The value selector.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>The maximum value, or null when no value contributes.<br/></returns>
        public TValue? Max<TValue>(
            Expression<Func<T, TValue>> selector,
            Condition<T>.EndCondition? where = null)
            where TValue : struct, INumber<TValue>
            => Compute<TValue, TValue, double>(Lift(selector), where, captureValues: false).Maximum;

        /// <summary>
        /// Computes count, missing count, sum, average, extrema, and both population and sample dispersion in one pass.<br/>
        /// The generic form retains the selected type for Sum and uses Double for the arithmetic average.<br/>
        /// </summary>
        /// <typeparam name="TNumber">The selected numeric and Sum result type.<br/></typeparam>
        /// <param name="selector">The numeric value selector.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>A complete numeric statistics snapshot.<br/></returns>
        public AggregateStatistics<TNumber, TNumber, double> Statistics<TNumber>(
            Expression<Func<T, TNumber>> selector,
            Condition<T>.EndCondition? where = null)
            where TNumber : struct, INumber<TNumber>
            => ToStatistics(Compute<TNumber, TNumber, double>(Lift(selector), where, captureValues: false, countMissing: true));

        /// <summary>
        /// Computes a Decimal-preserving statistics snapshot in one pass.<br/>
        /// Variance and standard-deviation fields remain Double because their square-root result is not generally representable as Decimal without a separate approximation contract.<br/>
        /// </summary>
        /// <param name="selector">The Decimal value selector.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>A Decimal Sum/Average statistics snapshot with Double dispersion values.<br/></returns>
        public AggregateStatistics<decimal, decimal, decimal> Statistics(
            Expression<Func<T, decimal>> selector,
            Condition<T>.EndCondition? where = null)
            => ToStatistics(Compute<decimal, decimal, decimal>(Lift(selector), where, captureValues: false, countMissing: true));

        /// <summary>
        /// Computes population variance using Welford's stable one-pass recurrence.<br/>
        /// </summary>
        /// <typeparam name="TNumber">The selected numeric type.<br/></typeparam>
        /// <param name="selector">The numeric value selector.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>Population variance, or null when no value contributes.<br/></returns>
        public double? PopulationVariance<TNumber>(
            Expression<Func<T, TNumber>> selector,
            Condition<T>.EndCondition? where = null)
            where TNumber : struct, INumber<TNumber>
            => Compute<TNumber, TNumber, double>(Lift(selector), where, captureValues: false).PopulationVariance;

        /// <summary>
        /// Computes sample variance using Bessel's correction.<br/>
        /// </summary>
        /// <typeparam name="TNumber">The selected numeric type.<br/></typeparam>
        /// <param name="selector">The numeric value selector.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>Sample variance, or null when fewer than two values contribute.<br/></returns>
        public double? SampleVariance<TNumber>(
            Expression<Func<T, TNumber>> selector,
            Condition<T>.EndCondition? where = null)
            where TNumber : struct, INumber<TNumber>
            => Compute<TNumber, TNumber, double>(Lift(selector), where, captureValues: false).SampleVariance;

        /// <summary>
        /// Computes population standard deviation as the square root of population variance.<br/>
        /// </summary>
        /// <typeparam name="TNumber">The selected numeric type.<br/></typeparam>
        /// <param name="selector">The numeric value selector.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>Population standard deviation, or null when no value contributes.<br/></returns>
        public double? PopulationStandardDeviation<TNumber>(
            Expression<Func<T, TNumber>> selector,
            Condition<T>.EndCondition? where = null)
            where TNumber : struct, INumber<TNumber>
        {
            double? variance = PopulationVariance(selector, where);
            return variance.HasValue ? Math.Sqrt(variance.Value) : null;
        }

        /// <summary>
        /// Computes sample standard deviation as the square root of sample variance.<br/>
        /// </summary>
        /// <typeparam name="TNumber">The selected numeric type.<br/></typeparam>
        /// <param name="selector">The numeric value selector.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>Sample standard deviation, or null when fewer than two values contribute.<br/></returns>
        public double? SampleStandardDeviation<TNumber>(
            Expression<Func<T, TNumber>> selector,
            Condition<T>.EndCondition? where = null)
            where TNumber : struct, INumber<TNumber>
        {
            double? variance = SampleVariance(selector, where);
            return variance.HasValue ? Math.Sqrt(variance.Value) : null;
        }

        /// <summary>
        /// Removes a floored fraction of sorted values from each tail and averages the retained positions.<br/>
        /// Equal boundary values are removed by position, not wholesale by value.<br/>
        /// </summary>
        /// <typeparam name="TNumber">The selected numeric type.<br/></typeparam>
        /// <param name="selector">The numeric value selector.<br/></param>
        /// <param name="trimFractionPerTail">Fraction removed independently from each tail, from zero through one half.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>The retained arithmetic mean, or null when no value remains.<br/></returns>
        public double? TrimmedMean<TNumber>(
            Expression<Func<T, TNumber>> selector,
            double trimFractionPerTail,
            Condition<T>.EndCondition? where = null)
            where TNumber : struct, INumber<TNumber>
        {
            ValidateTrimFraction(trimFractionPerTail);
            NumericAggregateSnapshot<TNumber, TNumber, double> snapshot =
                Compute<TNumber, TNumber, double>(Lift(selector), where, captureValues: true);
            int trimCount = checked((int)Math.Floor(snapshot.Values.Count * trimFractionPerTail));
            return CalculateTrimmedMean<TNumber, double>(snapshot.Values, trimCount);
        }

        /// <summary>
        /// Removes an exact number of sorted positions from each tail and averages the retained values.<br/>
        /// </summary>
        /// <typeparam name="TNumber">The selected numeric type.<br/></typeparam>
        /// <param name="selector">The numeric value selector.<br/></param>
        /// <param name="trimCountPerTail">The non-negative number of positions removed from each tail.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>The retained arithmetic mean, or null when no value remains.<br/></returns>
        public double? TrimmedMean<TNumber>(
            Expression<Func<T, TNumber>> selector,
            int trimCountPerTail,
            Condition<T>.EndCondition? where = null)
            where TNumber : struct, INumber<TNumber>
        {
            ValidateTrimCount(trimCountPerTail);
            NumericAggregateSnapshot<TNumber, TNumber, double> snapshot =
                Compute<TNumber, TNumber, double>(Lift(selector), where, captureValues: true);
            return CalculateTrimmedMean<TNumber, double>(snapshot.Values, trimCountPerTail);
        }

        /// <summary>
        /// Creates a single-key grouped aggregate stage over records selected by an optional condition.<br/>
        /// Group order follows the first appearance of each key in the selected record stream.<br/>
        /// </summary>
        /// <typeparam name="TKey">The grouping-key type.<br/></typeparam>
        /// <param name="keySelector">The single grouping-key selector.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>A grouped aggregate facade retaining this facade's ordinary or distinct value policy.<br/></returns>
        public GroupedAggregateFacade<TKey> GroupByResults<TKey>(
            Expression<Func<T, TKey>> keySelector,
            Condition<T>.EndCondition? where = null)
            where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(keySelector);
            return new GroupedAggregateFacade<TKey>(store, keySelector, where, distinctValues);
        }

        /// <summary>
        /// Creates a grouped aggregate stage whose string key is read directly from authoritative Inheto payloads.<br/>
        /// Direct and durably computed string selectors share the same backend-neutral semantics used by conditions and grouping drill-down.<br/>
        /// </summary>
        /// <param name="keySelector">Direct or computed string grouping selector.<br/></param>
        /// <param name="where">Optional source-record condition.<br/></param>
        /// <returns>A grouped aggregate facade retaining this facade's ordinary or distinct value policy.<br/></returns>
        public GroupedAggregateFacade<string> GroupByResults(
            Selector<T>.StringSelector keySelector,
            Condition<T>.EndCondition? where = null)
            => new(store, keySelector, where, distinctValues);

        /// <summary>
        /// Creates a grouped aggregate stage whose Boolean key is read directly from authoritative Inheto payloads.<br/>
        /// </summary>
        /// <param name="keySelector">Direct or computed Boolean grouping selector.<br/></param>
        /// <param name="where">Optional source-record condition.<br/></param>
        /// <returns>A grouped aggregate facade retaining this facade's ordinary or distinct value policy.<br/></returns>
        public GroupedAggregateFacade<bool> GroupByResults(
            Selector<T>.BooleanSelector keySelector,
            Condition<T>.EndCondition? where = null)
            => new(store, keySelector, where, distinctValues);

        /// <summary>
        /// Creates a grouped aggregate stage whose numeric key is read directly from authoritative Inheto payloads.<br/>
        /// </summary>
        /// <typeparam name="TKey">Exact numeric selector result type.<br/></typeparam>
        /// <param name="keySelector">Direct or computed numeric grouping selector.<br/></param>
        /// <param name="where">Optional source-record condition.<br/></param>
        /// <returns>A grouped aggregate facade retaining this facade's ordinary or distinct value policy.<br/></returns>
        public GroupedAggregateFacade<TKey> GroupByResults<TKey>(
            Selector<T>.NumericSelector<TKey> keySelector,
            Condition<T>.EndCondition? where = null)
            where TKey : struct, INumber<TKey>
            => new(store, keySelector, where, distinctValues);

        /// <summary>
        /// Creates a grouped aggregate stage whose Decimal key is read directly from authoritative Inheto payloads.<br/>
        /// </summary>
        public GroupedAggregateFacade<decimal> GroupByResults(
            Selector<T>.DecimalSelector keySelector,
            Condition<T>.EndCondition? where = null)
            => new(store, keySelector, where, distinctValues);

        /// <summary>
        /// Creates a grouped aggregate stage whose Guid key is read directly from authoritative Inheto payloads.<br/>
        /// </summary>
        public GroupedAggregateFacade<Guid> GroupByResults(
            Selector<T>.GuidSelector keySelector,
            Condition<T>.EndCondition? where = null)
            => new(store, keySelector, where, distinctValues);

        /// <summary>
        /// Creates a grouped aggregate stage whose DateTime key is read directly from authoritative Inheto payloads.<br/>
        /// </summary>
        public GroupedAggregateFacade<DateTime> GroupByResults(
            Selector<T>.DateTimeSelector keySelector,
            Condition<T>.EndCondition? where = null)
            => new(store, keySelector, where, distinctValues);

        /// <summary>
        /// Creates a grouped aggregate stage whose DateOnly key is read directly from authoritative Inheto payloads.<br/>
        /// </summary>
        public GroupedAggregateFacade<DateOnly> GroupByResults(
            Selector<T>.DateOnlySelector keySelector,
            Condition<T>.EndCondition? where = null)
            => new(store, keySelector, where, distinctValues);

        /// <summary>
        /// Creates a grouped aggregate stage whose TimeOnly key is read directly from authoritative Inheto payloads.<br/>
        /// </summary>
        public GroupedAggregateFacade<TimeOnly> GroupByResults(
            Selector<T>.TimeOnlySelector keySelector,
            Condition<T>.EndCondition? where = null)
            => new(store, keySelector, where, distinctValues);

        /// <summary>
        /// Creates a grouped aggregate stage whose TimeSpan key is read directly from authoritative Inheto payloads.<br/>
        /// </summary>
        public GroupedAggregateFacade<TimeSpan> GroupByResults(
            Selector<T>.TimeSpanSelector keySelector,
            Condition<T>.EndCondition? where = null)
            => new(store, keySelector, where, distinctValues);

        /// <summary>
        /// Compiles one numeric projection and executes the shared one-pass aggregate kernel over selected records.<br/>
        /// </summary>
        private NumericAggregateSnapshot<TValue, TSum, TMean> Compute<TValue, TSum, TMean>(
            Expression<Func<T, TValue?>> selector,
            Condition<T>.EndCondition? where,
            bool captureValues,
            bool countMissing = false)
            where TValue : struct, INumber<TValue>
            where TSum : struct, INumber<TSum>
            where TMean : struct, INumber<TMean>
        {
            ArgumentNullException.ThrowIfNull(selector);
            if (store.TryComputeNumericByLibraDex<TValue, TSum, TMean>(
                selector,
                where,
                distinctValues,
                captureValues,
                countMissing,
                out NumericAggregateSnapshot<TValue, TSum, TMean> indexed))
            {
                return indexed;
            }

            return ComputeNumeric<TValue, TSum, TMean>(
                store.IterateAggregateRecords(where),
                selector.Compile(),
                distinctValues,
                captureValues);
        }

        /// <summary>
        /// Counts non-null projected values, optionally suppressing duplicate projected values.<br/>
        /// </summary>
        private ulong CountProjectedValues<TValue>(
            Func<T, TValue?> selector,
            Condition<T>.EndCondition? where)
        {
            HashSet<TValue>? distinct = distinctValues ? new HashSet<TValue>() : null;
            ulong count = 0;
            foreach (T record in store.IterateAggregateRecords(where))
            {
                TValue? value = selector(record);
                if (value is null || (distinct is not null && !distinct.Add(value)))
                    continue;

                count = checked(count + 1);
            }

            return count;
        }

        /// <summary>
        /// Sums nullable projected numeric values while ignoring missing values.<br/>
        /// The result uses checked arithmetic in the selected numeric type and is zero when no value contributes.<br/>
        /// </summary>
        /// <typeparam name="TNumber">The selected numeric type and sum result type.<br/></typeparam>
        /// <param name="selector">The nullable numeric value selector.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>The checked sum of contributing values.<br/></returns>
        public TNumber Sum<TNumber>(
            Expression<Func<T, TNumber?>> selector,
            Condition<T>.EndCondition? where = null)
            where TNumber : struct, INumber<TNumber>
            => Compute<TNumber, TNumber, double>(selector, where, captureValues: false).Sum;

        /// <inheritdoc cref="Sum{TNumber}(Expression{Func{T, Nullable{TNumber}}}, Condition{T}.EndCondition?)"/>
        public long Sum(Expression<Func<T, sbyte?>> selector, Condition<T>.EndCondition? where = null)
            => Compute<sbyte, long, double>(selector, where, captureValues: false).Sum;

        /// <inheritdoc cref="Sum{TNumber}(Expression{Func{T, Nullable{TNumber}}}, Condition{T}.EndCondition?)"/>
        public ulong Sum(Expression<Func<T, byte?>> selector, Condition<T>.EndCondition? where = null)
            => Compute<byte, ulong, double>(selector, where, captureValues: false).Sum;

        /// <inheritdoc cref="Sum{TNumber}(Expression{Func{T, Nullable{TNumber}}}, Condition{T}.EndCondition?)"/>
        public long Sum(Expression<Func<T, short?>> selector, Condition<T>.EndCondition? where = null)
            => Compute<short, long, double>(selector, where, captureValues: false).Sum;

        /// <inheritdoc cref="Sum{TNumber}(Expression{Func{T, Nullable{TNumber}}}, Condition{T}.EndCondition?)"/>
        public ulong Sum(Expression<Func<T, ushort?>> selector, Condition<T>.EndCondition? where = null)
            => Compute<ushort, ulong, double>(selector, where, captureValues: false).Sum;

        /// <inheritdoc cref="Sum{TNumber}(Expression{Func{T, Nullable{TNumber}}}, Condition{T}.EndCondition?)"/>
        public long Sum(Expression<Func<T, int?>> selector, Condition<T>.EndCondition? where = null)
            => Compute<int, long, double>(selector, where, captureValues: false).Sum;

        /// <inheritdoc cref="Sum{TNumber}(Expression{Func{T, Nullable{TNumber}}}, Condition{T}.EndCondition?)"/>
        public ulong Sum(Expression<Func<T, uint?>> selector, Condition<T>.EndCondition? where = null)
            => Compute<uint, ulong, double>(selector, where, captureValues: false).Sum;

        /// <inheritdoc cref="Sum{TNumber}(Expression{Func{T, Nullable{TNumber}}}, Condition{T}.EndCondition?)"/>
        public double Sum(Expression<Func<T, float?>> selector, Condition<T>.EndCondition? where = null)
            => Compute<float, double, double>(selector, where, captureValues: false).Sum;

        /// <summary>
        /// Computes the arithmetic average of nullable projected numeric values while ignoring missing values.<br/>
        /// Integral and binary floating-point selectors return Double so fractional results are retained.<br/>
        /// </summary>
        /// <typeparam name="TNumber">The selected numeric type.<br/></typeparam>
        /// <param name="selector">The nullable numeric value selector.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>The arithmetic average, or null when no value contributes.<br/></returns>
        public double? Average<TNumber>(
            Expression<Func<T, TNumber?>> selector,
            Condition<T>.EndCondition? where = null)
            where TNumber : struct, INumber<TNumber>
            => Compute<TNumber, TNumber, double>(selector, where, captureValues: false).Average;

        /// <summary>
        /// Computes a Decimal-preserving average of nullable projected Decimal values while ignoring missing values.<br/>
        /// </summary>
        /// <param name="selector">The nullable Decimal value selector.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>The arithmetic average, or null when no value contributes.<br/></returns>
        public decimal? Average(
            Expression<Func<T, decimal?>> selector,
            Condition<T>.EndCondition? where = null)
            => Compute<decimal, decimal, decimal>(selector, where, captureValues: false).Average;

        /// <summary>
        /// Returns the smallest non-null projected numeric value.<br/>
        /// </summary>
        /// <typeparam name="TNumber">The selected numeric type.<br/></typeparam>
        /// <param name="selector">The nullable numeric value selector.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>The minimum contributing value, or null when no value contributes.<br/></returns>
        public TNumber? Min<TNumber>(
            Expression<Func<T, TNumber?>> selector,
            Condition<T>.EndCondition? where = null)
            where TNumber : struct, INumber<TNumber>
            => Compute<TNumber, TNumber, double>(selector, where, captureValues: false).Minimum;

        /// <summary>
        /// Returns the largest non-null projected numeric value.<br/>
        /// </summary>
        /// <typeparam name="TNumber">The selected numeric type.<br/></typeparam>
        /// <param name="selector">The nullable numeric value selector.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>The maximum contributing value, or null when no value contributes.<br/></returns>
        public TNumber? Max<TNumber>(
            Expression<Func<T, TNumber?>> selector,
            Condition<T>.EndCondition? where = null)
            where TNumber : struct, INumber<TNumber>
            => Compute<TNumber, TNumber, double>(selector, where, captureValues: false).Maximum;

        /// <summary>
        /// Computes a complete numeric statistics snapshot from nullable projected values.<br/>
        /// Missing values are excluded from numeric fields and included in <see cref="AggregateStatistics{TValue, TSum, TMean}.MissingCount"/>.<br/>
        /// </summary>
        /// <typeparam name="TNumber">The selected numeric and sum result type.<br/></typeparam>
        /// <param name="selector">The nullable numeric value selector.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>A one-pass statistics snapshot.<br/></returns>
        public AggregateStatistics<TNumber, TNumber, double> Statistics<TNumber>(
            Expression<Func<T, TNumber?>> selector,
            Condition<T>.EndCondition? where = null)
            where TNumber : struct, INumber<TNumber>
            => ToStatistics(Compute<TNumber, TNumber, double>(selector, where, captureValues: false, countMissing: true));

        /// <summary>
        /// Computes a Decimal-preserving statistics snapshot from nullable projected Decimal values.<br/>
        /// </summary>
        /// <param name="selector">The nullable Decimal value selector.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>A one-pass Decimal Sum/Average statistics snapshot.<br/></returns>
        public AggregateStatistics<decimal, decimal, decimal> Statistics(
            Expression<Func<T, decimal?>> selector,
            Condition<T>.EndCondition? where = null)
            => ToStatistics(Compute<decimal, decimal, decimal>(selector, where, captureValues: false, countMissing: true));

        /// <summary>
        /// Removes a floored fraction of sorted Decimal positions from each tail and averages the retained values.<br/>
        /// Decimal arithmetic is preserved in the returned mean.<br/>
        /// </summary>
        /// <param name="selector">The Decimal value selector.<br/></param>
        /// <param name="trimFractionPerTail">Fraction removed independently from each tail, from zero through one half.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>The retained Decimal mean, or null when no value remains.<br/></returns>
        public decimal? TrimmedMean(
            Expression<Func<T, decimal>> selector,
            double trimFractionPerTail,
            Condition<T>.EndCondition? where = null)
        {
            ValidateTrimFraction(trimFractionPerTail);
            NumericAggregateSnapshot<decimal, decimal, decimal> snapshot =
                Compute<decimal, decimal, decimal>(Lift(selector), where, captureValues: true);
            int trimCount = checked((int)Math.Floor(snapshot.Values.Count * trimFractionPerTail));
            return CalculateTrimmedMean<decimal, decimal>(snapshot.Values, trimCount);
        }

        /// <summary>
        /// Removes an exact number of sorted Decimal positions from each tail and averages the retained values.<br/>
        /// </summary>
        /// <param name="selector">The Decimal value selector.<br/></param>
        /// <param name="trimCountPerTail">The non-negative number of positions removed from each tail.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <returns>The retained Decimal mean, or null when no value remains.<br/></returns>
        public decimal? TrimmedMean(
            Expression<Func<T, decimal>> selector,
            int trimCountPerTail,
            Condition<T>.EndCondition? where = null)
        {
            ValidateTrimCount(trimCountPerTail);
            NumericAggregateSnapshot<decimal, decimal, decimal> snapshot =
                Compute<decimal, decimal, decimal>(Lift(selector), where, captureValues: true);
            return CalculateTrimmedMean<decimal, decimal>(snapshot.Values, trimCountPerTail);
        }

        /// <summary>
        /// Counts valid values produced by one direct or backend-neutral computed selector.<br/>
        /// The ordinary facade counts every valid projection; the distinct facade counts structurally distinct non-null projections and treats byte arrays by content.<br/>
        /// </summary>
        /// <param name="selector">Selector-builder value whose complete computation is evaluated from authoritative Inheto payloads.<br/></param>
        /// <param name="where">Optional condition selecting source records before projection.<br/></param>
        /// <returns>The contributing ordinary or distinct value count.<br/></returns>
        public ulong Count(ISelector selector, Condition<T>.EndCondition? where = null)
        {
            ArgumentNullException.ThrowIfNull(selector);
            using var lifecycle = store.EnterStoreOperationScope();
            if (store.TryCountNativeSelector(selector, where, distinctValues, out ulong indexedCount)) return indexedCount;
            if (!store.TryCreateStoreSelectorReader(selector, out var readValue))
            {
                throw new NotSupportedException(
                    "Aggregate selector counting requires a direct property or a computed selector with complete backend-neutral semantics.");
            }

            HashSet<object?>? distinct = distinctValues
                ? new HashSet<object?>(DirectGroupingScalarEqualityComparer.Instance)
                : null;
            ulong count = 0;
            foreach (Inheto.InhetoBinary payload in store.IterateAuthoritativeGroupingPayloads(where, nameof(Count)))
            {
                var candidate = readValue(payload);
                if (!candidate.HasValue || candidate.Value is null ||
                    (distinct is not null && !distinct.Add(candidate.Value)))
                {
                    continue;
                }
                count = checked(count + 1);
            }
            return count;
        }

        /// <summary>
        /// Counts distinct valid values produced by one direct or backend-neutral computed selector regardless of the current facade's multiplicity mode.<br/>
        /// </summary>
        /// <param name="selector">Selector-builder value to project before deduplication.<br/></param>
        /// <param name="where">Optional condition selecting source records.<br/></param>
        /// <returns>The number of structurally distinct non-null projected values.<br/></returns>
        public ulong DistinctCount(ISelector selector, Condition<T>.EndCondition? where = null)
            => new AggregateFacade(store, distinctValues: true).Count(selector, where);

        /// <summary>
        /// Sums one numeric selector using checked arithmetic in its exact result type.<br/>
        /// The distinct facade suppresses duplicate computed values before accumulation.<br/>
        /// </summary>
        /// <typeparam name="TNumber">Exact selector result and sum type.<br/></typeparam>
        /// <param name="selector">Direct or computed numeric selector.<br/></param>
        /// <param name="where">Optional source-record condition.<br/></param>
        /// <returns>The checked sum, or zero when no value contributes.<br/></returns>
        public TNumber Sum<TNumber>(
            Selector<T>.NumericSelector<TNumber> selector,
            Condition<T>.EndCondition? where = null)
            where TNumber : struct, INumber<TNumber>
            => ComputeSelector<TNumber, TNumber, double>(selector, where, captureValues: false).Sum;

        /// <summary>Uses a widened signed accumulator for an SByte selector.<br/></summary>
        public long Sum(Selector<T>.IntegralSelector<sbyte> selector, Condition<T>.EndCondition? where = null)
            => ComputeSelector<sbyte, long, double>(selector, where, captureValues: false).Sum;

        /// <summary>Uses a widened unsigned accumulator for a Byte selector.<br/></summary>
        public ulong Sum(Selector<T>.IntegralSelector<byte> selector, Condition<T>.EndCondition? where = null)
            => ComputeSelector<byte, ulong, double>(selector, where, captureValues: false).Sum;

        /// <summary>Uses a widened signed accumulator for an Int16 selector.<br/></summary>
        public long Sum(Selector<T>.IntegralSelector<short> selector, Condition<T>.EndCondition? where = null)
            => ComputeSelector<short, long, double>(selector, where, captureValues: false).Sum;

        /// <summary>Uses a widened unsigned accumulator for a UInt16 selector.<br/></summary>
        public ulong Sum(Selector<T>.IntegralSelector<ushort> selector, Condition<T>.EndCondition? where = null)
            => ComputeSelector<ushort, ulong, double>(selector, where, captureValues: false).Sum;

        /// <summary>Uses a widened signed accumulator for an Int32 selector.<br/></summary>
        public long Sum(Selector<T>.IntegralSelector<int> selector, Condition<T>.EndCondition? where = null)
            => ComputeSelector<int, long, double>(selector, where, captureValues: false).Sum;

        /// <summary>Uses a widened unsigned accumulator for a UInt32 selector.<br/></summary>
        public ulong Sum(Selector<T>.IntegralSelector<uint> selector, Condition<T>.EndCondition? where = null)
            => ComputeSelector<uint, ulong, double>(selector, where, captureValues: false).Sum;

        /// <summary>Uses a widened Double accumulator for a Single selector.<br/></summary>
        public double Sum(Selector<T>.RealSelector<float> selector, Condition<T>.EndCondition? where = null)
            => ComputeSelector<float, double, double>(selector, where, captureValues: false).Sum;

        /// <summary>
        /// Sums one Decimal selector without binary-floating conversion.<br/>
        /// </summary>
        /// <param name="selector">Direct or computed Decimal selector.<br/></param>
        /// <param name="where">Optional source-record condition.<br/></param>
        /// <returns>The checked Decimal sum, or zero when no value contributes.<br/></returns>
        public decimal Sum(Selector<T>.DecimalSelector selector, Condition<T>.EndCondition? where = null)
            => ComputeSelector<decimal, decimal, decimal>(selector, where, captureValues: false).Sum;

        /// <summary>
        /// Averages one numeric selector as Double so integral and binary-floating inputs retain fractional results.<br/>
        /// </summary>
        public double? Average<TNumber>(
            Selector<T>.NumericSelector<TNumber> selector,
            Condition<T>.EndCondition? where = null)
            where TNumber : struct, INumber<TNumber>
            => ComputeSelector<TNumber, TNumber, double>(selector, where, captureValues: false).Average;

        /// <summary>Averages one Decimal selector without binary-floating conversion.<br/></summary>
        public decimal? Average(Selector<T>.DecimalSelector selector, Condition<T>.EndCondition? where = null)
            => ComputeSelector<decimal, decimal, decimal>(selector, where, captureValues: false).Average;

        /// <summary>Returns the smallest valid value produced by one numeric selector.<br/></summary>
        public TNumber? Min<TNumber>(
            Selector<T>.NumericSelector<TNumber> selector,
            Condition<T>.EndCondition? where = null)
            where TNumber : struct, INumber<TNumber>
            => ComputeSelector<TNumber, TNumber, double>(selector, where, captureValues: false).Minimum;

        /// <summary>Returns the largest valid value produced by one numeric selector.<br/></summary>
        public TNumber? Max<TNumber>(
            Selector<T>.NumericSelector<TNumber> selector,
            Condition<T>.EndCondition? where = null)
            where TNumber : struct, INumber<TNumber>
            => ComputeSelector<TNumber, TNumber, double>(selector, where, captureValues: false).Maximum;

        /// <summary>Returns the smallest valid Decimal selector value.<br/></summary>
        public decimal? Min(Selector<T>.DecimalSelector selector, Condition<T>.EndCondition? where = null)
            => ComputeSelector<decimal, decimal, decimal>(selector, where, captureValues: false).Minimum;

        /// <summary>Returns the largest valid Decimal selector value.<br/></summary>
        public decimal? Max(Selector<T>.DecimalSelector selector, Condition<T>.EndCondition? where = null)
            => ComputeSelector<decimal, decimal, decimal>(selector, where, captureValues: false).Maximum;

        /// <summary>Computes count, missing count, sum, average, extrema, and dispersion for one numeric selector in one pass.<br/></summary>
        public AggregateStatistics<TNumber, TNumber, double> Statistics<TNumber>(
            Selector<T>.NumericSelector<TNumber> selector,
            Condition<T>.EndCondition? where = null)
            where TNumber : struct, INumber<TNumber>
            => ToStatistics(ComputeSelector<TNumber, TNumber, double>(selector, where, captureValues: false));

        /// <summary>Computes a Decimal-preserving statistics snapshot for one Decimal selector in one pass.<br/></summary>
        public AggregateStatistics<decimal, decimal, decimal> Statistics(
            Selector<T>.DecimalSelector selector,
            Condition<T>.EndCondition? where = null)
            => ToStatistics(ComputeSelector<decimal, decimal, decimal>(selector, where, captureValues: false));

        /// <summary>Computes population variance for one numeric selector using Welford's recurrence.<br/></summary>
        public double? PopulationVariance<TNumber>(Selector<T>.NumericSelector<TNumber> selector, Condition<T>.EndCondition? where = null)
            where TNumber : struct, INumber<TNumber>
            => ComputeSelector<TNumber, TNumber, double>(selector, where, captureValues: false).PopulationVariance;

        /// <summary>Computes sample variance for one numeric selector using Bessel's correction.<br/></summary>
        public double? SampleVariance<TNumber>(Selector<T>.NumericSelector<TNumber> selector, Condition<T>.EndCondition? where = null)
            where TNumber : struct, INumber<TNumber>
            => ComputeSelector<TNumber, TNumber, double>(selector, where, captureValues: false).SampleVariance;

        /// <summary>Computes population standard deviation for one numeric selector.<br/></summary>
        public double? PopulationStandardDeviation<TNumber>(Selector<T>.NumericSelector<TNumber> selector, Condition<T>.EndCondition? where = null)
            where TNumber : struct, INumber<TNumber>
        {
            double? variance = PopulationVariance(selector, where);
            return variance.HasValue ? Math.Sqrt(variance.Value) : null;
        }

        /// <summary>Computes sample standard deviation for one numeric selector.<br/></summary>
        public double? SampleStandardDeviation<TNumber>(Selector<T>.NumericSelector<TNumber> selector, Condition<T>.EndCondition? where = null)
            where TNumber : struct, INumber<TNumber>
        {
            double? variance = SampleVariance(selector, where);
            return variance.HasValue ? Math.Sqrt(variance.Value) : null;
        }

        /// <summary>Removes a floored fraction of selector values from each sorted tail and averages the retained positions.<br/></summary>
        public double? TrimmedMean<TNumber>(
            Selector<T>.NumericSelector<TNumber> selector,
            double trimFractionPerTail,
            Condition<T>.EndCondition? where = null)
            where TNumber : struct, INumber<TNumber>
        {
            ValidateTrimFraction(trimFractionPerTail);
            var snapshot = ComputeSelector<TNumber, TNumber, double>(selector, where, captureValues: true);
            int trimCount = checked((int)Math.Floor(snapshot.Values.Count * trimFractionPerTail));
            return CalculateTrimmedMean<TNumber, double>(snapshot.Values, trimCount);
        }

        /// <summary>Removes an exact number of selector values from each sorted tail and averages the retained positions.<br/></summary>
        public double? TrimmedMean<TNumber>(
            Selector<T>.NumericSelector<TNumber> selector,
            int trimCountPerTail,
            Condition<T>.EndCondition? where = null)
            where TNumber : struct, INumber<TNumber>
        {
            ValidateTrimCount(trimCountPerTail);
            var snapshot = ComputeSelector<TNumber, TNumber, double>(selector, where, captureValues: true);
            return CalculateTrimmedMean<TNumber, double>(snapshot.Values, trimCountPerTail);
        }

        /// <summary>Removes a floored fraction of Decimal selector values from each sorted tail and preserves Decimal averaging.<br/></summary>
        public decimal? TrimmedMean(Selector<T>.DecimalSelector selector, double trimFractionPerTail, Condition<T>.EndCondition? where = null)
        {
            ValidateTrimFraction(trimFractionPerTail);
            var snapshot = ComputeSelector<decimal, decimal, decimal>(selector, where, captureValues: true);
            int trimCount = checked((int)Math.Floor(snapshot.Values.Count * trimFractionPerTail));
            return CalculateTrimmedMean<decimal, decimal>(snapshot.Values, trimCount);
        }

        /// <summary>Removes an exact number of Decimal selector values from each sorted tail and preserves Decimal averaging.<br/></summary>
        public decimal? TrimmedMean(Selector<T>.DecimalSelector selector, int trimCountPerTail, Condition<T>.EndCondition? where = null)
        {
            ValidateTrimCount(trimCountPerTail);
            var snapshot = ComputeSelector<decimal, decimal, decimal>(selector, where, captureValues: true);
            return CalculateTrimmedMean<decimal, decimal>(snapshot.Values, trimCountPerTail);
        }

        /// <summary>
        /// Executes one exact typed selector through the shared numeric kernel over authoritative planned payloads.<br/>
        /// The selector reader is built once before traversal; missing or invalid computations become nullable projections and therefore share lambda aggregate semantics.<br/>
        /// </summary>
        private NumericAggregateSnapshot<TValue, TSum, TMean> ComputeSelector<TValue, TSum, TMean>(
            ISelector selector,
            Condition<T>.EndCondition? where,
            bool captureValues)
            where TValue : struct, INumber<TValue>
            where TSum : struct, INumber<TSum>
            where TMean : struct, INumber<TMean>
        {
            ArgumentNullException.ThrowIfNull(selector);
            using var lifecycle = store.EnterStoreOperationScope();
            if (store.TryComputeNamedSelectorByLibraDex<TValue, TSum, TMean>(
                    selector,
                    where,
                    distinctValues,
                    captureValues,
                    out NumericAggregateSnapshot<TValue, TSum, TMean> indexed))
            {
                return indexed;
            }
            if (!store.TryCreateStoreSelectorReader<TValue>(selector, out var readValue))
            {
                throw new NotSupportedException(
                    $"Aggregate selector result must be an exact {typeof(TValue).Name} value with complete backend-neutral semantics.");
            }

            return ComputeNumericCore<Inheto.InhetoBinary, TValue, TSum, TMean>(
                store.IterateAuthoritativeGroupingPayloads(where, nameof(ComputeSelector)),
                payload =>
                {
                    var candidate = readValue(payload);
                    return candidate.HasValue ? candidate.Value : null;
                },
                distinctValues,
                captureValues);
        }
    }

    /// <summary>
    /// Provides the six core aggregates within each value of one grouping key.<br/>
    /// The stage is immutable and enumerates selected records once per terminal aggregate call.<br/>
    /// </summary>
    /// <typeparam name="TKey">The non-null grouping-key type.<br/></typeparam>
    public sealed class GroupedAggregateFacade<TKey>
        where TKey : notnull
    {
        private readonly RecordStore<T> store;
        private readonly Expression<Func<T, TKey>>? keySelectorExpression;
        private readonly Func<T, TKey>? keySelector;
        private readonly Func<Inheto.InhetoBinary, (bool HasValue, TKey Value)>? payloadKeySelector;
        private readonly Condition<T>.EndCondition? where;
        private readonly bool distinctValues;

        /// <summary>
        /// Initializes a grouped aggregate stage.<br/>
        /// </summary>
        /// <param name="store">The owning record store.<br/></param>
        /// <param name="keySelector">The compiled grouping-key selector.<br/></param>
        /// <param name="where">The optional record condition.<br/></param>
        /// <param name="distinctValues">Whether terminal value projections ignore duplicates within each group.<br/></param>
        internal GroupedAggregateFacade(
            RecordStore<T> store,
            Expression<Func<T, TKey>> keySelector,
            Condition<T>.EndCondition? where,
            bool distinctValues)
        {
            this.store = store;
            keySelectorExpression = keySelector;
            this.keySelector = keySelector.Compile();
            this.where = where;
            this.distinctValues = distinctValues;
        }

        /// <summary>
        /// Initializes a grouped aggregate stage from one exact typed backend-neutral selector.<br/>
        /// The reusable payload reader is constructed once so direct and computed key evaluation performs no reflection or selector rendering per record.<br/>
        /// </summary>
        /// <param name="store">Owning record store.<br/></param>
        /// <param name="keySelector">Direct or computed selector whose exact result is <typeparamref name="TKey"/>.<br/></param>
        /// <param name="where">Optional source-record condition.<br/></param>
        /// <param name="distinctValues">Whether terminal value projections ignore duplicates within each group.<br/></param>
        internal GroupedAggregateFacade(
            RecordStore<T> store,
            ISelector keySelector,
            Condition<T>.EndCondition? where,
            bool distinctValues)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            ArgumentNullException.ThrowIfNull(keySelector);
            if (!store.TryCreateStoreSelectorReader<TKey>(keySelector, out payloadKeySelector))
            {
                throw new NotSupportedException(
                    $"Grouped aggregate key selector must produce an exact {typeof(TKey).Name} value with complete backend-neutral semantics.");
            }
            this.where = where;
            this.distinctValues = distinctValues;
        }

        /// <summary>
        /// Counts selected records in each group.<br/>
        /// </summary>
        /// <returns>One group key and record count row per encountered group.<br/></returns>
        public IReadOnlyList<AggregateGroup<TKey, ulong>> Count()
        {
            if (keySelectorExpression is not null &&
                store.TryCountGroupsByLibraDex(keySelectorExpression, where, out IReadOnlyList<AggregateGroup<TKey, ulong>> indexed))
                return indexed;

            Dictionary<TKey, ulong> counts = new();
            if (payloadKeySelector is not null)
            {
                using var lifecycle = store.EnterStoreOperationScope();
                foreach (Inheto.InhetoBinary payload in store.IterateAuthoritativeGroupingPayloads(where, nameof(Count)))
                {
                    TKey key = ReadPayloadKey(payload);
                    counts.TryGetValue(key, out ulong count);
                    counts[key] = checked(count + 1);
                }
                return counts.Select(static pair => new AggregateGroup<TKey, ulong>(pair.Key, pair.Value)).ToArray();
            }

            foreach (T record in store.IterateAggregateRecords(where))
            {
                TKey key = keySelector!(record) ?? throw new InvalidOperationException("Grouped aggregate keys cannot be null.");
                counts.TryGetValue(key, out ulong count);
                counts[key] = checked(count + 1);
            }

            return counts.Select(static pair => new AggregateGroup<TKey, ulong>(pair.Key, pair.Value)).ToArray();
        }

        /// <summary>
        /// Sums projected numeric values independently inside each group.<br/>
        /// Checked arithmetic uses the selected numeric type; callers selecting narrow integers should project a widened type when group totals may exceed that type.<br/>
        /// </summary>
        /// <typeparam name="TNumber">The selected numeric and sum result type.<br/></typeparam>
        /// <param name="selector">The grouped numeric value selector.<br/></param>
        /// <returns>One group key and sum row per encountered group.<br/></returns>
        public IReadOnlyList<AggregateGroup<TKey, TNumber>> Sum<TNumber>(Expression<Func<T, TNumber>> selector)
            where TNumber : struct, INumber<TNumber>
            => ComputeGroups<TNumber, TNumber, double>(Lift(selector))
                .Select(static group => new AggregateGroup<TKey, TNumber>(group.Key, group.Snapshot.Sum))
                .ToArray();

        /// <summary>
        /// Computes the arithmetic average of projected numeric values independently inside each group.<br/>
        /// </summary>
        /// <typeparam name="TNumber">The selected numeric type.<br/></typeparam>
        /// <param name="selector">The grouped numeric value selector.<br/></param>
        /// <returns>One group key and nullable Double average row per encountered group.<br/></returns>
        public IReadOnlyList<AggregateGroup<TKey, double?>> Average<TNumber>(Expression<Func<T, TNumber>> selector)
            where TNumber : struct, INumber<TNumber>
            => ComputeGroups<TNumber, TNumber, double>(Lift(selector))
                .Select(static group => new AggregateGroup<TKey, double?>(group.Key, group.Snapshot.Average))
                .ToArray();

        /// <summary>
        /// Computes a Decimal-preserving arithmetic average independently inside each group.<br/>
        /// </summary>
        /// <param name="selector">The grouped Decimal value selector.<br/></param>
        /// <returns>One group key and nullable Decimal average row per encountered group.<br/></returns>
        public IReadOnlyList<AggregateGroup<TKey, decimal?>> Average(Expression<Func<T, decimal>> selector)
            => ComputeGroups<decimal, decimal, decimal>(Lift(selector))
                .Select(static group => new AggregateGroup<TKey, decimal?>(group.Key, group.Snapshot.Average))
                .ToArray();

        /// <summary>
        /// Selects the minimum projected numeric value independently inside each group.<br/>
        /// </summary>
        /// <typeparam name="TNumber">The selected numeric type.<br/></typeparam>
        /// <param name="selector">The grouped numeric value selector.<br/></param>
        /// <returns>One group key and nullable minimum row per encountered group.<br/></returns>
        public IReadOnlyList<AggregateGroup<TKey, TNumber?>> Min<TNumber>(Expression<Func<T, TNumber>> selector)
            where TNumber : struct, INumber<TNumber>
            => ComputeGroups<TNumber, TNumber, double>(Lift(selector))
                .Select(static group => new AggregateGroup<TKey, TNumber?>(group.Key, group.Snapshot.Minimum))
                .ToArray();

        /// <summary>
        /// Selects the maximum projected numeric value independently inside each group.<br/>
        /// </summary>
        /// <typeparam name="TNumber">The selected numeric type.<br/></typeparam>
        /// <param name="selector">The grouped numeric value selector.<br/></param>
        /// <returns>One group key and nullable maximum row per encountered group.<br/></returns>
        public IReadOnlyList<AggregateGroup<TKey, TNumber?>> Max<TNumber>(Expression<Func<T, TNumber>> selector)
            where TNumber : struct, INumber<TNumber>
            => ComputeGroups<TNumber, TNumber, double>(Lift(selector))
                .Select(static group => new AggregateGroup<TKey, TNumber?>(group.Key, group.Snapshot.Maximum))
                .ToArray();

        /// <summary>
        /// Counts unique projected values independently inside each group.<br/>
        /// </summary>
        /// <typeparam name="TValue">The projected value type.<br/></typeparam>
        /// <param name="selector">The grouped value selector.<br/></param>
        /// <returns>One group key and distinct contributing-value count row per encountered group.<br/></returns>
        public IReadOnlyList<AggregateGroup<TKey, ulong>> DistinctCount<TValue>(Expression<Func<T, TValue>> selector)
            where TValue : struct, INumber<TValue>
        {
            return ComputeGroups<TValue, TValue, double>(Lift(selector), distinctOverride: true)
                .Select(static group => new AggregateGroup<TKey, ulong>(group.Key, group.Snapshot.Count))
                .ToArray();
        }

        /// <summary>
        /// Sums one direct or computed numeric selector independently inside each group.<br/>
        /// When both key and value are selectors, authoritative Inheto payloads are projected without hydrating complete records.<br/>
        /// </summary>
        public IReadOnlyList<AggregateGroup<TKey, TNumber>> Sum<TNumber>(Selector<T>.NumericSelector<TNumber> selector)
            where TNumber : struct, INumber<TNumber>
            => ComputeSelectorGroups<TNumber, TNumber, double>(selector)
                .Select(static group => new AggregateGroup<TKey, TNumber>(group.Key, group.Snapshot.Sum))
                .ToArray();

        /// <summary>Uses widened signed sums for SByte selector values within each group.<br/></summary>
        public IReadOnlyList<AggregateGroup<TKey, long>> Sum(Selector<T>.IntegralSelector<sbyte> selector)
            => ProjectGroupSums(ComputeSelectorGroups<sbyte, long, double>(selector));

        /// <summary>Uses widened unsigned sums for Byte selector values within each group.<br/></summary>
        public IReadOnlyList<AggregateGroup<TKey, ulong>> Sum(Selector<T>.IntegralSelector<byte> selector)
            => ProjectGroupSums(ComputeSelectorGroups<byte, ulong, double>(selector));

        /// <summary>Uses widened signed sums for Int16 selector values within each group.<br/></summary>
        public IReadOnlyList<AggregateGroup<TKey, long>> Sum(Selector<T>.IntegralSelector<short> selector)
            => ProjectGroupSums(ComputeSelectorGroups<short, long, double>(selector));

        /// <summary>Uses widened unsigned sums for UInt16 selector values within each group.<br/></summary>
        public IReadOnlyList<AggregateGroup<TKey, ulong>> Sum(Selector<T>.IntegralSelector<ushort> selector)
            => ProjectGroupSums(ComputeSelectorGroups<ushort, ulong, double>(selector));

        /// <summary>Uses widened signed sums for Int32 selector values within each group.<br/></summary>
        public IReadOnlyList<AggregateGroup<TKey, long>> Sum(Selector<T>.IntegralSelector<int> selector)
            => ProjectGroupSums(ComputeSelectorGroups<int, long, double>(selector));

        /// <summary>Uses widened unsigned sums for UInt32 selector values within each group.<br/></summary>
        public IReadOnlyList<AggregateGroup<TKey, ulong>> Sum(Selector<T>.IntegralSelector<uint> selector)
            => ProjectGroupSums(ComputeSelectorGroups<uint, ulong, double>(selector));

        /// <summary>Uses widened Double sums for Single selector values within each group.<br/></summary>
        public IReadOnlyList<AggregateGroup<TKey, double>> Sum(Selector<T>.RealSelector<float> selector)
            => ProjectGroupSums(ComputeSelectorGroups<float, double, double>(selector));

        /// <summary>Preserves Decimal sums for Decimal selector values within each group.<br/></summary>
        public IReadOnlyList<AggregateGroup<TKey, decimal>> Sum(Selector<T>.DecimalSelector selector)
            => ProjectGroupSums(ComputeSelectorGroups<decimal, decimal, decimal>(selector));

        /// <summary>Computes a Double arithmetic average for one numeric selector within each group.<br/></summary>
        public IReadOnlyList<AggregateGroup<TKey, double?>> Average<TNumber>(Selector<T>.NumericSelector<TNumber> selector)
            where TNumber : struct, INumber<TNumber>
            => ComputeSelectorGroups<TNumber, TNumber, double>(selector)
                .Select(static group => new AggregateGroup<TKey, double?>(group.Key, group.Snapshot.Average))
                .ToArray();

        /// <summary>Preserves Decimal arithmetic averages for one Decimal selector within each group.<br/></summary>
        public IReadOnlyList<AggregateGroup<TKey, decimal?>> Average(Selector<T>.DecimalSelector selector)
            => ComputeSelectorGroups<decimal, decimal, decimal>(selector)
                .Select(static group => new AggregateGroup<TKey, decimal?>(group.Key, group.Snapshot.Average))
                .ToArray();

        /// <summary>Returns the smallest valid numeric selector value within each group.<br/></summary>
        public IReadOnlyList<AggregateGroup<TKey, TNumber?>> Min<TNumber>(Selector<T>.NumericSelector<TNumber> selector)
            where TNumber : struct, INumber<TNumber>
            => ComputeSelectorGroups<TNumber, TNumber, double>(selector)
                .Select(static group => new AggregateGroup<TKey, TNumber?>(group.Key, group.Snapshot.Minimum))
                .ToArray();

        /// <summary>Returns the largest valid numeric selector value within each group.<br/></summary>
        public IReadOnlyList<AggregateGroup<TKey, TNumber?>> Max<TNumber>(Selector<T>.NumericSelector<TNumber> selector)
            where TNumber : struct, INumber<TNumber>
            => ComputeSelectorGroups<TNumber, TNumber, double>(selector)
                .Select(static group => new AggregateGroup<TKey, TNumber?>(group.Key, group.Snapshot.Maximum))
                .ToArray();

        /// <summary>Returns the smallest valid Decimal selector value within each group.<br/></summary>
        public IReadOnlyList<AggregateGroup<TKey, decimal?>> Min(Selector<T>.DecimalSelector selector)
            => ComputeSelectorGroups<decimal, decimal, decimal>(selector)
                .Select(static group => new AggregateGroup<TKey, decimal?>(group.Key, group.Snapshot.Minimum))
                .ToArray();

        /// <summary>Returns the largest valid Decimal selector value within each group.<br/></summary>
        public IReadOnlyList<AggregateGroup<TKey, decimal?>> Max(Selector<T>.DecimalSelector selector)
            => ComputeSelectorGroups<decimal, decimal, decimal>(selector)
                .Select(static group => new AggregateGroup<TKey, decimal?>(group.Key, group.Snapshot.Maximum))
                .ToArray();

        /// <summary>
        /// Counts structurally distinct non-null values produced by one direct or computed selector within each group.<br/>
        /// Byte-array values use content equality, matching the top-level distinct facade and backend-neutral grouping contract.<br/>
        /// </summary>
        public IReadOnlyList<AggregateGroup<TKey, ulong>> DistinctCount(ISelector selector)
        {
            ArgumentNullException.ThrowIfNull(selector);
            if (!store.TryCreateStoreSelectorReader(selector, out var valueReader))
                throw new NotSupportedException("Grouped distinct counting requires complete backend-neutral selector semantics.");

            using var lifecycle = store.EnterStoreOperationScope();
            var buckets = new Dictionary<TKey, HashSet<object?>>();
            var ordered = new List<TKey>();
            foreach (Inheto.InhetoBinary payload in store.IterateAuthoritativeGroupingPayloads(where, nameof(DistinctCount)))
            {
                TKey key = ReadAnyPayloadKey(payload);
                if (!buckets.TryGetValue(key, out HashSet<object?>? values))
                {
                    values = new HashSet<object?>(DirectGroupingScalarEqualityComparer.Instance);
                    buckets.Add(key, values);
                    ordered.Add(key);
                }
                (bool HasValue, object? Value) candidate = valueReader(payload);
                if (candidate.HasValue && candidate.Value is not null)
                    values.Add(candidate.Value);
            }
            return ordered
                .Select(key => new AggregateGroup<TKey, ulong>(key, checked((ulong)buckets[key].Count)))
                .ToArray();
        }

        /// <summary>
        /// Lazily yields one selected-record count per grouping key.<br/>
        /// A compatible single-key LibraDex index yields completed groups in ascending index order with only current-group transition state; unsupported shapes retain the established authoritative fallback and yield after its required grouping state is complete.<br/>
        /// Unlike <see cref="Count"/>, this terminal is intended for visible developer filtering and early termination without allocating a complete aggregate result list.<br/>
        /// </summary>
        /// <returns>A deferred group-count stream whose enumeration owns the complete store-operation lifetime.<br/></returns>
        public IEnumerable<AggregateGroup<TKey, ulong>> IterateCount()
        {
            if (keySelectorExpression is not null &&
                store.TryIterateCountGroupsByLibraDex(keySelectorExpression, where, out IEnumerable<AggregateGroup<TKey, ulong>> indexed))
            {
                return indexed;
            }

            return IterateFallback();

            IEnumerable<AggregateGroup<TKey, ulong>> IterateFallback()
            {
                foreach (AggregateGroup<TKey, ulong> group in Count())
                    yield return group;
            }
        }

        /// <summary>
        /// Lazily yields the checked sum of one numeric expression within each grouping key.<br/>
        /// Compatible grouping/value indexes reduce adjacent groups in ascending grouping-index order; unsupported expressions use the authoritative grouped reducer and then expose its finalized rows as a deferred stream.<br/>
        /// </summary>
        /// <typeparam name="TNumber">The selected numeric and sum result type.<br/></typeparam>
        /// <param name="selector">The grouped numeric value expression.<br/></param>
        /// <returns>A deferred grouped-sum stream.<br/></returns>
        public IEnumerable<AggregateGroup<TKey, TNumber>> IterateSum<TNumber>(Expression<Func<T, TNumber>> selector)
            where TNumber : struct, INumber<TNumber>
            => IterateExpressionGroups<TNumber, TNumber, double, TNumber>(Lift(selector), static snapshot => snapshot.Sum);

        /// <summary>
        /// Lazily yields the arithmetic average of one numeric expression within each grouping key.<br/>
        /// Integral and binary-floating inputs produce nullable Double results so fractional values are preserved.<br/>
        /// </summary>
        /// <typeparam name="TNumber">The selected numeric type.<br/></typeparam>
        /// <param name="selector">The grouped numeric value expression.<br/></param>
        /// <returns>A deferred grouped-average stream.<br/></returns>
        public IEnumerable<AggregateGroup<TKey, double?>> IterateAverage<TNumber>(Expression<Func<T, TNumber>> selector)
            where TNumber : struct, INumber<TNumber>
            => IterateExpressionGroups<TNumber, TNumber, double, double?>(Lift(selector), static snapshot => snapshot.Average);

        /// <summary>
        /// Lazily yields the Decimal-preserving arithmetic average within each grouping key.<br/>
        /// </summary>
        /// <param name="selector">The grouped Decimal value expression.<br/></param>
        /// <returns>A deferred grouped-average stream.<br/></returns>
        public IEnumerable<AggregateGroup<TKey, decimal?>> IterateAverage(Expression<Func<T, decimal>> selector)
            => IterateExpressionGroups<decimal, decimal, decimal, decimal?>(Lift(selector), static snapshot => snapshot.Average);

        /// <summary>
        /// Lazily yields the smallest numeric expression value within each grouping key.<br/>
        /// </summary>
        /// <typeparam name="TNumber">The selected numeric type.<br/></typeparam>
        /// <param name="selector">The grouped numeric value expression.<br/></param>
        /// <returns>A deferred grouped-minimum stream.<br/></returns>
        public IEnumerable<AggregateGroup<TKey, TNumber?>> IterateMin<TNumber>(Expression<Func<T, TNumber>> selector)
            where TNumber : struct, INumber<TNumber>
            => IterateExpressionGroups<TNumber, TNumber, double, TNumber?>(Lift(selector), static snapshot => snapshot.Minimum);

        /// <summary>
        /// Lazily yields the largest numeric expression value within each grouping key.<br/>
        /// </summary>
        /// <typeparam name="TNumber">The selected numeric type.<br/></typeparam>
        /// <param name="selector">The grouped numeric value expression.<br/></param>
        /// <returns>A deferred grouped-maximum stream.<br/></returns>
        public IEnumerable<AggregateGroup<TKey, TNumber?>> IterateMax<TNumber>(Expression<Func<T, TNumber>> selector)
            where TNumber : struct, INumber<TNumber>
            => IterateExpressionGroups<TNumber, TNumber, double, TNumber?>(Lift(selector), static snapshot => snapshot.Maximum);

        /// <summary>
        /// Lazily yields the count of distinct numeric expression values within each grouping key.<br/>
        /// Distinct state is bounded to the current group on the compatible LibraDex path.<br/>
        /// </summary>
        /// <typeparam name="TValue">The selected numeric type.<br/></typeparam>
        /// <param name="selector">The grouped numeric value expression.<br/></param>
        /// <returns>A deferred grouped-distinct-count stream.<br/></returns>
        public IEnumerable<AggregateGroup<TKey, ulong>> IterateDistinctCount<TValue>(Expression<Func<T, TValue>> selector)
            where TValue : struct, INumber<TValue>
            => IterateExpressionGroups<TValue, TValue, double, ulong>(Lift(selector), static snapshot => snapshot.Count, distinctOverride: true);

        /// <summary>
        /// Lazily yields checked grouped sums from one direct or computed typed numeric selector.<br/>
        /// Selector-key plus selector-value fallback remains payload-native and avoids complete-record hydration.<br/>
        /// </summary>
        /// <typeparam name="TNumber">The selected numeric and sum result type.<br/></typeparam>
        /// <param name="selector">The grouped numeric selector.<br/></param>
        /// <returns>A deferred grouped-sum stream.<br/></returns>
        public IEnumerable<AggregateGroup<TKey, TNumber>> IterateSum<TNumber>(Selector<T>.NumericSelector<TNumber> selector)
            where TNumber : struct, INumber<TNumber>
            => IterateSelectorGroups<TNumber, TNumber, double, TNumber>(selector, static snapshot => snapshot.Sum);

        /// <summary>Uses widened signed sums while lazily yielding grouped SByte selector results.<br/></summary>
        public IEnumerable<AggregateGroup<TKey, long>> IterateSum(Selector<T>.IntegralSelector<sbyte> selector)
            => IterateSelectorGroups<sbyte, long, double, long>(selector, static snapshot => snapshot.Sum);

        /// <summary>Uses widened unsigned sums while lazily yielding grouped Byte selector results.<br/></summary>
        public IEnumerable<AggregateGroup<TKey, ulong>> IterateSum(Selector<T>.IntegralSelector<byte> selector)
            => IterateSelectorGroups<byte, ulong, double, ulong>(selector, static snapshot => snapshot.Sum);

        /// <summary>Uses widened signed sums while lazily yielding grouped Int16 selector results.<br/></summary>
        public IEnumerable<AggregateGroup<TKey, long>> IterateSum(Selector<T>.IntegralSelector<short> selector)
            => IterateSelectorGroups<short, long, double, long>(selector, static snapshot => snapshot.Sum);

        /// <summary>Uses widened unsigned sums while lazily yielding grouped UInt16 selector results.<br/></summary>
        public IEnumerable<AggregateGroup<TKey, ulong>> IterateSum(Selector<T>.IntegralSelector<ushort> selector)
            => IterateSelectorGroups<ushort, ulong, double, ulong>(selector, static snapshot => snapshot.Sum);

        /// <summary>Uses widened signed sums while lazily yielding grouped Int32 selector results.<br/></summary>
        public IEnumerable<AggregateGroup<TKey, long>> IterateSum(Selector<T>.IntegralSelector<int> selector)
            => IterateSelectorGroups<int, long, double, long>(selector, static snapshot => snapshot.Sum);

        /// <summary>Uses widened unsigned sums while lazily yielding grouped UInt32 selector results.<br/></summary>
        public IEnumerable<AggregateGroup<TKey, ulong>> IterateSum(Selector<T>.IntegralSelector<uint> selector)
            => IterateSelectorGroups<uint, ulong, double, ulong>(selector, static snapshot => snapshot.Sum);

        /// <summary>Uses widened Double sums while lazily yielding grouped Single selector results.<br/></summary>
        public IEnumerable<AggregateGroup<TKey, double>> IterateSum(Selector<T>.RealSelector<float> selector)
            => IterateSelectorGroups<float, double, double, double>(selector, static snapshot => snapshot.Sum);

        /// <summary>Preserves Decimal sums while lazily yielding grouped Decimal selector results.<br/></summary>
        public IEnumerable<AggregateGroup<TKey, decimal>> IterateSum(Selector<T>.DecimalSelector selector)
            => IterateSelectorGroups<decimal, decimal, decimal, decimal>(selector, static snapshot => snapshot.Sum);

        /// <summary>Lazily yields Double arithmetic averages for one typed numeric selector within each group.<br/></summary>
        public IEnumerable<AggregateGroup<TKey, double?>> IterateAverage<TNumber>(Selector<T>.NumericSelector<TNumber> selector)
            where TNumber : struct, INumber<TNumber>
            => IterateSelectorGroups<TNumber, TNumber, double, double?>(selector, static snapshot => snapshot.Average);

        /// <summary>Lazily yields Decimal-preserving arithmetic averages for one Decimal selector within each group.<br/></summary>
        public IEnumerable<AggregateGroup<TKey, decimal?>> IterateAverage(Selector<T>.DecimalSelector selector)
            => IterateSelectorGroups<decimal, decimal, decimal, decimal?>(selector, static snapshot => snapshot.Average);

        /// <summary>Lazily yields the smallest valid numeric selector value within each group.<br/></summary>
        public IEnumerable<AggregateGroup<TKey, TNumber?>> IterateMin<TNumber>(Selector<T>.NumericSelector<TNumber> selector)
            where TNumber : struct, INumber<TNumber>
            => IterateSelectorGroups<TNumber, TNumber, double, TNumber?>(selector, static snapshot => snapshot.Minimum);

        /// <summary>Lazily yields the largest valid numeric selector value within each group.<br/></summary>
        public IEnumerable<AggregateGroup<TKey, TNumber?>> IterateMax<TNumber>(Selector<T>.NumericSelector<TNumber> selector)
            where TNumber : struct, INumber<TNumber>
            => IterateSelectorGroups<TNumber, TNumber, double, TNumber?>(selector, static snapshot => snapshot.Maximum);

        /// <summary>Lazily yields the smallest valid Decimal selector value within each group.<br/></summary>
        public IEnumerable<AggregateGroup<TKey, decimal?>> IterateMin(Selector<T>.DecimalSelector selector)
            => IterateSelectorGroups<decimal, decimal, decimal, decimal?>(selector, static snapshot => snapshot.Minimum);

        /// <summary>Lazily yields the largest valid Decimal selector value within each group.<br/></summary>
        public IEnumerable<AggregateGroup<TKey, decimal?>> IterateMax(Selector<T>.DecimalSelector selector)
            => IterateSelectorGroups<decimal, decimal, decimal, decimal?>(selector, static snapshot => snapshot.Maximum);

        /// <summary>
        /// Lazily exposes finalized per-group structural distinct counts from one backend-neutral selector.<br/>
        /// The fallback must complete each group's distinct set before that group's immutable count can be yielded.<br/>
        /// </summary>
        /// <param name="selector">The direct or computed selector whose non-null values are counted structurally.<br/></param>
        /// <returns>A deferred grouped-distinct-count stream.<br/></returns>
        public IEnumerable<AggregateGroup<TKey, ulong>> IterateDistinctCount(ISelector selector)
        {
            ArgumentNullException.ThrowIfNull(selector);
            return IterateFallback();

            IEnumerable<AggregateGroup<TKey, ulong>> IterateFallback()
            {
                foreach (AggregateGroup<TKey, ulong> group in DistinctCount(selector))
                    yield return group;
            }
        }

        /// <summary>
        /// Selects the native adjacent-group iterator when both lambda expressions resolve to compatible persistent indexes, otherwise defers the established authoritative grouped reducer.<br/>
        /// </summary>
        private IEnumerable<AggregateGroup<TKey, TResult>> IterateExpressionGroups<TValue, TSum, TMean, TResult>(
            Expression<Func<T, TValue?>> selector,
            Func<NumericAggregateSnapshot<TValue, TSum, TMean>, TResult> projector,
            bool? distinctOverride = null)
            where TValue : struct, INumber<TValue>
            where TSum : struct, INumber<TSum>
            where TMean : struct, INumber<TMean>
        {
            ArgumentNullException.ThrowIfNull(selector);
            bool useDistinctValues = distinctOverride ?? distinctValues;
            if (keySelectorExpression is not null &&
                store.TryIterateNumericGroupsByLibraDex<TKey, TValue, TSum, TMean>(
                    keySelectorExpression,
                    selector,
                    where,
                    useDistinctValues,
                    out IEnumerable<(TKey Key, NumericAggregateSnapshot<TValue, TSum, TMean> Snapshot)> indexed))
            {
                return Project(indexed);
            }

            return ProjectFallback();

            IEnumerable<AggregateGroup<TKey, TResult>> Project(IEnumerable<(TKey Key, NumericAggregateSnapshot<TValue, TSum, TMean> Snapshot)> source)
            {
                foreach ((TKey key, NumericAggregateSnapshot<TValue, TSum, TMean> snapshot) in source)
                    yield return new AggregateGroup<TKey, TResult>(key, projector(snapshot));
            }

            IEnumerable<AggregateGroup<TKey, TResult>> ProjectFallback()
            {
                foreach (GroupSnapshot<TValue, TSum, TMean> group in ComputeGroups<TValue, TSum, TMean>(selector, useDistinctValues))
                    yield return new AggregateGroup<TKey, TResult>(group.Key, projector(group.Snapshot));
            }
        }

        /// <summary>
        /// Defers payload-native selector grouping and projects finalized snapshots one row at a time.<br/>
        /// </summary>
        private IEnumerable<AggregateGroup<TKey, TResult>> IterateSelectorGroups<TValue, TSum, TMean, TResult>(
            ISelector selector,
            Func<NumericAggregateSnapshot<TValue, TSum, TMean>, TResult> projector)
            where TValue : struct, INumber<TValue>
            where TSum : struct, INumber<TSum>
            where TMean : struct, INumber<TMean>
        {
            ArgumentNullException.ThrowIfNull(selector);
            foreach (GroupSnapshot<TValue, TSum, TMean> group in ComputeSelectorGroups<TValue, TSum, TMean>(selector))
                yield return new AggregateGroup<TKey, TResult>(group.Key, projector(group.Snapshot));
        }

        /// <summary>
        /// Partitions the selected record stream by key and computes one numeric snapshot per encountered group.<br/>
        /// </summary>
        private IReadOnlyList<GroupSnapshot<TValue, TSum, TMean>> ComputeGroups<TValue, TSum, TMean>(
            Expression<Func<T, TValue?>> selector,
            bool? distinctOverride = null)
            where TValue : struct, INumber<TValue>
            where TSum : struct, INumber<TSum>
            where TMean : struct, INumber<TMean>
        {
            ArgumentNullException.ThrowIfNull(selector);
            bool useDistinctValues = distinctOverride ?? distinctValues;
            if (keySelectorExpression is not null && store.TryComputeNumericGroupsByLibraDex<TKey, TValue, TSum, TMean>(
                keySelectorExpression,
                selector,
                where,
                useDistinctValues,
                out IReadOnlyList<(TKey Key, NumericAggregateSnapshot<TValue, TSum, TMean> Snapshot)> indexed))
            {
                return indexed
                    .Select(static group => new GroupSnapshot<TValue, TSum, TMean>(group.Key, group.Snapshot))
                    .ToArray();
            }

            Func<T, TValue?> compiled = selector.Compile();
            if (payloadKeySelector is not null)
                return ComputePayloadGroups<TValue, TSum, TMean>(
                    payload =>
                    {
                        T? record = payload.ToObject<T>(store.NativeDeserializationOptions);
                        return record is null ? null : compiled(record);
                    },
                    useDistinctValues);

            Dictionary<TKey, List<T>> groups = new();
            foreach (T record in store.IterateAggregateRecords(where))
            {
                TKey key = keySelector!(record) ?? throw new InvalidOperationException("Grouped aggregate keys cannot be null.");
                if (!groups.TryGetValue(key, out List<T>? records))
                {
                    records = new List<T>();
                    groups.Add(key, records);
                }

                records.Add(record);
            }

            List<GroupSnapshot<TValue, TSum, TMean>> results = new(groups.Count);
            foreach ((TKey key, List<T> records) in groups)
            {
                results.Add(new GroupSnapshot<TValue, TSum, TMean>(
                    key,
                    ComputeNumeric<TValue, TSum, TMean>(records, compiled, useDistinctValues, captureValues: false)));
            }

            return results;
        }

        /// <summary>
        /// Projects typed grouped sum snapshots into the stable public group/result pair without numeric conversion.<br/>
        /// </summary>
        private static IReadOnlyList<AggregateGroup<TKey, TSum>> ProjectGroupSums<TValue, TSum, TMean>(
            IReadOnlyList<GroupSnapshot<TValue, TSum, TMean>> groups)
            where TValue : struct, INumber<TValue>
            where TSum : struct, INumber<TSum>
            where TMean : struct, INumber<TMean>
            => groups
                .Select(static group => new AggregateGroup<TKey, TSum>(group.Key, group.Snapshot.Sum))
                .ToArray();

        /// <summary>
        /// Builds one exact typed selector reader and reduces its numeric values into insertion-ordered groups.<br/>
        /// Selector-key plus selector-value execution projects the authoritative payload directly; a lambda key hydrates only because its developer delegate requires <typeparamref name="T"/>.<br/>
        /// </summary>
        private IReadOnlyList<GroupSnapshot<TValue, TSum, TMean>> ComputeSelectorGroups<TValue, TSum, TMean>(
            ISelector selector)
            where TValue : struct, INumber<TValue>
            where TSum : struct, INumber<TSum>
            where TMean : struct, INumber<TMean>
        {
            ArgumentNullException.ThrowIfNull(selector);
            if (!store.TryCreateStoreSelectorReader<TValue>(selector, out var valueReader))
            {
                throw new NotSupportedException(
                    $"Grouped aggregate value selector must produce an exact {typeof(TValue).Name} value with complete backend-neutral semantics.");
            }
            return ComputePayloadGroups<TValue, TSum, TMean>(
                payload =>
                {
                    (bool HasValue, TValue Value) candidate = valueReader(payload);
                    return candidate.HasValue ? candidate.Value : null;
                },
                distinctValues);
        }

        /// <summary>
        /// Performs one allocation-conscious payload traversal and incrementally updates compact per-group numeric state.<br/>
        /// Complete records and per-group record lists are avoided whenever the key/value pair can both be projected from selector descriptors.<br/>
        /// </summary>
        private IReadOnlyList<GroupSnapshot<TValue, TSum, TMean>> ComputePayloadGroups<TValue, TSum, TMean>(
            Func<Inheto.InhetoBinary, TValue?> valueReader,
            bool useDistinctValues)
            where TValue : struct, INumber<TValue>
            where TSum : struct, INumber<TSum>
            where TMean : struct, INumber<TMean>
        {
            using var lifecycle = store.EnterStoreOperationScope();
            var buckets = new Dictionary<TKey, GroupNumericAccumulator<TValue, TSum, TMean>>();
            var ordered = new List<(TKey Key, GroupNumericAccumulator<TValue, TSum, TMean> Accumulator)>();
            foreach (Inheto.InhetoBinary payload in store.IterateAuthoritativeGroupingPayloads(where, nameof(ComputePayloadGroups)))
            {
                TKey key = ReadAnyPayloadKey(payload);
                if (!buckets.TryGetValue(key, out GroupNumericAccumulator<TValue, TSum, TMean>? accumulator))
                {
                    accumulator = new GroupNumericAccumulator<TValue, TSum, TMean>(useDistinctValues);
                    buckets.Add(key, accumulator);
                    ordered.Add((key, accumulator));
                }
                accumulator.Add(valueReader(payload));
            }
            return ordered
                .Select(static group => new GroupSnapshot<TValue, TSum, TMean>(
                    group.Key,
                    group.Accumulator.ToSnapshot()))
                .ToArray();
        }

        /// <summary>
        /// Resolves one grouping key from a payload selector, or hydrates one record only when the grouped facade was created from a developer lambda.<br/>
        /// Missing and null keys fail consistently with the established non-null grouped aggregate contract.<br/>
        /// </summary>
        private TKey ReadAnyPayloadKey(Inheto.InhetoBinary payload)
        {
            if (payloadKeySelector is not null)
                return ReadPayloadKey(payload);

            T? record = payload.ToObject<T>(store.NativeDeserializationOptions);
            if (record is null)
                throw new InvalidDataException("A grouped aggregate payload could not be restored to its record type.");
            return keySelector!(record)
                ?? throw new InvalidOperationException("Grouped aggregate keys cannot be null.");
        }

        /// <summary>
        /// Reads one exact selector key while enforcing the grouped facade's non-null key contract.<br/>
        /// </summary>
        private TKey ReadPayloadKey(Inheto.InhetoBinary payload)
        {
            (bool HasValue, TKey Value) candidate = payloadKeySelector!(payload);
            if (!candidate.HasValue || candidate.Value is null)
                throw new InvalidOperationException("Grouped aggregate keys cannot be missing or null.");
            return candidate.Value;
        }

        /// <summary>
        /// Holds checked numeric aggregate state for one group and optionally one per-group distinct set.<br/>
        /// The state mirrors the shared scalar aggregate kernel so grouped selector arithmetic cannot drift in sum, mean, extrema, or dispersion semantics.<br/>
        /// </summary>
        private sealed class GroupNumericAccumulator<TValue, TSum, TMean>
            where TValue : struct, INumber<TValue>
            where TSum : struct, INumber<TSum>
            where TMean : struct, INumber<TMean>
        {
            private readonly HashSet<TValue>? distinct;
            private ulong count;
            private ulong missingCount;
            private TSum sum = TSum.Zero;
            private TMean mean = TMean.Zero;
            private TValue minimum;
            private TValue maximum;
            private double varianceMean;
            private double varianceM2;

            /// <summary>
            /// Creates one empty group accumulator with an optional exact-value distinct set.<br/>
            /// </summary>
            internal GroupNumericAccumulator(bool distinctValues)
            {
                distinct = distinctValues ? new HashSet<TValue>() : null;
            }

            /// <summary>
            /// Adds one nullable projection using checked sum arithmetic and Welford's stable running recurrence.<br/>
            /// Missing values increment only the missing count; duplicate values are ignored when distinct mode is active.<br/>
            /// </summary>
            internal void Add(TValue? optional)
            {
                if (!optional.HasValue)
                {
                    missingCount = checked(missingCount + 1);
                    return;
                }
                TValue value = optional.Value;
                if (distinct is not null && !distinct.Add(value))
                    return;

                count = checked(count + 1);
                sum = checked(sum + TSum.CreateChecked(value));
                TMean meanValue = TMean.CreateChecked(value);
                mean += (meanValue - mean) / TMean.CreateChecked(count);
                if (count == 1)
                {
                    minimum = value;
                    maximum = value;
                }
                else
                {
                    if (value < minimum) minimum = value;
                    if (value > maximum) maximum = value;
                }
                double varianceValue = double.CreateChecked(value);
                double delta = varianceValue - varianceMean;
                varianceMean += delta / count;
                varianceM2 += delta * (varianceValue - varianceMean);
            }

            /// <summary>
            /// Freezes the current group state into the same immutable snapshot consumed by existing grouped terminals.<br/>
            /// </summary>
            internal NumericAggregateSnapshot<TValue, TSum, TMean> ToSnapshot()
                => new(
                    count,
                    missingCount,
                    sum,
                    count == 0 ? null : mean,
                    count == 0 ? null : minimum,
                    count == 0 ? null : maximum,
                    count == 0 ? null : varianceM2 / count,
                    count < 2 ? null : varianceM2 / (count - 1),
                    []);
        }

        private readonly record struct GroupSnapshot<TValue, TSum, TMean>(
            TKey Key,
            NumericAggregateSnapshot<TValue, TSum, TMean> Snapshot)
            where TValue : struct, INumber<TValue>
            where TSum : struct, INumber<TSum>
            where TMean : struct, INumber<TMean>;

        /// <summary>
        /// Opens a forward-only reader that computes the minimum and maximum comparable value for each grouping key in one traversal.<br/>
        /// A compatible persistent grouping/value index pair streams completed groups in grouping-index order with current-group state only; unsupported selector or index shapes defer one compact authoritative fallback until <see cref="GroupedMinMaxReader{TKey, TValue}.Prepare"/> or the first read.<br/>
        /// Opening the reader performs no source traversal, creates no index, and does not compile the value expression unless fallback execution is required.<br/>
        /// </summary>
        /// <typeparam name="TValue">The directly comparable projected value type.<br/></typeparam>
        /// <param name="selector">The grouped value selector whose extrema are returned without numeric or temporal conversion.<br/></param>
        /// <returns>An unprepared grouped extrema reader.<br/></returns>
        public GroupedMinMaxReader<TKey, TValue> OpenMinMax<TValue>(Expression<Func<T, TValue>> selector)
            where TValue : notnull, IComparable<TValue>
        {
            ArgumentNullException.ThrowIfNull(selector);

            IEnumerable<AggregateMinMaxGroup<TKey, TValue>>? OpenIndexed()
            {
                if (keySelectorExpression is not null &&
                    store.TryIterateMinMaxGroupsByLibraDex(
                        keySelectorExpression,
                        selector,
                        where,
                        out IEnumerable<AggregateMinMaxGroup<TKey, TValue>> indexed))
                {
                    return indexed;
                }
                return null;
            }

            AggregateMinMaxGroup<TKey, TValue>[] PrepareFallback()
            {
                Func<T, TValue> valueSelector = selector.Compile();
                var positions = new Dictionary<TKey, int>();
                var groups = new List<AggregateMinMaxGroup<TKey, TValue>>();

                void Add(TKey key, TValue value)
                {
                    if (value is null)
                        return;
                    if (!positions.TryGetValue(key, out int position))
                    {
                        positions.Add(key, groups.Count);
                        groups.Add(new AggregateMinMaxGroup<TKey, TValue>(key, value, value));
                        return;
                    }

                    AggregateMinMaxGroup<TKey, TValue> group = groups[position];
                    TValue minimum = value.CompareTo(group.Minimum) < 0 ? value : group.Minimum;
                    TValue maximum = value.CompareTo(group.Maximum) > 0 ? value : group.Maximum;
                    groups[position] = new AggregateMinMaxGroup<TKey, TValue>(key, minimum, maximum);
                }

                if (payloadKeySelector is not null)
                {
                    using var lifecycle = store.EnterStoreOperationScope();
                    foreach (Inheto.InhetoBinary payload in store.IterateAuthoritativeGroupingPayloads(where, nameof(OpenMinMax)))
                    {
                        T? record = payload.ToObject<T>(store.NativeDeserializationOptions);
                        if (record is not null)
                            Add(ReadPayloadKey(payload), valueSelector(record));
                    }
                }
                else
                {
                    foreach (T record in store.IterateAggregateRecords(where))
                    {
                        TKey key = keySelector!(record)
                            ?? throw new InvalidOperationException("Grouped aggregate keys cannot be null.");
                        Add(key, valueSelector(record));
                    }
                }

                return groups.ToArray();
            }

            return new GroupedMinMaxReader<TKey, TValue>(OpenIndexed, PrepareFallback);
        }
    }

    /// <summary>
    /// Selects the ordinary or condition-filtered record stream used by aggregate terminals.<br/>
    /// </summary>
    private IEnumerable<T> IterateAggregateRecords(Condition<T>.EndCondition? where)
        => where is null
            ? IterateFractalRecordsForAggregate(nameof(IterateAggregateRecords))
            : IteratePlannedConditionRecords(where, nameof(IterateAggregateRecords));

    /// <summary>
    /// Lifts a non-nullable value selector into the nullable projection shape consumed by the shared kernel.<br/>
    /// </summary>
    private static Expression<Func<T, TValue?>> Lift<TValue>(Expression<Func<T, TValue>> selector)
        where TValue : struct
    {
        ArgumentNullException.ThrowIfNull(selector);
        return Expression.Lambda<Func<T, TValue?>>(
            Expression.Convert(selector.Body, typeof(TValue?)),
            selector.Parameters);
    }

    /// <summary>
    /// Computes checked sum, running mean, extrema, Welford dispersion, and optional captured values in one traversal.<br/>
    /// </summary>
    private static NumericAggregateSnapshot<TValue, TSum, TMean> ComputeNumeric<TValue, TSum, TMean>(
        IEnumerable<T> records,
        Func<T, TValue?> selector,
        bool distinctValues,
        bool captureValues)
        where TValue : struct, INumber<TValue>
        where TSum : struct, INumber<TSum>
        where TMean : struct, INumber<TMean>
        => ComputeNumericCore<T, TValue, TSum, TMean>(
            records,
            selector,
            distinctValues,
            captureValues);

    /// <summary>
    /// Computes the shared numeric aggregate state over any allocation-free typed source stream.<br/>
    /// Record lambdas and backend-neutral selector readers both enter this kernel so arithmetic, missing-value, distinctness, variance, and trimmed-value semantics cannot drift.<br/>
    /// </summary>
    /// <typeparam name="TSource">Record or serialized-payload source type traversed by the caller.<br/></typeparam>
    /// <typeparam name="TValue">Exact numeric value produced by the projection.<br/></typeparam>
    /// <typeparam name="TSum">Checked sum result type.<br/></typeparam>
    /// <typeparam name="TMean">Running-mean result type.<br/></typeparam>
    /// <param name="sources">Selected records or retained Inheto payloads.<br/></param>
    /// <param name="selector">Prebuilt projection returning null for missing or invalid values.<br/></param>
    /// <param name="distinctValues">Whether equal projected values contribute once.<br/></param>
    /// <param name="captureValues">Whether contributing values are retained for a trimmed-mean terminal.<br/></param>
    /// <returns>The complete single-pass numeric aggregate snapshot.<br/></returns>
    private static NumericAggregateSnapshot<TValue, TSum, TMean> ComputeNumericCore<TSource, TValue, TSum, TMean>(
        IEnumerable<TSource> sources,
        Func<TSource, TValue?> selector,
        bool distinctValues,
        bool captureValues)
        where TValue : struct, INumber<TValue>
        where TSum : struct, INumber<TSum>
        where TMean : struct, INumber<TMean>
    {
        HashSet<TValue>? distinct = distinctValues ? new HashSet<TValue>() : null;
        List<TValue> values = captureValues ? new List<TValue>() : [];
        ulong count = 0;
        ulong missingCount = 0;
        TSum sum = TSum.Zero;
        TMean mean = TMean.Zero;
        TValue minimum = default;
        TValue maximum = default;
        double varianceMean = 0;
        double varianceM2 = 0;

        foreach (TSource source in sources)
        {
            TValue? optional = selector(source);
            if (!optional.HasValue)
            {
                missingCount = checked(missingCount + 1);
                continue;
            }

            TValue value = optional.Value;
            if (distinct is not null && !distinct.Add(value))
                continue;

            count = checked(count + 1);
            sum = checked(sum + TSum.CreateChecked(value));
            TMean meanValue = TMean.CreateChecked(value);
            mean += (meanValue - mean) / TMean.CreateChecked(count);

            if (count == 1)
            {
                minimum = value;
                maximum = value;
            }
            else
            {
                if (value < minimum)
                    minimum = value;
                if (value > maximum)
                    maximum = value;
            }

            double varianceValue = double.CreateChecked(value);
            double delta = varianceValue - varianceMean;
            varianceMean += delta / count;
            varianceM2 += delta * (varianceValue - varianceMean);
            if (captureValues)
                values.Add(value);
        }

        return new NumericAggregateSnapshot<TValue, TSum, TMean>(
            count,
            missingCount,
            sum,
            count == 0 ? null : mean,
            count == 0 ? null : minimum,
            count == 0 ? null : maximum,
            count == 0 ? null : varianceM2 / count,
            count < 2 ? null : varianceM2 / (count - 1),
            values);
    }

    /// <summary>
    /// Converts an internal numeric snapshot into the stable public statistics result contract.<br/>
    /// </summary>
    private static AggregateStatistics<TValue, TSum, TMean> ToStatistics<TValue, TSum, TMean>(
        NumericAggregateSnapshot<TValue, TSum, TMean> snapshot)
        where TValue : struct, INumber<TValue>
        where TSum : struct, INumber<TSum>
        where TMean : struct, INumber<TMean>
        => new(
            snapshot.Count,
            snapshot.MissingCount,
            snapshot.Sum,
            snapshot.Average,
            snapshot.Minimum,
            snapshot.Maximum,
            snapshot.PopulationVariance,
            snapshot.SampleVariance,
            snapshot.PopulationVariance.HasValue ? Math.Sqrt(snapshot.PopulationVariance.Value) : null,
            snapshot.SampleVariance.HasValue ? Math.Sqrt(snapshot.SampleVariance.Value) : null);

    /// <summary>
    /// Sorts captured values, removes the requested number of positions from each tail, and computes a running mean.<br/>
    /// </summary>
    private static TMean? CalculateTrimmedMean<TValue, TMean>(List<TValue> values, int trimCountPerTail)
        where TValue : struct, INumber<TValue>
        where TMean : struct, INumber<TMean>
    {
        if (checked(trimCountPerTail * 2) >= values.Count)
            return null;

        values.Sort();
        int start = trimCountPerTail;
        int end = values.Count - trimCountPerTail;
        ulong count = 0;
        TMean mean = TMean.Zero;
        for (int i = start; i < end; i++)
        {
            count++;
            TMean value = TMean.CreateChecked(values[i]);
            mean += (value - mean) / TMean.CreateChecked(count);
        }

        return count == 0 ? null : mean;
    }

    /// <summary>
    /// Rejects a non-finite or out-of-range per-tail trim fraction.<br/>
    /// </summary>
    private static void ValidateTrimFraction(double trimFractionPerTail)
    {
        if (double.IsNaN(trimFractionPerTail) || trimFractionPerTail < 0 || trimFractionPerTail > 0.5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(trimFractionPerTail),
                trimFractionPerTail,
                "Trim fraction per tail must be between zero and one half.");
        }
    }

    /// <summary>
    /// Rejects a negative per-tail trim count.<br/>
    /// </summary>
    private static void ValidateTrimCount(int trimCountPerTail)
    {
        if (trimCountPerTail < 0)
            throw new ArgumentOutOfRangeException(nameof(trimCountPerTail), trimCountPerTail, "Trim count per tail cannot be negative.");
    }

    private readonly record struct NumericAggregateSnapshot<TValue, TSum, TMean>(
        ulong Count,
        ulong MissingCount,
        TSum Sum,
        TMean? Average,
        TValue? Minimum,
        TValue? Maximum,
        double? PopulationVariance,
        double? SampleVariance,
        List<TValue> Values)
        where TValue : struct, INumber<TValue>
        where TSum : struct, INumber<TSum>
        where TMean : struct, INumber<TMean>;

    /// <summary>
    /// Sums projected numeric values through the strongly typed ordinary aggregate facade.<br/>
    /// This overload lets the discoverable legacy-shaped <c>store.Sum(...)</c> call use the corrected typed implementation.<br/>
    /// </summary>
    /// <typeparam name="TNumber">The selected numeric type and default sum result type.<br/></typeparam>
    /// <param name="selector">The numeric value selector.<br/></param>
    /// <param name="where">The optional record condition.<br/></param>
    /// <returns>The checked sum, or numeric zero when no value contributes.<br/></returns>
    public TNumber Sum<TNumber>(
        Expression<Func<T, TNumber>> selector,
        Condition<T>.EndCondition? where = null)
        where TNumber : struct, INumber<TNumber>
        => Aggregates.Sum(selector, where);

    /// <inheritdoc cref="Sum{TNumber}(Expression{Func{T, TNumber}}, Condition{T}.EndCondition?)"/>
    public long Sum(Expression<Func<T, int>> selector, Condition<T>.EndCondition? where = null)
        => Aggregates.Sum(selector, where);

    /// <inheritdoc cref="Sum{TNumber}(Expression{Func{T, TNumber}}, Condition{T}.EndCondition?)"/>
    public long Sum(Expression<Func<T, sbyte>> selector, Condition<T>.EndCondition? where = null)
        => Aggregates.Sum(selector, where);

    /// <inheritdoc cref="Sum{TNumber}(Expression{Func{T, TNumber}}, Condition{T}.EndCondition?)"/>
    public ulong Sum(Expression<Func<T, byte>> selector, Condition<T>.EndCondition? where = null)
        => Aggregates.Sum(selector, where);

    /// <inheritdoc cref="Sum{TNumber}(Expression{Func{T, TNumber}}, Condition{T}.EndCondition?)"/>
    public long Sum(Expression<Func<T, short>> selector, Condition<T>.EndCondition? where = null)
        => Aggregates.Sum(selector, where);

    /// <inheritdoc cref="Sum{TNumber}(Expression{Func{T, TNumber}}, Condition{T}.EndCondition?)"/>
    public ulong Sum(Expression<Func<T, ushort>> selector, Condition<T>.EndCondition? where = null)
        => Aggregates.Sum(selector, where);

    /// <inheritdoc cref="Sum{TNumber}(Expression{Func{T, TNumber}}, Condition{T}.EndCondition?)"/>
    public ulong Sum(Expression<Func<T, uint>> selector, Condition<T>.EndCondition? where = null)
        => Aggregates.Sum(selector, where);

    /// <inheritdoc cref="Sum{TNumber}(Expression{Func{T, TNumber}}, Condition{T}.EndCondition?)"/>
    public double Sum(Expression<Func<T, float>> selector, Condition<T>.EndCondition? where = null)
        => Aggregates.Sum(selector, where);

    /// <summary>
    /// Sums nullable projected numeric values through the ordinary aggregate facade while ignoring missing values.<br/>
    /// Dedicated narrow-number overloads widen their accumulator just like their non-nullable counterparts.<br/>
    /// </summary>
    /// <typeparam name="TNumber">The selected numeric type and default sum result type.<br/></typeparam>
    /// <param name="selector">The nullable numeric value selector.<br/></param>
    /// <param name="where">The optional record condition.<br/></param>
    /// <returns>The checked sum, or numeric zero when no value contributes.<br/></returns>
    public TNumber Sum<TNumber>(
        Expression<Func<T, TNumber?>> selector,
        Condition<T>.EndCondition? where = null)
        where TNumber : struct, INumber<TNumber>
        => Aggregates.Sum(selector, where);

    /// <inheritdoc cref="Sum{TNumber}(Expression{Func{T, Nullable{TNumber}}}, Condition{T}.EndCondition?)"/>
    public long Sum(Expression<Func<T, int?>> selector, Condition<T>.EndCondition? where = null)
        => Aggregates.Sum(selector, where);

    /// <summary>
    /// Computes a fractional-safe arithmetic average through the strongly typed ordinary aggregate facade.<br/>
    /// Integral inputs return nullable Double; Decimal inputs select the Decimal-specific overload.<br/>
    /// </summary>
    /// <typeparam name="TNumber">The selected numeric type.<br/></typeparam>
    /// <param name="selector">The numeric value selector.<br/></param>
    /// <param name="where">The optional record condition.<br/></param>
    /// <returns>The arithmetic average, or null when no value contributes.<br/></returns>
    public double? Average<TNumber>(
        Expression<Func<T, TNumber>> selector,
        Condition<T>.EndCondition? where = null)
        where TNumber : struct, INumber<TNumber>
        => Aggregates.Average(selector, where);

    /// <summary>
    /// Computes a Decimal-preserving arithmetic average through the strongly typed ordinary aggregate facade.<br/>
    /// </summary>
    /// <param name="selector">The Decimal value selector.<br/></param>
    /// <param name="where">The optional record condition.<br/></param>
    /// <returns>The arithmetic average, or null when no value contributes.<br/></returns>
    public decimal? Average(
        Expression<Func<T, decimal>> selector,
        Condition<T>.EndCondition? where = null)
        => Aggregates.Average(selector, where);

    /// <summary>
    /// Computes a fractional-safe arithmetic average of nullable projected values through the ordinary aggregate facade.<br/>
    /// Missing values are ignored and integral inputs return nullable Double.<br/>
    /// </summary>
    /// <typeparam name="TNumber">The selected numeric type.<br/></typeparam>
    /// <param name="selector">The nullable numeric value selector.<br/></param>
    /// <param name="where">The optional record condition.<br/></param>
    /// <returns>The arithmetic average, or null when no value contributes.<br/></returns>
    public double? Average<TNumber>(
        Expression<Func<T, TNumber?>> selector,
        Condition<T>.EndCondition? where = null)
        where TNumber : struct, INumber<TNumber>
        => Aggregates.Average(selector, where);

    /// <summary>
    /// Computes a Decimal-preserving average of nullable projected Decimal values through the ordinary aggregate facade.<br/>
    /// </summary>
    /// <param name="selector">The nullable Decimal value selector.<br/></param>
    /// <param name="where">The optional record condition.<br/></param>
    /// <returns>The arithmetic average, or null when no value contributes.<br/></returns>
    public decimal? Average(
        Expression<Func<T, decimal?>> selector,
        Condition<T>.EndCondition? where = null)
        => Aggregates.Average(selector, where);

    /// <summary>
    /// Attempts to compute a numeric aggregate directly from one registered LibraDex ordered index.<br/>
    /// The selector must be one simple numeric member path, and an optional condition must translate completely inside the same persistent LibraDex identity group.<br/>
    /// Unsupported selectors, unavailable indexes, volatile cross-group filters, and non-translatable conditions return <c>false</c> so the shared persisted-record reducer remains the correctness fallback.<br/>
    /// </summary>
    /// <typeparam name="TValue">The indexed numeric key type.<br/></typeparam>
    /// <typeparam name="TSum">The checked sum result type.<br/></typeparam>
    /// <typeparam name="TMean">The running-mean result type.<br/></typeparam>
    /// <param name="selector">The nullable numeric projection whose simple path identifies the target index.<br/></param>
    /// <param name="where">The optional record condition.<br/></param>
    /// <param name="distinctValues">Whether adjacent equal ordered keys contribute only once.<br/></param>
    /// <param name="captureValues">Whether contributing values are retained for a later trimmed mean.<br/></param>
    /// <param name="countMissing">Whether selected identities must also be counted to report missing projections.<br/></param>
    /// <param name="snapshot">The completed numeric snapshot when this method returns <c>true</c>.<br/></param>
    /// <returns><c>true</c> when LibraDex executed the complete aggregate without record hydration; otherwise <c>false</c>.<br/></returns>
    private bool TryComputeNumericByLibraDex<TValue, TSum, TMean>(
        Expression<Func<T, TValue?>> selector,
        Condition<T>.EndCondition? where,
        bool distinctValues,
        bool captureValues,
        bool countMissing,
        out NumericAggregateSnapshot<TValue, TSum, TMean> snapshot)
        where TValue : struct, INumber<TValue>
        where TSum : struct, INumber<TSum>
        where TMean : struct, INumber<TMean>
    {
        using var lifecycle = EnterStoreOperationScope();
        snapshot = default;
        if (libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly ||
            !TryResolveAggregateSelectorPath(selector, out string path))
        {
            return false;
        }

        LibraDexSimpleIndex[] indexes = libraDexSimpleIndexes;
        LibraDexSimpleIndex target = default;
        bool found = false;
        for (int i = 0; i < indexes.Length; i++)
        {
            LibraDexSimpleIndex candidate = indexes[i];
            if (candidate.KeyType == typeof(TValue) &&
                string.Equals(candidate.Path, path, StringComparison.Ordinal))
            {
                target = candidate;
                found = true;
                break;
            }
        }

        if (!found)
            return false;

        CatalogIdentityGroupIndexes group;
        if (target.OwnerCatalog is null)
        {
            if (libraDexIdentityIndexes is null)
                return false;
            group = libraDexIdentityIndexes;
        }
        else
        {
            if (where is not null)
                return false;
            group = target.OwnerCatalog.Indexes.IndexSet(indexGroupName);
        }

        LibraDexConditionEndCondition condition;
        if (where is null)
        {
            condition = CreateLibraDexAllCondition(group, target.Index);
        }
        else if (!where.builder.TryBuildLibraDexConditionTree(
            group,
            HasLibraDexIndexInBoundGroup,
            TryResolveLibraDexDerivedSelector,
            out condition))
        {
            return false;
        }

        snapshot = ComputeNumericFromLibraDexTuples<TValue, TSum, TMean>(
            group.IterateTuples(target.Index, condition, QueryDirection.Ascending),
            distinctValues,
            captureValues,
            out ulong projectedCount);

        if (!countMissing)
            return true;

        ulong selectedCount = where is null
            ? CountAggregateRecordIdentities()
            : CountLibraDexConditionIdentities(group, condition);
        ulong missingCount = selectedCount > projectedCount ? selectedCount - projectedCount : 0;
        snapshot = snapshot with { MissingCount = missingCount };
        return true;
    }

    /// <summary>
    /// Attempts a numeric aggregate directly over the key stream selected by one named semantic selector.<br/>
    /// A matching persistent or volatile LibraDex index avoids Fractal payload extraction; absence of that physical reality returns <see langword="false"/> so the registered projection reader remains the authoritative fallback.<br/>
    /// </summary>
    private bool TryComputeNamedSelectorByLibraDex<TValue, TSum, TMean>(
        ISelector selector,
        Condition<T>.EndCondition? where,
        bool distinctValues,
        bool captureValues,
        out NumericAggregateSnapshot<TValue, TSum, TMean> snapshot)
        where TValue : struct, INumber<TValue>
        where TSum : struct, INumber<TSum>
        where TMean : struct, INumber<TMean>
    {
        snapshot = default;
        if (libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly ||
            selector is not ISelectorStructure structure ||
            structure.ResultType != typeof(TValue) ||
            structure.SemanticDescriptor is not NamedIndexSelectorDescriptor named)
            return false;

        string path = named.SemanticName;
        FunctionIndexProjection[] projections = functionIndexProjections;
        for (int i = 0; i < projections.Length; i++)
        {
            if (string.Equals(projections[i].FunctionId, path, StringComparison.Ordinal) &&
                projections[i].ResultType == typeof(TValue))
            {
                path = GetFunctionIndexLogicalName(path);
                break;
            }
        }

        LibraDexSimpleIndex target = default;
        bool found = false;
        LibraDexSimpleIndex[] indexes = libraDexSimpleIndexes;
        for (int i = 0; i < indexes.Length; i++)
        {
            if (indexes[i].KeyType == typeof(TValue) && string.Equals(indexes[i].Path, path, StringComparison.Ordinal))
            {
                target = indexes[i];
                found = true;
                break;
            }
        }
        if (!found)
            return false;

        CatalogIdentityGroupIndexes group;
        if (target.OwnerCatalog is null)
        {
            if (libraDexIdentityIndexes is null)
                return false;
            group = libraDexIdentityIndexes;
        }
        else
        {
            if (where is not null)
                return false;
            group = target.OwnerCatalog.Indexes.IndexSet(indexGroupName);
        }

        LibraDexConditionEndCondition condition;
        if (where is null)
            condition = CreateLibraDexAllCondition(group, target.Index);
        else if (!where.builder.TryBuildLibraDexConditionTree(
                     group,
                     HasLibraDexIndexInBoundGroup,
                     TryResolveLibraDexDerivedSelector,
                     out condition))
            return false;

        snapshot = ComputeNumericFromLibraDexTuples<TValue, TSum, TMean>(
            group.IterateTuples(target.Index, condition, QueryDirection.Ascending),
            distinctValues,
            captureValues,
            out _);
        return true;
    }

    /// <summary>
    /// Resolves a lambda body composed only of member access and harmless numeric/nullable conversions into a normalized Abraxas property path.<br/>
    /// Method calls, arithmetic, constants, and other computed projections are intentionally rejected.<br/>
    /// </summary>
    /// <param name="selector">The candidate simple-member selector.<br/></param>
    /// <param name="path">The normalized dotted path when resolution succeeds.<br/></param>
    /// <returns><c>true</c> for a simple member chain rooted at the selector parameter; otherwise <c>false</c>.<br/></returns>
    private static bool TryResolveAggregateSelectorPath(LambdaExpression selector, out string path)
    {
        path = string.Empty;
        Expression current = selector.Body;
        while (current is UnaryExpression unary &&
               (unary.NodeType == ExpressionType.Convert ||
                unary.NodeType == ExpressionType.ConvertChecked ||
                unary.NodeType == ExpressionType.TypeAs))
        {
            current = unary.Operand;
        }

        Stack<string> segments = new();
        while (current is MemberExpression member)
        {
            segments.Push(member.Member.Name);
            current = member.Expression!;
            while (current is UnaryExpression nestedUnary &&
                   (nestedUnary.NodeType == ExpressionType.Convert ||
                    nestedUnary.NodeType == ExpressionType.ConvertChecked ||
                    nestedUnary.NodeType == ExpressionType.TypeAs))
            {
                current = nestedUnary.Operand;
            }
        }

        if (current != selector.Parameters[0] || segments.Count == 0)
            return false;

        path = "." + string.Join(".", segments);
        return true;
    }

    /// <summary>
    /// Accumulates typed numeric keys from an ordered LibraDex tuple stream without hydrating Abraxas records.<br/>
    /// Null keys are missing projections and are excluded; distinct mode suppresses adjacent equal keys because the stream is key ordered.<br/>
    /// </summary>
    /// <param name="tuples">The LibraDex tuples ordered by the aggregate key.<br/></param>
    /// <param name="distinctValues">Whether adjacent equal ordered keys contribute only once to aggregate results.<br/></param>
    /// <param name="captureValues">Whether contributing values are retained for a later trimmed mean.<br/></param>
    /// <param name="projectedCount">The number of non-null indexed rows before optional distinct suppression.<br/></param>
    /// <returns>The completed numeric snapshot over ordinary or distinct contributing values.<br/></returns>
    private static NumericAggregateSnapshot<TValue, TSum, TMean> ComputeNumericFromLibraDexTuples<TValue, TSum, TMean>(
        IEnumerable<LibraDexRuntimeTuple> tuples,
        bool distinctValues,
        bool captureValues,
        out ulong projectedCount)
        where TValue : struct, INumber<TValue>
        where TSum : struct, INumber<TSum>
        where TMean : struct, INumber<TMean>
    {
        List<TValue> values = captureValues ? new List<TValue>() : [];
        ulong count = 0;
        TSum sum = TSum.Zero;
        TMean mean = TMean.Zero;
        TValue minimum = default;
        TValue maximum = default;
        TValue prior = default;
        bool hasPrior = false;
        double varianceMean = 0;
        double varianceM2 = 0;
        projectedCount = 0;

        foreach (LibraDexRuntimeTuple tuple in tuples)
        {
            if (tuple.Key is not TValue value)
                continue;
            projectedCount = checked(projectedCount + 1);
            if (distinctValues && hasPrior && EqualityComparer<TValue>.Default.Equals(prior, value))
                continue;

            prior = value;
            hasPrior = true;
            count = checked(count + 1);
            sum = checked(sum + TSum.CreateChecked(value));
            TMean meanValue = TMean.CreateChecked(value);
            mean += (meanValue - mean) / TMean.CreateChecked(count);

            if (count == 1)
            {
                minimum = value;
                maximum = value;
            }
            else
            {
                if (value < minimum)
                    minimum = value;
                if (value > maximum)
                    maximum = value;
            }

            double varianceValue = double.CreateChecked(value);
            double delta = varianceValue - varianceMean;
            varianceMean += delta / count;
            varianceM2 += delta * (varianceValue - varianceMean);
            if (captureValues)
                values.Add(value);
        }

        return new NumericAggregateSnapshot<TValue, TSum, TMean>(
            count,
            MissingCount: 0,
            sum,
            count == 0 ? null : mean,
            count == 0 ? null : minimum,
            count == 0 ? null : maximum,
            count == 0 ? null : varianceM2 / count,
            count < 2 ? null : varianceM2 / (count - 1),
            values);
    }

    /// <summary>
    /// Counts authoritative Abraxas record identities without reading or deserializing record payloads.<br/>
    /// This is used only when an indexed statistics request needs an exact missing-projection count and has no filter.<br/>
    /// </summary>
    private ulong CountAggregateRecordIdentities()
    {
        using var operation = EnterStoreOperationScope();
        if (TryCountAllRecordsByIndex(out ulong indexedCount)) return indexedCount;
        ulong count = 0;
        foreach (ulong _ in IterateRecordIDs())
            count = checked(count + 1);
        return count;
    }

    /// <summary>
    /// Counts distinct identities selected by one completely translated LibraDex condition without reading record payloads.<br/>
    /// </summary>
    private ulong CountLibraDexConditionIdentities(
        CatalogIdentityGroupIndexes group,
        LibraDexConditionEndCondition condition)
    {
        if (TryCountNativeCondition(group, condition, null, out ulong nativeCount)) return nativeCount;
        ulong count = 0;
        using LibraDexIdentityCursor<ulong> cursor = group.AbraxasIdentityQuery<ulong>().OpenCursor(
            condition,
            deduplication: IdentityDeduplication.Distinct);
        while (cursor.Next())
            count = checked(count + 1);
        return count;
    }

    /// <summary>
    /// Attempts to count non-null projected values directly from one registered LibraDex ordered index.<br/>
    /// Distinct mode suppresses adjacent equal keys; an optional condition must translate completely inside the target's persistent identity group.<br/>
    /// </summary>
    /// <typeparam name="TValue">The projected indexed key type.<br/></typeparam>
    /// <param name="selector">The simple member selector identifying the target index.<br/></param>
    /// <param name="where">The optional record condition.<br/></param>
    /// <param name="distinctValues">Whether duplicate ordered keys contribute only once.<br/></param>
    /// <param name="count">The contributing value count when this method returns <c>true</c>.<br/></param>
    /// <returns><c>true</c> when LibraDex executed the complete projected count; otherwise <c>false</c>.<br/></returns>
    private bool TryCountProjectedByLibraDex<TValue>(
        LambdaExpression selector,
        Condition<T>.EndCondition? where,
        bool distinctValues,
        out ulong count)
    {
        using var lifecycle = EnterStoreOperationScope();
        count = 0;
        Type valueType = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);
        if (libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly ||
            !TryResolveAggregateSelectorPath(selector, out string path))
        {
            return false;
        }

        if (NativeDeserializationOptions.Aliases.Contains(path)) return false;
        if (TryCountNativeProjection(path, valueType, where, distinctValues, out count)) return true;

        LibraDexSimpleIndex target = default;
        bool found = false;
        LibraDexSimpleIndex[] indexes = libraDexSimpleIndexes;
        for (int i = 0; i < indexes.Length; i++)
        {
            LibraDexSimpleIndex candidate = indexes[i];
            if (candidate.KeyType == valueType &&
                string.Equals(candidate.Path, path, StringComparison.Ordinal))
            {
                target = candidate;
                found = true;
                break;
            }
        }

        if (!found)
            return false;

        CatalogIdentityGroupIndexes group;
        if (target.OwnerCatalog is null)
        {
            if (libraDexIdentityIndexes is null)
                return false;
            group = libraDexIdentityIndexes;
        }
        else
        {
            if (where is not null)
                return false;
            group = target.OwnerCatalog.Indexes.IndexSet(indexGroupName);
        }

        LibraDexConditionEndCondition condition;
        if (where is null)
        {
            condition = CreateLibraDexAllCondition(group, target.Index);
        }
        else if (!where.builder.TryBuildLibraDexConditionTree(
            group,
            HasLibraDexIndexInBoundGroup,
            TryResolveLibraDexDerivedSelector,
            out condition))
        {
            return false;
        }

        object? prior = null;
        bool hasPrior = false;
        foreach (LibraDexRuntimeTuple tuple in group.IterateTuples(target.Index, condition, QueryDirection.Ascending))
        {
            if (tuple.Key is null)
                continue;
            if (distinctValues && hasPrior && CompareLibraDexSortKey(prior, tuple.Key) == 0)
                continue;

            prior = tuple.Key;
            hasPrior = true;
            count = checked(count + 1);
        }

        return true;
    }

    /// <summary>
    /// Prepares a deferred adjacent-key count traversal over one compatible persistent LibraDex grouping index.<br/>
    /// Enumeration holds a store-operation scope and yields each completed group immediately, so caller filtering or early termination never requires a complete aggregate result list.<br/>
    /// </summary>
    /// <typeparam name="TKey">The indexed grouping-key type.<br/></typeparam>
    /// <param name="keySelector">The simple grouping-key selector.<br/></param>
    /// <param name="where">The optional completely translatable condition.<br/></param>
    /// <param name="results">The deferred ascending grouping-index stream when preparation succeeds.<br/></param>
    /// <returns><c>true</c> when a persistent LibraDex grouping index can execute the complete traversal; otherwise <c>false</c>.<br/></returns>
    private bool TryIterateCountGroupsByLibraDex<TKey>(
        Expression<Func<T, TKey>> keySelector,
        Condition<T>.EndCondition? where,
        out IEnumerable<AggregateGroup<TKey, ulong>> results)
        where TKey : notnull
    {
        using var preparation = EnterStoreOperationScope();
        results = Array.Empty<AggregateGroup<TKey, ulong>>();
        if (!TryPrepareLibraDexGroupedTraversal(
            keySelector,
            where,
            out CatalogIdentityGroupIndexes group,
            out LibraDexSimpleIndex groupingIndex,
            out LibraDexConditionEndCondition condition))
        {
            return false;
        }

        results = Iterate();
        return true;

        IEnumerable<AggregateGroup<TKey, ulong>> Iterate()
        {
            using var lifecycle = EnterStoreOperationScope();
            TKey currentKey = default!;
            ulong currentCount = 0;
            bool hasCurrent = false;
            foreach (LibraDexRuntimeTuple tuple in group.IterateTuples(groupingIndex.Index, condition, QueryDirection.Ascending))
            {
                if (tuple.Key is not TKey key)
                    throw new InvalidOperationException("Grouped aggregate keys cannot be missing or null.");

                if (hasCurrent && !EqualityComparer<TKey>.Default.Equals(currentKey, key))
                {
                    yield return new AggregateGroup<TKey, ulong>(currentKey, currentCount);
                    currentCount = 0;
                }

                currentKey = key;
                hasCurrent = true;
                currentCount = checked(currentCount + 1);
            }

            if (hasCurrent)
                yield return new AggregateGroup<TKey, ulong>(currentKey, currentCount);
        }
    }

    /// <summary>
    /// Prepares a deferred numeric reduction over compatible persistent grouping and value indexes.<br/>
    /// A maintained single-key-per-identity value lookup supplies each grouped value without materializing the complete value index, so the first completed group can be returned with current-group state only.<br/>
    /// Distinct-value memory is bounded to the current group, and abandoning enumeration stops the grouping-index traversal immediately.<br/>
    /// </summary>
    /// <typeparam name="TKey">The indexed grouping-key type.<br/></typeparam>
    /// <typeparam name="TValue">The indexed numeric value type.<br/></typeparam>
    /// <typeparam name="TSum">The checked sum result type.<br/></typeparam>
    /// <typeparam name="TMean">The running-mean result type.<br/></typeparam>
    /// <param name="keySelector">The simple grouping-key selector.<br/></param>
    /// <param name="valueSelector">The simple numeric value selector.<br/></param>
    /// <param name="where">The optional completely translatable condition.<br/></param>
    /// <param name="distinctValues">Whether equal projected values contribute only once inside each group.<br/></param>
    /// <param name="results">The deferred ascending grouping-index stream when preparation succeeds.<br/></param>
    /// <returns><c>true</c> when compatible persistent LibraDex indexes can execute the complete reduction; otherwise <c>false</c>.<br/></returns>
    private bool TryIterateNumericGroupsByLibraDex<TKey, TValue, TSum, TMean>(
        Expression<Func<T, TKey>> keySelector,
        Expression<Func<T, TValue?>> valueSelector,
        Condition<T>.EndCondition? where,
        bool distinctValues,
        out IEnumerable<(TKey Key, NumericAggregateSnapshot<TValue, TSum, TMean> Snapshot)> results)
        where TKey : notnull
        where TValue : struct, INumber<TValue>
        where TSum : struct, INumber<TSum>
        where TMean : struct, INumber<TMean>
    {
        using var preparation = EnterStoreOperationScope();
        results = Array.Empty<(TKey, NumericAggregateSnapshot<TValue, TSum, TMean>)>();
        if (!TryPrepareLibraDexGroupedTraversal(
            keySelector,
            where,
            out CatalogIdentityGroupIndexes group,
            out LibraDexSimpleIndex groupingIndex,
            out LibraDexConditionEndCondition condition) ||
            groupingIndex.Index.IdentityKeyMultiplicity != IdentityKeyMultiplicity.SingleKeyPerIdentity ||
            !groupingIndex.IncludeNullValues ||
            (typeof(TKey) == typeof(string) && !groupingIndex.IncludeEmptyStrings) ||
            !TryResolveAggregateSelectorPath(valueSelector, out string valuePath) ||
            !TryFindPersistentLibraDexAggregateIndex(valuePath, typeof(TValue), out LibraDexSimpleIndex valueIndex) ||
            (typeof(TValue) == typeof(string) && !valueIndex.IncludeEmptyStrings) ||
            valueIndex.Index is not LibraDexIndex<TValue, ulong> typedValueIndex ||
            typedValueIndex.IdentityKeyMultiplicity != IdentityKeyMultiplicity.SingleKeyPerIdentity)
        {
            return false;
        }

        results = Iterate();
        return true;

        IEnumerable<(TKey Key, NumericAggregateSnapshot<TValue, TSum, TMean> Snapshot)> Iterate()
        {
            using var lifecycle = EnterStoreOperationScope();
            TKey currentKey = default!;
            IndexedGroupNumericAccumulator<TValue, TSum, TMean>? accumulator = null;
            bool hasCurrent = false;
            foreach (LibraDexRuntimeTuple tuple in group.IterateTuples(groupingIndex.Index, condition, QueryDirection.Ascending))
            {
                if (tuple.Key is not TKey key)
                    throw new InvalidOperationException("Grouped aggregate keys cannot be missing or null.");
                if (tuple.Identity is not ulong identity)
                    throw new InvalidDataException($"LibraDex grouped aggregate identity type '{tuple.Identity.GetType().FullName}' is not UInt64.");

                if (hasCurrent && !EqualityComparer<TKey>.Default.Equals(currentKey, key))
                {
                    yield return (currentKey, accumulator!.ToSnapshot());
                    accumulator = null;
                }

                if (accumulator is null)
                    accumulator = new IndexedGroupNumericAccumulator<TValue, TSum, TMean>(distinctValues);

                currentKey = key;
                hasCurrent = true;
                if (typedValueIndex.TryGetSingleKey(identity, out TValue value))
                    accumulator.Add(value);
            }

            if (hasCurrent)
                yield return (currentKey, accumulator!.ToSnapshot());
        }
    }

    /// <summary>
    /// Attempts to count selected records per simple indexed grouping key by scanning LibraDex tuples only.<br/>
    /// Compact group results are reordered against the ordinary selected identity stream so the public first-appearance ordering contract is preserved without record hydration.<br/>
    /// </summary>
    /// <typeparam name="TKey">The indexed grouping-key type.<br/></typeparam>
    /// <param name="keySelector">The simple grouping-key selector.<br/></param>
    /// <param name="where">The optional completely translatable condition.<br/></param>
    /// <param name="results">The grouped counts when this method returns <c>true</c>.<br/></param>
    /// <returns><c>true</c> when a persistent LibraDex grouping index executed the complete grouped count; otherwise <c>false</c>.<br/></returns>
    private bool TryCountGroupsByLibraDex<TKey>(
        Expression<Func<T, TKey>> keySelector,
        Condition<T>.EndCondition? where,
        out IReadOnlyList<AggregateGroup<TKey, ulong>> results)
        where TKey : notnull
    {
        using var lifecycle = EnterStoreOperationScope();
        results = Array.Empty<AggregateGroup<TKey, ulong>>();
        if (!TryPrepareLibraDexGroupedTraversal(
            keySelector,
            where,
            out CatalogIdentityGroupIndexes group,
            out LibraDexSimpleIndex groupingIndex,
            out LibraDexConditionEndCondition condition))
        {
            return false;
        }

        Dictionary<TKey, ulong> counts = new();
        Dictionary<ulong, TKey> keysByIdentity = new();
        foreach (LibraDexRuntimeTuple tuple in group.IterateTuples(groupingIndex.Index, condition, QueryDirection.Ascending))
        {
            if (tuple.Key is not TKey key)
                throw new InvalidOperationException("Grouped aggregate keys cannot be null.");
            if (tuple.Identity is not ulong identity)
                throw new InvalidDataException($"LibraDex grouped aggregate identity type '{tuple.Identity.GetType().FullName}' is not UInt64.");

            keysByIdentity[identity] = key;
            counts.TryGetValue(key, out ulong count);
            counts[key] = checked(count + 1);
        }

        IReadOnlyList<KeyValuePair<TKey, ulong>> ordered = OrderIndexedAggregateGroups(counts, keysByIdentity, where);
        AggregateGroup<TKey, ulong>[] projected = new AggregateGroup<TKey, ulong>[ordered.Count];
        for (int i = 0; i < ordered.Count; i++)
            projected[i] = new AggregateGroup<TKey, ulong>(ordered[i].Key, ordered[i].Value);

        results = projected;
        return true;
    }

    /// <summary>
    /// Attempts to compute one numeric snapshot per simple indexed grouping key using only LibraDex grouping/value tuple streams.<br/>
    /// The value index is scanned once into an identity map, then the grouping index performs compact per-group reduction without deserializing authoritative record payloads.<br/>
    /// </summary>
    /// <typeparam name="TKey">The indexed grouping-key type.<br/></typeparam>
    /// <typeparam name="TValue">The indexed numeric value type.<br/></typeparam>
    /// <typeparam name="TSum">The checked sum result type.<br/></typeparam>
    /// <typeparam name="TMean">The running-mean result type.<br/></typeparam>
    /// <param name="keySelector">The simple grouping-key selector.<br/></param>
    /// <param name="valueSelector">The simple numeric value selector.<br/></param>
    /// <param name="where">The optional completely translatable condition.<br/></param>
    /// <param name="distinctValues">Whether equal projected values contribute only once inside each group.<br/></param>
    /// <param name="results">The ordered grouped snapshots when this method returns <c>true</c>.<br/></param>
    /// <returns><c>true</c> when persistent LibraDex indexes executed the complete grouped reduction; otherwise <c>false</c>.<br/></returns>
    private bool TryComputeNumericGroupsByLibraDex<TKey, TValue, TSum, TMean>(
        Expression<Func<T, TKey>> keySelector,
        Expression<Func<T, TValue?>> valueSelector,
        Condition<T>.EndCondition? where,
        bool distinctValues,
        out IReadOnlyList<(TKey Key, NumericAggregateSnapshot<TValue, TSum, TMean> Snapshot)> results)
        where TKey : notnull
        where TValue : struct, INumber<TValue>
        where TSum : struct, INumber<TSum>
        where TMean : struct, INumber<TMean>
    {
        using var lifecycle = EnterStoreOperationScope();
        results = Array.Empty<(TKey, NumericAggregateSnapshot<TValue, TSum, TMean>)>();
        if (!TryPrepareLibraDexGroupedTraversal(
            keySelector,
            where,
            out CatalogIdentityGroupIndexes group,
            out LibraDexSimpleIndex groupingIndex,
            out LibraDexConditionEndCondition condition) ||
            !TryResolveAggregateSelectorPath(valueSelector, out string valuePath) ||
            !TryFindPersistentLibraDexAggregateIndex(valuePath, typeof(TValue), out LibraDexSimpleIndex valueIndex))
        {
            return false;
        }

        Dictionary<ulong, TValue> valuesByIdentity = new();
        foreach (LibraDexRuntimeTuple tuple in group.IterateTuples(valueIndex.Index, condition, QueryDirection.Ascending))
        {
            if (tuple.Key is TValue value && tuple.Identity is ulong identity)
                valuesByIdentity[identity] = value;
        }

        Dictionary<TKey, IndexedGroupNumericAccumulator<TValue, TSum, TMean>> accumulators = new();
        Dictionary<ulong, TKey> keysByIdentity = new();
        foreach (LibraDexRuntimeTuple tuple in group.IterateTuples(groupingIndex.Index, condition, QueryDirection.Ascending))
        {
            if (tuple.Key is not TKey key)
                throw new InvalidOperationException("Grouped aggregate keys cannot be null.");
            if (tuple.Identity is not ulong identity)
                throw new InvalidDataException($"LibraDex grouped aggregate identity type '{tuple.Identity.GetType().FullName}' is not UInt64.");

            keysByIdentity[identity] = key;
            if (!accumulators.TryGetValue(key, out IndexedGroupNumericAccumulator<TValue, TSum, TMean>? accumulator))
            {
                accumulator = new IndexedGroupNumericAccumulator<TValue, TSum, TMean>(distinctValues);
                accumulators.Add(key, accumulator);
            }

            if (valuesByIdentity.TryGetValue(identity, out TValue value))
                accumulator.Add(value);
        }

        IReadOnlyList<KeyValuePair<TKey, IndexedGroupNumericAccumulator<TValue, TSum, TMean>>> ordered =
            OrderIndexedAggregateGroups(accumulators, keysByIdentity, where);
        (TKey Key, NumericAggregateSnapshot<TValue, TSum, TMean> Snapshot)[] projected =
            new (TKey, NumericAggregateSnapshot<TValue, TSum, TMean>)[ordered.Count];
        for (int i = 0; i < ordered.Count; i++)
            projected[i] = (ordered[i].Key, ordered[i].Value.ToSnapshot());

        results = projected;
        return true;
    }

    /// <summary>
    /// Resolves a simple grouping selector, its persistent LibraDex index, and an optional completely translated condition.<br/>
    /// Volatile indexes are excluded because grouped value reduction requires both grouping and aggregate indexes to share one persistent identity universe.<br/>
    /// </summary>
    private bool TryPrepareLibraDexGroupedTraversal<TKey>(
        Expression<Func<T, TKey>> keySelector,
        Condition<T>.EndCondition? where,
        out CatalogIdentityGroupIndexes group,
        out LibraDexSimpleIndex groupingIndex,
        out LibraDexConditionEndCondition condition)
        where TKey : notnull
    {
        group = default!;
        groupingIndex = default;
        condition = default!;
        Type keyType = Nullable.GetUnderlyingType(typeof(TKey)) ?? typeof(TKey);
        if (libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly ||
            libraDexIdentityIndexes is null ||
            !TryResolveAggregateSelectorPath(keySelector, out string keyPath) ||
            !TryFindPersistentLibraDexAggregateIndex(keyPath, keyType, out groupingIndex))
        {
            return false;
        }

        group = libraDexIdentityIndexes;
        if (where is null)
        {
            condition = CreateLibraDexAllCondition(group, groupingIndex.Index);
            return true;
        }

        return where.builder.TryBuildLibraDexConditionTree(
            group,
            HasLibraDexIndexInBoundGroup,
            TryResolveLibraDexDerivedSelector,
            out condition);
    }

    /// <summary>
    /// Finds one maintained persistent simple index matching an exact normalized property path and key type.<br/>
    /// </summary>
    private bool TryFindPersistentLibraDexAggregateIndex(
        string path,
        Type keyType,
        out LibraDexSimpleIndex index)
    {
        LibraDexSimpleIndex[] indexes = libraDexSimpleIndexes;
        for (int i = 0; i < indexes.Length; i++)
        {
            LibraDexSimpleIndex candidate = indexes[i];
            if (candidate.OwnerCatalog is null &&
                candidate.KeyType == keyType &&
                string.Equals(candidate.Path, path, StringComparison.Ordinal))
            {
                index = candidate;
                return true;
            }
        }

        index = default;
        return false;
    }

    /// <summary>
    /// Reorders compact indexed group results by the first matching identity encountered in the ordinary aggregate record stream.<br/>
    /// This preserves the public first-appearance contract while all ordering work remains identity-only and therefore avoids payload hydration.<br/>
    /// </summary>
    private IReadOnlyList<KeyValuePair<TKey, TValue>> OrderIndexedAggregateGroups<TKey, TValue>(
        Dictionary<TKey, TValue> groups,
        Dictionary<ulong, TKey> keysByIdentity,
        Condition<T>.EndCondition? where)
        where TKey : notnull
    {
        List<KeyValuePair<TKey, TValue>> ordered = new(groups.Count);
        HashSet<TKey> emitted = new();
        IEnumerable<ulong> identities = where is null ? IterateRecordIDs() : IterateRecordIDs(where);
        foreach (ulong identity in identities)
        {
            if (keysByIdentity.TryGetValue(identity, out TKey? key) &&
                emitted.Add(key) &&
                groups.TryGetValue(key, out TValue? value))
            {
                ordered.Add(new KeyValuePair<TKey, TValue>(key, value));
            }
        }

        foreach (KeyValuePair<TKey, TValue> group in groups)
        {
            if (emitted.Add(group.Key))
                ordered.Add(group);
        }

        return ordered;
    }

    /// <summary>
    /// Holds the allocation-light mutable numeric state for one indexed aggregate group.<br/>
    /// A per-group hash set is created only for distinct terminals; ordinary grouped terminals keep constant state regardless of group size.<br/>
    /// </summary>
    private sealed class IndexedGroupNumericAccumulator<TValue, TSum, TMean>
        where TValue : struct, INumber<TValue>
        where TSum : struct, INumber<TSum>
        where TMean : struct, INumber<TMean>
    {
        private readonly HashSet<TValue>? distinct;
        private ulong count;
        private TSum sum = TSum.Zero;
        private TMean mean = TMean.Zero;
        private TValue minimum;
        private TValue maximum;
        private double varianceMean;
        private double varianceM2;

        /// <summary>
        /// Initializes one empty per-group accumulator.<br/>
        /// </summary>
        /// <param name="distinctValues">Whether duplicate values are suppressed inside this group.<br/></param>
        internal IndexedGroupNumericAccumulator(bool distinctValues)
        {
            distinct = distinctValues ? new HashSet<TValue>() : null;
        }

        /// <summary>
        /// Adds one indexed numeric value using checked sum conversion, a running mean, extrema comparisons, and Welford dispersion.<br/>
        /// </summary>
        /// <param name="value">The non-null indexed value contributed by one grouped identity.<br/></param>
        internal void Add(TValue value)
        {
            if (distinct is not null && !distinct.Add(value))
                return;

            count = checked(count + 1);
            sum = checked(sum + TSum.CreateChecked(value));
            TMean meanValue = TMean.CreateChecked(value);
            mean += (meanValue - mean) / TMean.CreateChecked(count);
            if (count == 1)
            {
                minimum = value;
                maximum = value;
            }
            else
            {
                if (value < minimum)
                    minimum = value;
                if (value > maximum)
                    maximum = value;
            }

            double varianceValue = double.CreateChecked(value);
            double delta = varianceValue - varianceMean;
            varianceMean += delta / count;
            varianceM2 += delta * (varianceValue - varianceMean);
        }

        /// <summary>
        /// Freezes the current per-group state into the shared numeric snapshot contract.<br/>
        /// </summary>
        /// <returns>A snapshot with no captured trim values because grouped terminals do not expose trimmed means.<br/></returns>
        internal NumericAggregateSnapshot<TValue, TSum, TMean> ToSnapshot()
            => new(
                count,
                MissingCount: 0,
                sum,
                count == 0 ? null : mean,
                count == 0 ? null : minimum,
                count == 0 ? null : maximum,
                count == 0 ? null : varianceM2 / count,
                count < 2 ? null : varianceM2 / (count - 1),
                []);
    }

    /// <summary>
    /// Opens a deferred one-pass grouped minimum/maximum stream over compatible persistent LibraDex indexes.<br/>
    /// The grouping index supplies adjacent group boundaries and the exact single-key value index supplies each selected identity's comparable value; neither complete records nor complete result sets are materialized.<br/>
    /// </summary>
    /// <typeparam name="TKey">The indexed non-null grouping-key type.<br/></typeparam>
    /// <typeparam name="TValue">The indexed directly comparable value type.<br/></typeparam>
    /// <param name="keySelector">The simple grouping-key selector.<br/></param>
    /// <param name="valueSelector">The simple extrema-value selector.<br/></param>
    /// <param name="where">The optional condition that must translate completely into the same persistent identity group.<br/></param>
    /// <param name="results">The deferred ascending grouping-index stream when compatible physical indexes exist.<br/></param>
    /// <returns><see langword="true"/> when preparation found a complete streaming route; otherwise <see langword="false"/> so the caller can use its authoritative compact fallback.<br/></returns>
    private bool TryIterateMinMaxGroupsByLibraDex<TKey, TValue>(
        Expression<Func<T, TKey>> keySelector,
        Expression<Func<T, TValue>> valueSelector,
        Condition<T>.EndCondition? where,
        out IEnumerable<AggregateMinMaxGroup<TKey, TValue>> results)
        where TKey : notnull
        where TValue : notnull, IComparable<TValue>
    {
        using var preparation = EnterStoreOperationScope();
        results = Array.Empty<AggregateMinMaxGroup<TKey, TValue>>();
        if (!TryPrepareLibraDexGroupedTraversal(
            keySelector,
            where,
            out CatalogIdentityGroupIndexes group,
            out LibraDexSimpleIndex groupingIndex,
            out LibraDexConditionEndCondition condition) ||
            !TryResolveAggregateSelectorPath(valueSelector, out string valuePath) ||
            !TryFindPersistentLibraDexAggregateIndex(valuePath, typeof(TValue), out LibraDexSimpleIndex valueIndex) ||
            valueIndex.Index is not LibraDexIndex<TValue, ulong> typedValueIndex ||
            typedValueIndex.IdentityKeyMultiplicity != IdentityKeyMultiplicity.SingleKeyPerIdentity)
        {
            return false;
        }

        results = Iterate();
        return true;

        IEnumerable<AggregateMinMaxGroup<TKey, TValue>> Iterate()
        {
            using var lifecycle = EnterStoreOperationScope();
            TKey currentKey = default!;
            TValue minimum = default!;
            TValue maximum = default!;
            bool hasCurrentGroup = false;
            bool hasCurrentValue = false;

            foreach (LibraDexRuntimeTuple tuple in group.IterateTuples(groupingIndex.Index, condition, QueryDirection.Ascending))
            {
                if (tuple.Key is not TKey key)
                    throw new InvalidOperationException("Grouped aggregate keys cannot be missing or null.");
                if (tuple.Identity is not ulong identity)
                    throw new InvalidDataException($"LibraDex grouped aggregate identity type '{tuple.Identity.GetType().FullName}' is not UInt64.");

                if (hasCurrentGroup && !EqualityComparer<TKey>.Default.Equals(currentKey, key))
                {
                    if (hasCurrentValue)
                        yield return new AggregateMinMaxGroup<TKey, TValue>(currentKey, minimum, maximum);
                    hasCurrentValue = false;
                }

                currentKey = key;
                hasCurrentGroup = true;
                if (!typedValueIndex.TryGetSingleKey(identity, out TValue value) || value is null)
                    continue;
                if (!hasCurrentValue)
                {
                    minimum = value;
                    maximum = value;
                    hasCurrentValue = true;
                    continue;
                }
                if (value.CompareTo(minimum) < 0)
                    minimum = value;
                if (value.CompareTo(maximum) > 0)
                    maximum = value;
            }

            if (hasCurrentGroup && hasCurrentValue)
                yield return new AggregateMinMaxGroup<TKey, TValue>(currentKey, minimum, maximum);
        }
    }

    /// <summary>
    /// Provides a forward-only reader over one minimum/maximum result per grouping key.<br/>
    /// <see cref="Prepare"/> is optional and idempotent: indexed routes only open their deferred cursor, while unsupported routes perform one O(group-count) compact reduction; <see cref="Read"/> and <see cref="MoveNext"/> prepare automatically when necessary.<br/>
    /// </summary>
    /// <typeparam name="TKey">The non-null grouping-key type.<br/></typeparam>
    /// <typeparam name="TValue">The directly comparable extrema type.<br/></typeparam>
    public sealed class GroupedMinMaxReader<TKey, TValue> : IDisposable
        where TKey : notnull
        where TValue : notnull, IComparable<TValue>
    {
        private readonly Func<IEnumerable<AggregateMinMaxGroup<TKey, TValue>>?> openIndexed;
        private readonly Func<AggregateMinMaxGroup<TKey, TValue>[]> prepareFallback;
        private IEnumerator<AggregateMinMaxGroup<TKey, TValue>>? indexed;
        private AggregateMinMaxGroup<TKey, TValue>[]? buffered;
        private AggregateMinMaxGroup<TKey, TValue> current;
        private int bufferedNext;
        private long ordinal = -1;
        private bool hasCurrent;
        private bool prepared;
        private bool disposed;

        /// <summary>
        /// Creates one unprepared grouped extrema reader from mutually exclusive indexed and authoritative fallback factories.<br/>
        /// </summary>
        /// <param name="openIndexed">Factory that returns a deferred indexed stream when the current topology can execute it completely.<br/></param>
        /// <param name="prepareFallback">Factory that performs the compact authoritative reduction only when no indexed stream is available.<br/></param>
        internal GroupedMinMaxReader(
            Func<IEnumerable<AggregateMinMaxGroup<TKey, TValue>>?> openIndexed,
            Func<AggregateMinMaxGroup<TKey, TValue>[]> prepareFallback)
        {
            this.openIndexed = openIndexed ?? throw new ArgumentNullException(nameof(openIndexed));
            this.prepareFallback = prepareFallback ?? throw new ArgumentNullException(nameof(prepareFallback));
        }

        /// <summary>Gets the current grouped extrema row after a successful <see cref="Read"/>.<br/></summary>
        public AggregateMinMaxGroup<TKey, TValue> Current
            => hasCurrent ? current : throw new InvalidOperationException("The grouped extrema reader is not positioned on a result.");

        /// <summary>Gets whether the reader has selected and prepared its indexed or fallback execution route.<br/></summary>
        public bool IsPrepared => prepared;

        /// <summary>Gets the zero-based result ordinal, or -1 before the first successful read.<br/></summary>
        public long Ordinal => ordinal;

        /// <summary>
        /// Selects and prepares the current execution route without advancing the public result cursor.<br/>
        /// Compatible indexes open a deferred stream without traversing it; fallback execution scans once and retains only one compact extrema row per group.<br/>
        /// Repeated calls are no-ops.<br/>
        /// </summary>
        public void Prepare()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (prepared)
                return;

            IEnumerable<AggregateMinMaxGroup<TKey, TValue>>? stream = openIndexed();
            if (stream is not null)
                indexed = stream.GetEnumerator();
            else
                buffered = prepareFallback();
            prepared = true;
        }

        /// <summary>
        /// Advances to the next completed grouping result.<br/>
        /// The first call automatically invokes <see cref="Prepare"/> when the developer has not done so explicitly.<br/>
        /// </summary>
        /// <returns><see langword="true"/> when <see cref="Current"/> is available; otherwise <see langword="false"/>.<br/></returns>
        public bool Read()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            hasCurrent = false;
            Prepare();

            if (indexed is not null)
            {
                if (!indexed.MoveNext())
                    return false;
                current = indexed.Current;
            }
            else
            {
                if ((uint)bufferedNext >= (uint)buffered!.Length)
                    return false;
                current = buffered[bufferedNext++];
            }

            hasCurrent = true;
            ordinal++;
            return true;
        }

        /// <summary>Aliases <see cref="Read"/> for ordinary enumerator vocabulary.<br/></summary>
        /// <returns><see langword="true"/> when another grouped extrema row is available.<br/></returns>
        public bool MoveNext() => Read();

        /// <summary>
        /// Releases the active indexed cursor and prevents further preparation or reading.<br/>
        /// Buffered fallback rows are ordinary managed memory and require no separate release.<br/>
        /// </summary>
        public void Dispose()
        {
            if (disposed)
                return;
            indexed?.Dispose();
            indexed = null;
            buffered = null;
            hasCurrent = false;
            disposed = true;
        }
    }
}

/// <summary>
/// Carries a one-pass numeric summary with explicit population and sample dispersion results.<br/>
/// Null projected values do not contribute to numeric fields and are reported separately through <see cref="MissingCount"/>.<br/>
/// </summary>
/// <typeparam name="TValue">The selected numeric type used by extrema.<br/></typeparam>
/// <typeparam name="TSum">The checked sum result type.<br/></typeparam>
/// <typeparam name="TMean">The arithmetic-average result type.<br/></typeparam>
/// <param name="Count">The contributing value count after optional distinct projection.<br/></param>
/// <param name="MissingCount">The number of selected records whose projected value was null.<br/></param>
/// <param name="Sum">The checked sum, or numeric zero when no value contributes.<br/></param>
/// <param name="Average">The arithmetic average, or null when no value contributes.<br/></param>
/// <param name="Minimum">The minimum contributing value, or null for empty input.<br/></param>
/// <param name="Maximum">The maximum contributing value, or null for empty input.<br/></param>
/// <param name="PopulationVariance">Population variance, or null for empty input.<br/></param>
/// <param name="SampleVariance">Sample variance, or null when fewer than two values contribute.<br/></param>
/// <param name="PopulationStandardDeviation">Population standard deviation, or null for empty input.<br/></param>
/// <param name="SampleStandardDeviation">Sample standard deviation, or null when fewer than two values contribute.<br/></param>
public readonly record struct AggregateStatistics<TValue, TSum, TMean>(
    ulong Count,
    ulong MissingCount,
    TSum Sum,
    TMean? Average,
    TValue? Minimum,
    TValue? Maximum,
    double? PopulationVariance,
    double? SampleVariance,
    double? PopulationStandardDeviation,
    double? SampleStandardDeviation)
    where TValue : struct
    where TSum : struct
    where TMean : struct;

/// <summary>
/// Represents one strongly typed single-key grouped aggregate result.<br/>
/// </summary>
/// <typeparam name="TKey">The grouping-key type.<br/></typeparam>
/// <typeparam name="TResult">The aggregate result type.<br/></typeparam>
/// <param name="Key">The grouping key.<br/></param>
/// <param name="Value">The aggregate result for that group.<br/></param>
public readonly record struct AggregateGroup<TKey, TResult>(TKey Key, TResult Value);

/// <summary>
/// Represents one grouping key with the minimum and maximum directly comparable values found in that group.<br/>
/// </summary>
/// <typeparam name="TKey">The grouping-key type.<br/></typeparam>
/// <typeparam name="TValue">The extrema value type.<br/></typeparam>
/// <param name="Key">The grouping key.<br/></param>
/// <param name="Minimum">The smallest contributing value.<br/></param>
/// <param name="Maximum">The largest contributing value.<br/></param>
public readonly record struct AggregateMinMaxGroup<TKey, TValue>(TKey Key, TValue Minimum, TValue Maximum);
