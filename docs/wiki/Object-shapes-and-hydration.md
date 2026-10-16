# Object shapes and hydration

[Guide home](Home.md)

The first examples do not require custom handlers or settings. The following capabilities are available when the application's object shapes or access patterns need them.

## Record shape and hydration settings

`store.SerializationOptions` and `store.DeserializationOptions` use Abraxas-owned `RecordSerializationOptions` and `RecordDeserializationOptions`. No Inheto settings type or namespace is needed to configure ordinary record persistence.

```csharp
customers.SerializationOptions.ExcludedMembers.Add(".SessionState");
customers.SerializationOptions.ExcludedTypes.Add(typeof(System.Threading.CancellationTokenSource));
customers.SerializationOptions.MemberTypes = RecordMemberTypes.PublicPropertiesAndFields;

// Replace settings when a different hydration policy is needed.
// Registered store links are rebound to the replacement automatically.
customers.DeserializationOptions = new RecordDeserializationOptions();
// If the target model later renames Name to DisplayName:
// customers.Aliases.Add(".Name", ".DisplayName");
```

Settings also support included paths, member-specific binary/text codecs, type-wide binary codecs, circular-member rules, collection comparers, and custom constructors/factories. Alias groups belong to the current deserialization settings; replacing those settings replaces their aliases too. Existing records and indexes are not rewritten by changing settings. Configure handlers before index attachment when an index depends on their representation, and finish configuration before concurrent use. Use independent deserialization options for different stores: they carry store-bound link hooks.

Activators receive an Abraxas-owned `RecordActivationContext`. Its `PropPath`, `StoredContext`, `ReadPropAs<T>(fullPath)`, exact/fill reads and raw-byte views provide construction context without exposing the underlying reader. Paths are full record paths, not relative to `PropPath`. The context and its raw-byte views are borrowed only for the callback; copy anything you need to retain. Returning null declines that instance's construction; callback errors propagate normally.

## Check a path or an unfamiliar runtime object

`CheckPropPath` checks the store's declared CLR members without calling getters or methods. Its readonly `RecordPropertyPathCheck` retains the existing cached plan, canonical spelling, result type, and precise failure/CLR-continuation information:

```csharp
RecordPropertyPathCheck path = customers.CheckPropPath<int>(".Name.Length");
Console.WriteLine($"{path.CanonicalPath}: {path.IsValid}");

RecordTypeReport report = customers.TestType(
    new Customer { Name = "Ada", Age = 37 },
    new RecordTypeProbeOptions { MaxDepth = 32, MaxEnumerableItems = 128 });

foreach (RecordTypeIssue issue in report.Issues)
    Console.WriteLine($"{issue.MemberPath}: {issue.Message}");
```

The structural path check is not a record-presence check and does not validate runtime dictionary keys or collection positions. Aliases do not add members to the declared CLR type. Direct stored-value paths keep their separate runtime navigation behavior.

`TestType` is an optional compatibility diagnostic, not part of each write. It inspects the supplied graph under the store's current serialization policy without persisting it or applying its suggestions. Its read-only report remains usable after the store closes. Probing can execute getters and enumerators; custom serializers and selected getter-stability checks also run by default. Set `InvokeCustomSerializers` or `ProbeGetterStability` to false when those checks are unwanted. Their side effects are not rolled back, and traversal limits do not interrupt a callback that fails to return. A clean report covers the inspected values, not later mutations or values outside the configured bounds.

See [binary readers and codecs](Binary-readers-and-codecs.md) for stored-value reads, raw bytes, and custom-property handling.
