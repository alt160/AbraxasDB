namespace MeridianTrading;

/// <summary>Coalesces queued UI reads without cancelling mutations or sharing database readers across threads.<br/>
/// Owned exclusively by the window's UI thread. At most one ticket per allowlisted read command is retained.<br/>
/// Active synchronous storage calls are not interrupted; the host rejects their obsolete responses after completion.<br/></summary>
internal sealed class RequestFreshness
{
    private readonly Dictionary<string, long> latest = new(StringComparer.Ordinal);
    private long view;
    private long sequence;
    internal long Skipped { get; private set; }
    internal readonly record struct Ticket(string Command, long View, long Sequence);

    /// <summary>Invalidates all earlier-view reads when navigation advances; late packets cannot move the view backwards.<br/></summary>
    internal void AdvanceView(long next)
    {
        if (next <= view) return;
        view = next;
        latest.Clear();
    }

    /// <summary>Registers only known read operations; writes and control messages deliberately receive no cancellation ticket.<br/>
    /// An old-view request gets an immediately stale ticket without replacing a current command's ticket.<br/></summary>
    internal Ticket? Observe(string command, long requestView)
    {
        if (command is not ("overview" or "dataset" or "readiness" or "page" or "edit-detail" or "customer-orders" or "group-orders" or "detail" or "analyze" or "search-schema" or "describe-search" or "search-records" or "lookup-record")) return null;
        var ticket = new Ticket(command, requestView, ++sequence);
        if (requestView == view) latest[command] = ticket.Sequence;
        return ticket;
    }

    /// <summary>Checks whether an operation should enter storage; absence of a ticket always preserves mutation execution.<br/></summary>
    internal bool IsCurrent(Ticket? ticket) => ticket is null || (ticket.Value.View == view && latest.TryGetValue(ticket.Value.Command, out long current) && current == ticket.Value.Sequence);

    /// <summary>Counts only reads actually skipped before dispatch, not active requests whose responses were discarded.<br/></summary>
    internal void RecordSkipped() => Skipped++;
}
