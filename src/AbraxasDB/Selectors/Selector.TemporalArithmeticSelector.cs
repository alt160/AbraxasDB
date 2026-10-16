using System.Numerics;

namespace AbraxasDB
{
    public partial class Selector<T>
    {
        /// <summary>
        /// Resolves the immutable semantic descriptor carried by a direct or computed selector.<br/>
        /// Direct typed properties are frozen into normalized property descriptors; incomplete selector shapes fail immediately rather than silently falling back to their leftmost property.<br/>
        /// </summary>
        /// <param name="selector">Direct or computed selector whose current semantic identity is required.<br/></param>
        /// <returns>The immutable backend-neutral descriptor.<br/></returns>
        private static IBackendNeutralSelectorDescriptor RequireSemanticDescriptor(ISelector selector)
        {
            ArgumentNullException.ThrowIfNull(selector);
            if (selector is not ISelectorStructure structure)
                throw new NotSupportedException("Selector arithmetic requires a backend-neutral selector structure.");
            if (structure.SemanticDescriptor is not null)
                return structure.SemanticDescriptor;
            if (!string.IsNullOrWhiteSpace(structure.TypedSourcePropertyPath))
                return new DirectPropertySelectorDescriptor(structure.TypedSourcePropertyPath);
            throw new NotSupportedException("Selector arithmetic requires complete direct or computed semantic intent.");
        }

        /// <summary>
        /// Immutable backend-neutral temporal arithmetic node inspected by durable capture and evaluated through one closed typed reader.<br/>
        /// </summary>
        internal abstract class TemporalArithmeticSelectorDescriptor : IBackendNeutralSelectorDescriptor
        {
            internal IBackendNeutralSelectorDescriptor Source { get; }
            internal IBackendNeutralSelectorDescriptor? RightSource { get; }
            internal TemporalArithmeticOperationKind Operation { get; }
            internal Type SourceType { get; }
            internal Type RightType { get; }
            internal Type ResultType { get; }
            internal abstract bool HasLiteralOperand { get; }
            internal abstract object? BoxedLiteralOperand { get; }

            /// <summary>
            /// Creates one validated binary temporal arithmetic node.<br/>
            /// </summary>
            /// <param name="source">Left semantic operand.<br/></param>
            /// <param name="rightSource">Right semantic operand.<br/></param>
            /// <param name="operation">Exact temporal operation.<br/></param>
            /// <param name="sourceType">Declared left CLR type.<br/></param>
            /// <param name="rightType">Declared right CLR type.<br/></param>
            /// <param name="resultType">Exact semantic result CLR type.<br/></param>
            /// <param name="hasLiteralOperand">Whether the right operand is a literal value rather than a selector; exactly one right-operand form must be supplied.<br/></param>
            protected TemporalArithmeticSelectorDescriptor(
                IBackendNeutralSelectorDescriptor source,
                IBackendNeutralSelectorDescriptor? rightSource,
                TemporalArithmeticOperationKind operation,
                Type sourceType,
                Type rightType,
                Type resultType,
                bool hasLiteralOperand)
            {
                Source = source ?? throw new ArgumentNullException(nameof(source));
                RightSource = rightSource;
                if (!Enum.IsDefined(operation))
                    throw new ArgumentOutOfRangeException(nameof(operation));
                Operation = operation;
                SourceType = sourceType ?? throw new ArgumentNullException(nameof(sourceType));
                RightType = rightType ?? throw new ArgumentNullException(nameof(rightType));
                ResultType = resultType ?? throw new ArgumentNullException(nameof(resultType));
                if ((rightSource is null) == !hasLiteralOperand)
                    throw new ArgumentException("Temporal arithmetic requires exactly one selector or literal right operand.");
                ValidateTemporalArithmeticContract(operation, sourceType, rightType, resultType);
            }

            /// <summary>
            /// Resolves the primary left property path used for diagnostics and residual planning.<br/>
            /// </summary>
            /// <param name="propertyPath">Leftmost normalized source property when available.<br/></param>
            /// <returns><see langword="true"/> when the recursive source reaches a direct property.<br/></returns>
            internal bool TryGetSourcePropertyPath(out string propertyPath)
                => TryGetTemporalSourcePropertyPath(Source, out propertyPath);

            /// <summary>
            /// Reads this arithmetic result using the exact type requested by its consuming condition, projection, aggregate, or calculated index.<br/>
            /// </summary>
            /// <typeparam name="TValue">Exact resolved consumption type.<br/></typeparam>
            /// <param name="binary">Authoritative Inheto record payload.<br/></param>
            /// <param name="options">Active developer deserialization registrations.<br/></param>
            /// <param name="value">Computed value when both operands are present and arithmetic succeeds.<br/></param>
            /// <returns><see langword="true"/> for a valid computed result; otherwise <see langword="false"/>.<br/></returns>
            internal bool TryRead<TValue>(
                Inheto.InhetoBinary binary,
                Inheto.DeserializationOptions options,
                out TValue value)
            {
                if (this is ITemporalArithmeticReader<TValue> typed)
                    return typed.TryRead(binary, options, out value);
                value = default!;
                return false;
            }
        }

        /// <summary>
        /// Closed typed temporal arithmetic node; its evaluator delegate is selected once during construction or durable replay and remains allocation-free for each record.<br/>
        /// </summary>
        internal sealed class TemporalArithmeticSelectorDescriptor<TLeft, TRight, TResult> :
            TemporalArithmeticSelectorDescriptor,
            ITemporalArithmeticReader<TResult>
        {
            private readonly Func<TLeft, TRight, TResult> evaluator;
            private readonly bool hasLiteralOperand;
            private readonly TRight literalOperand = default!;

            internal override bool HasLiteralOperand => hasLiteralOperand;
            internal override object? BoxedLiteralOperand => hasLiteralOperand ? literalOperand : null;

            /// <summary>
            /// Creates one closed arithmetic descriptor and resolves its exact operation delegate once.<br/>
            /// </summary>
            internal TemporalArithmeticSelectorDescriptor(
                IBackendNeutralSelectorDescriptor source,
                IBackendNeutralSelectorDescriptor rightSource,
                TemporalArithmeticOperationKind operation)
                : base(source, rightSource, operation, typeof(TLeft), typeof(TRight), typeof(TResult), hasLiteralOperand: false)
            {
                evaluator = CreateTemporalEvaluator<TLeft, TRight, TResult>(operation);
            }

            /// <summary>
            /// Creates one closed temporal arithmetic descriptor with an immutable literal right operand.<br/>
            /// </summary>
            internal TemporalArithmeticSelectorDescriptor(
                IBackendNeutralSelectorDescriptor source,
                TRight literalOperand,
                TemporalArithmeticOperationKind operation)
                : base(source, rightSource: null, operation, typeof(TLeft), typeof(TRight), typeof(TResult), hasLiteralOperand: true)
            {
                this.literalOperand = literalOperand;
                hasLiteralOperand = true;
                evaluator = CreateTemporalEvaluator<TLeft, TRight, TResult>(operation);
            }

            /// <summary>
            /// Reads both operands and executes the preselected typed operation.<br/>
            /// Missing/null values, overflow, invalid DateTime bounds, zero divisors, and non-finite scalar results return no value.<br/>
            /// </summary>
            public bool TryRead(
                Inheto.InhetoBinary binary,
                Inheto.DeserializationOptions options,
                out TResult value)
            {
                value = default!;
                if (!TryReadTemporalValue(Source, binary, options, out TLeft left))
                {
                    return false;
                }
                TRight right;
                if (RightSource is not null)
                {
                    if (!TryReadTemporalValue(RightSource, binary, options, out right))
                        return false;
                }
                else if (hasLiteralOperand)
                {
                    right = literalOperand;
                }
                else
                {
                    return false;
                }

                try
                {
                    value = evaluator(left, right);
                    return value is not double real || double.IsFinite(real);
                }
                catch (ArithmeticException)
                {
                    value = default!;
                    return false;
                }
                catch (ArgumentOutOfRangeException)
                {
                    value = default!;
                    return false;
                }
            }
        }

        /// <summary>
        /// Builds one exact typed temporal evaluator for the validated source/right/result contract.<br/>
        /// Reflection is not used during record evaluation; the object casts occur only while the closed delegate is created.<br/>
        /// </summary>
        private static Func<TLeft, TRight, TResult> CreateTemporalEvaluator<TLeft, TRight, TResult>(
            TemporalArithmeticOperationKind operation)
        {
            Delegate evaluator = operation switch
            {
                TemporalArithmeticOperationKind.DateTimeAddTimeSpan =>
                    (Func<DateTime, TimeSpan, DateTime>)(static (left, right) => left.Add(right)),
                TemporalArithmeticOperationKind.DateTimeSubtractTimeSpan =>
                    (Func<DateTime, TimeSpan, DateTime>)(static (left, right) => left.Subtract(right)),
                TemporalArithmeticOperationKind.DateTimeSubtractDateTime =>
                    (Func<DateTime, DateTime, TimeSpan>)(static (left, right) => left.Subtract(right)),
                TemporalArithmeticOperationKind.TimeOnlyAddTimeSpan =>
                    (Func<TimeOnly, TimeSpan, TimeOnly>)(static (left, right) => left.Add(right)),
                TemporalArithmeticOperationKind.TimeOnlySubtractTimeSpan =>
                    (Func<TimeOnly, TimeSpan, TimeOnly>)(static (left, right) => left.Add(-right)),
                TemporalArithmeticOperationKind.TimeOnlySubtractTimeOnly =>
                    (Func<TimeOnly, TimeOnly, TimeSpan>)(static (left, right) => left - right),
                TemporalArithmeticOperationKind.TimeSpanAddTimeSpan =>
                    (Func<TimeSpan, TimeSpan, TimeSpan>)(static (left, right) => left.Add(right)),
                TemporalArithmeticOperationKind.TimeSpanSubtractTimeSpan =>
                    (Func<TimeSpan, TimeSpan, TimeSpan>)(static (left, right) => left.Subtract(right)),
                TemporalArithmeticOperationKind.TimeSpanMultiplyDouble =>
                    (Func<TimeSpan, double, TimeSpan>)(static (left, right) => left * right),
                TemporalArithmeticOperationKind.TimeSpanDivideDouble =>
                    (Func<TimeSpan, double, TimeSpan>)(static (left, right) => right == 0d
                        ? throw new DivideByZeroException()
                        : left / right),
                TemporalArithmeticOperationKind.TimeSpanDivideTimeSpan =>
                    (Func<TimeSpan, TimeSpan, double>)(static (left, right) => right == TimeSpan.Zero
                        ? throw new DivideByZeroException()
                        : left / right),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
            return (Func<TLeft, TRight, TResult>)(object)evaluator;
        }

        /// <summary>
        /// Reads one direct, binary-sliced, numeric, or recursively temporal operand using its exact declared type.<br/>
        /// </summary>
        private static bool TryReadTemporalValue<TValue>(
            IBackendNeutralSelectorDescriptor descriptor,
            Inheto.InhetoBinary binary,
            Inheto.DeserializationOptions options,
            out TValue value)
        {
            value = default!;
            if (descriptor is DirectPropertySelectorDescriptor direct)
            {
                if (!Condition<T>.HasConditionValue(binary, direct.PropertyPath, options))
                    return false;
                value = binary.ReadPropAs<TValue>(direct.PropertyPath, options)!;
                return value is not null;
            }
            if (descriptor is ITemporalArithmeticReader<TValue> temporal)
                return temporal.TryRead(binary, options, out value);
            if (descriptor is NumericArithmeticSelectorDescriptor numeric)
                return numeric.TryRead(binary, options, out value);
            if (descriptor is TimeSpanProjectionSelectorDescriptor projection)
                return projection.TryRead(binary, options, out value);
            if (descriptor is BuiltInSelectorDescriptor builtIn)
                return builtIn.TryRead(binary, options, out value);
            if (descriptor is Condition<T>.BinarySliceSelectorDescriptor slice &&
                Condition<T>.TryExtractPropPath(slice.SourceSelector, out var propertyPath) &&
                Condition<T>.HasConditionValue(binary, propertyPath, options))
            {
                byte[] source = binary.ReadPropAs<byte[]>(propertyPath, options)!;
                return slice.TryRead(source, out value);
            }
            return false;
        }

        private static bool TryGetTemporalSourcePropertyPath(
            IBackendNeutralSelectorDescriptor descriptor,
            out string propertyPath)
        {
            if (descriptor is DirectPropertySelectorDescriptor direct)
            {
                propertyPath = direct.PropertyPath;
                return true;
            }
            if (descriptor is TemporalArithmeticSelectorDescriptor temporal)
                return temporal.TryGetSourcePropertyPath(out propertyPath);
            if (descriptor is NumericArithmeticSelectorDescriptor numeric)
                return numeric.TryGetSourcePropertyPath(out propertyPath);
            if (descriptor is TimeSpanProjectionSelectorDescriptor projection)
                return projection.TryGetSourcePropertyPath(out propertyPath);
            if (descriptor is BuiltInSelectorDescriptor builtIn)
                return builtIn.TryGetSourcePropertyPath(out propertyPath);
            if (descriptor is Condition<T>.BinarySliceSelectorDescriptor slice &&
                Condition<T>.TryExtractPropPath(slice.SourceSelector, out propertyPath))
            {
                return true;
            }
            propertyPath = string.Empty;
            return false;
        }

        internal static void ValidateTemporalArithmeticContract(
            TemporalArithmeticOperationKind operation,
            Type sourceType,
            Type rightType,
            Type resultType)
        {
            var valid = operation switch
            {
                TemporalArithmeticOperationKind.DateTimeAddTimeSpan or
                TemporalArithmeticOperationKind.DateTimeSubtractTimeSpan =>
                    sourceType == typeof(DateTime) && rightType == typeof(TimeSpan) && resultType == typeof(DateTime),
                TemporalArithmeticOperationKind.DateTimeSubtractDateTime =>
                    sourceType == typeof(DateTime) && rightType == typeof(DateTime) && resultType == typeof(TimeSpan),
                TemporalArithmeticOperationKind.TimeOnlyAddTimeSpan or
                TemporalArithmeticOperationKind.TimeOnlySubtractTimeSpan =>
                    sourceType == typeof(TimeOnly) && rightType == typeof(TimeSpan) && resultType == typeof(TimeOnly),
                TemporalArithmeticOperationKind.TimeOnlySubtractTimeOnly =>
                    sourceType == typeof(TimeOnly) && rightType == typeof(TimeOnly) && resultType == typeof(TimeSpan),
                TemporalArithmeticOperationKind.TimeSpanAddTimeSpan or
                TemporalArithmeticOperationKind.TimeSpanSubtractTimeSpan =>
                    sourceType == typeof(TimeSpan) && rightType == typeof(TimeSpan) && resultType == typeof(TimeSpan),
                TemporalArithmeticOperationKind.TimeSpanMultiplyDouble or
                TemporalArithmeticOperationKind.TimeSpanDivideDouble =>
                    sourceType == typeof(TimeSpan) && rightType == typeof(double) && resultType == typeof(TimeSpan),
                TemporalArithmeticOperationKind.TimeSpanDivideTimeSpan =>
                    sourceType == typeof(TimeSpan) && rightType == typeof(TimeSpan) && resultType == typeof(double),
                _ => false
            };
            if (!valid)
            {
                throw new ArgumentException(
                    $"Temporal operation '{operation}' does not support '{sourceType.FullName}', '{rightType.FullName}' -> '{resultType.FullName}'.");
            }
        }

        internal interface ITemporalArithmeticReader<TValue>
        {
            bool TryRead(
                Inheto.InhetoBinary binary,
                Inheto.DeserializationOptions options,
                out TValue value);
        }

        internal enum TemporalArithmeticOperationKind
        {
            DateTimeAddTimeSpan = 0,
            DateTimeSubtractTimeSpan = 1,
            DateTimeSubtractDateTime = 2,
            TimeOnlyAddTimeSpan = 3,
            TimeOnlySubtractTimeSpan = 4,
            TimeOnlySubtractTimeOnly = 5,
            TimeSpanAddTimeSpan = 6,
            TimeSpanSubtractTimeSpan = 7,
            TimeSpanMultiplyDouble = 8,
            TimeSpanDivideDouble = 9,
            TimeSpanDivideTimeSpan = 10
        }
    }
}
