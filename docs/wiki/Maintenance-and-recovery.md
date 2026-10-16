# Maintenance and recovery

[Guide home](Home.md)

## Explicit maintenance

Maintenance is separate from ordinary record reads and writes. The original method names remain simple, and their options and reports belong to Abraxas:

```csharp
IndexMaintenanceAssessment assessment = recordBase.AssessIndexMaintenance();
IndexMaintenanceResult repacked = recordBase.Repack(new IndexMaintenanceOptions
{
    Mode = IndexMaintenanceMode.Bounded,
    MaxWorkItems = 256
});

RecordBaseCompactionResult compacted = await recordBase.CompactAsync();
long indexBytesReclaimed = compacted.IndexStorage.ReclaimedBytes;
```

`AssessIndexMaintenance` inspects the ordered-index catalog without changing it. Its disconnected, read-only report includes recommendations, projection issues and storage accounting; unknown byte counts stay `null`, rather than becoming estimates. `Optimize` reshapes supported index topology and can increase file size. `Repack` densifies supported variable-payload shelves without promising file truncation. Both default to a bounded walk; check `Completed` and `IncompleteReasons` before treating the requested work as complete.

`Compact` runs the existing bounded record-storage maintenance pass, then replaces the persistent ordered-index catalog and rebinds live stores. Its `IndexStorage` counts describe that index file only, not total database or record-file savings; reclaimed bytes can be negative if the replacement is larger. Active ordered-index operations must drain before replacement. Dispose readers/enumerators promptly. Async cancellation applies while acquiring maintenance ownership and draining operations; it does not make an admitted synchronous index walk interruptible per tuple.

## Moving or copying a RecordBase

Move or copy the complete six-file family while it is offline. Opening it at a different directory or basename raises `RecordBaseRelocationException`; it does not silently take ownership. The exception provides the attempted directory, exact logical basename and affected file's stored location/identity, with the original storage failure retained as its cause.

If the move or copy was deliberate, call `RecordBase.TakeOwnership(exception)` and retry `Open`. You can also use `RecordBase.TakeOwnership(directory, name)` explicitly. Keep other processes away from the files throughout adoption. The new directory and filenames must fit their existing ownership-header reservations. Missing files, retained restore originals and pending recovery/publication artifacts are rejected; ownership adoption is not repair, recovery or a multi-file atomic transaction.

## Backups and restore

Use `recordBase.CreateBackup(directory)` for a folder snapshot, or `CreateBackupArchive(path)` for a single transportable archive. Backups coordinate the record store, indexes and reserved record identities under write lockout. Each new backup contains all six authoritative files plus a versioned integrity manifest.

Restore into an offline destination with `RecordBase.RestoreBackup(sourceDirectory, targetDirectory, name)` or `RestoreBackupArchive(...)`. Restore checks the original member sizes/digests before changing ownership, validates a private staged copy, and retains the previous destination files until the installed replacement successfully reopens. Handled failures and accepted cancellation roll back that installation. Unrelated files and similarly named RecordBases are left alone. Keep other processes away from the destination throughout restore; this is not a promise of multi-file power-loss atomicity.

`Open` rejects incomplete stores instead of creating missing authority; `CreateOrOpen` creates only when the complete family is absent. Missing/corrupt routing is not repaired by scanning payload bytes. If rollback cannot finish, the exception identifies retained recovery files and ordinary open/create remains blocked until that set is resolved.

Older **complete** snapshots without a manifest need `RestoreBackupOptions.AllowLegacyBackupWithoutManifest = true`. The result reports `LegacyStructuralOnly` rather than `ManifestIntegrity`: structure can be checked, but original bytes and same-generation coherence cannot be proven. Complete includes the `.idgen` reservation file; old five-file backups lacking it are not accepted as lossless restores.
