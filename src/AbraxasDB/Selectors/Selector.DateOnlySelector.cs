namespace AbraxasDB
{

    public partial class Selector<T>
    {
        /// <summary>
        /// A composable DateOnly value selector for record-store operations.<br/>
        /// </summary>
        /// <remarks>
        /// Construction records expression intent; it does not execute a query or read record values.<br/>
        /// </remarks>
        public class DateOnlySelector : StructuredDateTimeSelector, IValue<DateOnly>
        {
            internal DateOnlySelector(Builder parentBuilder, ValueOrExpression<string> selectorExpression, bool addBase = true) : base(parentBuilder, selectorExpression, addBase) { }

            /// <summary>
            /// Selects the calendar year.<br/>
            /// </summary>
            /// <returns>A numeric selector for the requested temporal component.<br/></returns>
            public IntegralSelector<long> Year => ExtractBits(BuiltInSelectorOperationKind.DateOnlyYear);
            /// <summary>
            /// Selects the month number from 1 through 12.<br/>
            /// </summary>
            /// <returns>A numeric selector for the requested temporal component.<br/></returns>
            public IntegralSelector<long> Month => ExtractBits(BuiltInSelectorOperationKind.DateOnlyMonth);
            /// <summary>
            /// Selects the day of the month.<br/>
            /// </summary>
            /// <returns>A numeric selector for the requested temporal component.<br/></returns>
            public IntegralSelector<long> Day => ExtractBits(BuiltInSelectorOperationKind.DateOnlyDay);
            /// <summary>
            /// Selects the day-of-week number, with Sunday equal to zero.<br/>
            /// </summary>
            /// <returns>A numeric selector for the requested temporal component.<br/></returns>
            public IntegralSelector<long> DayOfWeek => ExtractBits(BuiltInSelectorOperationKind.DateOnlyDayOfWeek);

        }
    }
}

