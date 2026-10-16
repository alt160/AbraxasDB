using AbraxasDB;
using System.Diagnostics;

namespace AbraxasShowcase;

/// <summary>Demonstrates value-only iteration and caller-owned stack-friendly analysis state over stored records.<br/></summary>
internal static class AnalysisScenarios
{
    internal static IReadOnlyList<IShowcaseScenario> All { get; } =
    [
        new CatalogActionScenario("iterate-selected-values", "Scale and performance", "Analyze selected values without objects",
            "Read just Total and OrderedAt from each order without constructing a ShowcaseOrder per row.", "Full traversal", false,
            new ScenarioDocumentation("Need to calculate over many orders without building an object for each one? A value reader returns the selected members as typed accessors. The example reports measured allocations; it does not promise the whole pipeline allocates nothing.",
                ["selected values", "value accessor", "allocation measurement"], ["OpenValuesReader", "ValueAccessor.AsDecimal", "ValueAccessor.AsDateTime"],
                ["value reader", "allocation", "analysis", "struct"], ["Traverse complete objects for throughput"], 840),
            "Scale/SelectedValueAnalysis.cs.txt", SelectedValues),
        new CatalogActionScenario("analyze-custom-struct", "Scale and performance", "Analyze records with a custom struct",
            "Fill your own struct from three selected values, then compare with reading complete order objects.", "Full traversal", false,
            new ScenarioDocumentation("If a reporting loop needs several fields per order, compare two ways to fill your own struct: GetAs<TStruct>() and a value reader whose fields you assemble explicitly. Neither requires your own reference-type row; the measured allocations show the cost of each route.",
                ["custom value type", "multiple selected values", "allocation comparison"], ["OpenValuesReader", "ValueAccessor", "RowReader.GetAs<TOut>"],
                ["custom struct", "analysis", "allocation", "GC", "projection"], ["Analyze selected values without objects"], 850),
            "Scale/CustomStructAnalysis.cs.txt", CustomStruct)
    ];

    /// <summary>Iterates two typed members without materializing the complete order object for each row.<br/></summary>
    /// <param name="workspace">Prepared Scale and performance group whose order store is read.<br/></param>
    /// <param name="token">Cancellation signal observed between rows.<br/></param>
    /// <returns>Aggregate result and observed current-thread allocation.<br/></returns>
    private static ScenarioResult SelectedValues(ShowcaseWorkspace workspace, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        using (var warmup = workspace.Orders.OpenValuesReader(".Total", ".OrderedAt"))
            while (warmup.Read())
                _ = (warmup.Get(0).AsDecimal ?? 0m) + (warmup.Get(1).AsDateTime?.Month ?? 0);
        using var reader = workspace.Orders.OpenValuesReader(".Total", ".OrderedAt");
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        decimal total = 0m;
        int rows = 0;
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            decimal amount = reader.Get(0).AsDecimal ?? 0m;
            DateTime orderedAt = reader.Get(1).AsDateTime ?? default;
            total += amount * (orderedAt.Month == 0 ? 1 : orderedAt.Month);
            rows++;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        watch.Stop();
        return new ScenarioResult(rows > 0, "Read two selected values per order without constructing an order object in application code.",
            [new("Orders visited", rows.ToString()), new("Observed loop allocation", allocated.ToString() + " bytes"), new("Weighted total", total.ToString("0.00"))],
            total, typeof(decimal), watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Fills a caller-owned struct from three borrowed values and compares its analysis with full-object hydration.<br/></summary>
    /// <param name="workspace">Prepared Scale and performance group whose order store is read.<br/></param>
    /// <param name="token">Cancellation signal observed between rows.<br/></param>
    /// <returns>Matching checksums and allocation observations from both traversal styles.<br/></returns>
    private static ScenarioResult CustomStruct(ShowcaseWorkspace workspace, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        using (var warmValues = workspace.Orders.OpenValuesReader(".Total", ".OrderedAt", ".CustomerID"))
            while (warmValues.Read())
                _ = new ShowcaseOrderAnalysisRow(warmValues.Get(0).AsDecimal ?? 0m,
                    warmValues.Get(1).AsDateTime ?? default, warmValues.Get(2).AsUInt64 ?? 0UL).Score();
        using (var warmObjects = workspace.Orders.Readers.Open())
            while (warmObjects.Read())
                _ = warmObjects.Get().Total;
        using (var warmProjected = workspace.Orders.Readers.Open())
            while (warmProjected.Read())
                _ = warmProjected.GetAs<ShowcaseOrderAnalysisRow>().Score();
        decimal valueChecksum = 0m;
        int valueRows = 0;
        long valueBytes;
        using (var values = workspace.Orders.OpenValuesReader(".Total", ".OrderedAt", ".CustomerID"))
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            while (values.Read())
            {
                token.ThrowIfCancellationRequested();
                var row = new ShowcaseOrderAnalysisRow(values.Get(0).AsDecimal ?? 0m,
                    values.Get(1).AsDateTime ?? default, values.Get(2).AsUInt64 ?? 0UL);
                valueChecksum += row.Score();
                valueRows++;
            }
            valueBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        decimal objectChecksum = 0m;
        int objectRows = 0;
        long objectBytes;
        using (var objects = workspace.Orders.Readers.Open())
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            while (objects.Read())
            {
                token.ThrowIfCancellationRequested();
                ShowcaseOrder order = objects.Get();
                objectChecksum += new ShowcaseOrderAnalysisRow(order.Total, order.OrderedAt, order.CustomerID).Score();
                objectRows++;
            }
            objectBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        decimal projectedChecksum = 0m;
        int projectedRows = 0;
        long projectedBytes;
        using (var projected = workspace.Orders.Readers.Open())
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            while (projected.Read())
            {
                token.ThrowIfCancellationRequested();
                ShowcaseOrderAnalysisRow row = projected.GetAs<ShowcaseOrderAnalysisRow>();
                projectedChecksum += row.Score();
                projectedRows++;
            }
            projectedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        watch.Stop();
        bool passed = valueRows > 0 && valueRows == objectRows && valueRows == projectedRows
            && valueChecksum == objectChecksum && valueChecksum == projectedChecksum;
        return new ScenarioResult(passed, passed ? "Both struct paths produced the same analysis as full-object reads." : "The analysis traversals did not agree.",
            [new("Orders visited", valueRows.ToString()), new("Struct/value loop allocation", valueBytes.ToString() + " bytes"),
             new("GetAs<struct> loop allocation", projectedBytes.ToString() + " bytes"),
             new("Full-object loop allocation", objectBytes.ToString() + " bytes"), new("Analysis score", valueChecksum.ToString("0.00"))],
            valueChecksum, typeof(decimal), watch.Elapsed.TotalMilliseconds);
    }
}

/// <summary>Caller-defined, reference-free row of selected order values used for one multi-field calculation.<br/></summary>
public struct ShowcaseOrderAnalysisRow
{
    public decimal Total { get; set; }
    public DateTime OrderedAt { get; set; }
    public ulong CustomerID { get; set; }

    /// <summary>Copies three selected scalar values into application-owned value-type state without creating a row object.<br/></summary>
    /// <param name="total">Stored monetary total.<br/></param>
    /// <param name="orderedAt">Stored order timestamp.<br/></param>
    /// <param name="customerID">Stored external customer identity.<br/></param>
    public ShowcaseOrderAnalysisRow(decimal total, DateTime orderedAt, ulong customerID)
    {
        Total = total;
        OrderedAt = orderedAt;
        CustomerID = customerID;
    }

    /// <summary>Combines amount, calendar month, and customer shard into one deterministic analysis score.<br/></summary>
    /// <returns>Multi-field score calculated without a per-row reference object.<br/></returns>
    public decimal Score() => Total * ((OrderedAt.Month % 4) + 1) + (CustomerID % 7);
}
