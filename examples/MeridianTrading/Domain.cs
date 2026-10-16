namespace MeridianTrading;

/// <summary>
/// Customer master data whose natural <see cref="Orders"/> collection is composed by Abraxas/Inheto from separately stored orders selected through the indexed child <see cref="SalesOrder.CustomerID"/> relationship.<br/>
/// Application consumers receive the completed collection from ordinary object materialization; no separate ORM or repository join is required.<br/>
/// </summary>
public sealed class Customer
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Region { get; set; } = "";
    public string Email { get; set; } = "";
    public decimal CreditLimit { get; set; }
    public List<SalesOrder>? Orders { get; set; }
}

/// <summary>
/// Order header whose natural <see cref="Lines"/> collection is composed by Abraxas/Inheto from separately stored line records.<br/>
/// The parent payload retains only a packed ordered sequence of line identities, preserving normalized storage without changing the runtime object shape.<br/>
/// </summary>
public sealed class SalesOrder
{
    public string Number { get; set; } = "";
    public ulong CustomerID { get; set; }
    public string Region { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime OrderedAt { get; set; }
    public decimal Total { get; set; }
    public List<OrderLine>? Lines { get; set; }
}

/// <summary>Independent commercial line; unit price is the historical price agreed on the order.<br/></summary>
public sealed class OrderLine
{
    public ulong ID { get; set; }
    public ulong ProductID { get; set; }
    public string SKU { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
}

/// <summary>Independently editable catalog product; catalog pricing does not rewrite historical orders.<br/></summary>
public sealed class Product
{
    public string SKU { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal Price { get; set; }
    public int ReorderPoint { get; set; }
}

/// <summary>One stock position in a warehouse; product relation is a normalized identity.<br/></summary>
public sealed class InventoryPosition
{
    public ulong ProductID { get; set; }
    public string SKU { get; set; } = "";
    public string Warehouse { get; set; } = "";
    public int OnHand { get; set; }
    public int Reserved { get; set; }
}

/// <summary>Readiness marker for deterministic generated content; unfinished generation is never ready.<br/></summary>
public sealed class DatasetManifest
{
    public string Version { get; set; } = "meridian-2";
    public string Phase { get; set; } = "Empty";
    public int Seed { get; set; } = 1729;
    public int RequestedOrders { get; set; }
    public double GenerationMilliseconds { get; set; }
    public string Profile { get; set; } = "Legacy smoke";
    public int RequestedCustomers { get; set; }
    public int RequestedProducts { get; set; }
    public int CompletedOrders { get; set; }
    public long CompletedLines { get; set; }
    public double IngestionMilliseconds { get; set; }
    public double IndexingMilliseconds { get; set; }
    public string LastError { get; set; } = "";
    public List<IndexBuildTiming> IndexTimings { get; set; } = [];
    public int ApplicationIndexVersion { get; set; }
    public int OrdersPerCheckpoint { get; set; } = 256;
}

/// <summary>One completed index-build interval, separate from data generation and UI work.<br/></summary>
public sealed class IndexBuildTiming
{
    public string Name { get; set; } = "";
    public double Milliseconds { get; set; }
}
