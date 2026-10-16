using System.Diagnostics;

namespace MeridianTrading;

/// <summary>Exclusive wall-time accounting at Meridian's public Abraxas boundaries, not an engine CPU profiler.<br/>
/// Dispatch is synchronous on one worker; thread-local scopes avoid per-row delegates and never flow into other requests.<br/></summary>
internal sealed class ActionTiming : IDisposable
{
    [ThreadStatic] private static ActionTiming? current;
    private readonly ActionTiming? previous;
    private readonly Action<string>? progress;
    private long stamp = Stopwatch.GetTimestamp(), databaseTicks, calculationTicks;
    private bool database;
    private string phase = "";
    internal double DatabaseMs => databaseTicks * 1000d / Stopwatch.Frequency;
    internal double CalculationMs => calculationTicks * 1000d / Stopwatch.Frequency;

    /// <summary>Starts an application-work scope on the dispatch worker and optionally publishes named phases.<br/></summary>
    internal ActionTiming(Action<string>? progress = null) { previous = current; current = this; this.progress = progress; }

    /// <summary>Marks a public database call or reader traversal; nested calculation scopes are subtracted exclusively.<br/></summary>
    internal static Scope Db() => new(current, true);
    /// <summary>Marks application validation, folding or response shaping outside engine work.<br/></summary>
    internal static Scope Calculate() => new(current, false);
    /// <summary>Publishes a changed real operation label without inventing a percentage or a completion count.<br/></summary>
    internal static void Phase(string text)
    {
        if (current is not { } value || value.phase == text) return;
        value.phase = text; value.progress?.Invoke(text);
    }

    /// <summary>
    /// Captures the exclusive Abraxas-boundary and application/calculation wall time accumulated by the current request.<br/>
    /// The active interval is included without changing its category, so callers can subtract two snapshots to attribute one logical test without adding scope transitions to its hot path.<br/>
    /// A caller outside a measured request receives a zero snapshot, which keeps command-line verification behavior deterministic.<br/>
    /// </summary>
    /// <returns>Current exclusive timing totals for the dispatch worker.<br/></returns>
    internal static TimingSnapshot Capture()
    {
        ActionTiming? owner = current;
        if (owner is null) return default;

        long now = Stopwatch.GetTimestamp();
        long databaseTicks = owner.databaseTicks;
        long calculationTicks = owner.calculationTicks;
        if (owner.database) databaseTicks += now - owner.stamp;
        else calculationTicks += now - owner.stamp;
        return new TimingSnapshot(
            databaseTicks * 1000d / Stopwatch.Frequency,
            calculationTicks * 1000d / Stopwatch.Frequency);
    }
    /// <summary>Closes the current interval and switches categories without counting nested intervals twice.<br/></summary>
    private void Switch(bool next)
    {
        long now = Stopwatch.GetTimestamp();
        if (database) databaseTicks += now - stamp; else calculationTicks += now - stamp;
        stamp = now; database = next;
    }
    /// <summary>Completes dispatch accounting and restores the previous worker-local context even on failure.<br/></summary>
    public void Dispose() { Switch(false); current = previous; }

    /// <summary>Allocation-free category boundary; a missing request context is a no-op for ordinary CLI callers.<br/></summary>
    internal readonly struct Scope : IDisposable
    {
        private readonly ActionTiming? owner;
        private readonly bool prior;
        /// <summary>Saves and replaces the active category for one lexical scope.<br/></summary>
        internal Scope(ActionTiming? owner, bool database) { this.owner = owner; prior = owner?.database ?? false; owner?.Switch(database); }
        /// <summary>Restores the parent category after successful or failed work.<br/></summary>
        public void Dispose() => owner?.Switch(prior);
    }
}

/// <summary>
/// Stores one non-mutating observation of the request's exclusive timing counters.<br/>
/// Subtracting snapshots attributes a bounded workflow while preserving the action footer's existing Abraxas-versus-application accounting contract.<br/>
/// </summary>
/// <param name="DatabaseMs">Wall time accumulated inside public Abraxas calls and reader traversal scopes.<br/></param>
/// <param name="CalculationMs">Wall time accumulated in Meridian calculation, validation, shaping, and progress publication.<br/></param>
internal readonly record struct TimingSnapshot(double DatabaseMs, double CalculationMs);
