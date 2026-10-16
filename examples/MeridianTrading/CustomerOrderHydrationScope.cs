using System.Globalization;
using System.Text.Json;
using System.Threading;

namespace MeridianTrading;

/// <summary>
/// Identifies the caller-selected policy that the registered <see cref="Customer.Orders"/> hydration hook should apply.<br/>
/// The value describes application intent only; Abraxas still owns condition planning, index traversal, and object materialization.<br/>
/// </summary>
internal enum CustomerOrderHydrationMode
{
    FullHistory,
    DateWindow,
    RecentCount,
    OrderValue
}

/// <summary>
/// Immutable request policy consumed ambiently while Abraxas/Inheto materializes <see cref="Customer.Orders"/>.<br/>
/// Inactive fields remain present so one allocation-free value can represent every supported demonstration mode.<br/>
/// </summary>
/// <param name="Mode">Active relationship-hydration mode.<br/></param>
/// <param name="FromDate">Inclusive lower order-date boundary.<br/></param>
/// <param name="ThroughDate">Inclusive upper order-date boundary.<br/></param>
/// <param name="RecentCount">Maximum number of newest orders to hydrate.<br/></param>
/// <param name="MinimumTotal">Inclusive lower order-value boundary.<br/></param>
/// <param name="MaximumTotal">Inclusive upper order-value boundary.<br/></param>
internal readonly record struct CustomerOrderHydrationPolicy(
    CustomerOrderHydrationMode Mode,
    DateTime FromDate,
    DateTime ThroughDate,
    int RecentCount,
    decimal MinimumTotal,
    decimal MaximumTotal)
{
    internal static CustomerOrderHydrationPolicy FullHistory => new(CustomerOrderHydrationMode.FullHistory, default, default, 0, 0, 0);

    internal bool IsFiltered => Mode != CustomerOrderHydrationMode.FullHistory;

    internal string Label => Mode switch
    {
        CustomerOrderHydrationMode.DateWindow => $"Date window · {FromDate:MMM d, yyyy}–{ThroughDate:MMM d, yyyy}",
        CustomerOrderHydrationMode.RecentCount => $"Most recent {RecentCount:N0} orders",
        CustomerOrderHydrationMode.OrderValue => $"Order value · {MinimumTotal:C0}–{MaximumTotal:C0}",
        _ => "Full history"
    };

    internal string IndexRoute => Mode switch
    {
        CustomerOrderHydrationMode.DateWindow => "CustomerID + OrderedAt indexed condition",
        CustomerOrderHydrationMode.RecentCount => "CustomerID population → OrderedAt index descending → bounded read",
        CustomerOrderHydrationMode.OrderValue => "CustomerID + Total indexed condition",
        _ => "CustomerID indexed condition"
    };

    /// <summary>
    /// Validates the browser's allow-listed hydration-policy payload and converts it into one immutable request value.<br/>
    /// Disabled filtering always returns <see cref="FullHistory"/> regardless of any retained browser control values.<br/>
    /// Date boundaries are inclusive for complete calendar days; counts and currency remain deliberately bounded for predictable sample behavior.<br/>
    /// </summary>
    /// <param name="payload">Detached JSON object supplied by the Meridian WebView bridge.<br/></param>
    /// <returns>A validated full-history or filtered hydration policy.<br/></returns>
    internal static CustomerOrderHydrationPolicy FromWire(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("enabled", out JsonElement enabled) || !enabled.GetBoolean())
            return FullHistory;

        string mode = payload.GetProperty("mode").GetString() ?? "";
        switch (mode)
        {
            case "date":
                DateTime from = DateTime.ParseExact(payload.GetProperty("fromDate").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None);
                DateTime through = DateTime.ParseExact(payload.GetProperty("throughDate").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None);
                if (from > through || through.Subtract(from).TotalDays > 3_650) throw new ArgumentException("Order-date hydration range must be ordered and no wider than ten years.");
                return new(CustomerOrderHydrationMode.DateWindow, from.Date, through.Date.AddDays(1).AddTicks(-1), 0, 0, 0);
            case "recent":
                int count = payload.GetProperty("recentCount").GetInt32();
                if (count is < 1 or > 500) throw new ArgumentException("Recent-order hydration count must be between 1 and 500.");
                return new(CustomerOrderHydrationMode.RecentCount, default, default, count, 0, 0);
            case "value":
                decimal minimum = payload.GetProperty("minimumTotal").GetDecimal();
                decimal maximum = payload.GetProperty("maximumTotal").GetDecimal();
                if (minimum < 0 || minimum > maximum || maximum > 1_000_000_000m) throw new ArgumentException("Order-value hydration range must be ordered between zero and one billion dollars.");
                return new(CustomerOrderHydrationMode.OrderValue, default, default, 0, minimum, maximum);
            default:
                throw new ArgumentException("Unknown customer-order hydration policy.");
        }
    }
}

/// <summary>
/// Carries one immutable customer-order hydration policy through the current asynchronous execution context.<br/>
/// The property-path resolver can therefore adapt its Abraxas query without adding persistence concerns to domain objects or changing ordinary <c>Customers.Get(id)</c> consumption.<br/>
/// Nested scopes restore their predecessor, and disposal prevents policy leakage into later or concurrent requests.<br/>
/// </summary>
internal static class CustomerOrderHydrationScope
{
    private static readonly AsyncLocal<Frame?> current = new();

    internal static CustomerOrderHydrationPolicy Current => current.Value?.Policy ?? CustomerOrderHydrationPolicy.FullHistory;

    /// <summary>
    /// Applies one immutable policy to the current execution context until the returned scope is disposed.<br/>
    /// Execution-context flow preserves the policy across asynchronous continuations while isolating unrelated concurrent requests.<br/>
    /// </summary>
    /// <param name="policy">Validated relationship-hydration policy for the current application operation.<br/></param>
    /// <returns>A disposable scope that restores the exact preceding ambient value.<br/></returns>
    internal static IDisposable Push(CustomerOrderHydrationPolicy policy)
    {
        Frame? prior = current.Value;
        current.Value = new(policy);
        return new Lease(prior);
    }

    /// <summary>Retains one immutable policy frame so nested scopes can restore rather than overwrite caller intent.<br/></summary>
    /// <param name="Policy">Policy visible to the registered relationship resolver.<br/></param>
    internal sealed record Frame(CustomerOrderHydrationPolicy Policy);

    /// <summary>Restores the ambient predecessor exactly once when one hydration operation leaves its scope.<br/></summary>
    private sealed class Lease : IDisposable
    {
        private Frame? prior;
        private bool disposed;

        /// <summary>Captures the ambient predecessor that must be restored when this scope completes.<br/></summary>
        /// <param name="prior">Previous frame, or <see langword="null"/> when the caller began without a policy.<br/></param>
        internal Lease(Frame? prior) => this.prior = prior;

        /// <summary>Restores the preceding frame once and makes repeated disposal an allocation-free no-op.<br/></summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            current.Value = prior;
            prior = null;
        }
    }
}
