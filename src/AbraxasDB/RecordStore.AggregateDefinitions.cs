using System.Linq.Expressions;
using System.Numerics;
using System.Runtime.InteropServices;
using Inheto;
using LibraDex;

namespace AbraxasDB;

public partial class RecordStore<T>
{
    public sealed partial class AggregateFacade
    {
        /// <summary>
        /// Starts a reusable aggregate definition by grouping records through one typed expression.<br/>
        /// Definition is semantic only; no record is read until <see cref="Open{TKey, T1}(AggregateDefinition{TKey, T1})"/> opens the completed shape.<br/>
        /// </summary>
        /// <typeparam name="TValue">Grouping-key type.<br/></typeparam>
        /// <param name="key">Grouping expression.<br/></param>
        /// <param name="where">Optional condition applied before grouping and reduction.<br/></param>
        /// <returns>A grouped definition on which output fields and reductions can be declared.<br/></returns>
        public AggregateDefinition<TValue> GroupBy<TValue>(
            Expression<Func<T, TValue>> key,
            Condition<T>.EndCondition? where = null)
            where TValue : notnull
            => new AggregateDefinitionStart(this, where).GroupBy(key);

        /// <summary>
        /// Starts a reusable aggregate definition by grouping records through one validated textual property path.<br/>
        /// The path is validated while the semantic definition is constructed; execution remains deferred until the completed shape is opened.<br/>
        /// </summary>
        /// <typeparam name="TValue">Grouping-key type.<br/></typeparam>
        /// <param name="propPath">Grouping property path.<br/></param>
        /// <param name="where">Optional condition applied before grouping and reduction.<br/></param>
        /// <returns>A grouped definition on which output fields and reductions can be declared.<br/></returns>
        public AggregateDefinition<TValue> GroupBy<TValue>(
            string propPath,
            Condition<T>.EndCondition? where = null)
            where TValue : notnull
            => new AggregateDefinitionStart(this, where).GroupBy<TValue>(propPath);

        /// <summary>
        /// Starts a reusable aggregate definition by grouping records through one reusable selector.<br/>
        /// Selector identity is retained for backend planning and developer-created expression-index matching; execution remains deferred until the completed shape is opened.<br/>
        /// </summary>
        /// <typeparam name="TValue">Grouping-key type.<br/></typeparam>
        /// <param name="key">Reusable grouping selector.<br/></param>
        /// <param name="where">Optional condition applied before grouping and reduction.<br/></param>
        /// <returns>A grouped definition on which output fields and reductions can be declared.<br/></returns>
        public AggregateDefinition<TValue> GroupBy<TValue>(
            Selector<T>.IValue<TValue> key,
            Condition<T>.EndCondition? where = null)
            where TValue : notnull
            => new AggregateDefinitionStart(this, where).GroupBy(key);

        /// <summary>
        /// Starts a reusable aggregate definition with two typed grouping expressions.<br/>
        /// Both key parts remain independently typed and can be projected as flat values through <see cref="AggregateGroupDefinition{TKey1, TKey2}.AddKeys"/>.<br/>
        /// </summary>
        /// <typeparam name="TKey1">First grouping-key type.<br/></typeparam>
        /// <typeparam name="TKey2">Second grouping-key type.<br/></typeparam>
        /// <param name="key1">First grouping expression.<br/></param>
        /// <param name="key2">Second grouping expression.<br/></param>
        /// <param name="where">Optional condition applied before grouping and reduction.<br/></param>
        /// <returns>A two-key grouped definition.<br/></returns>
        public AggregateGroupDefinition<TKey1, TKey2> GroupBy<TKey1, TKey2>(
            Expression<Func<T, TKey1>> key1,
            Expression<Func<T, TKey2>> key2,
            Condition<T>.EndCondition? where = null)
            where TKey1 : notnull
            where TKey2 : notnull
            => new AggregateDefinitionStart(this, where).GroupBy(key1, key2);

        /// <summary>
        /// Starts a reusable aggregate definition with two validated textual grouping paths.<br/>
        /// Both key parts remain independently typed and can be projected as flat values through <see cref="AggregateGroupDefinition{TKey1, TKey2}.AddKeys"/>.<br/>
        /// </summary>
        /// <typeparam name="TKey1">First grouping-key type.<br/></typeparam>
        /// <typeparam name="TKey2">Second grouping-key type.<br/></typeparam>
        /// <param name="key1">First grouping property path.<br/></param>
        /// <param name="key2">Second grouping property path.<br/></param>
        /// <param name="where">Optional condition applied before grouping and reduction.<br/></param>
        /// <returns>A two-key grouped definition.<br/></returns>
        public AggregateGroupDefinition<TKey1, TKey2> GroupBy<TKey1, TKey2>(
            string key1,
            string key2,
            Condition<T>.EndCondition? where = null)
            where TKey1 : notnull
            where TKey2 : notnull
            => new AggregateDefinitionStart(this, where).GroupBy<TKey1, TKey2>(key1, key2);

        /// <summary>
        /// Starts a reusable aggregate definition by calculating a grouping key from one typed source expression.<br/>
        /// The calculation is compiled once and invoked only when the aggregate executes.<br/>
        /// </summary>
        /// <typeparam name="TSource">Source-value type.<br/></typeparam>
        /// <typeparam name="TKey">Calculated grouping-key type.<br/></typeparam>
        /// <param name="source">Source expression supplied to the calculation.<br/></param>
        /// <param name="calculation">Calculation that produces the grouping key.<br/></param>
        /// <param name="where">Optional condition applied before grouping and reduction.<br/></param>
        /// <returns>A grouped definition on which output fields and reductions can be declared.<br/></returns>
        public AggregateDefinition<TKey> GroupByCalc<TSource, TKey>(
            Expression<Func<T, TSource>> source,
            Func<TSource, TKey> calculation,
            Condition<T>.EndCondition? where = null)
            where TKey : notnull
            => new AggregateDefinitionStart(this, where).GroupByCalc(source, calculation);

        /// <summary>
        /// Starts a reusable aggregate definition by calculating a grouping key from one validated textual property path.<br/>
        /// Missing or null sources create no group; malformed paths fail while the semantic definition is constructed.<br/>
        /// </summary>
        /// <typeparam name="TSource">Source-value type.<br/></typeparam>
        /// <typeparam name="TKey">Calculated grouping-key type.<br/></typeparam>
        /// <param name="propPath">Source property path supplied to the calculation.<br/></param>
        /// <param name="calculation">Calculation that produces the grouping key.<br/></param>
        /// <param name="where">Optional condition applied before grouping and reduction.<br/></param>
        /// <returns>A grouped definition on which output fields and reductions can be declared.<br/></returns>
        public AggregateDefinition<TKey> GroupByCalc<TSource, TKey>(
            string propPath,
            Func<TSource, TKey> calculation,
            Condition<T>.EndCondition? where = null)
            where TKey : notnull
            => new AggregateDefinitionStart(this, where).GroupByCalc(propPath, calculation);

        /// <summary>
        /// Opens a typed aggregate reader for a one-value result definition.<br/>
        /// Opening is fast and does not enumerate records; <see cref="AggregateReader{TResult}.Prepare"/> or the first read performs preparation.<br/>
        /// </summary>
        public AggregateReader<T1> Open<TKey, T1>(AggregateDefinition<TKey, T1> definition)
            where TKey : notnull
            => OpenDefinition(definition);

        /// <summary>
        /// Opens a typed aggregate reader for a two-value tuple result definition.<br/>
        /// </summary>
        public AggregateReader<(T1 Value1, T2 Value2)> Open<TKey, T1, T2>(AggregateDefinition<TKey, T1, T2> definition)
            where TKey : notnull
            => OpenDefinition(definition);

        /// <summary>
        /// Opens a typed aggregate reader for a three-value tuple result definition.<br/>
        /// </summary>
        public AggregateReader<(T1 Value1, T2 Value2, T3 Value3)> Open<TKey, T1, T2, T3>(AggregateDefinition<TKey, T1, T2, T3> definition)
            where TKey : notnull
            => OpenDefinition(definition);

        /// <summary>
        /// Opens a typed aggregate reader for a four-value tuple result definition.<br/>
        /// </summary>
        public AggregateReader<(T1 Value1, T2 Value2, T3 Value3, T4 Value4)> Open<TKey, T1, T2, T3, T4>(AggregateDefinition<TKey, T1, T2, T3, T4> definition)
            where TKey : notnull
            => OpenDefinition(definition);

        private AggregateReader<TResult> OpenDefinition<TKey, TResult>(AggregateDefinitionBase<TKey, TResult> definition)
            where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (!ReferenceEquals(store, definition.Owner.store) || distinctValues != definition.Owner.distinctValues)
                throw new InvalidOperationException("An aggregate definition can only be opened by its owning store and multiplicity facade.");
            return new AggregateReader<TResult>(definition.Plan.Prepare);
        }

        /// <summary>
        /// Represents the semantic starting point of one aggregate result shape.<br/>
        /// Grouping and output declarations return new immutable stages; retaining this stage does not retain execution state.<br/>
        /// </summary>
        private sealed class AggregateDefinitionStart
        {
            private readonly AggregateFacade owner;
            private readonly Condition<T>.EndCondition? where;

            internal AggregateDefinitionStart(AggregateFacade owner, Condition<T>.EndCondition? where)
            {
                this.owner = owner;
                this.where = where;
            }

            /// <summary>
            /// Groups records by one typed expression.<br/>
            /// A simple member expression reads its value directly from Inheto bytes; a calculated expression materializes only when execution requires it.<br/>
            /// </summary>
            public AggregateDefinition<TValue> GroupBy<TValue>(Expression<Func<T, TValue>> key)
                where TValue : notnull
            {
                ArgumentNullException.ThrowIfNull(key);
                return Create(owner.CreateExpressionReader(key));
            }

            /// <summary>
            /// Groups records by one validated textual property path.<br/>
            /// The path is validated once while defining the operation and is executed from each authoritative Inheto payload.<br/>
            /// </summary>
            public AggregateDefinition<TValue> GroupBy<TValue>(string propPath)
                where TValue : notnull
                => Create(owner.CreatePathReader<TValue>(propPath));

            /// <summary>
            /// Groups records by one reusable selector while retaining its backend-neutral semantic identity.<br/>
            /// The selector is read from authoritative payload bytes unless execution resolves a compatible maintained index.<br/>
            /// </summary>
            public AggregateDefinition<TValue> GroupBy<TValue>(Selector<T>.IValue<TValue> key)
                where TValue : notnull
            {
                ArgumentNullException.ThrowIfNull(key);
                return Create(owner.CreateSelectorReader(key));
            }

            /// <summary>
            /// Groups records by two typed expressions while keeping both key parts available for flat result projection.<br/>
            /// Direct member expressions are read independently from authoritative Inheto bytes; no combined string key or complete record is required.<br/>
            /// </summary>
            /// <typeparam name="TKey1">First grouping-key type.<br/></typeparam>
            /// <typeparam name="TKey2">Second grouping-key type.<br/></typeparam>
            /// <param name="key1">First grouping expression.<br/></param>
            /// <param name="key2">Second grouping expression.<br/></param>
            /// <returns>A two-key definition stage whose <see cref="AggregateGroupDefinition{TKey1, TKey2}.AddKeys"/> method projects both keys as flat result values.<br/></returns>
            public AggregateGroupDefinition<TKey1, TKey2> GroupBy<TKey1, TKey2>(
                Expression<Func<T, TKey1>> key1,
                Expression<Func<T, TKey2>> key2)
                where TKey1 : notnull
                where TKey2 : notnull
                => CreateTwo(owner.CreateExpressionReader(key1), owner.CreateExpressionReader(key2));

            /// <summary>
            /// Groups records by two validated textual property paths while keeping both key parts available for flat result projection.<br/>
            /// Missing or null values in either path omit that record from grouped output, matching the ordinary single-key definition contract.<br/>
            /// </summary>
            /// <typeparam name="TKey1">First grouping-key type.<br/></typeparam>
            /// <typeparam name="TKey2">Second grouping-key type.<br/></typeparam>
            /// <param name="key1">First property path.<br/></param>
            /// <param name="key2">Second property path.<br/></param>
            /// <returns>A two-key definition stage.<br/></returns>
            public AggregateGroupDefinition<TKey1, TKey2> GroupBy<TKey1, TKey2>(string key1, string key2)
                where TKey1 : notnull
                where TKey2 : notnull
                => CreateTwo(owner.CreatePathReader<TKey1>(key1), owner.CreatePathReader<TKey2>(key2));

            /// <summary>
            /// Groups records by a developer calculation over one typed source expression.<br/>
            /// The calculation is compiled once and invoked only when the aggregate executes.<br/>
            /// </summary>
            public AggregateDefinition<TKey> GroupByCalc<TSource, TKey>(
                Expression<Func<T, TSource>> source,
                Func<TSource, TKey> calculation)
                where TKey : notnull
            {
                ArgumentNullException.ThrowIfNull(source);
                ArgumentNullException.ThrowIfNull(calculation);
                PayloadReader<TSource> read = owner.CreateExpressionReader(source);
                return Create(new PayloadReader<TKey>(payload =>
                {
                    ReadValue<TSource> value = read.Read(payload);
                    return value.HasValue
                        ? new ReadValue<TKey>(true, calculation(value.Value))
                        : default;
                }));
            }

            /// <summary>
            /// Groups records by a developer calculation over one validated textual property path.<br/>
            /// Missing or null sources do not create a group; malformed paths fail while the definition is constructed.<br/>
            /// </summary>
            public AggregateDefinition<TKey> GroupByCalc<TSource, TKey>(
                string propPath,
                Func<TSource, TKey> calculation)
                where TKey : notnull
            {
                ArgumentNullException.ThrowIfNull(calculation);
                PayloadReader<TSource> read = owner.CreatePathReader<TSource>(propPath);
                return Create(new PayloadReader<TKey>(payload =>
                {
                    ReadValue<TSource> value = read.Read(payload);
                    return value.HasValue
                        ? new ReadValue<TKey>(true, calculation(value.Value))
                        : default;
                }));
            }

            private AggregateDefinition<TKey> Create<TKey>(PayloadReader<TKey> keyReader)
                where TKey : notnull
            {
                var plan = new AggregatePlan<TKey, EmptyState, EmptyResult>(
                    owner,
                    where,
                    keyReader,
                    static () => default,
                    static (ref EmptyState _, TKey _, InhetoBinary _) => { },
                    static (in EmptyState _, TKey _) => default,
                    prepareIndexed: static () => static (ref EmptyState _, TKey _, ulong _) => { },
                    prepareComposite: static _ => static (ref EmptyState _, TKey _, AggregateCompositeValues _) => { });
                return new AggregateDefinition<TKey>(owner, plan);
            }

            private AggregateGroupDefinition<TKey1, TKey2> CreateTwo<TKey1, TKey2>(
                PayloadReader<TKey1> read1,
                PayloadReader<TKey2> read2)
                where TKey1 : notnull
                where TKey2 : notnull
            {
                var read = new PayloadReader<AggregateKey<TKey1, TKey2>>(payload =>
                {
                    ReadValue<TKey1> value1 = read1.Read(payload);
                    if (!value1.HasValue || value1.Value is null)
                        return default;
                    ReadValue<TKey2> value2 = read2.Read(payload);
                    return !value2.HasValue || value2.Value is null
                        ? default
                        : new ReadValue<AggregateKey<TKey1, TKey2>>(
                            true,
                            new AggregateKey<TKey1, TKey2>(value1.Value, value2.Value));
                }, grouping: new TwoKeyGroupingSource<TKey1, TKey2>(read1.Path, read2.Path));
                return new AggregateGroupDefinition<TKey1, TKey2>(Create(read));
            }
        }

        /// <summary>
        /// Holds two strongly typed grouping-key values without concatenation, boxing, or string ambiguity.<br/>
        /// Record-struct equality keeps the two key domains independently typed.<br/>
        /// </summary>
        public readonly record struct AggregateKey<TKey1, TKey2>(TKey1 Value1, TKey2 Value2)
            where TKey1 : notnull
            where TKey2 : notnull;

        /// <summary>
        /// Represents a semantic two-key grouping before its flat key outputs are declared.<br/>
        /// The stage performs no traversal and exists only to keep the common multi-key result shape discoverable in IntelliSense.<br/>
        /// </summary>
        public sealed class AggregateGroupDefinition<TKey1, TKey2>
            where TKey1 : notnull
            where TKey2 : notnull
        {
            private readonly AggregateDefinition<AggregateKey<TKey1, TKey2>> definition;

            /// <summary>Captures one immutable two-key definition.<br/></summary>
            internal AggregateGroupDefinition(AggregateDefinition<AggregateKey<TKey1, TKey2>> definition)
                => this.definition = definition;

            /// <summary>
            /// Projects both grouping-key parts as the first two flat result values without rereading or recalculating either path.<br/>
            /// Further reductions append through the ordinary typed aggregate-definition methods.<br/>
            /// </summary>
            /// <returns>A definition whose first two result values are the first and second grouping keys.<br/></returns>
            public AggregateDefinition<AggregateKey<TKey1, TKey2>, TKey1, TKey2> AddKeys()
                => definition.AddProjectedKey(static key => key.Value1)
                    .AddProjectedKey(static key => key.Value2);
        }

        /// <summary>
        /// Represents a grouped aggregate definition before its first output value is declared.<br/>
        /// </summary>
        public sealed partial class AggregateDefinition<TKey>
            where TKey : notnull
        {
            private readonly AggregateFacade owner;
            private readonly AggregatePlanBase<TKey, EmptyResult> plan;

            internal AggregateDefinition(AggregateFacade owner, AggregatePlanBase<TKey, EmptyResult> plan)
            {
                this.owner = owner;
                this.plan = plan;
            }

            /// <summary>Projects the retained grouping key as the first result value without recalculating it.<br/></summary>
            public AggregateDefinition<TKey, TKey> AddKey()
                => Add(CreateKeyReducer<TKey>());

            /// <summary>Adds the number of source records entering each group.<br/></summary>
            public AggregateDefinition<TKey, ulong> AddCount()
                => Add(CreateCountReducer<TKey>());

            /// <summary>Adds one representative property value from the first record entering each group.<br/></summary>
            public AggregateDefinition<TKey, TValue> AddProp<TValue>(string propPath)
                => Add(owner.CreateFirstReducer<TKey, TValue>(owner.CreatePathReader<TValue>(propPath)));

            /// <summary>Adds one representative calculation from the first record entering each group.<br/></summary>
            public AggregateDefinition<TKey, TValue> AddCalc<TValue>(Expression<Func<T, TValue>> calculation)
                => Add(owner.CreateFirstReducer<TKey, TValue>(owner.CreateExpressionReader(calculation)));

            /// <summary>Adds one representative calculation over a typed source expression from the first record entering each group.<br/></summary>
            public AggregateDefinition<TKey, TValue> AddCalc<TSource, TValue>(Expression<Func<T, TSource>> source, Func<TSource, TValue> calculation)
                => Add(owner.CreateCalculatedFirstReducer<TKey, TSource, TValue>(owner.CreateExpressionReader(source), calculation));

            /// <summary>Adds one representative calculation over a validated textual property path from the first record entering each group.<br/></summary>
            public AggregateDefinition<TKey, TValue> AddCalc<TSource, TValue>(string propPath, Func<TSource, TValue> calculation)
                => Add(owner.CreateCalculatedFirstReducer<TKey, TSource, TValue>(owner.CreatePathReader<TSource>(propPath), calculation));

            /// <summary>Adds the checked sum of one numeric expression.<br/></summary>
            public AggregateDefinition<TKey, TNumber> AddSum<TNumber>(Expression<Func<T, TNumber>> value)
                where TNumber : struct, INumber<TNumber>
                => Add(owner.CreateSumReducer<TKey, TNumber>(owner.CreateExpressionReader(value)));

            /// <summary>Adds the checked sum of one reusable numeric selector.<br/></summary>
            public AggregateDefinition<TKey, TNumber> AddSum<TNumber>(Selector<T>.IValue<TNumber> value)
                where TNumber : struct, INumber<TNumber>
                => Add(owner.CreateSumReducer<TKey, TNumber>(owner.CreateSelectorReader(value)));

            /// <summary>Adds the arithmetic mean of one numeric expression as a nullable Double.<br/></summary>
            public AggregateDefinition<TKey, double?> AddAverage<TNumber>(Expression<Func<T, TNumber>> value)
                where TNumber : struct, INumber<TNumber>
                => Add(owner.CreateAverageReducer<TKey, TNumber>(owner.CreateExpressionReader(value)));

            /// <summary>Adds the Decimal-preserving arithmetic mean of one Decimal expression.<br/></summary>
            public AggregateDefinition<TKey, decimal?> AddAverage(Expression<Func<T, decimal>> value)
                => Add(owner.CreateDecimalAverageReducer<TKey>(owner.CreateExpressionReader(value)));

            /// <summary>Adds the smallest non-null value produced by one expression.<br/></summary>
            public AggregateDefinition<TKey, TValue?> AddMin<TValue>(Expression<Func<T, TValue>> value)
                where TValue : struct, IComparable<TValue>
                => Add(owner.CreateExtremumReducer<TKey, TValue>(owner.CreateExpressionReader(value), minimum: true));

            /// <summary>Adds the largest non-null value produced by one expression.<br/></summary>
            public AggregateDefinition<TKey, TValue?> AddMax<TValue>(Expression<Func<T, TValue>> value)
                where TValue : struct, IComparable<TValue>
                => Add(owner.CreateExtremumReducer<TKey, TValue>(owner.CreateExpressionReader(value), minimum: false));

            /// <summary>Adds one developer-defined streaming reduction over complete records.<br/></summary>
            public AggregateDefinition<TKey, TResult> AddAggregate<TState, TResult>(
                Func<TState> seed,
                AggregateStep<TState> accumulate,
                AggregateFinish<TState, TResult> finish)
                => Add(owner.CreateCustomReducer<TKey, TState, TResult>(seed, accumulate, finish));

            /// <summary>Adds one developer-defined streaming reduction over values from a typed source expression.<br/></summary>
            public AggregateDefinition<TKey, TResult> AddAggregate<TSource, TState, TResult>(
                Expression<Func<T, TSource>> source,
                Func<TState> seed,
                AggregateValueStep<TSource, TState> accumulate,
                AggregateFinish<TState, TResult> finish)
                => Add(owner.CreateCustomValueReducer<TKey, TSource, TState, TResult>(owner.CreateExpressionReader(source), seed, accumulate, finish));

            /// <summary>Adds one developer-defined streaming reduction over values from a validated textual property path.<br/></summary>
            public AggregateDefinition<TKey, TResult> AddAggregate<TSource, TState, TResult>(
                string propPath,
                Func<TState> seed,
                AggregateValueStep<TSource, TState> accumulate,
                AggregateFinish<TState, TResult> finish)
                => Add(owner.CreateCustomValueReducer<TKey, TSource, TState, TResult>(owner.CreatePathReader<TSource>(propPath), seed, accumulate, finish));

            private AggregateDefinition<TKey, TValue> Add<TState, TValue>(AggregateReducer<TKey, TState, TValue> reducer)
            {
                AggregatePlanBase<TKey, TValue> next = plan.Append(reducer, static (_, value) => value);
                return new AggregateDefinition<TKey, TValue>(owner, next);
            }

            /// <summary>Appends one value projected directly from the retained grouping key.<br/></summary>
            internal AggregateDefinition<TKey, TValue> AddProjectedKey<TValue>(Func<TKey, TValue> project)
                => Add(CreateProjectedKeyReducer(project));
        }

        /// <summary>Represents a typed aggregate definition with one declared output value.<br/></summary>
        public sealed partial class AggregateDefinition<TKey, T1> : AggregateDefinitionBase<TKey, T1>
            where TKey : notnull
        {
            internal AggregateDefinition(AggregateFacade owner, AggregatePlanBase<TKey, T1> plan) : base(owner, plan) { }

            /// <summary>Adds the number of records entering each group as the next result value.<br/></summary>
            public AggregateDefinition<TKey, T1, ulong> AddCount() => Add(CreateCountReducer<TKey>());
            /// <summary>Adds a representative validated property from the first record entering each group.<br/></summary>
            public AggregateDefinition<TKey, T1, TValue> AddProp<TValue>(string path) => Add(Owner.CreateFirstReducer<TKey, TValue>(Owner.CreatePathReader<TValue>(path)));
            /// <summary>Adds a representative expression result from the first record entering each group.<br/></summary>
            public AggregateDefinition<TKey, T1, TValue> AddCalc<TValue>(Expression<Func<T, TValue>> value) => Add(Owner.CreateFirstReducer<TKey, TValue>(Owner.CreateExpressionReader(value)));
            /// <summary>Adds a representative calculation over a typed source expression.<br/></summary>
            public AggregateDefinition<TKey, T1, TValue> AddCalc<TSource, TValue>(Expression<Func<T, TSource>> source, Func<TSource, TValue> calculation) => Add(Owner.CreateCalculatedFirstReducer<TKey, TSource, TValue>(Owner.CreateExpressionReader(source), calculation));
            /// <summary>Adds a representative calculation over a validated textual source path.<br/></summary>
            public AggregateDefinition<TKey, T1, TValue> AddCalc<TSource, TValue>(string path, Func<TSource, TValue> calculation) => Add(Owner.CreateCalculatedFirstReducer<TKey, TSource, TValue>(Owner.CreatePathReader<TSource>(path), calculation));
            /// <summary>Adds the checked numeric sum for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, TNumber> AddSum<TNumber>(Expression<Func<T, TNumber>> value) where TNumber : struct, INumber<TNumber> => Add(Owner.CreateSumReducer<TKey, TNumber>(Owner.CreateExpressionReader(value)));
            /// <summary>Adds the checked sum for one reusable numeric selector.<br/></summary>
            public AggregateDefinition<TKey, T1, TNumber> AddSum<TNumber>(Selector<T>.IValue<TNumber> value) where TNumber : struct, INumber<TNumber> => Add(Owner.CreateSumReducer<TKey, TNumber>(Owner.CreateSelectorReader(value)));
            /// <summary>Adds the arithmetic mean as nullable Double for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, double?> AddAverage<TNumber>(Expression<Func<T, TNumber>> value) where TNumber : struct, INumber<TNumber> => Add(Owner.CreateAverageReducer<TKey, TNumber>(Owner.CreateExpressionReader(value)));
            /// <summary>Adds a Decimal-preserving arithmetic mean for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, decimal?> AddAverage(Expression<Func<T, decimal>> value) => Add(Owner.CreateDecimalAverageReducer<TKey>(Owner.CreateExpressionReader(value)));
            /// <summary>Adds the smallest non-null comparable value for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, TValue?> AddMin<TValue>(Expression<Func<T, TValue>> value) where TValue : struct, IComparable<TValue> => Add(Owner.CreateExtremumReducer<TKey, TValue>(Owner.CreateExpressionReader(value), true));
            /// <summary>Adds the largest non-null comparable value for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, TValue?> AddMax<TValue>(Expression<Func<T, TValue>> value) where TValue : struct, IComparable<TValue> => Add(Owner.CreateExtremumReducer<TKey, TValue>(Owner.CreateExpressionReader(value), false));
            /// <summary>Adds a custom streaming reduction over complete records with by-reference state.<br/></summary>
            public AggregateDefinition<TKey, T1, TResult> AddAggregate<TState, TResult>(Func<TState> seed, AggregateStep<TState> accumulate, AggregateFinish<TState, TResult> finish) => Add(Owner.CreateCustomReducer<TKey, TState, TResult>(seed, accumulate, finish));
            /// <summary>Adds a custom streaming reduction over a typed source expression with by-reference state.<br/></summary>
            public AggregateDefinition<TKey, T1, TResult> AddAggregate<TSource, TState, TResult>(Expression<Func<T, TSource>> source, Func<TState> seed, AggregateValueStep<TSource, TState> accumulate, AggregateFinish<TState, TResult> finish) => Add(Owner.CreateCustomValueReducer<TKey, TSource, TState, TResult>(Owner.CreateExpressionReader(source), seed, accumulate, finish));
            /// <summary>Adds a custom streaming reduction over a validated textual source path with by-reference state.<br/></summary>
            public AggregateDefinition<TKey, T1, TResult> AddAggregate<TSource, TState, TResult>(string path, Func<TState> seed, AggregateValueStep<TSource, TState> accumulate, AggregateFinish<TState, TResult> finish) => Add(Owner.CreateCustomValueReducer<TKey, TSource, TState, TResult>(Owner.CreatePathReader<TSource>(path), seed, accumulate, finish));

            private AggregateDefinition<TKey, T1, TValue> Add<TState, TValue>(AggregateReducer<TKey, TState, TValue> reducer)
                => new(Owner, Plan.Append(reducer, static (prior, value) => (prior, value)));

            /// <summary>Appends one value projected directly from the retained grouping key.<br/></summary>
            internal AggregateDefinition<TKey, T1, TValue> AddProjectedKey<TValue>(Func<TKey, TValue> project)
                => Add(CreateProjectedKeyReducer(project));
        }

        /// <summary>Represents a typed aggregate definition with two declared output values.<br/></summary>
        public sealed partial class AggregateDefinition<TKey, T1, T2> : AggregateDefinitionBase<TKey, (T1 Value1, T2 Value2)>
            where TKey : notnull
        {
            internal AggregateDefinition(AggregateFacade owner, AggregatePlanBase<TKey, (T1, T2)> plan) : base(owner, plan) { }

            /// <summary>Adds the number of records entering each group as the next result value.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, ulong> AddCount() => Add(CreateCountReducer<TKey>());
            /// <summary>Adds a representative validated property from the first record entering each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, TValue> AddProp<TValue>(string path) => Add(Owner.CreateFirstReducer<TKey, TValue>(Owner.CreatePathReader<TValue>(path)));
            /// <summary>Adds a representative expression result from the first record entering each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, TValue> AddCalc<TValue>(Expression<Func<T, TValue>> value) => Add(Owner.CreateFirstReducer<TKey, TValue>(Owner.CreateExpressionReader(value)));
            /// <summary>Adds a representative calculation over a typed source expression.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, TValue> AddCalc<TSource, TValue>(Expression<Func<T, TSource>> source, Func<TSource, TValue> calculation) => Add(Owner.CreateCalculatedFirstReducer<TKey, TSource, TValue>(Owner.CreateExpressionReader(source), calculation));
            /// <summary>Adds a representative calculation over a validated textual source path.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, TValue> AddCalc<TSource, TValue>(string path, Func<TSource, TValue> calculation) => Add(Owner.CreateCalculatedFirstReducer<TKey, TSource, TValue>(Owner.CreatePathReader<TSource>(path), calculation));
            /// <summary>Adds the checked numeric sum for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, TNumber> AddSum<TNumber>(Expression<Func<T, TNumber>> value) where TNumber : struct, INumber<TNumber> => Add(Owner.CreateSumReducer<TKey, TNumber>(Owner.CreateExpressionReader(value)));
            /// <summary>Adds the arithmetic mean as nullable Double for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, double?> AddAverage<TNumber>(Expression<Func<T, TNumber>> value) where TNumber : struct, INumber<TNumber> => Add(Owner.CreateAverageReducer<TKey, TNumber>(Owner.CreateExpressionReader(value)));
            /// <summary>Adds a Decimal-preserving arithmetic mean for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, decimal?> AddAverage(Expression<Func<T, decimal>> value) => Add(Owner.CreateDecimalAverageReducer<TKey>(Owner.CreateExpressionReader(value)));
            /// <summary>Adds the smallest non-null comparable value for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, TValue?> AddMin<TValue>(Expression<Func<T, TValue>> value) where TValue : struct, IComparable<TValue> => Add(Owner.CreateExtremumReducer<TKey, TValue>(Owner.CreateExpressionReader(value), true));
            /// <summary>Adds the largest non-null comparable value for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, TValue?> AddMax<TValue>(Expression<Func<T, TValue>> value) where TValue : struct, IComparable<TValue> => Add(Owner.CreateExtremumReducer<TKey, TValue>(Owner.CreateExpressionReader(value), false));
            /// <summary>Adds a custom streaming reduction over complete records with by-reference state.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, TResult> AddAggregate<TState, TResult>(Func<TState> seed, AggregateStep<TState> accumulate, AggregateFinish<TState, TResult> finish) => Add(Owner.CreateCustomReducer<TKey, TState, TResult>(seed, accumulate, finish));
            /// <summary>Adds a custom streaming reduction over a typed source expression with by-reference state.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, TResult> AddAggregate<TSource, TState, TResult>(Expression<Func<T, TSource>> source, Func<TState> seed, AggregateValueStep<TSource, TState> accumulate, AggregateFinish<TState, TResult> finish) => Add(Owner.CreateCustomValueReducer<TKey, TSource, TState, TResult>(Owner.CreateExpressionReader(source), seed, accumulate, finish));
            /// <summary>Adds a custom streaming reduction over a validated textual source path with by-reference state.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, TResult> AddAggregate<TSource, TState, TResult>(string path, Func<TState> seed, AggregateValueStep<TSource, TState> accumulate, AggregateFinish<TState, TResult> finish) => Add(Owner.CreateCustomValueReducer<TKey, TSource, TState, TResult>(Owner.CreatePathReader<TSource>(path), seed, accumulate, finish));

            private AggregateDefinition<TKey, T1, T2, TValue> Add<TState, TValue>(AggregateReducer<TKey, TState, TValue> reducer)
                => new(Owner, Plan.Append(reducer, static (prior, value) => (prior.Value1, prior.Value2, value)));
        }

        /// <summary>Represents a typed aggregate definition with three declared output values.<br/></summary>
        public sealed partial class AggregateDefinition<TKey, T1, T2, T3> : AggregateDefinitionBase<TKey, (T1 Value1, T2 Value2, T3 Value3)>
            where TKey : notnull
        {
            internal AggregateDefinition(AggregateFacade owner, AggregatePlanBase<TKey, (T1, T2, T3)> plan) : base(owner, plan) { }

            /// <summary>Adds the number of records entering each group as the next result value.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, ulong> AddCount() => Add(CreateCountReducer<TKey>());
            /// <summary>Adds a representative validated property from the first record entering each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, TValue> AddProp<TValue>(string path) => Add(Owner.CreateFirstReducer<TKey, TValue>(Owner.CreatePathReader<TValue>(path)));
            /// <summary>Adds a representative expression result from the first record entering each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, TValue> AddCalc<TValue>(Expression<Func<T, TValue>> value) => Add(Owner.CreateFirstReducer<TKey, TValue>(Owner.CreateExpressionReader(value)));
            /// <summary>Adds a representative calculation over a typed source expression.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, TValue> AddCalc<TSource, TValue>(Expression<Func<T, TSource>> source, Func<TSource, TValue> calculation) => Add(Owner.CreateCalculatedFirstReducer<TKey, TSource, TValue>(Owner.CreateExpressionReader(source), calculation));
            /// <summary>Adds a representative calculation over a validated textual source path.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, TValue> AddCalc<TSource, TValue>(string path, Func<TSource, TValue> calculation) => Add(Owner.CreateCalculatedFirstReducer<TKey, TSource, TValue>(Owner.CreatePathReader<TSource>(path), calculation));
            /// <summary>Adds the checked numeric sum for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, TNumber> AddSum<TNumber>(Expression<Func<T, TNumber>> value) where TNumber : struct, INumber<TNumber> => Add(Owner.CreateSumReducer<TKey, TNumber>(Owner.CreateExpressionReader(value)));
            /// <summary>Adds the arithmetic mean as nullable Double for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, double?> AddAverage<TNumber>(Expression<Func<T, TNumber>> value) where TNumber : struct, INumber<TNumber> => Add(Owner.CreateAverageReducer<TKey, TNumber>(Owner.CreateExpressionReader(value)));
            /// <summary>Adds a Decimal-preserving arithmetic mean for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, decimal?> AddAverage(Expression<Func<T, decimal>> value) => Add(Owner.CreateDecimalAverageReducer<TKey>(Owner.CreateExpressionReader(value)));
            /// <summary>Adds the smallest non-null comparable value for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, TValue?> AddMin<TValue>(Expression<Func<T, TValue>> value) where TValue : struct, IComparable<TValue> => Add(Owner.CreateExtremumReducer<TKey, TValue>(Owner.CreateExpressionReader(value), true));
            /// <summary>Adds the largest non-null comparable value for each group.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, TValue?> AddMax<TValue>(Expression<Func<T, TValue>> value) where TValue : struct, IComparable<TValue> => Add(Owner.CreateExtremumReducer<TKey, TValue>(Owner.CreateExpressionReader(value), false));
            /// <summary>Adds a custom streaming reduction over complete records with by-reference state.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, TResult> AddAggregate<TState, TResult>(Func<TState> seed, AggregateStep<TState> accumulate, AggregateFinish<TState, TResult> finish) => Add(Owner.CreateCustomReducer<TKey, TState, TResult>(seed, accumulate, finish));
            /// <summary>Adds a custom streaming reduction over a typed source expression with by-reference state.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, TResult> AddAggregate<TSource, TState, TResult>(Expression<Func<T, TSource>> source, Func<TState> seed, AggregateValueStep<TSource, TState> accumulate, AggregateFinish<TState, TResult> finish) => Add(Owner.CreateCustomValueReducer<TKey, TSource, TState, TResult>(Owner.CreateExpressionReader(source), seed, accumulate, finish));
            /// <summary>Adds a custom streaming reduction over a validated textual source path with by-reference state.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, TResult> AddAggregate<TSource, TState, TResult>(string path, Func<TState> seed, AggregateValueStep<TSource, TState> accumulate, AggregateFinish<TState, TResult> finish) => Add(Owner.CreateCustomValueReducer<TKey, TSource, TState, TResult>(Owner.CreatePathReader<TSource>(path), seed, accumulate, finish));

            private AggregateDefinition<TKey, T1, T2, T3, TValue> Add<TState, TValue>(AggregateReducer<TKey, TState, TValue> reducer)
                => new(Owner, Plan.Append(reducer, static (prior, value) => (prior.Value1, prior.Value2, prior.Value3, value)));
        }

        /// <summary>Represents a completed four-value aggregate result shape.<br/></summary>
        public sealed partial class AggregateDefinition<TKey, T1, T2, T3, T4> : AggregateDefinitionBase<TKey, (T1 Value1, T2 Value2, T3 Value3, T4 Value4)>
            where TKey : notnull
        {
            internal AggregateDefinition(AggregateFacade owner, AggregatePlanBase<TKey, (T1, T2, T3, T4)> plan) : base(owner, plan) { }
        }

        /// <summary>
        /// Accumulates one developer-defined aggregate value into caller-selected state.<br/>
        /// State is passed by reference so struct accumulators are updated without boxing or defensive copies.<br/>
        /// </summary>
        public delegate void AggregateStep<TState>(ref TState state, T record);

        /// <summary>
        /// Accumulates one selected source value into caller-selected state.<br/>
        /// Direct property sources remain payload-based, and struct state is passed by reference without boxing.<br/>
        /// </summary>
        public delegate void AggregateValueStep<TSource, TState>(ref TState state, TSource value);

        /// <summary>Projects one completed developer-defined accumulator into its result value.<br/></summary>
        public delegate TResult AggregateFinish<TState, TResult>(in TState state);

        /// <summary>Provides the store-owned base for fluent aggregate definitions, retaining their execution plan and originating aggregate facade.<br/>Instances are produced by the aggregate API rather than constructed or extended by application code.<br/></summary>
        /// <typeparam name="TKey">Non-null grouping-key type used by the aggregate plan.<br/></typeparam>
        /// <typeparam name="TResult">Completed result type produced for each group.<br/></typeparam>
        public abstract class AggregateDefinitionBase<TKey, TResult>
            where TKey : notnull
        {
            private protected AggregateDefinitionBase(AggregateFacade owner, AggregatePlanBase<TKey, TResult> plan)
            {
                Owner = owner;
                Plan = plan;
            }

            internal AggregateFacade Owner { get; }
            internal AggregatePlanBase<TKey, TResult> Plan { get; }
        }

        internal readonly struct PayloadReader<TValue>
        {
            private readonly Func<InhetoBinary, ReadValue<TValue>> read;

            /// <summary>Captures one executable payload reader and its optional exact persistent-index path.<br/></summary>
            internal PayloadReader(Func<InhetoBinary, ReadValue<TValue>> read, string? path = null, IndexedGroupingSource<TValue>? grouping = null)
            {
                this.read = read ?? throw new ArgumentNullException(nameof(read));
                Path = path;
                Grouping = grouping;
            }

            /// <summary>Gets the normalized simple property path, or null when evaluation requires calculation or materialization.<br/></summary>
            internal string? Path { get; }

            /// <summary>Gets structured multi-key index planning metadata without inventing a combined property path.<br/></summary>
            internal IndexedGroupingSource<TValue>? Grouping { get; }

            /// <summary>Reads one value from the already-open authoritative Inheto payload.<br/></summary>
            internal ReadValue<TValue> Read(InhetoBinary payload) => read(payload);
        }

        internal delegate void Reduce<TKey, TState>(ref TState state, TKey key, InhetoBinary payload);
        internal delegate void IndexedReduce<TKey, TState>(ref TState state, TKey key, ulong identity);
        internal delegate void CompositeReduce<TKey, TState>(ref TState state, TKey key, AggregateCompositeValues values);
        internal delegate TValue Complete<TKey, TState, TValue>(in TState state, TKey key);

        internal readonly ref struct AggregateCompositeValues
        {
            private readonly ReadOnlySpan<object?> values;
            private readonly LibraDexCompositeKey? key;

            /// <summary>Wraps one borrowed low-allocation composite traversal path.<br/></summary>
            internal AggregateCompositeValues(ReadOnlySpan<object?> values)
            {
                this.values = values;
                key = null;
            }

            /// <summary>Gets one logical composite part without copying the full key.<br/></summary>
            internal object? this[int ordinal] => key is null ? values[ordinal] : key[ordinal];
        }

        internal readonly record struct ReadValue<TValue>(bool HasValue, TValue Value);
        internal readonly record struct EmptyResult;
        internal struct EmptyState;
        internal struct CountState { internal ulong Value; }
        internal struct FirstState<TValue> { internal bool Assigned; internal TValue Value; }
        internal struct SumState<TValue> where TValue : struct, INumber<TValue> { internal TValue Value; }
        internal struct AverageState { internal ulong Count; internal double Mean; }
        internal struct DecimalAverageState { internal ulong Count; internal decimal Mean; }
        internal struct ExtremumState<TValue> where TValue : struct, IComparable<TValue> { internal bool HasValue; internal TValue Value; }
        internal struct CompositeState<TPrior, TNext> { internal TPrior Prior; internal TNext Next; }

        internal readonly struct AggregateReducer<TKey, TState, TValue>
        {
            internal AggregateReducer(
                Func<TState> seed,
                Reduce<TKey, TState> reduce,
                Complete<TKey, TState, TValue> complete,
                Action<TState>? dispose = null,
                Func<IndexedReduce<TKey, TState>?>? prepareIndexed = null,
                Func<LibraDexCompositeIndex, CompositeReduce<TKey, TState>?>? prepareComposite = null)
            {
                Seed = seed;
                Reduce = reduce;
                Complete = complete;
                Dispose = dispose;
                PrepareIndexed = prepareIndexed;
                PrepareComposite = prepareComposite;
            }

            internal Func<TState> Seed { get; }
            internal Reduce<TKey, TState> Reduce { get; }
            internal Complete<TKey, TState, TValue> Complete { get; }
            internal Action<TState>? Dispose { get; }
            internal Func<IndexedReduce<TKey, TState>?>? PrepareIndexed { get; }
            internal Func<LibraDexCompositeIndex, CompositeReduce<TKey, TState>?>? PrepareComposite { get; }
        }

        internal abstract class AggregatePlanBase<TKey, TResult>
            where TKey : notnull
        {
            internal abstract AggregatePlanBase<TKey, TNextResult> Append<TNextState, TNextValue, TNextResult>(
                AggregateReducer<TKey, TNextState, TNextValue> reducer,
                Func<TResult, TNextValue, TNextResult> combine);

            internal abstract IEnumerator<TResult> Prepare();
        }

        internal sealed class AggregatePlan<TKey, TState, TResult> : AggregatePlanBase<TKey, TResult>
            where TKey : notnull
        {
            private readonly AggregateFacade owner;
            private readonly Condition<T>.EndCondition? where;
            private readonly PayloadReader<TKey> readKey;
            private readonly Func<TState> seed;
            private readonly Reduce<TKey, TState> reduce;
            private readonly Complete<TKey, TState, TResult> complete;
            private readonly Action<TState>? dispose;
            private readonly Func<IndexedReduce<TKey, TState>?>? prepareIndexed;
            private readonly Func<LibraDexCompositeIndex, CompositeReduce<TKey, TState>?>? prepareComposite;

            internal AggregatePlan(
                AggregateFacade owner,
                Condition<T>.EndCondition? where,
                PayloadReader<TKey> readKey,
                Func<TState> seed,
                Reduce<TKey, TState> reduce,
                Complete<TKey, TState, TResult> complete,
                Action<TState>? dispose = null,
                Func<IndexedReduce<TKey, TState>?>? prepareIndexed = null,
                Func<LibraDexCompositeIndex, CompositeReduce<TKey, TState>?>? prepareComposite = null)
            {
                this.owner = owner;
                this.where = where;
                this.readKey = readKey;
                this.seed = seed;
                this.reduce = reduce;
                this.complete = complete;
                this.dispose = dispose;
                this.prepareIndexed = prepareIndexed;
                this.prepareComposite = prepareComposite;
            }

            internal override AggregatePlanBase<TKey, TNextResult> Append<TNextState, TNextValue, TNextResult>(
                AggregateReducer<TKey, TNextState, TNextValue> reducer,
                Func<TResult, TNextValue, TNextResult> combine)
            {
                Func<IndexedReduce<TKey, CompositeState<TState, TNextState>>?>? combinedIndexed =
                    prepareIndexed is null || reducer.PrepareIndexed is null
                        ? null
                        : () =>
                        {
                            IndexedReduce<TKey, TState>? prior = prepareIndexed();
                            IndexedReduce<TKey, TNextState>? next = reducer.PrepareIndexed();
                            if (prior is null || next is null)
                                return null;
                            return (ref CompositeState<TState, TNextState> state, TKey key, ulong identity) =>
                            {
                                prior(ref state.Prior, key, identity);
                                next(ref state.Next, key, identity);
                            };
                        };

                Func<LibraDexCompositeIndex, CompositeReduce<TKey, CompositeState<TState, TNextState>>?>? combinedComposite =
                    prepareComposite is null || reducer.PrepareComposite is null
                        ? null
                        : index =>
                        {
                            CompositeReduce<TKey, TState>? prior = prepareComposite(index);
                            CompositeReduce<TKey, TNextState>? next = reducer.PrepareComposite(index);
                            if (prior is null || next is null)
                                return null;
                            return (ref CompositeState<TState, TNextState> state, TKey key, AggregateCompositeValues values) =>
                            {
                                prior(ref state.Prior, key, values);
                                next(ref state.Next, key, values);
                            };
                        };

                return new AggregatePlan<TKey, CompositeState<TState, TNextState>, TNextResult>(
                    owner,
                    where,
                    readKey,
                    () => new CompositeState<TState, TNextState> { Prior = seed(), Next = reducer.Seed() },
                    (ref CompositeState<TState, TNextState> state, TKey key, InhetoBinary payload) =>
                    {
                        reduce(ref state.Prior, key, payload);
                        reducer.Reduce(ref state.Next, key, payload);
                    },
                    (in CompositeState<TState, TNextState> state, TKey key) =>
                        combine(complete(in state.Prior, key), reducer.Complete(in state.Next, key)),
                    dispose is null && reducer.Dispose is null
                        ? null
                        : state =>
                        {
                            dispose?.Invoke(state.Prior);
                            reducer.Dispose?.Invoke(state.Next);
                        },
                    combinedIndexed,
                    combinedComposite);
            }

            internal override IEnumerator<TResult> Prepare()
            {
                if (where is null &&
                    prepareComposite is not null &&
                    readKey.Path is not null &&
                    owner.store.TryPrepareAggregateDefinitionCompositeTraversal(
                        readKey.Path,
                        typeof(TKey),
                        where,
                        prepareComposite,
                        out CatalogIdentityGroupIndexes compositeGroup,
                        out LibraDexCompositeIndex compositeIndex,
                        out LibraDexConditionEndCondition? compositeCondition,
                        out CompositeReduce<TKey, TState>? composite))
                {
                    return new CompositeAggregateResultEnumerator(
                        owner.store,
                        compositeIndex.RoutedIndex,
                        seed,
                        composite!,
                        complete,
                        dispose);
                }

                IndexedReduce<TKey, TState>? indexed = prepareIndexed?.Invoke();
                if (where is null && readKey.Grouping is not null &&
                    owner.store.libraDexBackendMode != LibraDexIndexBackendMode.FractalOnly)
                {
                    // Prefer a fully indexed join: an existing composite can have expensive cold node loading.
                    // Preparing this source only resolves handles; its identity map remains deferred.
                    var source = readKey.Grouping.Prepare(owner.store);
                    if (source is not null && indexed is not null)
                        return IterateJoinedIndexGroups(source, indexed).GetEnumerator();
                    if (prepareComposite is not null && owner.store.libraDexCompositeIndexes is not null)
                    {
                        LibraDexCompositeIndex? selected = null;
                        CompositeReduce<TKey, TState>? selectedReduce = null;
                        CompositeKeyRead<TKey>? selectedKey = null;
                        foreach (var candidate in owner.store.libraDexCompositeIndexes)
                        {
                            var key = readKey.Grouping.MatchComposite(candidate);
                            var reduction = key is null ? null : prepareComposite(candidate);
                            if (reduction is null || (selected is not null && candidate.Specs.Length >= selected.Specs.Length)) continue;
                            selected = candidate; selectedReduce = reduction; selectedKey = key;
                        }
                        if (selected is not null)
                            return new CompositeAggregateResultEnumerator(owner.store, selected.RoutedIndex, seed, selectedReduce!, complete, dispose, selectedKey);
                    }
                    if (source is not null)
                        return IterateJoinedIndexGroups(source, indexed).GetEnumerator();
                }
                if (where is null &&
                    readKey.Path is not null &&
                    owner.store.TryPrepareAggregateDefinitionIndexedTraversal(
                        readKey.Path,
                        typeof(TKey),
                        where,
                        out CatalogIdentityGroupIndexes indexedGroup,
                        out LibraDexSimpleIndex groupingIndex,
                        out LibraDexConditionEndCondition indexedCondition))
                {
                    if (groupingIndex.Index is LibraDexIndex<TKey, ulong> typedGroupingIndex)
                    {
                        return IterateTypedIndex(typedGroupingIndex, indexed).GetEnumerator();
                    }

                    IEnumerable<LibraDexRuntimeTuple> source =
                        indexedGroup.IterateTuples(groupingIndex.Index, QueryDirection.Ascending);
                    if (groupingIndex.Index.IdentityKeyMultiplicity != IdentityKeyMultiplicity.SingleKeyPerIdentity)
                        source = IterateDistinctAggregateGroupingTuples(source);
                    return indexed is not null
                        ? IterateIndexed(source, indexed).GetEnumerator()
                        : IterateIndexedPayloads(source).GetEnumerator();
                }

                Dictionary<TKey, TState> groups = new();
                using (owner.store.EnterStoreOperationScope())
                {
                    foreach (InhetoBinary payload in owner.store.IterateAuthoritativeGroupingPayloads(where, nameof(Open)))
                    {
                        ReadValue<TKey> candidate = readKey.Read(payload);
                        if (!candidate.HasValue || candidate.Value is null)
                            continue;

                        ref TState state = ref CollectionsMarshal.GetValueRefOrAddDefault(groups, candidate.Value, out bool exists)!;
                        if (!exists)
                            state = seed();
                        reduce(ref state, candidate.Value, payload);
                    }
                }

                return new AggregateResultEnumerator(groups, complete, dispose);

                // Reduces an identity-joined multi-key source without retaining authoritative records.
                // State is retained per result group because joining scalar indexes does not provide full joint-key ordering.
                // Failures release accumulated custom reducer state; early result disposal uses the established result enumerator.
                IEnumerable<TResult> IterateJoinedIndexGroups(IEnumerable<(TKey Key, ulong Identity)> source, IndexedReduce<TKey, TState>? apply)
                {
                    using var operation = owner.store.EnterStoreOperationScope();
                    var joinedGroups = new Dictionary<TKey, TState>();
                    try
                    {
                        foreach (var row in source)
                            ApplyJoinedRow(joinedGroups, row.Key, row.Identity, apply);
                    }
                    catch
                    {
                        if (dispose is not null) foreach (var state in joinedGroups.Values) dispose(state);
                        throw;
                    }
                    using var result = new AggregateResultEnumerator(joinedGroups, complete, dispose);
                    while (result.MoveNext()) yield return result.Current;
                }

                // Updates a joint group through one dictionary probe without retaining a ref local in the iterator state machine.
                // This preserves the project's C# 12 language level and avoids copying a potentially large composed reducer state.
                void ApplyJoinedRow(Dictionary<TKey, TState> target, TKey key, ulong identity, IndexedReduce<TKey, TState>? apply)
                {
                    ref TState state = ref CollectionsMarshal.GetValueRefOrAddDefault(target, key, out bool exists)!;
                    if (!exists) state = seed();
                    if (apply is not null) apply(ref state, key, identity);
                    else ReducePayload(ref state, key, identity);
                }

                IEnumerable<TResult> IterateIndexed(
                    IEnumerable<LibraDexRuntimeTuple> source,
                    IndexedReduce<TKey, TState> apply)
                {
                    using var operation = owner.store.EnterStoreOperationScope();
                    TKey currentKey = default!;
                    TState state = default!;
                    bool hasState = false;
                    try
                    {
                        foreach (LibraDexRuntimeTuple tuple in source)
                        {
                            if (tuple.Key is null)
                                continue;
                            if (tuple.Key is not TKey key || tuple.Identity is not ulong identity)
                                throw new InvalidDataException("The indexed aggregate grouping stream returned an incompatible key or identity type.");
                            if (!hasState)
                            {
                                currentKey = key;
                                state = seed();
                                hasState = true;
                            }
                            else if (!EqualityComparer<TKey>.Default.Equals(currentKey, key))
                            {
                                TResult result = complete(in state, currentKey);
                                dispose?.Invoke(state);
                                hasState = false;
                                currentKey = key;
                                state = seed();
                                hasState = true;
                                apply(ref state, key, identity);
                                yield return result;
                                continue;
                            }

                            apply(ref state, key, identity);
                        }

                        if (hasState)
                        {
                            TResult result = complete(in state, currentKey);
                            dispose?.Invoke(state);
                            hasState = false;
                            yield return result;
                        }
                    }
                    finally
                    {
                        if (hasState)
                            dispose?.Invoke(state);
                    }
                }

                IEnumerable<TResult> IterateIndexedPayloads(IEnumerable<LibraDexRuntimeTuple> source)
                {
                    using var operation = owner.store.EnterStoreOperationScope();
                    TKey currentKey = default!;
                    TState state = default!;
                    bool hasState = false;
                    try
                    {
                        foreach (LibraDexRuntimeTuple tuple in source)
                        {
                            if (tuple.Key is null)
                                continue;
                            if (tuple.Key is not TKey key || tuple.Identity is not ulong identity)
                                throw new InvalidDataException("The indexed aggregate grouping stream returned an incompatible key or identity type.");
                            if (!hasState)
                            {
                                currentKey = key;
                                state = seed();
                                hasState = true;
                            }
                            else if (!EqualityComparer<TKey>.Default.Equals(currentKey, key))
                            {
                                TResult result = complete(in state, currentKey);
                                dispose?.Invoke(state);
                                hasState = false;
                                currentKey = key;
                                state = seed();
                                hasState = true;
                                ReducePayload(ref state, key, identity);
                                yield return result;
                                continue;
                            }

                            ReducePayload(ref state, key, identity);
                        }

                        if (hasState)
                        {
                            TResult result = complete(in state, currentKey);
                            dispose?.Invoke(state);
                            hasState = false;
                            yield return result;
                        }
                    }
                    finally
                    {
                        if (hasState)
                            dispose?.Invoke(state);
                    }
                }

                IEnumerable<TResult> IterateTypedIndex(
                    LibraDexIndex<TKey, ulong> index,
                    IndexedReduce<TKey, TState>? applyIndexed)
                {
                    using var operation = owner.store.EnterStoreOperationScope();
                    using LibraDexRangeReader<TKey, ulong> rows = index.OpenReader(QueryDirection.Ascending);
                    HashSet<ulong>? seen = index.IdentityKeyMultiplicity == IdentityKeyMultiplicity.SingleKeyPerIdentity
                        ? null
                        : new HashSet<ulong>();
                    TKey currentKey = default!;
                    TState state = default!;
                    bool hasState = false;
                    try
                    {
                        while (rows.MoveNext())
                        {
                            TKey key = rows.CurrentKey;
                            ulong identity = rows.CurrentIdentity;
                            if (key is null || (seen is not null && !seen.Add(identity)))
                                continue;
                            if (!hasState)
                            {
                                currentKey = key;
                                state = seed();
                                hasState = true;
                            }
                            else if (!EqualityComparer<TKey>.Default.Equals(currentKey, key))
                            {
                                TResult result = complete(in state, currentKey);
                                dispose?.Invoke(state);
                                hasState = false;
                                currentKey = key;
                                state = seed();
                                hasState = true;
                                ApplyTypedRow(ref state, key, identity, applyIndexed);
                                yield return result;
                                continue;
                            }

                            ApplyTypedRow(ref state, key, identity, applyIndexed);
                        }

                        if (hasState)
                        {
                            TResult result = complete(in state, currentKey);
                            dispose?.Invoke(state);
                            hasState = false;
                            yield return result;
                        }
                    }
                    finally
                    {
                        if (hasState)
                            dispose?.Invoke(state);
                    }
                }

                void ApplyTypedRow(
                    ref TState state,
                    TKey key,
                    ulong identity,
                    IndexedReduce<TKey, TState>? applyIndexed)
                {
                    if (applyIndexed is not null)
                        applyIndexed(ref state, key, identity);
                    else
                        ReducePayload(ref state, key, identity);
                }

                void ReducePayload(ref TState state, TKey key, ulong identity)
                {
                    if (!owner.store.rbase.data.TryGetOne(identity, out var record))
                    {
                        throw new InvalidDataException(
                            $"The aggregate grouping index referenced record identity '{identity}', but its authoritative Fractal payload was absent.");
                    }

                    using (record)
                    {
                        InhetoBinary payload = owner.store.CreateRecordBinary(identity, record.Data);
                        payload.SetMaterializationContext(identity);
                        reduce(ref state, key, payload);
                    }
                }

            }

            /// <summary>
            /// Reduces one naturally ordered covering-composite traversal with only the current group's state.<br/>
            /// The underlying LibraDex cursor lends one key-part span and creates no stable key container per source entry.<br/>
            /// </summary>
            private sealed class CompositeAggregateResultEnumerator : IEnumerator<TResult>
            {
                private readonly IDisposable operation;
                private readonly LibraDexRoutedCompositeIndex.EntryCursor rows;
                private readonly Func<TState> seed;
                private readonly CompositeReduce<TKey, TState> reduce;
                private readonly Complete<TKey, TState, TResult> complete;
                private readonly Action<TState>? release;
                private readonly CompositeKeyRead<TKey>? readCompositeKey;
                private TKey groupKey = default!;
                private TState state = default!;
                private TResult current = default!;
                private bool hasState;
                private bool completed;
                private bool disposed;

                /// <summary>Opens one borrowed ordered traversal without advancing it.<br/></summary>
                internal CompositeAggregateResultEnumerator(
                    RecordStore<T> store,
                    LibraDexRoutedCompositeIndex index,
                    Func<TState> seed,
                    CompositeReduce<TKey, TState> reduce,
                    Complete<TKey, TState, TResult> complete,
                    Action<TState>? release,
                    CompositeKeyRead<TKey>? readCompositeKey = null)
                {
                    operation = store.EnterStoreOperationScope();
                    try
                    {
                        rows = index.OpenEntryCursor();
                    }
                    catch
                    {
                        operation.Dispose();
                        throw;
                    }
                    this.seed = seed;
                    this.reduce = reduce;
                    this.complete = complete;
                    this.release = release;
                    this.readCompositeKey = readCompositeKey;
                }

                /// <summary>Gets the last completed aggregate result.<br/></summary>
                public TResult Current => current;

                object System.Collections.IEnumerator.Current => Current!;

                /// <summary>Consumes entries through the next grouping-key boundary and completes one result.<br/></summary>
                public bool MoveNext()
                {
                    ObjectDisposedException.ThrowIf(disposed, this);
                    if (completed)
                        return false;

                    while (rows.Read())
                    {
                        ReadOnlySpan<object?> parts = rows.KeyParts;
                        ReadValue<TKey> candidate = readCompositeKey is null
                            ? ReadCompositeValue<TKey>(new AggregateCompositeValues(parts), 0)
                            : readCompositeKey(new AggregateCompositeValues(parts));
                        if (!candidate.HasValue) continue;
                        TKey key = candidate.Value;
                        if (rows.Identity is not ulong)
                            throw new InvalidDataException("The composite aggregate stream returned an incompatible grouping key or identity type.");
                        if (!hasState)
                        {
                            groupKey = key;
                            state = seed();
                            hasState = true;
                        }
                        else if (!EqualityComparer<TKey>.Default.Equals(groupKey, key))
                        {
                            TResult result = CompleteCurrent();
                            groupKey = key;
                            state = seed();
                            hasState = true;
                            reduce(ref state, key, new AggregateCompositeValues(parts));
                            current = result;
                            return true;
                        }

                        reduce(ref state, key, new AggregateCompositeValues(parts));
                    }

                    completed = true;
                    if (!hasState)
                    {
                        current = default!;
                        return false;
                    }

                    current = CompleteCurrent();
                    return true;
                }

                /// <summary>Releases the pending current-group state and the borrowed LibraDex traversal.<br/></summary>
                public void Dispose()
                {
                    if (disposed)
                        return;
                    disposed = true;
                    if (hasState)
                    {
                        release?.Invoke(state);
                        hasState = false;
                    }
                    rows.Dispose();
                    operation.Dispose();
                }

                void System.Collections.IEnumerator.Reset()
                    => throw new NotSupportedException("Aggregate result readers cannot be reset.");

                /// <summary>Completes and releases the current group exactly once.<br/></summary>
                private TResult CompleteCurrent()
                {
                    try
                    {
                        return complete(in state, groupKey);
                    }
                    finally
                    {
                        release?.Invoke(state);
                        hasState = false;
                    }
                }
            }

            /// <summary>
            /// Streams completed group values while deterministically releasing each group's owned reduction state.<br/>
            /// Disposing the reader early walks only the remaining compact group states so temporary spill resources cannot escape the reader lifetime.<br/>
            /// </summary>
            private sealed class AggregateResultEnumerator : IEnumerator<TResult>
            {
                private Dictionary<TKey, TState>.Enumerator rows;
                private readonly Complete<TKey, TState, TResult> complete;
                private readonly Action<TState>? dispose;
                private TResult current = default!;
                private bool disposed;

                /// <summary>Owns the completed group-state dictionary from preparation through full or partial result consumption.<br/></summary>
                internal AggregateResultEnumerator(
                    Dictionary<TKey, TState> groups,
                    Complete<TKey, TState, TResult> complete,
                    Action<TState>? dispose)
                {
                    rows = groups.GetEnumerator();
                    this.complete = complete;
                    this.dispose = dispose;
                }

                /// <summary>Gets the last completed result value.<br/></summary>
                public TResult Current => current;

                object System.Collections.IEnumerator.Current => Current!;

                /// <summary>Completes one group, releases its owned state, and advances without retaining the prior result state.<br/></summary>
                public bool MoveNext()
                {
                    ObjectDisposedException.ThrowIf(disposed, this);
                    if (!rows.MoveNext())
                    {
                        current = default!;
                        return false;
                    }

                    (TKey key, TState state) = rows.Current;
                    try
                    {
                        current = complete(in state, key);
                        return true;
                    }
                    catch
                    {
                        DisposeRemaining();
                        throw;
                    }
                    finally
                    {
                        dispose?.Invoke(state);
                    }
                }

                /// <summary>Releases every unread compact group state, including when preparation was never followed by a read.<br/></summary>
                public void Dispose()
                {
                    if (disposed)
                        return;
                    disposed = true;
                    DisposeRemaining();
                    rows.Dispose();
                }

                void System.Collections.IEnumerator.Reset()
                    => throw new NotSupportedException("Aggregate result readers cannot be reset.");

                /// <summary>Walks only unread group states and invokes their optional ownership release action.<br/></summary>
                private void DisposeRemaining()
                {
                    while (rows.MoveNext())
                        dispose?.Invoke(rows.Current.Value);
                }
            }
        }

        private PayloadReader<TValue> CreatePathReader<TValue>(string propPath)
        {
            if (string.IsNullOrWhiteSpace(propPath))
                throw new ArgumentException("A property path is required.", nameof(propPath));

            if (store.Links.TryResolveTarget(propPath, out string linkedPath))
            {
                return new PayloadReader<TValue>(
                    payload =>
                    {
                        if (!payload.TryGetMaterializationContext<ulong>(out ulong identity))
                            throw new InvalidOperationException($"Aggregate link '{linkedPath}' requires the current record identity.");
                        if (!store.Links.TryReadProperty(
                            payload,
                            identity,
                            linkedPath,
                            typeof(TValue),
                            policy: null,
                            store.NativeDeserializationOptions,
                            out object? value) || value is null)
                        {
                            return default;
                        }

                        return new ReadValue<TValue>(true, (TValue)value);
                    },
                    linkedPath);
            }

            PropPathCheck check = InhetoBinary.CheckPropPath<T, TValue>(propPath);
            if (!check.IsValid)
                throw new ArgumentException($"Property path '{propPath}' is invalid for {typeof(T).Name}: {check.FailureKind} ({check.FailedSegment ?? "unknown segment"}).", nameof(propPath));

            return new PayloadReader<TValue>(
                payload =>
                {
                    PropPathReadState state = payload.TryReadPropPath(check, out TValue? value, store.NativeDeserializationOptions, throwOnInvocationError: true);
                    return state == PropPathReadState.Value
                        ? new ReadValue<TValue>(true, value!)
                        : default;
                },
                check.CanonicalPath);
        }

        private PayloadReader<TValue> CreateExpressionReader<TValue>(Expression<Func<T, TValue>> expression)
        {
            ArgumentNullException.ThrowIfNull(expression);
            if (TryResolveAggregateSelectorPath(expression, out string path))
                return CreatePathReader<TValue>(path);

            Func<T, TValue> compiled = expression.Compile();
            return new PayloadReader<TValue>(payload =>
            {
                if (!payload.TryGetMaterializationContext<ulong>(out ulong identity))
                    throw new InvalidOperationException("Aggregate calculation requires the current record identity for link-aware materialization.");
                T? record = store.MaterializeReaderObject<T>(payload, identity, nameof(AggregateFacade.Open));
                return record is null ? default : new ReadValue<TValue>(true, compiled(record));
            });
        }

        private PayloadReader<TValue> CreateSelectorReader<TValue>(Selector<T>.IValue<TValue> selector)
        {
            ArgumentNullException.ThrowIfNull(selector);
            if (!store.TryCreateStoreSelectorReader<TValue>(selector, out var read))
                throw new NotSupportedException($"Selector result '{typeof(TValue).FullName}' does not have an executable aggregate scalar reader.");
            string? path = selector is ISelectorStructure structure &&
                structure.SemanticDescriptor is DirectPropertySelectorDescriptor direct
                    ? direct.PropertyPath
                    : null;
            return new PayloadReader<TValue>(payload =>
            {
                (bool hasValue, TValue value) = read(payload);
                return hasValue ? new ReadValue<TValue>(true, value) : default;
            }, path);
        }

        private static AggregateReducer<TKey, EmptyState, TKey> CreateKeyReducer<TKey>()
            where TKey : notnull
            => new(
                static () => default,
                static (ref EmptyState _, TKey _, InhetoBinary _) => { },
                static (in EmptyState _, TKey key) => key,
                prepareIndexed: static () => static (ref EmptyState _, TKey _, ulong _) => { },
                prepareComposite: static _ => static (ref EmptyState _, TKey _, AggregateCompositeValues _) => { });

        private static AggregateReducer<TKey, EmptyState, TValue> CreateProjectedKeyReducer<TKey, TValue>(Func<TKey, TValue> project)
            where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(project);
            return new AggregateReducer<TKey, EmptyState, TValue>(
                static () => default,
                static (ref EmptyState _, TKey _, InhetoBinary _) => { },
                (in EmptyState _, TKey key) => project(key),
                prepareIndexed: static () => static (ref EmptyState _, TKey _, ulong _) => { },
                prepareComposite: static _ => static (ref EmptyState _, TKey _, AggregateCompositeValues _) => { });
        }

        private static AggregateReducer<TKey, CountState, ulong> CreateCountReducer<TKey>()
            where TKey : notnull
            => new(
                static () => default,
                static (ref CountState state, TKey _, InhetoBinary _) => state.Value = checked(state.Value + 1),
                static (in CountState state, TKey _) => state.Value,
                prepareIndexed: static () => static (ref CountState state, TKey _, ulong _) => state.Value = checked(state.Value + 1),
                prepareComposite: static _ => static (ref CountState state, TKey _, AggregateCompositeValues _) => state.Value = checked(state.Value + 1));

        private AggregateReducer<TKey, FirstState<TValue>, TValue> CreateFirstReducer<TKey, TValue>(PayloadReader<TValue> read)
            where TKey : notnull
            => CreateEndpointReducer<TKey, TValue>(read, first: true);

        private AggregateReducer<TKey, FirstState<TValue>, TValue> CreateCalculatedFirstReducer<TKey, TSource, TValue>(
            PayloadReader<TSource> read,
            Func<TSource, TValue> calculation)
            where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(calculation);
            return new AggregateReducer<TKey, FirstState<TValue>, TValue>(
                static () => default,
                (ref FirstState<TValue> state, TKey _, InhetoBinary payload) =>
                {
                    if (state.Assigned)
                        return;
                    state.Assigned = true;
                    ReadValue<TSource> source = read.Read(payload);
                    state.Value = source.HasValue ? calculation(source.Value) : default!;
                },
                static (in FirstState<TValue> state, TKey _) => state.Value,
                prepareIndexed: () =>
                {
                    Func<ulong, ReadValue<TSource>>? indexed = PrepareIndexedValueReader(read);
                    return indexed is null
                        ? null
                        : (ref FirstState<TValue> state, TKey _, ulong identity) =>
                        {
                            if (state.Assigned)
                                return;
                            state.Assigned = true;
                            ReadValue<TSource> source = indexed(identity);
                            state.Value = source.HasValue ? calculation(source.Value) : default!;
                        };
                });
        }

        private AggregateReducer<TKey, SumState<TNumber>, TNumber> CreateSumReducer<TKey, TNumber>(PayloadReader<TNumber> read)
            where TKey : notnull
            where TNumber : struct, INumber<TNumber>
            => new(
                static () => new SumState<TNumber> { Value = TNumber.Zero },
                (ref SumState<TNumber> state, TKey _, InhetoBinary payload) =>
                {
                    ReadValue<TNumber> value = read.Read(payload);
                    if (value.HasValue)
                        state.Value = checked(state.Value + value.Value);
                },
                static (in SumState<TNumber> state, TKey _) => state.Value,
                prepareIndexed: () =>
                {
                    Func<ulong, ReadValue<TNumber>>? indexed = PrepareIndexedValueReader(read);
                    return indexed is null
                        ? null
                        : (ref SumState<TNumber> state, TKey _, ulong identity) =>
                        {
                            ReadValue<TNumber> value = indexed(identity);
                            if (value.HasValue)
                                state.Value = checked(state.Value + value.Value);
                        };
                },
                prepareComposite: index =>
                {
                    if (!TryGetCompositeValueOrdinal(read, index, out int ordinal))
                        return null;
                    return (ref SumState<TNumber> state, TKey _, AggregateCompositeValues values) =>
                    {
                        ReadValue<TNumber> value = ReadCompositeValue<TNumber>(values, ordinal);
                        if (value.HasValue)
                            state.Value = checked(state.Value + value.Value);
                    };
                });

        private AggregateReducer<TKey, AverageState, double?> CreateAverageReducer<TKey, TNumber>(PayloadReader<TNumber> read)
            where TKey : notnull
            where TNumber : struct, INumber<TNumber>
            => new(
                static () => default,
                (ref AverageState state, TKey _, InhetoBinary payload) =>
                {
                    ReadValue<TNumber> value = read.Read(payload);
                    if (!value.HasValue)
                        return;
                    state.Count = checked(state.Count + 1);
                    double number = double.CreateChecked(value.Value);
                    state.Mean += (number - state.Mean) / state.Count;
                },
                static (in AverageState state, TKey _) => state.Count == 0 ? null : state.Mean,
                prepareIndexed: () =>
                {
                    Func<ulong, ReadValue<TNumber>>? indexed = PrepareIndexedValueReader(read);
                    return indexed is null
                        ? null
                        : (ref AverageState state, TKey _, ulong identity) =>
                        {
                            ReadValue<TNumber> value = indexed(identity);
                            if (!value.HasValue)
                                return;
                            state.Count = checked(state.Count + 1);
                            double number = double.CreateChecked(value.Value);
                            state.Mean += (number - state.Mean) / state.Count;
                        };
                },
                prepareComposite: index =>
                {
                    if (!TryGetCompositeValueOrdinal(read, index, out int ordinal))
                        return null;
                    return (ref AverageState state, TKey _, AggregateCompositeValues values) =>
                    {
                        ReadValue<TNumber> value = ReadCompositeValue<TNumber>(values, ordinal);
                        if (!value.HasValue)
                            return;
                        state.Count = checked(state.Count + 1);
                        double number = double.CreateChecked(value.Value);
                        state.Mean += (number - state.Mean) / state.Count;
                    };
                });

        private AggregateReducer<TKey, DecimalAverageState, decimal?> CreateDecimalAverageReducer<TKey>(PayloadReader<decimal> read)
            where TKey : notnull
            => new(
                static () => default,
                (ref DecimalAverageState state, TKey _, InhetoBinary payload) =>
                {
                    ReadValue<decimal> value = read.Read(payload);
                    if (!value.HasValue)
                        return;
                    state.Count = checked(state.Count + 1);
                    state.Mean += (value.Value - state.Mean) / state.Count;
                },
                static (in DecimalAverageState state, TKey _) => state.Count == 0 ? null : state.Mean,
                prepareIndexed: () =>
                {
                    Func<ulong, ReadValue<decimal>>? indexed = PrepareIndexedValueReader(read);
                    return indexed is null
                        ? null
                        : (ref DecimalAverageState state, TKey _, ulong identity) =>
                        {
                            ReadValue<decimal> value = indexed(identity);
                            if (!value.HasValue)
                                return;
                            state.Count = checked(state.Count + 1);
                            state.Mean += (value.Value - state.Mean) / state.Count;
                        };
                },
                prepareComposite: index =>
                {
                    if (!TryGetCompositeValueOrdinal(read, index, out int ordinal))
                        return null;
                    return (ref DecimalAverageState state, TKey _, AggregateCompositeValues values) =>
                    {
                        ReadValue<decimal> value = ReadCompositeValue<decimal>(values, ordinal);
                        if (!value.HasValue)
                            return;
                        state.Count = checked(state.Count + 1);
                        state.Mean += (value.Value - state.Mean) / state.Count;
                    };
                });

        private AggregateReducer<TKey, ExtremumState<TValue>, TValue?> CreateExtremumReducer<TKey, TValue>(PayloadReader<TValue> read, bool minimum)
            where TKey : notnull
            where TValue : struct, IComparable<TValue>
            => new(
                static () => default,
                (ref ExtremumState<TValue> state, TKey _, InhetoBinary payload) =>
                {
                    ReadValue<TValue> value = read.Read(payload);
                    if (!value.HasValue)
                        return;
                    if (!state.HasValue || (minimum ? value.Value.CompareTo(state.Value) < 0 : value.Value.CompareTo(state.Value) > 0))
                    {
                        state.HasValue = true;
                        state.Value = value.Value;
                    }
                },
                static (in ExtremumState<TValue> state, TKey _) => state.HasValue ? state.Value : null,
                prepareIndexed: () =>
                {
                    Func<ulong, ReadValue<TValue>>? indexed = PrepareIndexedValueReader(read);
                    return indexed is null
                        ? null
                        : (ref ExtremumState<TValue> state, TKey _, ulong identity) =>
                        {
                            ReadValue<TValue> value = indexed(identity);
                            if (!value.HasValue)
                                return;
                            if (!state.HasValue || (minimum ? value.Value.CompareTo(state.Value) < 0 : value.Value.CompareTo(state.Value) > 0))
                            {
                                state.HasValue = true;
                                state.Value = value.Value;
                            }
                        };
                },
                prepareComposite: index =>
                {
                    if (!TryGetCompositeValueOrdinal(read, index, out int ordinal))
                        return null;
                    return (ref ExtremumState<TValue> state, TKey _, AggregateCompositeValues values) =>
                    {
                        ReadValue<TValue> value = ReadCompositeValue<TValue>(values, ordinal);
                        if (!value.HasValue)
                            return;
                        if (!state.HasValue || (minimum ? value.Value.CompareTo(state.Value) < 0 : value.Value.CompareTo(state.Value) > 0))
                        {
                            state.HasValue = true;
                            state.Value = value.Value;
                        }
                    };
                });

        private AggregateReducer<TKey, TState, TResult> CreateCustomReducer<TKey, TState, TResult>(
            Func<TState> seed,
            AggregateStep<TState> accumulate,
            AggregateFinish<TState, TResult> finish)
            where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(seed);
            ArgumentNullException.ThrowIfNull(accumulate);
            ArgumentNullException.ThrowIfNull(finish);
            return new AggregateReducer<TKey, TState, TResult>(
                seed,
                (ref TState state, TKey _, InhetoBinary payload) =>
                {
                    T? record = payload.ToObject<T>(store.NativeDeserializationOptions);
                    if (record is not null)
                        accumulate(ref state, record);
                },
                (in TState state, TKey _) => finish(in state));
        }

        private AggregateReducer<TKey, TState, TResult> CreateCustomValueReducer<TKey, TSource, TState, TResult>(
            PayloadReader<TSource> read,
            Func<TState> seed,
            AggregateValueStep<TSource, TState> accumulate,
            AggregateFinish<TState, TResult> finish)
            where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(seed);
            ArgumentNullException.ThrowIfNull(accumulate);
            ArgumentNullException.ThrowIfNull(finish);
            return new AggregateReducer<TKey, TState, TResult>(
                seed,
                (ref TState state, TKey _, InhetoBinary payload) =>
                {
                    ReadValue<TSource> source = read.Read(payload);
                    if (source.HasValue)
                        accumulate(ref state, source.Value);
                },
                (in TState state, TKey _) => finish(in state),
                prepareIndexed: () =>
                {
                    Func<ulong, ReadValue<TSource>>? indexed = PrepareIndexedValueReader(read);
                    return indexed is null
                        ? null
                        : (ref TState state, TKey _, ulong identity) =>
                        {
                            ReadValue<TSource> source = indexed(identity);
                            if (source.HasValue)
                                accumulate(ref state, source.Value);
                        };
                });
        }

        /// <summary>Prepares one exact single-key persistent-index reader for a simple aggregate source path.<br/></summary>
        private Func<ulong, ReadValue<TValue>>? PrepareIndexedValueReader<TValue>(PayloadReader<TValue> read)
        {
            if (read.Path is null ||
                !store.TryPrepareAggregateDefinitionIndexedValueReader(read.Path, out LibraDexIndex<TValue, ulong>? index))
            {
                return null;
            }

            return identity => index!.TryGetSingleKey(identity, out TValue value)
                ? new ReadValue<TValue>(true, value)
                : default;
        }

        /// <summary>Resolves one direct aggregate value path to its ordinal in a candidate covering composite index.<br/></summary>
        private static bool TryGetCompositeValueOrdinal<TValue>(
            PayloadReader<TValue> read,
            LibraDexCompositeIndex index,
            out int ordinal)
        {
            if (read.Path is not null)
            {
                for (int i = 1; i < index.Descriptors.Length; i++)
                {
                    if (MatchesAggregateCompositePart(index, i, read.Path, typeof(TValue)))
                    {
                        ordinal = i;
                        return true;
                    }
                }
            }

            ordinal = -1;
            return false;
        }

        /// <summary>Reads one typed logical value from a borrowed or condition-materialized composite key.<br/></summary>
        private static ReadValue<TValue> ReadCompositeValue<TValue>(AggregateCompositeValues values, int ordinal)
        {
            object? value = values[ordinal];
            return value is null || value is NullKey || value is ScalarNull
                ? default
                : value is TValue typed
                    ? new ReadValue<TValue>(true, typed)
                    : throw new InvalidDataException($"Composite aggregate part {ordinal} returned '{value.GetType().FullName}', not '{typeof(TValue).FullName}'.");
        }
    }

    /// <summary>
    /// Resolves one group-first covering composite index for an aggregate definition.<br/>
    /// The selected index must cover every reducer through one prepared composite callback; otherwise the plan remains eligible for the existing simple-index or authoritative-payload routes.<br/>
    /// </summary>
    private bool TryPrepareAggregateDefinitionCompositeTraversal<TKey, TState>(
        string keyPath,
        Type keyType,
        Condition<T>.EndCondition? where,
        Func<LibraDexCompositeIndex, AggregateFacade.CompositeReduce<TKey, TState>?> prepare,
        out CatalogIdentityGroupIndexes group,
        out LibraDexCompositeIndex compositeIndex,
        out LibraDexConditionEndCondition? condition,
        out AggregateFacade.CompositeReduce<TKey, TState>? reduce)
        where TKey : notnull
    {
        group = default!;
        compositeIndex = default!;
        condition = null;
        reduce = null;
        if (libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly ||
            libraDexIdentityIndexes is null ||
            libraDexCompositeIndexes is null)
        {
            return false;
        }

        Type effectiveType = NormalizeValueType(keyType);
        int selectedPartCount = int.MaxValue;
        for (int i = 0; i < libraDexCompositeIndexes.Length; i++)
        {
            LibraDexCompositeIndex candidate = libraDexCompositeIndexes[i];
            if (!MatchesAggregateCompositePart(candidate, 0, keyPath, effectiveType))
                continue;

            AggregateFacade.CompositeReduce<TKey, TState>? candidateReduce = prepare(candidate);
            if (candidateReduce is null || candidate.Specs.Length >= selectedPartCount)
                continue;

            compositeIndex = candidate;
            reduce = candidateReduce;
            selectedPartCount = candidate.Specs.Length;
        }

        if (reduce is null)
            return false;

        group = libraDexIdentityIndexes;
        if (where is null)
            return true;

        return where.builder.TryBuildLibraDexConditionTree(
            group,
            HasLibraDexIndexInBoundGroup,
            TryResolveLibraDexDerivedSelector,
            out condition);
    }

    /// <summary>Tests whether one composite part is the exact direct property and normalized type required by an aggregate source.<br/></summary>
    private static bool MatchesAggregateCompositePart(
        LibraDexCompositeIndex index,
        int ordinal,
        string path,
        Type valueType)
    {
        if ((uint)ordinal >= (uint)index.Descriptors.Length ||
            NormalizeValueType(index.Descriptors[ordinal].ResultType) != NormalizeValueType(valueType) ||
            index.Descriptors[ordinal] is not ISelectorStructure structure ||
            structure.SemanticDescriptor is not DirectPropertySelectorDescriptor direct)
        {
            return false;
        }

        return string.Equals(direct.PropertyPath, path, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves a simple aggregate grouping path and optional condition into one complete persistent LibraDex traversal.<br/>
    /// A maintained direct scalar source may use a permissive backend; execution deduplicates identities unless the backend proves single-key multiplicity.<br/>
    /// String indexes must retain empty values so indexed and authoritative grouping remain equivalent.<br/>
    /// </summary>
    private bool TryPrepareAggregateDefinitionIndexedTraversal(
        string keyPath,
        Type keyType,
        Condition<T>.EndCondition? where,
        out CatalogIdentityGroupIndexes group,
        out LibraDexSimpleIndex groupingIndex,
        out LibraDexConditionEndCondition condition)
    {
        group = default!;
        groupingIndex = default;
        condition = default!;
        Type effectiveType = Nullable.GetUnderlyingType(keyType) ?? keyType;
        if (libraDexBackendMode == LibraDexIndexBackendMode.FractalOnly ||
            libraDexIdentityIndexes is null ||
            !TryFindPersistentLibraDexAggregateIndex(keyPath, effectiveType, out groupingIndex) ||
            (groupingIndex.Index.IdentityKeyMultiplicity != IdentityKeyMultiplicity.SingleKeyPerIdentity &&
                !IsDirectScalarAggregateGroupingIndex(groupingIndex)) ||
            (effectiveType == typeof(string) && !groupingIndex.IncludeEmptyStrings))
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
    /// Resolves one exact simple aggregate source path into a maintained persistent single-key lookup.<br/>
    /// Empty-string omission is rejected because it would be observationally different from direct Inheto property evaluation.<br/>
    /// </summary>
    private bool TryPrepareAggregateDefinitionIndexedValueReader<TValue>(
        string path,
        out LibraDexIndex<TValue, ulong>? index)
    {
        index = null;
        Type effectiveType = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);
        if (effectiveType != typeof(TValue) ||
            !TryFindPersistentLibraDexAggregateIndex(path, effectiveType, out LibraDexSimpleIndex candidate) ||
            candidate.Index is not LibraDexIndex<TValue, ulong> typed ||
            typed.IdentityKeyMultiplicity != IdentityKeyMultiplicity.SingleKeyPerIdentity ||
            (effectiveType == typeof(string) && !candidate.IncludeEmptyStrings))
        {
            return false;
        }

        index = typed;
        return true;
    }

    /// <summary>
    /// Streams typed aggregate result values from one prepared semantic definition.<br/>
    /// Ordered index routes retain only the current group's accumulator; unordered fallback retains one compact accumulator per distinct group.<br/>
    /// Complete source records are never collected by this reader.<br/>
    /// </summary>
    public sealed class AggregateReader<TResult> : IDisposable
    {
        private readonly Func<IEnumerator<TResult>> prepare;
        private IEnumerator<TResult>? rows;
        private TResult current = default!;
        private bool hasCurrent;
        private bool disposed;

        internal AggregateReader(Func<IEnumerator<TResult>> prepare)
            => this.prepare = prepare;

        /// <summary>Gets whether the execution route has been selected and its traversal context opened.<br/></summary>
        public bool IsPrepared => rows is not null;

        /// <summary>
        /// Selects and prepares the current execution route.<br/>
        /// Ordered index routes open a deferred cursor without traversing the complete source; unordered fallback performs its required compact group-state construction here.<br/>
        /// Calling this method more than once is a no-op; <see cref="Read"/> invokes it automatically when necessary.<br/>
        /// </summary>
        public void Prepare()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            rows ??= prepare();
        }

        /// <summary>Advances to the next completed aggregate result, preparing automatically on the first call.<br/></summary>
        public bool Read()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Prepare();
            hasCurrent = rows!.MoveNext();
            current = hasCurrent ? rows.Current : default!;
            return hasCurrent;
        }

        /// <summary>Aliases <see cref="Read"/> for ordinary enumerator vocabulary.<br/></summary>
        public bool MoveNext() => Read();

        /// <summary>Gets the typed value at the current aggregate row.<br/></summary>
        public TResult Get()
            => hasCurrent ? current : throw new InvalidOperationException("The reader is not positioned on an aggregate result.");

        /// <summary>Gets the typed value at the current aggregate row.<br/></summary>
        public TResult Current => Get();

        /// <summary>Loads subsequent aggregate values into caller-owned storage without allocating a collection.<br/></summary>
        public int Load(Span<TResult> destination)
        {
            int count = 0;
            while (count < destination.Length && Read())
                destination[count++] = Get();
            return count;
        }

        /// <summary>Pulls at most the requested number of aggregate values into a newly allocated exact-length array.<br/></summary>
        public TResult[] Pull(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0)
                return Array.Empty<TResult>();
            var values = new TResult[count];
            int loaded = Load(values);
            if (loaded == count)
                return values;
            if (loaded == 0)
                return Array.Empty<TResult>();
            Array.Resize(ref values, loaded);
            return values;
        }

        /// <summary>Releases the prepared group enumerator.<br/></summary>
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            rows?.Dispose();
        }
    }

    /// <summary>
    /// Recognizes a complete, persistent, direct scalar grouping source independently of backend multiplicity.<br/>
    /// Calculated, function, virtual and custom-codec indexes are excluded: matching a path alone does not prove identical grouping semantics.<br/>
    /// This is eligibility for identity-deduplicated traversal, not a promise permitting deduplication elision.<br/>
    /// Partial indexes are registered separately and never enter the ordinary simple-index descriptor set.<br/>
    /// </summary>
    /// <param name="index">The already matched maintained simple-index descriptor.<br/></param>
    /// <returns>True only for a direct scalar source whose keys can represent the requested property.<br/></returns>
    private static bool IsDirectScalarAggregateGroupingIndex(LibraDexSimpleIndex index)
        => index.OwnerCatalog is null &&
            (index.KeyType == typeof(string) || index.KeyType.IsValueType) &&
            index.CustomKeyReader is null && index.CustomPayloadReader is null &&
            index.CalculatedSelector is null && index.CalculatedReader is null &&
            index.FunctionRuntimeReader is null && !index.IsFunctionIndex && !index.IsVirtualProperty;

    /// <summary>
    /// Retains the first non-null scalar grouping tuple for each UInt64 identity without boxing identities in the set.<br/>
    /// Like authoritative scalar grouping, null keys do not participate; null entries cannot hide a later participating key.<br/>
    /// This is record-contribution deduplication, not DISTINCT values or collection-membership grouping.<br/>
    /// The source remains lazy, and iterator disposal releases its underlying traversal on early exit or failure.<br/>
    /// </summary>
    /// <param name="source">A complete, semantically compatible scalar key/identity stream.<br/></param>
    /// <returns>First participating tuples in source order, with each identity emitted at most once.<br/></returns>
    private static IEnumerable<LibraDexRuntimeTuple> IterateDistinctAggregateGroupingTuples(IEnumerable<LibraDexRuntimeTuple> source)
    {
        HashSet<ulong> seen = new();
        foreach (LibraDexRuntimeTuple tuple in source)
        {
            if (tuple.Key is null)
                continue;
            if (tuple.Identity is not ulong identity)
                throw new InvalidDataException("The indexed aggregate grouping stream returned an incompatible identity type.");
            if (seen.Add(identity))
                yield return tuple;
        }
    }
}
