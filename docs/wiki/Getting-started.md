# Getting started

[Guide home](Home.md)

Consider an existing application type like this. It needs no Abraxas base class or persistence attributes:

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

A `RecordBase` can contain multiple named stores in an application-selected file system folder. A RecordBase consists of several related files sharing a basename, not one self-contained database file. `Put(record)` returns a new `RecordID`; the application can retain it separately rather than adding an identity property to its object. `Put(id, record)` replaces an existing record from that same store. Reading by identity does not require a secondary index.

Keep the RecordBase and its stores open for the application's useful working lifetime, then dispose the RecordBase during shutdown. There is no need to open and close it for every record.

Next: [queries and indexes](Queries-and-indexes.md).
