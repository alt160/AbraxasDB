namespace AbraxasShowcase;

/// <summary>One ordinary identity-free customer persisted by the showcase foundation category.<br/></summary>
public sealed class ShowcaseCustomer
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Region { get; set; } = "";
    public string[] Tags { get; set; } = [];
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
}

/// <summary>One normalized order whose customer relationship is retained as an external Abraxas identity.<br/></summary>
public sealed class ShowcaseOrder
{
    public string Number { get; set; } = "";
    public ulong CustomerID { get; set; }
    public DateTime OrderedAt { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = "";
}
/// <summary>Persisted description of the deterministic laboratory currently used by all scenario runners.<br/></summary>
public sealed class LaboratoryManifest
{
    public string State { get; set; } = "Empty";
    public string GroupID { get; set; } = "";
    public int SetupVersion { get; set; }
    public string Size { get; set; } = "Small";
    public string Complexity { get; set; } = "Simple";
    public int Seed { get; set; }
    public int CustomerCount { get; set; }
    public int OrderCount { get; set; }
    public DateTime GeneratedUtc { get; set; }
    public double GenerationMilliseconds { get; set; }
    public string LastScenarioID { get; set; } = "";
    public string LastScenarioTitle { get; set; } = "";
}

/// <summary>Inspectable proof that a RecordBase was created, closed, and reopened from the same owned folder.<br/></summary>
public sealed class RecordBaseLifecycleProof
{
    public string Directory { get; init; } = "";
    public string Name { get; init; } = "";
    public bool Reopened { get; init; }
    public int DurableFileCount { get; init; }
    public int ActiveStoreCount { get; init; }
}

/// <summary>Inspectable proof that a typed store and its selected indexes exist in a newly created RecordBase.<br/></summary>
public sealed class IndexFoundationProof
{
    public string StoreName { get; init; } = "";
    public string RecordType { get; init; } = "";
    public string[] IndexNames { get; init; } = [];
    public bool ReusedWithoutDuplication { get; init; }
}

/// <summary>Validated request used to regenerate the shared RecordBase laboratory.<br/></summary>
internal sealed record GenerationRequest(
    string Size,
    string Complexity,
    int CustomerCount,
    int OrdersPerCustomer,
    int Seed,
    bool PersistAcrossRuns);

/// <summary>Inspectable public-metadata view of the active shared RecordBase.<br/></summary>
public sealed class RecordBaseFoundationProof
{
    public string Directory { get; init; } = "";
    public string State { get; init; } = "";
    public StoreFoundationInfo[] Stores { get; init; } = [];
}

/// <summary>One durable typed-store definition returned by the public RecordBase metadata API.<br/></summary>
public sealed class StoreFoundationInfo
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string RecordType { get; init; } = "";
    public string Lifecycle { get; init; } = "";
    public Guid InstanceID { get; init; }
}

/// <summary>Inspectable evidence that two typed facades resolve the same durable store and population.<br/></summary>
public sealed class TypedStoreReuseProof
{
    public string StoreName { get; init; } = "";
    public string RecordType { get; init; } = "";
    public Guid InstanceID { get; init; }
    public ulong CreateOrOpenCount { get; init; }
    public ulong OpenCount { get; init; }
    public string[] IndexNames { get; init; } = [];
    public bool SameDurableStore { get; init; }
}

/// <summary>Nested customer profile used to demonstrate ordinary object-graph persistence.<br/></summary>
public sealed class ShowcaseCustomerProfile
{
    public string AccountCode { get; set; } = "";
    public ShowcaseContact Contact { get; set; } = new();
    public ShowcaseAddress[] Locations { get; set; } = [];
    public ShowcasePreferences Preferences { get; set; } = new();
}

/// <summary>Nested contact value retained as part of a customer profile graph.<br/></summary>
public sealed class ShowcaseContact
{
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string[] PhoneNumbers { get; set; } = [];
}

/// <summary>Nested address value used in a bounded ordered profile collection.<br/></summary>
public sealed class ShowcaseAddress
{
    public string Kind { get; set; } = "";
    public string City { get; set; } = "";
    public string Region { get; set; } = "";
    public string PostalCode { get; set; } = "";
}

/// <summary>Nested preference value showing scalar and collection members in one graph branch.<br/></summary>
public sealed class ShowcasePreferences
{
    public bool ReceivesUpdates { get; set; }
    public string Locale { get; set; } = "";
    public string[] Interests { get; set; } = [];
    public Guid FavoriteId { get; set; }
    public DateOnly RenewalDate { get; set; }
    public TimeOnly ContactTime { get; set; }
    public DateTimeOffset LastContact { get; set; }
    public Uri Portal { get; set; } = new("https://example.test/");
    public Version FormatVersion { get; set; } = new(1, 0);
    public Dictionary<string, int> Limits { get; set; } = [];
    public HashSet<string> Labels { get; set; } = [];
}

/// <summary>Ordinary domain type deliberately containing no Abraxas persistence identity member.<br/></summary>
public sealed class ShowcaseIdentityFreeNote
{
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}
/// <summary>Inspectable evidence separating an external Abraxas RecordID from its identity-free domain object.<br/></summary>
public sealed class ExternalIdentityProof
{
    public ulong RecordID { get; init; }
    public bool DomainTypeDeclaresIdentity { get; init; }
    public ShowcaseIdentityFreeNote HydratedObject { get; init; } = new();
}

/// <summary>Inspectable bounded browser snapshot for the currently active scenario-group environment.<br/></summary>
public sealed class GroupDataBrowser
{
    public string Group { get; init; } = "";
    public string RecordBaseState { get; init; } = "";
    public GroupStoreData[] Stores { get; init; } = [];
}

/// <summary>One typed store and a bounded set of real hydrated records retained for live exploration.<br/></summary>
public sealed class GroupStoreData
{
    public string Name { get; init; } = "";
    public string RecordType { get; init; } = "";
    public ulong TotalRecords { get; init; }
    public object[] SampleRecords { get; init; } = [];
}

/// <summary>One selectable first-level member exposed by a scenario-group store.<br/></summary>
public sealed record GroupStoreProperty(string Path, string Type, bool Scalar);

/// <summary>Read-only metadata used to configure the connected group data browser without hydrating records.<br/></summary>
public sealed record GroupStoreDescriptor(string Name, string RecordType, ulong TotalRecords, GroupStoreProperty[] Properties);

/// <summary>One display-safe runtime value read directly from an Abraxas dynamic value reader.<br/></summary>
public sealed record GroupBrowserValue(string Path, string RuntimeType, string Text);

/// <summary>One identity and its selected values without a hydrated domain object.<br/></summary>
public sealed record GroupBrowserRow(string RecordID, GroupBrowserValue[] Values);

/// <summary>A single explicitly paged store view produced without hydrating its records.<br/></summary>
public sealed record GroupBrowserPage(
    string GroupID,
    string StoreName,
    string RecordType,
    ulong TotalRecords,
    int PageIndex,
    int PageSize,
    bool HasPrevious,
    bool HasNext,
    string[] PropertyPaths,
    GroupBrowserRow[] Rows);

/// <summary>Persisted teaching model with members governed by path hooks, type hooks, exclusions, and activation.<br/></summary>
public sealed class ShowcaseHookDocument
{
    public string Title { get; set; } = "";
    public string PrivateMemo { get; set; } = "";
    public ShowcaseHookToken PathToken { get; set; } = new();
    public ShowcaseHookToken[] TypeTokens { get; set; } = [];
    public ShowcaseHookAddress Address { get; set; } = null!;
    public ShowcaseHookTransient? Transient { get; set; }
    public ShowcaseContextualPayload ContextPayload { get; set; } = new();
}

/// <summary>Small semantic value converted to bytes by the selected Inheto hook.<br/></summary>
public sealed class ShowcaseHookToken
{
    public int Code { get; set; }
}

/// <summary>Nested destination demonstrating a custom deserialization activator.<br/></summary>
public sealed class ShowcaseHookAddress
{
    public string City { get; set; } = "";
    public string CreatedBy { get; set; } = "Default constructor";
}

/// <summary>Whole member type intentionally excluded from the hook-group payload.<br/></summary>
public sealed class ShowcaseHookTransient
{
    public string RuntimeNote { get; set; } = "";
}

/// <summary>Customer whose Orders collection is derived from normalized child records during materialization.<br/></summary>
public sealed class ShowcaseGraphCustomer
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public List<ShowcaseGraphOrder>? Orders { get; set; }
}

/// <summary>Order whose Lines property stores only ordered child identities while line payloads live separately.<br/></summary>
public sealed class ShowcaseGraphOrder
{
    public string Number { get; set; } = "";
    public ulong CustomerID { get; set; }
    public List<ShowcaseGraphLine>? Lines { get; set; }
}

/// <summary>Independent line record whose optional ID member receives its generated RecordID.<br/></summary>
public sealed class ShowcaseGraphLine
{
    public ulong ID { get; set; }
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
}

/// <summary>Application-owned value whose Regex is stored as pattern and options, not runtime object internals.<br/></summary>
public sealed class ShowcaseContextualPayload
{
    public string Text { get; set; } = "";
    public System.Text.RegularExpressions.Regex Pattern { get; set; } = new(".*");
}

/// <summary>Demonstrates an ambient per-async-flow preference without making reads depend on that preference.<br/></summary>
public static class ShowcaseCodecContext
{
    public static System.Threading.AsyncLocal<bool> UseUtf16 { get; } = new();
}

/// <summary>Versioned byte format selected at write time and decoded from its marker at read time.<br/></summary>
public static class ShowcaseContextualCodec
{
    /// <summary>Encodes semantic text and Regex configuration in the currently selected byte format.<br/>The ambient setting influences this write only; the persisted marker makes later reads independent of it.<br/></summary>
    /// <param name="value">The semantic value to persist.<br/></param>
    /// <returns>A marker followed by UTF-8 or UTF-16 JSON containing only the stable application data.<br/></returns>
    public static byte[] Encode(ShowcaseContextualPayload value)
    {
        ArgumentNullException.ThrowIfNull(value);
        bool utf16 = ShowcaseCodecContext.UseUtf16.Value;
        string json = System.Text.Json.JsonSerializer.Serialize(new { value.Text, Pattern = value.Pattern.ToString(), Options = (int)value.Pattern.Options });
        byte[] content = utf16 ? System.Text.Encoding.Unicode.GetBytes(json) : System.Text.Encoding.UTF8.GetBytes(json);
        byte[] stored = new byte[content.Length + 1];
        stored[0] = utf16 ? (byte)2 : (byte)1;
        content.CopyTo(stored, 1);
        return stored;
    }

    /// <summary>Recreates the semantic value using the persisted format marker, regardless of current ambient state.<br/>Unknown markers fail explicitly instead of silently misreading older data.<br/></summary>
    /// <param name="stored">Marker and encoded payload returned by the deserialization hook.<br/></param>
    /// <returns>The decoded application value.<br/></returns>
    public static ShowcaseContextualPayload Decode(ReadOnlySpan<byte> stored)
    {
        if (stored.Length == 0) throw new InvalidDataException("The contextual payload has no format marker.");
        string json = stored[0] switch
        {
            1 => System.Text.Encoding.UTF8.GetString(stored[1..]),
            2 => System.Text.Encoding.Unicode.GetString(stored[1..]),
            _ => throw new InvalidDataException($"Unknown contextual payload format {stored[0]}.")
        };
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        return new ShowcaseContextualPayload
        {
            Text = root.GetProperty("Text").GetString() ?? "",
            Pattern = new System.Text.RegularExpressions.Regex(root.GetProperty("Pattern").GetString() ?? "",
                (System.Text.RegularExpressions.RegexOptions)root.GetProperty("Options").GetInt32())
        };
    }
}

/// <summary>One persisted graph used to teach collection, member, dictionary-key, and multidimensional PropPath navigation.<br/></summary>
public sealed class ShowcasePathGraph
{
    public ShowcaseAddressBook Addresses { get; set; } = new();
    public ShowcaseCoordinate[,] Coordinates { get; set; } = new ShowcaseCoordinate[0, 0];

    /// <summary>Creates a deterministic graph in which Home and collection element zero reference one address.<br/>The third address carries a literal dictionary key ending in an exclamation mark, while Coordinates has a populated row-one-column-two value.<br/></summary>
    /// <returns>A complete graph ready to persist as one record.<br/></returns>
    public static ShowcasePathGraph CreateExample()
    {
        var home = new ShowcasePathAddress { Street = "100 Home Lane", Notes = new Dictionary<string, string> { ["Gate Code"] = "2468" } };
        var addresses = new ShowcaseAddressBook
        {
            home,
            new ShowcasePathAddress { Street = "200 Work Way" },
            new ShowcasePathAddress { Street = "300 Archive Road", Notes = new Dictionary<string, string> { ["Important!"] = "Keep this" } }
        };
        addresses.Home = home;
        var coordinates = new ShowcaseCoordinate[2, 3];
        for (int row = 0; row < 2; row++)
            for (int column = 0; column < 3; column++)
                coordinates[row, column] = new ShowcaseCoordinate { Latitude = row * 10 + column };
        return new ShowcasePathGraph { Addresses = addresses, Coordinates = coordinates };
    }
}

/// <summary>A list subclass that also persists its own Home member; both routes may refer to the same address.<br/></summary>
public sealed class ShowcaseAddressBook : List<ShowcasePathAddress>
{
    public ShowcasePathAddress? Home { get; set; }
}

/// <summary>Address with ordinary members and literal string-keyed notes.<br/></summary>
public sealed class ShowcasePathAddress
{
    public string Street { get; set; } = "";
    public Dictionary<string, string> Notes { get; set; } = [];
}

/// <summary>Coordinate value selected through a multidimensional array path.<br/></summary>
public sealed class ShowcaseCoordinate
{
    public decimal Latitude { get; set; }
}

/// <summary>An ordinary account with numeric and exact-text values used to demonstrate durable unique indexes.<br/></summary>
public sealed class ShowcaseUniqueAccount
{
    public int AccountNumber { get; set; }
    public string Login { get; set; } = "";
    public string Name { get; set; } = "";
    public string Tenant { get; set; } = "";
}

/// <summary>An ordinary document used to compare protected payload policies without storing real secrets in a demo.<br/></summary>
public sealed class ShowcaseVaultRecord
{
    public string Code { get; set; } = "";
    public string Body { get; set; } = "";
    public string SecretCode { get; set; } = "";
}
