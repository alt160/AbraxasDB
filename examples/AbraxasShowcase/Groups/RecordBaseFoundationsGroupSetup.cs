using AbraxasDB;

namespace AbraxasShowcase;

/// <summary>Exact compiled setup used by every scenario in the RecordBase foundations group.<br/></summary>
public static class RecordBaseFoundationsGroupSetup
{
// <group-environment>
    /// <summary>
    /// Creates the complete RecordBase topology shared by the foundation scenarios.<br/>
    /// Stores and indexes are established once so individual scenarios never modify the environment structure.<br/>
    /// </summary>
    /// <param name="directory">Owned directory that will contain the group's RecordBase files.<br/></param>
    public static void CreateEnvironment(string directory)
    {
        using RecordBase recordBase = RecordBase.CreateOrOpen(directory, "showcase");
        RecordStore<ShowcaseCustomer> customers = recordBase.CreateOrOpenStore<ShowcaseCustomer>(
            "customers",
            "Showcase customers");
        RecordStore<ShowcaseOrder> orders = recordBase.CreateOrOpenStore<ShowcaseOrder>(
            "orders",
            "Normalized showcase orders");
        customers.CreateIndex(customer => customer.Code, IndexDirection.Ascending);
        customers.CreateIndex(customer => customer.Region, IndexDirection.Ascending);
        orders.CreateIndex(order => order.CustomerID, IndexDirection.Ascending);
        orders.CreateIndex(order => order.OrderedAt, IndexDirection.Ascending);
        orders.CreateIndex(order => order.Status, IndexDirection.Ascending);
    }
// </group-environment>

// <group-generation>
    /// <summary>Generates the deterministic customer and normalized order baseline shared by the group.<br/></summary>
    /// <param name="directory">Owned directory containing the prepared group RecordBase.<br/></param>
    /// <param name="customerCount">Number of baseline customer records to generate.<br/></param>
    /// <param name="ordersPerCustomer">Number of normalized orders generated for every customer.<br/></param>
    /// <param name="seed">Random seed controlling repeatable order values and dates.<br/></param>
    /// <param name="progress">Optional progress callback receiving completed work, total work, and phase.<br/></param>
    /// <param name="cancellationToken">Cancellation observed between generated records.<br/></param>
    public static void GenerateDeterministicData(
        string directory,
        int customerCount,
        int ordersPerCustomer,
        int seed,
        Action<int, int, string>? progress,
        CancellationToken cancellationToken)
    {
        using RecordBase recordBase = RecordBase.CreateOrOpen(directory, "showcase");
        RecordStore<ShowcaseCustomer> customers = recordBase.OpenStore<ShowcaseCustomer>("customers");
        RecordStore<ShowcaseOrder> orders = recordBase.OpenStore<ShowcaseOrder>("orders");
        int total = checked(customerCount + customerCount * ordersPerCustomer);
        int completed = 0;
        var random = new Random(seed);
        var customerIDs = new List<ulong>(customerCount);
        string[] regions = ["West", "Northeast", "South", "Midwest", "International"];
        string[] statuses = ["Pending", "Processing", "Shipped", "Delivered", "Cancelled"];

        for (int index = 0; index < customerCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            customerIDs.Add(customers.Put(new ShowcaseCustomer
            {
                Code = $"CUS-{index + 1:0000000}",
                Name = $"Customer {index + 1:0000000}",
                Region = regions[index % regions.Length],
                Tags = [index % 2 == 0 ? "priority" : "standard", "generated"]
            }));
            progress?.Invoke(++completed, total, "Generating customers");
        }

        for (int customerIndex = 0; customerIndex < customerIDs.Count; customerIndex++)
        {
            for (int orderIndex = 0; orderIndex < ordersPerCustomer; orderIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                orders.Put(new ShowcaseOrder
                {
                    Number = $"SO-{customerIndex + 1:0000000}-{orderIndex + 1:00}",
                    CustomerID = customerIDs[customerIndex],
                    OrderedAt = new DateTime(2026, 1, 1).AddDays(random.Next(365)),
                    Total = decimal.Round(25m + (decimal)random.NextDouble() * 4975m, 2),
                    Status = statuses[random.Next(statuses.Length)]
                });
                progress?.Invoke(++completed, total, "Generating normalized orders");
            }
        }
    }
// </group-generation>
}
