using AbraxasDB;

namespace AbraxasShowcase;

/// <summary>Prepares one stable store whose declared model demonstrates Inheto shape controls used through Abraxas.<br/></summary>
internal static class MaterializationGroupSetup
{
    /// <summary>Creates the durable document store once; runtime hooks are reapplied whenever that store opens.<br/></summary>
    /// <param name="directory">Showcase-owned RecordBase directory.<br/></param>
    internal static void CreateEnvironment(string directory)
    {
        using RecordBase recordBase = RecordBase.CreateOrOpen(directory, "showcase");
        recordBase.CreateOrOpenStore<ShowcaseHookDocument>("hook-documents", "Hook and materialization examples");
    }

    /// <summary>Registers exclusions, path and type codecs, and an activator before any document is read or written.<br/></summary>
    /// <param name="documents">Opened typed store whose options are scoped to this group.<br/></param>
    internal static void Configure(RecordStore<ShowcaseHookDocument> documents)
    {
        documents.SerializationOptions.ExcludedMembers.Add(".PrivateMemo");
        documents.SerializationOptions.ExcludedMembers.Add(".Address.CreatedBy");
        documents.SerializationOptions.ExcludedTypes.Add(typeof(ShowcaseHookTransient));
        documents.SerializationOptions.AddTypeSerializer<ShowcaseHookToken>(value => BitConverter.GetBytes(value.Code));
        documents.DeserializationOptions.AddTypeDeserializer<ShowcaseHookToken>(bytes => new ShowcaseHookToken { Code = BitConverter.ToInt32(bytes) });
        documents.SerializationOptions.AddSerializer<ShowcaseHookToken>(".PathToken", value => BitConverter.GetBytes(checked(value.Code + 1000)));
        documents.DeserializationOptions.AddDeserializer<ShowcaseHookToken>(".PathToken", bytes => new ShowcaseHookToken { Code = BitConverter.ToInt32(bytes) - 1000 });
        documents.SerializationOptions.AddTypeSerializer<ShowcaseContextualPayload>(ShowcaseContextualCodec.Encode);
        documents.DeserializationOptions.AddTypeDeserializer<ShowcaseContextualPayload>(ShowcaseContextualCodec.Decode);
        documents.DeserializationOptions.AddActivator<ShowcaseHookAddress>(_ => new ShowcaseHookAddress { CreatedBy = "Custom activator" });
    }

    /// <summary>Generates deterministic documents with distinct values for every configured serialization boundary.<br/></summary>
    /// <param name="directory">Showcase-owned RecordBase directory.<br/></param>
    /// <param name="count">Number of root documents to write.<br/></param>
    /// <param name="tokensPerDocument">Number of type-hook tokens in each document.<br/></param>
    /// <param name="seed">Repeatable seed controlling token values.<br/></param>
    /// <param name="progress">Optional setup progress callback.<br/></param>
    /// <param name="token">Cancellation observed between document writes.<br/></param>
    internal static void GenerateData(string directory, int count, int tokensPerDocument, int seed, Action<int, int, string>? progress, CancellationToken token)
    {
        using RecordBase recordBase = RecordBase.CreateOrOpen(directory, "showcase");
        RecordStore<ShowcaseHookDocument> documents = recordBase.OpenStore<ShowcaseHookDocument>("hook-documents");
        Configure(documents);
        var random = new Random(seed);
        for (int index = 0; index < count; index++)
        {
            token.ThrowIfCancellationRequested();
            documents.Put(new ShowcaseHookDocument
            {
                Title = $"Document {index + 1:0000000}",
                PrivateMemo = $"Not stored {index + 1}",
                PathToken = new ShowcaseHookToken { Code = index + 1 },
                TypeTokens = Enumerable.Range(0, tokensPerDocument).Select(_ => new ShowcaseHookToken { Code = random.Next(1, 10000) }).ToArray(),
                Address = new ShowcaseHookAddress { City = index % 2 == 0 ? "Phoenix" : "Seattle", CreatedBy = "Writer instance" },
                Transient = new ShowcaseHookTransient { RuntimeNote = "Not stored" }
            });
            progress?.Invoke(index + 1, count, "Generating hook documents");
        }
    }
}
