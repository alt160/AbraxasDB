using LibraDex;

namespace AbraxasDB;

public partial class RecordStore<T>
{
    public sealed partial class AggregateFacade
    {
        /// <summary>Reads a typed grouping key from borrowed composite parts without materializing a key container.<br/></summary>
        internal delegate ReadValue<TKey> CompositeKeyRead<TKey>(AggregateCompositeValues values);

        /// <summary>Preserves structural key planning independently of the payload reader's single-property path.<br/></summary>
        internal abstract class IndexedGroupingSource<TKey>
        {
            /// <summary>Prepares a lazy complete scalar-index source, or declines without reading data when semantics are unsupported.<br/></summary>
            internal abstract IEnumerable<(TKey Key, ulong Identity)>? Prepare(RecordStore<T> store);

            /// <summary>Matches an existing composite's grouping prefix and returns a borrowed-part key reader when compatible.<br/></summary>
            internal abstract CompositeKeyRead<TKey>? MatchComposite(LibraDexCompositeIndex index);
        }

        /// <summary>Retains two distinct direct-property identities and joins their participating records with typed keys.<br/>
        /// Query-local lookup state avoids new persistent indexes and cache-invalidation machinery.<br/></summary>
        internal sealed class TwoKeyGroupingSource<TKey1, TKey2> : IndexedGroupingSource<AggregateKey<TKey1, TKey2>>
            where TKey1 : notnull
            where TKey2 : notnull
        {
            private readonly string? firstPath;
            private readonly string? secondPath;

            /// <summary>Captures each component path separately; null paths retain authoritative fallback for calculated selectors.<br/></summary>
            internal TwoKeyGroupingSource(string? firstPath, string? secondPath)
            {
                this.firstPath = firstPath; this.secondPath = secondPath;
            }

            /// <summary>Requires two complete maintained direct scalar indexes and leaves unsupported definitions unchanged.<br/>
            /// Actual map construction is deferred until enumeration, inside the aggregate's operation lease.<br/></summary>
            internal override IEnumerable<(AggregateKey<TKey1, TKey2> Key, ulong Identity)>? Prepare(RecordStore<T> store)
            {
                if (firstPath is null || secondPath is null ||
                    !store.TryPrepareAggregateDefinitionIndexedTraversal(firstPath, typeof(TKey1), null, out var group, out var first, out _) ||
                    !store.TryPrepareAggregateDefinitionIndexedTraversal(secondPath, typeof(TKey2), null, out _, out var second, out _) ||
                    !IsDirectScalarAggregateGroupingIndex(first) || !IsDirectScalarAggregateGroupingIndex(second)) return null;
                return Join(group, first.Index, second.Index);
            }

            /// <summary>Admits an exact ordered two-part prefix without conflating pair grouping with scalar grouping.<br/>
            /// The composite reducer separately proves coverage for every requested value.<br/></summary>
            internal override CompositeKeyRead<AggregateKey<TKey1, TKey2>>? MatchComposite(LibraDexCompositeIndex index)
            {
                if (firstPath is null || secondPath is null ||
                    !MatchesAggregateCompositePart(index, 0, firstPath, typeof(TKey1)) ||
                    !MatchesAggregateCompositePart(index, 1, secondPath, typeof(TKey2))) return null;
                return ReadCompositeKey;
            }

            /// <summary>Reads both borrowed keys and excludes a record when either component is absent or null.<br/></summary>
            private static ReadValue<AggregateKey<TKey1, TKey2>> ReadCompositeKey(AggregateCompositeValues values)
            {
                var first = ReadCompositeValue<TKey1>(values, 0);
                var second = ReadCompositeValue<TKey2>(values, 1);
                return first.HasValue && second.HasValue
                    ? new(true, new(first.Value, second.Value)) : default;
            }

            /// <summary>Builds one query-local identity map, then consumes each matched identity once.<br/>
            /// Removing a matched entry performs both the join and duplicate suppression without a second identity set.<br/>
            /// Null keys never participate; equal keys on distinct records remain distinct contributions.<br/>
            /// Conflicting second keys violate scalar-index integrity and fail rather than selecting an arbitrary value.<br/></summary>
            private static IEnumerable<(AggregateKey<TKey1, TKey2> Key, ulong Identity)> Join(CatalogIdentityGroupIndexes group, IIndex first, IIndex second)
            {
                var secondByIdentity = new Dictionary<ulong, TKey2>();
                foreach (var tuple in group.IterateTuples(second))
                {
                    if (tuple.Key is null) continue;
                    if (tuple.Key is not TKey2 key || tuple.Identity is not ulong identity) throw new InvalidDataException("Two-key grouping index returned an incompatible second key or identity.");
                    if (!secondByIdentity.TryAdd(identity, key) && !EqualityComparer<TKey2>.Default.Equals(secondByIdentity[identity], key))
                        throw new InvalidDataException("A scalar grouping index contains conflicting keys for one record identity.");
                }
                foreach (var tuple in group.IterateTuples(first))
                {
                    if (tuple.Key is null) continue;
                    if (tuple.Key is not TKey1 key || tuple.Identity is not ulong identity) throw new InvalidDataException("Two-key grouping index returned an incompatible first key or identity.");
                    if (secondByIdentity.Remove(identity, out var next)) yield return (new(key, next), identity);
                }
            }
        }
    }
}
