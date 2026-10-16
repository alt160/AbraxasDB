using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace AbraxasDB
{
    /// <summary>
    /// Common marker contract for Abraxas property and calculated selectors.<br/>
    /// </summary>
    public interface ISelector
    {
        /// <summary>
        /// Returns the immutable selector instance for assembly-owned compatibility paths that previously required a defensive clone.<br/>
        /// </summary>
        internal ISelector Clone() => this;
    }

    /// <summary>
    /// Centralizes the exact scalar result families admitted by selectors, readers, conditions, grouping, aggregates, and Abraxas-owned index projections.<br/>
    /// Keeping this list outside LibraDex preserves LibraDex as a thin binary indexing engine while Abraxas owns developer-facing projection policy.<br/>
    /// </summary>
    internal static class SelectorScalarContract
    {
        /// <summary>
        /// Returns whether a CLR type has an established Abraxas/Inheto scalar conversion and selector execution path.<br/>
        /// </summary>
        /// <param name="type">Exact non-nullable result type to validate.<br/></param>
        /// <returns><see langword="true"/> for supported text, binary, Boolean, character, numeric, identifier, and temporal scalar types.<br/></returns>
        internal static bool IsSupported(Type type)
            => type == typeof(string) || type == typeof(byte[]) || type == typeof(bool) || type == typeof(char) ||
               type == typeof(sbyte) || type == typeof(byte) || type == typeof(short) || type == typeof(ushort) ||
               type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) ||
               type == typeof(Int128) || type == typeof(UInt128) || type == typeof(BigInteger) ||
               type == typeof(Half) || type == typeof(float) || type == typeof(double) || type == typeof(decimal) ||
               type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateOnly) ||
               type == typeof(TimeOnly) || type == typeof(TimeSpan);

        /// <summary>
        /// Returns whether a CLR type may supply values to a runtime-only external selector.<br/>
        /// Exact enum values are admitted as sources because Inheto preserves their declared CLR type and underlying representation, while durable selector/index contracts remain limited to the established scalar families.<br/>
        /// </summary>
        /// <param name="type">Exact non-nullable source type to validate.<br/></param>
        /// <returns><see langword="true"/> for an established scalar type or an exact enum type.<br/></returns>
        internal static bool IsSupportedExternalSource(Type type)
            => IsSupported(type) || type.IsEnum;
    }

    /// <summary>
    /// Identifies hexadecimal or Base64 text when decoding a string selector to binary data.<br/>
    /// </summary>
    public enum EncodingKind
    {
        /// <summary>Hexadecimal digits representing binary bytes.<br/></summary>
        Hex,
        /// <summary>Base64 text representing binary bytes.<br/></summary>
        Base64
    }

    /// <summary>
    /// Identifies standard hyphenated GUID text or its compact 32-hexadecimal-digit form.<br/>
    /// </summary>
    public enum GuidStringFormat
    {
        /// <summary>The standard D-format GUID text, including hyphens.<br/></summary>
        Hyphenated,
        /// <summary>The N-format GUID text: 32 hexadecimal digits without hyphens.<br/></summary>
        Numeric
    }

    /// <summary>
    /// Exposes the backend-neutral structural meaning retained by a selector builder.<br/>
    /// This internal contract preserves public selector source compatibility while record-store routing consumes structural path and result metadata directly.<br/>
    /// </summary>
    internal interface ISelectorStructure
    {
        /// <summary>
        /// Gets the normalized property path when the selector still represents exactly one direct property access, or null after any computed transformation.<br/>
        /// </summary>
        string? SimplePropertyPath { get; }

        /// <summary>
        /// Gets the normalized source property path when the selector is either a direct property access or only a typed view over that property.<br/>
        /// Arithmetic, functions, concatenation, slicing, literals, and grouping clear this value because their result is no longer the stored property itself.<br/>
        /// </summary>
        string? TypedSourcePropertyPath { get; }

        /// <summary>
        /// Gets the CLR value type exposed by the selector view, or null when the selector has no typed result contract.<br/>
        /// Backend-neutral consumers compare this result type with the retained source member type to reject incompatible type-only views.<br/>
        /// </summary>
        Type? ResultType { get; }

        /// <summary>
        /// Gets an immutable backend-neutral descriptor when the selector represents a supported computed value rather than a direct property or type-only view.<br/>
        /// </summary>
        IBackendNeutralSelectorDescriptor? SemanticDescriptor => null;
    }

    public partial class Selector<T>
    {
        /// <summary>
        /// Identifies a selector whose exact scalar result type is known at compile time.<br/>
        /// The contract intentionally exposes no general-purpose projection surface; it exists so readers and other Abraxas terminals can infer the result type without accepting arbitrary object shapes.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact supported scalar value produced by the selector.<br/></typeparam>
        public interface IValue<out TValue> : ISelector
        {
        }

        /// <summary>
        /// Represents a runtime-only function projection over one existing typed selector.<br/>
        /// The result remains constrained to established Abraxas scalar types and can be consumed by selector-aware readers, conditions, grouping, and non-durable terminals.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact scalar result produced by the application function.<br/></typeparam>
        public sealed class ExternalSelector<TValue> : IValue<TValue>, ISelectorStructure
        {
            private readonly RuntimeExternalSelectorDescriptor descriptor;

            string? ISelectorStructure.SimplePropertyPath => null;
            string? ISelectorStructure.TypedSourcePropertyPath => null;
            Type? ISelectorStructure.ResultType => typeof(TValue);
            IBackendNeutralSelectorDescriptor? ISelectorStructure.SemanticDescriptor => descriptor;

            /// <summary>
            /// Creates one immutable runtime selector around the frozen source descriptor and application function.<br/>
            /// </summary>
            internal ExternalSelector(RuntimeExternalSelectorDescriptor descriptor)
                => this.descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));

            /// <summary>
            /// Returns a diagnostic form that deliberately identifies the non-durable application boundary.<br/>
            /// </summary>
            public override string ToString() => $"External<{typeof(TValue).Name}>({descriptor.Source})";
        }

        /// <summary>
        /// Retains one strongly typed runtime projection and compiles its source reader once per consuming operation.<br/>
        /// </summary>
        internal sealed class RuntimeExternalSelectorDescriptor : IRuntimeSelectorDescriptor<T>
        {
            private readonly Type sourceType;
            private readonly Type resultType;
            private readonly Delegate operation;

            internal IBackendNeutralSelectorDescriptor Source { get; }

            /// <summary>
            /// Captures one runtime-only operation after exact scalar source and result validation.<br/>
            /// </summary>
            internal RuntimeExternalSelectorDescriptor(
                IBackendNeutralSelectorDescriptor source,
                Type sourceType,
                Type resultType,
                Delegate operation)
            {
                Source = source ?? throw new ArgumentNullException(nameof(source));
                this.sourceType = sourceType ?? throw new ArgumentNullException(nameof(sourceType));
                this.resultType = resultType ?? throw new ArgumentNullException(nameof(resultType));
                this.operation = operation ?? throw new ArgumentNullException(nameof(operation));
                if (!SelectorScalarContract.IsSupportedExternalSource(sourceType) || !SelectorScalarContract.IsSupported(resultType))
                    throw new NotSupportedException("Selector.External supports only established Abraxas scalar source and result types.");
            }

            /// <summary>
            /// Creates an exact evaluator by closing the captured source/result generic pair once outside the row loop.<br/>
            /// </summary>
            public bool TryBuild<TValueResult>(
                Inheto.DeserializationOptions options,
                out string propPath,
                out Func<Inheto.InhetoBinary, (bool HasValue, TValueResult Value)> reader)
            {
                if (resultType != typeof(TValueResult))
                {
                    propPath = string.Empty;
                    reader = default!;
                    return false;
                }

                var method = typeof(RuntimeExternalSelectorDescriptor)
                    .GetMethod(nameof(BuildTyped), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .MakeGenericMethod(sourceType, resultType);
                object?[] arguments = { options, null, null };
                bool success;
                try
                {
                    success = (bool)method.Invoke(this, arguments)!;
                }
                catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is not null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                    throw;
                }
                propPath = (string)arguments[1]!;
                reader = success
                    ? (Func<Inheto.InhetoBinary, (bool HasValue, TValueResult Value)>)arguments[2]!
                    : default!;
                return success;
            }

            /// <summary>
            /// Builds the strongly typed hot-path closure selected once by <see cref="TryBuild{TValueResult}"/>.<br/>
            /// Reflection is confined to construction; each payload visit performs only typed delegate calls.<br/>
            /// </summary>
            private bool BuildTyped<TSource, TResult>(
                Inheto.DeserializationOptions options,
                out string propPath,
                out Func<Inheto.InhetoBinary, (bool HasValue, TResult Value)> reader)
            {
                var wrapped = new ValueOrExpression<string>(static () => string.Empty)
                    .WithSelectorDescriptor(Source);
                if (!Condition<T>.TryBuildConditionValueReader<TSource>(wrapped, options, out propPath, out var sourceReader))
                {
                    reader = default!;
                    return false;
                }

                var typedOperation = (Func<TSource, TResult>)operation;
                reader = binary =>
                {
                    (bool hasValue, TSource value) = sourceReader(binary);
                    if (!hasValue)
                        return (false, default!);
                    TResult projected = typedOperation(value);
                    return projected is null ? (false, default!) : (true, projected);
                };
                return true;
            }
        }

        /// <summary>
        /// Create a new selector with an empty builder.
        /// </summary>
        public Selector()
        {
        }

        /// <summary>
        /// Starts a selector from one exact case-sensitive semantic index name rather than an object property path.<br/>
        /// Store-bound consumers use the matching LibraDex key stream when present and may use the registered projection reader when payload fallback is required.<br/>
        /// </summary>
        /// <param name="semanticName">Function or expression index name registered with the record store.<br/></param>
        /// <returns>An untyped selector that can be completed through the ordinary <c>As...</c> family.<br/></returns>
        public ValueTypeSelector Index(string semanticName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(semanticName);
            if (Encoding.UTF8.GetByteCount(semanticName) > 256)
                throw new ArgumentOutOfRangeException(nameof(semanticName), "Semantic index names cannot exceed 256 UTF-8 bytes.");
            var expression = ((ValueOrExpression<string>)(() => string.Empty))
                .WithSelectorDescriptor(new NamedIndexSelectorDescriptor(semanticName));
            return new ValueTypeSelector(new Builder(), expression, addBase: false);
        }

        /// <summary>
        /// Selects a decimal member from its expression-derived property path without reading a record or invoking the member getter.<br/>
        /// </summary>
        #region Prop Expressions (factory methods)
        public DecimalSelector Prop(Expression<Func<T, decimal>> propSelector)
        {
            var path = propSelector.GetPropertyPath();
            return new DecimalSelector(Builder.ForProperty(path), path, addBase: false);
        }
        /// <summary>
        /// Selects a ReadOnlyMemory&lt;byte&gt; property using its expression-derived path.<br/>
        /// </summary>
        /// <param name="propSelector">Member expression rooted at the record entity type; it identifies the stored property rather than executing application code.<br/></param>
        /// <returns>A typed selector for the identified property.<br/></returns>
        /// <remarks>
        /// No record is read or hydrated by this call.<br/>
        /// </remarks>
        public BinarySelector Prop(Expression<Func<T, ReadOnlyMemory<byte>>> propSelector)
        {
            var path = propSelector.GetPropertyPath();
            return new BinarySelector(Builder.ForProperty(path), path, addBase: false);
        }
        /// <summary>
        /// Selects a Memory&lt;byte&gt; property using its expression-derived path.<br/>
        /// </summary>
        /// <param name="propSelector">Member expression rooted at the record entity type; it identifies the stored property rather than executing application code.<br/></param>
        /// <returns>A typed selector for the identified property.<br/></returns>
        /// <remarks>
        /// No record is read or hydrated by this call.<br/>
        /// </remarks>
        public BinarySelector Prop(Expression<Func<T, Memory<byte>>> propSelector)
        {
            var path = propSelector.GetPropertyPath();
            return new BinarySelector(Builder.ForProperty(path), path, addBase: false);
        }
        /// <summary>
        /// Selects a System.IO.MemoryStream property using its expression-derived path.<br/>
        /// </summary>
        /// <param name="propSelector">Member expression rooted at the record entity type; it identifies the stored property rather than executing application code.<br/></param>
        /// <returns>A typed selector for the identified property.<br/></returns>
        /// <remarks>
        /// No record is read or hydrated by this call.<br/>
        /// </remarks>
        public BinarySelector Prop(Expression<Func<T, System.IO.MemoryStream>> propSelector)
        {
            var path = propSelector.GetPropertyPath();
            return new BinarySelector(Builder.ForProperty(path), path, addBase: false);
        }
        /// <summary>
        /// Selects a byte[] property using its expression-derived path.<br/>
        /// </summary>
        /// <param name="propSelector">Member expression rooted at the record entity type; it identifies the stored property rather than executing application code.<br/></param>
        /// <returns>A typed selector for the identified property.<br/></returns>
        /// <remarks>
        /// No record is read or hydrated by this call.<br/>
        /// </remarks>
        public BinarySelector Prop(Expression<Func<T, byte[]>> propSelector)
        {
            var path = propSelector.GetPropertyPath();
            return new BinarySelector(Builder.ForProperty(path), path, addBase: false);
        }
        /// <summary>
        /// Selects a bool property using its expression-derived path.<br/>
        /// </summary>
        /// <param name="propSelector">Member expression rooted at the record entity type; it identifies the stored property rather than executing application code.<br/></param>
        /// <returns>A typed selector for the identified property.<br/></returns>
        /// <remarks>
        /// No record is read or hydrated by this call.<br/>
        /// </remarks>
        public BooleanSelector Prop(Expression<Func<T, bool>> propSelector)
        {
            var path = propSelector.GetPropertyPath();
            return new BooleanSelector(Builder.ForProperty(path), path, addBase: false);
        }
        /// <summary>
        /// Selects a TimeOnly property using its expression-derived path.<br/>
        /// </summary>
        /// <param name="propSelector">Member expression rooted at the record entity type; it identifies the stored property rather than executing application code.<br/></param>
        /// <returns>A typed selector for the identified property.<br/></returns>
        /// <remarks>
        /// No record is read or hydrated by this call.<br/>
        /// </remarks>
        public TimeOnlySelector Prop(Expression<Func<T, TimeOnly>> propSelector)
        {
            var path = propSelector.GetPropertyPath();
            return new TimeOnlySelector(Builder.ForProperty(path), path, addBase: false);
        }
        /// <summary>
        /// Selects a DateOnly property using its expression-derived path.<br/>
        /// </summary>
        /// <param name="propSelector">Member expression rooted at the record entity type; it identifies the stored property rather than executing application code.<br/></param>
        /// <returns>A typed selector for the identified property.<br/></returns>
        /// <remarks>
        /// No record is read or hydrated by this call.<br/>
        /// </remarks>
        public DateOnlySelector Prop(Expression<Func<T, DateOnly>> propSelector)
        {
            var path = propSelector.GetPropertyPath();
            return new DateOnlySelector(Builder.ForProperty(path), path, addBase: false);
        }
        /// <summary>
        /// Selects a DateTime property using its expression-derived path.<br/>
        /// </summary>
        /// <param name="propSelector">Member expression rooted at the record entity type; it identifies the stored property rather than executing application code.<br/></param>
        /// <returns>A typed selector for the identified property.<br/></returns>
        /// <remarks>
        /// No record is read or hydrated by this call.<br/>
        /// </remarks>
        public DateTimeSelector Prop(Expression<Func<T, DateTime>> propSelector)
        {
            var path = propSelector.GetPropertyPath();
            return new DateTimeSelector(Builder.ForProperty(path), path, addBase: false);
        }
        /// <summary>
        /// Selects a TimeSpan property using its expression-derived path.<br/>
        /// </summary>
        /// <param name="propSelector">Member expression rooted at the record entity type; it identifies the stored property rather than executing application code.<br/></param>
        /// <returns>A typed selector for the identified property.<br/></returns>
        /// <remarks>
        /// No record is read or hydrated by this call.<br/>
        /// </remarks>
        public TimeSpanSelector Prop(Expression<Func<T, TimeSpan>> propSelector)
        {
            var path = propSelector.GetPropertyPath();
            return new TimeSpanSelector(Builder.ForProperty(path), path, addBase: false);
        }
        /// <summary>
        /// Selects a Guid property using its expression-derived path.<br/>
        /// </summary>
        /// <param name="propSelector">Member expression rooted at the record entity type; it identifies the stored property rather than executing application code.<br/></param>
        /// <returns>A typed selector for the identified property.<br/></returns>
        /// <remarks>
        /// No record is read or hydrated by this call.<br/>
        /// </remarks>
        public GuidSelector Prop(Expression<Func<T, Guid>> propSelector)
        {
            var path = propSelector.GetPropertyPath();
            return new GuidSelector(Builder.ForProperty(path), path, addBase: false);
        }
        /// <summary>
        /// Selects a string property using its expression-derived path.<br/>
        /// </summary>
        /// <param name="propSelector">Member expression rooted at the record entity type; it identifies the stored property rather than executing application code.<br/></param>
        /// <returns>A typed selector for the identified property.<br/></returns>
        /// <remarks>
        /// No record is read or hydrated by this call.<br/>
        /// </remarks>
        public StringSelector Prop(Expression<Func<T, string>> propSelector)
        {
            var path = propSelector.GetPropertyPath();
            return new StringSelector(Builder.ForProperty(path), (ValueOrExpression<string>)path, addBase: false);
        }
        /// <summary>
        /// Selects a Num property using its expression-derived path.<br/>
        /// </summary>
        /// <typeparam name="Num">Exact numeric property type.<br/></typeparam>
        /// <param name="propSelector">Member expression rooted at the record entity type; it identifies the stored property rather than executing application code.<br/></param>
        /// <returns>A typed selector for the identified property.<br/></returns>
        /// <remarks>
        /// No record is read or hydrated by this call.<br/>
        /// </remarks>
        public NumericSelector<Num> Prop<Num>(Expression<Func<T, Num>> propSelector) where Num : INumber<Num>
        {
            var path = propSelector.GetPropertyPath();
            // Choose integral vs real selector from implemented interfaces without trying to close
            // IBinaryInteger<TSelf> over a non-integral TSelf, which violates that interface's constraint.
            var t = typeof(Num);
            var binaryInteger = t.GetInterfaces().Any(static contract =>
                contract.IsGenericType &&
                contract.GetGenericTypeDefinition() == typeof(System.Numerics.IBinaryInteger<>));
            var selectorExpression = (ValueOrExpression<string>)path;
            var sourceBuilder = Builder.ForProperty(path);
            object selObj;
            if (binaryInteger)
            {
                selObj = Activator.CreateInstance(
                    typeof(IntegralSelector<>).MakeGenericType(typeof(T), t),
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                    binder: null,
                    args: new object[] { sourceBuilder, selectorExpression, false },
                    culture: null)!;
            }
            else
            {
                selObj = Activator.CreateInstance(
                    typeof(RealSelector<>).MakeGenericType(typeof(T), t),
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                    binder: null,
                    args: new object[] { sourceBuilder, selectorExpression, false },
                    culture: null)!;
            }
            return (NumericSelector<Num>)selObj;
        }

        /// <summary>
        /// Selects an <see cref="sbyte"/> property as an integral expression so arithmetic operations remain discoverable without a path-string cast.<br/>
        /// </summary>
        public IntegralSelector<sbyte> Prop(Expression<Func<T, sbyte>> propSelector)
            => CreateIntegralPropertySelector(propSelector);

        /// <summary>
        /// Selects a <see cref="byte"/> property as an integral expression so arithmetic operations remain discoverable without a path-string cast.<br/>
        /// </summary>
        public IntegralSelector<byte> Prop(Expression<Func<T, byte>> propSelector)
            => CreateIntegralPropertySelector(propSelector);

        /// <summary>
        /// Selects an <see cref="short"/> property as an integral expression so arithmetic operations remain discoverable without a path-string cast.<br/>
        /// </summary>
        public IntegralSelector<short> Prop(Expression<Func<T, short>> propSelector)
            => CreateIntegralPropertySelector(propSelector);

        /// <summary>
        /// Selects a <see cref="ushort"/> property as an integral expression so arithmetic operations remain discoverable without a path-string cast.<br/>
        /// </summary>
        public IntegralSelector<ushort> Prop(Expression<Func<T, ushort>> propSelector)
            => CreateIntegralPropertySelector(propSelector);

        /// <summary>
        /// Selects an <see cref="int"/> property as an integral expression so arithmetic operations remain discoverable without a path-string cast.<br/>
        /// </summary>
        public IntegralSelector<int> Prop(Expression<Func<T, int>> propSelector)
            => CreateIntegralPropertySelector(propSelector);

        /// <summary>
        /// Selects a <see cref="uint"/> property as an integral expression so arithmetic operations remain discoverable without a path-string cast.<br/>
        /// </summary>
        public IntegralSelector<uint> Prop(Expression<Func<T, uint>> propSelector)
            => CreateIntegralPropertySelector(propSelector);

        /// <summary>
        /// Selects a <see cref="long"/> property as an integral expression so arithmetic operations remain discoverable without a path-string cast.<br/>
        /// </summary>
        public IntegralSelector<long> Prop(Expression<Func<T, long>> propSelector)
            => CreateIntegralPropertySelector(propSelector);

        /// <summary>
        /// Selects a <see cref="ulong"/> property as an integral expression so arithmetic operations remain discoverable without a path-string cast.<br/>
        /// </summary>
        public IntegralSelector<ulong> Prop(Expression<Func<T, ulong>> propSelector)
            => CreateIntegralPropertySelector(propSelector);

        /// <summary>
        /// Selects an <see cref="Int128"/> property as an integral expression so arithmetic operations remain discoverable without a path-string cast.<br/>
        /// </summary>
        public IntegralSelector<Int128> Prop(Expression<Func<T, Int128>> propSelector)
            => CreateIntegralPropertySelector(propSelector);

        /// <summary>
        /// Selects a <see cref="UInt128"/> property as an integral expression so arithmetic operations remain discoverable without a path-string cast.<br/>
        /// </summary>
        public IntegralSelector<UInt128> Prop(Expression<Func<T, UInt128>> propSelector)
            => CreateIntegralPropertySelector(propSelector);

        /// <summary>
        /// Selects a <see cref="BigInteger"/> property as an integral expression so arithmetic operations remain discoverable without a path-string cast.<br/>
        /// </summary>
        public IntegralSelector<BigInteger> Prop(Expression<Func<T, BigInteger>> propSelector)
            => CreateIntegralPropertySelector(propSelector);

        /// <summary>
        /// Selects a <see cref="Half"/> property as a real expression so arithmetic operations remain discoverable without a path-string cast.<br/>
        /// </summary>
        public RealSelector<Half> Prop(Expression<Func<T, Half>> propSelector)
            => CreateRealPropertySelector(propSelector);

        /// <summary>
        /// Selects a <see cref="float"/> property as a real expression so arithmetic operations remain discoverable without a path-string cast.<br/>
        /// </summary>
        public RealSelector<float> Prop(Expression<Func<T, float>> propSelector)
            => CreateRealPropertySelector(propSelector);

        /// <summary>
        /// Selects a <see cref="double"/> property as a real expression so arithmetic operations remain discoverable without a path-string cast.<br/>
        /// </summary>
        public RealSelector<double> Prop(Expression<Func<T, double>> propSelector)
            => CreateRealPropertySelector(propSelector);

        /// <summary>
        /// Creates one exact integral property selector without reflection and freezes its normalized backend-neutral property identity.<br/>
        /// This shared construction path keeps typed-lambda overloads behaviorally identical to the generic numeric fallback while preserving their richer compile-time return type.<br/>
        /// </summary>
        /// <typeparam name="TInt">Exact binary-integer property type.<br/></typeparam>
        /// <param name="propSelector">Direct property expression rooted at the record type.<br/></param>
        /// <returns>An integral selector with arithmetic methods visible to the developer.<br/></returns>
        private IntegralSelector<TInt> CreateIntegralPropertySelector<TInt>(
            Expression<Func<T, TInt>> propSelector)
            where TInt : INumber<TInt>, IBinaryInteger<TInt>
        {
            ArgumentNullException.ThrowIfNull(propSelector);
            var path = propSelector.GetPropertyPath();
            return new IntegralSelector<TInt>(
                Builder.ForProperty(path),
                (ValueOrExpression<string>)path,
                addBase: false);
        }

        /// <summary>
        /// Creates one exact built-in real property selector without reflection and freezes its normalized backend-neutral property identity.<br/>
        /// This shared construction path preserves the existing typed property semantics while exposing arithmetic methods directly through IntelliSense.<br/>
        /// </summary>
        /// <typeparam name="TReal">Exact real property type.<br/></typeparam>
        /// <param name="propSelector">Direct property expression rooted at the record type.<br/></param>
        /// <returns>A real selector with arithmetic methods visible to the developer.<br/></returns>
        private RealSelector<TReal> CreateRealPropertySelector<TReal>(
            Expression<Func<T, TReal>> propSelector)
            where TReal : INumber<TReal>
        {
            ArgumentNullException.ThrowIfNull(propSelector);
            var path = propSelector.GetPropertyPath();
            return new RealSelector<TReal>(
                Builder.ForProperty(path),
                (ValueOrExpression<string>)path,
                addBase: false);
        }
        #endregion

        /// <summary>
        /// Creates a selector from a property, field, or admitted parameterless-method path and freezes Inheto's canonical spelling immediately.<br/>
        /// Invalid members fail at selector construction; method parentheses and the leading dot remain optional developer syntax.<br/>
        /// </summary>
        /// <param name="propPath">Path rooted at <typeparamref name="T"/>, such as <c>.CompanyName.Length</c>.<br/></param>
        /// <returns>An untyped value selector whose subsequent <c>As*</c> member supplies the condition result contract.<br/></returns>
        public ValueTypeSelector PropPath(string propPath)
        {
            Inheto.PropPathCheck check = Inheto.InhetoBinary.CheckPropPath(typeof(T), propPath);
            if (!check.IsValid)
            {
                throw new ArgumentException(
                    $"Property path '{propPath}' is not resolvable from '{typeof(T).FullName}': {check.FailureKind}" +
                    (string.IsNullOrEmpty(check.FailedSegment) ? "." : $" at segment '{check.FailedSegment}'."),
                    nameof(propPath));
            }

            return new ValueTypeSelector(
                Builder.ForProperty(check.CanonicalPath),
                (ValueOrExpression<string>)check.CanonicalPath,
                addBase: false);
        }

        /// <summary>
        /// Shortcut for the ID field.
        /// </summary>
    public IntegralSelector<ulong> ID => new IntegralSelector<ulong>(new Builder(), (ValueOrExpression<string>)"COL:recordID", addBase: false);

    /// <summary>
    /// Gets the integral selector for the record's state metadata.<br/>
    /// </summary>
    /// <returns>A selector over the record state rather than an object property.<br/></returns>
    public IntegralSelector<long> State => new IntegralSelector<long>(new Builder(), (ValueOrExpression<string>)"COL:state", addBase: false);
    }

    /// <summary>
    /// Builds one boxed backend-neutral value reader from a public selector's immutable structural contract.<br/>
    /// The generic condition readers remain the single semantic implementation; boxing occurs only because aggregate and grouping result contracts are intentionally heterogeneous.<br/>
    /// </summary>
    internal static class SelectorSemanticReader<TRecord>
    {
        /// <summary>
        /// Unsupported selector shapes return <see langword="false"/> before record iteration begins.<br/>
        /// </summary>
        /// <param name="selector">Public selector whose structural result type and descriptor define the value computation.<br/></param>
        /// <param name="options">Active Inheto deserialization registrations used by direct and derived reads.<br/></param>
        /// <param name="reader">Allocation-stable per-record reader returning presence plus the boxed heterogeneous result.<br/></param>
        /// <returns><see langword="true"/> when the selector has a supported result type and backend-neutral descriptor.<br/></returns>
        internal static bool TryCreate(
            ISelector selector,
            Inheto.DeserializationOptions options,
            out Func<Inheto.InhetoBinary, (bool HasValue, object? Value)> reader)
        {
            ArgumentNullException.ThrowIfNull(selector);
            ArgumentNullException.ThrowIfNull(options);
            reader = default!;

            if (selector is not ISelectorStructure structure || structure.ResultType is not Type resultType)
                return false;

            if (resultType == typeof(string)) return TryCreateBoxed<string>(selector, options, out reader);
            if (resultType == typeof(byte[])) return TryCreateBoxed<byte[]>(selector, options, out reader);
            if (resultType == typeof(bool)) return TryCreateBoxed<bool>(selector, options, out reader);
            if (resultType == typeof(char)) return TryCreateBoxed<char>(selector, options, out reader);
            if (resultType == typeof(sbyte)) return TryCreateBoxed<sbyte>(selector, options, out reader);
            if (resultType == typeof(byte)) return TryCreateBoxed<byte>(selector, options, out reader);
            if (resultType == typeof(short)) return TryCreateBoxed<short>(selector, options, out reader);
            if (resultType == typeof(ushort)) return TryCreateBoxed<ushort>(selector, options, out reader);
            if (resultType == typeof(int)) return TryCreateBoxed<int>(selector, options, out reader);
            if (resultType == typeof(uint)) return TryCreateBoxed<uint>(selector, options, out reader);
            if (resultType == typeof(long)) return TryCreateBoxed<long>(selector, options, out reader);
            if (resultType == typeof(ulong)) return TryCreateBoxed<ulong>(selector, options, out reader);
            if (resultType == typeof(Int128)) return TryCreateBoxed<Int128>(selector, options, out reader);
            if (resultType == typeof(UInt128)) return TryCreateBoxed<UInt128>(selector, options, out reader);
            if (resultType == typeof(System.Numerics.BigInteger)) return TryCreateBoxed<System.Numerics.BigInteger>(selector, options, out reader);
            if (resultType == typeof(Half)) return TryCreateBoxed<Half>(selector, options, out reader);
            if (resultType == typeof(float)) return TryCreateBoxed<float>(selector, options, out reader);
            if (resultType == typeof(double)) return TryCreateBoxed<double>(selector, options, out reader);
            if (resultType == typeof(decimal)) return TryCreateBoxed<decimal>(selector, options, out reader);
            if (resultType == typeof(Guid)) return TryCreateBoxed<Guid>(selector, options, out reader);
            if (resultType == typeof(DateTime)) return TryCreateBoxed<DateTime>(selector, options, out reader);
            if (resultType == typeof(DateOnly)) return TryCreateBoxed<DateOnly>(selector, options, out reader);
            if (resultType == typeof(TimeOnly)) return TryCreateBoxed<TimeOnly>(selector, options, out reader);
            if (resultType == typeof(TimeSpan)) return TryCreateBoxed<TimeSpan>(selector, options, out reader);

            return false;
        }

        /// <summary>
        /// Creates one exact typed selector reader for aggregate facades and typed grouping stages.<br/>
        /// Result-type equality is validated before the established condition reader is built, preventing implicit narrowing, widening, or reinterpretation at the aggregate boundary.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact CLR result type exposed by the selector.<br/></typeparam>
        /// <param name="selector">Direct or computed selector with complete backend-neutral semantics.<br/></param>
        /// <param name="options">Active Inheto deserialization registrations.<br/></param>
        /// <param name="reader">Reusable reflection-free typed reader when construction succeeds.<br/></param>
        /// <returns><see langword="true"/> only when the selector result type and semantic descriptor are complete and exact.<br/></returns>
        internal static bool TryCreate<TValue>(
            ISelector selector,
            Inheto.DeserializationOptions options,
            out Func<Inheto.InhetoBinary, (bool HasValue, TValue Value)> reader)
        {
            ArgumentNullException.ThrowIfNull(selector);
            ArgumentNullException.ThrowIfNull(options);
            reader = default!;
            if (selector is not ISelectorStructure structure || structure.ResultType != typeof(TValue))
                return false;

            IBackendNeutralSelectorDescriptor? descriptor = structure.SemanticDescriptor;
            if (descriptor is null && !string.IsNullOrWhiteSpace(structure.TypedSourcePropertyPath))
                descriptor = new DirectPropertySelectorDescriptor(structure.TypedSourcePropertyPath);
            if (descriptor is null)
                return false;

            var wrapped = new ValueOrExpression<string>(static () => string.Empty)
                .WithSelectorDescriptor(descriptor);
            return Condition<TRecord>.TryBuildConditionValueReader<TValue>(
                wrapped,
                options,
                out _,
                out reader);
        }

        /// <summary>
        /// Adapts the established typed condition reader to heterogeneous aggregate and grouping contracts.<br/>
        /// The typed delegate is built once; the hot record path performs no reflection or selector rendering.<br/>
        /// </summary>
        private static bool TryCreateBoxed<TValue>(
            ISelector selector,
            Inheto.DeserializationOptions options,
            out Func<Inheto.InhetoBinary, (bool HasValue, object? Value)> reader)
        {
            reader = default!;
            if (!TryCreate<TValue>(selector, options, out var typedReader))
            {
                return false;
            }

            reader = binary =>
            {
                var result = typedReader(binary);
                return result.HasValue
                    ? (true, result.Value)
                    : (false, null);
            };
            return true;
        }
    }

    /// <summary>
    /// Identifies deterministic built-in selector transformations that do not belong to the specialized arithmetic, decimal, TimeSpan, or string-operation grammars.<br/>
    /// Numeric values are private wire details; persisted selector identity uses the stable enum names.<br/>
    /// </summary>
    internal enum BuiltInSelectorOperationKind
    {
        BinaryGetByte,        BinaryToHex,        BinaryToBase64,        DateTimeYear,        DateTimeMonth,        DateTimeDay,        DateTimeHour,        DateTimeMinute,        DateTimeSecond,        DateTimeMillisecond,        DateTimeNanoSlice,        DateTimeDayOfWeek,        DateOnlyYear,        DateOnlyMonth,        DateOnlyDay,        DateOnlyDayOfWeek,        TimeOnlyHour,        TimeOnlyMinute,        TimeOnlySecond,        TimeOnlyMillisecond,        TimeOnlyNanoSlice,        StringConcatLiteral,        GuidAsBinary,        GuidAsStringHyphenated,        GuidAsStringNumeric,        GuidSegment,        GuidSlice,        GuidStartsWith,        GuidEndsWith,        GuidContains,        GuidMatchesPattern,        GuidEqual,        GuidNotEqual,        NumericToHex,        NumericToBase64,        NumericToDecimal,        DecimalToInt64,        StringPrependLiteral,        ScalarToInvariantString,        EnumToString
    }

    public partial class Selector<T>
    {
        /// <summary>
        /// The exact source/result CLR types and immutable operands are validated once, while record evaluation recursively consumes typed Inheto values without reflection.<br/>
        /// </summary>
        internal sealed class BuiltInSelectorDescriptor : IBackendNeutralSelectorDescriptor
        {
            internal IBackendNeutralSelectorDescriptor Source { get; }
            internal BuiltInSelectorOperationKind Operation { get; }
            internal Type SourceType { get; }
            internal Type ResultType { get; }
            internal long FirstIntegerOperand { get; }
            internal long SecondIntegerOperand { get; }
            internal string? TextOperand { get; }
            private readonly Func<Inheto.InhetoBinary, Inheto.DeserializationOptions, (bool HasValue, string? Value)>? enumTextReader;

            /// <summary>
            /// Creates one immutable built-in selector node after validating the operation's exact type and operand contract.<br/>
            /// Invalid internal combinations fail immediately so no selector can advertise partial backend-neutral semantics.<br/>
            /// </summary>
            /// <param name="source">Direct or computed source descriptor.<br/></param>
            /// <param name="operation">Built-in transformation to execute.<br/></param>
            /// <param name="sourceType">Exact CLR source type.<br/></param>
            /// <param name="resultType">Exact CLR result type.<br/></param>
            /// <param name="firstIntegerOperand">Optional first integral argument.<br/></param>
            /// <param name="secondIntegerOperand">Optional second integral argument.<br/></param>
            /// <param name="textOperand">Optional frozen text or Guid argument.<br/></param>
            internal BuiltInSelectorDescriptor(
                IBackendNeutralSelectorDescriptor source,
                BuiltInSelectorOperationKind operation,
                Type sourceType,
                Type resultType,
                long firstIntegerOperand = 0,
                long secondIntegerOperand = 0,
                string? textOperand = null)
            {
                Source = source ?? throw new ArgumentNullException(nameof(source));
                Operation = operation;
                SourceType = sourceType ?? throw new ArgumentNullException(nameof(sourceType));
                ResultType = resultType ?? throw new ArgumentNullException(nameof(resultType));
                FirstIntegerOperand = firstIntegerOperand;
                SecondIntegerOperand = secondIntegerOperand;
                TextOperand = textOperand;
                ValidateContract();
                if (Operation == BuiltInSelectorOperationKind.EnumToString)
                    enumTextReader = CreateEnumTextReader(SourceType);
            }

            /// <summary>
            /// Returns the single authoritative source property used to evaluate this recursive transformation tree.<br/>
            /// </summary>
            /// <param name="propertyPath">Normalized source property path when available.<br/></param>
            /// <returns><see langword="true"/> when every nested node resolves to one direct source property.<br/></returns>
            internal bool TryGetSourcePropertyPath(out string propertyPath)
            {
                if (Source is DirectPropertySelectorDescriptor direct)
                {
                    propertyPath = direct.PropertyPath;
                    return true;
                }
                if (Source is BuiltInSelectorDescriptor builtIn)
                    return builtIn.TryGetSourcePropertyPath(out propertyPath);
                if (Source is StringOperationSelectorDescriptor text)
                    return text.TryGetSourcePropertyPath(out propertyPath);
                if (Source is DecimalOperationSelectorDescriptor decimalOperation)
                    return decimalOperation.TryGetSourcePropertyPath(out propertyPath);
                if (Source is TimeSpanProjectionSelectorDescriptor timeSpan)
                    return timeSpan.TryGetSourcePropertyPath(out propertyPath);
                if (Source is NumericArithmeticSelectorDescriptor numeric)
                    return numeric.TryGetSourcePropertyPath(out propertyPath);
                if (Source is TemporalArithmeticSelectorDescriptor temporal)
                    return temporal.TryGetSourcePropertyPath(out propertyPath);
                if (Source is Condition<T>.BinarySliceSelectorDescriptor slice &&
                    slice.SourceSelector.SelectorDescriptor is DirectPropertySelectorDescriptor sliceDirect)
                {
                    propertyPath = sliceDirect.PropertyPath;
                    return true;
                }

                propertyPath = string.Empty;
                return false;
            }

            /// <summary>
            /// Evaluates this built-in operation against one authoritative Inheto payload.<br/>
            /// Missing/null sources, invalid bounds, malformed Guid operands, and incompatible requested result types return no value rather than throwing from record traversal.<br/>
            /// </summary>
            /// <typeparam name="TValue">Exact CLR value expected by the consumer.<br/></typeparam>
            /// <param name="binary">Authoritative serialized record.<br/></param>
            /// <param name="options">Inheto deserialization options shared by the owning store.<br/></param>
            /// <param name="value">Computed value when successful.<br/></param>
            /// <returns><see langword="true"/> when source restoration and transformation both succeed.<br/></returns>
            internal bool TryRead<TValue>(
                Inheto.InhetoBinary binary,
                Inheto.DeserializationOptions options,
                out TValue value)
            {
                value = default!;
                if (typeof(TValue) != ResultType)
                    return false;

                object? candidate;
                if (Operation == BuiltInSelectorOperationKind.EnumToString)
                {
                    (bool hasValue, string? enumText) = enumTextReader!(binary, options);
                    if (!hasValue)
                        return false;
                    candidate = enumText;
                }
                else if (SourceType == typeof(byte[]) && TryReadSource(binary, options, out byte[] bytes))
                    candidate = EvaluateBinary(bytes);
                else if (SourceType == typeof(DateTime) && TryReadSource(binary, options, out DateTime dateTime))
                    candidate = EvaluateDateTime(dateTime);
                else if (SourceType == typeof(DateOnly) && TryReadSource(binary, options, out DateOnly dateOnly))
                    candidate = EvaluateDateOnly(dateOnly);
                else if (SourceType == typeof(TimeOnly) && TryReadSource(binary, options, out TimeOnly timeOnly))
                    candidate = EvaluateTimeOnly(timeOnly);
                else if (SourceType == typeof(string) && TryReadSource(binary, options, out string text))
                    candidate = EvaluateString(text);
                else if (SourceType == typeof(bool) && TryReadSource(binary, options, out bool boolean))
                    candidate = EvaluateInvariantString(boolean);
                else if (SourceType == typeof(char) && TryReadSource(binary, options, out char character))
                    candidate = EvaluateInvariantString(character);
                else if (SourceType == typeof(Guid) && TryReadSource(binary, options, out Guid guid))
                    candidate = EvaluateGuid(guid);
                else if (SourceType == typeof(TimeSpan) && TryReadSource(binary, options, out TimeSpan timeSpan))
                    candidate = EvaluateInvariantString(timeSpan);
                else if (SourceType == typeof(sbyte) && TryReadSource(binary, options, out sbyte signedByte))
                    candidate = EvaluateNumeric(signedByte);
                else if (SourceType == typeof(byte) && TryReadSource(binary, options, out byte unsignedByte))
                    candidate = EvaluateNumeric(unsignedByte);
                else if (SourceType == typeof(short) && TryReadSource(binary, options, out short signedInt16))
                    candidate = EvaluateNumeric(signedInt16);
                else if (SourceType == typeof(ushort) && TryReadSource(binary, options, out ushort unsignedInt16))
                    candidate = EvaluateNumeric(unsignedInt16);
                else if (SourceType == typeof(int) && TryReadSource(binary, options, out int signedInt32))
                    candidate = EvaluateNumeric(signedInt32);
                else if (SourceType == typeof(uint) && TryReadSource(binary, options, out uint unsignedInt32))
                    candidate = EvaluateNumeric(unsignedInt32);
                else if (SourceType == typeof(long) && TryReadSource(binary, options, out long signedInt64))
                    candidate = EvaluateNumeric(signedInt64);
                else if (SourceType == typeof(ulong) && TryReadSource(binary, options, out ulong unsignedInt64))
                    candidate = EvaluateNumeric(unsignedInt64);
                else if (SourceType == typeof(nint) && TryReadSource(binary, options, out nint signedNativeInt))
                    candidate = EvaluateNumeric(signedNativeInt);
                else if (SourceType == typeof(nuint) && TryReadSource(binary, options, out nuint unsignedNativeInt))
                    candidate = EvaluateNumeric(unsignedNativeInt);
                else if (SourceType == typeof(Int128) && TryReadSource(binary, options, out Int128 signedInt128))
                    candidate = EvaluateNumeric(signedInt128);
                else if (SourceType == typeof(UInt128) && TryReadSource(binary, options, out UInt128 unsignedInt128))
                    candidate = EvaluateNumeric(unsignedInt128);
                else if (SourceType == typeof(BigInteger) && TryReadSource(binary, options, out BigInteger bigInteger))
                    candidate = EvaluateBigInteger(bigInteger);
                else if (SourceType == typeof(Half) && TryReadSource(binary, options, out Half half))
                    candidate = EvaluateNumeric(half);
                else if (SourceType == typeof(float) && TryReadSource(binary, options, out float single))
                    candidate = EvaluateNumeric(single);
                else if (SourceType == typeof(double) && TryReadSource(binary, options, out double doubleValue))
                    candidate = EvaluateNumeric(doubleValue);
                else if (SourceType == typeof(decimal) && TryReadSource(binary, options, out decimal decimalValue))
                    candidate = EvaluateNumeric(decimalValue);
                else
                    return false;

                if (candidate is not TValue typed)
                    return false;
                value = typed;
                return true;
            }

            private bool TryReadSource<TSource>(
                Inheto.InhetoBinary binary,
                Inheto.DeserializationOptions options,
                out TSource value)
            {
                value = default!;
                if (Source is DirectPropertySelectorDescriptor direct)
                {
                    if (!Condition<T>.HasConditionValue(binary, direct.PropertyPath, options))
                        return false;
                    value = binary.ReadPropAs<TSource>(direct.PropertyPath, options)!;
                    return value is not null;
                }
                if (Source is BuiltInSelectorDescriptor builtIn)
                    return builtIn.TryRead(binary, options, out value);
                if (Source is StringOperationSelectorDescriptor text)
                    return text.TryRead(binary, options, out value);
                if (Source is DecimalOperationSelectorDescriptor decimalOperation)
                    return decimalOperation.TryRead(binary, options, out value);
                if (Source is TimeSpanProjectionSelectorDescriptor timeSpan)
                    return timeSpan.TryRead(binary, options, out value);
                if (Source is NumericArithmeticSelectorDescriptor numeric)
                    return numeric.TryRead(binary, options, out value);
                if (Source is TemporalArithmeticSelectorDescriptor temporal)
                    return temporal.TryRead(binary, options, out value);
                if (Source is Condition<T>.BinarySliceSelectorDescriptor slice &&
                    slice.TryRead(binary, options, out value))
                    return true;
                return false;
            }

            /// <summary>
            /// Creates one closed generic enum reader when the immutable descriptor is authored or replayed.<br/>
            /// Reflection is paid once per descriptor construction; row evaluation subsequently uses the cached typed delegate and enum-keyed string cache.<br/>
            /// </summary>
            /// <param name="enumType">Exact enum CLR type retained in durable selector identity.<br/></param>
            /// <returns>A typed Inheto-to-canonical-text reader for the descriptor source.<br/></returns>
            private Func<Inheto.InhetoBinary, Inheto.DeserializationOptions, (bool HasValue, string? Value)> CreateEnumTextReader(Type enumType)
            {
                System.Reflection.MethodInfo factory = GetType()
                    .GetMethod(nameof(CreateEnumTextReaderCore), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .MakeGenericMethod(enumType);
                return (Func<Inheto.InhetoBinary, Inheto.DeserializationOptions, (bool HasValue, string? Value)>)factory.Invoke(this, null)!;
            }

            /// <summary>
            /// Closes the durable enum-text reader over one exact enum type without boxing values on the steady-state lookup path.<br/>
            /// </summary>
            /// <typeparam name="TEnum">Exact enum type declared by the selector source.<br/></typeparam>
            /// <returns>A reusable typed reader that returns no value when the source path is missing or null.<br/></returns>
            private Func<Inheto.InhetoBinary, Inheto.DeserializationOptions, (bool HasValue, string? Value)> CreateEnumTextReaderCore<TEnum>()
                where TEnum : struct, Enum
                => (binary, options) => TryReadSource(binary, options, out TEnum value)
                    ? (true, EnumDisplayTextCache<TEnum>.Get(value))
                    : (false, null);

            private object? EvaluateBinary(byte[] source)
            {
                return Operation switch
                {
                    BuiltInSelectorOperationKind.BinaryGetByte when
                        FirstIntegerOperand >= 0 && FirstIntegerOperand < source.LongLength => source[(int)FirstIntegerOperand],
                    BuiltInSelectorOperationKind.BinaryToHex => Convert.ToHexString(source),
                    BuiltInSelectorOperationKind.BinaryToBase64 => Convert.ToBase64String(source),
                    _ => null
                };
            }

            private object? EvaluateDateTime(DateTime source)
                => Operation switch
                {
                    BuiltInSelectorOperationKind.DateTimeYear => (long)source.Year,
                    BuiltInSelectorOperationKind.DateTimeMonth => (long)source.Month,
                    BuiltInSelectorOperationKind.DateTimeDay => (long)source.Day,
                    BuiltInSelectorOperationKind.DateTimeHour => (long)source.Hour,
                    BuiltInSelectorOperationKind.DateTimeMinute => (long)source.Minute,
                    BuiltInSelectorOperationKind.DateTimeSecond => (long)source.Second,
                    BuiltInSelectorOperationKind.DateTimeMillisecond => (long)source.Millisecond,
                    BuiltInSelectorOperationKind.DateTimeNanoSlice => (source.Ticks % TimeSpan.TicksPerMillisecond) / 10L,
                    BuiltInSelectorOperationKind.DateTimeDayOfWeek => (long)source.DayOfWeek,
                    BuiltInSelectorOperationKind.ScalarToInvariantString => EvaluateInvariantString(source),
                    _ => null
                };

            private object? EvaluateDateOnly(DateOnly source)
                => Operation switch
                {
                    BuiltInSelectorOperationKind.DateOnlyYear => (long)source.Year,
                    BuiltInSelectorOperationKind.DateOnlyMonth => (long)source.Month,
                    BuiltInSelectorOperationKind.DateOnlyDay => (long)source.Day,
                    BuiltInSelectorOperationKind.DateOnlyDayOfWeek => (long)source.DayOfWeek,
                    BuiltInSelectorOperationKind.ScalarToInvariantString => EvaluateInvariantString(source),
                    _ => null
                };

            private object? EvaluateTimeOnly(TimeOnly source)
                => Operation switch
                {
                    BuiltInSelectorOperationKind.TimeOnlyHour => (long)source.Hour,
                    BuiltInSelectorOperationKind.TimeOnlyMinute => (long)source.Minute,
                    BuiltInSelectorOperationKind.TimeOnlySecond => (long)source.Second,
                    BuiltInSelectorOperationKind.TimeOnlyMillisecond => (long)source.Millisecond,
                    BuiltInSelectorOperationKind.TimeOnlyNanoSlice => (source.Ticks % TimeSpan.TicksPerMillisecond) / 10L,
                    BuiltInSelectorOperationKind.ScalarToInvariantString => EvaluateInvariantString(source),
                    _ => null
                };

            private object? EvaluateString(string source)
                => Operation switch
                {
                    BuiltInSelectorOperationKind.StringConcatLiteral => string.Concat(source, TextOperand),
                    BuiltInSelectorOperationKind.StringPrependLiteral => string.Concat(TextOperand, source),
                    BuiltInSelectorOperationKind.ScalarToInvariantString => source,
                    _ => null
                };

            private object? EvaluateGuid(Guid source)
            {
                switch (Operation)
                {
                    case BuiltInSelectorOperationKind.GuidAsBinary:
                        return source.ToByteArray();
                    case BuiltInSelectorOperationKind.GuidAsStringHyphenated:
                        return source.ToString("D", System.Globalization.CultureInfo.InvariantCulture);
                    case BuiltInSelectorOperationKind.GuidAsStringNumeric:
                        return source.ToString("N", System.Globalization.CultureInfo.InvariantCulture);
                    case BuiltInSelectorOperationKind.ScalarToInvariantString:
                        return EvaluateInvariantString(source);
                    case BuiltInSelectorOperationKind.GuidEqual:
                        return Guid.TryParse(TextOperand, out Guid equal) && source == equal ? 1L : 0L;
                    case BuiltInSelectorOperationKind.GuidNotEqual:
                        return Guid.TryParse(TextOperand, out Guid notEqual) && source != notEqual ? 1L : 0L;
                }

                Span<char> numeric = stackalloc char[32];
                if (!source.TryFormat(numeric, out int charactersWritten, "N") || charactersWritten != numeric.Length)
                    return null;
                return Operation switch
                {
                    BuiltInSelectorOperationKind.GuidSegment => ReadGuidSegment(numeric, checked((int)FirstIntegerOperand)),
                    BuiltInSelectorOperationKind.GuidSlice => ReadGuidSlice(
                        numeric,
                        checked((int)FirstIntegerOperand),
                        checked((int)SecondIntegerOperand)),
                    BuiltInSelectorOperationKind.GuidStartsWith => MatchesGuidText(numeric, GuidComparisonKind.StartsWith),
                    BuiltInSelectorOperationKind.GuidEndsWith => MatchesGuidText(numeric, GuidComparisonKind.EndsWith),
                    BuiltInSelectorOperationKind.GuidContains => MatchesGuidText(numeric, GuidComparisonKind.Contains),
                    BuiltInSelectorOperationKind.GuidMatchesPattern => MatchesGuidText(numeric, GuidComparisonKind.MatchesPattern),
                    _ => null
                };
            }

            /// <summary>
            /// Encodes one fixed-width numeric value through its natural byte representation and returns the requested textual encoding.<br/>
            /// The default preserves the former little-endian contract; the descriptor reverses the written bytes when the developer requests big endian.<br/>
            /// </summary>
            /// <typeparam name="TNumber">Exact fixed-width numeric source type validated by this descriptor.<br/></typeparam>
            /// <param name="source">Numeric value restored from the direct or recursively computed source.<br/></param>
            /// <returns>The converted decimal/Int64 value, uppercase hexadecimal text, or Base64 text according to <see cref="Operation"/>.<br/></returns>
            private object? EvaluateNumeric<TNumber>(TNumber source)
                where TNumber : unmanaged, INumber<TNumber>
            {
                if (Operation == BuiltInSelectorOperationKind.ScalarToInvariantString)
                    return EvaluateInvariantString(source);

                try
                {
                    if (Operation == BuiltInSelectorOperationKind.NumericToDecimal)
                        return decimal.CreateChecked(source);
                    if (Operation == BuiltInSelectorOperationKind.DecimalToInt64)
                        return decimal.ToInt64(Unsafe.As<TNumber, decimal>(ref source));
                }
                catch (OverflowException)
                {
                    return null;
                }

                Span<byte> bytes = stackalloc byte[16];
                int length;
                if (typeof(TNumber) == typeof(sbyte) || typeof(TNumber) == typeof(byte))
                {
                    bytes[0] = Unsafe.As<TNumber, byte>(ref source);
                    length = 1;
                }
                else if (typeof(TNumber) == typeof(Half))
                {
                    short bits = BitConverter.HalfToInt16Bits(Unsafe.As<TNumber, Half>(ref source));
                    BitConverter.TryWriteBytes(bytes, bits);
                    length = sizeof(short);
                }
                else if (typeof(TNumber) == typeof(decimal))
                {
                    decimal value = Unsafe.As<TNumber, decimal>(ref source);
                    decimal.GetBits(value, MemoryMarshal.Cast<byte, int>(bytes));
                    length = 16;
                }
                else
                {
                    length = Unsafe.SizeOf<TNumber>();
                    MemoryMarshal.Write(bytes, in source);
                }

                return EncodeNumericBytes(bytes[..length]);
            }

            /// <summary>
            /// Encodes one variable-width two's-complement <see cref="BigInteger"/> without retaining an intermediate heap array.<br/>
            /// Small values use stack storage; unusually large values rent a temporary buffer and return it before the result escapes.<br/>
            /// </summary>
            /// <param name="source">Arbitrary-precision signed integer to encode.<br/></param>
            /// <returns>Uppercase hexadecimal or Base64 text according to <see cref="Operation"/>.<br/></returns>
            private object? EvaluateBigInteger(BigInteger source)
            {
                if (Operation == BuiltInSelectorOperationKind.ScalarToInvariantString)
                    return EvaluateInvariantString(source);

                if (Operation == BuiltInSelectorOperationKind.NumericToDecimal)
                {
                    try
                    {
                        return decimal.CreateChecked(source);
                    }
                    catch (OverflowException)
                    {
                        return null;
                    }
                }

                int length = source.GetByteCount(isUnsigned: false);
                if (length <= 256)
                {
                    Span<byte> bytes = stackalloc byte[length];
                    if (!source.TryWriteBytes(bytes, out int written, isUnsigned: false, isBigEndian: false))
                        throw new InvalidOperationException("A validated BigInteger did not fit its reported byte count.");
                    return EncodeNumericBytes(bytes[..written]);
                }

                byte[] rented = System.Buffers.ArrayPool<byte>.Shared.Rent(length);
                try
                {
                    if (!source.TryWriteBytes(rented, out int written, isUnsigned: false, isBigEndian: false))
                        throw new InvalidOperationException("A validated BigInteger did not fit its reported byte count.");
                    return EncodeNumericBytes(rented.AsSpan(0, written));
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(rented);
                }
            }

            /// <summary>
            /// Applies the descriptor's endian choice and converts one numeric byte span to its requested textual representation.<br/>
            /// </summary>
            /// <param name="bytes">Natural little-endian numeric bytes owned by the current stack or temporary buffer.<br/></param>
            /// <returns>Uppercase hexadecimal or Base64 text.<br/></returns>
            private string EncodeNumericBytes(Span<byte> bytes)
            {
                if (FirstIntegerOperand != 0)
                    bytes.Reverse();
                return Operation == BuiltInSelectorOperationKind.NumericToHex
                    ? Convert.ToHexString(bytes)
                    : Convert.ToBase64String(bytes);
            }

            /// <summary>
            /// Converts one admitted scalar through a deterministic culture-independent display contract.<br/>
            /// Temporal and Guid values use round-trip-oriented defaults; an authored format overrides that default for <see cref="IFormattable"/> values.<br/>
            /// </summary>
            /// <typeparam name="TValue">Exact scalar source type validated by the descriptor.<br/></typeparam>
            /// <param name="source">Restored scalar value to format.<br/></param>
            /// <returns>Invariant text suitable for durable calculated-selector evaluation.<br/></returns>
            private string EvaluateInvariantString<TValue>(TValue source)
            {
                string? format = TextOperand;
                if (format is null)
                {
                    if (typeof(TValue) == typeof(DateTime) || typeof(TValue) == typeof(DateOnly) || typeof(TValue) == typeof(TimeOnly))
                        format = "O";
                    else if (typeof(TValue) == typeof(TimeSpan))
                        format = "c";
                    else if (typeof(TValue) == typeof(Guid))
                        format = "D";
                }

                return source is IFormattable formattable
                    ? formattable.ToString(format, CultureInfo.InvariantCulture) ?? string.Empty
                    : Convert.ToString(source, CultureInfo.InvariantCulture) ?? string.Empty;
            }

            private static byte[] ReadGuidSegment(ReadOnlySpan<char> numeric, int segment)
            {
                (int Start, int Length) bounds = segment switch
                {
                    1 => (0, 8),
                    2 => (8, 4),
                    3 => (12, 4),
                    4 => (16, 4),
                    5 => (20, 12),
                    _ => throw new InvalidOperationException("A validated Guid segment was outside the supported range.")
                };
                return Convert.FromHexString(numeric.Slice(bounds.Start, bounds.Length));
            }

            private static byte[] ReadGuidSlice(ReadOnlySpan<char> numeric, int startNibble, int nibbleCount)
            {
                int outputLength = (nibbleCount + 1) / 2;
                var output = new byte[outputLength];
                for (int index = 0; index < nibbleCount; index++)
                {
                    int nibble = Uri.FromHex(numeric[startNibble + index]);
                    if ((index & 1) == 0)
                        output[index / 2] = (byte)(nibble << 4);
                    else
                        output[index / 2] |= (byte)nibble;
                }
                return output;
            }

            private long MatchesGuidText(ReadOnlySpan<char> numeric, GuidComparisonKind mode)
            {
                if (string.IsNullOrEmpty(TextOperand))
                    return 0L;
                Span<char> cleaned = stackalloc char[32];
                int count = 0;
                foreach (char character in TextOperand)
                {
                    if ((Uri.IsHexDigit(character) || character is 'x' or 'X') && count < cleaned.Length)
                        cleaned[count++] = char.ToLowerInvariant(character);
                }
                int start = mode switch
                {
                    GuidComparisonKind.StartsWith => 0,
                    GuidComparisonKind.EndsWith => numeric.Length - count,
                    GuidComparisonKind.Contains => (numeric.Length - count) / 2,
                    GuidComparisonKind.MatchesPattern when count == numeric.Length => 0,
                    _ => -1
                };
                if (start < 0)
                    return 0L;
                for (int index = 0; index < count; index++)
                {
                    char expected = cleaned[index];
                    if (expected != 'x' && char.ToLowerInvariant(numeric[start + index]) != expected)
                        return 0L;
                }
                return 1L;
            }

            private void ValidateContract()
            {
                bool binary = Operation is BuiltInSelectorOperationKind.BinaryGetByte or
                    BuiltInSelectorOperationKind.BinaryToHex or
                    BuiltInSelectorOperationKind.BinaryToBase64;
                bool dateTime = Operation is >= BuiltInSelectorOperationKind.DateTimeYear and <= BuiltInSelectorOperationKind.DateTimeDayOfWeek;
                bool dateOnly = Operation is >= BuiltInSelectorOperationKind.DateOnlyYear and <= BuiltInSelectorOperationKind.DateOnlyDayOfWeek;
                bool timeOnly = Operation is >= BuiltInSelectorOperationKind.TimeOnlyHour and <= BuiltInSelectorOperationKind.TimeOnlyNanoSlice;
                bool text = Operation is BuiltInSelectorOperationKind.StringConcatLiteral or BuiltInSelectorOperationKind.StringPrependLiteral;
                bool guid = Operation is >= BuiltInSelectorOperationKind.GuidAsBinary and <= BuiltInSelectorOperationKind.GuidNotEqual;
                bool numericEncoding = Operation is BuiltInSelectorOperationKind.NumericToHex or BuiltInSelectorOperationKind.NumericToBase64;
                bool numericToDecimal = Operation == BuiltInSelectorOperationKind.NumericToDecimal;
                bool decimalToInt64 = Operation == BuiltInSelectorOperationKind.DecimalToInt64;
                bool invariantText = Operation == BuiltInSelectorOperationKind.ScalarToInvariantString;
                bool enumText = Operation == BuiltInSelectorOperationKind.EnumToString;
                Type expectedSource = binary ? typeof(byte[]) : dateTime ? typeof(DateTime) : dateOnly ? typeof(DateOnly) : timeOnly ? typeof(TimeOnly) : text ? typeof(string) : guid ? typeof(Guid) : numericEncoding || numericToDecimal || invariantText || enumText ? SourceType : decimalToInt64 ? typeof(decimal) : typeof(void);
                Type expectedResult = Operation switch
                {
                    BuiltInSelectorOperationKind.BinaryGetByte => typeof(byte),
                    BuiltInSelectorOperationKind.BinaryToHex or BuiltInSelectorOperationKind.BinaryToBase64 => typeof(string),
                    >= BuiltInSelectorOperationKind.DateTimeYear and <= BuiltInSelectorOperationKind.TimeOnlyNanoSlice => typeof(long),
                    BuiltInSelectorOperationKind.StringConcatLiteral or BuiltInSelectorOperationKind.StringPrependLiteral => typeof(string),
                    BuiltInSelectorOperationKind.GuidAsBinary or BuiltInSelectorOperationKind.GuidSegment or BuiltInSelectorOperationKind.GuidSlice => typeof(byte[]),
                    BuiltInSelectorOperationKind.GuidAsStringHyphenated or BuiltInSelectorOperationKind.GuidAsStringNumeric => typeof(string),
                    >= BuiltInSelectorOperationKind.GuidStartsWith and <= BuiltInSelectorOperationKind.GuidNotEqual => typeof(long),
                    BuiltInSelectorOperationKind.NumericToHex or BuiltInSelectorOperationKind.NumericToBase64 => typeof(string),
                    BuiltInSelectorOperationKind.NumericToDecimal => typeof(decimal),
                    BuiltInSelectorOperationKind.DecimalToInt64 => typeof(long),
                    BuiltInSelectorOperationKind.ScalarToInvariantString => typeof(string),
                    BuiltInSelectorOperationKind.EnumToString => typeof(string),
                    _ => typeof(void)
                };
                if (SourceType != expectedSource || ResultType != expectedResult ||
                    ((numericEncoding || numericToDecimal) && !IsSupportedNumericEncodingType(SourceType)) ||
                    (invariantText && !IsSupportedInvariantStringType(SourceType)) ||
                    (enumText && !SourceType.IsEnum))
                    throw new ArgumentException($"Built-in selector operation '{Operation}' has an invalid source/result CLR contract.");
                if (numericEncoding && FirstIntegerOperand is not (0L or 1L))
                    throw new ArgumentOutOfRangeException(nameof(FirstIntegerOperand));
                if (Operation == BuiltInSelectorOperationKind.BinaryGetByte && FirstIntegerOperand < 0)
                    throw new ArgumentOutOfRangeException(nameof(FirstIntegerOperand));
                if (Operation == BuiltInSelectorOperationKind.GuidSegment && FirstIntegerOperand is < 1 or > 5)
                    throw new ArgumentOutOfRangeException(nameof(FirstIntegerOperand));
                if (Operation == BuiltInSelectorOperationKind.GuidSlice &&
                    (FirstIntegerOperand < 0 || FirstIntegerOperand > 31 || SecondIntegerOperand <= 0 || FirstIntegerOperand + SecondIntegerOperand > 32))
                {
                    throw new ArgumentOutOfRangeException(nameof(SecondIntegerOperand));
                }
                bool literalText = Operation is BuiltInSelectorOperationKind.StringConcatLiteral or BuiltInSelectorOperationKind.StringPrependLiteral;
                bool guidText = Operation is >= BuiltInSelectorOperationKind.GuidStartsWith and <= BuiltInSelectorOperationKind.GuidNotEqual;
                if (literalText && TextOperand is null)
                    throw new ArgumentException("A string concatenation operation requires an immutable literal operand.", nameof(TextOperand));
                if (guidText && string.IsNullOrEmpty(TextOperand))
                    throw new ArgumentException("The Guid selector operation requires a non-empty immutable operand.", nameof(TextOperand));
                if (!literalText && !guidText && !invariantText && TextOperand is not null)
                    throw new ArgumentException("This built-in selector operation does not accept a text operand.", nameof(TextOperand));
            }

            /// <summary>
            /// Returns whether one scalar has an invariant textual representation admitted by template concatenation.<br/>
            /// Binary arrays are deliberately excluded because their implicit CLR text is not a useful or stable value representation.<br/>
            /// </summary>
            /// <param name="type">Normalized non-nullable source type.<br/></param>
            /// <returns><see langword="true"/> for supported textual, Boolean, character, numeric, identifier, and temporal scalars.<br/></returns>
            private static bool IsSupportedInvariantStringType(Type type)
                => type != typeof(byte[]) && SelectorScalarContract.IsSupported(type);

            /// <summary>
            /// Returns whether one CLR numeric type has a stable natural-byte encoding in the public selector contract.<br/>
            /// </summary>
            /// <param name="type">Exact source type recorded by the persisted descriptor.<br/></param>
            /// <returns><see langword="true"/> for every type accepted by <c>NumericSelector.ToHex</c> and <c>ToBase64</c>.<br/></returns>
            private static bool IsSupportedNumericEncodingType(Type type)
                => type == typeof(sbyte) || type == typeof(byte) ||
                   type == typeof(short) || type == typeof(ushort) ||
                   type == typeof(int) || type == typeof(uint) ||
                   type == typeof(long) || type == typeof(ulong) ||
                   type == typeof(nint) || type == typeof(nuint) ||
                   type == typeof(Int128) || type == typeof(UInt128) ||
                   type == typeof(BigInteger) || type == typeof(Half) ||
                   type == typeof(float) || type == typeof(double) ||
                   type == typeof(decimal);
        }
    }

    /// <summary>
    /// Adds the runtime application offramp to every compile-time typed selector without turning the selector hierarchy into an unrestricted query-expression provider.<br/>
    /// </summary>
    public static class SelectorExternalExtensions
    {
        /// <summary>
        /// Applies developer code to each present scalar selector value during native Abraxas execution.<br/>
        /// Source and result must both belong to the supported scalar contract; null reference results become missing selector values, exceptions remain visible, and the operation cannot be serialized or used as durable index intent.<br/>
        /// </summary>
        /// <typeparam name="TRecord">Record type owning the source selector.<br/></typeparam>
        /// <typeparam name="TSource">Exact scalar source type inferred from the selector.<br/></typeparam>
        /// <typeparam name="TResult">Exact supported scalar result type inferred from the operation.<br/></typeparam>
        /// <param name="source">Direct or computed typed selector supplying each present value.<br/></param>
        /// <param name="operation">Application function invoked once per present source value.<br/></param>
        /// <returns>A runtime-only typed selector consumable by selector-aware Abraxas terminals.<br/></returns>
        public static Selector<TRecord>.ExternalSelector<TResult> External<TRecord, TSource, TResult>(
            this Selector<TRecord>.IValue<TSource> source,
            Func<TSource, TResult> operation)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(operation);
            if (!SelectorScalarContract.IsSupportedExternalSource(typeof(TSource)) ||
                !SelectorScalarContract.IsSupported(typeof(TResult)))
            {
                throw new NotSupportedException(
                    "Selector.External supports only established Abraxas scalar source and result types. " +
                    "Use an Inheto converter plus an Abraxas function index for custom binary projections.");
            }
            if (source is not ISelectorStructure structure || structure.ResultType != typeof(TSource))
                throw new NotSupportedException("Selector.External requires a complete exact typed source selector.");

            IBackendNeutralSelectorDescriptor? descriptor = structure.SemanticDescriptor;
            if (descriptor is null && !string.IsNullOrWhiteSpace(structure.TypedSourcePropertyPath))
                descriptor = new DirectPropertySelectorDescriptor(structure.TypedSourcePropertyPath);
            if (descriptor is null)
                throw new NotSupportedException("Selector.External requires executable source semantics.");

            return new Selector<TRecord>.ExternalSelector<TResult>(
                new Selector<TRecord>.RuntimeExternalSelectorDescriptor(
                    descriptor,
                    typeof(TSource),
                    typeof(TResult),
                    operation));
        }
    }
}

namespace AbraxasDB
{
    /// <summary>
    /// Retains one process-lifetime canonical display string for every observed value of an exact enum type.<br/>
    /// Declared enum values are preloaded once, and unnamed flag combinations pay conversion/allocation only on their first observation.<br/>
    /// </summary>
    /// <typeparam name="TEnum">Exact enum type whose display values are cached.<br/></typeparam>
    internal static class EnumDisplayTextCache<TEnum>
        where TEnum : struct, Enum
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<TEnum, string> Values = CreateValues();

        /// <summary>
        /// Gets the canonical display text for one enum value without allocating after that exact numeric value has been observed.<br/>
        /// </summary>
        /// <param name="value">Exact enum value to render.<br/></param>
        /// <returns>The process-lifetime canonical display string matching <see cref="Enum.ToString()"/>.<br/></returns>
        internal static string Get(TEnum value)
            => Values.GetOrAdd(value, static current => current.ToString());

        /// <summary>
        /// Preloads every declared enum constant so named values never allocate during record evaluation.<br/>
        /// Aliased numeric constants naturally converge on the dictionary's single canonical entry.<br/>
        /// </summary>
        /// <returns>The initialized value-to-text cache.<br/></returns>
        private static System.Collections.Concurrent.ConcurrentDictionary<TEnum, string> CreateValues()
        {
            var values = new System.Collections.Concurrent.ConcurrentDictionary<TEnum, string>();
            foreach (TEnum value in Enum.GetValues<TEnum>())
                values.TryAdd(value, value.ToString());
            return values;
        }
    }
}
