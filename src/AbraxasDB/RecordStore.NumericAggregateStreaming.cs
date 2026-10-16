using System.Diagnostics;
using System.Numerics;
using LibraDex;

namespace AbraxasDB;

public partial class RecordStore<T>
{
    /// <summary>
    /// Reports opt-in measurements of the typed numeric index reduction.<br/>
    /// Counters cover cursor traversal, not selection preparation or caller-side work.<br/>
    /// No inverse map is built implicitly; an already-ready map may serve selected integral Sum requests.<br/>
    /// Otherwise selected identities are tested against streamed keys.<br/>
    /// </summary>
    public event EventHandler<NumericAggregateExecutionEventArgs>? NumericAggregateExecutionCompleted;

    /// <summary>
    /// Dispatches supported scalar numeric indexes once, outside the reader loop.<br/>
    /// Unsupported/dynamic keys and extrema retain their existing compatibility behavior.<br/>
    /// </summary>
    /// <param name="index">Resolved maintained index.<br/></param>
    /// <param name="selected">Identity set, or null for every tuple.<br/></param>
    /// <param name="kind">Requested legacy operation.<br/></param>
    /// <param name="path">Normalized property path for diagnostics.<br/></param>
    /// <param name="operationName">Public operation for diagnostics.<br/></param>
    /// <param name="result">Completed legacy result when supported.<br/></param>
    /// <returns>Whether typed reduction handled this index and operation.<br/></returns>
    private bool TryExecuteTypedNumericPathAggregate(IIndex index, HashSet<ulong>? selected,
        LegacyPathAggregateKind kind, string path, string operationName, out object? result)
    {
        result = null;
        if (kind is not (LegacyPathAggregateKind.Sum or LegacyPathAggregateKind.Average))
            return false;
        switch (index)
        {
                case LibraDexIndex<byte, ulong> typed0: result = ExecuteTypedNumericPathAggregate(typed0, selected, kind, path, operationName); return true;
                case LibraDexIndex<sbyte, ulong> typed1: result = ExecuteTypedNumericPathAggregate(typed1, selected, kind, path, operationName); return true;
                case LibraDexIndex<short, ulong> typed2: result = ExecuteTypedNumericPathAggregate(typed2, selected, kind, path, operationName); return true;
                case LibraDexIndex<ushort, ulong> typed3: result = ExecuteTypedNumericPathAggregate(typed3, selected, kind, path, operationName); return true;
                case LibraDexIndex<int, ulong> typed4: result = ExecuteTypedNumericPathAggregate(typed4, selected, kind, path, operationName); return true;
                case LibraDexIndex<uint, ulong> typed5: result = ExecuteTypedNumericPathAggregate(typed5, selected, kind, path, operationName); return true;
                case LibraDexIndex<long, ulong> typed6: result = ExecuteTypedNumericPathAggregate(typed6, selected, kind, path, operationName); return true;
                case LibraDexIndex<ulong, ulong> typed7: result = ExecuteTypedNumericPathAggregate(typed7, selected, kind, path, operationName); return true;
                case LibraDexIndex<Half, ulong> typed8: result = ExecuteTypedNumericPathAggregate(typed8, selected, kind, path, operationName); return true;
                case LibraDexIndex<float, ulong> typed9: result = ExecuteTypedNumericPathAggregate(typed9, selected, kind, path, operationName); return true;
                case LibraDexIndex<double, ulong> typed10: result = ExecuteTypedNumericPathAggregate(typed10, selected, kind, path, operationName); return true;
                case LibraDexIndex<decimal, ulong> typed11: result = ExecuteTypedNumericPathAggregate(typed11, selected, kind, path, operationName); return true;
                case LibraDexIndex<Int128, ulong> typed12: result = ExecuteTypedNumericPathAggregate(typed12, selected, kind, path, operationName); return true;
                case LibraDexIndex<UInt128, ulong> typed13: result = ExecuteTypedNumericPathAggregate(typed13, selected, kind, path, operationName); return true;
                case LibraDexIndex<System.Numerics.BigInteger, ulong> typed14: result = ExecuteTypedNumericPathAggregate(typed14, selected, kind, path, operationName); return true;
            default: return false;
        }
    }

    /// <summary>
    /// Reduces ordinary non-null numeric keys using LibraDex's typed physical cursor.<br/>
    /// Scalar null routes are intentionally omitted because null contributes to neither Sum nor Average.<br/>
    /// Selection retains set semantics; every selected physical tuple contributes, including multiple keys for one identity.<br/>
    /// An already-ready inverse map may answer selected integral Sum without a forward scan.<br/>
    /// The cursor is disposed on completion or arithmetic failure, and no full tuple collection, universal condition,
    /// new inverse map, or per-row key/identity boxing is introduced.<br/>
    /// </summary>
    /// <typeparam name="TNumber">Native numeric key type.<br/></typeparam>
    /// <param name="index">Index owning the typed reader.<br/></param>
    /// <param name="selected">Optional identity filter; an empty set opens no reader.<br/></param>
    /// <param name="kind">Sum or Average.<br/></param>
    /// <param name="path">Diagnostic property path.<br/></param>
    /// <param name="operationName">Diagnostic operation name.<br/></param>
    /// <returns>The legacy unsigned, checked aggregate result.<br/></returns>
    private object? ExecuteTypedNumericPathAggregate<TNumber>(LibraDexIndex<TNumber, ulong> index,
        HashSet<ulong>? selected, LegacyPathAggregateKind kind, string path, string operationName)
        where TNumber : INumberBase<TNumber>
    {
        var handler = NumericAggregateExecutionCompleted;
        long start = handler is null ? 0 : Stopwatch.GetTimestamp();
        long allocated = handler is null ? 0 : GC.GetAllocatedBytesForCurrentThread();
        long examined = 0, matched = 0, lookups = 0;
        var accumulator = new LegacyPathAggregateAccumulator(kind);
        string route = selected is { Count: 0 } ? "Empty selection" : "LibraDex typed numeric cursor";
        // At most Int32.MaxValue distinct identities times any UInt64 value fits in Decimal.
        // Thus integral Sum can change visitation order without changing checked arithmetic semantics.
        // Decimal/float sums and incremental Average retain ordered cursor reduction.
        bool inverse = selected is { Count: > 0 } && kind == LegacyPathAggregateKind.Sum &&
            (typeof(TNumber) == typeof(byte) || typeof(TNumber) == typeof(sbyte) ||
             typeof(TNumber) == typeof(short) || typeof(TNumber) == typeof(ushort) ||
             typeof(TNumber) == typeof(int) || typeof(TNumber) == typeof(uint) ||
             typeof(TNumber) == typeof(long) || typeof(TNumber) == typeof(ulong)) &&
            index.TryVisitReadySingleKeys(selected, key => accumulator.AddNumeric(decimal.CreateChecked(key)), out matched);
        if (inverse)
        {
            route = "LibraDex ready inverse identity lookup";
            lookups = selected!.Count;
        }
        else if (selected is not { Count: 0 })
        {
            using var reader = index.OpenReader();
            while (reader.TryReadNext(out TNumber key, out ulong identity))
            {
                if (handler is not null) examined++;
                if (selected is not null && !selected.Contains(identity)) continue;
                accumulator.AddNumeric(decimal.CreateChecked(key));
                if (handler is not null) matched++;
            }
        }
        object? result = accumulator.Complete();
        if (handler is not null)
            handler(this, new NumericAggregateExecutionEventArgs(operationName, path,
                route,
                selected?.Count, examined, matched, Stopwatch.GetElapsedTime(start),
                GC.GetAllocatedBytesForCurrentThread() - allocated, lookups));
        return result;
    }
}

/// <summary>
/// Immutable measurements for one completed typed numeric index reduction.<br/>
/// Allocation is current-thread managed allocation for the reduction only, excluding this event payload.<br/>
/// RowsExamined counts ordinary non-null index tuples, not authoritative Fractal reads.<br/>
/// </summary>
public sealed class NumericAggregateExecutionEventArgs(
    string operation, string propertyPath, string route, int? selectedIdentityCount,
    long rowsExamined, long rowsMatched, TimeSpan duration, long allocatedBytes, long identityLookups = 0) : EventArgs
{
    /// <summary>Public operation name.<br/></summary>
    public string Operation { get; } = operation;
    /// <summary>Normalized aggregate property path.<br/></summary>
    public string PropertyPath { get; } = propertyPath;
    /// <summary>Actual reduction route.<br/></summary>
    public string Route { get; } = route;
    /// <summary>Distinct selected identities; null denotes all identities.<br/></summary>
    public int? SelectedIdentityCount { get; } = selectedIdentityCount;
    /// <summary>Ordinary tuples visited by the physical reader.<br/></summary>
    public long RowsExamined { get; } = rowsExamined;
    /// <summary>Tuples included in numeric reduction.<br/></summary>
    public long RowsMatched { get; } = rowsMatched;
    /// <summary>Elapsed reduction time excluding selection preparation.<br/></summary>
    public TimeSpan Duration { get; } = duration;
    /// <summary>Current-thread managed bytes allocated by reduction.<br/></summary>
    public long AllocatedBytes { get; } = allocatedBytes;
    /// <summary>Selected identity probes against a previously complete inverse map; zero on a cursor scan.<br/></summary>
    public long IdentityLookups { get; } = identityLookups;
}
