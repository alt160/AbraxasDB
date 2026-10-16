using System.Diagnostics;

namespace AbraxasShowcase;

/// <summary>Runnable proof that varied ordinary .NET member shapes need no annotations or custom codecs.<br/></summary>
internal static class OrdinaryShapeScenarios
{
    internal static IReadOnlyList<IShowcaseScenario> All { get; } =
    [
        New("ordinary-temporal-values", "Round-trip temporal and identity values", "Save and read Guid, DateOnly, TimeOnly, and DateTimeOffset members without special mapping.", 130, "ObjectIdentity/TemporalValues.cs.txt", TemporalValues),
        New("ordinary-framework-values", "Round-trip Uri and Version", "Save and read Uri and Version members without a custom serializer hook.", 140, "ObjectIdentity/FrameworkValues.cs.txt", FrameworkValues),
        New("ordinary-dictionary", "Round-trip a dictionary", "Save a dictionary inside an ordinary .NET object and read its keys and values back.", 150, "ObjectIdentity/DictionaryValues.cs.txt", DictionaryValues),
        New("ordinary-hashset", "Round-trip a HashSet", "Save a set and check membership after reading it back; a set does not promise an order.", 160, "ObjectIdentity/HashSetValues.cs.txt", HashSetValues)
    ];

    /// <summary>Registers one searchable, paste-ready scenario over the group's existing profile store.<br/></summary>
    /// <param name="id">Stable scenario identity.<br/></param>
    /// <param name="title">Developer-facing title.<br/></param>
    /// <param name="description">What the real round-trip will demonstrate.<br/></param>
    /// <param name="order">Learning-path order inside the early object group.<br/></param>
    /// <param name="source">Embedded source fragment copied into the complete example.<br/></param>
    /// <param name="action">Native operation over the already-open group store.<br/></param>
    /// <returns>A complete runnable scenario.<br/></returns>
    private static IShowcaseScenario New(string id, string title, string description, int order, string source,
        Func<ShowcaseWorkspace, CancellationToken, ScenarioResult> action) =>
        new CatalogActionScenario(id, "Object shapes and identity", title, description, "One profile read", false,
            new ScenarioDocumentation(
                $"If you already have a .NET class, you should not need to decorate it just to try persistence. The group saved an ordinary ShowcaseCustomerProfile; RecordStore<T>.Get reads it back without persistence attributes, conversion code, or registered hooks. {description}",
                ["ordinary .NET shapes", "no annotations", "no custom serializer"],
                ["RecordStore<T>.Get", "Inheto"],
                [title, description, "POCO", "without ceremony"],
                ["Persist a nested object graph"], order), source, action);

    /// <summary>Reads one actual generated profile whose preferences contain the no-ceremony sample values.<br/></summary>
    /// <param name="workspace">Active object-shapes group workspace.<br/></param>
    /// <param name="token">Cancellation checked before the bounded read.<br/></param>
    /// <returns>The RecordID and hydrated preferences retained by the native host.<br/></returns>
    private static (ulong ID, ShowcasePreferences Preferences) ReadPreferences(ShowcaseWorkspace workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var reader = workspace.Profiles.Readers.Open();
        if (!reader.Read()) throw new InvalidOperationException("The prepared group has no profiles.");
        return (reader.ID, workspace.Profiles.Get(reader.ID)!.Preferences);
    }

    /// <summary>Proves four familiar value types are present after an ordinary typed read.<br/></summary>
    private static ScenarioResult TemporalValues(ShowcaseWorkspace workspace, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        var (id, values) = ReadPreferences(workspace, token);
        bool passed = values.FavoriteId != Guid.Empty && values.RenewalDate.Year == 2026
            && values.ContactTime.Hour is >= 9 and <= 16 && values.LastContact.Year == 2026;
        watch.Stop();
        return new ScenarioResult(passed, "Guid and temporal values survived the profile round-trip without hooks.", [],
            [new("Guid", id, values.FavoriteId, typeof(Guid)), new("DateOnly", id, values.RenewalDate, typeof(DateOnly)),
             new("TimeOnly", id, values.ContactTime, typeof(TimeOnly)), new("DateTimeOffset", id, values.LastContact, typeof(DateTimeOffset))],
            watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Proves the promoted Uri and Version members hydrate with their .NET semantics intact.<br/></summary>
    private static ScenarioResult FrameworkValues(ShowcaseWorkspace workspace, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        var (id, values) = ReadPreferences(workspace, token);
        bool passed = values.Portal.Host == "example.test" && values.FormatVersion.Major == 2;
        watch.Stop();
        return new ScenarioResult(passed, "Uri and Version were read back as .NET values without custom converters.", [],
            [new("Uri", id, values.Portal, typeof(Uri)), new("Version", id, values.FormatVersion, typeof(Version))],
            watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Proves a string-keyed dictionary retains both generated keys and their values.<br/></summary>
    private static ScenarioResult DictionaryValues(ShowcaseWorkspace workspace, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        var (id, values) = ReadPreferences(workspace, token);
        bool passed = values.Limits.TryGetValue("daily", out int daily) && values.Limits.TryGetValue("weekly", out int weekly)
            && daily >= 2 && weekly > daily;
        watch.Stop();
        return new ScenarioResult(passed, "The dictionary retained its keys and values without a mapping contract.",
            [new("Entries", values.Limits.Count.ToString())], values.Limits, typeof(Dictionary<string, int>), watch.Elapsed.TotalMilliseconds, id);
    }

    /// <summary>Proves HashSet membership without promising iteration order as a stored contract.<br/></summary>
    private static ScenarioResult HashSetValues(ShowcaseWorkspace workspace, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        var (id, values) = ReadPreferences(workspace, token);
        bool passed = values.Labels.Count == 2 && values.Labels.Contains("generated")
            && (values.Labels.Contains("priority") || values.Labels.Contains("standard"));
        watch.Stop();
        return new ScenarioResult(passed, "HashSet membership survived the round-trip; its order is not the point.",
            [new("Members", values.Labels.Count.ToString())], values.Labels, typeof(HashSet<string>), watch.Elapsed.TotalMilliseconds, id);
    }

}
