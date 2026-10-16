# Performance and threading

[Guide home](Home.md)

## Performance by doing the necessary work

Abraxas's performance approach is structural: direct identity lookup, typed indexes, allocation-conscious binary serialization, reusable readers, and selective value access. An application can often avoid reconstructing an object graph simply to test a condition, count a result, or read one property.

Index-backed selections can narrow the records examined. Covered reads can avoid payload access. Reader APIs can avoid building a result array or constructing a whole object for every row. The cost still depends on the condition, maintained indexes, returned shape, record sizes, and storage device; a broad residual filter or an explicit materialization remains real work.

Memory-only indexes must be recreated after reopen. More indexes add storage and mutation work. Write-through writes have a different throughput profile from deferred persistence. Choose these tradeoffs for the workload rather than assuming one setting is fastest for everything.

To understand what a query actually does, subscribe to `ConditionExecutionPlanned`: `IndexDirect` means the maintained indexes execute the complete condition, `IndexCandidateWithResidual` means indexes narrow the candidates before residual record evaluation, and `RecordScan` means the records must be examined. Each leaf reports whether its index was used through `UsedByIndex`.

`OrderingExecutionPlanned` explains direct-index versus defensive candidate ordering. `FastFindExecutionCompleted` reports the actual route, work counts, and completion status for an enumerated FastFind operation. These diagnostics are opt-in; Abraxas manages its component bindings internally. `RecordBase.IndexCatalogFileFormatVersion` reports the actual opened index format, not merely the requested creation setting.

## Threading and durability

Multiple threads can read and write through an open RecordBase and its stores. Abraxas coordinates record mutations and index maintenance; thread-safe access does not mean every mutation runs in parallel. Supported bulk-write routes can execute physical work concurrently, while operations with additional coordination requirements use a serialized path.

Finish store configuration before concurrent use, and keep application objects and parameter values stable while an operation consumes them. Give each active enumeration its own reader and dispose it when finished. Do not advance the same mutable reader concurrently or keep borrowed current-row data after advancing it. A read session can also affect when writers or maintenance can proceed; concurrent access does not imply that every read is a non-blocking snapshot.

Record updates use FractalKVS's record-level A/B durability. Abraxas coordinates record mutations, indexes, and recovery; that record durability is separate from arbitrary multi-record ACID transactions.

The quick start explicitly requests write-through record-file writes through Abraxas's `DurabilityMode.Standard`. The default `RecordBaseConfiguration.Default` uses deferred persistence; a completed call under that policy should not be interpreted as a synchronous physical disk flush. Actual persistence also depends on the operating system and storage device. Select and test the durability policy appropriate for the application.
