using System.Buffers;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using FractalKVS;
using LibraDex;
using Microsoft.Win32.SafeHandles;

namespace AbraxasDB;

/// <summary>
/// Owns a persistent record-file family, its secondary-index catalog, and the typed stores and lifecycle operations built on them.<br/>
/// </summary>
public partial class RecordBase
{
    private const string BackupManifestSuffix = ".abraxas-backup.json";
    private static readonly string[] AuthoritativeBackupSuffixes = [".fractD", ".fractD.shm", ".fractX", ".fractX.shm", ".lbdx", ".idgen"];
    // Non-shipping tests access this private seam by reflection; ordinary data paths never consult it.
    private static Action<string, int>? backupRestoreFault = null;

    /// <summary>Describes the evidence established before restoring a complete snapshot.<br/></summary>
    public enum BackupVerification
    {
        /// <summary>All original member sizes and SHA-256 digests matched a supported manifest; structural/reopen validation also passed.<br/>
        /// A digest detects alteration, not malicious replacement of both data and manifest.<br/></summary>
        ManifestIntegrity = 0,
        /// <summary>A complete legacy snapshot passed structural/reopen validation without proof of original bytes or same-generation coherence.<br/></summary>
        LegacyStructuralOnly = 1
    }

    /// <summary>Rejects path syntax in a logical basename before constructing any owned member path.<br/></summary>
    /// <param name="value">RecordBase basename.<br/></param>
    private static void ValidateRecordBaseName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value is "." or ".." || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            value.Contains('/') || value.Contains('\\') || value.Contains(':') || value.EndsWith('.') || value.EndsWith(' '))
            throw new ArgumentException("RecordBase name must be a basename, not a path.", nameof(value));
    }

    /// <summary>Builds the exact authoritative member names, including durable identity reservations.<br/></summary>
    /// <param name="baseName">Validated basename.<br/></param><returns>The six exact names.<br/></returns>
    private static string[] BackupMemberNames(string baseName)
        => AuthoritativeBackupSuffixes.Select(suffix => baseName + suffix).ToArray();

    /// <summary>Detects an existing family by exact ownership, never by a neighboring name's prefix.<br/></summary>
    /// <param name="directory">Candidate directory.<br/></param><param name="baseName">Validated basename.<br/></param>
    /// <returns>Whether authoritative members or known operation artifacts exist.<br/></returns>
    private static bool HasRecordBaseMembers(string directory, string baseName)
        => BackupMemberNames(baseName).Concat(BackupRuntimeNames(baseName)).Any(member => Path.Exists(Path.Combine(directory, member))) ||
            RetainedRestoreWorkspaces(directory, baseName).Any();

    /// <summary>Finds operation-owned restore directories whose original member set has not been released.<br/></summary>
    /// <param name="directory">Destination directory.<br/></param><param name="baseName">Validated basename.<br/></param>
    /// <returns>Exact generated workspaces retaining at least one original member.<br/></returns>
    private static IEnumerable<string> RetainedRestoreWorkspaces(string directory, string baseName)
    {
        if (!Directory.Exists(directory)) yield break;
        string prefix = "." + baseName + ".restore-";
        foreach (string path in Directory.EnumerateDirectories(directory))
        {
            string leaf = Path.GetFileName(path);
            if (!leaf.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(leaf[prefix.Length..], "N", out _)) continue;
            string original = Path.Combine(path, "original");
            if (!File.Exists(Path.Combine(path, "completed")) && Directory.Exists(original) && Directory.EnumerateFileSystemEntries(original).Any()) yield return path;
        }
    }

    /// <summary>Fails closed on retained restore authority rather than opening or creating an ambiguous active generation.<br/></summary>
    /// <param name="directory">Destination directory.<br/></param><param name="baseName">Validated basename.<br/></param>
    /// <param name="activeRestore">Only the current private publication may validate beside its own retained originals.<br/></param>
    private static void EnsureNoRetainedRestore(string directory, string baseName, string? activeRestore = null)
    {
        foreach (string path in RetainedRestoreWorkspaces(directory, baseName))
            if (!StringComparer.OrdinalIgnoreCase.Equals(path, activeRestore))
                throw new IOException($"RecordBase has retained restore authority at '{path}'. Keep it offline and resolve that recovery set before opening, creating or restoring it.");
    }

    /// <summary>Lists exact non-authoritative artifacts that must not replay against a replacement generation.<br/></summary>
    /// <param name="baseName">Validated basename.<br/></param><returns>Exact artifact names; no wildcard matches.<br/></returns>
    private static string[] BackupRuntimeNames(string baseName)
        => [baseName + BackupManifestSuffix, "." + baseName + PurgeJournalSuffix, "." + baseName + PurgeJournalSuffix + ".tmp",
            baseName + ".lbdx.publication-ready", baseName + ".lbdx.publication-prepare"];

    /// <summary>Requires every authoritative member before any constructor can create or extend storage.<br/></summary>
    /// <param name="directory">Offline or quiescent directory.<br/></param><param name="baseName">Validated basename.<br/></param>
    /// <param name="deep">Whether to validate reachable native routing and selected records.<br/></param><param name="ct">Cancellation token.<br/></param>
    private static void ValidateCompleteRecordBase(string directory, string baseName, bool deep, CancellationToken ct)
    {
        foreach (string member in BackupMemberNames(baseName))
        {
            string path = Path.Combine(directory, member);
            if (!File.Exists(path))
                throw new InvalidDataException($"Incomplete RecordBase: required member '{member}' is missing. Restore a complete backup; missing authority is not automatically recreated.");
            RejectBackupMemberAlias(path);
        }
        SnapshotValidator.Validate(directory, baseName, deep, ct);
        using var generator = new FileStream(Path.Combine(directory, baseName + ".idgen"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (generator.Length != AtomicIntegers.RegionSize)
            throw new InvalidDataException("Incomplete or unsupported identity reservation file.");
    }

    /// <summary>Resolves directory junction/symlink ancestors, including an absent destination's existing parents.<br/></summary>
    /// <param name="path">Absolute or relative directory.<br/></param><returns>Normalized resolved directory identity.<br/></returns>
    private static string ResolveBackupDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full)!;
        string current = root;
        foreach (string segment in full[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            var info = new DirectoryInfo(current);
            if ((info.Exists || Path.Exists(current)) && (info.Attributes & FileAttributes.ReparsePoint) != 0)
                current = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                    ?? throw new IOException($"Could not resolve directory alias '{current}'.");
        }
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
    }

    /// <summary>Rejects a source/destination directory identity collision before creating staging or changing files.<br/></summary>
    /// <param name="source">Source directory.<br/></param><param name="target">Destination directory.<br/></param>
    private static void RejectBackupDirectoryAlias(string source, string target)
    {
        if (string.Equals(ResolveBackupDirectory(source), ResolveBackupDirectory(target), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Backup source and destination resolve to the same directory.");
    }

    /// <summary>Prevents an archive path from being overwritten or retired as an authoritative source/destination member.<br/></summary>
    /// <param name="archive">Absolute archive path.<br/></param><param name="directory">Live source or restore destination directory.<br/></param>
    /// <param name="baseName">Validated RecordBase basename.<br/></param>
    private static void RejectArchiveMemberCollision(string archive, string directory, string baseName)
    {
        string archiveDirectory = ResolveBackupDirectory(Path.GetDirectoryName(Path.GetFullPath(archive))!);
        if (StringComparer.OrdinalIgnoreCase.Equals(archiveDirectory, ResolveBackupDirectory(directory)) &&
            BackupMemberNames(baseName).Concat(BackupRuntimeNames(baseName)).Contains(Path.GetFileName(archive), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("An archive cannot occupy an authoritative RecordBase member or recovery-artifact path.", nameof(archive));
    }

    /// <summary>Rejects redirected members and, on Windows, multiply linked files before backup/restore ownership changes.<br/></summary>
    /// <param name="path">Existing exact member.<br/></param>
    private static void RejectBackupMemberAlias(string path)
    {
        if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new IOException($"Backup member '{path}' is not an ordinary file.");
        if (OperatingSystem.IsWindows())
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!GetBackupFileInformation(handle, out var info))
                throw new IOException($"Cannot establish backup member identity for '{path}'.", Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
            if (info.NumberOfLinks != 1 || (info.Attributes & (uint)FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Backup member '{path}' has a filesystem alias.");
        }
    }

    /// <summary>Creates a coherent complete snapshot and manifest in private destination-local staging before replacing any previous backup.<br/></summary>
    /// <param name="target">Resolved target directory.<br/></param><param name="options">Overwrite policy.<br/></param>
    /// <param name="ct">Cancellation between copy blocks and publication phases.<br/></param><returns>Installed snapshot metadata including the manifest.<br/></returns>
    private BackupResult CreateCompleteBackup(string target, BackupOptions options, CancellationToken ct)
    {
        ValidateCompleteRecordBase(dataFilesLocation.FullName, name, deep: false, ct);
        if (BackupRuntimeNames(name).Skip(1).Any(member => Path.Exists(Path.Combine(dataFilesLocation.FullName, member))))
            throw new IOException("Pending runtime recovery must complete before a new RecordBase backup can be created.");
        Directory.CreateDirectory(target);
        string operation = Path.Combine(target, "." + name + ".backup-" + Guid.NewGuid().ToString("N"));
        string stage = Path.Combine(operation, "new");
        Directory.CreateDirectory(stage);
        bool retain = false;
        try
        {
            string[] members = BackupMemberNames(name);
            string manifestName = name + BackupManifestSuffix;
            EnsureBackupDestination(target, members.Append(manifestName), options.OverwriteExisting);
            _ = data.Backup(stage, cancellationToken: ct);
            _ = libraDexCatalog.Backup(Path.Combine(stage, name + ".lbdx"), cancellationToken: ct);
            _ = CopyBackupMember(Path.Combine(dataFilesLocation.FullName, name + ".idgen"), Path.Combine(stage, name + ".idgen"), ct, sourceIsQuiescentLiveFile: true);
            var entries = new List<BackupManifestMember>();
            foreach (string member in members)
                entries.Add(HashBackupMember(Path.Combine(stage, member), ct));
            var manifest = new BackupManifest(1, Guid.NewGuid(), name, DateTime.UtcNow, entries.ToArray());
            using (var stream = new FileStream(Path.Combine(stage, manifestName), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, manifest);
                stream.Flush(flushToDisk: true);
            }
            ulong bytes = checked((ulong)entries.Sum(entry => entry.Length) + (ulong)new FileInfo(Path.Combine(stage, manifestName)).Length);
            PublishBackupSet(operation, stage, target, members.Append(manifestName).ToArray(), [], options.OverwriteExisting,
                null, ct, ref retain);
            return new BackupResult(target, members.Length + 1, bytes, manifest.CreatedUtc);
        }
        finally { if (!retain) TryDeleteBackupWorkspace(operation); }
    }

    /// <summary>Copies and verifies original bytes before ownership changes, validates staging, then installs a complete rollback-protected record base.<br/></summary>
    /// <param name="sourceDirectory">Snapshot directory.<br/></param><param name="targetDirectory">Offline destination.<br/></param>
    /// <param name="baseName">Exact basename.<br/></param><param name="configuration">Reopen configuration.<br/></param>
    /// <param name="options">Compatibility and overwrite policy.<br/></param><param name="ct">Cancellation token.<br/></param>
    /// <returns>Verification evidence and restored authoritative byte counts.<br/></returns>
    private static RestoreBackupResult RestoreCompleteBackup(string sourceDirectory, string targetDirectory, string baseName,
        RecordBaseConfiguration configuration, RestoreBackupOptions options, CancellationToken ct)
    {
        ValidateRecordBaseName(baseName);
        ct.ThrowIfCancellationRequested();
        string source = ResolveBackupDirectory(sourceDirectory);
        string target = ResolveBackupDirectory(targetDirectory);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        RejectBackupDirectoryAlias(source, target);
        EnsureNoRetainedRestore(target, baseName);
        string[] members = BackupMemberNames(baseName);
        // Preflight is read-only; source validation must not adopt ownership or initialize a missing member.
        ValidateCompleteRecordBase(source, baseName, deep: false, ct);
        BackupManifest? manifest = ReadBackupManifest(source, baseName, members, options.AllowLegacyBackupWithoutManifest);
        using var sourceLease = new BackupSourceLease(source,
            manifest is null ? members : members.Append(baseName + BackupManifestSuffix));
        string[] artifacts = BackupRuntimeNames(baseName);
        if (!options.ClearExisting && artifacts.Skip(1).Any(member => Path.Exists(Path.Combine(target, member))))
            throw new IOException("Destination has pending runtime recovery artifacts; ClearExisting=true is required to retire that generation.");
        EnsureBackupDestination(target, members.Concat(artifacts), options.OverwriteExisting);
        Directory.CreateDirectory(target);
        string operation = Path.Combine(target, "." + baseName + ".restore-" + Guid.NewGuid().ToString("N"));
        string stage = Path.Combine(operation, "new");
        Directory.CreateDirectory(stage);
        bool retain = false;
        try
        {
            ulong bytes = 0;
            foreach (string member in members)
            {
                var copied = CopyBackupMember(Path.Combine(source, member), Path.Combine(stage, member), ct);
                if (manifest is not null)
                {
                    var expected = manifest.Members.Single(entry => StringComparer.OrdinalIgnoreCase.Equals(entry.Name, member));
                    if (copied.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(copied.Sha256), Convert.FromHexString(expected.Sha256)))
                        throw new InvalidDataException($"Backup integrity mismatch for '{member}'.");
                }
                bytes = checked(bytes + (ulong)copied.Length);
            }
            ValidateCompleteRecordBase(stage, baseName, deep: true, ct);
            AdoptAndValidateRestoredRecordBase(stage, baseName, configuration, ct);
            backupRestoreFault?.Invoke("staged", -1);
            ct.ThrowIfCancellationRequested();
            RejectBackupDirectoryAlias(source, target);
            PublishBackupSet(operation, stage, target, members, artifacts, options.OverwriteExisting,
                () => AdoptAndValidateRestoredRecordBase(target, baseName, configuration, ct, operation), ct, ref retain);
            return new RestoreBackupResult(source, target, members.Length, bytes,
                manifest is null ? BackupVerification.LegacyStructuralOnly : BackupVerification.ManifestIntegrity, DateTime.UtcNow);
        }
        finally { if (!retain) TryDeleteBackupWorkspace(operation); }
    }

    /// <summary>Validates the bounded, versioned manifest and its exact member set; missing integrity metadata needs explicit legacy consent.<br/></summary>
    /// <param name="directory">Snapshot directory.<br/></param><param name="baseName">Expected basename.<br/></param>
    /// <param name="members">Exact required names.<br/></param><param name="allowLegacy">Explicit legacy compatibility selection.<br/></param>
    /// <returns>Supported manifest or null for an explicitly permitted legacy snapshot.<br/></returns>
    private static BackupManifest? ReadBackupManifest(string directory, string baseName, string[] members, bool allowLegacy)
    {
        string path = Path.Combine(directory, baseName + BackupManifestSuffix);
        if (!File.Exists(path))
        {
            if (allowLegacy) return null;
            throw new InvalidDataException("Backup integrity manifest is missing. Only complete legacy backups can be explicitly accepted with AllowLegacyBackupWithoutManifest=true.");
        }
        RejectBackupMemberAlias(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > 65536) throw new InvalidDataException("Invalid backup manifest length.");
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 8 });
        ValidateBackupJsonNames(document.RootElement);
        var manifest = document.RootElement.Deserialize<BackupManifest>() ?? throw new InvalidDataException("Empty backup manifest.");
        if (manifest.FormatVersion != 1 || manifest.SnapshotId == Guid.Empty || !StringComparer.Ordinal.Equals(manifest.Name, baseName) ||
            manifest.Members is null || manifest.Members.Length != members.Length)
            throw new InvalidDataException("Unsupported or mismatched backup manifest.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in manifest.Members)
        {
            if (member is null || !members.Contains(member.Name, StringComparer.OrdinalIgnoreCase) || !names.Add(member.Name) ||
                member.Length < 0 || member.Sha256 is null || member.Sha256.Length != 64 || !member.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid, duplicate or unexpected backup manifest member.");
        }
        return manifest;
    }

    /// <summary>Rejects duplicate JSON property names rather than silently choosing the last conflicting value.<br/></summary>
    /// <param name="value">One bounded manifest JSON value.<br/></param>
    private static void ValidateBackupJsonNames(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate backup manifest JSON property.");
                ValidateBackupJsonNames(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var element in value.EnumerateArray()) ValidateBackupJsonNames(element);
    }

    /// <summary>Streams one immutable source member into a new private file with pooled copy storage and SHA-256 evidence.<br/></summary>
    /// <param name="source">Exact source file.<br/></param><param name="target">New operation-owned destination.<br/></param>
    /// <param name="ct">Cancellation between blocks.<br/></param>
    /// <param name="sourceIsQuiescentLiveFile">Allows the already-open, durably flushed ID reservation mapping only while the caller holds the cross-component drain.<br/></param>
    /// <returns>Original copied-byte evidence.<br/></returns>
    private static BackupManifestMember CopyBackupMember(string source, string target, CancellationToken ct, bool sourceIsQuiescentLiveFile = false)
    {
        RejectBackupMemberAlias(source);
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read,
            sourceIsQuiescentLiveFile ? FileShare.ReadWrite | FileShare.Delete : FileShare.Read, 65536, FileOptions.SequentialScan);
        using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan);
        var result = HashBackupStream(input, Path.GetFileName(source), output, ct);
        output.Flush(flushToDisk: true);
        return result;
    }

    /// <summary>Hashes an operation-owned staged snapshot without altering its bytes.<br/></summary>
    /// <param name="path">Staged member.<br/></param><param name="ct">Cancellation token.<br/></param>
    /// <returns>Member size and digest.<br/></returns>
    private static BackupManifestMember HashBackupMember(string path, CancellationToken ct)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
        return HashBackupStream(stream, Path.GetFileName(path), null, ct);
    }

    /// <summary>Shares one bounded pooled hash/copy loop across new backup manifests and restore integrity checks.<br/></summary>
    /// <param name="input">Stable source stream.<br/></param><param name="member">Exact basename.<br/></param>
    /// <param name="output">Optional new staged output.<br/></param><param name="ct">Cancellation between blocks.<br/></param>
    /// <returns>Exact bytes read and SHA-256.<br/></returns>
    private static BackupManifestMember HashBackupStream(FileStream input, string member, FileStream? output, CancellationToken ct)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long length = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                int read = input.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                output?.Write(buffer, 0, read);
                hash.AppendData(buffer, 0, read);
                length = checked(length + read);
                backupRestoreFault?.Invoke("copy-block", read);
            }
            return new BackupManifestMember(member, length, Convert.ToHexString(hash.GetHashAndReset()));
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    /// <summary>Adopts only verified private/installed copies, opens the actual RecordBase, and validates catalog traversal and identity reservations.<br/></summary>
    /// <param name="directory">Staged or installed directory.<br/></param><param name="baseName">Exact basename.<br/></param>
    /// <param name="configuration">Normal reopen configuration.<br/></param><param name="ct">Cancellation between records/index entries.<br/></param>
    /// <param name="activeRestore">Private workspace allowed only during this operation's final validation.<br/></param>
    private static void AdoptAndValidateRestoredRecordBase(string directory, string baseName, RecordBaseConfiguration configuration, CancellationToken ct, string? activeRestore = null)
    {
        FractalStore.TakeOwnership(directory, baseName, configuration.FractalStoreConfig with { CreateIfNotExists = false });
        var restored = new RecordBase { dataFilesLocation = new DirectoryInfo(directory), name = baseName, config = configuration };
        restored.Init(createNew: false, activeRestore);
        using var restoredLease = restored;
        long[] counters = new long[256];
        using (var stream = new FileStream(Path.Combine(directory, baseName + ".idgen"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            stream.ReadExactly(MemoryMarshal.AsBytes(counters.AsSpan()));
        foreach (ulong identity in restored.data.IterateLiveRecordIDs())
        {
            ct.ThrowIfCancellationRequested();
            byte store = (byte)(identity >> 56);
            if (store == 0) continue;
            ulong counter = identity & 0x00FFFFFFFFFFFFFFUL;
            if (!RowIDGenerator.IsValidRecordID(identity, store) || counters[store] < 0 || (ulong)counters[store] < counter || (ulong)counters[store] > RowIDGenerator.MaximumRecordID)
                throw new InvalidDataException("Identity reservation counters do not cover the restored live records for this edition.");
        }
        var assessment = restored.libraDexCatalog.Maintenance.Assess();
        if (assessment.ProjectionIssues.Count != 0)
            throw new InvalidDataException("Restored catalog projection metadata is inconsistent.");
        ct.ThrowIfCancellationRequested();
        var infos = restored.libraDexCatalog.Indexes.List();
        var companionSlots = BackupCompanionSlots(infos);
        foreach (var info in infos)
        {
            ct.ThrowIfCancellationRequested();
            if (companionSlots.Contains(info.SlotIndex)) continue;
            var set = restored.libraDexCatalog.Indexes.IndexSet(info.Group);
            var index = set.Define(info.Name).Open();
            foreach (var tuple in set.IterateTuples(index))
            {
                ct.ThrowIfCancellationRequested();
                _ = tuple.Identity;
            }
        }
    }

    /// <summary>Identifies exact physical projection companions from their logical owners, never by guessed names or empty-group heuristics.<br/></summary>
    /// <param name="infos">Complete catalog directory metadata.<br/></param><returns>Owned companion slot ordinals.<br/></returns>
    private static HashSet<int> BackupCompanionSlots(IEnumerable<CatalogIndexInfo> infos)
    {
        var slots = new HashSet<int>();
        foreach (var info in infos)
        {
            foreach (int slot in new[] { info.ExactReversedProjectionSlotIndex, info.FoldedProjectionSlotIndex,
                info.SortKeyProjectionSlotIndex, info.FoldedReversedProjectionSlotIndex, info.NormalizedProjectionSlotIndex, info.NormalizedReversedProjectionSlotIndex })
                if (slot >= 0) slots.Add(slot);
            if (info.SortKeyProfiles is not null)
                foreach (var profile in info.SortKeyProfiles)
                    if (profile.SlotIndex >= 0) slots.Add(profile.SlotIndex);
        }
        return slots;
    }

    /// <summary>Checks exact destination collisions and aliases without deleting anything.<br/></summary>
    /// <param name="target">Destination directory.<br/></param><param name="members">Exact owned names.<br/></param>
    /// <param name="overwrite">Whether existing members can be retired.<br/></param>
    private static void EnsureBackupDestination(string target, IEnumerable<string> members, bool overwrite)
    {
        foreach (string member in members)
        {
            string path = Path.Combine(target, member);
            if (!Path.Exists(path)) continue;
            if (!overwrite) throw new IOException($"Destination member already exists: '{path}'.");
            RejectBackupMemberAlias(path);
            // Best-effort offline preflight; the caller must still exclude opens/writers for the whole operation.
            using var offlineProbe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        }
    }

    /// <summary>Publishes exact staged members, retaining originals until validation and final cancellation acceptance succeed.<br/>
    /// Handled failures roll back every moved member; rollback failure preserves the operation directory and reports its path.<br/>
    /// No multi-file crash/power-loss atomicity is implied.<br/></summary>
    /// <param name="operation">Unique destination-local workspace.<br/></param><param name="stage">New member directory.<br/></param>
    /// <param name="target">Offline destination directory.<br/></param><param name="members">Exact new names.<br/></param>
    /// <param name="artifacts">Exact old runtime artifacts to retire, never prefix matches.<br/></param>
    /// <param name="overwrite">Collision policy.<br/></param><param name="validate">Optional final reopen validation.<br/></param>
    /// <param name="ct">Cancellation checked before each move and after validation.<br/></param>
    /// <param name="retain">Set when failed rollback requires preserving all recovery artifacts.<br/></param>
    private static void PublishBackupSet(string operation, string stage, string target, string[] members, string[] artifacts,
        bool overwrite, Action? validate, CancellationToken ct, ref bool retain)
    {
        string original = Path.Combine(operation, "original");
        Directory.CreateDirectory(original);
        var retired = new List<string>();
        var installed = new List<string>();
        try
        {
            EnsureBackupDestination(target, members.Concat(artifacts), overwrite);
            foreach (string member in members.Concat(artifacts).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                string path = Path.Combine(target, member);
                if (File.Exists(path))
                {
                    File.Move(path, Path.Combine(original, member));
                    retired.Add(member);
                }
                backupRestoreFault?.Invoke("retire", retired.Count);
            }
            for (int i = 0; i < members.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                File.Move(Path.Combine(stage, members[i]), Path.Combine(target, members[i]));
                installed.Add(members[i]);
                backupRestoreFault?.Invoke("install", i);
            }
            backupRestoreFault?.Invoke("reopen", -1);
            validate?.Invoke();
            backupRestoreFault?.Invoke("validated", -1);
            ct.ThrowIfCancellationRequested();
            // Records successful final validation if later best-effort retirement cleanup cannot remove this workspace.
            using var completed = new FileStream(Path.Combine(operation, "completed"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            completed.WriteByte(1);
            completed.Flush(flushToDisk: true);
        }
        catch (Exception failure)
        {
            var errors = new List<Exception>();
            try { backupRestoreFault?.Invoke("rollback-start", -1); }
            catch (Exception exception) { errors.Add(exception); }
            for (int i = installed.Count - 1; i >= 0; i--)
            {
                try { File.Delete(Path.Combine(target, installed[i])); }
                catch (Exception exception) { errors.Add(exception); }
            }
            for (int i = retired.Count - 1; i >= 0; i--)
            {
                try
                {
                    backupRestoreFault?.Invoke("rollback", i);
                    File.Move(Path.Combine(original, retired[i]), Path.Combine(target, retired[i]));
                }
                catch (Exception exception) { errors.Add(exception); }
            }
            if (errors.Count != 0)
            {
                retain = true;
                errors.Insert(0, failure);
                var exception = new IOException($"Backup/restore rollback was incomplete. Keep target offline. Recovery files are retained at '{operation}'.", new AggregateException(errors));
                exception.Data["RecoveryDirectory"] = operation;
                throw exception;
            }
            throw;
        }
        // Cleanup is outside the transaction: never interpret failure to delete retired files as a reason to undo success.
        TryDeleteBackupWorkspace(original);
    }

    /// <summary>Removes only a caller-generated private operation workspace; cleanup failure never destroys recovery evidence.<br/></summary>
    /// <param name="path">Exact unique workspace already owned by this operation.<br/></param>
    private static void TryDeleteBackupWorkspace(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Extracts only unique flat exact snapshot members; traversal, duplicates and unrecognized archive content fail before publication.<br/></summary>
    /// <param name="archivePath">Existing source archive.<br/></param><param name="directory">Private empty extraction directory.<br/></param>
    /// <param name="baseName">Expected basename.<br/></param><param name="ct">Cancellation between streamed blocks.<br/></param>
    private static void ExtractCompleteBackupArchive(string archivePath, string directory, string baseName, CancellationToken ct)
    {
        ValidateRecordBaseName(baseName);
        ct.ThrowIfCancellationRequested();
        var allowed = new HashSet<string>(BackupMemberNames(baseName).Append(baseName + BackupManifestSuffix), StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
            if (!allowed.Contains(entry.FullName) || !seen.Add(entry.FullName) ||
                entry.FullName.Equals(baseName + BackupManifestSuffix, StringComparison.OrdinalIgnoreCase) && entry.Length > 65536)
                throw new InvalidDataException("Backup archive contains duplicate, unexpected or non-flat member paths.");
        byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                using var input = entry.Open();
                using var output = new FileStream(Path.Combine(directory, entry.FullName), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    int read = input.Read(buffer, 0, buffer.Length);
                    if (read == 0) break;
                    output.Write(buffer, 0, read);
                }
                output.Flush(flushToDisk: true);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    /// <summary>Version-one integrity metadata written only after all complete snapshot members exist.<br/></summary>
    private sealed record BackupManifest(int FormatVersion, Guid SnapshotId, string Name, DateTime CreatedUtc, BackupManifestMember[] Members);
    /// <summary>Exact original member name, size and SHA-256, checked before ownership rehoming.<br/></summary>
    private sealed record BackupManifestMember(string Name, long Length, string Sha256);

    /// <summary>Holds every source member read-only through staging/publication so Windows writers cannot change or replace members between copies.<br/></summary>
    private sealed class BackupSourceLease : IDisposable
    {
        private readonly List<FileStream> streams = new();

        /// <summary>Acquires the complete immutable input set, releasing partial acquisitions if one member is unavailable.<br/></summary>
        /// <param name="directory">Snapshot directory.<br/></param><param name="members">Exact authoritative/manifest names.<br/></param>
        internal BackupSourceLease(string directory, IEnumerable<string> members)
        {
            try
            {
                foreach (string member in members)
                {
                    string path = Path.Combine(directory, member);
                    RejectBackupMemberAlias(path);
                    streams.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
                }
            }
            catch { Dispose(); throw; }
        }

        /// <summary>Releases all immutable source handles; no input bytes or names are changed.<br/></summary>
        public void Dispose()
        {
            foreach (var stream in streams) stream.Dispose();
            streams.Clear();
        }
    }

    /// <summary>Win32 file identity fields used solely to reject redirected or multiply linked offline members.<br/></summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BackupFileInformation
    {
        internal uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        internal uint VolumeSerial, SizeHigh, SizeLow, NumberOfLinks, IndexHigh, IndexLow;
    }

    /// <summary>Reads file identity from an already-open Windows handle without altering storage.<br/></summary>
    /// <param name="handle">Read-only handle.<br/></param><param name="information">Native file identity.<br/></param>
    /// <returns>Whether Windows returned identity information.<br/></returns>
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetBackupFileInformation(SafeFileHandle handle, out BackupFileInformation information);
}
