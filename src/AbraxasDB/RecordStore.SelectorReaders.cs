using System;
using System.Runtime.CompilerServices;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Opens a replayable forward-only reader that evaluates one typed selector without hydrating the containing record object.<br/>
        /// The selector result type is inferred from the selector itself and is limited to Abraxas-supported scalar families.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact scalar result type exposed by the selector.<br/></typeparam>
        /// <param name="selector">Direct or computed selector evaluated against each borrowed Fractal/Inheto payload.<br/></param>
        /// <returns>A reader positioned before the first selected value.<br/></returns>
        public DataReader<TValue?> OpenValuesReader<TValue>(Selector<T>.IValue<TValue> selector)
            => OpenSelectorReader(CreateAllRowPlan(), selector, withIds: false);

        /// <summary>
        /// Opens a replayable forward-only selector reader over records matching a completed condition and optional ordering.<br/>
        /// LibraDex supplies candidate identities where possible while selector evaluation consumes the authoritative borrowed Fractal/Inheto payload.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact scalar result type exposed by the selector.<br/></typeparam>
        /// <param name="where">Completed condition selecting records.<br/></param>
        /// <param name="selector">Direct or computed selector evaluated for each selected record.<br/></param>
        /// <param name="sort">Optional property-path ordering.<br/></param>
        /// <returns>A reader positioned before the first selected value.<br/></returns>
        public DataReader<TValue?> OpenValuesReader<TValue>(
            Condition<T>.EndCondition where,
            Selector<T>.IValue<TValue> selector,
            params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(where);
            return OpenSelectorReader(CreateConditionRowPlan(where, sort), selector, withIds: false);
        }

        /// <summary>
        /// Opens a replayable forward-only selector reader over records matching persistent State and an optional mask.<br/>
        /// Selector evaluation reads only the required value path and does not hydrate the record object.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact scalar result type exposed by the selector.<br/></typeparam>
        /// <param name="state">Persistent State value to match.<br/></param>
        /// <param name="selector">Direct or computed selector evaluated for each selected record.<br/></param>
        /// <param name="mask">Optional mask applying <c>(State &amp; mask) == state</c>.<br/></param>
        /// <returns>A reader positioned before the first selected value.<br/></returns>
        public DataReader<TValue?> OpenValuesReader<TValue>(
            long state,
            Selector<T>.IValue<TValue> selector,
            long? mask = null)
            => OpenSelectorReader(CreateStateRowPlan(state, mask), selector, withIds: false);

        /// <summary>
        /// Opens a replayable forward-only reader that evaluates one typed selector and includes each record identity.<br/>
        /// The selector result type is inferred without exposing unrestricted target-type conversion.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact scalar result type exposed by the selector.<br/></typeparam>
        /// <param name="selector">Direct or computed selector evaluated against each borrowed payload.<br/></param>
        /// <returns>A reader positioned before the first identity and selected value.<br/></returns>
        public DataReader<(ulong ID, TValue? Value)> OpenValuesReaderWithIds<TValue>(Selector<T>.IValue<TValue> selector)
            => OpenSelectorReaderWithIds(CreateAllRowPlan(), selector);

        /// <summary>
        /// Opens a replayable identity-aware selector reader over records matching a completed condition and optional ordering.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact scalar result type exposed by the selector.<br/></typeparam>
        /// <param name="where">Completed condition selecting records.<br/></param>
        /// <param name="selector">Direct or computed selector evaluated for each selected record.<br/></param>
        /// <param name="sort">Optional property-path ordering.<br/></param>
        /// <returns>A reader positioned before the first identity and selected value.<br/></returns>
        public DataReader<(ulong ID, TValue? Value)> OpenValuesReaderWithIds<TValue>(
            Condition<T>.EndCondition where,
            Selector<T>.IValue<TValue> selector,
            params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(where);
            return OpenSelectorReaderWithIds(CreateConditionRowPlan(where, sort), selector);
        }

        /// <summary>
        /// Opens a replayable identity-aware selector reader over records matching persistent State and an optional mask.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact scalar result type exposed by the selector.<br/></typeparam>
        /// <param name="state">Persistent State value to match.<br/></param>
        /// <param name="selector">Direct or computed selector evaluated for each selected record.<br/></param>
        /// <param name="mask">Optional mask applying <c>(State &amp; mask) == state</c>.<br/></param>
        /// <returns>A reader positioned before the first identity and selected value.<br/></returns>
        public DataReader<(ulong ID, TValue? Value)> OpenValuesReaderWithIds<TValue>(
            long state,
            Selector<T>.IValue<TValue> selector,
            long? mask = null)
            => OpenSelectorReaderWithIds(CreateStateRowPlan(state, mask), selector);

        /// <summary>
        /// Opens a replayable reader that evaluates two independently typed selectors during one payload visit.<br/>
        /// This is a reader result shape, not a tuple-valued selector, so each component retains its own scalar semantics and possible index identity.<br/>
        /// </summary>
        /// <typeparam name="TValue1">Exact first selector result type.<br/></typeparam>
        /// <typeparam name="TValue2">Exact second selector result type.<br/></typeparam>
        /// <param name="selector1">First direct or computed selector.<br/></param>
        /// <param name="selector2">Second direct or computed selector.<br/></param>
        /// <returns>A reader positioned before the first pair of selected values.<br/></returns>
        public DataReader<(TValue1? Value1, TValue2? Value2)> OpenValuesReader<TValue1, TValue2>(
            Selector<T>.IValue<TValue1> selector1,
            Selector<T>.IValue<TValue2> selector2)
            => OpenSelectorPairReader(CreateAllRowPlan(), selector1, selector2, withIds: false);

        /// <summary>
        /// Opens a replayable two-selector reader over records matching a completed condition and optional ordering.<br/>
        /// Both selectors are evaluated from the same borrowed payload visit.<br/>
        /// </summary>
        /// <typeparam name="TValue1">Exact first selector result type.<br/></typeparam>
        /// <typeparam name="TValue2">Exact second selector result type.<br/></typeparam>
        /// <param name="where">Completed condition selecting records.<br/></param>
        /// <param name="selector1">First direct or computed selector.<br/></param>
        /// <param name="selector2">Second direct or computed selector.<br/></param>
        /// <param name="sort">Optional property-path ordering.<br/></param>
        /// <returns>A reader positioned before the first pair of selected values.<br/></returns>
        public DataReader<(TValue1? Value1, TValue2? Value2)> OpenValuesReader<TValue1, TValue2>(
            Condition<T>.EndCondition where,
            Selector<T>.IValue<TValue1> selector1,
            Selector<T>.IValue<TValue2> selector2,
            params SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(where);
            return OpenSelectorPairReader(CreateConditionRowPlan(where, sort), selector1, selector2, withIds: false);
        }

        /// <summary>
        /// Creates the single-selector reader after validating and compiling its scalar evaluator once.<br/>
        /// </summary>
        private DataReader<TValue?> OpenSelectorReader<TValue>(
            ReaderSourcePlan sourcePlan,
            Selector<T>.IValue<TValue> selector,
            bool withIds)
        {
            ArgumentNullException.ThrowIfNull(selector);
            if (!TryCreateStoreSelectorReader<TValue>(selector, out var readValue))
                throw new NotSupportedException($"Selector result '{typeof(TValue).FullName}' does not have an executable Abraxas scalar reader.");
            return new DataReader<TValue?>(
                this,
                sourcePlan,
                (binary, id) =>
                {
                    (bool hasValue, TValue value) = readValue(binary);
                    return hasValue ? value : default;
                },
                BuildSelectorReaderShape(typeof(TValue), withIds, selector));
        }

        /// <summary>
        /// Creates the identity-aware single-selector reader after compiling its evaluator once.<br/>
        /// </summary>
        private DataReader<(ulong ID, TValue? Value)> OpenSelectorReaderWithIds<TValue>(
            ReaderSourcePlan sourcePlan,
            Selector<T>.IValue<TValue> selector)
        {
            ArgumentNullException.ThrowIfNull(selector);
            if (!TryCreateStoreSelectorReader<TValue>(selector, out var readValue))
                throw new NotSupportedException($"Selector result '{typeof(TValue).FullName}' does not have an executable Abraxas scalar reader.");
            return new DataReader<(ulong ID, TValue? Value)>(
                this,
                sourcePlan,
                (binary, id) =>
                {
                    (bool hasValue, TValue value) = readValue(binary);
                    return (id, hasValue ? value : default);
                },
                BuildSelectorReaderShape(typeof(TValue), withIds: true, selector));
        }

        /// <summary>
        /// Creates the two-selector reader after compiling both scalar evaluators once outside the hot row loop.<br/>
        /// </summary>
        private DataReader<(TValue1? Value1, TValue2? Value2)> OpenSelectorPairReader<TValue1, TValue2>(
            ReaderSourcePlan sourcePlan,
            Selector<T>.IValue<TValue1> selector1,
            Selector<T>.IValue<TValue2> selector2,
            bool withIds)
        {
            ArgumentNullException.ThrowIfNull(selector1);
            ArgumentNullException.ThrowIfNull(selector2);
            if (!TryCreateStoreSelectorReader<TValue1>(selector1, out var read1) ||
                !TryCreateStoreSelectorReader<TValue2>(selector2, out var read2))
                throw new NotSupportedException("One or more selectors do not have an executable Abraxas scalar reader.");
            return new DataReader<(TValue1? Value1, TValue2? Value2)>(
                this,
                sourcePlan,
                (binary, id) =>
                {
                    (bool has1, TValue1 value1) = read1(binary);
                    (bool has2, TValue2 value2) = read2(binary);
                    return (has1 ? value1 : default, has2 ? value2 : default);
                },
                BuildSelectorReaderShape(typeof((TValue1, TValue2)), withIds, selector1, selector2));
        }

        /// <summary>
        /// Builds a bookmark result-shape identity from durable selector fingerprints when possible and instance identity for runtime-only selectors.<br/>
        /// Runtime identity deliberately prevents a bookmark from being replayed through unrelated application delegates that happen to render alike.<br/>
        /// </summary>
        private static string BuildSelectorReaderShape(Type resultType, bool withIds, params ISelector[] selectors)
        {
            var identities = new string[selectors.Length];
            for (int i = 0; i < selectors.Length; i++)
            {
                try
                {
                    identities[i] = SelectorDescriptor<T>.FromSelector(selectors[i]).SemanticFingerprint;
                }
                catch (NotSupportedException)
                {
                    identities[i] = $"runtime:{RuntimeHelpers.GetHashCode(selectors[i]):x8}";
                }
            }
            return BuildTypedValuesBookmarkShape(resultType, withIds, identities);
        }
    }
}
