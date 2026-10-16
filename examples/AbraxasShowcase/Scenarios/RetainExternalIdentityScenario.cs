using AbraxasDB;
using System.Diagnostics;

namespace AbraxasShowcase;

/// <summary>Identity scenario proving persistence identity can remain external to an ordinary domain type.<br/></summary>
internal sealed class RetainExternalIdentityScenario : IShowcaseScenario
{
    public string Id => "retain-external-identity";
    public string Category => "Object shapes and identity";
    public string Title => "Work with external RecordID identity";
    public string Description => "Store an object with no ID member, keep the returned RecordID separately, and use it to get the object again.";
    public string Cost => "Immediate";
    public bool Mutates => true;
    public ScenarioDocumentation Documentation { get; } = new(
        "Used to putting an ID property on every persisted class? Abraxas does not require it. Put returns a RecordID and Get accepts that ID later. This store has no ObjectIDSetter, so the .NET object stays unchanged and application code keeps the ID beside it.",
        ["external identity", "identity-free POCO", "RecordID", "domain independence"],
        ["RecordStore<T>.Put", "RecordStore<T>.Get", "RecordID"],
        ["identity", "id", "key", "poco", "domain model", "retrieve"],
        ["Put and get an ordinary C# object"],
        120);
    public string SourceCode => ExampleSource.Read("ObjectIdentity/RetainExternalIdentity.cs.txt");
    public string CompleteSource => ExampleSource.CompleteScenario(Id);

    /// <summary>
    /// Runs the showcase orchestration around the focused Abraxas example and produces inspection evidence.<br/>
    /// </summary>
    /// <param name="workspace">Active shared RecordBase laboratory selected by the developer.<br/></param>
    /// <param name="cancellationToken">Cancellation observed before the mutation begins.<br/></param>
    /// <returns>External identity and hydrated-domain evidence retained for live inspection.<br/></returns>
    public ScenarioResult Run(ShowcaseWorkspace workspace, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        (ulong recordID, ShowcaseIdentityFreeNote note, ShowcaseIdentityFreeNote hydrated) =
            PersistIdentityFreeNote(workspace.Notes);
        bool declaresIdentity = typeof(ShowcaseIdentityFreeNote).GetProperties()
            .Any(property => property.Name.Contains("ID", StringComparison.OrdinalIgnoreCase));
        timer.Stop();

        bool passed = recordID != 0
            && !declaresIdentity
            && hydrated.Subject == note.Subject
            && hydrated.Body == note.Body
            && hydrated.CreatedUtc == note.CreatedUtc;
        return new ScenarioResult(
            passed,
            passed ? "The external RecordID retrieved an unchanged identity-free domain object." : "The external identity contract was not preserved.",
            [
                new ScenarioFact("Domain identity property", declaresIdentity ? "Present" : "None")
            ],
            hydrated,
            typeof(ShowcaseIdentityFreeNote),
            timer.Elapsed.TotalMilliseconds,
            recordID,
            "Retrieved identity-free object");
    }

// <showcase-code>
    /// <summary>
    /// Persists a domain object that declares no identity member and retrieves it with the separately retained RecordID.<br/>
    /// The domain object remains free of Abraxas-specific identity members while the application retains its durable identity explicitly.<br/>
    /// </summary>
    /// <param name="notes">Prepared group-owned typed note store.<br/></param>
    /// <returns>The external RecordID, submitted note, and hydrated identity-free note.<br/></returns>
    public static (
        ulong RecordID,
        ShowcaseIdentityFreeNote Submitted,
        ShowcaseIdentityFreeNote Hydrated) PersistIdentityFreeNote(
            RecordStore<ShowcaseIdentityFreeNote> notes)
    {
        var note = new ShowcaseIdentityFreeNote
        {
            Subject = "External identity",
            Body = "The Abraxas RecordID remains outside this domain object.",
            CreatedUtc = DateTime.UtcNow
        };

        ulong recordID = notes.Put(note);
        ShowcaseIdentityFreeNote hydrated = notes.Get(recordID);
        return (recordID, note, hydrated);
    }
// </showcase-code>
}
