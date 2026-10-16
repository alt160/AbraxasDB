# Open-source edition

[Guide home](Home.md)

The OSS edition retains its compiled structural capacity:

| Boundary | OSS edition |
| --- | --- |
| Application stores in one RecordBase | Up to 15; store identity zero is reserved. |
| Per-store record-identity counter | 19 bits; maximum counter value 524,287. |
| Individual serialized record size | Below approximately 2 GiB, including required overhead, and subject to available memory; see [individual record size](#individual-record-size). |
| Built-in record-payload compression | Not included. |
| Built-in record-payload encryption | Not included. |

Record identities advance monotonically. This is an identity-space boundary, not a promise of exactly 524,287 simultaneously live records: block reservation, unused reserved identities, and reopen behavior can consume capacity, and deleting records does not reset the generator. Capacity exhaustion fails rather than wrapping identities or silently overwriting an existing record.

Read the compiled limits without opening a database through `RecordBase.MaximumStoreCount` and `RecordBase.MaximumRecordCounter`. These report structural limits, not remaining capacity or the current live-record count.

## Capacity in practice

**OSS does not impose a small database-byte quota.** The store and identity limits are separate from total payload size. Even records averaging a few KiB can add up to millions of records and substantial local datasets across a RecordBase's stores.

For example, an order containing 25 line items with 50 distinct text values averaging 80 UTF-8 bytes contains about 4,000 bytes of text alone. Item identities, quantities, prices, customer details, and structural metadata can bring the serialized record beyond 4 KiB without any unusually large individual value.

The following sizing examples assume the same record count in each of 15 application stores:

| Average serialized record size | Records per store | Records across 15 stores | Combined payload |
| --- | ---: | ---: | ---: |
| 4 KiB | 250,000 | 3.75 million | ~14.3 GiB |
| 4 KiB | 500,000 | 7.5 million | ~28.6 GiB |
| 8 KiB | 500,000 | 7.5 million | ~57.2 GiB |

These are arithmetic sizing examples, not measured typical record sizes or tested capacity guarantees. Each example assumes the same record count in all 15 application stores and sufficient unused identities in every store. Combined payload excludes indexes, record headers, metadata, and retained mutations; actual disk usage will differ. KiB and GiB use binary units (1 KiB = 1,024 bytes; 1 GiB = 1,073,741,824 bytes).

## Individual record size

Each final serialized record must fit the current contiguous managed-buffer write path: below approximately 2 GiB, including required overhead, and subject to available memory. Available memory, concurrent operations, and temporary buffers determine what is practical for a particular workload; near-limit records require workload-specific testing. This is a current implementation boundary, not an OSS licensing restriction or a total RecordBase-size limit.

This boundary is not a recommended operating size. Writing very large serialized records requires large contiguous buffers, and peak working memory can substantially exceed the final payload size. A large final payload still incurs buffering and copying costs even when a custom serializer produces it.

The boundary applies to the final stored representation, not the original object's memory footprint or logical data size. Custom serialization can reduce that representation, but its returned bytes still become part of the same contiguous record payload; custom hooks do not automatically provide a chunked write path for an oversized record.

These limits belong to Abraxas's OSS edition, not to the independently usable Inheto, FractalKVS, or LibraDex packages. The OSS license selected for this release is Apache-2.0.

## Commercial edition

Abraxas OSS may be used in commercial applications under Apache-2.0.

A separately licensed commercial edition provides expanded capacity and additional features. It is not included in this repository or the OSS NuGet package. Commercial-edition details and licensing information will be linked here when the public website is available.
