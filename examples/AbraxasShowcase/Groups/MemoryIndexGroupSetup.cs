using AbraxasDB;

namespace AbraxasShowcase;

/// <summary>Prepares durable customer records without durable secondary indexes so each memory-index lesson starts from the same baseline.<br/></summary>
public static class MemoryIndexGroupSetup
{
// <group-environment>
    /// <summary>Creates the group's RecordBase and customer store without creating any secondary index.<br/></summary>
    /// <param name="directory">Showcase-owned directory for the group's related files.<br/></param>
    public static void CreateEnvironment(string directory)
    {
        using RecordBase recordBase = RecordBase.CreateOrOpen(directory, "showcase");
        _ = recordBase.CreateOrOpenStore<ShowcaseCustomer>("customers", "Memory-index examples");
        _ = recordBase.CreateOrOpenStore<ShowcaseOrder>("orders", "Memory-index examples for orders");
    }
// </group-environment>

// <group-generation>
    /// <summary>Writes repeatable ordinary customers so each temporary index can backfill from persisted records.<br/></summary>
    /// <param name="directory">Prepared group RecordBase directory.<br/></param>
    /// <param name="customerCount">Number of durable customer records to write.<br/></param>
    /// <param name="ordersPerCustomer">Number of ordinary order records to retain per customer.<br/></param>
    /// <param name="progress">Optional progress report for the setup UI.<br/></param>
    /// <param name="cancellationToken">Cancellation checked before each write.<br/></param>
    public static void GenerateDeterministicData(string directory, int customerCount, int ordersPerCustomer, Action<int, int, string>? progress, CancellationToken cancellationToken)
    {
        using RecordBase recordBase = RecordBase.CreateOrOpen(directory, "showcase");
        RecordStore<ShowcaseCustomer> customers = recordBase.OpenStore<ShowcaseCustomer>("customers");
        RecordStore<ShowcaseOrder> orders = recordBase.OpenStore<ShowcaseOrder>("orders");
        string[] firstNames = ["Ada", "Grace", "Alan", "Katherine"];
        string[] lastNames = ["Lovelace", "Hopper", "Turing", "Johnson"];
        for (int index = 0; index < customerCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ulong customerId = customers.Put(new ShowcaseCustomer
            {
                Code = $"MEM-{index + 1:000000}",
                FirstName = firstNames[index % firstNames.Length],
                LastName = lastNames[index % lastNames.Length],
                Name = $"Customer {index + 1:000000}",
                Region = index % 2 == 0 ? "West" : "East"
            });
            progress?.Invoke(index + 1, customerCount * (ordersPerCustomer + 1), "Writing durable customers without secondary indexes");
            for (int orderIndex = 0; orderIndex < ordersPerCustomer; orderIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                orders.Put(new ShowcaseOrder { Number = $"MEM-SO-{index + 1:000000}-{orderIndex + 1:00}", CustomerID = customerId,
                    OrderedAt = new DateTime(2026, 1, 1).AddDays(index % 365), Total = 20m + index + orderIndex, Status = "Open" });
                progress?.Invoke(index * (ordersPerCustomer + 1) + orderIndex + 2, customerCount * (ordersPerCustomer + 1), "Writing durable orders");
            }
        }
    }
// </group-generation>
}
