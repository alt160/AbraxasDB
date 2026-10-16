using System.Linq.Expressions;
using Inheto;

namespace AbraxasDB;

public partial class RecordStore<T>
{
    public sealed partial class AggregateFacade
    {
        public sealed partial class AggregateDefinition<TKey>
        {
            /// <summary>
            /// Adds the value from the first record encountered in each group.<br/>
            /// A null or missing value is retained as the endpoint value rather than skipped.<br/>
            /// </summary>
            public AggregateDefinition<TKey, TValue> AddFirst<TValue>(Expression<Func<T, TValue>> value)
                => Add(owner.CreateEndpointReducer<TKey, TValue>(owner.CreateExpressionReader(value), first: true));

            /// <summary>
            /// Adds the value from the last record encountered in each group.<br/>
            /// A null or missing value is retained as the endpoint value rather than skipped.<br/>
            /// </summary>
            public AggregateDefinition<TKey, TValue> AddLast<TValue>(Expression<Func<T, TValue>> value)
                => Add(owner.CreateEndpointReducer<TKey, TValue>(owner.CreateExpressionReader(value), first: false));

            /// <summary>
            /// Adds the selected value having the smallest non-null order value in each group.<br/>
            /// The selected value may itself be null; equal order values retain the first encountered record.<br/>
            /// </summary>
            public AggregateDefinition<TKey, TValue> AddFirstBy<TValue, TOrder>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order)
                where TOrder : IComparable<TOrder>
                => Add(owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder>(owner.CreateExpressionReader(value), owner.CreateExpressionReader(order), first: true));

            /// <summary>
            /// Adds the selected value having the smallest non-null order and tie-break values in each group.<br/>
            /// The selected value may itself be null; completely equal keys retain the first encountered record.<br/>
            /// </summary>
            public AggregateDefinition<TKey, TValue> AddFirstBy<TValue, TOrder, TThen>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order, Expression<Func<T, TThen>> thenBy)
                where TOrder : IComparable<TOrder>
                where TThen : IComparable<TThen>
                => Add(owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder, TThen>(owner.CreateExpressionReader(value), owner.CreateExpressionReader(order), owner.CreateExpressionReader(thenBy), first: true));

            /// <summary>
            /// Adds the selected value having the largest non-null order value in each group.<br/>
            /// The selected value may itself be null; equal order values retain the first encountered record.<br/>
            /// </summary>
            public AggregateDefinition<TKey, TValue> AddLastBy<TValue, TOrder>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order)
                where TOrder : IComparable<TOrder>
                => Add(owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder>(owner.CreateExpressionReader(value), owner.CreateExpressionReader(order), first: false));

            /// <summary>
            /// Adds the selected value having the largest non-null order and tie-break values in each group.<br/>
            /// The selected value may itself be null; completely equal keys retain the first encountered record.<br/>
            /// </summary>
            public AggregateDefinition<TKey, TValue> AddLastBy<TValue, TOrder, TThen>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order, Expression<Func<T, TThen>> thenBy)
                where TOrder : IComparable<TOrder>
                where TThen : IComparable<TThen>
                => Add(owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder, TThen>(owner.CreateExpressionReader(value), owner.CreateExpressionReader(order), owner.CreateExpressionReader(thenBy), first: false));
        }

        public sealed partial class AggregateDefinition<TKey, T1>
        {
            /// <summary>Adds the first encountered value, preserving a null or missing endpoint rather than skipping it.<br/></summary>
            public AggregateDefinition<TKey, T1, TValue> AddFirst<TValue>(Expression<Func<T, TValue>> value) => Add(Owner.CreateEndpointReducer<TKey, TValue>(Owner.CreateExpressionReader(value), true));
            /// <summary>Adds the last encountered value, preserving a null or missing endpoint rather than skipping it.<br/></summary>
            public AggregateDefinition<TKey, T1, TValue> AddLast<TValue>(Expression<Func<T, TValue>> value) => Add(Owner.CreateEndpointReducer<TKey, TValue>(Owner.CreateExpressionReader(value), false));
            /// <summary>Adds the value having the smallest non-null order value; equal order values retain the first encountered record.<br/></summary>
            public AggregateDefinition<TKey, T1, TValue> AddFirstBy<TValue, TOrder>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order) where TOrder : IComparable<TOrder> => Add(Owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(order), true));
            /// <summary>Adds the value having the smallest non-null order and tie-break values; completely equal keys retain the first encountered record.<br/></summary>
            public AggregateDefinition<TKey, T1, TValue> AddFirstBy<TValue, TOrder, TThen>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order, Expression<Func<T, TThen>> thenBy) where TOrder : IComparable<TOrder> where TThen : IComparable<TThen> => Add(Owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder, TThen>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(order), Owner.CreateExpressionReader(thenBy), true));
            /// <summary>Adds the value having the largest non-null order value; equal order values retain the first encountered record.<br/></summary>
            public AggregateDefinition<TKey, T1, TValue> AddLastBy<TValue, TOrder>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order) where TOrder : IComparable<TOrder> => Add(Owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(order), false));
            /// <summary>Adds the value having the largest non-null order and tie-break values; completely equal keys retain the first encountered record.<br/></summary>
            public AggregateDefinition<TKey, T1, TValue> AddLastBy<TValue, TOrder, TThen>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order, Expression<Func<T, TThen>> thenBy) where TOrder : IComparable<TOrder> where TThen : IComparable<TThen> => Add(Owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder, TThen>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(order), Owner.CreateExpressionReader(thenBy), false));
        }

        public sealed partial class AggregateDefinition<TKey, T1, T2>
        {
            /// <summary>Adds the first encountered value, preserving a null or missing endpoint rather than skipping it.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, TValue> AddFirst<TValue>(Expression<Func<T, TValue>> value) => Add(Owner.CreateEndpointReducer<TKey, TValue>(Owner.CreateExpressionReader(value), true));
            /// <summary>Adds the last encountered value, preserving a null or missing endpoint rather than skipping it.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, TValue> AddLast<TValue>(Expression<Func<T, TValue>> value) => Add(Owner.CreateEndpointReducer<TKey, TValue>(Owner.CreateExpressionReader(value), false));
            /// <summary>Adds the value having the smallest non-null order value; equal order values retain the first encountered record.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, TValue> AddFirstBy<TValue, TOrder>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order) where TOrder : IComparable<TOrder> => Add(Owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(order), true));
            /// <summary>Adds the value having the smallest non-null order and tie-break values; completely equal keys retain the first encountered record.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, TValue> AddFirstBy<TValue, TOrder, TThen>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order, Expression<Func<T, TThen>> thenBy) where TOrder : IComparable<TOrder> where TThen : IComparable<TThen> => Add(Owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder, TThen>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(order), Owner.CreateExpressionReader(thenBy), true));
            /// <summary>Adds the value having the largest non-null order value; equal order values retain the first encountered record.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, TValue> AddLastBy<TValue, TOrder>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order) where TOrder : IComparable<TOrder> => Add(Owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(order), false));
            /// <summary>Adds the value having the largest non-null order and tie-break values; completely equal keys retain the first encountered record.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, TValue> AddLastBy<TValue, TOrder, TThen>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order, Expression<Func<T, TThen>> thenBy) where TOrder : IComparable<TOrder> where TThen : IComparable<TThen> => Add(Owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder, TThen>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(order), Owner.CreateExpressionReader(thenBy), false));
        }

        public sealed partial class AggregateDefinition<TKey, T1, T2, T3>
        {
            /// <summary>Adds the first encountered value, preserving a null or missing endpoint rather than skipping it.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, TValue> AddFirst<TValue>(Expression<Func<T, TValue>> value) => Add(Owner.CreateEndpointReducer<TKey, TValue>(Owner.CreateExpressionReader(value), true));
            /// <summary>Adds the last encountered value, preserving a null or missing endpoint rather than skipping it.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, TValue> AddLast<TValue>(Expression<Func<T, TValue>> value) => Add(Owner.CreateEndpointReducer<TKey, TValue>(Owner.CreateExpressionReader(value), false));
            /// <summary>Adds the value having the smallest non-null order value; equal order values retain the first encountered record.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, TValue> AddFirstBy<TValue, TOrder>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order) where TOrder : IComparable<TOrder> => Add(Owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(order), true));
            /// <summary>Adds the value having the smallest non-null order and tie-break values; completely equal keys retain the first encountered record.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, TValue> AddFirstBy<TValue, TOrder, TThen>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order, Expression<Func<T, TThen>> thenBy) where TOrder : IComparable<TOrder> where TThen : IComparable<TThen> => Add(Owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder, TThen>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(order), Owner.CreateExpressionReader(thenBy), true));
            /// <summary>Adds the value having the largest non-null order value; equal order values retain the first encountered record.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, TValue> AddLastBy<TValue, TOrder>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order) where TOrder : IComparable<TOrder> => Add(Owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(order), false));
            /// <summary>Adds the value having the largest non-null order and tie-break values; completely equal keys retain the first encountered record.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, TValue> AddLastBy<TValue, TOrder, TThen>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order, Expression<Func<T, TThen>> thenBy) where TOrder : IComparable<TOrder> where TThen : IComparable<TThen> => Add(Owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder, TThen>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(order), Owner.CreateExpressionReader(thenBy), false));
        }

        public sealed partial class AggregateDefinition<TKey, T1, T2, T3, T4>
        {
            /// <summary>Adds the first encountered value, preserving a null or missing endpoint rather than skipping it.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, TValue> AddFirst<TValue>(Expression<Func<T, TValue>> value) => Add(Owner.CreateEndpointReducer<TKey, TValue>(Owner.CreateExpressionReader(value), true));
            /// <summary>Adds the last encountered value, preserving a null or missing endpoint rather than skipping it.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, TValue> AddLast<TValue>(Expression<Func<T, TValue>> value) => Add(Owner.CreateEndpointReducer<TKey, TValue>(Owner.CreateExpressionReader(value), false));
            /// <summary>Adds the value having the smallest non-null order value; equal order values retain the first encountered record.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, TValue> AddFirstBy<TValue, TOrder>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order) where TOrder : IComparable<TOrder> => Add(Owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(order), true));
            /// <summary>Adds the value having the smallest non-null order and tie-break values; completely equal keys retain the first encountered record.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, TValue> AddFirstBy<TValue, TOrder, TThen>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order, Expression<Func<T, TThen>> thenBy) where TOrder : IComparable<TOrder> where TThen : IComparable<TThen> => Add(Owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder, TThen>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(order), Owner.CreateExpressionReader(thenBy), true));
            /// <summary>Adds the value having the largest non-null order value; equal order values retain the first encountered record.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, TValue> AddLastBy<TValue, TOrder>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order) where TOrder : IComparable<TOrder> => Add(Owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(order), false));
            /// <summary>Adds the value having the largest non-null order and tie-break values; completely equal keys retain the first encountered record.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, TValue> AddLastBy<TValue, TOrder, TThen>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order, Expression<Func<T, TThen>> thenBy) where TOrder : IComparable<TOrder> where TThen : IComparable<TThen> => Add(Owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder, TThen>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(order), Owner.CreateExpressionReader(thenBy), false));
        }

        public sealed partial class AggregateDefinition<TKey, T1, T2, T3, T4, T5>
        {
            /// <summary>Adds the first encountered value, preserving a null or missing endpoint rather than skipping it.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, TValue> AddFirst<TValue>(Expression<Func<T, TValue>> value) => Add(Owner.CreateEndpointReducer<TKey, TValue>(Owner.CreateExpressionReader(value), true));
            /// <summary>Adds the last encountered value, preserving a null or missing endpoint rather than skipping it.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, TValue> AddLast<TValue>(Expression<Func<T, TValue>> value) => Add(Owner.CreateEndpointReducer<TKey, TValue>(Owner.CreateExpressionReader(value), false));
            /// <summary>Adds the value having the smallest non-null order value; equal order values retain the first encountered record.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, TValue> AddFirstBy<TValue, TOrder>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order) where TOrder : IComparable<TOrder> => Add(Owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(order), true));
            /// <summary>Adds the value having the smallest non-null order and tie-break values; completely equal keys retain the first encountered record.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, TValue> AddFirstBy<TValue, TOrder, TThen>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order, Expression<Func<T, TThen>> thenBy) where TOrder : IComparable<TOrder> where TThen : IComparable<TThen> => Add(Owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder, TThen>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(order), Owner.CreateExpressionReader(thenBy), true));
            /// <summary>Adds the value having the largest non-null order value; equal order values retain the first encountered record.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, TValue> AddLastBy<TValue, TOrder>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order) where TOrder : IComparable<TOrder> => Add(Owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(order), false));
            /// <summary>Adds the value having the largest non-null order and tie-break values; completely equal keys retain the first encountered record.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, TValue> AddLastBy<TValue, TOrder, TThen>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order, Expression<Func<T, TThen>> thenBy) where TOrder : IComparable<TOrder> where TThen : IComparable<TThen> => Add(Owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder, TThen>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(order), Owner.CreateExpressionReader(thenBy), false));
        }

        public sealed partial class AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6>
        {
            /// <summary>Adds the first encountered value, preserving a null or missing endpoint rather than skipping it.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, TValue> AddFirst<TValue>(Expression<Func<T, TValue>> value) => Add(Owner.CreateEndpointReducer<TKey, TValue>(Owner.CreateExpressionReader(value), true));
            /// <summary>Adds the last encountered value, preserving a null or missing endpoint rather than skipping it.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, TValue> AddLast<TValue>(Expression<Func<T, TValue>> value) => Add(Owner.CreateEndpointReducer<TKey, TValue>(Owner.CreateExpressionReader(value), false));
            /// <summary>Adds the value having the smallest non-null order value; equal order values retain the first encountered record.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, TValue> AddFirstBy<TValue, TOrder>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order) where TOrder : IComparable<TOrder> => Add(Owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(order), true));
            /// <summary>Adds the value having the smallest non-null order and tie-break values; completely equal keys retain the first encountered record.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, TValue> AddFirstBy<TValue, TOrder, TThen>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order, Expression<Func<T, TThen>> thenBy) where TOrder : IComparable<TOrder> where TThen : IComparable<TThen> => Add(Owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder, TThen>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(order), Owner.CreateExpressionReader(thenBy), true));
            /// <summary>Adds the value having the largest non-null order value; equal order values retain the first encountered record.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, TValue> AddLastBy<TValue, TOrder>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order) where TOrder : IComparable<TOrder> => Add(Owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(order), false));
            /// <summary>Adds the value having the largest non-null order and tie-break values; completely equal keys retain the first encountered record.<br/></summary>
            public AggregateDefinition<TKey, T1, T2, T3, T4, T5, T6, TValue> AddLastBy<TValue, TOrder, TThen>(Expression<Func<T, TValue>> value, Expression<Func<T, TOrder>> order, Expression<Func<T, TThen>> thenBy) where TOrder : IComparable<TOrder> where TThen : IComparable<TThen> => Add(Owner.CreateOrderedEndpointReducer<TKey, TValue, TOrder, TThen>(Owner.CreateExpressionReader(value), Owner.CreateExpressionReader(order), Owner.CreateExpressionReader(thenBy), false));
        }

        internal struct OrderedEndpointState<TValue, TOrder>
        {
            internal bool Assigned;
            internal TValue Value;
            internal TOrder Order;
        }

        internal struct OrderedEndpointState<TValue, TOrder, TThen>
        {
            internal bool Assigned;
            internal TValue Value;
            internal TOrder Order;
            internal TThen Then;
        }

        /// <summary>
        /// Creates an encounter-order endpoint reducer with payload, direct-index, and covering-composite execution paths.<br/>
        /// Assignment is based on record position, so a missing or null selected value is deliberately retained as default.<br/>
        /// </summary>
        private AggregateReducer<TKey, FirstState<TValue>, TValue> CreateEndpointReducer<TKey, TValue>(PayloadReader<TValue> read, bool first)
            where TKey : notnull
            => new(
                static () => default,
                (ref FirstState<TValue> state, TKey _, InhetoBinary payload) => AssignEndpoint(ref state, read.Read(payload), first),
                static (in FirstState<TValue> state, TKey _) => state.Value,
                prepareIndexed: () =>
                {
                    Func<ulong, ReadValue<TValue>>? indexed = PrepareIndexedValueReader(read);
                    return indexed is null ? null : (ref FirstState<TValue> state, TKey _, ulong identity) => AssignEndpoint(ref state, indexed(identity), first);
                },
                prepareComposite: index =>
                {
                    if (!TryGetCompositeValueOrdinal(read, index, out int ordinal))
                        return null;
                    return (ref FirstState<TValue> state, TKey _, AggregateCompositeValues values) => AssignEndpoint(ref state, ReadCompositeValue<TValue>(values, ordinal), first);
                });

        /// <summary>
        /// Creates an explicitly ordered endpoint reducer without retaining source records.<br/>
        /// Null or missing order values do not participate; the selected endpoint value may be null or missing.<br/>
        /// </summary>
        private AggregateReducer<TKey, OrderedEndpointState<TValue, TOrder>, TValue> CreateOrderedEndpointReducer<TKey, TValue, TOrder>(PayloadReader<TValue> readValue, PayloadReader<TOrder> readOrder, bool first)
            where TKey : notnull
            where TOrder : IComparable<TOrder>
            => new(
                static () => default,
                (ref OrderedEndpointState<TValue, TOrder> state, TKey _, InhetoBinary payload) => ConsiderOrderedEndpoint(ref state, readValue.Read(payload), readOrder.Read(payload), first),
                static (in OrderedEndpointState<TValue, TOrder> state, TKey _) => state.Value,
                prepareIndexed: () =>
                {
                    Func<ulong, ReadValue<TValue>>? indexedValue = PrepareIndexedValueReader(readValue);
                    Func<ulong, ReadValue<TOrder>>? indexedOrder = PrepareIndexedValueReader(readOrder);
                    return indexedValue is null || indexedOrder is null
                        ? null
                        : (ref OrderedEndpointState<TValue, TOrder> state, TKey _, ulong identity) => ConsiderOrderedEndpoint(ref state, indexedValue(identity), indexedOrder(identity), first);
                },
                prepareComposite: index =>
                {
                    if (!TryGetCompositeValueOrdinal(readValue, index, out int valueOrdinal) ||
                        !TryGetCompositeValueOrdinal(readOrder, index, out int orderOrdinal))
                        return null;
                    return (ref OrderedEndpointState<TValue, TOrder> state, TKey _, AggregateCompositeValues values) =>
                        ConsiderOrderedEndpoint(ref state, ReadCompositeValue<TValue>(values, valueOrdinal), ReadCompositeValue<TOrder>(values, orderOrdinal), first);
                });

        /// <summary>
        /// Creates an explicitly ordered endpoint reducer with one deterministic tie-break selector.<br/>
        /// Null or missing ordering parts do not participate; the selected endpoint value may be null or missing.<br/>
        /// </summary>
        private AggregateReducer<TKey, OrderedEndpointState<TValue, TOrder, TThen>, TValue> CreateOrderedEndpointReducer<TKey, TValue, TOrder, TThen>(PayloadReader<TValue> readValue, PayloadReader<TOrder> readOrder, PayloadReader<TThen> readThen, bool first)
            where TKey : notnull
            where TOrder : IComparable<TOrder>
            where TThen : IComparable<TThen>
            => new(
                static () => default,
                (ref OrderedEndpointState<TValue, TOrder, TThen> state, TKey _, InhetoBinary payload) => ConsiderOrderedEndpoint(ref state, readValue.Read(payload), readOrder.Read(payload), readThen.Read(payload), first),
                static (in OrderedEndpointState<TValue, TOrder, TThen> state, TKey _) => state.Value,
                prepareIndexed: () =>
                {
                    Func<ulong, ReadValue<TValue>>? indexedValue = PrepareIndexedValueReader(readValue);
                    Func<ulong, ReadValue<TOrder>>? indexedOrder = PrepareIndexedValueReader(readOrder);
                    Func<ulong, ReadValue<TThen>>? indexedThen = PrepareIndexedValueReader(readThen);
                    return indexedValue is null || indexedOrder is null || indexedThen is null
                        ? null
                        : (ref OrderedEndpointState<TValue, TOrder, TThen> state, TKey _, ulong identity) => ConsiderOrderedEndpoint(ref state, indexedValue(identity), indexedOrder(identity), indexedThen(identity), first);
                },
                prepareComposite: index =>
                {
                    if (!TryGetCompositeValueOrdinal(readValue, index, out int valueOrdinal) ||
                        !TryGetCompositeValueOrdinal(readOrder, index, out int orderOrdinal) ||
                        !TryGetCompositeValueOrdinal(readThen, index, out int thenOrdinal))
                        return null;
                    return (ref OrderedEndpointState<TValue, TOrder, TThen> state, TKey _, AggregateCompositeValues values) =>
                        ConsiderOrderedEndpoint(ref state, ReadCompositeValue<TValue>(values, valueOrdinal), ReadCompositeValue<TOrder>(values, orderOrdinal), ReadCompositeValue<TThen>(values, thenOrdinal), first);
                });

        /// <summary>Assigns the selected endpoint once for first or on every record for last, including null endpoints.<br/></summary>
        private static void AssignEndpoint<TValue>(ref FirstState<TValue> state, ReadValue<TValue> value, bool first)
        {
            if (first && state.Assigned)
                return;
            state.Assigned = true;
            state.Value = value.HasValue ? value.Value : default!;
        }

        /// <summary>Compares one non-null order value and retains the corresponding endpoint value without allocating row state.<br/></summary>
        private static void ConsiderOrderedEndpoint<TValue, TOrder>(ref OrderedEndpointState<TValue, TOrder> state, ReadValue<TValue> value, ReadValue<TOrder> order, bool first)
            where TOrder : IComparable<TOrder>
        {
            if (!order.HasValue || order.Value is null)
                return;
            if (state.Assigned)
            {
                int comparison = order.Value.CompareTo(state.Order);
                if (first ? comparison >= 0 : comparison <= 0)
                    return;
            }
            state.Assigned = true;
            state.Order = order.Value;
            state.Value = value.HasValue ? value.Value : default!;
        }

        /// <summary>Compares primary and tie-break values lexicographically and retains the selected endpoint without allocating row state.<br/></summary>
        private static void ConsiderOrderedEndpoint<TValue, TOrder, TThen>(ref OrderedEndpointState<TValue, TOrder, TThen> state, ReadValue<TValue> value, ReadValue<TOrder> order, ReadValue<TThen> then, bool first)
            where TOrder : IComparable<TOrder>
            where TThen : IComparable<TThen>
        {
            if (!order.HasValue || order.Value is null || !then.HasValue || then.Value is null)
                return;
            if (state.Assigned)
            {
                int comparison = order.Value.CompareTo(state.Order);
                if (comparison == 0)
                    comparison = then.Value.CompareTo(state.Then);
                if (first ? comparison >= 0 : comparison <= 0)
                    return;
            }
            state.Assigned = true;
            state.Order = order.Value;
            state.Then = then.Value;
            state.Value = value.HasValue ? value.Value : default!;
        }
    }
}
