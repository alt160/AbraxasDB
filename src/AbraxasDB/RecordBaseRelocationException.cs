namespace AbraxasDB;

/// <summary>
/// Reports that a complete RecordBase was opened from a directory or filename different from its persisted ownership location.<br/>
/// Catch this Abraxas exception to inspect the attempted location and decide whether to explicitly call RecordBase.TakeOwnership before retrying.<br/>
/// A location mismatch is not proof of corruption, and catching this exception does not authorize automatic adoption of untrusted files.<br/>
/// The original storage exception remains available through InnerException for diagnostics, not as an application-facing dependency.<br/>
/// </summary>
public sealed class RecordBaseRelocationException : IOException
{
    /// <summary>
    /// Creates immutable relocation context using only Abraxas/BCL values.<br/>
    /// No files are opened, created, adopted or normalized by constructing this exception.<br/>
    /// The library supplies an absolute attempted directory; manually supplied relative directories follow normal process-current-directory behavior when explicitly adopted.<br/>
    /// </summary>
    /// <param name="currentDirectory">Directory used for the attempted RecordBase open.<br/></param>
    /// <param name="recordBaseName">Exact logical RecordBase basename, rather than a physical filename parsed for extensions.<br/></param>
    /// <param name="storedDirectory">Directory recorded in the affected header, when available.<br/></param>
    /// <param name="storedFileName">Physical filename recorded in the affected header, when available.<br/></param>
    /// <param name="currentFileName">Physical filename used for the attempted open, when available.<br/></param>
    /// <param name="fileIdentity">Affected record-file identity, not a logical store or record identity, when available.<br/></param>
    /// <param name="isRecordIndexFile">Whether the mismatched header belongs to the numeric record index instead of record data.<br/></param>
    /// <param name="innerException">Original failure retained without replacing or copying its diagnostic context.<br/></param>
    public RecordBaseRelocationException(
        string currentDirectory,
        string recordBaseName,
        string? storedDirectory = null,
        string? storedFileName = null,
        string? currentFileName = null,
        Guid? fileIdentity = null,
        bool isRecordIndexFile = false,
        Exception? innerException = null)
        : base($"RecordBase '{recordBaseName}' has a relocated {(isRecordIndexFile ? "record-index" : "record-data")} file. " +
            $"Stored directory='{storedDirectory ?? "?"}', filename='{storedFileName ?? "?"}'; " +
            $"attempted directory='{currentDirectory}', filename='{currentFileName ?? "?"}'. " +
            "Keep the complete RecordBase offline and explicitly call RecordBase.TakeOwnership(...) only if this move or copy was intended.", innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordBaseName);
        CurrentDirectory = currentDirectory;
        RecordBaseName = recordBaseName;
        StoredDirectory = storedDirectory;
        StoredFileName = storedFileName;
        CurrentFileName = currentFileName;
        FileIdentity = fileIdentity;
        IsRecordIndexFile = isRecordIndexFile;
    }

    /// <summary>Gets the directory used for the attempted open; library-generated exceptions contain an absolute path.<br/></summary>
    public string CurrentDirectory { get; }
    /// <summary>Gets the exact logical RecordBase basename used for the attempted open, including any dots.<br/></summary>
    public string RecordBaseName { get; }
    /// <summary>Gets the directory recorded in the affected header, or null when unavailable.<br/></summary>
    public string? StoredDirectory { get; }
    /// <summary>Gets the physical filename recorded in the affected header, or null when unavailable.<br/></summary>
    public string? StoredFileName { get; }
    /// <summary>Gets the physical filename used for the attempted open, or null when unavailable.<br/></summary>
    public string? CurrentFileName { get; }
    /// <summary>Gets the affected record-file identity when available; this is not a logical store or record identity.<br/></summary>
    public Guid? FileIdentity { get; }
    /// <summary>Gets whether the ownership mismatch belongs to the numeric record-index file.<br/></summary>
    public bool IsRecordIndexFile { get; }
    /// <summary>Gets whether the ownership mismatch belongs to the record-data file.<br/></summary>
    public bool IsRecordDataFile => !IsRecordIndexFile;
}
