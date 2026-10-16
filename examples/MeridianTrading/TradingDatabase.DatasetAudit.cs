using AbraxasDB;
using System.Diagnostics;

namespace MeridianTrading;

internal sealed partial class TradingDatabase
{
    private const int DatasetAuditProgressInterval = 8_192;

    /// <summary>
    /// Performs an explicit order-independent hydration audit over every order header and order line in the current workspace.<br/>
    /// The workflow deliberately uses <c>Readers.OpenUnordered()</c> because it needs complete objects but has no ordering requirement; indexed aggregates supply independent expected counts.<br/>
    /// Registered graph links remain suppressed so the scan measures each normalized store exactly once instead of recursively hydrating order lines through their parent orders.<br/>
    /// Progress reports distinguish snapshot preparation, determinate record consumption, and completion without fabricating progress inside Fractal snapshot construction.<br/>
    /// </summary>
    /// <param name="progress">Optional callback receiving bounded real progress updates.<br/></param>
    /// <param name="cancellationToken">Cancellation observed between bounded scan intervals.<br/></param>
    /// <returns>A serializable audit report containing counts, totals, integrity checks, and one order-independent identity fingerprint per store.<br/></returns>
    internal object AuditDataset(
        Action<DatasetAuditProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var elapsed = Stopwatch.StartNew();
        var checks = new List<DatasetAuditCheck>(7);
        ActionTiming.Phase("Abraxas — counting live normalized records");
        progress?.Invoke(new DatasetAuditProgress(
            "Counting live records",
            0,
            0,
            0,
            0,
            false,
            0,
            0,
            "aggregate calls",
            "Reading independent live counts from the order and line indexes",
            elapsed.Elapsed.TotalMilliseconds));

        DatasetAuditTimingStart countStart = StartDatasetAuditTiming();
        ulong expectedOrders;
        ulong expectedLines;
        using (ActionTiming.Db())
        {
            expectedOrders = Orders.Aggregates.Count();
            expectedLines = Lines.Aggregates.Count();
        }
        DatasetAuditTiming countTiming = StopDatasetAuditTiming(countStart);
        checks.Add(new DatasetAuditCheck(
            "Live record baseline",
            true,
            "Orders.Aggregates.Count() + Lines.Aggregates.Count()",
            2,
            "aggregate calls",
            false,
            0,
            countTiming.ElapsedMs,
            countTiming.DatabaseMs,
            countTiming.CalculationMs,
            $"{expectedOrders:N0} orders and {expectedLines:N0} lines reported by independent live aggregate indexes."));

        progress?.Invoke(new DatasetAuditProgress(
            "Preparing order snapshot",
            0,
            0,
            expectedOrders,
            expectedLines,
            false,
            0,
            expectedOrders,
            "orders",
            $"Preparing an unordered snapshot for {expectedOrders:N0} order headers",
            elapsed.Elapsed.TotalMilliseconds));
        DatasetAuditTimingStart orderStart = StartDatasetAuditTiming();
        DatasetStoreAudit orders = AuditOrders(
            expectedOrders,
            expectedLines,
            elapsed,
            progress,
            cancellationToken);
        DatasetAuditTiming orderTiming = StopDatasetAuditTiming(orderStart);
        checks.Add(new DatasetAuditCheck(
            "Order hydration + invariants",
            orders.Count == expectedOrders && orders.InvalidRecords == 0,
            "Orders.Readers.OpenUnordered() · ReadNoLinks/GetNoLinks",
            orders.Count,
            "order objects",
            true,
            orders.InvalidRecords + CountDifference(orders.Count, expectedOrders),
            orderTiming.ElapsedMs,
            orderTiming.DatabaseMs,
            orderTiming.CalculationMs,
            $"Hydrated {orders.Count:N0} of {expectedOrders:N0}; {orders.InvalidRecords:N0} scalar-invariant findings; links suppressed; identity XOR {orders.IdentityXor}."));

        progress?.Invoke(new DatasetAuditProgress(
            "Preparing order-line snapshot",
            orders.Count,
            0,
            expectedOrders,
            expectedLines,
            false,
            0,
            expectedLines,
            "line objects",
            $"Preparing an unordered snapshot for {expectedLines:N0} normalized order lines",
            elapsed.Elapsed.TotalMilliseconds));
        DatasetAuditTimingStart lineStart = StartDatasetAuditTiming();
        DatasetStoreAudit lines = AuditLines(
            orders.Count,
            expectedOrders,
            expectedLines,
            elapsed,
            progress,
            cancellationToken);
        DatasetAuditTiming lineTiming = StopDatasetAuditTiming(lineStart);
        checks.Add(new DatasetAuditCheck(
            "Order-line hydration + invariants",
            lines.Count == expectedLines && lines.InvalidRecords == 0,
            "Lines.Readers.OpenUnordered() · ReadNoLinks/GetNoLinks",
            lines.Count,
            "line objects",
            true,
            lines.InvalidRecords + CountDifference(lines.Count, expectedLines),
            lineTiming.ElapsedMs,
            lineTiming.DatabaseMs,
            lineTiming.CalculationMs,
            $"Hydrated {lines.Count:N0} of {expectedLines:N0}; {lines.InvalidRecords:N0} scalar-invariant findings; {lines.Units:N0} units; identity XOR {lines.IdentityXor}."));

        DatasetAuditTimingStart customerReferenceStart = StartDatasetAuditTiming();
        DatasetReferenceAudit customerReferences = AuditCustomerReferences(
            orders.Count,
            lines.Count,
            expectedOrders,
            expectedLines,
            elapsed,
            progress,
            cancellationToken);
        DatasetAuditTiming customerReferenceTiming = StopDatasetAuditTiming(customerReferenceStart);
        checks.Add(new DatasetAuditCheck(
            "Customer reference integrity",
            customerReferences.OrphanRecords == 0 && customerReferences.DependentRecords == expectedOrders,
            "Orders .CustomerID grouped scalar index → Customers.Exists(identity)",
            customerReferences.DistinctKeys,
            "distinct keys",
            true,
            customerReferences.OrphanRecords + CountDifference(customerReferences.DependentRecords, expectedOrders),
            customerReferenceTiming.ElapsedMs,
            customerReferenceTiming.DatabaseMs,
            customerReferenceTiming.CalculationMs,
            $"{customerReferences.DependentRecords:N0} orders covered by {customerReferences.DistinctKeys:N0} indexed keys and {customerReferences.DistinctKeys:N0} direct existence probes; {customerReferences.MissingKeys:N0} missing keys affect {customerReferences.OrphanRecords:N0} orders."));

        DatasetAuditTimingStart productReferenceStart = StartDatasetAuditTiming();
        DatasetReferenceAudit productReferences = AuditProductReferences(
            orders.Count,
            lines.Count,
            expectedOrders,
            expectedLines,
            elapsed,
            progress,
            cancellationToken);
        DatasetAuditTiming productReferenceTiming = StopDatasetAuditTiming(productReferenceStart);
        checks.Add(new DatasetAuditCheck(
            "Product reference integrity",
            productReferences.OrphanRecords == 0 && productReferences.DependentRecords == expectedLines,
            "Lines .ProductID grouped scalar index → Products.Exists(identity)",
            productReferences.DistinctKeys,
            "distinct keys",
            true,
            productReferences.OrphanRecords + CountDifference(productReferences.DependentRecords, expectedLines),
            productReferenceTiming.ElapsedMs,
            productReferenceTiming.DatabaseMs,
            productReferenceTiming.CalculationMs,
            $"{productReferences.DependentRecords:N0} lines covered by {productReferences.DistinctKeys:N0} indexed keys and {productReferences.DistinctKeys:N0} direct existence probes; {productReferences.MissingKeys:N0} missing keys affect {productReferences.OrphanRecords:N0} lines."));

        DatasetAuditTimingStart membershipStart = StartDatasetAuditTiming();
        DatasetPackedMembershipAudit packedMembership = AuditPackedLineMembership(
            orders.Count,
            lines.Count,
            expectedOrders,
            expectedLines,
            elapsed,
            progress,
            cancellationToken);
        DatasetAuditTiming membershipTiming = StopDatasetAuditTiming(membershipStart);
        ulong membershipFindings = checked(
            packedMembership.BrokenOrders +
            packedMembership.DuplicateLineIDs +
            CountDifference(packedMembership.LinkedLines, expectedLines) +
            CountDifference(packedMembership.Orders, expectedOrders));
        checks.Add(new DatasetAuditCheck(
            "Packed order-line membership",
            membershipFindings == 0 && packedMembership.UniqueLineIDs == expectedLines,
            "Orders.Readers.OpenUnordered() · registered .Lines hook → Lines.GetMany(ids)",
            checked(packedMembership.Orders + packedMembership.LinkedLines),
            "hydrated objects",
            true,
            membershipFindings,
            membershipTiming.ElapsedMs,
            membershipTiming.DatabaseMs,
            membershipTiming.CalculationMs,
            $"{packedMembership.Orders:N0} order manifests linked {packedMembership.LinkedLines:N0} existing children; {packedMembership.UniqueLineIDs:N0} unique IDs, {packedMembership.DuplicateLineIDs:N0} duplicates, {packedMembership.BrokenOrders:N0} broken manifests; temporary identity buffer {packedMembership.TemporaryBytes / 1048576d:N2} MiB."));

        DatasetAuditTimingStart parityStart = StartDatasetAuditTiming();
        decimal totalDifference;
        using (ActionTiming.Calculate())
            totalDifference = Math.Abs(orders.Total - lines.Total);
        DatasetAuditTiming parityTiming = StopDatasetAuditTiming(parityStart);
        checks.Add(new DatasetAuditCheck(
            "Normalized monetary parity",
            totalDifference == 0m,
            "Meridian decimal comparison of independently folded store totals",
            2,
            "store totals",
            false,
            totalDifference == 0m ? 0UL : 1UL,
            parityTiming.ElapsedMs,
            parityTiming.DatabaseMs,
            parityTiming.CalculationMs,
            $"Order headers {orders.Total:C2}; normalized lines {lines.Total:C2}; absolute difference {totalDifference:C2}."));

        bool passed = checks.All(static check => check.Passed);
        ulong scannedRecords = checked(orders.Count + lines.Count);
        ulong hydratedObjects = checked(scannedRecords + packedMembership.Orders + packedMembership.LinkedLines);
        double measuredTestMs = checks.Sum(static check => check.ElapsedMs);
        elapsed.Stop();
        double unattributedMs = Math.Max(0d, elapsed.Elapsed.TotalMilliseconds - measuredTestMs);

        ActionTiming.Phase(passed ? "Hydration audit complete" : "Hydration audit found inconsistencies");
        progress?.Invoke(new DatasetAuditProgress(
            passed ? "Complete" : "Complete with findings",
            orders.Count,
            lines.Count,
            expectedOrders,
            expectedLines,
            true,
            1,
            1,
            "audit",
            $"{checks.Count:N0} measured tests · {hydratedObjects:N0} hydrated objects",
            elapsed.Elapsed.TotalMilliseconds));

        return new DatasetAuditResult(
            passed,
            "Public Abraxas readers, aggregates, complete scalar indexes, direct identity probes, and registered links",
            scannedRecords,
            hydratedObjects,
            orders,
            lines,
            customerReferences,
            productReferences,
            packedMembership,
            totalDifference,
            elapsed.Elapsed.TotalMilliseconds,
            measuredTestMs,
            unattributedMs,
            checks.ToArray());
    }

    /// <summary>
    /// Hydrates every order header in physical order and folds only scalar validation data while the current payload remains borrowed.<br/>
    /// Abraxas time includes reader preparation, traversal, hydration, and disposal; application validation is separately attributed through nested calculation scopes.<br/>
    /// </summary>
    /// <param name="expectedOrders">Independent expected order count.<br/></param>
    /// <param name="expectedLines">Independent expected line count used by the shared progress denominator.<br/></param>
    /// <param name="elapsed">Whole-audit timer used only for progress status.<br/></param>
    /// <param name="progress">Optional bounded progress callback.<br/></param>
    /// <param name="cancellationToken">Cancellation observed at least once per progress interval.<br/></param>
    /// <returns>Folded order audit values.<br/></returns>
    private DatasetStoreAudit AuditOrders(
        ulong expectedOrders,
        ulong expectedLines,
        Stopwatch elapsed,
        Action<DatasetAuditProgress>? progress,
        CancellationToken cancellationToken)
    {
        ActionTiming.Phase("Abraxas — preparing unordered order snapshot");
        RecordStore<SalesOrder>.RowReader reader;
        using (ActionTiming.Db())
            reader = Orders.Readers.OpenUnordered();

        ulong count = 0;
        ulong invalid = 0;
        ulong identityXor = 0;
        decimal total = 0m;
        try
        {
            bool exhausted = false;
            while (!exhausted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (ActionTiming.Db())
                {
                    for (int index = 0; index < DatasetAuditProgressInterval; index++)
                    {
                        if (!reader.ReadNoLinks())
                        {
                            exhausted = true;
                            break;
                        }

                        SalesOrder order = reader.GetNoLinks();
                        ulong identity = reader.ID;
                        using (ActionTiming.Calculate())
                        {
                            count++;
                            identityXor ^= identity;
                            total += order.Total;
                            if (string.IsNullOrWhiteSpace(order.Number) ||
                                order.CustomerID == 0 ||
                                string.IsNullOrWhiteSpace(order.Region) ||
                                string.IsNullOrWhiteSpace(order.Status) ||
                                order.Total < 0m ||
                                order.Lines is not null)
                            {
                                invalid++;
                            }
                        }
                    }
                }

                ActionTiming.Phase("Abraxas — hydrating order headers");
                progress?.Invoke(new DatasetAuditProgress(
                    "Scanning orders",
                    count,
                    0,
                    expectedOrders,
                    expectedLines,
                    true,
                    count,
                    expectedOrders,
                    "order objects",
                    $"{count:N0} / {expectedOrders:N0} order headers hydrated without links",
                    elapsed.Elapsed.TotalMilliseconds));
            }
        }
        finally
        {
            using (ActionTiming.Db())
                reader.Dispose();
        }

        return new DatasetStoreAudit(count, expectedOrders, total, invalid, identityXor, 0);
    }

    /// <summary>
    /// Hydrates every normalized order line in physical order and folds quantities and monetary totals without constructing parent-order graphs.<br/>
    /// The reader is disposed through the measured Abraxas boundary even when cancellation or materialization fails, preserving the store snapshot lane.<br/>
    /// </summary>
    /// <param name="completedOrders">Actual order count already scanned for overall progress.<br/></param>
    /// <param name="expectedOrders">Independent expected order count.<br/></param>
    /// <param name="expectedLines">Independent expected line count.<br/></param>
    /// <param name="elapsed">Whole-audit timer used only for progress status.<br/></param>
    /// <param name="progress">Optional bounded progress callback.<br/></param>
    /// <param name="cancellationToken">Cancellation observed at least once per progress interval.<br/></param>
    /// <returns>Folded line audit values.<br/></returns>
    private DatasetStoreAudit AuditLines(
        ulong completedOrders,
        ulong expectedOrders,
        ulong expectedLines,
        Stopwatch elapsed,
        Action<DatasetAuditProgress>? progress,
        CancellationToken cancellationToken)
    {
        ActionTiming.Phase("Abraxas — preparing unordered order-line snapshot");
        RecordStore<OrderLine>.RowReader reader;
        using (ActionTiming.Db())
            reader = Lines.Readers.OpenUnordered();

        ulong count = 0;
        ulong invalid = 0;
        ulong identityXor = 0;
        long units = 0;
        decimal total = 0m;
        try
        {
            bool exhausted = false;
            while (!exhausted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (ActionTiming.Db())
                {
                    for (int index = 0; index < DatasetAuditProgressInterval; index++)
                    {
                        if (!reader.ReadNoLinks())
                        {
                            exhausted = true;
                            break;
                        }

                        OrderLine line = reader.GetNoLinks();
                        ulong identity = reader.ID;
                        using (ActionTiming.Calculate())
                        {
                            count++;
                            identityXor ^= identity;
                            units = checked(units + line.Quantity);
                            total += line.Total;
                            if (line.ProductID == 0 ||
                                string.IsNullOrWhiteSpace(line.SKU) ||
                                line.Quantity <= 0 ||
                                line.UnitPrice < 0m ||
                                line.Total < 0m ||
                                line.Total != line.Quantity * line.UnitPrice)
                            {
                                invalid++;
                            }
                        }
                    }
                }

                ActionTiming.Phase("Abraxas — hydrating normalized order lines");
                progress?.Invoke(new DatasetAuditProgress(
                    "Scanning order lines",
                    completedOrders,
                    count,
                    expectedOrders,
                    expectedLines,
                    true,
                    count,
                    expectedLines,
                    "line objects",
                    $"{count:N0} / {expectedLines:N0} normalized order lines hydrated without links",
                    elapsed.Elapsed.TotalMilliseconds));
            }
        }
        finally
        {
            using (ActionTiming.Db())
                reader.Dispose();
        }

        return new DatasetStoreAudit(count, expectedLines, total, invalid, identityXor, units);
    }

    /// <summary>
    /// Verifies every distinct child-held customer identity through the complete <c>.CustomerID</c> scalar index and one existence-only master probe per key.<br/>
    /// Group counts prove that the indexed relationship covers the same number of order records as the independent live baseline without hydrating order or customer objects.<br/>
    /// Malformed, cross-store, and absent customer identities are reported as relationship findings instead of terminating the remaining audit.<br/>
    /// </summary>
    /// <param name="completedOrders">Order objects already hydrated by the full-object pass.<br/></param>
    /// <param name="completedLines">Line objects already hydrated by the full-object pass.<br/></param>
    /// <param name="expectedOrders">Independent expected live order count.<br/></param>
    /// <param name="expectedLines">Independent expected live line count retained in progress packets.<br/></param>
    /// <param name="elapsed">Whole-audit timer used only for progress status.<br/></param>
    /// <param name="progress">Optional bounded progress callback.<br/></param>
    /// <param name="cancellationToken">Cancellation observed between distinct relationship keys.<br/></param>
    /// <returns>Indexed key coverage and orphan counts for the customer relationship.<br/></returns>
    private DatasetReferenceAudit AuditCustomerReferences(
        ulong completedOrders,
        ulong completedLines,
        ulong expectedOrders,
        ulong expectedLines,
        Stopwatch elapsed,
        Action<DatasetAuditProgress>? progress,
        CancellationToken cancellationToken)
    {
        ActionTiming.Phase("Abraxas — validating indexed customer references");
        using (ActionTiming.Db())
        {
            if (!Orders.TryGetIndex(".CustomerID", out _))
                throw new InvalidOperationException("The ready Meridian dataset is missing its complete .CustomerID relationship index.");
        }

        var definition = Orders.Aggregates.GroupBy(order => order.CustomerID).AddKey().AddCount();
        var reader = Orders.Aggregates.Open(definition);
        ulong records = 0;
        ulong distinctKeys = 0;
        ulong existingKeys = 0;
        ulong missingKeys = 0;
        ulong orphanRecords = 0;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool hasRow;
                (ulong Value1, ulong Value2) row = default;
                using (ActionTiming.Db())
                {
                    hasRow = reader.Read();
                    if (hasRow) row = reader.Get();
                }
                if (!hasRow) break;

                bool exists;
                try
                {
                    using (ActionTiming.Db())
                        exists = Customers.Exists(row.Value1);
                }
                catch (ArgumentException)
                {
                    exists = false;
                }

                using (ActionTiming.Calculate())
                {
                    distinctKeys++;
                    records = checked(records + row.Value2);
                    if (exists) existingKeys++;
                    else
                    {
                        missingKeys++;
                        orphanRecords = checked(orphanRecords + row.Value2);
                    }
                }

                if (distinctKeys % 256 == 0)
                {
                    progress?.Invoke(new DatasetAuditProgress(
                        "Checking customer references",
                        completedOrders,
                        completedLines,
                        expectedOrders,
                        expectedLines,
                        false,
                        distinctKeys,
                        0,
                        "distinct customer keys",
                        $"{distinctKeys:N0} indexed keys checked · {records:N0} orders covered",
                        elapsed.Elapsed.TotalMilliseconds));
                }
            }
        }
        finally
        {
            using (ActionTiming.Db())
                reader.Dispose();
        }

        progress?.Invoke(new DatasetAuditProgress(
            "Customer references complete",
            completedOrders,
            completedLines,
            expectedOrders,
            expectedLines,
            true,
            records,
            expectedOrders,
            "order references",
            $"{records:N0} order references · {missingKeys:N0} missing customer keys",
            elapsed.Elapsed.TotalMilliseconds));
        return new DatasetReferenceAudit(records, distinctKeys, existingKeys, missingKeys, orphanRecords);
    }

    /// <summary>
    /// Verifies every distinct child-held product identity through the complete <c>.ProductID</c> scalar index and one existence-only master probe per key.<br/>
    /// The grouped index reduces a million-line dataset to its distinct product keys while preserving a summed dependent-record count for coverage parity.<br/>
    /// Malformed, cross-store, and absent product identities remain visible findings while unrelated keys continue to be checked.<br/>
    /// </summary>
    /// <param name="completedOrders">Order objects already hydrated by the full-object pass.<br/></param>
    /// <param name="completedLines">Line objects already hydrated by the full-object pass.<br/></param>
    /// <param name="expectedOrders">Independent expected live order count retained in progress packets.<br/></param>
    /// <param name="expectedLines">Independent expected live line count.<br/></param>
    /// <param name="elapsed">Whole-audit timer used only for progress status.<br/></param>
    /// <param name="progress">Optional bounded progress callback.<br/></param>
    /// <param name="cancellationToken">Cancellation observed between distinct relationship keys.<br/></param>
    /// <returns>Indexed key coverage and orphan counts for the product relationship.<br/></returns>
    private DatasetReferenceAudit AuditProductReferences(
        ulong completedOrders,
        ulong completedLines,
        ulong expectedOrders,
        ulong expectedLines,
        Stopwatch elapsed,
        Action<DatasetAuditProgress>? progress,
        CancellationToken cancellationToken)
    {
        ActionTiming.Phase("Abraxas — validating indexed product references");
        using (ActionTiming.Db())
        {
            if (!Lines.TryGetIndex(".ProductID", out _))
                throw new InvalidOperationException("The ready Meridian dataset is missing its complete .ProductID relationship index.");
        }

        var definition = Lines.Aggregates.GroupBy(line => line.ProductID).AddKey().AddCount();
        var reader = Lines.Aggregates.Open(definition);
        ulong records = 0;
        ulong distinctKeys = 0;
        ulong existingKeys = 0;
        ulong missingKeys = 0;
        ulong orphanRecords = 0;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool hasRow;
                (ulong Value1, ulong Value2) row = default;
                using (ActionTiming.Db())
                {
                    hasRow = reader.Read();
                    if (hasRow) row = reader.Get();
                }
                if (!hasRow) break;

                bool exists;
                try
                {
                    using (ActionTiming.Db())
                        exists = Products.Exists(row.Value1);
                }
                catch (ArgumentException)
                {
                    exists = false;
                }

                using (ActionTiming.Calculate())
                {
                    distinctKeys++;
                    records = checked(records + row.Value2);
                    if (exists) existingKeys++;
                    else
                    {
                        missingKeys++;
                        orphanRecords = checked(orphanRecords + row.Value2);
                    }
                }

                if (distinctKeys % 256 == 0)
                {
                    progress?.Invoke(new DatasetAuditProgress(
                        "Checking product references",
                        completedOrders,
                        completedLines,
                        expectedOrders,
                        expectedLines,
                        false,
                        distinctKeys,
                        0,
                        "distinct product keys",
                        $"{distinctKeys:N0} indexed keys checked · {records:N0} lines covered",
                        elapsed.Elapsed.TotalMilliseconds));
                }
            }
        }
        finally
        {
            using (ActionTiming.Db())
                reader.Dispose();
        }

        progress?.Invoke(new DatasetAuditProgress(
            "Product references complete",
            completedOrders,
            completedLines,
            expectedOrders,
            expectedLines,
            true,
            records,
            expectedLines,
            "line references",
            $"{records:N0} line references · {missingKeys:N0} missing product keys",
            elapsed.Elapsed.TotalMilliseconds));
        return new DatasetReferenceAudit(records, distinctKeys, existingKeys, missingKeys, orphanRecords);
    }

    /// <summary>
    /// Hydrates every order's registered packed <c>.Lines</c> relationship and proves exact membership against the independent live line count.<br/>
    /// The hook decodes ordered child identities and calls <c>Lines.GetMany(ids)</c>; each returned identity therefore names an existing line in the correct store.<br/>
    /// One compact eight-byte slot per expected line is sorted to detect duplicate membership; existing children, no duplicates, and equal cardinality together prove that every live line is linked exactly once.<br/>
    /// </summary>
    /// <param name="completedOrders">Order objects already hydrated by the link-suppressed pass.<br/></param>
    /// <param name="completedLines">Line objects already hydrated by the normalized line pass.<br/></param>
    /// <param name="expectedOrders">Independent expected live order count.<br/></param>
    /// <param name="expectedLines">Independent expected live line count and temporary identity capacity.<br/></param>
    /// <param name="elapsed">Whole-audit timer used only for progress status.<br/></param>
    /// <param name="progress">Optional bounded progress callback.<br/></param>
    /// <param name="cancellationToken">Cancellation observed at least once per bounded order chunk and before sorting.<br/></param>
    /// <returns>Exact packed-membership counts and bounded temporary-storage usage.<br/></returns>
    private DatasetPackedMembershipAudit AuditPackedLineMembership(
        ulong completedOrders,
        ulong completedLines,
        ulong expectedOrders,
        ulong expectedLines,
        Stopwatch elapsed,
        Action<DatasetAuditProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (expectedLines > int.MaxValue)
            throw new InvalidOperationException("The exact packed-membership audit currently supports up to 2,147,483,647 live order lines.");

        ActionTiming.Phase("Abraxas — preparing packed order-line relationship snapshot");
        progress?.Invoke(new DatasetAuditProgress(
            "Preparing packed line relationships",
            completedOrders,
            completedLines,
            expectedOrders,
            expectedLines,
            false,
            0,
            expectedOrders,
            "orders",
            $"Allocating {expectedLines * sizeof(ulong) / 1048576d:N2} MiB for exact linked-identity verification",
            elapsed.Elapsed.TotalMilliseconds));

        ulong[] linkedIdentities;
        using (ActionTiming.Calculate())
            linkedIdentities = GC.AllocateUninitializedArray<ulong>(checked((int)expectedLines));

        RecordStore<SalesOrder>.RowReader reader;
        using (ActionTiming.Db())
            reader = Orders.Readers.OpenUnordered();

        ulong orders = 0;
        ulong linkedLines = 0;
        ulong brokenOrders = 0;
        int retainedIdentities = 0;
        try
        {
            bool exhausted = false;
            while (!exhausted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using (ActionTiming.Db())
                {
                    for (int index = 0; index < DatasetAuditProgressInterval; index++)
                    {
                        if (!reader.Read())
                        {
                            exhausted = true;
                            break;
                        }

                        SalesOrder? order = null;
                        try
                        {
                            order = reader.Get();
                        }
                        catch (Exception error) when (IsPackedMembershipFinding(error))
                        {
                            using (ActionTiming.Calculate())
                            {
                                orders++;
                                brokenOrders++;
                            }
                        }

                        if (order is null) continue;
                        using (ActionTiming.Calculate())
                        {
                            orders++;
                            foreach (OrderLine line in order.Lines ?? [])
                            {
                                if (retainedIdentities < linkedIdentities.Length)
                                    linkedIdentities[retainedIdentities++] = line.ID;
                                linkedLines++;
                            }
                        }
                    }
                }

                ActionTiming.Phase("Abraxas — hydrating packed order-line relationships");
                progress?.Invoke(new DatasetAuditProgress(
                    "Hydrating packed line relationships",
                    completedOrders,
                    completedLines,
                    expectedOrders,
                    expectedLines,
                    true,
                    orders,
                    expectedOrders,
                    "orders",
                    $"{orders:N0} / {expectedOrders:N0} order manifests · {linkedLines:N0} existing child lines",
                    elapsed.Elapsed.TotalMilliseconds));
            }
        }
        finally
        {
            using (ActionTiming.Db())
                reader.Dispose();
        }

        cancellationToken.ThrowIfCancellationRequested();
        ActionTiming.Phase("Meridian — checking exact packed membership uniqueness");
        progress?.Invoke(new DatasetAuditProgress(
            "Checking membership uniqueness",
            completedOrders,
            completedLines,
            expectedOrders,
            expectedLines,
            false,
            checked((ulong)retainedIdentities),
            expectedLines,
            "linked identities",
            $"Sorting {retainedIdentities:N0} linked line identities to detect duplicates",
            elapsed.Elapsed.TotalMilliseconds));

        ulong uniqueLineIDs = 0;
        ulong duplicateLineIDs = 0;
        using (ActionTiming.Calculate())
        {
            Array.Sort(linkedIdentities, 0, retainedIdentities);
            if (retainedIdentities > 0) uniqueLineIDs = 1;
            for (int index = 1; index < retainedIdentities; index++)
            {
                if (linkedIdentities[index] == linkedIdentities[index - 1]) duplicateLineIDs++;
                else uniqueLineIDs++;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new DatasetPackedMembershipAudit(
            orders,
            linkedLines,
            uniqueLineIDs,
            duplicateLineIDs,
            brokenOrders,
            checked((long)linkedIdentities.LongLength * sizeof(ulong)));
    }

    /// <summary>
    /// Identifies expected data-integrity failures raised while the registered packed relationship decodes IDs and retrieves children.<br/>
    /// Other exceptions remain fatal because swallowing engine, allocation, or programming failures would misreport an incomplete audit as ordinary corrupt data.<br/>
    /// </summary>
    /// <param name="error">Exception raised by one order's registered link materializer.<br/></param>
    /// <returns><see langword="true"/> only for malformed payloads, cross-store IDs, or absent child records.<br/></returns>
    private static bool IsPackedMembershipFinding(Exception error)
        => error is InvalidDataException or KeyNotFoundException or ArgumentException;

    /// <summary>
    /// Captures the wall-clock and exclusive request counters at the beginning of one displayed Dataset audit test.<br/>
    /// The returned value is allocation-free and does not alter the active Abraxas/calculation category.<br/>
    /// </summary>
    /// <returns>Start state used by <see cref="StopDatasetAuditTiming"/>.<br/></returns>
    private static DatasetAuditTimingStart StartDatasetAuditTiming()
        => new(Stopwatch.GetTimestamp(), ActionTiming.Capture());

    /// <summary>
    /// Completes one displayed Dataset audit test and reconciles its wall time with exclusive Abraxas and application/calculation deltas.<br/>
    /// Small residual time is retained by the UI as boundary overhead instead of being mislabeled as storage or calculation work.<br/>
    /// </summary>
    /// <param name="start">Previously captured test start state.<br/></param>
    /// <returns>Non-negative wall and exclusive category durations for the test.<br/></returns>
    private static DatasetAuditTiming StopDatasetAuditTiming(DatasetAuditTimingStart start)
    {
        TimingSnapshot end = ActionTiming.Capture();
        double elapsedMs = (Stopwatch.GetTimestamp() - start.Timestamp) * 1000d / Stopwatch.Frequency;
        return new DatasetAuditTiming(
            Math.Max(0d, elapsedMs),
            Math.Max(0d, end.DatabaseMs - start.Timing.DatabaseMs),
            Math.Max(0d, end.CalculationMs - start.Timing.CalculationMs));
    }

    /// <summary>
    /// Returns the absolute cardinality difference without narrowing either unsigned count.<br/>
    /// This makes missing and unexpected records contribute equally to a displayed finding count.<br/>
    /// </summary>
    /// <param name="left">First unsigned count.<br/></param>
    /// <param name="right">Second unsigned count.<br/></param>
    /// <returns>Absolute difference between the two counts.<br/></returns>
    private static ulong CountDifference(ulong left, ulong right)
        => left >= right ? left - right : right - left;
}

/// <summary>
/// Carries one bounded real-progress observation from the native hydration audit to WebView2.<br/>
/// <paramref name="Determinate"/> is false only while an unordered physical-location snapshot is being prepared without an item-level callback.<br/>
/// </summary>
/// <param name="Phase">Current concrete audit phase.<br/></param>
/// <param name="CompletedOrders">Order headers fully hydrated and folded.<br/></param>
/// <param name="CompletedLines">Order lines fully hydrated and folded.<br/></param>
/// <param name="TotalOrders">Independent expected live order count.<br/></param>
/// <param name="TotalLines">Independent expected live order-line count.<br/></param>
/// <param name="Determinate">Whether <paramref name="PhaseCompleted"/> and <paramref name="PhaseTotal"/> define truthful percentage progress.<br/></param>
/// <param name="PhaseCompleted">Completed work items in the current phase.<br/></param>
/// <param name="PhaseTotal">Known total work items in the current phase, or zero when the backend cannot truthfully provide one.<br/></param>
/// <param name="PhaseUnit">Developer-facing unit for the current phase counters.<br/></param>
/// <param name="Detail">Measured phase detail shown beside the progress indicator.<br/></param>
/// <param name="ElapsedMs">Elapsed whole-audit wall time at publication.<br/></param>
internal readonly record struct DatasetAuditProgress(
    string Phase,
    ulong CompletedOrders,
    ulong CompletedLines,
    ulong TotalOrders,
    ulong TotalLines,
    bool Determinate,
    ulong PhaseCompleted,
    ulong PhaseTotal,
    string PhaseUnit,
    string Detail,
    double ElapsedMs);

/// <summary>
/// Summarizes one normalized store's unordered hydration pass without retaining its materialized objects.<br/>
/// </summary>
/// <param name="Count">Objects hydrated from the physical snapshot.<br/></param>
/// <param name="ExpectedCount">Independent indexed aggregate count captured before scanning.<br/></param>
/// <param name="Total">Folded monetary total for parity checks.<br/></param>
/// <param name="InvalidRecords">Objects that violated the sample application's required scalar invariants.<br/></param>
/// <param name="IdentityXor">Order-independent XOR fingerprint of the authoritative record identities.<br/></param>
/// <param name="Units">Folded line quantity, or zero for a store without a quantity measure.<br/></param>
internal readonly record struct DatasetStoreAudit(
    ulong Count,
    ulong ExpectedCount,
    decimal Total,
    ulong InvalidRecords,
    ulong IdentityXor,
    long Units);

/// <summary>
/// Summarizes one normalized child-to-master relationship checked through an indexed group traversal and direct master existence probes.<br/>
/// The dependent-record sum independently proves index coverage while distinct-key counts describe the actual number of master probes performed.<br/>
/// </summary>
/// <param name="DependentRecords">Child records represented by all grouped index keys.<br/></param>
/// <param name="DistinctKeys">Distinct relationship keys returned by the complete scalar index.<br/></param>
/// <param name="ExistingKeys">Distinct keys whose master identity exists.<br/></param>
/// <param name="MissingKeys">Distinct malformed, cross-store, or absent master identities.<br/></param>
/// <param name="OrphanRecords">Child records owned by missing keys.<br/></param>
internal readonly record struct DatasetReferenceAudit(
    ulong DependentRecords,
    ulong DistinctKeys,
    ulong ExistingKeys,
    ulong MissingKeys,
    ulong OrphanRecords);

/// <summary>
/// Summarizes exact parent-owned packed order-line membership after registered-link hydration.<br/>
/// Every counted linked line was retrieved directly by identity; duplicate detection plus live-count parity proves one-to-one coverage.<br/>
/// </summary>
/// <param name="Orders">Order manifests visited through the unordered snapshot.<br/></param>
/// <param name="LinkedLines">Existing child objects returned by all successfully hydrated manifests.<br/></param>
/// <param name="UniqueLineIDs">Distinct linked child identities after sorting.<br/></param>
/// <param name="DuplicateLineIDs">Repeated child identity occurrences beyond their first membership.<br/></param>
/// <param name="BrokenOrders">Order manifests whose packed payload or child retrieval failed integrity validation.<br/></param>
/// <param name="TemporaryBytes">Exact temporary identity-buffer capacity used for duplicate detection.<br/></param>
internal readonly record struct DatasetPackedMembershipAudit(
    ulong Orders,
    ulong LinkedLines,
    ulong UniqueLineIDs,
    ulong DuplicateLineIDs,
    ulong BrokenOrders,
    long TemporaryBytes);

/// <summary>
/// Retains the monotonic timestamp and exclusive request counters captured before one displayed audit test.<br/>
/// This internal start state is never serialized to WebView2.<br/>
/// </summary>
/// <param name="Timestamp">Monotonic <see cref="Stopwatch"/> timestamp.<br/></param>
/// <param name="Timing">Exclusive request timing counters at the same logical boundary.<br/></param>
internal readonly record struct DatasetAuditTimingStart(long Timestamp, TimingSnapshot Timing);

/// <summary>
/// Stores reconciled native timing for one independently displayed audit test.<br/>
/// Elapsed time is wall time; Abraxas and calculation values are exclusive subsets, leaving only small boundary overhead.<br/>
/// </summary>
/// <param name="ElapsedMs">Whole test wall time.<br/></param>
/// <param name="DatabaseMs">Exclusive time inside public Abraxas boundaries.<br/></param>
/// <param name="CalculationMs">Exclusive Meridian validation, calculation, shaping, and progress time.<br/></param>
internal readonly record struct DatasetAuditTiming(double ElapsedMs, double DatabaseMs, double CalculationMs);

/// <summary>
/// Describes one independently timed hydration or relationship-integrity test.<br/>
/// </summary>
/// <param name="Name">Short invariant label shown in the Dataset UI.<br/></param>
/// <param name="Passed">Whether the invariant held for the captured snapshots.<br/></param>
/// <param name="Route">Public Abraxas or Meridian calculation route exercised by the test.<br/></param>
/// <param name="WorkItems">Actual logical work-item count used for optional throughput reporting.<br/></param>
/// <param name="WorkUnit">Human-readable unit associated with <paramref name="WorkItems"/>.<br/></param>
/// <param name="RateRelevant">Whether a work-items-per-second rate is meaningful for this test shape.<br/></param>
/// <param name="Findings">Count of failed identities, records, memberships, or comparisons.<br/></param>
/// <param name="ElapsedMs">Whole test wall time.<br/></param>
/// <param name="DatabaseMs">Exclusive time at public Abraxas boundaries.<br/></param>
/// <param name="CalculationMs">Exclusive Meridian validation, calculation, shaping, and progress time.<br/></param>
/// <param name="Detail">Measured values supporting the result.<br/></param>
internal readonly record struct DatasetAuditCheck(
    string Name,
    bool Passed,
    string Route,
    ulong WorkItems,
    string WorkUnit,
    bool RateRelevant,
    ulong Findings,
    double ElapsedMs,
    double DatabaseMs,
    double CalculationMs,
    string Detail);

/// <summary>
/// Reports the complete order-independent hydration audit returned to the WebView2 client.<br/>
/// </summary>
/// <param name="Passed">Whether every declared count, field, and normalization check passed.<br/></param>
/// <param name="Route">Explicit Abraxas reader route and link policy used by the audit.<br/></param>
/// <param name="RecordsScanned">Total objects actually hydrated across both normalized stores.<br/></param>
/// <param name="HydratedObjects">All object materializations, including the registered packed-relationship pass.<br/></param>
/// <param name="Orders">Order-header scan measurements.<br/></param>
/// <param name="Lines">Order-line scan measurements.<br/></param>
/// <param name="CustomerReferences">Indexed child-held customer relationship measurements.<br/></param>
/// <param name="ProductReferences">Indexed child-held product relationship measurements.<br/></param>
/// <param name="PackedMembership">Parent-owned packed order-line relationship measurements.<br/></param>
/// <param name="TotalDifference">Absolute difference between header totals and normalized line totals.<br/></param>
/// <param name="ElapsedMs">Whole native audit duration, excluding WebView response serialization and rendering.<br/></param>
/// <param name="MeasuredTestMs">Sum of independently measured test wall times.<br/></param>
/// <param name="UnattributedMs">Whole-audit wall time not assigned to a displayed test, retained for reconciliation.<br/></param>
/// <param name="Checks">Individual timed tests and measured evidence.<br/></param>
internal readonly record struct DatasetAuditResult(
    bool Passed,
    string Route,
    ulong RecordsScanned,
    ulong HydratedObjects,
    DatasetStoreAudit Orders,
    DatasetStoreAudit Lines,
    DatasetReferenceAudit CustomerReferences,
    DatasetReferenceAudit ProductReferences,
    DatasetPackedMembershipAudit PackedMembership,
    decimal TotalDifference,
    double ElapsedMs,
    double MeasuredTestMs,
    double UnattributedMs,
    DatasetAuditCheck[] Checks);
