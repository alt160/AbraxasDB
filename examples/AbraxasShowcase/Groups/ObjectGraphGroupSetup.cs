using AbraxasDB;
using Inheto;
using System.Buffers.Binary;

namespace AbraxasShowcase;

/// <summary>Owns three normalized stores and the two distinct link shapes used by Meridian-style object graphs.<br/></summary>
internal static class ObjectGraphGroupSetup
{
    /// <summary>Creates the customer, order, and line stores plus the reverse-relationship index.<br/></summary>
    /// <param name="directory">Showcase-owned RecordBase directory.<br/></param>
    internal static void CreateEnvironment(string directory)
    {
        using RecordBase recordBase = RecordBase.CreateOrOpen(directory, "showcase");
        recordBase.CreateOrOpenStore<ShowcaseGraphCustomer>("graph-customers", "Graph customers");
        RecordStore<ShowcaseGraphOrder> orders = recordBase.CreateOrOpenStore<ShowcaseGraphOrder>("graph-orders", "Graph orders");
        recordBase.CreateOrOpenStore<ShowcaseGraphLine>("graph-lines", "Graph order lines");
        orders.CreateIndex(order => order.CustomerID, IndexDirection.Ascending);
    }

    /// <summary>Registers a packed ordered line-ID link and an indexed reverse customer-orders link on opened stores.<br/></summary>
    /// <param name="customers">Parent customer store.<br/></param>
    /// <param name="orders">Order store with packed line membership.<br/></param>
    /// <param name="lines">Authoritative line-payload store.<br/></param>
    internal static void Configure(RecordStore<ShowcaseGraphCustomer> customers, RecordStore<ShowcaseGraphOrder> orders, RecordStore<ShowcaseGraphLine> lines)
    {
        lines.ObjectIDSetter = static (line, id) => line.ID = id;
        orders.Links.Add(order => order.Lines, PackLineIds, bytes => HydrateLines(lines, bytes));
        customers.Links.AddReadOnly<List<ShowcaseGraphOrder>>(".Orders", LinkSource.RecordID, bytes =>
        {
            ulong customerID = InhetoBinaryCodec.Decode<ulong>(bytes);
            var result = new List<ShowcaseGraphOrder>();
            var condition = orders.Where.Prop(order => order.CustomerID).EqualTo(customerID).EndCondition;
            using var reader = orders.Readers.Open(condition);
            while (reader.Read()) result.Add(reader.Get());
            return result;
        });
    }

    /// <summary>Encodes only the ordered child RecordIDs, leaving each line payload in its own store.<br/></summary>
    /// <param name="lines">Runtime line collection supplied by the link writer.<br/></param>
    /// <returns>Count-prefixed little-endian RecordIDs in the original order.<br/></returns>
    private static byte[] PackLineIds(List<ShowcaseGraphLine>? lines)
    {
        int count = lines?.Count ?? 0;
        byte[] bytes = new byte[checked(4 + count * 8)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, count);
        for (int i = 0; i < count; i++) BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(4 + i * 8), lines![i].ID);
        return bytes;
    }

    /// <summary>Reads the stored identity list and hydrates the exact ordered children by direct RecordID lookup.<br/></summary>
    /// <param name="store">Authoritative line store.<br/></param>
    /// <param name="bytes">Packed relationship value from the parent order.<br/></param>
    /// <returns>Natural line objects in the stored membership order.<br/></returns>
    private static List<ShowcaseGraphLine> HydrateLines(RecordStore<ShowcaseGraphLine> store, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4) throw new InvalidDataException("Invalid line identity payload.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        if (count < 0 || bytes.Length != 4L + count * 8L) throw new InvalidDataException("Invalid line identity count.");
        var ids = new ulong[count];
        for (int i = 0; i < count; i++) ids[i] = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(4 + i * 8, 8));
        IReadOnlyList<ShowcaseGraphLine> values = store.GetMany(ids);
        var result = new List<ShowcaseGraphLine>(count);
        for (int i = 0; i < count; i++) { values[i].ID = ids[i]; result.Add(values[i]); }
        return result;
    }

    /// <summary>Writes deterministic parents and authoritative children, then persists only ordered line IDs on each order.<br/></summary>
    /// <param name="directory">Showcase-owned RecordBase directory.<br/></param>
    /// <param name="customerCount">Number of customer roots.<br/></param>
    /// <param name="ordersPerCustomer">Number of normalized orders for each customer.<br/></param>
    /// <param name="seed">Repeatable seed controlling line quantities.<br/></param>
    /// <param name="progress">Optional setup progress callback.<br/></param>
    /// <param name="token">Cancellation observed between order graphs.<br/></param>
    internal static void GenerateData(string directory, int customerCount, int ordersPerCustomer, int seed, Action<int, int, string>? progress, CancellationToken token)
    {
        using RecordBase recordBase = RecordBase.CreateOrOpen(directory, "showcase");
        RecordStore<ShowcaseGraphCustomer> customers = recordBase.OpenStore<ShowcaseGraphCustomer>("graph-customers");
        RecordStore<ShowcaseGraphOrder> orders = recordBase.OpenStore<ShowcaseGraphOrder>("graph-orders");
        RecordStore<ShowcaseGraphLine> lines = recordBase.OpenStore<ShowcaseGraphLine>("graph-lines");
        Configure(customers, orders, lines);
        var random = new Random(seed);
        int total = checked(customerCount + customerCount * ordersPerCustomer), completed = 0;
        for (int customerIndex = 0; customerIndex < customerCount; customerIndex++)
        {
            token.ThrowIfCancellationRequested();
            ulong customerID = customers.PutNoLinks(new ShowcaseGraphCustomer { Code = $"GC-{customerIndex + 1:0000000}", Name = $"Graph customer {customerIndex + 1:0000000}" });
            progress?.Invoke(++completed, total, "Generating graph customers");
            for (int orderIndex = 0; orderIndex < ordersPerCustomer; orderIndex++)
            {
                token.ThrowIfCancellationRequested();
                var members = new List<ShowcaseGraphLine>();
                for (int lineIndex = 0; lineIndex < 2; lineIndex++)
                {
                    var line = new ShowcaseGraphLine { Sku = $"SKU-{lineIndex + 1:000}", Quantity = random.Next(1, 6) };
                    ulong id = lines.Put(line);
                    line.ID = id;
                    members.Add(line);
                }
                orders.Put(new ShowcaseGraphOrder { Number = $"GO-{customerIndex + 1:0000000}-{orderIndex + 1:00}", CustomerID = customerID, Lines = members });
                progress?.Invoke(++completed, total, "Generating normalized order graphs");
            }
        }
    }
}
