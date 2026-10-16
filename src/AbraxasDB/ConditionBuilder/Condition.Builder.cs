using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using LibraDex;

namespace AbraxasDB
{

    /// <summary>
    /// Provides the typed fluent condition language and builder stages for an Abraxas record shape.<br/>
    /// </summary>
    /// <typeparam name="T">CLR record shape against which conditions are authored.<br/></typeparam>
    public static partial class Condition<T>
    {
		internal readonly record struct ParameterMetadata(string? Alias, string? PropertyPath);



        /// <summary>
        /// Owns mutable condition fragments, explicit parameters, overrides, and execution-time operand materialization; use independent clones for independently mutable conditions.<br/>
        /// </summary>
        public partial class Builder : IConditionParameterContainer
        {
            internal delegate bool SemanticIndexReaderResolver(
                string semanticName,
                Type expectedType,
                out Func<Inheto.InhetoBinary, (bool HasValue, object? Value)> reader);

            private SemanticIndexReaderResolver? semanticIndexReaderResolver;
            static long _builderSeq = 0;
            string builderID = string.Empty;
            /// <summary>
            /// Creates an empty independent condition builder and its parameter/override collections.<br/>
            /// </summary>
            public Builder()
            {
				fragments = new();
				Parameters = new();
				aliasOverrides = new(StringComparer.Ordinal);
				propPathOverrides = new(StringComparer.Ordinal);
				valueReplacements = new();
				propPathOccurrenceTracker = new(StringComparer.Ordinal);
                var id = Interlocked.Increment(ref _builderSeq);
                builderID = $"_{id:x4}_";
            }

            internal Builder Clone()
            {
                var cloned = new Builder
                {
                    IsGroup = IsGroup,
                    groupCounter = groupCounter,
                    Clock = Clock,
                    semanticIndexReaderResolver = semanticIndexReaderResolver
                };

                foreach (var fragment in fragments)
                {
                    cloned.fragments.Add(fragment is ICloneableConditionFragment cloneable
                        ? cloneable.CloneConditionFragment()
                        : fragment);
                }

                cloned.Parameters.RegisterRange(Parameters.Iterate());

				foreach (var kvp in aliasOverrides)
				{
					cloned.aliasOverrides[kvp.Key] = kvp.Value;
				}

				foreach (var kvp in propPathOverrides)
				{
					var list = new List<PropPathOverride>(kvp.Value.Count);
					foreach (var entry in kvp.Value)
					{
						list.Add(entry.Copy());
					}
					cloned.propPathOverrides[kvp.Key] = list;
				}

				cloned.valueReplacements.AddRange(valueReplacements);

                return cloned;
            }

            /// <summary>
            /// Binds one execution-scoped semantic-index fallback resolver to this builder and every nested condition group.<br/>
            /// The resolver is store-owned and is never captured by durable condition descriptors.<br/>
            /// </summary>
            /// <param name="resolver">Store resolver that maps an exact semantic name and CLR type to a payload reader.<br/></param>
            internal void BindSemanticIndexReaderResolver(SemanticIndexReaderResolver resolver)
            {
                semanticIndexReaderResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
                for (int i = 0; i < fragments.Count; i++)
                {
                    if (fragments[i] is GroupConditionFragment group)
                        group.GroupBuilder.BindSemanticIndexReaderResolver(resolver);
                }
            }

            /// <summary>
            /// Attempts to resolve one named semantic operand through the store currently executing this condition.<br/>
            /// </summary>
            internal bool TryResolveSemanticIndexReader(
                string semanticName,
                Type expectedType,
                out Func<Inheto.InhetoBinary, (bool HasValue, object? Value)> reader)
            {
                reader = default!;
                return semanticIndexReaderResolver is not null &&
                    semanticIndexReaderResolver(semanticName, expectedType, out reader);
            }

            internal void AddGroup(EndCondition group)
            {
                ArgumentNullException.ThrowIfNull(group);
                var groupBuilderClone = group.builder.Clone();
                groupBuilderClone.IsGroup = true;

                AddFragment(new GroupStartFragment());
                Parameters.RegisterRange(groupBuilderClone.Parameters.Iterate());
                AddFragment(new GroupConditionFragment(groupBuilderClone));
                AddFragment(new GroupEndFragment());
            }

            /// <summary>
            /// Resets per-execution parameter ordinals and property-path occurrence counters before an execution plan materializes operands.<br/>
            /// This resets parameter materialization state without altering the condition's immutable semantic descriptor.<br/>
            /// </summary>
            internal void BeginParameterMaterialization()
            {
                BeginParameterMaterialization(Clock.GetUtcNow().UtcDateTime, new LibraDexParameterSnapshot());
            }

            /// <summary>
            /// Resets this builder and every nested group builder for one execution plan using one frozen UTC instant.<br/>
            /// Nested builders own independent deferred-operand caches, so recursive reset prevents grouped conditions from reusing values across executions while the shared instant preserves clock-relative parity across the complete tree.<br/>
            /// </summary>
            /// <param name="utcNow">The single execution-plan UTC instant shared by the complete condition tree.<br/></param>
            /// <param name="parameterSnapshot">The bound-parameter snapshot shared by this builder and all nested groups for the execution.<br/></param>
            private void BeginParameterMaterialization(DateTime utcNow, LibraDexParameterSnapshot parameterSnapshot)
            {
                propPathOccurrenceTracker.Clear();
                executionMaterializationCache.Clear();
                executionParameterSnapshot = parameterSnapshot;
                executionUtcNow = utcNow;
                for (var i = 0; i < fragments.Count; i++)
                {
                    if (fragments[i] is GroupConditionFragment group)
                        group.GroupBuilder.BeginParameterMaterialization(utcNow, parameterSnapshot);
                }
            }

            /// <summary>
            /// Invokes one deferred typed operand, applies the builder's replacement and property-path override rules, raises the existing materialization hook, and returns the final typed value.<br/>
            /// Callers should invoke this once while constructing an execution plan and capture the result outside per-record evaluators.<br/>
            /// </summary>
            /// <typeparam name="TValue">The CLR operand type required by the typed condition operation.<br/></typeparam>
            /// <param name="operand">The static or deferred developer-supplied operand.<br/></param>
            /// <param name="propertyPath">The condition property path associated with this operand.<br/></param>
            /// <param name="parameterAlias">The optional named-parameter alias associated with this operand.<br/></param>
            /// <param name="owner">The optional condition-leaf identity used to cache this operand's materialized value for the current execution; null disables owner-scoped reuse.<br/></param>
            /// <returns>The final typed operand after all existing Abraxas parameter customization hooks run.<br/></returns>
            internal TValue MaterializeParameter<TValue>(ValueOrExpression<TValue> operand, string? propertyPath, string? parameterAlias = null, object? owner = null)
            {
                ArgumentNullException.ThrowIfNull(operand);
                if (owner is not null && executionMaterializationCache.TryGetValue(owner, out var ownerValues) && ownerValues.TryGetValue(operand, out var cached))
                    return (TValue)cached!;

                var metadata = new ParameterMetadata(parameterAlias ?? operand.BindingName, propertyPath);
                object? original;
                if (operand.BoundParameter is LibraDex.LibraDexParameter<TValue> parameter)
                {
                    original = executionParameterSnapshot.Read(parameter);
                }
                else
                {
                    original = operand.GetValue();
                }
                var resolved = ResolveParameterValue(original, metadata, out var effectivePropertyPath, out var propertyPathOccurrence);
                resolved = ApplyParameterMaterializationHooks(resolved, metadata, effectivePropertyPath, propertyPathOccurrence);
                if (resolved is TValue typed)
                {
                    CacheExecutionMaterialization(owner, operand, typed);
                    return typed;
                }
                if (resolved is null && default(TValue) is null)
                {
                    CacheExecutionMaterialization(owner, operand, null);
                    return default!;
                }

                throw new InvalidOperationException($"Condition parameter for '{effectivePropertyPath ?? propertyPath ?? "<unknown>"}' materialized as '{resolved?.GetType()}' but expected '{typeof(TValue)}'.");
            }

            /// <summary>
            /// Materializes one runtime condition wrapper when the consuming planner knows its exact declared CLR type only at semantic-wire traversal time.<br/>
            /// This is the untyped bridge for planner-only descriptor capture; it delegates to <see cref="MaterializeParameter{TValue}(ValueOrExpression{TValue}, string?, string?, object?)"/> so parameter snapshots, aliases, property overrides, event hooks, and per-leaf caching remain identical to normal execution.<br/>
            /// </summary>
            /// <param name="operand">The original typed condition wrapper, never a reconstructed delegate or copied parameter value.<br/></param>
            /// <param name="valueType">The wrapper's exact declared value type.<br/></param>
            /// <param name="propertyPath">The logical selector path used by the established materialization pipeline.<br/></param>
            /// <param name="parameterAlias">The optional explicit binding name.<br/></param>
            /// <param name="owner">The original condition leaf that owns the execution-scoped materialization cache entry.<br/></param>
            /// <returns>The materialized CLR value for this execution plan.<br/></returns>
            internal object? MaterializeParameterUntyped(
                object operand,
                Type valueType,
                string? propertyPath,
                string? parameterAlias = null,
                object? owner = null)
            {
                ArgumentNullException.ThrowIfNull(operand);
                ArgumentNullException.ThrowIfNull(valueType);
                Type wrapperType = typeof(ValueOrExpression<>).MakeGenericType(valueType);
                if (!wrapperType.IsInstanceOfType(operand))
                {
                    throw new ArgumentException(
                        $"Condition operand type '{operand.GetType()}' is not the expected '{wrapperType}'.",
                        nameof(operand));
                }

                try
                {
                    MethodInfo method = typeof(Builder)
                        .GetMethod(nameof(MaterializeParameterUntypedCore), BindingFlags.Instance | BindingFlags.NonPublic)!
                        .MakeGenericMethod(valueType);
                    return method.Invoke(this, new[] { operand, propertyPath, parameterAlias, owner });
                }
                catch (TargetInvocationException exception) when (exception.InnerException is not null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                    throw;
                }
            }

            /// <summary>
            /// Closes <see cref="MaterializeParameterUntyped(object, Type, string?, string?, object?)"/> over the exact wrapper type without changing normal typed materialization behavior.<br/>
            /// </summary>
            /// <typeparam name="TValue">The operand's exact declared CLR type.<br/></typeparam>
            /// <param name="operand">The verified typed wrapper boxed for reflection dispatch.<br/></param>
            /// <param name="propertyPath">The logical selector path associated with the operand.<br/></param>
            /// <param name="parameterAlias">The optional explicit parameter name.<br/></param>
            /// <param name="owner">The original condition leaf cache owner.<br/></param>
            /// <returns>The materialized typed value boxed as an object.<br/></returns>
            private object? MaterializeParameterUntypedCore<TValue>(
                object operand,
                string? propertyPath,
                string? parameterAlias,
                object? owner)
                => MaterializeParameter((ValueOrExpression<TValue>)operand, propertyPath, parameterAlias, owner);
            /// <summary>
            /// Caches one already-materialized operand for a specific semantic leaf so hybrid planning can translate that same leaf without reinvoking developer code or reraising hooks.<br/>
            /// Reference equality keeps identical values supplied to different condition leaves independent.<br/>
            /// </summary>
            /// <param name="owner">The condition operation instance that owns the operand.<br/></param>
            /// <param name="operand">The deferred operand wrapper.<br/></param>
            /// <param name="value">The execution-scoped final value.<br/></param>
            private void CacheExecutionMaterialization(object? owner, object operand, object? value)
            {
                if (owner is null)
                    return;

                if (!executionMaterializationCache.TryGetValue(owner, out var ownerValues))
                {
                    ownerValues = new Dictionary<object, object?>(ReferenceEqualityComparer.Instance);
                    executionMaterializationCache.Add(owner, ownerValues);
                }

                ownerValues[operand] = value;
            }

            /// <summary>
            /// Materializes one typed temporal operand through the ordinary replacement/event/cache pipeline and then applies the selected binary representation's value precision.<br/>
            /// Calendar SDT normalizes DateTime and TimeOnly values to its ten-tick quantum; Precision SDT, raw CLR layouts, ordered TimeSpan, DateOnly, and exact whole-tick values remain unchanged.<br/>
            /// </summary>
            /// <typeparam name="TValue">The CLR temporal operand type required by the consuming condition.<br/></typeparam>
            /// <param name="selector">The immutable selector whose descriptor declares the binary representation.<br/></param>
            /// <param name="operand">The immediate or deferred developer-supplied temporal operand.<br/></param>
            /// <param name="propertyPath">The source property path used by existing materialization hooks.<br/></param>
            /// <param name="parameterAlias">The optional named-parameter alias associated with this operand.<br/></param>
            /// <param name="owner">The optional operation owner used by the existing execution-scoped materialization cache.<br/></param>
            /// <returns>The materialized operand normalized to the value precision carried by the selected representation.<br/></returns>
            internal TValue MaterializeTemporalParameter<TValue>(
                ValueOrExpression<string> selector,
                ValueOrExpression<TValue> operand,
                string? propertyPath,
                string? parameterAlias = null,
                object? owner = null)
            {
                TValue materialized = MaterializeParameter(operand, propertyPath, parameterAlias, owner);
                if (selector.SelectorDescriptor is not BinarySliceSelectorDescriptor slice)
                    return materialized;

                object normalized = (slice.SliceType, materialized) switch
                {
                    (BinarySliceKind.DateTimeCalendarSdt, DateTime dateTime) =>
                        LibraDexTemporalScalarCodec.DecodeDateTime(
                            LibraDexTemporalScalarCodec.Encode(dateTime, DateTimeKeyEncoding.CalendarSdt),
                            DateTimeKeyEncoding.CalendarSdt),
                    (BinarySliceKind.TimeOnlyCalendarSdt, TimeOnly timeOnly) =>
                        LibraDexTemporalScalarCodec.DecodeTimeOnly(
                            LibraDexTemporalScalarCodec.Encode(timeOnly, DateTimeKeyEncoding.CalendarSdt),
                            DateTimeKeyEncoding.CalendarSdt),
                    (BinarySliceKind.DateTimeOffsetCalendarSdtUtc, DateTime dateTime) =>
                        LibraDexTemporalScalarCodec.DecodeDateTime(
                            LibraDexTemporalScalarCodec.Encode(dateTime, DateTimeKeyEncoding.CalendarSdt),
                            DateTimeKeyEncoding.CalendarSdt),
                    _ => materialized!
                };
                return (TValue)normalized;
            }

			private object? ResolveParameterValue(object? value, ParameterMetadata metadata, out string? effectivePropertyPath, out int? propertyPathOccurrence)
			{
				var resolved = ApplyValueReplacements(value);
				effectivePropertyPath = string.IsNullOrWhiteSpace(metadata.PropertyPath) ? null : metadata.PropertyPath;
				propertyPathOccurrence = null;

				if (!string.IsNullOrWhiteSpace(effectivePropertyPath))
				{
					var occurrence = RegisterPropPathOccurrence(effectivePropertyPath!);
					propertyPathOccurrence = occurrence;
					if (propPathOverrides.TryGetValue(effectivePropertyPath!, out var overrides))
					{
						foreach (var entry in overrides)
						{
							if (entry.AppliesToAll || (entry.Indexes is not null && entry.Indexes.Contains(occurrence)))
							{
								resolved = entry.Value;
							}
						}
					}
				}

				if (!string.IsNullOrWhiteSpace(metadata.Alias) && aliasOverrides.TryGetValue(metadata.Alias, out var aliasValue))
				{
					resolved = aliasValue;
				}

				return resolved;
			}

			private object? ApplyParameterMaterializationHooks(object? value, ParameterMetadata metadata, string? propertyPath, int? propertyPathOccurrence)
			{
				var context = parameterMaterializationContext;
				var condition = context?.Condition;
				if (condition is null)
					return value;

				var args = new ParameterMaterializedEventArgs(
					condition,
					context!.RecordStore,
					context.RecordBase,
					propertyPath,
					propertyPathOccurrence,
					metadata.Alias,
					value);

				condition.RaiseParameterMaterialized(args);
				var replacement = args.Value;
				if (replacement is null || value is null || value.GetType().IsInstanceOfType(replacement))
					return replacement;

				throw new InvalidOperationException($"Parameter materialization handler attempted to supply value of type '{replacement.GetType()}' but expected '{value.GetType()}'.");
			}

			private object? ApplyValueReplacements(object? value)
			{
				var current = value;
				foreach (var replacement in valueReplacements)
				{
					if (replacement.IsMatch(current))
					{
						current = replacement.Replacement;
					}
				}
				return current;
			}

			private int RegisterPropPathOccurrence(string propertyPath)
			{
				if (!propPathOccurrenceTracker.TryGetValue(propertyPath, out var current))
				{
					current = 0;
				}

				propPathOccurrenceTracker[propertyPath] = current + 1;
				return current;
			}

			private void AddPropPathOverride(string propPath, PropPathOverride entry)
			{
				if (!propPathOverrides.TryGetValue(propPath, out var list))
				{
					list = new List<PropPathOverride>();
					propPathOverrides[propPath] = list;
				}

				if (entry.AppliesToAll)
				{
					list.RemoveAll(o => o.AppliesToAll);
				}
				else if (entry.Indexes is not null)
				{
					list.RemoveAll(o => o.Indexes is not null && o.Indexes.SetEquals(entry.Indexes));
				}

				list.Add(entry);
			}

			/// <summary>
			/// Registers a named operand override and broadcasts it to nested builders; later materialization applies it without rebuilding the condition shape.<br/>
			/// </summary>
			/// <param name="alias">Non-empty parameter alias matched with ordinal name comparison.<br/></param>
			/// <param name="value">Replacement value, which must be compatible with the consuming operand type.<br/></param>
			/// <returns>This builder for continued condition authoring.<br/></returns>
			public Builder ChangeNamedParameter(string alias, object? value)
			{
				ArgumentException.ThrowIfNullOrWhiteSpace(alias);
				aliasOverrides[alias] = value;
				BroadcastToNestedBuilders(b => b.ChangeNamedParameter(alias, value));
				return this;
			}

			/// <summary>
			/// Registers a replacement for all materialization occurrences of the exact property path, including nested builders.<br/>
			/// </summary>
			/// <param name="propPath">Non-empty exact property-path key used by the materialization override rules.<br/></param>
			/// <param name="value">Replacement value, which must be compatible with the consuming operand type.<br/></param>
			/// <returns>This builder for continued condition authoring.<br/></returns>
			public Builder ChangeAllParamsByPropPath(string propPath, object? value)
			{
				ArgumentException.ThrowIfNullOrWhiteSpace(propPath);
				AddPropPathOverride(propPath, new PropPathOverride(value, null));
				BroadcastToNestedBuilders(b => b.ChangeAllParamsByPropPath(propPath, value));
				return this;
			}

			/// <summary>
			/// Registers a replacement for selected zero-based materialization occurrences of the exact property path, including nested builders.<br/>
			/// </summary>
			/// <param name="propPath">Non-empty exact property-path key used by the materialization override rules.<br/></param>
			/// <param name="value">Replacement value, which must be compatible with the consuming operand type.<br/></param>
			/// <param name="occurrenceIndexes">One or more non-negative, zero-based occurrence positions for this path.<br/></param>
			/// <returns>This builder for continued condition authoring.<br/></returns>
			public Builder ChangeParamsByPropPath(string propPath, object? value, params int[] occurrenceIndexes)
			{
				ArgumentException.ThrowIfNullOrWhiteSpace(propPath);
				if (occurrenceIndexes is null || occurrenceIndexes.Length == 0)
					throw new ArgumentException("At least one occurrence index must be provided.", nameof(occurrenceIndexes));
				if (occurrenceIndexes.Any(i => i < 0))
					throw new ArgumentOutOfRangeException(nameof(occurrenceIndexes), "Occurrence indexes must be zero or greater.");

				var indexSet = new HashSet<int>(occurrenceIndexes);
				AddPropPathOverride(propPath, new PropPathOverride(value, indexSet));
				BroadcastToNestedBuilders(b => b.ChangeParamsByPropPath(propPath, value, occurrenceIndexes));
				return this;
			}

			/// <summary>
			/// Registers a value-based replacement rule for future materialization and broadcasts it to nested builders.<br/>
			/// </summary>
			/// <param name="existingValue">Value matched by the replacement rule using object equality unless string comparison is supplied.<br/></param>
			/// <param name="newValue">Replacement value returned when the rule matches.<br/></param>
			/// <param name="comparison">Optional StringComparison used only when both compared values are strings; null uses ordinary object equality.<br/></param>
			/// <returns>This builder for continued condition authoring.<br/></returns>
			public Builder ReplaceParameterValue(object? existingValue, object? newValue, StringComparison? comparison = null)
			{
				valueReplacements.Add(new ValueReplacement(existingValue, newValue, comparison));
				BroadcastToNestedBuilders(b => b.ReplaceParameterValue(existingValue, newValue, comparison));
				return this;
			}

            internal int groupCounter = 0;
			internal void AddFragment(IConditionFragment fragment)
            {
                ConditionParameterDiscovery.RegisterFromFragment(Parameters, fragment);
                fragments.Add(fragment);
            }

            internal IReadOnlyList<IConditionFragment> Fragments => fragments;

            /// <summary>
            /// Gets the explicit parameters structurally referenced by this condition builder.<br/>
            /// Entries remain ordered by first appearance and exclude function-based deferred expressions.<br/>
            /// </summary>
            public ConditionParameterCollection Parameters { get; }

            ConditionParameterCollection IConditionParameterContainer.ExplicitParameters => Parameters;

			private readonly List<IConditionFragment> fragments;
			private readonly Dictionary<string, object?> aliasOverrides;
			private readonly Dictionary<string, List<PropPathOverride>> propPathOverrides;
			private readonly List<ValueReplacement> valueReplacements;
			private readonly Dictionary<string, int> propPathOccurrenceTracker;
            private readonly Dictionary<object, Dictionary<object, object?>> executionMaterializationCache = new(ReferenceEqualityComparer.Instance);
            private LibraDexParameterSnapshot executionParameterSnapshot = new();
			private ParameterMaterializationContext? parameterMaterializationContext;

			internal ParameterMaterializationContext? CurrentParameterContext => parameterMaterializationContext;

			private void BroadcastToNestedBuilders(Action<Builder> apply)
			{
				foreach (var fragment in fragments)
				{
					if (fragment is GroupConditionFragment group)
					{
						apply(group.GroupBuilder);
					}
				}
			}

			internal IDisposable PushParameterContext(EndCondition? condition, RecordStore<T>? recordStore, RecordBase? recordBase)
			{
				if (condition is null && recordStore is null && recordBase is null)
					return new ParameterContextScope(this, null);

				return new ParameterContextScope(this, new ParameterMaterializationContext(condition, recordStore, recordBase));
			}

			internal IDisposable PushParameterContext(ParameterMaterializationContext? context)
				=> new ParameterContextScope(this, context);

			private sealed record PropPathOverride(object? Value, HashSet<int>? Indexes)
			{
				public bool AppliesToAll => Indexes is null;
				public PropPathOverride Copy() => new(Value, Indexes is null ? null : new HashSet<int>(Indexes));
			}

			private sealed class ParameterContextScope : IDisposable
			{
				private readonly Builder builder;
				private readonly ParameterMaterializationContext? previous;
				private bool disposed;

				public ParameterContextScope(Builder builder, ParameterMaterializationContext? context)
				{
					this.builder = builder;
					previous = builder.parameterMaterializationContext;
					builder.parameterMaterializationContext = context;
				}

				public void Dispose()
				{
					if (disposed)
						return;

					builder.parameterMaterializationContext = previous;
					disposed = true;
				}
			}

			private sealed record ValueReplacement(object? MatchValue, object? Replacement, StringComparison? Comparison)
			{
				public bool IsMatch(object? value)
				{
					if (Comparison.HasValue && MatchValue is string match && value is string current)
					{
						return string.Equals(current, match, Comparison.Value);
					}

					return Equals(value, MatchValue);
				}
			}

			internal sealed record ParameterMaterializationContext(EndCondition? Condition, RecordStore<T>? RecordStore, RecordBase? RecordBase);

			/// <summary>
			/// Gets the builder's group-authoring marker; this is metadata and does not execute the group.<br/>
			/// </summary>
			public bool IsGroup { get; internal set; }

            private DateTime? executionUtcNow;

            internal TimeProvider Clock { get; set; } = TimeProvider.System;

            internal DateTime UtcNow => executionUtcNow ??= Clock.GetUtcNow().UtcDateTime;
        }
    }
}
