# Binary readers and codecs

[Guide home](Home.md)

## Read a record without hydrating the whole object

`GetBinary(id)` returns an Abraxas-owned `RecordBinary` with independent decoded bytes. Read a value, hydrate a different shape, or reuse a prepared path without requiring an Inheto type in your application:

```csharp
RecordBinary data = customers.GetBinary(id);
string? storedName = data.ReadPropAs<string>(".Name");
RecordPropertyPathCheck nameLength = customers.CheckPropPath<int>(".Name.Length");
RecordPropertyPathReadState state = data.TryReadPropPath(nameLength, out int length);

using (var binaries = customers.OpenBinaryReader())
{
    while (binaries.Read())
    {
        RecordBinary row = binaries.Get(); // Borrowed: consume before the next Read/Skip/Dispose.
        Console.WriteLine(row.ReadPropAs<string>(".Name"));
        // RecordBinary retained = row.Detach(); // Explicit independent copy when needed.
    }
}
```

`TryGetBinary`, `GetBinaries` and `IterateBinaries` provide the corresponding detached retrieval forms; batches preserve requested order and duplicates. `OpenBinaryReader` also accepts conditions and ordering. Its `CopyCurrent()` is the convenient explicit-copy counterpart to a borrowed row. Missing same-store identities return false/default from `TryGetBinary`; a default binary has `IsValid == false`. Cross-store identities remain invalid.

`RawMemory`, `RawSpan`, custom payload slices and UTF-8 string slices avoid copying, but follow their source's lifetime. `CopyPayload()` explicitly returns independent bytes. `Detach()` copies bytes and creates independent reader state; `ShallowClone()` creates independent cursor/caches over shared bytes and does **not** extend a borrowed row's lifetime. Copying the readonly value alone also shares reader state: do not use the same binary concurrently. Configuration and callbacks remain shared rather than snapshotted; callbacks that query a store still need that store to be available.

Typed/runtime `ReadPropAs`, exact/fill/enumerable reads and `ToObject` preserve ordinary shape, alias, hook and reference behavior. A prepared path can report missing/null sources or a failed CLR continuation rather than flattening these into a value. The binary contains decoded Inheto data; `GetStoredPayload`/`OpenStoredPayloadReader` are the separate exact-storage-byte APIs and do not decode the storage representation.

## Binary slices and application codecs

Stored custom-property bytes remain readable even if their reader or write-link registration is later removed. Automatic hydration defaults to ignoring an unhandled custom property, preserving the destination member's existing or constructor-initialized value. Set `store.DeserializationOptions.UnhandledCustomProperties = UnhandledRecordCustomPropertyHandling.AsBinary` for a binary-compatible projection; incompatible semantic destinations report a type mismatch. Explicit raw-byte reads remain available. Registered readers still take precedence and their errors are not hidden; ordinary byte arrays and type-wide codecs are unaffected.

Binary slice conditions describe how your own bytes should be interpreted; the representation is not guessed. The simple date/time overloads retain their raw .NET tick or day-number defaults. Abraxas-owned choices such as `BinaryTimeSpanEncoding.OrderedTicks` and `BinaryDateTimeEncoding.PrecisionSdt` describe other supported layouts explicitly. These choices must match the bytes you actually stored; they do not convert the stored payload into a different format.

For encoded text, supply a runtime `Encoding`, or a numeric code page and `BinaryTextFallback` when the interpretation must be durably recreated. Standard code pages and fallback policies can be persisted in condition descriptors; custom runtime encodings or fallback behavior remain runtime-only.

`RegisterBinaryCodec<TValue>(...)` and the typed `Links.Add(...)` / `Links.AddReadOnly(...)` helpers accept Abraxas's `BinaryDecoder<TValue>` callback. It receives a borrowed `ReadOnlySpan<byte>` rather than requiring another byte-array copy. Copy bytes only if you need to retain them after the callback returns. Codec registration binds the decoder once without adding a forwarding callback to each decode; links retain their existing typed-result adaptation. Persistent custom indexes also require a stable format ID/version and registration again when opening the store, before index attachment.
