using AbraxasDB;
using System.Security.Cryptography;
using System.Text.Json;

namespace MeridianTrading;

internal sealed partial class TradingDatabase
{
    /// <summary>Reads one scalar record and closes its reader before returning to a potential write operation.<br/>
    /// Link suppression prevents customer editing from materializing the customer's full order history.<br/></summary>
    private static T ReadScalar<T>(RecordStore<T> store, ulong id)
    {
        using var work = ActionTiming.Db();
        using var reader = store.Readers.Open(new[] { id });
        if (!reader.ReadNoLinks()) throw new KeyNotFoundException("Record is missing.");
        return reader.GetNoLinks();
    }

    /// <summary>Computes a scalar content token for optimistic editing under the host's single-writer gate.<br/>
    /// This is application-level stale-form detection, not an engine transaction or cross-process compare-and-swap.<br/></summary>
    private static string Revision<T>(T value) { using var work = ActionTiming.Calculate(); return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Wire.Options))); }

    /// <summary>Returns the editable scalar projection and its revision, without traversing relationship members.<br/></summary>
    internal object EditDetail(string entity, ulong id)
    {
        object value = ReadEditable(entity, id);
        return new { id = id.ToString(), value, revision = Revision(value) };
    }

    /// <summary>Routes one editable entity to its typed scalar store; unknown and relationship-only entities fail closed.<br/></summary>
    private object ReadEditable(string entity, ulong id) => entity switch
        {
            "customers" => ReadScalar(Customers, id),
            "products" => ReadScalar(Products, id),
            "inventory" => ReadScalar(Inventory, id),
            "orders" => ReadScalar(Orders, id),
            _ => throw new ArgumentException("This dataset is read-only.")
        };

    /// <summary>Retrieves one fifty-order customer relationship page through the indexed child foreign identity.<br/>
    /// Complete graph hydration remains available on Customer.Orders; this UI path deliberately pages the same relation.<br/></summary>
    internal object CustomerOrders(ulong id, int offset)
    {
        _ = ReadScalar(Customers, id);
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        var condition = Orders.Where.Prop(order => order.CustomerID).EqualTo(id).EndCondition;
        return ReadPage(Orders, offset, condition, "Number");
    }

    /// <summary>Drills into exactly one group using the same case-sensitive public equality predicate.<br/>
    /// All statuses remain included unless Status itself is selected as the grouping dimension.<br/></summary>
    internal object GroupOrders(string group, string key, int offset)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        var condition = group switch
        {
            "Region" => Orders.Where.Prop(order => order.Region).EqualTo(key).EndCondition,
            "Status" => Orders.Where.Prop(order => order.Status).EqualTo(key).EndCondition,
            _ => throw new ArgumentException("Unknown grouping dimension.")
        };
        return ReadPage(Orders, offset, condition, "Number");
    }

    /// <summary>Validates an explicit allowlist of scalar fields, rejects stale forms, and preserves link payloads.<br/>
    /// Product prices and customer attributes do not rewrite historical order snapshots. Relationship edits require the future journal.<br/></summary>
    internal void SaveEdit(string entity, ulong id, string revision, JsonElement fields)
    {
        object current = ReadEditable(entity, id);
        if (Revision(current) != revision) throw new InvalidOperationException("This record changed. Reopen it before saving.");
        switch (entity)
        {
            case "customers":
                var customer = (Customer)current;
                customer.Name = RequiredText(fields, "name", 120);
                customer.Email = RequiredText(fields, "email", 200);
                if (!System.Net.Mail.MailAddress.TryCreate(customer.Email, out var address) || address.Address != customer.Email) throw new ArgumentException("Enter a valid email address.");
                customer.CreditLimit = Amount(fields, "creditLimit");
                using (ActionTiming.Db()) Customers.PutNoLinks(id, customer);
                break;
            case "products":
                var product = (Product)current;
                product.Name = RequiredText(fields, "name", 120);
                product.Category = RequiredText(fields, "category", 80);
                product.Price = Amount(fields, "price");
                product.ReorderPoint = Units(fields, "reorderPoint");
                using (ActionTiming.Db()) Products.PutNoLinks(id, product);
                break;
            case "inventory":
                var stock = (InventoryPosition)current;
                stock.OnHand = Units(fields, "onHand");
                stock.Reserved = Units(fields, "reserved");
                if (stock.Reserved > stock.OnHand) throw new ArgumentException("Reserved units cannot exceed units on hand.");
                using (ActionTiming.Db()) Inventory.PutNoLinks(id, stock);
                break;
            case "orders": UpdateOrderStatus(id, RequiredText(fields, "status", 20)); break;
            default: throw new ArgumentException("This dataset is read-only.");
        }
    }

    /// <summary>Validates a trimmed business label or email without accepting blank or oversized text.<br/></summary>
    private static string RequiredText(JsonElement fields, string name, int maximum)
    {
        string value = fields.GetProperty(name).GetString()?.Trim() ?? "";
        if (value.Length == 0 || value.Length > maximum) throw new ArgumentException($"{name} must contain 1–{maximum} characters.");
        return value;
    }

    /// <summary>Accepts nonnegative bounded currency with exact cent precision; never silently rounds an edit.<br/></summary>
    private static decimal Amount(JsonElement fields, string name)
    {
        decimal value = fields.GetProperty(name).GetDecimal();
        if (value < 0 || value > 1_000_000_000m || decimal.Round(value, 2) != value) throw new ArgumentException($"{name} must be between 0 and 1 billion with at most two decimal places.");
        return value;
    }

    /// <summary>Accepts bounded whole-unit stock quantities and reorder thresholds.<br/></summary>
    private static int Units(JsonElement fields, string name)
    {
        int value = fields.GetProperty(name).GetInt32();
        if (value < 0 || value > 100_000_000) throw new ArgumentException($"{name} must be between 0 and 100 million whole units.");
        return value;
    }

    /// <summary>
    /// Materializes one customer through the ordinary <c>Customers.Get(...)</c> application path under a validated request-scoped ambient hydration policy.<br/>
    /// Abraxas/Inheto resolves <see cref="Customer.Orders"/> from the indexed child <see cref="SalesOrder.CustomerID"/> relationship, optionally adding the policy's date, recent-count, or value rule, and each returned order resolves <see cref="SalesOrder.Lines"/> from its packed line identities before this method regains control.<br/>
    /// The returned counts are calculated only after hydration and make the policy-complete cross-store object graph explicit to the Meridian UI; no repository join, post-load filter, tracking context, or UI-side relationship merge is involved.<br/>
    /// The existing <see cref="CustomerOrders(ulong, int)"/> route remains the bounded paging alternative for ordinary browsing of customers with potentially large order histories.<br/>
    /// </summary>
    /// <param name="id">Customer record identity whose natural object graph should be materialized.<br/></param>
    /// <param name="hydration">Allow-listed ambient hydration-policy payload supplied by Meridian's controls.<br/></param>
    /// <returns>The policy-complete customer object, relationship counts, applied policy, and a human-readable Abraxas hydration path.<br/></returns>
    internal object CustomerObjectGraph(ulong id, JsonElement hydration)
    {
        CustomerOrderHydrationPolicy policy = CustomerOrderHydrationPolicy.FromWire(hydration);
        ActionTiming.Phase($"Abraxas — materializing Customer.Orders · {policy.Label}");
        Customer customer;
        using var hydrationScope = CustomerOrderHydrationScope.Push(policy);
        using (ActionTiming.Db())
        {
            // This is the entire application-side relationship request. The registered Abraxas/Inheto
            // property-path resolvers complete Customer.Orders and every SalesOrder.Lines collection
            // across their separate stores before Customers.Get(...) returns this object.
            customer = Customers.Get(id);
        }

        int orderCount;
        int lineCount;
        using (ActionTiming.Calculate())
        {
            orderCount = customer.Orders?.Count ?? 0;
            lineCount = customer.Orders?.Sum(order => order.Lines?.Count ?? 0) ?? 0;
        }

        return new
        {
            id = id.ToString(),
            customer,
            orderCount,
            lineCount,
            policy = new { mode = policy.Mode.ToString(), policy.IsFiltered, policy.Label, policy.IndexRoute },
            route = $"Customers.Get → {policy.IndexRoute} → SalesOrder.Lines by packed identities · no separate ORM"
        };
    }

    /// <summary>
    /// Returns the live indexed boundaries used to configure Meridian's three ambient relationship-hydration controls.<br/>
    /// Date boundaries read only the first key in each direction of the attached <c>.OrderedAt</c> index; value boundaries use Abraxas numeric aggregates and do not hydrate order payloads.<br/>
    /// These are presentation limits rather than relationship results, and the active policy remains request-scoped through <see cref="CustomerOrderHydrationScope"/>.<br/>
    /// </summary>
    /// <returns>Dataset date/value limits and the sample's bounded recent-order maximum.<br/></returns>
    internal object CustomerHydrationOptions()
    {
        ActionTiming.Phase("Abraxas — reading customer hydration-policy boundaries");
        using var work = ActionTiming.Db();
        if (Orders.Count == 0) return new { minimumDate = "2025-09-02", maximumDate = "2026-09-01", minimumTotal = 0m, maximumTotal = 1m, recentMaximum = 100 };
        DateTime minimumDate = ReadOrderIndexBoundary<DateTime>(".OrderedAt", SortDirection.Ascending);
        DateTime maximumDate = ReadOrderIndexBoundary<DateTime>(".OrderedAt", SortDirection.Descending);
        decimal minimumTotal = Orders.Aggregates.Min(order => order.Total) ?? 0m;
        decimal maximumTotal = Orders.Aggregates.Max(order => order.Total) ?? Math.Max(1m, minimumTotal);
        return new
        {
            minimumDate = minimumDate.ToString("yyyy-MM-dd"),
            maximumDate = maximumDate.ToString("yyyy-MM-dd"),
            minimumTotal,
            maximumTotal,
            recentMaximum = 100
        };
    }

    /// <summary>
    /// Reads one scalar boundary from an exact attached order index without materializing a complete <see cref="SalesOrder"/> or any registered relationship.<br/>
    /// The requested traversal direction selects the minimum or maximum key while preserving the public Abraxas reader contract.<br/>
    /// </summary>
    /// <typeparam name="TValue">Exact scalar value type stored by the selected index.<br/></typeparam>
    /// <param name="propPath">Exact indexed property path to traverse.<br/></param>
    /// <param name="direction">Ascending for the minimum or descending for the maximum.<br/></param>
    /// <returns>The first scalar value in the requested exact-index direction.<br/></returns>
    private TValue ReadOrderIndexBoundary<TValue>(string propPath, SortDirection direction)
    {
        if (!Orders.TryGetIndex(propPath, out RecordIndex index)) throw new InvalidOperationException($"Customer hydration controls require the prepared {propPath} index.");
        using var reader = Orders.Readers.OpenSorted(new IndexSort(index, direction));
        if (!reader.ReadNoLinks()) throw new InvalidOperationException("Order store is empty.");
        return reader.Get<TValue>(propPath)!;
    }
}
