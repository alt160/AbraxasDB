namespace AbraxasDB
{

    public partial class Selector<T>
    {
        /// <summary>
        /// A composable TimeOnly value selector for record-store operations.<br/>
        /// </summary>
        /// <remarks>
        /// Construction records expression intent; it does not execute a query or read record values.<br/>
        /// </remarks>
        public class TimeOnlySelector : StructuredDateTimeSelector, IValue<TimeOnly>
        {
            internal TimeOnlySelector(Builder parentBuilder, ValueOrExpression<string> selectorExpression, bool addBase = true) : base(parentBuilder, selectorExpression, addBase) { }

            internal TimeOnlySelector(
                Builder parentBuilder,
                ValueOrExpression<string> selectorExpression,
                bool addBase,
                IBackendNeutralSelectorDescriptor semanticDescriptor)
                : base(parentBuilder, selectorExpression, addBase, semanticDescriptor) { }

            /// <summary>
            /// Selects the hour from 0 through 23.<br/>
            /// </summary>
            /// <returns>A numeric selector for the requested temporal component.<br/></returns>
            public IntegralSelector<long> Hour => ExtractBits(BuiltInSelectorOperationKind.TimeOnlyHour);
            /// <summary>
            /// Selects the minute from 0 through 59.<br/>
            /// </summary>
            /// <returns>A numeric selector for the requested temporal component.<br/></returns>
            public IntegralSelector<long> Minute => ExtractBits(BuiltInSelectorOperationKind.TimeOnlyMinute);
            /// <summary>
            /// Selects the second from 0 through 59.<br/>
            /// </summary>
            /// <returns>A numeric selector for the requested temporal component.<br/></returns>
            public IntegralSelector<long> Second => ExtractBits(BuiltInSelectorOperationKind.TimeOnlySecond);
            /// <summary>
            /// Selects the millisecond component from 0 through 999.<br/>
            /// </summary>
            /// <returns>A numeric selector for the requested temporal component.<br/></returns>
            public IntegralSelector<long> Millisecond => ExtractBits(BuiltInSelectorOperationKind.TimeOnlyMillisecond);
            /// <summary>
            /// Selects the sub-millisecond microsecond component computed as (Ticks % TicksPerMillisecond) / 10.<br/>
            /// </summary>
            /// <returns>A numeric selector for the requested temporal component.<br/></returns>
            /// <remarks>
            /// Despite its historical name, this component is microseconds within the current millisecond, from 0 through 999; it is not a nanosecond count.<br/>
            /// </remarks>
            public IntegralSelector<long> NanoSlice => ExtractBits(BuiltInSelectorOperationKind.TimeOnlyNanoSlice);

            /// <summary>
            /// Adds one duration to this time-of-day selector using <see cref="TimeOnly.Add(TimeSpan)"/> rollover semantics.<br/>
            /// </summary>
            public TimeOnlySelector Add(TimeSpan value)
                => ApplyDuration(value, TemporalArithmeticOperationKind.TimeOnlyAddTimeSpan);

            /// <summary>
            /// Adds one duration selector to this time-of-day selector.<br/>
            /// </summary>
            public TimeOnlySelector Add(TimeSpanSelector selector)
                => ApplyDuration(selector, TemporalArithmeticOperationKind.TimeOnlyAddTimeSpan);

            /// <summary>
            /// Subtracts one duration using 24-hour rollover semantics.<br/>
            /// </summary>
            public TimeOnlySelector Subtract(TimeSpan value)
                => ApplyDuration(value, TemporalArithmeticOperationKind.TimeOnlySubtractTimeSpan);

            /// <summary>
            /// Subtracts one duration selector using 24-hour rollover semantics.<br/>
            /// </summary>
            public TimeOnlySelector Subtract(TimeSpanSelector selector)
                => ApplyDuration(selector, TemporalArithmeticOperationKind.TimeOnlySubtractTimeSpan);

            /// <summary>
            /// Subtracts another time-of-day selector and returns the signed duration between them.<br/>
            /// </summary>
            public TimeSpanSelector Subtract(TimeOnlySelector selector)
                => ApplyDifference(selector);

            /// <summary>
            /// Adds the TimeSpan property selected by a typed lambda.<br/>
            /// </summary>
            public TimeOnlySelector PropAdd(System.Linq.Expressions.Expression<Func<T, TimeSpan>> property)
                => Add(new Selector<T>().Prop(property));

            /// <summary>
            /// Begins textual TimeSpan-property addition.<br/>
            /// </summary>
            public TimeOnlyDurationPropertyArithmeticSelector PropAdd(string propertyPath)
                => new(this, propertyPath, TemporalArithmeticOperationKind.TimeOnlyAddTimeSpan);

            /// <summary>
            /// Subtracts the TimeOnly property selected by a typed lambda and returns a TimeSpan.<br/>
            /// </summary>
            public TimeSpanSelector PropSubtract(System.Linq.Expressions.Expression<Func<T, TimeOnly>> property)
                => Subtract(new Selector<T>().Prop(property));

            /// <summary>
            /// Subtracts the TimeSpan property selected by a typed lambda and returns a TimeOnly.<br/>
            /// </summary>
            public TimeOnlySelector PropSubtract(System.Linq.Expressions.Expression<Func<T, TimeSpan>> property)
                => Subtract(new Selector<T>().Prop(property));

            /// <summary>
            /// Begins textual TimeOnly or TimeSpan property subtraction.<br/>
            /// </summary>
            public TimeOnlyPropertyArithmeticSelector PropSubtract(string propertyPath)
                => new(this, propertyPath);

            internal TimeOnlySelector ApplyDuration(TimeSpan value, TemporalArithmeticOperationKind operation)
            {
                var source = GetSemanticSourceDescriptor()
                    ?? throw new NotSupportedException("TimeOnly arithmetic requires complete semantic source intent.");
                parentBuilder.MarkComputed();
                var descriptor = new TemporalArithmeticSelectorDescriptor<TimeOnly, TimeSpan, TimeOnly>(source, value, operation);
                return new TimeOnlySelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            internal TimeOnlySelector ApplyDuration(TimeSpanSelector selector, TemporalArithmeticOperationKind operation)
            {
                var source = GetSemanticSourceDescriptor()
                    ?? throw new NotSupportedException("TimeOnly arithmetic requires complete semantic source intent.");
                var right = RequireSemanticDescriptor(selector);
                parentBuilder.MarkComputed();
                var descriptor = new TemporalArithmeticSelectorDescriptor<TimeOnly, TimeSpan, TimeOnly>(source, right, operation);
                return new TimeOnlySelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            internal TimeSpanSelector ApplyDifference(TimeOnlySelector selector)
            {
                var source = GetSemanticSourceDescriptor()
                    ?? throw new NotSupportedException("TimeOnly subtraction requires complete semantic source intent.");
                var right = RequireSemanticDescriptor(selector);
                parentBuilder.MarkComputed();
                var descriptor = new TemporalArithmeticSelectorDescriptor<TimeOnly, TimeOnly, TimeSpan>(
                    source,
                    right,
                    TemporalArithmeticOperationKind.TimeOnlySubtractTimeOnly);
                return new TimeSpanSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

        }

        /// <summary>
        /// Completes a textual duration operand for time-of-day arithmetic with daily rollover.<br/>
        /// </summary>
        /// <remarks>
        /// Construction records expression intent; it does not execute a query or read record values.<br/>
        /// </remarks>
        public sealed class TimeOnlyDurationPropertyArithmeticSelector
        {
            private readonly TimeOnlySelector source;
            private readonly string propertyPath;
            private readonly TemporalArithmeticOperationKind operation;

            internal TimeOnlyDurationPropertyArithmeticSelector(
                TimeOnlySelector source,
                string propertyPath,
                TemporalArithmeticOperationKind operation)
            {
                this.source = source;
                this.propertyPath = propertyPath;
                this.operation = operation;
            }

            /// <summary>
            /// Completes the textual right operand as TimeSpan.<br/>
            /// </summary>
            public TimeOnlySelector AsTimeSpan
                => source.ApplyDuration(new Selector<T>().PropPath(propertyPath).AsTimeSpan, operation);
        }

        /// <summary>
        /// Completes a textual time-of-day operand for a TimeOnly difference.<br/>
        /// </summary>
        /// <remarks>
        /// Construction records expression intent; it does not execute a query or read record values.<br/>
        /// </remarks>
        public sealed class TimeOnlyPropertyArithmeticSelector
        {
            private readonly TimeOnlySelector source;
            private readonly string propertyPath;

            internal TimeOnlyPropertyArithmeticSelector(TimeOnlySelector source, string propertyPath)
            {
                this.source = source;
                this.propertyPath = propertyPath;
            }

            /// <summary>
            /// Completes the right property as TimeOnly and returns the signed difference as TimeSpan.<br/>
            /// </summary>
            public TimeSpanSelector AsTimeOnly
                => source.ApplyDifference(new Selector<T>().PropPath(propertyPath).AsTimeOnly);

            /// <summary>
            /// Completes the right property as TimeSpan and returns the shifted TimeOnly selector.<br/>
            /// </summary>
            public TimeOnlySelector AsTimeSpan
                => source.ApplyDuration(
                    new Selector<T>().PropPath(propertyPath).AsTimeSpan,
                    TemporalArithmeticOperationKind.TimeOnlySubtractTimeSpan);
        }
    }
}

