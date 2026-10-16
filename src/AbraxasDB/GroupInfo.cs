using System;
using System.Collections.Generic;
using System.Threading;

namespace AbraxasDB;

public partial class RecordStore<T>
{


    /// <summary>
    /// Describes a single grouping bucket produced by a grouped query.
    /// Holds the group key values plus the total record count for that bucket.
    ///
    /// The snapshot accessors are retained compatibility members; current grouping and drilldown operations do not populate them.<br/>
    /// </summary>
    public sealed class GroupInfo
    {
        private const string DefaultSnapshotName = "__default__";

        private readonly object snapshotGate = new();
        private readonly Dictionary<string, SnapshotState> snapshots = new(StringComparer.Ordinal);

        internal GroupInfo(
            RecordStore<T> store,
            Condition<T>.EndCondition? where,
            IReadOnlyList<string> groupBy,
            IReadOnlyList<object?> keyValues,
            ulong count,
            double? sum,
            double? average,
            object? min,
            object? max,
            ulong? distinctCount,
            string? name = null)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
            GroupBy = groupBy ?? throw new ArgumentNullException(nameof(groupBy));
            KeyValues = keyValues ?? throw new ArgumentNullException(nameof(keyValues));

            if (GroupBy.Count != KeyValues.Count)
                throw new ArgumentException("GroupBy and KeyValues must have the same number of elements.");

            Where = where is null ? null : new Condition<T>.EndCondition(where.builder.Clone());
            Count = count;
            Sum = sum;
            Average = average;
            Min = min;
            Max = max;
            DistinctCount = distinctCount;

            Identity = Guid.NewGuid();
            Signature = CreateSignature(GroupBy, KeyValues);
            Name = name ?? string.Empty;
        }

        /// <summary>
        /// The record store instance that produced this group.
        /// </summary>
        public RecordStore<T> Store { get; }

        /// <summary>
        /// Base filter used to produce the grouped result-set this group belongs to.
        /// </summary>
        public Condition<T>.EndCondition? Where { get; }

        /// <summary>
        /// Group-by key identifiers (tokenized) used to form this group (in key order).
        /// </summary>
        public IReadOnlyList<string> GroupBy { get; }

        /// <summary>
        /// Group key values for this group (aligned with <see cref="GroupBy"/>).
        /// </summary>
        public IReadOnlyList<object?> KeyValues { get; }

        /// <summary>
        /// Total number of records counted for this group.
        /// </summary>
        public ulong Count { get; }

        /// <summary>
        /// Gets the optional sum computed for this group; it does not trigger aggregation when accessed.<br/>
        /// </summary>
        public double? Sum { get; }

        /// <summary>
        /// Gets the optional average computed for this group; it does not trigger aggregation when accessed.<br/>
        /// </summary>
        public double? Average { get; }

        /// <summary>
        /// Gets the optional minimum computed for this group in its result value type.<br/>
        /// </summary>
        public object? Min { get; }

        /// <summary>
        /// Gets the optional maximum computed for this group in its result value type.<br/>
        /// </summary>
        public object? Max { get; }

        /// <summary>
        /// Gets the optional distinct-value count computed for this group.<br/>
        /// </summary>
        public ulong? DistinctCount { get; }

        /// <summary>
        /// Developer-controlled label for the group. Intended for UI/logging, not identity.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Unique identity for this specific <see cref="GroupInfo"/> instance.
        /// Useful for UI keys and correlating application-owned group state.<br/>
        /// </summary>
        public Guid Identity { get; }

        /// <summary>
        /// Deterministic signature derived from <see cref="GroupBy"/> and <see cref="KeyValues"/>.
        /// This can be used as a stable key within or across result sets.
        /// </summary>
        public string Signature { get; }

        /// <summary>
        /// Gets the retained compatibility snapshot for this group.<br/>
        /// Current grouping and drilldown operations do not capture record IDs here, so this returns an empty list.<br/>
        /// </summary>
        public IReadOnlyList<ulong> RecordIdSnapshot => TryGetRecordIdSnapshot(out var snapshot) ? snapshot : Array.Empty<ulong>();

        /// <summary>
        /// Gets the completion state of the retained compatibility snapshot.<br/>
        /// Current grouping and drilldown operations do not populate this snapshot, so this returns false.<br/>
        /// </summary>
        public bool RecordIdSnapshotIsComplete => TryGetRecordIdSnapshotMetadata(out var meta) && meta.IsComplete;

        /// <summary>
        /// Tries to get a named compatibility snapshot.<br/>
        /// Current grouping and drilldown operations do not populate these snapshots; no captured list is available.<br/>
        /// </summary>
        public bool TryGetRecordIdSnapshot(string snapshotName, out IReadOnlyList<ulong> recordIds)
        {
            if (string.IsNullOrWhiteSpace(snapshotName))
                snapshotName = DefaultSnapshotName;

            lock (snapshotGate)
            {
                if (snapshots.TryGetValue(snapshotName, out var state) && state.Buffer is not null)
                {
                    recordIds = state.Buffer;
                    return true;
                }
            }

            recordIds = Array.Empty<ulong>();
            return false;
        }

        /// <summary>
        /// Tries to get metadata for a named compatibility snapshot.<br/>
        /// Current grouping and drilldown operations do not populate these snapshots; no captured metadata is available.<br/>
        /// </summary>
        public bool TryGetRecordIdSnapshotMetadata(string snapshotName, out RecordIdSnapshotMetadata metadata)
        {
            if (string.IsNullOrWhiteSpace(snapshotName))
                snapshotName = DefaultSnapshotName;

            lock (snapshotGate)
            {
                if (snapshots.TryGetValue(snapshotName, out var state))
                {
                    metadata = new RecordIdSnapshotMetadata(state.Count, state.IsComplete);
                    return true;
                }
            }

            metadata = default;
            return false;
        }

        /// <summary>
        /// Clears the default snapshot.
        /// </summary>
        public void ClearRecordIdSnapshot() => ClearRecordIdSnapshot(DefaultSnapshotName);

        /// <summary>
        /// Clears a named snapshot.
        /// </summary>
        public void ClearRecordIdSnapshot(string snapshotName)
        {
            if (string.IsNullOrWhiteSpace(snapshotName))
                snapshotName = DefaultSnapshotName;

            lock (snapshotGate)
            {
                snapshots.Remove(snapshotName);
            }
        }

        /// <summary>
        /// Clears all snapshots currently held by this group.
        /// </summary>
        public void ClearAllRecordIdSnapshots()
        {
            lock (snapshotGate)
            {
                snapshots.Clear();
            }
        }

        private bool TryGetRecordIdSnapshot(out IReadOnlyList<ulong> recordIds)
        {
            lock (snapshotGate)
            {
                if (snapshots.TryGetValue(DefaultSnapshotName, out var state) && state.Buffer is not null)
                {
                    recordIds = state.Buffer;
                    return true;
                }
            }

            recordIds = Array.Empty<ulong>();
            return false;
        }

        private bool TryGetRecordIdSnapshotMetadata(out RecordIdSnapshotMetadata meta)
        {
            lock (snapshotGate)
            {
                if (snapshots.TryGetValue(DefaultSnapshotName, out var state))
                {
                    meta = new RecordIdSnapshotMetadata(state.Count, state.IsComplete);
                    return true;
                }
            }

            meta = default;
            return false;
        }

        private static string CreateSignature(IReadOnlyList<string> groupBy, IReadOnlyList<object?> keyValues)
        {
            var parts = new string[groupBy.Count];
            for (var i = 0; i < groupBy.Count; i++)
            {
                var path = groupBy[i];
                var value = keyValues[i];
                var valueStr = value is null ? "<null>" : value.ToString();
                parts[i] = $"{path}={valueStr}";
            }

            return string.Join("|", parts);
        }

        private struct SnapshotState(List<ulong>? buffer, bool isComplete)
        {
            public List<ulong>? Buffer = buffer;
            public int Count = 0;
            public bool IsComplete = isComplete;
        }

        /// <summary>
        /// Describes the number of identities observed in a retained drilldown snapshot and whether enumeration reached its end.<br/>
        /// </summary>
        /// <param name="Count">Number of record identities captured in this snapshot, not the original group's total record count.<br/></param>
        /// <param name="IsComplete">Whether the snapshot enumeration completed rather than stopping early; this does not establish freshness against later store mutations.<br/></param>
        public readonly record struct RecordIdSnapshotMetadata(int Count, bool IsComplete);
    }
}
