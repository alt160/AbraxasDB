using AbraxasDB;

namespace AbraxasShowcase;

/// <summary>Exact compiled setup used by every scenario in the object-shapes and identity group.<br/></summary>
public static class ObjectShapesIdentityGroupSetup
{
// <group-environment>
    /// <summary>Creates the typed stores shared by the object-graph and external-identity scenarios.<br/></summary>
    /// <param name="directory">Owned directory that will contain the group's RecordBase files.<br/></param>
    public static void CreateEnvironment(string directory)
    {
        using RecordBase recordBase = RecordBase.CreateOrOpen(directory, "showcase");
        _ = recordBase.CreateOrOpenStore<ShowcaseCustomerProfile>(
            "customer-profiles",
            "Nested customer profile examples");
        _ = recordBase.CreateOrOpenStore<ShowcaseIdentityFreeNote>(
            "identity-free-notes",
            "Identity-free domain object examples");
    }
// </group-environment>

// <group-generation>
    /// <summary>Generates deterministic nested profiles and identity-free notes for browsing and later scenarios.<br/></summary>
    /// <param name="directory">Owned directory containing the prepared group RecordBase.<br/></param>
    /// <param name="profileCount">Number of nested customer profiles to generate.<br/></param>
    /// <param name="notesPerProfile">Number of identity-free notes generated for every profile.<br/></param>
    /// <param name="seed">Random seed controlling repeatable profile and note values.<br/></param>
    /// <param name="progress">Optional progress callback receiving completed work, total work, and phase.<br/></param>
    /// <param name="cancellationToken">Cancellation observed between generated records.<br/></param>
    public static void GenerateDeterministicData(
        string directory,
        int profileCount,
        int notesPerProfile,
        int seed,
        Action<int, int, string>? progress,
        CancellationToken cancellationToken)
    {
        using RecordBase recordBase = RecordBase.CreateOrOpen(directory, "showcase");
        RecordStore<ShowcaseCustomerProfile> profiles = recordBase.OpenStore<ShowcaseCustomerProfile>("customer-profiles");
        RecordStore<ShowcaseIdentityFreeNote> notes = recordBase.OpenStore<ShowcaseIdentityFreeNote>("identity-free-notes");
        int total = checked(profileCount + profileCount * notesPerProfile);
        int completed = 0;
        var random = new Random(seed);
        string[] cities = ["Phoenix", "Tempe", "Mesa", "Scottsdale", "Flagstaff"];

        for (int index = 0; index < profileCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string city = cities[random.Next(cities.Length)];
            profiles.Put(new ShowcaseCustomerProfile
            {
                AccountCode = $"PROFILE-{index + 1:000000}",
                Contact = new ShowcaseContact
                {
                    DisplayName = $"Profile Customer {index + 1:000000}",
                    Email = $"profile-{index + 1:000000}@example.test",
                    PhoneNumbers = [$"+1 602 555 {1000 + index % 9000:0000}"]
                },
                Locations = [new ShowcaseAddress { Kind = "Primary", City = city, Region = "AZ", PostalCode = $"{85000 + index % 500:00000}" }],
                Preferences = new ShowcasePreferences
                {
                    ReceivesUpdates = index % 2 == 0,
                    Locale = "en-US",
                    Interests = ["object graphs", index % 2 == 0 ? "performance" : "identity"],
                    FavoriteId = new Guid(index + 1, 0, 0, new byte[8]),
                    RenewalDate = new DateOnly(2026, 1, 1).AddDays(index),
                    ContactTime = new TimeOnly(9 + index % 8, 15),
                    LastContact = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero).AddHours(index),
                    Portal = new Uri($"https://example.test/profiles/{index + 1}"),
                    FormatVersion = new Version(2, index % 4),
                    Limits = new Dictionary<string, int> { ["daily"] = index + 2, ["weekly"] = index + 10 },
                    Labels = new HashSet<string> { "generated", index % 2 == 0 ? "priority" : "standard" },
                }
            });
            progress?.Invoke(++completed, total, "Generating nested profiles");

            for (int noteIndex = 0; noteIndex < notesPerProfile; noteIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                notes.Put(new ShowcaseIdentityFreeNote
                {
                    Subject = $"Profile {index + 1:000000} note {noteIndex + 1:00}",
                    Body = "Generated identity-free note for the object-shapes scenario group.",
                    CreatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(index * Math.Max(1, notesPerProfile) + noteIndex)
                });
                progress?.Invoke(++completed, total, "Generating identity-free notes");
            }
        }
    }
// </group-generation>
}
