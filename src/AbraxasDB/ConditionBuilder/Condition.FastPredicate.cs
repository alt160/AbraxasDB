using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AbraxasDB
{
    public static partial class Condition<T>
    {
        internal interface IFastPredicateSource
        {
            bool TryBuildEvaluator(Builder builder, Inheto.DeserializationOptions options, out Func<Inheto.InhetoBinary, bool> evaluator);
        }

        /// <summary>
        /// Owns regular-expression runners for one parallel semantic-evaluation worker.<br/>
        /// A compiled <see cref="Regex"/> retains only a small shared runner cache, so concurrent calls against one instance can allocate a replacement runner for nearly every record.<br/>
        /// FastFind workers enter this scope once, lazily clone each distinct execution-frozen regex once, and reuse that clone until the worker exits; sequential callers continue using the original instance directly.<br/>
        /// </summary>
        internal sealed class SemanticWorkerRegexScope : IDisposable
        {
            [ThreadStatic]
            private static SemanticWorkerRegexScope? current;

            private readonly SemanticWorkerRegexScope? prior;
            private Dictionary<Regex, Regex>? workerRegexes;
            private Regex? lastSource;
            private Regex? lastWorkerRegex;
            private bool disposed;

            private SemanticWorkerRegexScope()
            {
                prior = current;
                current = this;
            }

            /// <summary>
            /// Enters one worker-owned regex scope on the current dedicated evaluation thread.<br/>
            /// Nested scopes restore their predecessor when disposed so unrelated condition execution cannot inherit a worker's cached runners.<br/>
            /// </summary>
            /// <returns>A scope that must be disposed by the same worker thread that entered it.<br/></returns>
            internal static SemanticWorkerRegexScope Enter()
            {
                return new SemanticWorkerRegexScope();
            }

            /// <summary>
            /// Resolves the regex instance appropriate for the current execution thread.<br/>
            /// Sequential execution has no worker scope and therefore preserves the exact caller/planner instance; parallel execution pays one clone per distinct regex per worker and then a single reference comparison on the usual one-regex hot path.<br/>
            /// </summary>
            /// <param name="source">Execution-frozen regex whose pattern, options, and timeout define the condition semantics.<br/></param>
            /// <returns>The original regex for sequential execution, or the current worker's reusable clone for parallel execution.<br/></returns>
            internal static Regex Resolve(Regex source)
            {
                ArgumentNullException.ThrowIfNull(source);
                SemanticWorkerRegexScope? scope = current;
                return scope is null ? source : scope.ResolveWorkerRegex(source);
            }

            /// <summary>
            /// Restores the preceding thread scope and releases every worker-local regex reference.<br/>
            /// The scope deliberately owns no cross-thread state, so disposal requires no synchronization and cannot retain completed condition plans on long-lived worker threads.<br/>
            /// </summary>
            public void Dispose()
            {
                if (disposed)
                    return;

                if (!ReferenceEquals(current, this))
                    throw new InvalidOperationException("Semantic worker regex scopes must be disposed in reverse order on the thread that entered them.");

                disposed = true;
                current = prior;
                lastSource = null;
                lastWorkerRegex = null;
                workerRegexes?.Clear();
                workerRegexes = null;
            }

            /// <summary>
            /// Returns or creates the current worker's clone for one source regex.<br/>
            /// A last-used pair removes dictionary work from the common single-regex condition while a reference-keyed dictionary preserves reuse for composite conditions containing multiple regex leaves.<br/>
            /// </summary>
            /// <param name="source">Execution-frozen source regex.<br/></param>
            /// <returns>The worker-owned semantic equivalent.<br/></returns>
            private Regex ResolveWorkerRegex(Regex source)
            {
                if (ReferenceEquals(lastSource, source))
                    return lastWorkerRegex!;

                workerRegexes ??= new Dictionary<Regex, Regex>(ReferenceEqualityComparer.Instance);
                if (!workerRegexes.TryGetValue(source, out Regex? workerRegex))
                {
                    workerRegex = new Regex(source.ToString(), source.Options, source.MatchTimeout);
                    workerRegexes.Add(source, workerRegex);
                }

                lastSource = source;
                lastWorkerRegex = workerRegex;
                return workerRegex;
            }
        }

        /// <summary>
        /// Identifies a condition leaf whose value lives in Abraxas' reserved external State index rather than inside the Inheto payload.<br/>
        /// RecordStore supplies the nullable State value only for plans that contain one of these leaves, preserving the ordinary payload-only hot path.<br/>
        /// </summary>
        internal interface IStatePredicateSource
        {
            /// <summary>
            /// Materializes one nullable-State predicate from the leaf's frozen operands.<br/>
            /// </summary>
            /// <param name="builder">Condition builder that owns execution-scoped operand materialization.<br/></param>
            /// <param name="evaluator">State predicate when the leaf is the reserved State selector.<br/></param>
            /// <returns><see langword="true"/> when State owns this leaf; otherwise <see langword="false"/>.<br/></returns>
            bool TryBuildStateEvaluator(Builder builder, out Func<long?, bool> evaluator);
        }

        /// <summary>
        /// Resolves one direct property selector exclusively through its immutable backend-neutral descriptor.<br/>
        /// Derived descriptors return false because their source paths are resolved by their dedicated adapters.<br/>
        /// </summary>
        /// <param name="selectorExpression">The selector wrapper whose structural metadata is being inspected.<br/></param>
        /// <param name="propPath">The normalized direct property path when resolution succeeds.<br/></param>
        /// <returns><c>true</c> only when the selector carries a direct-property descriptor; otherwise <c>false</c>.<br/></returns>
        internal static bool TryExtractPropPath(ValueOrExpression<string> selectorExpression, out string propPath)
        {
            propPath = string.Empty;
            if (selectorExpression?.SelectorDescriptor is DirectPropertySelectorDescriptor directDescriptor)
            {
                propPath = directDescriptor.PropertyPath;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Resolves a direct or runtime-deferred property selector for the current execution plan.<br/>
        /// Deferred paths use the builder's ordinary parameter snapshot and owner cache, so a mutable typed parameter is coherent across the complete condition and an expression is invoked only once for this selector during one execution.<br/>
        /// The selected path is canonicalized and validated against the record type before it can participate in semantic evaluation or LibraDex index planning.<br/>
        /// </summary>
        /// <param name="builder">Condition builder owning the current execution materialization scope.<br/></param>
        /// <param name="selectorExpression">Direct or deferred property selector.<br/></param>
        /// <param name="propPath">Canonical property path when resolution succeeds.<br/></param>
        /// <returns><see langword="true"/> for direct or deferred property selectors; otherwise <see langword="false"/>.<br/></returns>
        internal static bool TryExtractPropPath(
            Builder builder,
            ValueOrExpression<string> selectorExpression,
            out string propPath)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (TryExtractPropPath(selectorExpression, out propPath))
                return true;

            if (selectorExpression?.SelectorDescriptor is not DeferredPropertySelectorDescriptor deferred)
                return false;

            string selected = builder.MaterializeParameter(
                deferred.PropertyPath,
                propertyPath: null,
                owner: deferred);
            if (string.IsNullOrWhiteSpace(selected))
                throw new InvalidOperationException("A deferred condition property path materialized as null, empty, or whitespace.");

            Inheto.PropPathCheck check = Inheto.InhetoBinary.CheckPropPath(typeof(T), selected);
            if (!check.IsValid)
            {
                throw new InvalidOperationException(
                    $"Deferred condition property path '{selected}' is not resolvable from '{typeof(T).FullName}': {check.FailureKind}" +
                    (string.IsNullOrEmpty(check.FailedSegment) ? "." : $" at segment '{check.FailedSegment}'."));
            }

            propPath = check.CanonicalPath;
            return true;
        }

        /// <summary>
        /// Builds one backend-neutral evaluator that reads a typed property directly from Inheto and applies a pre-materialized predicate.<br/>
        /// The caller must freeze all deferred operands and clock-relative values before passing the predicate so record evaluation performs only presence testing, typed restoration, and the predicate itself.<br/>
        /// Missing and null property values return false for ordinary positive and negated comparisons; explicit null tests use their separate condition path.<br/>
        /// </summary>
        /// <typeparam name="TValue">The CLR value type restored from the indexed property path.<br/></typeparam>
        /// <param name="builder">The owning condition builder used to materialize the property-path selector once for this evaluator.<br/></param>
        /// <param name="selectorExpression">The property-path selector used by the condition leaf.<br/></param>
        /// <param name="options">Inheto deserialization options used to restore the typed property value.<br/></param>
        /// <param name="predicate">The fully materialized value predicate applied to each present property.<br/></param>
        /// <param name="evaluator">The completed per-record evaluator when this method returns true.<br/></param>
        /// <returns>True when the selector resolves to a property path; otherwise false.<br/></returns>
        internal static bool TryBuildTypedEvaluator<TValue>(
            Builder builder,
            ValueOrExpression<string> selectorExpression,
            Inheto.DeserializationOptions options,
            Func<TValue, bool> predicate,
            out Func<Inheto.InhetoBinary, bool> evaluator)
        {
            evaluator = default!;
            if (!TryBuildConditionValueReader<TValue>(builder, selectorExpression, options, out _, out var readValue))
                return false;

            evaluator = binary =>
            {
                var candidate = readValue(binary);
                return candidate.HasValue && predicate(candidate.Value);
            };
            return true;
        }
        /// <summary>
        /// Builds one backend-neutral membership evaluator for a typed property-path condition.<br/>
        /// Every deferred collection element is materialized exactly once while the execution plan is built, then stored in a typed hash set for constant-time record evaluation.<br/>
        /// Missing and null property values do not satisfy either membership or negated-membership conditions.<br/>
        /// </summary>
        /// <typeparam name="TValue">The CLR value type read from Inheto and stored in the membership set.<br/></typeparam>
        /// <param name="builder">The condition builder that owns parameter hooks and execution-scoped materialization state.<br/></param>
        /// <param name="selectorExpression">The property-path selector used by the condition leaf.<br/></param>
        /// <param name="parameters">Static or deferred membership operands.<br/></param>
        /// <param name="options">Inheto deserialization options used to restore the typed property value.<br/></param>
        /// <param name="negate">When true, accepts present values outside the materialized set; otherwise accepts values inside it.<br/></param>
        /// <param name="owner">The condition leaf used to isolate execution-scoped operand caching.<br/></param>
        /// <param name="evaluator">The completed per-record evaluator when this method returns true.<br/></param>
        /// <returns>True when the selector resolves to a property path; otherwise false.<br/></returns>
        internal static bool TryBuildMembershipEvaluator<TValue>(
            Builder builder,
            ValueOrExpression<string> selectorExpression,
            IEnumerable<ValueOrExpression<TValue>>? parameters,
            Inheto.DeserializationOptions options,
            bool negate,
            object owner,
            out Func<Inheto.InhetoBinary, bool> evaluator)
        {
            evaluator = default!;
            if (!TryBuildConditionValueReader<TValue>(builder, selectorExpression, options, out var propPath, out var readValue))
                return false;

            var values = parameters?
                .Select(parameter => builder.MaterializeTemporalParameter(
                    selectorExpression,
                    parameter,
                    propPath,
                    owner: owner))
                .ToHashSet()
                ?? new HashSet<TValue>();

            evaluator = binary =>
            {
                var candidate = readValue(binary);
                if (!candidate.HasValue)
                    return false;

                var contains = values.Contains(candidate.Value);
                return negate ? !contains : contains;
            };
            return true;
        }

        /// <summary>
        /// Builds one backend-neutral membership evaluator from an execution-frozen deferred set.<br/>
        /// The complete enumerable is materialized once through the builder's identity-wide parameter snapshot, then copied into a typed hash set for constant-time record evaluation.<br/>
        /// Missing and null property values do not satisfy either membership or negated-membership conditions.<br/>
        /// </summary>
        /// <typeparam name="TValue">The CLR value type read from Inheto and stored in the membership set.<br/></typeparam>
        /// <param name="builder">The condition builder that owns execution-scoped parameter state.<br/></param>
        /// <param name="selectorExpression">The property-path selector used by the condition leaf.<br/></param>
        /// <param name="setParameter">The single deferred membership operand.<br/></param>
        /// <param name="options">Inheto deserialization options used to restore the typed property value.<br/></param>
        /// <param name="negate">When true, accepts present values outside the materialized set.<br/></param>
        /// <param name="owner">The condition leaf used to isolate execution-scoped operand caching.<br/></param>
        /// <param name="evaluator">The completed per-record evaluator when this method returns true.<br/></param>
        /// <returns>True when the selector resolves to a property path; otherwise false.<br/></returns>
        internal static bool TryBuildMembershipEvaluator<TValue>(
            Builder builder,
            ValueOrExpression<string> selectorExpression,
            ValueOrExpression<IEnumerable<TValue>> setParameter,
            Inheto.DeserializationOptions options,
            bool negate,
            object owner,
            out Func<Inheto.InhetoBinary, bool> evaluator)
        {
            evaluator = default!;
            if (!TryBuildConditionValueReader<TValue>(selectorExpression, options, out var propPath, out var readValue))
                return false;

            IEnumerable<TValue> materialized = builder.MaterializeParameter(setParameter, propPath, owner: owner);
            var values = new HashSet<TValue>(materialized);
            evaluator = binary =>
            {
                var candidate = readValue(binary);
                if (!candidate.HasValue)
                    return false;

                bool contains = values.Contains(candidate.Value);
                return negate ? !contains : contains;
            };
            return true;
        }

        /// <summary>
        /// Builds a backend-neutral typed reader for either a direct Inheto property or a described binary-slice derivation.<br/>
        /// Derived readers retain the source property path for parameter metadata while converting bytes according to the immutable selector descriptor.<br/>
        /// Missing, null, invalid, or unsupported derived values return <c>HasValue == false</c> and are excluded from ordinary value comparisons.<br/>
        /// </summary>
        /// <typeparam name="TValue">The CLR value expected by the consuming condition operator.<br/></typeparam>
        /// <param name="selectorExpression">The direct or derived selector expression.<br/></param>
        /// <param name="options">Inheto deserialization options used to restore the source property.<br/></param>
        /// <param name="propPath">The direct property path or derived selector's source property path.<br/></param>
        /// <param name="reader">The completed per-record typed value reader.<br/></param>
        /// <returns>True when the selector has a supported backend-neutral reader; otherwise false.<br/></returns>
        internal static bool TryBuildConditionValueReader<TValue>(
            ValueOrExpression<string> selectorExpression,
            Inheto.DeserializationOptions options,
            out string propPath,
            out Func<Inheto.InhetoBinary, (bool HasValue, TValue Value)> reader)
        {
            reader = default!;
            propPath = string.Empty;
            if (selectorExpression is null)
                return false;

            if (selectorExpression.SelectorDescriptor is IRuntimeSelectorDescriptor<T> runtimeSelector)
                return runtimeSelector.TryBuild(options, out propPath, out reader);

            if (typeof(TValue) == typeof(string) &&
                TryBuildStringConcatReader(selectorExpression.SelectorDescriptor, options, out propPath, out var concatReader))
            {
                reader = (Func<Inheto.InhetoBinary, (bool HasValue, TValue Value)>)(object)concatReader;
                return true;
            }

            if (selectorExpression.SelectorDescriptor is Selector<T>.DecimalOperationSelectorDescriptor decimalOperation)
            {
                if (!decimalOperation.TryGetSourcePropertyPath(out propPath))
                    return false;

                reader = binary =>
                    decimalOperation.TryRead(binary, options, out TValue converted)
                        ? (true, converted)
                        : (false, default!);
                return true;
            }

            if (selectorExpression.SelectorDescriptor is Selector<T>.StringOperationSelectorDescriptor stringOperation)
            {
                if (!stringOperation.TryGetSourcePropertyPath(out propPath))
                    return false;

                reader = binary =>
                    stringOperation.TryRead(binary, options, out TValue converted)
                        ? (true, converted)
                        : (false, default!);
                return true;
            }

            if (selectorExpression.SelectorDescriptor is Selector<T>.TimeSpanProjectionSelectorDescriptor timeSpanProjection)
            {
                if (!timeSpanProjection.TryGetSourcePropertyPath(out propPath))
                    return false;

                reader = binary =>
                    timeSpanProjection.TryRead(binary, options, out TValue converted)
                        ? (true, converted)
                        : (false, default!);
                return true;
            }

            if (selectorExpression.SelectorDescriptor is Selector<T>.NumericArithmeticSelectorDescriptor numericArithmetic)
            {
                if (!numericArithmetic.TryGetSourcePropertyPath(out propPath))
                    return false;

                reader = binary =>
                    numericArithmetic.TryRead(binary, options, out TValue converted)
                        ? (true, converted)
                        : (false, default!);
                return true;
            }

            if (selectorExpression.SelectorDescriptor is Selector<T>.TemporalArithmeticSelectorDescriptor temporalArithmetic)
            {
                if (!temporalArithmetic.TryGetSourcePropertyPath(out propPath))
                    return false;

                reader = binary =>
                    temporalArithmetic.TryRead(binary, options, out TValue converted)
                        ? (true, converted)
                        : (false, default!);
                return true;
            }

            if (selectorExpression.SelectorDescriptor is Selector<T>.BuiltInSelectorDescriptor builtIn)
            {
                if (!builtIn.TryGetSourcePropertyPath(out propPath))
                    return false;

                reader = binary =>
                    builtIn.TryRead(binary, options, out TValue converted)
                        ? (true, converted)
                        : (false, default!);
                return true;
            }

            if (selectorExpression.SelectorDescriptor is BinarySliceSelectorDescriptor slice)
            {
                if (!TryGetConditionSourcePropPath(slice.SourceSelector, out propPath))
                    return false;

                var sourcePath = propPath;
                reader = binary =>
                {
                    return slice.TryRead<TValue>(binary, options, out var converted)
                        ? (true, converted)
                        : (false, default!);
                };
                return true;
            }

            if (!TryExtractPropPath(selectorExpression, out propPath))
                return false;

            var directPath = propPath;
            Inheto.PropPathCheck pathCheck = Inheto.InhetoBinary.CheckPropPath<T, TValue>(directPath);
            if (!pathCheck.IsValid)
            {
                throw new InvalidOperationException(
                    $"Condition property path '{directPath}' is not resolvable from '{typeof(T).FullName}': {pathCheck.FailureKind}" +
                    (string.IsNullOrEmpty(pathCheck.FailedSegment) ? "." : $" at segment '{pathCheck.FailedSegment}'."));
            }

            reader = binary =>
            {
                Inheto.PropPathReadState state = binary.TryReadPropPath(
                    pathCheck,
                    out TValue? candidate,
                    options,
                    throwOnInvocationError: true);
                return state == Inheto.PropPathReadState.Value
                    ? (true, candidate!)
                    : (false, default!);
            };
            return true;
        }

        /// <summary>
        /// Compiles a selector-concatenation tree once into a flat execution plan.<br/>
        /// Literal runs are coalesced during construction; ordinary rows with up to four parts use fixed-arity <see cref="string.Concat(string?, string?)"/> overloads, while longer rows rent temporary reference storage and create exactly one result string.<br/>
        /// Missing and null selector parts become empty strings so evaluation follows normal C# concatenation semantics without intermediate concatenated strings.<br/>
        /// </summary>
        /// <param name="descriptor">Potential concatenation root.<br/></param>
        /// <param name="options">Active Inheto deserialization registrations.<br/></param>
        /// <param name="propPath">First source property path retained for diagnostics and customization context.<br/></param>
        /// <param name="reader">Flat allocation-lean concatenation reader when the descriptor is a concatenation root.<br/></param>
        /// <returns><see langword="true"/> when a complete concatenation plan was built; otherwise <see langword="false"/>.<br/></returns>
        private static bool TryBuildStringConcatReader(
            IBackendNeutralSelectorDescriptor? descriptor,
            Inheto.DeserializationOptions options,
            out string propPath,
            out Func<Inheto.InhetoBinary, (bool HasValue, string Value)> reader)
        {
            propPath = string.Empty;
            reader = default!;
            if (!IsStringConcatNode(descriptor))
                return false;

            var segments = new List<StringConcatSegment>(4);
            if (!TryFlattenStringConcat(descriptor!, options, segments, ref propPath))
                return false;

            var plan = new StringConcatPlan(segments.ToArray());
            reader = plan.Read;
            return true;
        }

        /// <summary>
        /// Expands nested selector and literal concatenation nodes into their exact left-to-right parts.<br/>
        /// Non-concatenation leaves reuse the established typed selector-reader compiler, preserving all supported string projections without adding reflection to record evaluation.<br/>
        /// </summary>
        private static bool TryFlattenStringConcat(
            IBackendNeutralSelectorDescriptor descriptor,
            Inheto.DeserializationOptions options,
            List<StringConcatSegment> segments,
            ref string propPath)
        {
            if (descriptor is Selector<T>.BuiltInSelectorDescriptor builtIn &&
                builtIn.Operation == BuiltInSelectorOperationKind.StringConcatLiteral)
            {
                return TryFlattenStringConcat(builtIn.Source, options, segments, ref propPath) &&
                    AppendStringConcatLiteral(segments, builtIn.TextOperand ?? string.Empty);
            }

            if (descriptor is Selector<T>.BuiltInSelectorDescriptor prefix &&
                prefix.Operation == BuiltInSelectorOperationKind.StringPrependLiteral)
            {
                return AppendStringConcatLiteral(segments, prefix.TextOperand ?? string.Empty) &&
                    TryFlattenStringConcat(prefix.Source, options, segments, ref propPath);
            }

            if (descriptor is Selector<T>.StringOperationSelectorDescriptor operation &&
                operation.Operation == StringOperationKind.ConcatSelector)
            {
                return TryFlattenStringConcat(operation.Source, options, segments, ref propPath) &&
                    TryFlattenStringConcat(operation.RightSource!, options, segments, ref propPath);
            }

            var wrapped = new ValueOrExpression<string>(static () => string.Empty)
                .WithSelectorDescriptor(descriptor);
            if (!TryBuildConditionValueReader<string>(wrapped, options, out var sourcePath, out var sourceReader))
                return false;
            if (propPath.Length == 0)
                propPath = sourcePath;
            segments.Add(new StringConcatSegment(sourceReader));
            return true;
        }

        private static bool IsStringConcatNode(IBackendNeutralSelectorDescriptor? descriptor)
            => descriptor is Selector<T>.BuiltInSelectorDescriptor
                {
                    Operation: BuiltInSelectorOperationKind.StringConcatLiteral or BuiltInSelectorOperationKind.StringPrependLiteral
                } ||
                descriptor is Selector<T>.StringOperationSelectorDescriptor
                {
                    Operation: StringOperationKind.ConcatSelector
                };

        private static bool AppendStringConcatLiteral(List<StringConcatSegment> segments, string literal)
        {
            if (literal.Length == 0)
                return true;
            if (segments.Count != 0 && segments[^1].IsLiteral)
            {
                segments[^1] = new StringConcatSegment(string.Concat(segments[^1].Literal, literal));
                return true;
            }
            segments.Add(new StringConcatSegment(literal));
            return true;
        }

        private readonly struct StringConcatSegment
        {
            internal StringConcatSegment(string literal)
            {
                Literal = literal;
                Reader = null;
            }

            internal StringConcatSegment(Func<Inheto.InhetoBinary, (bool HasValue, string Value)> reader)
            {
                Literal = null;
                Reader = reader ?? throw new ArgumentNullException(nameof(reader));
            }

            internal string? Literal { get; }
            internal Func<Inheto.InhetoBinary, (bool HasValue, string Value)>? Reader { get; }
            internal bool IsLiteral => Reader is null;

            internal string Read(Inheto.InhetoBinary binary)
            {
                if (Reader is null)
                    return Literal ?? string.Empty;
                (bool hasValue, string value) = Reader(binary);
                return hasValue ? value ?? string.Empty : string.Empty;
            }
        }

        private sealed class StringConcatPlan
        {
            private readonly StringConcatSegment[] segments;

            internal StringConcatPlan(StringConcatSegment[] segments)
            {
                this.segments = segments ?? throw new ArgumentNullException(nameof(segments));
            }

            internal (bool HasValue, string Value) Read(Inheto.InhetoBinary binary)
            {
                switch (segments.Length)
                {
                    case 0:
                        return (true, string.Empty);
                    case 1:
                        return (true, segments[0].Read(binary));
                    case 2:
                        return (true, string.Concat(segments[0].Read(binary), segments[1].Read(binary)));
                    case 3:
                        return (true, string.Concat(
                            segments[0].Read(binary),
                            segments[1].Read(binary),
                            segments[2].Read(binary)));
                    case 4:
                        return (true, string.Concat(
                            segments[0].Read(binary),
                            segments[1].Read(binary),
                            segments[2].Read(binary),
                            segments[3].Read(binary)));
                }

                string?[] values = ArrayPool<string?>.Shared.Rent(segments.Length);
                try
                {
                    int totalLength = 0;
                    for (int index = 0; index < segments.Length; index++)
                    {
                        string value = segments[index].Read(binary);
                        values[index] = value;
                        totalLength = checked(totalLength + value.Length);
                    }

                    string result = string.Create(
                        totalLength,
                        (Values: values, Count: segments.Length),
                        static (destination, state) =>
                        {
                            int offset = 0;
                            for (int index = 0; index < state.Count; index++)
                            {
                                string value = state.Values[index] ?? string.Empty;
                                value.AsSpan().CopyTo(destination[offset..]);
                                offset += value.Length;
                            }
                        });
                    return (true, result);
                }
                finally
                {
                    Array.Clear(values, 0, segments.Length);
                    ArrayPool<string?>.Shared.Return(values);
                }
            }
        }

        /// <summary>
        /// Builds a typed condition reader with access to the executing store's semantic-index projection registry.<br/>
        /// Named operands resolve through the store-bound projection only after LibraDex planning has found no usable physical index; every other selector retains the established direct or computed reader path.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact CLR type requested by the consuming operator.<br/></typeparam>
        /// <param name="builder">Condition builder bound to the executing record store.<br/></param>
        /// <param name="selectorExpression">Direct, computed, or named selector operand.<br/></param>
        /// <param name="options">Active Inheto deserialization options.<br/></param>
        /// <param name="propPath">Diagnostic operand context used for parameter materialization.<br/></param>
        /// <param name="reader">Reusable allocation-lean typed payload reader.<br/></param>
        /// <returns><see langword="true"/> when the selector has exact typed semantic evaluation support.<br/></returns>
        internal static bool TryBuildConditionValueReader<TValue>(
            Builder builder,
            ValueOrExpression<string> selectorExpression,
            Inheto.DeserializationOptions options,
            out string propPath,
            out Func<Inheto.InhetoBinary, (bool HasValue, TValue Value)> reader)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (selectorExpression is null)
            {
                propPath = string.Empty;
                reader = default!;
                return false;
            }

            if (selectorExpression.SelectorDescriptor is DeferredPropertySelectorDescriptor)
            {
                if (!TryExtractPropPath(builder, selectorExpression, out propPath))
                {
                    reader = default!;
                    return false;
                }

                var direct = ((ValueOrExpression<string>)propPath)
                    .WithSelectorDescriptor(new DirectPropertySelectorDescriptor(propPath));
                return TryBuildConditionValueReader(direct, options, out propPath, out reader);
            }

            if (selectorExpression.SelectorDescriptor is not NamedIndexSelectorDescriptor named)
                return TryBuildConditionValueReader(selectorExpression, options, out propPath, out reader);

            propPath = named.SemanticName;
            reader = default!;
            if (!builder.TryResolveSemanticIndexReader(named.SemanticName, typeof(TValue), out var untypedReader))
                return false;

            reader = binary =>
            {
                (bool hasValue, object? value) = untypedReader(binary);
                return hasValue && value is TValue typed
                    ? (true, typed)
                    : (false, default!);
            };
            return true;
        }

        /// <summary>
        /// Resolves the source property path used to materialize semantic operands without relaxing the stricter direct-index selector test.<br/>
        /// Direct selectors return their own property path; described binary slices return the unsliced source-blob path retained by the descriptor.<br/>
        /// </summary>
        /// <param name="selectorExpression">The direct or derived selector used by the semantic condition leaf.<br/></param>
        /// <param name="propPath">The property path that owns the source value and parameter customization scope.<br/></param>
        /// <returns>True when the selector has a direct semantic source property; otherwise false.<br/></returns>
        internal static bool TryGetConditionSourcePropPath(
            ValueOrExpression<string> selectorExpression,
            out string propPath)
        {
            propPath = string.Empty;
            if (selectorExpression is null)
                return false;

            if (selectorExpression.SelectorDescriptor is Selector<T>.DecimalOperationSelectorDescriptor decimalOperation)
                return decimalOperation.TryGetSourcePropertyPath(out propPath);

            if (selectorExpression.SelectorDescriptor is Selector<T>.StringOperationSelectorDescriptor stringOperation)
                return stringOperation.TryGetSourcePropertyPath(out propPath);

            if (selectorExpression.SelectorDescriptor is Selector<T>.TimeSpanProjectionSelectorDescriptor timeSpanProjection)
                return timeSpanProjection.TryGetSourcePropertyPath(out propPath);

            if (selectorExpression.SelectorDescriptor is Selector<T>.NumericArithmeticSelectorDescriptor numericArithmetic)
                return numericArithmetic.TryGetSourcePropertyPath(out propPath);

            if (selectorExpression.SelectorDescriptor is Selector<T>.TemporalArithmeticSelectorDescriptor temporalArithmetic)
                return temporalArithmetic.TryGetSourcePropertyPath(out propPath);

            if (selectorExpression.SelectorDescriptor is Selector<T>.BuiltInSelectorDescriptor builtIn)
                return builtIn.TryGetSourcePropertyPath(out propPath);

            if (selectorExpression.SelectorDescriptor is BinarySliceSelectorDescriptor slice)
                return TryGetConditionSourcePropPath(slice.SourceSelector, out propPath);

            if (selectorExpression.SelectorDescriptor is NamedIndexSelectorDescriptor named)
            {
                propPath = named.SemanticName;
                return true;
            }

            return TryExtractPropPath(selectorExpression, out propPath);
        }

        /// <summary>
        /// Resolves the materialization context for a direct, derived, named, or runtime-deferred condition selector.<br/>
        /// Deferred direct paths are frozen through the current builder before ordinary source-path inspection continues.<br/>
        /// </summary>
        /// <param name="builder">Condition builder owning the active execution snapshot.<br/></param>
        /// <param name="selectorExpression">Condition selector whose source path is required.<br/></param>
        /// <param name="propPath">Canonical source path or semantic name when available.<br/></param>
        /// <returns><see langword="true"/> when a source path can be resolved for this execution.<br/></returns>
        internal static bool TryGetConditionSourcePropPath(
            Builder builder,
            ValueOrExpression<string> selectorExpression,
            out string propPath)
        {
            if (selectorExpression is null)
            {
                propPath = string.Empty;
                return false;
            }

            if (selectorExpression.SelectorDescriptor is DeferredPropertySelectorDescriptor)
                return TryExtractPropPath(builder, selectorExpression, out propPath);

            return TryGetConditionSourcePropPath(selectorExpression, out propPath);
        }

        /// <summary>
        /// Reads one typed semantic value from either a direct Inheto property or a backend-neutral binary-slice descriptor.<br/>
        /// The caller supplies the already-resolved source path so record evaluation requires no textual selector rendering or parsing.<br/>
        /// Missing, null, invalid, insufficient, and unsupported values return false rather than participating in ordinary value comparisons.<br/>
        /// </summary>
        /// <typeparam name="TValue">The CLR value required by the consuming condition operation.<br/></typeparam>
        /// <param name="selectorExpression">The direct or derived selector being evaluated.<br/></param>
        /// <param name="propPath">The direct property path or derived selector's source-blob path.<br/></param>
        /// <param name="binary">The candidate Inheto value container.<br/></param>
        /// <param name="options">The deserialization options used to restore the source property.<br/></param>
        /// <param name="value">The restored or derived CLR value when successful.<br/></param>
        /// <returns>True when a present valid value was restored; otherwise false.<br/></returns>
        internal static bool TryReadConditionValue<TValue>(
            ValueOrExpression<string> selectorExpression,
            string propPath,
            Inheto.InhetoBinary binary,
            Inheto.DeserializationOptions options,
            out TValue value)
        {
            value = default!;

            if (selectorExpression?.SelectorDescriptor is Selector<T>.DecimalOperationSelectorDescriptor decimalOperation)
                return decimalOperation.TryRead(binary, options, out value);

            if (selectorExpression?.SelectorDescriptor is Selector<T>.StringOperationSelectorDescriptor stringOperation)
                return stringOperation.TryRead(binary, options, out value);

            if (selectorExpression?.SelectorDescriptor is Selector<T>.TimeSpanProjectionSelectorDescriptor timeSpanProjection)
                return timeSpanProjection.TryRead(binary, options, out value);

            if (selectorExpression?.SelectorDescriptor is Selector<T>.NumericArithmeticSelectorDescriptor numericArithmetic)
                return numericArithmetic.TryRead(binary, options, out value);

            if (selectorExpression?.SelectorDescriptor is Selector<T>.TemporalArithmeticSelectorDescriptor temporalArithmetic)
                return temporalArithmetic.TryRead(binary, options, out value);

            if (selectorExpression?.SelectorDescriptor is BinarySliceSelectorDescriptor slice)
            {
                if (!HasConditionValue(binary, propPath, options))
                    return false;
                var source = binary.ReadPropAs<byte[]>(propPath, options);
                return slice.TryRead(source, out value);
            }

            Inheto.PropPathCheck pathCheck = Inheto.InhetoBinary.CheckPropPath<T, TValue>(propPath);
            if (!pathCheck.IsValid)
                return false;

            Inheto.PropPathReadState state = binary.TryReadPropPath(
                pathCheck,
                out TValue? candidate,
                options,
                throwOnInvocationError: true);
            if (state != Inheto.PropPathReadState.Value)
                return false;

            value = candidate!;
            return true;
        }

    }
}
