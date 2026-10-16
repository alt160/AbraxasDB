using AbraxasDB;
using System.Diagnostics;

namespace AbraxasShowcase;

/// <summary>Object-shape scenario proving nested objects and bounded collections round-trip as an ordinary C# graph.<br/></summary>
internal sealed class PersistNestedObjectGraphScenario : IShowcaseScenario
{
    public string Id => "persist-object-graph";
    public string Category => "Object shapes and identity";
    public string Title => "Persist a nested object graph";
    public string Description => "Store and retrieve a customer profile that contains nested objects, arrays, and ordinary values.";
    public string Cost => "Immediate on every laboratory size";
    public bool Mutates => true;
    public ScenarioDocumentation Documentation { get; } = new(
        "Have a customer object with contact, addresses, and preferences inside it? Store that ordinary .NET object directly; there is no separate document-conversion step in this example. Get returns the profile so you can inspect each nested value.",
        ["nested objects", "bounded collections", "strong typing", "graph hydration"],
        ["RecordBase.CreateOrOpenStore<T>", "RecordStore<T>.Put", "RecordStore<T>.Get"],
        ["object graph", "nested", "array", "collection", "hydrate", "profile"],
        ["Create and reuse a typed RecordStore"],
        110);
    public string SourceCode => ExampleSource.Read("ObjectIdentity/PersistObjectGraph.cs.txt");
    public string CompleteSource => ExampleSource.CompleteScenario(Id);

    /// <summary>
    /// Runs the showcase orchestration around the focused Abraxas example and produces inspection evidence.<br/>
    /// </summary>
    /// <param name="workspace">Active shared RecordBase laboratory selected by the developer.<br/></param>
    /// <param name="cancellationToken">Cancellation observed before the mutation begins.<br/></param>
    /// <returns>The hydrated profile graph retained for recursive live inspection.<br/></returns>
    public ScenarioResult Run(ShowcaseWorkspace workspace, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        (ulong recordID, ShowcaseCustomerProfile profile, ShowcaseCustomerProfile hydrated) =
            PersistCustomerProfile(workspace.Profiles);
        timer.Stop();

        bool passed = hydrated.AccountCode == profile.AccountCode
            && hydrated.Contact.DisplayName == profile.Contact.DisplayName
            && hydrated.Contact.PhoneNumbers.SequenceEqual(profile.Contact.PhoneNumbers)
            && hydrated.Locations.Select(location => location.PostalCode).SequenceEqual(profile.Locations.Select(location => location.PostalCode))
            && hydrated.Preferences.ReceivesUpdates
            && hydrated.Preferences.Interests.SequenceEqual(profile.Preferences.Interests);
        return new ScenarioResult(
            passed,
            passed ? "The complete strongly typed object graph round-tripped exactly." : "A nested object-graph branch differed after hydration.",
            [
                new ScenarioFact("Nested branches", "Contact · Locations · Preferences")
            ],
            hydrated,
            typeof(ShowcaseCustomerProfile),
            timer.Elapsed.TotalMilliseconds,
            recordID,
            "Retrieved profile");
    }

// <showcase-code>
    /// <summary>
    /// Persists one strongly typed profile graph and retrieves the complete graph by its external RecordID.<br/>
    /// The application-facing operation uses ordinary C# objects directly without an intermediate document representation.<br/>
    /// </summary>
    /// <param name="profiles">Prepared group-owned typed profile store.<br/></param>
    /// <returns>The external RecordID, submitted profile, and completely hydrated profile graph.<br/></returns>
    public static (
        ulong RecordID,
        ShowcaseCustomerProfile Submitted,
        ShowcaseCustomerProfile Hydrated) PersistCustomerProfile(
            RecordStore<ShowcaseCustomerProfile> profiles)
    {
        var profile = new ShowcaseCustomerProfile
        {
            AccountCode = "GRAPH-" + DateTime.UtcNow.Ticks,
            Contact = new ShowcaseContact
            {
                DisplayName = "Ada Northwind",
                Email = "ada@example.test",
                PhoneNumbers = ["+1 602 555 0101", "+1 602 555 0102"]
            },
            Locations =
            [
                new ShowcaseAddress { Kind = "Billing", City = "Phoenix", Region = "AZ", PostalCode = "85001" },
                new ShowcaseAddress { Kind = "Shipping", City = "Tempe", Region = "AZ", PostalCode = "85281" }
            ],
            Preferences = new ShowcasePreferences
            {
                ReceivesUpdates = true,
                Locale = "en-US",
                Interests = ["performance", "object graphs", "indexes"]
            }
        };

        ulong recordID = profiles.Put(profile);
        ShowcaseCustomerProfile hydrated = profiles.Get(recordID);
        return (recordID, profile, hydrated);
    }
// </showcase-code>
}
