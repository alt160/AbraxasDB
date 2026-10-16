namespace AbraxasDB
{

    public partial class Selector<T>
    {
        /// <summary>
        /// A composable DateTime value selector for record-store operations.<br/>
        /// </summary>
        /// <remarks>
        /// Construction records expression intent; it does not execute a query or read record values.<br/>
        /// </remarks>
        public class DateTimeSelector : StructuredDateTimeSelector, IValue<DateTime>
        {
            internal DateTimeSelector(Builder parentBuilder, ValueOrExpression<string> selectorExpression, bool addBase = true) : base(parentBuilder, selectorExpression, addBase) { }

            internal DateTimeSelector(
                Builder parentBuilder,
                ValueOrExpression<string> selectorExpression,
                bool addBase,
                IBackendNeutralSelectorDescriptor semanticDescriptor)
                : base(parentBuilder, selectorExpression, addBase, semanticDescriptor) { }

            /// <summary>
            /// Selects the calendar year.<br/>
            /// </summary>
            /// <returns>A numeric selector for the requested temporal component.<br/></returns>
            public IntegralSelector<long> Year => ExtractBits(BuiltInSelectorOperationKind.DateTimeYear);
            /// <summary>
            /// Selects the month number from 1 through 12.<br/>
            /// </summary>
            /// <returns>A numeric selector for the requested temporal component.<br/></returns>
            public IntegralSelector<long> Month => ExtractBits(BuiltInSelectorOperationKind.DateTimeMonth);
            /// <summary>
            /// Selects the day of the month.<br/>
            /// </summary>
            /// <returns>A numeric selector for the requested temporal component.<br/></returns>
            public IntegralSelector<long> Day => ExtractBits(BuiltInSelectorOperationKind.DateTimeDay);
            /// <summary>
            /// Selects the hour from 0 through 23.<br/>
            /// </summary>
            /// <returns>A numeric selector for the requested temporal component.<br/></returns>
            public IntegralSelector<long> Hour => ExtractBits(BuiltInSelectorOperationKind.DateTimeHour);
            /// <summary>
            /// Selects the minute from 0 through 59.<br/>
            /// </summary>
            /// <returns>A numeric selector for the requested temporal component.<br/></returns>
            public IntegralSelector<long> Minute => ExtractBits(BuiltInSelectorOperationKind.DateTimeMinute);
            /// <summary>
            /// Selects the second from 0 through 59.<br/>
            /// </summary>
            /// <returns>A numeric selector for the requested temporal component.<br/></returns>
            public IntegralSelector<long> Second => ExtractBits(BuiltInSelectorOperationKind.DateTimeSecond);
            /// <summary>
            /// Selects the millisecond component from 0 through 999.<br/>
            /// </summary>
            /// <returns>A numeric selector for the requested temporal component.<br/></returns>
            public IntegralSelector<long> Millisecond => ExtractBits(BuiltInSelectorOperationKind.DateTimeMillisecond);
            /// <summary>
            /// Selects the sub-millisecond microsecond component computed as (Ticks % TicksPerMillisecond) / 10.<br/>
            /// </summary>
            /// <returns>A numeric selector for the requested temporal component.<br/></returns>
            /// <remarks>
            /// Despite its historical name, this component is microseconds within the current millisecond, from 0 through 999; it is not a nanosecond count.<br/>
            /// </remarks>
            public IntegralSelector<long> NanoSlice => ExtractBits(BuiltInSelectorOperationKind.DateTimeNanoSlice);
            /// <summary>
            /// Selects the day-of-week number, with Sunday equal to zero.<br/>
            /// </summary>
            /// <returns>A numeric selector for the requested temporal component.<br/></returns>
            public IntegralSelector<long> DayOfWeek => ExtractBits(BuiltInSelectorOperationKind.DateTimeDayOfWeek);

            /// <summary>
            /// Adds one duration to this DateTime selector and returns a DateTime selector carrying the complete semantic expression.<br/>
            /// </summary>
            public DateTimeSelector Add(TimeSpan value)
                => ApplyDateTimeDuration(value, TemporalArithmeticOperationKind.DateTimeAddTimeSpan);

            /// <summary>
            /// Adds one duration selector to this DateTime selector.<br/>
            /// </summary>
            public DateTimeSelector Add(TimeSpanSelector selector)
                => ApplyDateTimeDuration(selector, TemporalArithmeticOperationKind.DateTimeAddTimeSpan);

            /// <summary>
            /// Subtracts one duration from this DateTime selector and returns a DateTime selector.<br/>
            /// </summary>
            public DateTimeSelector Subtract(TimeSpan value)
                => ApplyDateTimeDuration(value, TemporalArithmeticOperationKind.DateTimeSubtractTimeSpan);

            /// <summary>
            /// Subtracts one duration selector from this DateTime selector and returns a DateTime selector.<br/>
            /// </summary>
            public DateTimeSelector Subtract(TimeSpanSelector selector)
                => ApplyDateTimeDuration(selector, TemporalArithmeticOperationKind.DateTimeSubtractTimeSpan);

            /// <summary>
            /// Subtracts another DateTime selector and returns the elapsed signed duration.<br/>
            /// </summary>
            public TimeSpanSelector Subtract(DateTimeSelector selector)
                => ApplyDateTimeDifference(selector);

            /// <summary>
            /// Adds the TimeSpan property selected by a typed lambda.<br/>
            /// </summary>
            public DateTimeSelector PropAdd(System.Linq.Expressions.Expression<Func<T, TimeSpan>> property)
                => Add(new Selector<T>().Prop(property));

            /// <summary>
            /// Begins a textual same-record property addition; <c>.AsTimeSpan</c> completes the operand type.<br/>
            /// </summary>
            public DateTimeDurationPropertyArithmeticSelector PropAdd(string propertyPath)
                => new(this, propertyPath, TemporalArithmeticOperationKind.DateTimeAddTimeSpan);

            /// <summary>
            /// Subtracts the DateTime property selected by a typed lambda and returns a TimeSpan selector.<br/>
            /// </summary>
            public TimeSpanSelector PropSubtract(System.Linq.Expressions.Expression<Func<T, DateTime>> property)
                => Subtract(new Selector<T>().Prop(property));

            /// <summary>
            /// Subtracts the TimeSpan property selected by a typed lambda and returns a DateTime selector.<br/>
            /// </summary>
            public DateTimeSelector PropSubtract(System.Linq.Expressions.Expression<Func<T, TimeSpan>> property)
                => Subtract(new Selector<T>().Prop(property));

            /// <summary>
            /// Begins a textual same-record property subtraction; the completion type determines whether the result is DateTime or TimeSpan.<br/>
            /// </summary>
            public DateTimePropertyArithmeticSelector PropSubtract(string propertyPath)
                => new(this, propertyPath);

            internal DateTimeSelector ApplyDateTimeDuration(
                TimeSpan value,
                TemporalArithmeticOperationKind operation)
            {
                var source = GetSemanticSourceDescriptor()
                    ?? throw new NotSupportedException("DateTime arithmetic requires complete semantic source intent.");
                parentBuilder.MarkComputed();
                var descriptor = new TemporalArithmeticSelectorDescriptor<DateTime, TimeSpan, DateTime>(source, value, operation);
                return new DateTimeSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            internal DateTimeSelector ApplyDateTimeDuration(
                TimeSpanSelector selector,
                TemporalArithmeticOperationKind operation)
            {
                var source = GetSemanticSourceDescriptor()
                    ?? throw new NotSupportedException("DateTime arithmetic requires complete semantic source intent.");
                var right = RequireSemanticDescriptor(selector);
                parentBuilder.MarkComputed();
                var descriptor = new TemporalArithmeticSelectorDescriptor<DateTime, TimeSpan, DateTime>(source, right, operation);
                return new DateTimeSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            internal TimeSpanSelector ApplyDateTimeDifference(DateTimeSelector selector)
            {
                var source = GetSemanticSourceDescriptor()
                    ?? throw new NotSupportedException("DateTime subtraction requires complete semantic source intent.");
                var right = RequireSemanticDescriptor(selector);
                parentBuilder.MarkComputed();
                var descriptor = new TemporalArithmeticSelectorDescriptor<DateTime, DateTime, TimeSpan>(
                    source,
                    right,
                    TemporalArithmeticOperationKind.DateTimeSubtractDateTime);
                return new TimeSpanSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

        }

        /// <summary>
        /// Completes a textual duration operand for DateTime arithmetic.<br/>
        /// </summary>
        /// <remarks>
        /// Construction records expression intent; it does not execute a query or read record values.<br/>
        /// </remarks>
        public sealed class DateTimeDurationPropertyArithmeticSelector
        {
            private readonly DateTimeSelector source;
            private readonly string propertyPath;
            private readonly TemporalArithmeticOperationKind operation;

            internal DateTimeDurationPropertyArithmeticSelector(
                DateTimeSelector source,
                string propertyPath,
                TemporalArithmeticOperationKind operation)
            {
                this.source = source;
                this.propertyPath = propertyPath;
                this.operation = operation;
            }

            /// <summary>
            /// Completes the textual right operand as TimeSpan and returns the resulting DateTime selector.<br/>
            /// </summary>
            public DateTimeSelector AsTimeSpan
                => source.ApplyDateTimeDuration(
                    new Selector<T>().PropPath(propertyPath).AsTimeSpan,
                    operation);
        }

        /// <summary>
        /// Completes a textual DateTime arithmetic operand as DateTime for a duration difference or TimeSpan for a shifted date.<br/>
        /// </summary>
        /// <remarks>
        /// Construction records expression intent; it does not execute a query or read record values.<br/>
        /// </remarks>
        public sealed class DateTimePropertyArithmeticSelector
        {
            private readonly DateTimeSelector source;
            private readonly string propertyPath;

            internal DateTimePropertyArithmeticSelector(DateTimeSelector source, string propertyPath)
            {
                this.source = source;
                this.propertyPath = propertyPath;
            }

            /// <summary>
            /// Completes the right property as DateTime and returns the signed difference as TimeSpan.<br/>
            /// </summary>
            public TimeSpanSelector AsDateTime
                => source.ApplyDateTimeDifference(
                    new Selector<T>().PropPath(propertyPath).AsDateTime);

            /// <summary>
            /// Completes the right property as TimeSpan and returns the shifted DateTime selector.<br/>
            /// </summary>
            public DateTimeSelector AsTimeSpan
                => source.ApplyDateTimeDuration(
                    new Selector<T>().PropPath(propertyPath).AsTimeSpan,
                    TemporalArithmeticOperationKind.DateTimeSubtractTimeSpan);
        }
    }
}

