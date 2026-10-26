<p align="center">
  <img src="https://raw.githubusercontent.com/alt160/AbraxasDB/main/docs/images/AbraxasDB.png" width="420" alt="AbraxasDB logo" />
</p>

# Abraxas

*Data persistence for developers, not professors.*

Abraxas is an embedded, .NET-native recordbase for applications that maintain substantial local repositories of structured data. It persists ordinary .NET objects and lets you read back complete objects or selected values. Its serialization, record-storage, indexing, and query engines run inside your application—not in a separate database server or an unmanaged database engine.

In simplest terms: **object → storage → object**, or **object → storage → selected values**.

The driving goal is low or no developer ceremony. That includes performance and memory efficiency, not just fewer lines in the first example. This README describes the open-source edition only.

[First record](#a-first-record) · [Queries and values](#find-records-or-just-read-their-values) · [Important contracts](#important-contracts) · [OSS limits](#open-source-edition) · [Guides](https://github.com/alt160/AbraxasDB/wiki/Home)

## Why Abraxas?

Your applications, services, and libraries work with .NET objects. They might be instances of types you created, types supplied by .NET or another library, or a mix. When you need to persist hundreds, thousands, or millions of them, you have to choose how to store and retrieve them. That choice often means adding conversion or mapping code between your objects and the storage engine—or modifying your types to fit its requirements.

Abraxas brings serialization, durable record storage, and maintained indexes together behind one .NET API:

- **Keep ordinary application types.** No required base class, persistence attributes, table mapping, or identity property. Supported nested and inherited collections remain part of the record, with shared and circular references preserved within its graph.
- **Read only what you need.** Retrieve a complete object, a subtree, or selected values. Conditions, counts, and value readers do not require reconstructing the whole object first.
- **Start simply; add indexes when useful.** Identity reads and conditions work without secondary indexes. Scalar, text, bounded binary, composite, and partial indexes are available, including memory-only indexes.
- **Let storage grow.** No final database size is required up front. The file-backed design accommodates multi-gigabyte datasets without loading every record payload into memory at open.
- **Avoid hand-maintained sidecars.** Abraxas coordinates records and their indexes; secondary indexes can be rebuilt from authoritative records without rewriting their payloads.

## A first record

Install the `AbraxasDB` NuGet package. The library targets .NET 8 and works with compatible later .NET runtimes, including .NET 9 and 10. Windows is the currently runtime-validated release platform; Linux/macOS runtime and durability acceptance are not yet claimed.

```shell
dotnet add package AbraxasDB
```

Consider an existing application type. It needs no Abraxas-specific changes:

```csharp
public sealed class Customer
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
}
```

Create or open a RecordBase, create or open a typed store, and persist an instance:

```csharp
using AbraxasDB;
using System;
using System.IO;

string directory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "AbraxasExamples");

var configuration = RecordBaseConfiguration.Create(
    durabilityMode: RecordBaseConfiguration.DurabilityMode.Standard);

using RecordBase recordBase = RecordBase.CreateOrOpen(
    directory, "application", configuration);

RecordStore<Customer> customers = recordBase.CreateOrOpenStore<Customer>(
    "customers", "Customer records");

ulong id = customers.Put(new Customer { Name = "Ada", Age = 37 });
Customer saved = customers.Get(id);
Console.WriteLine($"{id}: {saved.Name}, age {saved.Age}");
```

A RecordBase can contain multiple named stores. Keep it and its stores open for the application's useful working lifetime, then dispose the RecordBase during shutdown.

`Put(record)` returns a new identity; keep it separately from the object if that suits the application. `Put(id, record)` replaces an existing record in the same store. Identity reads need no secondary index.

## Find records, or just read their values

Build one condition and reuse it for counting and reading:

```csharp
var adultsNamedA = customers.Where
    .Prop(customer => customer.Name).StartsWith("A")
    .AND.Prop(customer => customer.Age).GreaterOrEqual(18)
    .EndCondition;

Console.WriteLine($"Matches: {customers.CountWhere(adultsNamedA)}");
foreach (Customer match in customers.IterateRecords(adultsNamedA))
    Console.WriteLine(match.Name);
```

The condition also works without indexes. Add indexes during application setup when they benefit the workload:

```csharp
customers.CreateIndex(customer => customer.Name, IndexDirection.Ascending);
customers.CreateIndex(customer => customer.Age, IndexDirection.Ascending);
```

If only a name and an age are needed, read those values without constructing a `Customer` for every result:

```csharp
using (var values = customers.OpenValuesReader(
    adultsNamedA, ".Name", ".Age"))
{
    while (values.Read())
    {
        string? name = values.Get(".Name").AsString;
        int? age = values.Get(".Age").AsInt32;
        Console.WriteLine($"{name}: {age}");
    }
}
```

Use `IterateRecordIDs(adultsNamedA)` when only identities are needed. Conditions also accept property-path strings, and supported covered reads can retrieve values from index keys without opening the record payload.

See [queries and indexes](https://github.com/alt160/AbraxasDB/wiki/Queries-and-indexes) for property paths, text profiles, reusable parameters, and index-definition rules.

## Useful scenarios

Abraxas is intended for applications that maintain substantial local repositories of structured data—including records whose object graphs are much richer than a few flat fields.

- **Asset and engineering repositories:** parts, assemblies, specifications, design variants, and related data in CAD, engineering, and technical applications.
- **Analysis repositories:** measurements, simulation results, and historical datasets. Filter records and stream selected values for calculations without reconstructing every complete object graph.
- **Legal and technical reference libraries:** cases, statutes, annotations, citation metadata, and other complex records maintained locally.

These are examples, not industry boundaries. The common pattern is substantial local data, complex objects, and different needs for retrieving or analyzing their contents. Smaller uses—settings, application records, and persistent dictionaries—fit too.

Abraxas's performance approach is structural: direct identity lookup, typed indexes, allocation-conscious binary serialization, reusable readers, and selective value access. More indexes add mutation work, and actual cost depends on the query, returned data, durability policy, and storage device. See [performance and threading](https://github.com/alt160/AbraxasDB/wiki/Performance-and-threading).

## Important contracts

- **Saving is explicit.** Changing a hydrated object does not silently save it; call `Put` again. Shared and circular references are preserved within a record, not made into implicit relationships between independent records.
- **Concurrent access is supported.** Multiple threads can read and write, with coordination where needed. Finish configuration first; give each enumeration its own reader and dispose it promptly. Do not retain borrowed row data after advancing its reader.
- **Durability is a deliberate choice.** The example requests `DurabilityMode.Standard` for write-through record-file writes. Defaults use deferred persistence; a completed call is not necessarily a synchronous physical disk flush. Record-level A/B durability is not an arbitrary multi-record ACID transaction.
- **A RecordBase is a file family.** It is not one self-contained database file. Use the backup/restore APIs or follow the offline relocation procedure rather than moving individual files.
- **Record size and total storage are different limits.** Each final serialized record must fit the current contiguous managed-buffer path: below approximately 2 GiB, including required overhead, and subject to available memory. This constrains the stored representation, not the original object's memory footprint; custom serialization can reduce that representation. The OSS identity-space limits below apply separately.

The guides explain [threading and durability](https://github.com/alt160/AbraxasDB/wiki/Performance-and-threading#threading-and-durability), [binary-reader lifetimes](https://github.com/alt160/AbraxasDB/wiki/Binary-readers-and-codecs), and [backup, restore, and relocation](https://github.com/alt160/AbraxasDB/wiki/Maintenance-and-recovery).

## Open-source edition

| Boundary | OSS edition |
| --- | --- |
| Application stores in one RecordBase | Up to 15; store identity zero is reserved. |
| Per-store max record count           |  524,287. |
| Individual serialized record size | Below approximately 2 GiB, including required overhead, and subject to available memory; see record-size note below. |
| Built-in record-payload compression and encryption | Not included. |

The per-store counter is an identity-space limit, not a live-record quota. Reservations and reopen behavior can consume capacity, and deleting records does not reset the generator. Exhaustion fails rather than wrapping identities or overwriting existing records.

**Record-size note:** The per-record boundary is a current implementation limit, not an OSS licensing restriction or a recommended operating size. Writing very large serialized records requires large contiguous buffers, and peak working memory can substantially exceed the final payload size. Custom serialization can reduce the stored representation, but a large final payload still incurs buffering and copying costs.

**OSS does not impose a small database-byte quota.** A RecordBase can contain substantial data even when individual serialized records average only a few KiB.

For example, an order containing 25 line items with 50 distinct text values averaging 80 UTF-8 bytes contains about 4,000 bytes of text alone. Item identities, quantities, prices, customer details, and structural metadata can bring the serialized record beyond 4 KiB without any unusually large individual value.

For perspective, the following sizing examples assume the same record count in each of 15 application stores:

| Average serialized record size | Records per store | Records across 15 stores | Combined payload |
| --- | ---: | ---: | ---: |
| 4 KiB | 250,000 | 3.75 million | ~14.3 GiB |
| 4 KiB | 500,000 | 7.5 million | ~28.6 GiB |
| 8 KiB | 500,000 | 7.5 million | ~57.2 GiB |

These are arithmetic sizing examples, not measured typical record sizes or tested capacity guarantees. They assume sufficient unused identities in every store and exclude indexes, record headers, metadata, and retained mutations; actual disk usage will differ. KiB and GiB use binary units (1 KiB = 1,024 bytes; 1 GiB = 1,073,741,824 bytes).

License: Apache-2.0. See [OSS limits](https://github.com/alt160/AbraxasDB/wiki/Open-source-edition) for the full contract.

### Commercial edition

Abraxas OSS may be used in commercial applications under Apache-2.0.

A separately licensed commercial edition provides expanded capacity and additional features. It is not included in this repository or the OSS NuGet package. Commercial-edition details and licensing information will be linked here when it becomes available.

## Guides

Two optional Windows apps demonstrate the public package. Each has its own source repository and precompiled portable ZIP releases:

| App | What it demonstrates | Download |
| --- | --- | --- |
| [Abraxas Showcase](https://github.com/alt160/AbraxasDB.Showcase) | Runnable lessons, complete C# examples, object inspection and memory-backed indexes. | [Windows x64 ZIP](https://github.com/alt160/AbraxasDB.Showcase/releases/latest) |
| [Meridian Trading](https://github.com/alt160/AbraxasDB.MeridianTrading) | A local trading repository with linked object graphs, indexed browsing, analysis and editing. | [Windows x64 ZIP](https://github.com/alt160/AbraxasDB.MeridianTrading/releases/latest) |

The app ZIPs require the **.NET 10 Windows Desktop Runtime (x64)** and **WebView2 Evergreen Runtime**, installed separately. They do not bundle .NET. AbraxasDB and its component libraries remain on .NET 8.

<a href="https://github.com/alt160/AbraxasDB.Showcase"><img src="https://raw.githubusercontent.com/alt160/AbraxasDB.Showcase/v1.0.0/docs/images/showcase-home.png" width="420" alt="Abraxas Showcase welcome page" /></a>
<a href="https://github.com/alt160/AbraxasDB.MeridianTrading"><img src="https://raw.githubusercontent.com/alt160/AbraxasDB.MeridianTrading/v1.0.0/docs/images/meridian-overview.png" width="420" alt="Meridian Trading overview" /></a>

Earlier .NET 8 sample builds remain available in the existing AbraxasDB releases.

The current library source tree contains neither desktop app. To avoid fetching their historical Git objects, clone the current tree with `git clone --depth 1 https://github.com/alt160/AbraxasDB.git`.

The [GitHub wiki](https://github.com/alt160/AbraxasDB/wiki/Home) is the maintained home for the guides:

- [Getting started](https://github.com/alt160/AbraxasDB/wiki/Getting-started)
- [Queries and indexes](https://github.com/alt160/AbraxasDB/wiki/Queries-and-indexes)
- [Object shapes and hydration](https://github.com/alt160/AbraxasDB/wiki/Object-shapes-and-hydration)
- [Binary readers and codecs](https://github.com/alt160/AbraxasDB/wiki/Binary-readers-and-codecs)
- [Performance and threading](https://github.com/alt160/AbraxasDB/wiki/Performance-and-threading)
- [Maintenance and recovery](https://github.com/alt160/AbraxasDB/wiki/Maintenance-and-recovery)
- [Open-source edition](https://github.com/alt160/AbraxasDB/wiki/Open-source-edition)
- [Design and use cases](https://github.com/alt160/AbraxasDB/wiki/Design-and-use-cases)

The files in `docs/wiki` are retained as archived staging copies; use the wiki for current guidance.

Abraxas builds on [Inheto](https://github.com/alt160/Inheto), [FractalKVS](https://github.com/alt160/FractalKVS), and [LibraDex](https://github.com/alt160/LibraDex). It adds store lifecycle, record/index coordination, queries, readers, and recovery around those independently useful .NET libraries.
