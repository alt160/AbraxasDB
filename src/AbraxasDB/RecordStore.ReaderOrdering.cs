using System;
using System.Collections.Generic;
using Inheto;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Prepares one explicit developer-requested property order without creating or mutating a persistent index.<br/>
        /// Candidate identities are validated and deduplicated once, key values are read directly from borrowed Fractal payload memory, and the compact identity result supports constant-time reader skipping.<br/>
        /// A single-key request avoids allocating one key array per row; multi-key requests allocate only the key vector required for comparison.<br/>
        /// </summary>
        /// <param name="recordIDs">Candidate identities to order.<br/></param>
        /// <param name="sort">One or more explicit property-order descriptors.<br/></param>
        /// <returns>A compact identity array in the requested order with identity ascending as the deterministic final tie-breaker.<br/></returns>
        private ulong[] PrepareTransientIdentityOrder(IEnumerable<ulong> recordIDs, SortBy[] sort)
        {
            ArgumentNullException.ThrowIfNull(recordIDs);
            ArgumentNullException.ThrowIfNull(sort);
            if (sort.Length == 0)
                throw new ArgumentException("At least one sort descriptor is required for transient ordering.", nameof(sort));

            for (int i = 0; i < sort.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(sort[i].Key.Path))
                    throw new ArgumentException("Sort descriptors must contain a property path.", nameof(sort));
            }

            var candidates = new List<ulong>();
            var seen = new HashSet<ulong>();
            foreach (ulong recordID in recordIDs)
            {
                EnsureRecordBelongsToCurrentStore(recordID, nameof(recordIDs));
                if (seen.Add(recordID))
                    candidates.Add(recordID);
            }

            return sort.Length == 1
                ? PrepareSingleKeyIdentityOrder(candidates, sort[0])
                : PrepareMultiKeyIdentityOrder(candidates, sort);
        }

        /// <summary>
        /// Prepares the allocation-lean single-property specialization of an ad hoc identity order.<br/>
        /// Each surviving row stores one identity and one boxed comparison value rather than a one-element object array.<br/>
        /// </summary>
        /// <param name="candidates">Distinct store-owned candidate identities.<br/></param>
        /// <param name="sort">The single requested order descriptor.<br/></param>
        /// <returns>Compact ordered identities.<br/></returns>
        private ulong[] PrepareSingleKeyIdentityOrder(List<ulong> candidates, SortBy sort)
        {
            var rows = new List<SingleKeyIdentitySortRow>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                ulong recordID = candidates[i];
                if (!rbase.data.TryGetOne(recordID, out var storedRecord))
                    continue;

                using (storedRecord)
                {
                    InhetoBinary payload = CreateRecordBinary(recordID, storedRecord.Data);
                    _ = TryReadLegacyAggregateValue(payload, sort.Key.Path, out object? key);
                    rows.Add(new SingleKeyIdentitySortRow(recordID, key));
                }
            }

            rows.Sort((left, right) => CompareSingleKeyIdentitySortRows(left, right, sort));
            var result = new ulong[rows.Count];
            for (int i = 0; i < rows.Count; i++)
                result[i] = rows[i].Identity;
            return result;
        }

        /// <summary>
        /// Prepares a multi-property ad hoc identity order with one compact comparison-key vector per surviving row.<br/>
        /// The complete sort is paid once when the reader opens; subsequent reads stream the identity array and skips move by ordinal.<br/>
        /// </summary>
        /// <param name="candidates">Distinct store-owned candidate identities.<br/></param>
        /// <param name="sort">Requested lexicographic order descriptors.<br/></param>
        /// <returns>Compact ordered identities.<br/></returns>
        private ulong[] PrepareMultiKeyIdentityOrder(List<ulong> candidates, SortBy[] sort)
        {
            var rows = new List<MultiKeyIdentitySortRow>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                ulong recordID = candidates[i];
                if (!rbase.data.TryGetOne(recordID, out var storedRecord))
                    continue;

                using (storedRecord)
                {
                    InhetoBinary payload = CreateRecordBinary(recordID, storedRecord.Data);
                    var keys = new object?[sort.Length];
                    for (int keyIndex = 0; keyIndex < sort.Length; keyIndex++)
                        _ = TryReadLegacyAggregateValue(payload, sort[keyIndex].Key.Path, out keys[keyIndex]);
                    rows.Add(new MultiKeyIdentitySortRow(recordID, keys));
                }
            }

            rows.Sort((left, right) => CompareMultiKeyIdentitySortRows(left, right, sort));
            var result = new ulong[rows.Count];
            for (int i = 0; i < rows.Count; i++)
                result[i] = rows[i].Identity;
            return result;
        }

        /// <summary>Compares two single-key transient rows and applies identity ascending as the final deterministic tie-breaker.<br/></summary>
        private static int CompareSingleKeyIdentitySortRows(
            SingleKeyIdentitySortRow left,
            SingleKeyIdentitySortRow right,
            SortBy sort)
        {
            int comparison = CompareTransientSortValue(left.Key, right.Key, sort);
            return comparison != 0 ? comparison : left.Identity.CompareTo(right.Identity);
        }

        /// <summary>Compares two multi-key transient rows lexicographically and applies identity ascending as the final deterministic tie-breaker.<br/></summary>
        private static int CompareMultiKeyIdentitySortRows(
            MultiKeyIdentitySortRow left,
            MultiKeyIdentitySortRow right,
            SortBy[] sort)
        {
            for (int i = 0; i < sort.Length; i++)
            {
                int comparison = CompareTransientSortValue(left.Keys[i], right.Keys[i], sort[i]);
                if (comparison != 0)
                    return comparison;
            }
            return left.Identity.CompareTo(right.Identity);
        }

        /// <summary>Compares one pair of transient scalar values under its explicit text and direction semantics.<br/></summary>
        private static int CompareTransientSortValue(object? left, object? right, SortBy sort)
        {
            int comparison = left is string leftText && right is string rightText
                ? string.Compare(leftText, rightText, sort.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                : CompareLibraDexSortKey(left, right);
            return sort.Descending ? -comparison : comparison;
        }

        private readonly record struct SingleKeyIdentitySortRow(ulong Identity, object? Key);
        private readonly record struct MultiKeyIdentitySortRow(ulong Identity, object?[] Keys);
    }
}
