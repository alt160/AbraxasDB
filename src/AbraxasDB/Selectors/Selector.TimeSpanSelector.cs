namespace AbraxasDB
{

    public partial class Selector<T>
    {
        /// <summary>
        /// A composable TimeSpan value selector for record-store operations.<br/>
        /// </summary>
        /// <remarks>
        /// Construction records expression intent; it does not execute a query or read record values.<br/>
        /// </remarks>
        public class TimeSpanSelector : IValue<TimeSpan>,ISelectorStructure
        {
            private readonly Builder parentBuilder;
            private readonly ValueOrExpression<string> selectorExpression;
            private readonly IBackendNeutralSelectorDescriptor? semanticDescriptor;

            string? ISelectorStructure.SimplePropertyPath => parentBuilder.SimplePropertyPath;
            string? ISelectorStructure.TypedSourcePropertyPath => parentBuilder.TypedSourcePropertyPath;
            Type? ISelectorStructure.ResultType => typeof(TimeSpan);
            IBackendNeutralSelectorDescriptor? ISelectorStructure.SemanticDescriptor => semanticDescriptor;

            internal TimeSpanSelector(Builder parentBuilder, ValueOrExpression<string> selectorExpression, bool addBase = true)
                : this(parentBuilder, selectorExpression, addBase, semanticDescriptor: null)
            {
            }

            /// <summary>
            /// </summary>
            /// <param name="selectorExpression">Base selector expression used only by the retained renderer.<br/></param>
            /// <param name="addBase">Whether to append the base selector part.<br/></param>
            /// <param name="semanticDescriptor">Optional immutable TimeSpan source descriptor.<br/></param>
            /// <param name="parentBuilder">The owning selector builder from which this direct or computed TimeSpan selector is constructed.<br/></param>
            internal TimeSpanSelector(
                Builder parentBuilder,
                ValueOrExpression<string> selectorExpression,
                bool addBase,
                IBackendNeutralSelectorDescriptor? semanticDescriptor)
            {
                this.parentBuilder = addBase || semanticDescriptor is not null
                    ? parentBuilder.AsComputed()
                    : parentBuilder;
                this.selectorExpression = selectorExpression;
                this.semanticDescriptor = semanticDescriptor;
                if (addBase) parentBuilder.MarkComputed();
            }

            /// <summary>
            /// Adds another duration selector and returns a TimeSpan selector carrying both semantic operands.<br/>
            /// </summary>
            public TimeSpanSelector Add(TimeSpanSelector selector)
                => ApplyTimeSpan(selector, TemporalArithmeticOperationKind.TimeSpanAddTimeSpan);

            /// <summary>
            /// Adds one literal duration.<br/>
            /// </summary>
            public TimeSpanSelector Add(TimeSpan value)
                => ApplyTimeSpan(value, TemporalArithmeticOperationKind.TimeSpanAddTimeSpan);

            /// <summary>
            /// Subtracts another duration selector.<br/>
            /// </summary>
            public TimeSpanSelector Subtract(TimeSpanSelector selector)
                => ApplyTimeSpan(selector, TemporalArithmeticOperationKind.TimeSpanSubtractTimeSpan);

            /// <summary>
            /// Subtracts one literal duration.<br/>
            /// </summary>
            public TimeSpanSelector Subtract(TimeSpan value)
                => ApplyTimeSpan(value, TemporalArithmeticOperationKind.TimeSpanSubtractTimeSpan);

            /// <summary>
            /// Multiplies this duration by a scalar selector.<br/>
            /// </summary>
            public TimeSpanSelector Multiply(RealSelector<double> selector)
                => ApplyScalar(selector, TemporalArithmeticOperationKind.TimeSpanMultiplyDouble);

            /// <summary>
            /// Multiplies this duration by a literal scalar.<br/>
            /// </summary>
            public TimeSpanSelector Multiply(double value)
                => ApplyScalar(value, TemporalArithmeticOperationKind.TimeSpanMultiplyDouble);

            /// <summary>
            /// Divides this duration by a scalar selector.<br/>
            /// </summary>
            public TimeSpanSelector Divide(RealSelector<double> selector)
                => ApplyScalar(selector, TemporalArithmeticOperationKind.TimeSpanDivideDouble);

            /// <summary>
            /// Divides this duration by a literal scalar.<br/>
            /// </summary>
            public TimeSpanSelector Divide(double value)
                => ApplyScalar(value, TemporalArithmeticOperationKind.TimeSpanDivideDouble);

            /// <summary>
            /// Divides this duration by another duration and returns their dimensionless ratio.<br/>
            /// </summary>
            public RealSelector<double> Divide(TimeSpanSelector selector)
                => ApplyRatio(selector);

            /// <summary>
            /// Adds the TimeSpan property selected by a typed lambda.<br/>
            /// </summary>
            public TimeSpanSelector PropAdd(System.Linq.Expressions.Expression<Func<T, TimeSpan>> property)
                => Add(new Selector<T>().Prop(property));

            /// <summary>
            /// Begins textual TimeSpan property addition.<br/>
            /// </summary>
            public TimeSpanPropertyArithmeticSelector PropAdd(string propertyPath)
                => new(this, propertyPath, TemporalArithmeticOperationKind.TimeSpanAddTimeSpan);

            /// <summary>
            /// Subtracts the TimeSpan property selected by a typed lambda.<br/>
            /// </summary>
            public TimeSpanSelector PropSubtract(System.Linq.Expressions.Expression<Func<T, TimeSpan>> property)
                => Subtract(new Selector<T>().Prop(property));

            /// <summary>
            /// Begins textual TimeSpan property subtraction.<br/>
            /// </summary>
            public TimeSpanPropertyArithmeticSelector PropSubtract(string propertyPath)
                => new(this, propertyPath, TemporalArithmeticOperationKind.TimeSpanSubtractTimeSpan);

            /// <summary>
            /// Multiplies by the Double property selected by a typed lambda.<br/>
            /// </summary>
            public TimeSpanSelector PropMultiply(System.Linq.Expressions.Expression<Func<T, double>> property)
                => Multiply(new Selector<T>().Prop(property));

            /// <summary>
            /// Begins textual scalar multiplication.<br/>
            /// </summary>
            public TimeSpanScalarPropertyArithmeticSelector PropMultiply(string propertyPath)
                => new(this, propertyPath, TemporalArithmeticOperationKind.TimeSpanMultiplyDouble);

            /// <summary>
            /// Divides by the Double property selected by a typed lambda.<br/>
            /// </summary>
            public TimeSpanSelector PropDivide(System.Linq.Expressions.Expression<Func<T, double>> property)
                => Divide(new Selector<T>().Prop(property));

            /// <summary>
            /// Divides by the TimeSpan property selected by a typed lambda and returns a ratio.<br/>
            /// </summary>
            public RealSelector<double> PropDivide(System.Linq.Expressions.Expression<Func<T, TimeSpan>> property)
                => Divide(new Selector<T>().Prop(property));

            /// <summary>
            /// Begins textual scalar or duration division.<br/>
            /// </summary>
            public TimeSpanDividePropertyArithmeticSelector PropDivide(string propertyPath)
                => new(this, propertyPath);

            internal TimeSpanSelector ApplyTimeSpan(TimeSpan value, TemporalArithmeticOperationKind operation)
            {
                var source = GetSemanticValueDescriptor()
                    ?? throw new NotSupportedException("TimeSpan arithmetic requires complete semantic source intent.");
                parentBuilder.MarkComputed();
                var descriptor = new TemporalArithmeticSelectorDescriptor<TimeSpan, TimeSpan, TimeSpan>(source, value, operation);
                return new TimeSpanSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            internal TimeSpanSelector ApplyTimeSpan(TimeSpanSelector selector, TemporalArithmeticOperationKind operation)
            {
                var source = GetSemanticValueDescriptor()
                    ?? throw new NotSupportedException("TimeSpan arithmetic requires complete semantic source intent.");
                var right = RequireSemanticDescriptor(selector);
                parentBuilder.MarkComputed();
                var descriptor = new TemporalArithmeticSelectorDescriptor<TimeSpan, TimeSpan, TimeSpan>(source, right, operation);
                return new TimeSpanSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            internal TimeSpanSelector ApplyScalar(double value, TemporalArithmeticOperationKind operation)
            {
                var source = GetSemanticValueDescriptor()
                    ?? throw new NotSupportedException("TimeSpan scalar arithmetic requires complete semantic source intent.");
                parentBuilder.MarkComputed();
                var descriptor = new TemporalArithmeticSelectorDescriptor<TimeSpan, double, TimeSpan>(source, value, operation);
                return new TimeSpanSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            internal TimeSpanSelector ApplyScalar(RealSelector<double> selector, TemporalArithmeticOperationKind operation)
            {
                var source = GetSemanticValueDescriptor()
                    ?? throw new NotSupportedException("TimeSpan scalar arithmetic requires complete semantic source intent.");
                var right = RequireSemanticDescriptor(selector);
                parentBuilder.MarkComputed();
                var descriptor = new TemporalArithmeticSelectorDescriptor<TimeSpan, double, TimeSpan>(source, right, operation);
                return new TimeSpanSelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            internal RealSelector<double> ApplyRatio(TimeSpanSelector selector)
            {
                var source = GetSemanticValueDescriptor()
                    ?? throw new NotSupportedException("TimeSpan ratio arithmetic requires complete semantic source intent.");
                var right = RequireSemanticDescriptor(selector);
                parentBuilder.MarkComputed();
                var descriptor = new TemporalArithmeticSelectorDescriptor<TimeSpan, TimeSpan, double>(
                    source,
                    right,
                    TemporalArithmeticOperationKind.TimeSpanDivideTimeSpan);
                return new RealSelector<double>(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            /// <summary>
            /// </summary>
            /// <returns>An immutable direct or derived source descriptor, or null after unsupported selector composition.<br/></returns>
            private IBackendNeutralSelectorDescriptor? GetSemanticValueDescriptor()
            {
                if (semanticDescriptor is not null)
                    return semanticDescriptor;

                var sourcePath = parentBuilder.TypedSourcePropertyPath;
                return string.IsNullOrWhiteSpace(sourcePath)
                    ? null
                    : new DirectPropertySelectorDescriptor(sourcePath);
            }

            /// <summary>
            /// Adds one retained real-valued TimeSpan projection and returns a selector carrying the matching neutral descriptor.<br/>
            /// </summary>
            /// <param name="projection">Backend-neutral projection kind.<br/></param>
            /// <returns>A double selector preserving native semantic intent.<br/></returns>
            private RealSelector<double> ApplyRealOp(TimeSpanProjectionKind projection)
            {
                var source = GetSemanticValueDescriptor();
                var descriptor = source is null
                    ? null
                    : new TimeSpanProjectionSelectorDescriptor(source, projection);
                parentBuilder.MarkComputed();
                return new RealSelector<double>(
                    parentBuilder,
                    selectorExpression,
                    addBase: false,
                    descriptor);
            }

            /// <summary>
            /// Selects the signed duration in 100-nanosecond ticks.<br/>
            /// </summary>
            /// <returns>A numeric selector for the requested temporal component.<br/></returns>
            public IntegralSelector<long> Ticks
            {
                get
                {
                    var source = GetSemanticValueDescriptor();
                    var descriptor = source is null
                        ? null
                        : new TimeSpanProjectionSelectorDescriptor(
                            source,
                            TimeSpanProjectionKind.Ticks);
                    return new IntegralSelector<long>(
                        parentBuilder,
                        selectorExpression,
                        addBase: false,
                        descriptor);
                }
            }

            /// <summary>
            /// Selects the complete duration expressed in fractional days.<br/>
            /// </summary>
            /// <returns>A numeric selector for the requested temporal component.<br/></returns>
            public RealSelector<double> TotalDays
                => ApplyRealOp(TimeSpanProjectionKind.TotalDays);
            /// <summary>
            /// Selects the complete duration expressed in fractional hours.<br/>
            /// </summary>
            /// <returns>A numeric selector for the requested temporal component.<br/></returns>
            public RealSelector<double> TotalHours
                => ApplyRealOp(TimeSpanProjectionKind.TotalHours);
            /// <summary>
            /// Selects the complete duration expressed in fractional minutes.<br/>
            /// </summary>
            /// <returns>A numeric selector for the requested temporal component.<br/></returns>
            public RealSelector<double> TotalMinutes
                => ApplyRealOp(TimeSpanProjectionKind.TotalMinutes);
            /// <summary>
            /// Selects the complete duration expressed in fractional seconds.<br/>
            /// </summary>
            /// <returns>A numeric selector for the requested temporal component.<br/></returns>
            public RealSelector<double> TotalSeconds
                => ApplyRealOp(TimeSpanProjectionKind.TotalSeconds);
            /// <summary>
            /// Selects the complete duration expressed in fractional milliseconds.<br/>
            /// </summary>
            /// <returns>A numeric selector for the requested temporal component.<br/></returns>
            public RealSelector<double> TotalMilliseconds
                => ApplyRealOp(TimeSpanProjectionKind.TotalMilliseconds);

        }

        /// <summary>
        /// Completes a textual duration operand for TimeSpan arithmetic.<br/>
        /// </summary>
        /// <remarks>
        /// Construction records expression intent; it does not execute a query or read record values.<br/>
        /// </remarks>
        public sealed class TimeSpanPropertyArithmeticSelector
        {
            private readonly TimeSpanSelector source;
            private readonly string propertyPath;
            private readonly TemporalArithmeticOperationKind operation;

            internal TimeSpanPropertyArithmeticSelector(
                TimeSpanSelector source,
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
            public TimeSpanSelector AsTimeSpan
                => source.ApplyTimeSpan(new Selector<T>().PropPath(propertyPath).AsTimeSpan, operation);
        }

        /// <summary>
        /// Completes a textual Double operand for duration scaling.<br/>
        /// </summary>
        /// <remarks>
        /// Construction records expression intent; it does not execute a query or read record values.<br/>
        /// </remarks>
        public sealed class TimeSpanScalarPropertyArithmeticSelector
        {
            private readonly TimeSpanSelector source;
            private readonly string propertyPath;
            private readonly TemporalArithmeticOperationKind operation;

            internal TimeSpanScalarPropertyArithmeticSelector(
                TimeSpanSelector source,
                string propertyPath,
                TemporalArithmeticOperationKind operation)
            {
                this.source = source;
                this.propertyPath = propertyPath;
                this.operation = operation;
            }

            /// <summary>
            /// Completes the textual right operand as Double.<br/>
            /// </summary>
            public TimeSpanSelector AsDouble
                => source.ApplyScalar(new Selector<T>().PropPath(propertyPath).AsDouble, operation);
        }

        /// <summary>
        /// Completes a textual duration divisor as Double for a duration result or TimeSpan for a dimensionless ratio.<br/>
        /// </summary>
        /// <remarks>
        /// Construction records expression intent; it does not execute a query or read record values.<br/>
        /// </remarks>
        public sealed class TimeSpanDividePropertyArithmeticSelector
        {
            private readonly TimeSpanSelector source;
            private readonly string propertyPath;

            internal TimeSpanDividePropertyArithmeticSelector(TimeSpanSelector source, string propertyPath)
            {
                this.source = source;
                this.propertyPath = propertyPath;
            }

            /// <summary>
            /// Completes the textual divisor as Double and returns a TimeSpan.<br/>
            /// </summary>
            public TimeSpanSelector AsDouble
                => source.ApplyScalar(
                    new Selector<T>().PropPath(propertyPath).AsDouble,
                    TemporalArithmeticOperationKind.TimeSpanDivideDouble);

            /// <summary>
            /// Completes the textual divisor as TimeSpan and returns a dimensionless Double selector.<br/>
            /// </summary>
            public RealSelector<double> AsTimeSpan
                => source.ApplyRatio(new Selector<T>().PropPath(propertyPath).AsTimeSpan);
        }

        /// <summary>
        /// Direct TimeSpan properties and deterministic binary TimeSpan slices share the same immutable projection semantics.<br/>
        /// </summary>
        internal sealed class TimeSpanProjectionSelectorDescriptor : IBackendNeutralSelectorDescriptor
        {
            internal IBackendNeutralSelectorDescriptor Source { get; }
            internal TimeSpanProjectionKind Projection { get; }
            private readonly string sourcePropertyPath;

            /// <summary>
            /// Creates one immutable TimeSpan projection node.<br/>
            /// </summary>
            /// <param name="source">Direct property or deterministic TimeSpan-producing descriptor.<br/></param>
            /// <param name="projection">Ticks or total-unit projection to apply.<br/></param>
            internal TimeSpanProjectionSelectorDescriptor(
                IBackendNeutralSelectorDescriptor source,
                TimeSpanProjectionKind projection)
            {
                Source = source ?? throw new ArgumentNullException(nameof(source));
                if (!Enum.IsDefined(projection))
                    throw new ArgumentOutOfRangeException(nameof(projection));
                Projection = projection;
                sourcePropertyPath = source switch
                {
                    DirectPropertySelectorDescriptor direct => direct.PropertyPath,
                    Condition<T>.BinarySliceSelectorDescriptor slice
                        when Condition<T>.TryExtractPropPath(
                            slice.SourceSelector,
                            out var propertyPath) => propertyPath,
                    TemporalArithmeticSelectorDescriptor temporal
                        when temporal.TryGetSourcePropertyPath(out var propertyPath) => propertyPath,
                    _ => string.Empty
                };
            }

            /// <summary>
            /// Resolves the normalized source property used for Inheto reads and developer conversion context.<br/>
            /// </summary>
            /// <param name="propertyPath">Normalized direct source path when available.<br/></param>
            /// <returns><see langword="true"/> when the projection ultimately reads one direct property.<br/></returns>
            internal bool TryGetSourcePropertyPath(out string propertyPath)
            {
                propertyPath = sourcePropertyPath;
                return propertyPath.Length != 0;
            }

            /// <summary>
            /// Reads and projects one TimeSpan source to the exact result type required by its consuming numeric condition.<br/>
            /// Missing, null, malformed, undersized, or type-incompatible sources return no value.<br/>
            /// </summary>
            /// <typeparam name="TValue"><see cref="long"/> for Ticks or <see cref="double"/> for Total* projections.<br/></typeparam>
            /// <param name="binary">Serialized Inheto record supplying the source.<br/></param>
            /// <param name="options">Active developer deserialization registrations.<br/></param>
            /// <param name="value">Projected typed value when successful.<br/></param>
            /// <returns><see langword="true"/> when the source and result type are valid.<br/></returns>
            internal bool TryRead<TValue>(
                Inheto.InhetoBinary binary,
                Inheto.DeserializationOptions options,
                out TValue value)
            {
                value = default!;
                if (!TryReadTimeSpan(binary, options, out var source))
                    return false;

                if (Projection == TimeSpanProjectionKind.Ticks)
                {
                    if (typeof(TValue) != typeof(long))
                        return false;
                    value = (TValue)(object)source.Ticks;
                    return true;
                }

                if (typeof(TValue) != typeof(double))
                    return false;

                var ticksPerUnit = Projection switch
                {
                    TimeSpanProjectionKind.TotalDays => TimeSpan.TicksPerDay,
                    TimeSpanProjectionKind.TotalHours => TimeSpan.TicksPerHour,
                    TimeSpanProjectionKind.TotalMinutes => TimeSpan.TicksPerMinute,
                    TimeSpanProjectionKind.TotalSeconds => TimeSpan.TicksPerSecond,
                    TimeSpanProjectionKind.TotalMilliseconds => TimeSpan.TicksPerMillisecond,
                    _ => 0L
                };
                if (ticksPerUnit == 0)
                    return false;
                value = (TValue)(object)(source.Ticks / (double)ticksPerUnit);
                return true;
            }

            /// <summary>
            /// Restores one direct TimeSpan or evaluates one deterministic binary TimeSpan slice.<br/>
            /// </summary>
            /// <param name="binary">Serialized record supplying the source value.<br/></param>
            /// <param name="options">Active developer deserialization registrations.<br/></param>
            /// <param name="value">Restored TimeSpan when successful.<br/></param>
            /// <returns><see langword="true"/> when a present valid source is available.<br/></returns>
            private bool TryReadTimeSpan(
                Inheto.InhetoBinary binary,
                Inheto.DeserializationOptions options,
                out TimeSpan value)
            {
                value = default;
                if (!TryGetSourcePropertyPath(out var propertyPath) ||
                    !Condition<T>.HasConditionValue(binary, propertyPath, options))
                {
                    return false;
                }

                if (Source is DirectPropertySelectorDescriptor)
                {
                    value = binary.ReadPropAs<TimeSpan>(propertyPath, options);
                    return true;
                }

                if (Source is Condition<T>.BinarySliceSelectorDescriptor slice)
                {
                    var source = binary.ReadPropAs<byte[]>(propertyPath, options);
                    return slice.TryRead(source, out value);
                }

                if (Source is TemporalArithmeticSelectorDescriptor temporal)
                    return temporal.TryRead(binary, options, out value);

                return false;
            }
        }
    }

    /// <summary>
    /// Identifies one backend-neutral TimeSpan scalar projection.<br/>
    /// </summary>
    internal enum TimeSpanProjectionKind
    {
        Ticks = 0,        TotalDays = 1,        TotalHours = 2,        TotalMinutes = 3,        TotalSeconds = 4,        TotalMilliseconds = 5
    }
}
