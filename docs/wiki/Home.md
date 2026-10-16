# Abraxas guides

> Archived staging copy. The maintained guide is in the [GitHub wiki](https://github.com/alt160/AbraxasDB/wiki/Home). Edit the wiki rather than this snapshot.

Abraxas is an embedded, .NET-native recordbase for applications that maintain substantial local repositories of structured data. It persists ordinary .NET objects and reads back complete objects or selected values. Asset and engineering repositories, analysis datasets, and legal or technical reference libraries are examples—not industry boundaries. Smaller application-data uses fit too. These guides cover the open-source edition.

Start with [Getting started](Getting-started.md), then read [Queries and indexes](Queries-and-indexes.md). The other guides cover the controls and operational details you need as the application grows.

## Guides

- [Getting started](Getting-started.md) — Open a RecordBase and store ordinary objects without persistence attributes or an identity property.
- [Queries and indexes](Queries-and-indexes.md) — Conditions, property paths, optional indexes, text profiles, parameters, and deferred bindings.
- [Object shapes and hydration](Object-shapes-and-hydration.md) — Member selection, exclusions, aliases, activation hooks, and compatibility diagnostics.
- [Binary readers and codecs](Binary-readers-and-codecs.md) — Selective reads, alternative hydration shapes, borrowed data, custom bytes, and codecs.
- [Performance and threading](Performance-and-threading.md) — Execution diagnostics, allocation-conscious access, concurrency, and durability policies.
- [Maintenance and recovery](Maintenance-and-recovery.md) — Maintenance, compaction, relocation, backups, restore, and recovery boundaries.
- [Open-source edition](Open-source-edition.md) — Structural capacity, record-identity exhaustion, and edition exclusions.
- [Design and use cases](Design-and-use-cases.md) — The object-first approach, useful workloads, and the components underneath.

## Example conventions

Examples that use `customers` or `id` continue from the first-record example in [Getting started](Getting-started.md). They demonstrate individual API operations, not a requirement to configure every feature before storing the first record.

Read the [threading and durability guidance](Performance-and-threading.md#threading-and-durability) and [OSS limits](Open-source-edition.md) before choosing the deployment and write policy.
