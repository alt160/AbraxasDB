# Design and use cases

[Guide home](Home.md)

*Data persistence for developers, not professors.*

Abraxas is a .NET-native, embedded recordbase for applications that maintain substantial local repositories of structured data: a local database built around serialized objects instead of table rows. It persists ordinary .NET objects, finds them by their values or system-assigned record identities, and lets an application read just what it needs—from one value or a subtree to a complete object graph. Binary serialization, durable record storage, and maintained secondary indexes work together behind one API, designed to reduce allocation pressure and unnecessary work per record.

In simplest terms, Abraxas is...
>Object->Storage->Object
>or
> Object->Storage->Value(s)

Your application does not need data contracts, SQL, persistence attributes, or a table mapping for every type. It can keep its existing objects—including supported nested and inherited collections, with shared and circular references preserved within each record's object graph.

The driving goal is low or no developer ceremony: keep persistence close to the objects and code you already have. That includes performance and memory efficiency, not just fewer lines in the first example. Start with ordinary records; add indexes, selective reads, or custom handling when the application benefits from them.

These guides describe the open-source edition only.

## Why Abraxas?

Your applications, services, and libraries work with .NET objects. They might be instances of types you created, types supplied by .NET or another library, or a mix. When you need to persist hundreds, thousands, or millions of them, you have to choose how to store and retrieve them. That choice often means adding conversion or mapping code between your objects and the storage engine—or modifying your types to fit its requirements.

Abraxas is built to reduce that work. It persists ordinary .NET objects without requiring a separate table model, persistence attributes, or an identity property on your types. Durable record storage, maintained indexes, and selective reads work together behind one API: retrieve a complete object when you need it, or read particular values without reconstructing the whole graph. Custom handling remains available for types or representations that need it.

Storage grows with the data rather than requiring a final database size up front. Multi-gigabyte RecordBases—including datasets measured in tens of gigabytes—fit the file-backed design; opening a RecordBase does not load all its record payloads into managed memory. Large objects can be individual records, but each serialized record must fit the current contiguous managed-buffer path: under 2 GB in practice, and subject to available memory. Total storage size is separate from the OSS edition's [store and record-identity limits](Open-source-edition.md).

- **Keep ordinary application types.** Object persistence does not require an Abraxas base class, identity property, or attributes on every member. Supported nested objects, collections, and references remain part of the record's shape.
- **Start without index setup.** Read directly by record identity or build a condition over stored values. Add indexes for the searches and ordering that benefit from them; they are not a prerequisite for persisting or finding records.
- **Read less.** Return identities or a few values without constructing complete object graphs merely to filter, display, count, or process records.
- **Use the index shape the task needs.** Scalar, text, bounded binary, composite, and partial indexes serve different lookup and ordering needs. Keep indexes on disk or use memory-only indexes for a temporary working set.
- **Avoid hand-maintained sidecars.** Abraxas coordinates stored records and their indexes. Secondary indexes can be rebuilt from authoritative records without rewriting their payloads.

These are explicit records, not automatically tracked live objects. `Put` persists a record; `Get` reconstructs an object from its stored values. Changing that object does not silently save it—call `Put` again. Shared and circular references are preserved within a record's graph, not turned into implicit relationships between independently stored records. Conditions and indexes work on stored values without requiring the original graph to be hydrated first.

Abraxas serves familiar record-management, search, filtering, ordering, and reporting workloads through a .NET API. It is not a relational SQL engine or a replacement for every database workload.

## Useful scenarios

Embedded describes where Abraxas runs, not the size or complexity of the application's data. Its intended uses include substantial local repositories with records whose object graphs are richer than a few flat fields:

- **Asset and engineering repositories.** Persist parts, assemblies, specifications, design variants, and related data in CAD, engineering, and technical applications. Keep supported nested objects and collections in their application shapes, then use maintained indexes and selective reads for the values needed by a particular view or operation.
- **Analysis repositories.** Store measurements, simulation results, and historical datasets. Filter records and stream selected values for calculations, reports, or background processing without reconstructing every complete object graph. Hydrate complete records when the application needs their full shape.
- **Legal and technical reference libraries.** Maintain cases, statutes, annotations, citation metadata, and other complex structured records locally. Find records by indexed values and read the portions needed by a view without materializing every record in full. These examples concern structured persistence and retrieval, not built-in document extraction, OCR, or full-text search.

These are examples, not industry boundaries. The common pattern is substantial local data, complex objects, and different needs for retrieving or analyzing their contents. Total stored bytes, individual record size, and record-identity capacity are separate concerns; the [OSS limits](Open-source-edition.md) still apply.

Smaller application-data uses remain valuable too:

- **Manage the application's everyday records.** Persist customers, products, orders, work items, or other domain objects; retrieve, update, and delete them by identity, and search or order them by their values. These are familiar database tasks, without requiring SQL or a separate database service.
- **Build searchable catalogs, histories, and reporting views.** Maintain indexed names, dates, statuses, and combinations of values across a growing dataset. Count matches or stream selected fields for a report, export, or background job without hydrating every matching object.
- **Keep application data between sessions.** Desktop applications, embedded services, and background workers can reopen their stored objects without a persistence-specific model.
- **Replace a dictionary and its growing collection of sidecars.** Retrieve directly by record identity, then find by name, date, status, or a combination of values without maintaining separate lookup and sorted collections yourself.
- **Store hierarchical objects without flattening them.** Retain supported nested objects and collections, including shared and circular references within a record, instead of mapping every member into tables.
- **Fill a grid or process a few fields from many records.** Read the requested values without constructing and discarding complete objects for every row.
- **Build a temporary indexed working set.** Keep records durable while creating memory-only indexes for the current session; recreate those indexes when the application reopens.

## The components underneath

Abraxas is built on three independently useful .NET libraries:

| Component | Responsibility |
| --- | --- |
| [Inheto](https://github.com/alt160/Inheto) | Binary object serialization, reference preservation, and property-path value access. |
| [FractalKVS](https://github.com/alt160/FractalKVS) | Authoritative record payload storage and numeric-identity retrieval. |
| [LibraDex](https://github.com/alt160/LibraDex) | Secondary key-to-identity indexing, conditions, ordered traversal, and indexed value access. |

Abraxas adds the store lifecycle, record/index coordination, query and reader APIs, and recovery behavior around those components. It does not use SQLite as its storage or query engine.
