using AbraxasDB;

namespace AbraxasShowcase;

/// <summary>Exact compiled setup used by every scenario in the Stores group.<br/></summary>
public static class StoresGroupSetup
{
// <group-environment>
    /// <summary>Creates two typed stores so store metadata and behavior can be explored side by side.<br/></summary>
    /// <param name="directory">Owned directory that will contain the group's RecordBase files.<br/></param>
    public static void CreateEnvironment(string directory)
    {
        using RecordBase recordBase = RecordBase.CreateOrOpen(directory, "showcase");
        _ = recordBase.CreateOrOpenStore<ShowcaseCustomer>("customers", "Store examples for customers");
        _ = recordBase.CreateOrOpenStore<ShowcaseOrder>("orders", "Store examples for orders");
    }
// </group-environment>

// <group-generation>
    /// <summary>Generates deterministic records in both typed stores.<br/></summary>
    /// <param name="directory">Owned directory containing the prepared group RecordBase.<br/></param>
    /// <param name="customerCount">Number of customer records to generate.<br/></param>
    /// <param name="ordersPerCustomer">Number of order records generated for every customer.<br/></param>
    /// <param name="seed">Random seed controlling repeatable order values.<br/></param>
    /// <param name="progress">Optional setup progress callback.<br/></param>
    /// <param name="cancellationToken">Cancellation observed between records.<br/></param>
    public static void GenerateDeterministicData(string directory, int customerCount, int ordersPerCustomer, int seed, Action<int, int, string>? progress, CancellationToken cancellationToken)
    {
        using RecordBase recordBase = RecordBase.CreateOrOpen(directory, "showcase");
        RecordStore<ShowcaseCustomer> customers = recordBase.OpenStore<ShowcaseCustomer>("customers");
        RecordStore<ShowcaseOrder> orders = recordBase.OpenStore<ShowcaseOrder>("orders");
        var random = new Random(seed);
        var customerIDs = new List<ulong>(customerCount);
        int total = checked(customerCount + customerCount * ordersPerCustomer);
        int completed = 0;
        for (int index = 0; index < customerCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            customerIDs.Add(customers.Put(new ShowcaseCustomer { Code = $"STORE-{index + 1:000000}", Name = $"Store customer {index + 1:000000}", Region = index % 2 == 0 ? "West" : "East", Tags = ["stores", "generated"] }));
            progress?.Invoke(++completed, total, "Generating customers");
        }
        for (int customerIndex = 0; customerIndex < customerIDs.Count; customerIndex++)
        {
            for (int orderIndex = 0; orderIndex < ordersPerCustomer; orderIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                orders.Put(new ShowcaseOrder { Number = $"STORE-SO-{customerIndex + 1:000000}-{orderIndex + 1:00}", CustomerID = customerIDs[customerIndex], OrderedAt = new DateTime(2026, 1, 1).AddDays(random.Next(365)), Total = decimal.Round(10m + (decimal)random.NextDouble() * 990m, 2), Status = orderIndex % 2 == 0 ? "Open" : "Closed" });
                progress?.Invoke(++completed, total, "Generating orders");
            }
        }
    }
// </group-generation>
}
