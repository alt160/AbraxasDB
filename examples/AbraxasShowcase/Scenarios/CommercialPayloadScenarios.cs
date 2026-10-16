using AbraxasDB;
using System.Diagnostics;
using System.Security.Cryptography;

namespace AbraxasShowcase;

/// <summary>Self-contained commercial-edition examples for adaptive compression, authenticated encryption, and volatile sensitive-key lookup.<br/></summary>
internal static class CommercialPayloadScenarios
{
    internal static IReadOnlyList<IShowcaseScenario> All { get; } =
    [
        New("adaptive-payload-compression", "Compress application records adaptively", "Compare a small raw record with a large Brotli-compressed record and inspect content-free savings telemetry.", 900,
            "Commercial/AdaptiveCompression.cs.txt", "Compression applies to application-record payloads, not automatically to index keys. Abraxas attempts Brotli only above the configured size and stores it only when the required byte savings are met.",
            ["adaptive Brotli", "raw fallback", "payload telemetry"], ["RecordPayloadCompressionOptions", "CompressionTelemetry"], AdaptiveCompression),
        New("encrypted-payload-roundtrip", "Encrypt and reopen a record", "Write an AES-256-GCM application record, then reopen it with the same developer-owned key resolver.", 910,
            "Commercial/EncryptedRoundTrip.cs.txt", "The application provides a 32-byte AES key by key ID. Abraxas encrypts the application record payload and authenticates its record/store binding. The generated key in this example is temporary demonstration material: a real app must retain keys securely for later reads.",
            ["AES-256-GCM", "developer-owned keys", "authenticated payload"], ["RecordPayloadEncryptionOptions.AesGcm", "RecordBase.CreateOrOpen"], EncryptedRoundTrip),
        New("compressed-and-encrypted-payload", "Compress and encrypt the same record", "Enable both policies and observe that the application payload compresses before encryption.", 920,
            "Commercial/CompressedAndEncrypted.cs.txt", "Compression and encryption are independent settings and can be combined. Abraxas chooses whether Brotli saves enough bytes, then wraps the resulting payload in AES-GCM. Compression telemetry measures the inner payload, not ciphertext size or total RecordBase size.",
            ["combined policies", "compression before encryption", "telemetry scope"], ["RecordPayloadCompressionOptions.Brotli", "RecordPayloadEncryptionOptions.AesGcm"], CompressedAndEncrypted),
        New("encrypted-payload-memory-index", "Keep a sensitive lookup index in memory", "Encrypt the durable record and use a memory-only index for its synthetic sensitive lookup value.", 930,
            "Commercial/EncryptedMemoryIndex.cs.txt", "A memory-only index avoids a durable LibraDex copy of the lookup key, but the property still lives in its stored record. Encrypting the record payload addresses that second exposure. Other metadata, memory, swap, backups, and temporary files need their own threat-model review.",
            ["memory-only index", "encrypted record", "index visibility boundary"], ["CreateMemoryIndex", "GetIndexes", "RecordPayloadEncryptionOptions.AesGcm"], EncryptedMemoryIndex)
    ];

    /// <summary>Registers a readable commercial lesson with one exact standalone code sample and compiled native execution delegate.<br/></summary>
    /// <param name="id">Stable scenario identity.<br/></param>
    /// <param name="title">Learner-facing title.<br/></param>
    /// <param name="description">Concise demonstrated outcome.<br/></param>
    /// <param name="order">Learning-path order after the FOSS scenarios.<br/></param>
    /// <param name="source">Embedded standalone C# example fragment.<br/></param>
    /// <param name="explanation">Practical feature explanation and caveats.<br/></param>
    /// <param name="concepts">Searchable lesson topics.<br/></param>
    /// <param name="apiNames">Public Abraxas APIs used by the lesson.<br/></param>
    /// <param name="run">Edition-gated native scenario action.<br/></param>
    /// <returns>One cataloged scenario whose source is visible in every edition.<br/></returns>
    private static IShowcaseScenario New(string id, string title, string description, int order, string source,
        string explanation, string[] concepts, string[] apiNames, Func<ShowcaseWorkspace, CancellationToken, ScenarioResult> run)
        => new CatalogActionScenario(id, "Protected record payloads", title, description, "Small isolated write and reopen", true,
            new ScenarioDocumentation(explanation, concepts, apiNames, ["commercial edition", "protected payload"], ["Create and reopen a RecordBase"], order), source, run);

    /// <summary>Demonstrates adaptive compression and distinguishes a skipped small record from a compressed large one.<br/></summary>
    /// <param name="workspace">Isolated Showcase-owned directory prepared for this self-contained example.<br/></param>
    /// <param name="token">Cancellation signal checked before storage and reopen.<br/></param>
    /// <returns>Content-free compression telemetry and durable-value proof.<br/></returns>
    private static ScenarioResult AdaptiveCompression(ShowcaseWorkspace workspace, CancellationToken token)
    {
        throw new NotSupportedException("Commercial Abraxas payload compression is unavailable in this FOSS build.");
    }

    /// <summary>Writes and reopens an authenticated encrypted application record using one temporary demonstration key.<br/></summary>
    /// <param name="workspace">Isolated Showcase-owned directory prepared for this self-contained example.<br/></param>
    /// <param name="token">Cancellation signal checked before storage and reopen.<br/></param>
    /// <returns>Encrypted round-trip proof without exposing key bytes.<br/></returns>
    private static ScenarioResult EncryptedRoundTrip(ShowcaseWorkspace workspace, CancellationToken token)
    {
        throw new NotSupportedException("Commercial Abraxas payload encryption is unavailable in this FOSS build.");
    }

    /// <summary>Runs Brotli and AES-GCM together and verifies the inner compression telemetry and final materialized value.<br/></summary>
    /// <param name="workspace">Isolated Showcase-owned directory prepared for this self-contained example.<br/></param>
    /// <param name="token">Cancellation signal checked before storage and reopen.<br/></param>
    /// <returns>Combined-policy proof with a content-free compression observation.<br/></returns>
    private static ScenarioResult CompressedAndEncrypted(ShowcaseWorkspace workspace, CancellationToken token)
    {
        throw new NotSupportedException("Commercial Abraxas payload policies are unavailable in this FOSS build.");
    }

    /// <summary>Combines encrypted durable storage with a volatile lookup index so the sensitive key has no durable LibraDex copy.<br/></summary>
    /// <param name="workspace">Isolated Showcase-owned directory prepared for this self-contained example.<br/></param>
    /// <param name="token">Cancellation signal checked before storage and reopen.<br/></param>
    /// <returns>Proof of encrypted-value recovery and memory-index lifecycle.<br/></returns>
    private static ScenarioResult EncryptedMemoryIndex(ShowcaseWorkspace workspace, CancellationToken token)
    {
        throw new NotSupportedException("Commercial Abraxas payload encryption is unavailable in this FOSS build.");
    }
}
