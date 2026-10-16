using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace AbraxasDB
{
    /// <summary>Provides the operation name and record identity shared by record-store lifecycle notifications.<br/></summary>
    public abstract class RecordStoreEventArgs : EventArgs
    {
        /// <summary>Initializes the operation and record identity shared by record lifecycle notifications.<br/></summary>
        /// <param name="operationName">Non-empty public operation name producing the notification.<br/></param>
        /// <param name="recordID">Record identity associated with the operation.<br/></param>
        protected RecordStoreEventArgs(string operationName, ulong recordID)
        {
            ArgumentException.ThrowIfNullOrEmpty(operationName);
            OperationName = operationName;
            RecordID = recordID;
        }

        /// <summary>Gets the public operation name that produced this lifecycle notification.<br/></summary>
        public string OperationName { get; }
        /// <summary>Gets the record identity associated with this lifecycle notification.<br/></summary>
        public ulong RecordID { get; }
    }

    /// <summary>Describes a record immediately before the requested CLR object is materialized.<br/></summary>
    public class RecordMaterializingEventArgs : RecordStoreEventArgs
    {
        /// <summary>Creates a notification for the requested target type before materialization.<br/></summary>
        /// <param name="operationName">The public operation producing this notification.<br/></param>
        /// <param name="recordID">The record identity associated with the operation.<br/></param>
        /// <param name="requestedType">The requested CLR hydration target type.<br/></param>
        public RecordMaterializingEventArgs(string operationName, ulong recordID, Type requestedType)
            : base(operationName, recordID)
        {
            RequestedType = requestedType ?? throw new ArgumentNullException(nameof(requestedType));
        }

        /// <summary>Gets the requested hydration target CLR type, which need not match the type originally serialized.<br/></summary>
        public Type RequestedType { get; }
    }

    /// <summary>Describes the requested CLR object after record materialization, including a null result when produced by the materializer.<br/></summary>
    public sealed class RecordMaterializedEventArgs : RecordMaterializingEventArgs
    {
        /// <summary>Creates a notification containing the materialization result.<br/></summary>
        /// <param name="operationName">The public operation producing this notification.<br/></param>
        /// <param name="recordID">The record identity associated with the operation.<br/></param>
        /// <param name="requestedType">The requested CLR hydration target type.<br/></param>
        /// <param name="record">The materialized result, which may be null.<br/></param>
        public RecordMaterializedEventArgs(string operationName, ulong recordID, Type requestedType, object? record)
            : base(operationName, recordID, requestedType)
        {
            Record = record;
        }

        /// <summary>Gets the materialized result, or null when the materializer produced a null result.<br/></summary>
        public object? Record { get; }
    }

    /// <summary>Provides the source object, optional State, and write-route context shared by record-persistence notifications.<br/></summary>
    public abstract class RecordPersistenceEventArgs : RecordStoreEventArgs
    {
        /// <summary>Initializes source-object and route context shared by persistence notifications.<br/></summary>
        /// <param name="operationName">Non-empty public operation name producing the notification.<br/></param>
        /// <param name="recordID">Record identity associated with the write.<br/></param>
        /// <param name="record">Non-null source object supplied for this write.<br/></param>
        /// <param name="state">Optional developer State supplied with the write.<br/></param>
        /// <param name="isNewRecord">Whether this write creates a new record identity.<br/></param>
        /// <param name="usesFastPath">Whether the notification identifies a fast-path write.<br/></param>
        protected RecordPersistenceEventArgs(
            string operationName,
            ulong recordID,
            object record,
            long? state,
            bool isNewRecord,
            bool usesFastPath)
            : base(operationName, recordID)
        {
            Record = record ?? throw new ArgumentNullException(nameof(record));
            RecordType = record.GetType();
            State = state;
            IsNewRecord = isNewRecord;
            UsesFastPath = usesFastPath;
        }

        /// <summary>Gets the source object supplied for this write; the reference is not a detached copy.<br/></summary>
        public object Record { get; }
        /// <summary>Gets the runtime CLR type of the source object supplied for this write.<br/></summary>
        public Type RecordType { get; }
        /// <summary>Gets the optional developer State value supplied with the write.<br/></summary>
        public long? State { get; }
        /// <summary>Gets whether this write is creating a new record identity rather than replacing an existing record.<br/></summary>
        public bool IsNewRecord { get; }
        /// <summary>Gets whether this notification identifies the write as using a fast-path operation.<br/></summary>
        public bool UsesFastPath { get; }
    }

    /// <summary>Describes a record before serialization and persistence; the notification does not indicate that the write has committed.<br/></summary>
    public sealed class RecordPersistingEventArgs : RecordPersistenceEventArgs
    {
        /// <summary>Creates a notification describing the source record before persistence.<br/></summary>
        /// <param name="operationName">The public operation producing this notification.<br/></param>
        /// <param name="recordID">The record identity associated with the operation.<br/></param>
        /// <param name="record">The non-null source record supplied for this write.<br/></param>
        /// <param name="state">Optional developer State supplied with the write.<br/></param>
        /// <param name="isNewRecord">Whether the write creates a new record identity.<br/></param>
        /// <param name="usesFastPath">Whether the notification identifies a fast-path write.<br/></param>
        public RecordPersistingEventArgs(string operationName, ulong recordID, object record, long? state, bool isNewRecord, bool usesFastPath)
            : base(operationName, recordID, record, state, isNewRecord, usesFastPath)
        {
        }
    }

    /// <summary>Describes a record after authoritative persistence and maintained-index convergence complete successfully.<br/></summary>
    public sealed class RecordPersistedEventArgs : RecordPersistenceEventArgs
    {
        /// <summary>Creates a notification describing the source record after successful persistence.<br/></summary>
        /// <param name="operationName">The public operation producing this notification.<br/></param>
        /// <param name="recordID">The record identity associated with the operation.<br/></param>
        /// <param name="record">The non-null source record supplied for this write.<br/></param>
        /// <param name="state">Optional developer State supplied with the write.<br/></param>
        /// <param name="isNewRecord">Whether the write creates a new record identity.<br/></param>
        /// <param name="usesFastPath">Whether the notification identifies a fast-path write.<br/></param>
        public RecordPersistedEventArgs(string operationName, ulong recordID, object record, long? state, bool isNewRecord, bool usesFastPath)
            : base(operationName, recordID, record, state, isNewRecord, usesFastPath)
        {
        }
    }

    /// <summary>Describes a record before deletion, including whether physical payload zero-writing was requested.<br/></summary>
    public class RecordDeletingEventArgs : RecordStoreEventArgs
    {
        /// <summary>Creates a notification describing the requested deletion.<br/></summary>
        /// <param name="operationName">The public operation producing this notification.<br/></param>
        /// <param name="recordID">The record identity associated with the operation.<br/></param>
        /// <param name="zeroWrite">Whether deletion requested overwriting discoverable physical payload copies with zeros.<br/></param>
        public RecordDeletingEventArgs(string operationName, ulong recordID, bool zeroWrite)
            : base(operationName, recordID)
        {
            ZeroWrite = zeroWrite;
        }

        /// <summary>Gets whether deletion requested overwriting discoverable physical payload copies with zeros; this is not a guarantee of device-level secure erasure.<br/></summary>
        public bool ZeroWrite { get; }
    }

    /// <summary>Describes a record after deletion and maintained-index convergence complete successfully.<br/></summary>
    public sealed class RecordDeletedEventArgs : RecordDeletingEventArgs
    {
        /// <summary>Creates a notification describing a completed deletion.<br/></summary>
        /// <param name="operationName">The public operation producing this notification.<br/></param>
        /// <param name="recordID">The record identity associated with the operation.<br/></param>
        /// <param name="zeroWrite">Whether deletion requested overwriting discoverable physical payload copies with zeros.<br/></param>
        public RecordDeletedEventArgs(string operationName, ulong recordID, bool zeroWrite)
            : base(operationName, recordID, zeroWrite)
        {
        }
    }

    /// <summary>Describes a condition-building lifecycle notification and retains the original condition instance.<br/></summary>
    public sealed class ConditionEventArgs : EventArgs
    {
        /// <summary>Creates a condition lifecycle notification retaining the supplied condition instance.<br/></summary>
        /// <param name="operationName">The public operation producing this notification.<br/></param>
        /// <param name="condition">The original condition instance involved in this execution.<br/></param>
        public ConditionEventArgs(string operationName, object condition)
        {
            ArgumentException.ThrowIfNullOrEmpty(operationName);
            OperationName = operationName;
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            ConditionType = condition.GetType();
        }

        /// <summary>Gets the public operation whose condition is being built.<br/></summary>
        public string OperationName { get; }
        /// <summary>Gets the original condition instance involved in the lifecycle notification.<br/></summary>
        public object Condition { get; }
        /// <summary>Gets the runtime CLR type of the condition instance, not the record entity type.<br/></summary>
        public Type ConditionType { get; }
    }

    /// <summary>Exposes one execution-time condition operand to the store's parameter-materialization hook; handlers may replace its value before evaluation.<br/></summary>
    public sealed class ConditionParameterEventArgs : EventArgs
    {
        /// <summary>Creates a mutable operand-value notification for one condition-materialization step.<br/></summary>
        /// <param name="operationName">The public operation producing this notification.<br/></param>
        /// <param name="condition">The original condition instance involved in this execution.<br/></param>
        /// <param name="conditionEntityType">The CLR record entity type against which the condition is authored.<br/></param>
        /// <param name="propertyPath">The effective operand property path, or null when none is associated.<br/></param>
        /// <param name="propertyPathOccurrence">Zero-based materialization occurrence for the effective path in this execution, or null when unavailable.<br/></param>
        /// <param name="parameterAlias">Optional named-parameter alias associated with the operand.<br/></param>
        /// <param name="value">The materialized operand value, which handlers may replace with a compatible value.<br/></param>
        /// <param name="recordBase">The owning RecordBase when available, otherwise null.<br/></param>
        public ConditionParameterEventArgs(
            string operationName,
            object condition,
            Type conditionEntityType,
            string? propertyPath,
            int? propertyPathOccurrence,
            string? parameterAlias,
            object? value,
            RecordBase? recordBase)
        {
            ArgumentException.ThrowIfNullOrEmpty(operationName);
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            ConditionEntityType = conditionEntityType ?? throw new ArgumentNullException(nameof(conditionEntityType));
            OperationName = operationName;
            PropertyPath = propertyPath;
            PropertyPathOccurrence = propertyPathOccurrence;
            ParameterAlias = parameterAlias;
            Value = value;
            RecordBase = recordBase;
        }

        /// <summary>Gets the public operation materializing the condition operand.<br/></summary>
        public string OperationName { get; }
        /// <summary>Gets the original condition instance whose operand is being materialized.<br/></summary>
        public object Condition { get; }
        /// <summary>Gets the CLR record entity type against which the condition is authored.<br/></summary>
        public Type ConditionEntityType { get; }
        /// <summary>Gets the effective operand property path, or null when the operand has no associated path.<br/></summary>
        public string? PropertyPath { get; }
        /// <summary>Gets the zero-based materialization occurrence for the effective property path in the current execution, or null when unavailable.<br/></summary>
        public int? PropertyPathOccurrence { get; }
        /// <summary>Gets the optional named-parameter alias associated with this operand.<br/></summary>
        public string? ParameterAlias { get; }
        /// <summary>Gets the owning RecordBase when a record-store execution context is available.<br/></summary>
        public RecordBase? RecordBase { get; }
        /// <summary>Gets or sets the operand value returned to the materialization pipeline; replacements must be compatible with the operation's expected value type.<br/></summary>
        public object? Value { get; set; }
    }

    /// <summary>
    /// Identifies the storage execution path selected for one completed Abraxas condition.<br/>
    /// Values describe planning only; no diagnostic callback is performed while records or LibraDex identities are traversed.<br/>
    /// </summary>
    public enum ConditionExecutionPlanKind
    {
        /// <summary>The complete condition tree executes through maintained indexes.<br/></summary>
        IndexDirect = 0,
        /// <summary>One safe index leaf reduces candidates and authoritative record payloads evaluate the complete residual condition.<br/></summary>
        IndexCandidateWithResidual = 1,
        /// <summary>Authoritative records are walked and their payloads evaluate the complete condition.<br/></summary>
        RecordScan = 2
    }

    /// <summary>
    /// Describes how one Abraxas condition leaf participated in the selected execution plan.<br/>
    /// The diagnostic captures planning-time identity and availability only and never stores a per-record callback.<br/>
    /// </summary>
    public sealed class ConditionExecutionLeafDiagnostic
    {
        /// <summary>
        /// Initializes one immutable leaf diagnostic.<br/>
        /// </summary>
        /// <param name="leafType">The concrete Abraxas condition leaf type.<br/></param>
        /// <param name="indexName">The resolved direct or maintained-projection index name, or null when none resolved.<br/></param>
        /// <param name="indexAvailable">Whether the resolved maintained index is available for this store.<br/></param>
        /// <param name="usedByIndex">Whether this leaf participates in the selected direct-index or candidate-index condition.<br/></param>
        /// <param name="reason">A concise explanation of the leaf's planning disposition.<br/></param>
        public ConditionExecutionLeafDiagnostic(
            Type leafType,
            string? indexName,
            bool indexAvailable,
            bool usedByIndex,
            string reason)
        {
            LeafType = leafType ?? throw new ArgumentNullException(nameof(leafType));
            IndexName = indexName;
            IndexAvailable = indexAvailable;
            UsedByIndex = usedByIndex;
            Reason = string.IsNullOrWhiteSpace(reason)
                ? throw new ArgumentException("A planning disposition reason is required.", nameof(reason))
                : reason;
        }

        /// <summary>Gets the concrete Abraxas condition leaf CLR type.<br/></summary>
        public Type LeafType { get; }
        /// <summary>Gets the resolved direct or maintained-projection index name, or null when none was resolved.<br/></summary>
        public string? IndexName { get; }
        /// <summary>Gets whether the resolved maintained index was available to this store when the plan was selected.<br/></summary>
        public bool IndexAvailable { get; }
        /// <summary>Gets whether the selected plan uses this leaf through a maintained index, rather than only residual record evaluation.<br/></summary>
        public bool UsedByIndex { get; }
        /// <summary>Gets the planning-time explanation of this leaf's index or residual-evaluation disposition.<br/></summary>
        public string Reason { get; }
    }

    /// <summary>
    /// Reports the one execution plan selected for a completed Abraxas condition.<br/>
    /// The payload is created only when <c>ConditionExecutionPlanned</c> has a subscriber and is raised once before result traversal begins.<br/>
    /// </summary>
    public sealed class ConditionExecutionPlanEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes one immutable condition-plan diagnostic payload.<br/>
        /// </summary>
        /// <param name="operationName">The public RecordStore operation being planned.<br/></param>
        /// <param name="condition">The completed Abraxas condition supplied by the developer.<br/></param>
        /// <param name="planKind">The selected storage execution path.<br/></param>
        /// <param name="indexNames">Distinct maintained index names used by the selected plan.<br/></param>
        /// <param name="leaves">Per-leaf planning dispositions.<br/></param>
        /// <param name="reason">A concise plan-level selection reason.<br/></param>
        public ConditionExecutionPlanEventArgs(
            string operationName,
            object condition,
            ConditionExecutionPlanKind planKind,
            IReadOnlyList<string> indexNames,
            IReadOnlyList<ConditionExecutionLeafDiagnostic> leaves,
            string reason)
        {
            ArgumentException.ThrowIfNullOrEmpty(operationName);
            OperationName = operationName;
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            ConditionType = condition.GetType();
            PlanKind = planKind;
            IndexNames = indexNames ?? throw new ArgumentNullException(nameof(indexNames));
            Leaves = leaves ?? throw new ArgumentNullException(nameof(leaves));
            Reason = string.IsNullOrWhiteSpace(reason)
                ? throw new ArgumentException("A plan selection reason is required.", nameof(reason))
                : reason;
        }

        /// <summary>Gets the public record-store operation whose condition was planned.<br/></summary>
        public string OperationName { get; }
        /// <summary>Gets the original completed condition instance supplied for execution.<br/></summary>
        public object Condition { get; }
        /// <summary>Gets the runtime CLR type of the completed condition instance.<br/></summary>
        public Type ConditionType { get; }
        /// <summary>Gets the selected complete-index, candidate-with-residual, or record-scan execution path.<br/></summary>
        public ConditionExecutionPlanKind PlanKind { get; }
        /// <summary>Gets the distinct maintained index names used by the selected plan.<br/></summary>
        public IReadOnlyList<string> IndexNames { get; }
        /// <summary>Gets the planning-time dispositions of the condition leaves.<br/></summary>
        public IReadOnlyList<ConditionExecutionLeafDiagnostic> Leaves { get; }
        /// <summary>Gets the plan-level explanation of the selected execution path.<br/></summary>
        public string Reason { get; }
    }

    /// <summary>
    /// Identifies the stable Abraxas-level reason that an index definition or indexed value was rejected.<br/>
    /// The values intentionally describe developer-facing contracts rather than LibraDex storage shapes or exception types.<br/>
    /// </summary>
    public enum IndexContractFailureKind
    {
        /// <summary>A non-null key conflicts with another record in a unique index.<br/></summary>
        UniqueConstraint = 0,

        /// <summary>A requested index definition conflicts with the already persisted definition.<br/></summary>
        Configuration = 1,

        /// <summary>An indexed value could not be read from its authoritative Inheto payload.<br/></summary>
        KeyRead = 2,

        /// <summary>A derived index projection could not produce its deterministic key.<br/></summary>
        Projection = 3,

        /// <summary>A binary key exceeded the explicit maximum payload length.<br/></summary>
        BinaryKeyTooLarge = 4,

        /// <summary>An application-provided binary codec failed or violated its fixed-width contract.<br/></summary>
        Codec = 5,

        /// <summary>The ordered backend rejected a tuple after Abraxas validation completed.<br/></summary>
        BackendMutation = 6,

        /// <summary>A persisted application-codec identity, version, or shape is missing, incomplete, or different from runtime registration.<br/></summary>
        CodecIdentity = 7,

        /// <summary>The selected ordered index may expose the same identity under more than one key.<br/></summary>
        IdentityMultiplicity = 8
    }

    /// <summary>
    /// Reports a stable Abraxas index-contract failure without requiring developers to understand the active ordered engine's exception vocabulary.<br/>
    /// Argument validation still uses the standard argument exception family; this type covers failures tied to an accepted index definition or indexed record value.<br/>
    /// </summary>
    public sealed class IndexContractException : InvalidOperationException
    {
        /// <summary>
        /// Creates one immutable Abraxas index-contract failure.<br/>
        /// </summary>
        /// <param name="failureKind">The machine-readable developer-facing reason for the failure.<br/></param>
        /// <param name="message">The stable explanatory message presented to the developer.<br/></param>
        /// <param name="indexName">The logical or physical Abraxas-owned index name when available.<br/></param>
        /// <param name="keyPath">The indexed property path when available.<br/></param>
        /// <param name="recordID">The affected Abraxas record identity when available.<br/></param>
        /// <param name="innerException">The engine, codec, or projection exception retained for diagnostics when one exists.<br/></param>
        public IndexContractException(
            IndexContractFailureKind failureKind,
            string message,
            string? indexName = null,
            string? keyPath = null,
            ulong? recordID = null,
            Exception? innerException = null)
            : base(message, innerException)
        {
            FailureKind = failureKind;
            IndexName = indexName;
            KeyPath = keyPath;
            RecordID = recordID;
        }

        /// <summary>Gets the stable developer-facing reason for the failure.<br/></summary>
        public IndexContractFailureKind FailureKind { get; }

        /// <summary>Gets the affected logical or physical index name when known.<br/></summary>
        public string? IndexName { get; }

        /// <summary>Gets the affected property path when known.<br/></summary>
        public string? KeyPath { get; }

        /// <summary>Gets the affected record identity when known.<br/></summary>
        public ulong? RecordID { get; }
    }

    /// <summary>
    /// Reports that Abraxas could not restore LibraDex convergence after one automatic repair attempt.<br/>
    /// This is not a transaction or rollback signal: Fractal remains the authoritative record reality, while the applied-USN checkpoint and any retained State intent keep later reopen or mutation recovery deterministic.<br/>
    /// </summary>
    public sealed class IndexConvergenceException : InvalidOperationException
    {
        /// <summary>
        /// Creates one immutable convergence failure containing both source progress and partial-completion facts.<br/>
        /// </summary>
        /// <param name="operationName">Abraxas operation that required convergence.<br/></param>
        /// <param name="sourceUsn">Current authoritative Fractal store USN.<br/></param>
        /// <param name="appliedSourceUsn">Last proven LibraDex source USN, or <c>null</c> when no checkpoint was established.<br/></param>
        /// <param name="recordMutationCompleted">Whether the requested operation had already changed authoritative record storage before repair failed.<br/></param>
        /// <param name="pendingStateIntentRetained">Whether a replayable developer State intent remains durably retained.<br/></param>
        /// <param name="recordID">Affected record identity when applicable.<br/></param>
        /// <param name="innerException">Original mutation and/or repair failure retained for diagnostics.<br/></param>
        public IndexConvergenceException(
            string operationName,
            ulong sourceUsn,
            ulong? appliedSourceUsn,
            bool recordMutationCompleted,
            bool pendingStateIntentRetained,
            ulong? recordID = null,
            Exception? innerException = null)
            : base(
                $"Operation '{operationName}' could not restore its derived indexes after one automatic repair attempt. " +
                $"RecordMutationCompleted={recordMutationCompleted}; SourceUSN={sourceUsn}; " +
                $"AppliedSourceUSN={(appliedSourceUsn.HasValue ? appliedSourceUsn.Value.ToString() : "none")}; " +
                $"PendingStateIntentRetained={pendingStateIntentRetained}; " +
                $"RecordID={(recordID.HasValue ? recordID.Value.ToString() : "none")}.",
                innerException)
        {
            OperationName = operationName;
            SourceUsn = sourceUsn;
            AppliedSourceUsn = appliedSourceUsn;
            RecordMutationCompleted = recordMutationCompleted;
            PendingStateIntentRetained = pendingStateIntentRetained;
            RecordID = recordID;
        }

        /// <summary>Gets the Abraxas operation that encountered the convergence failure.<br/></summary>
        public string OperationName { get; }

        /// <summary>Gets the current authoritative Fractal store USN observed by the failed repair.<br/></summary>
        public ulong SourceUsn { get; }

        /// <summary>Gets the last proven LibraDex source USN, or <c>null</c> when no checkpoint existed.<br/></summary>
        public ulong? AppliedSourceUsn { get; }

        /// <summary>Gets whether authoritative record storage had already changed for the requested operation before index convergence failed.<br/></summary>
        public bool RecordMutationCompleted { get; }

        /// <summary>Gets whether the required single automatic convergence attempt was performed.<br/></summary>
        public bool RepairAttempted => true;

        /// <summary>Gets whether a replayable developer State intent remains durably retained.<br/></summary>
        public bool PendingStateIntentRetained { get; }

        /// <summary>Gets the affected record identity when the operation targeted one record.<br/></summary>
        public ulong? RecordID { get; }
    }

    /// <summary>
    /// Identifies the Abraxas-level execution route selected for one property-ordered identity request.<br/>
    /// The names describe unified store behavior rather than exposing physical LibraDex cursor shapes.<br/>
    /// </summary>
    public enum OrderingExecutionPlanKind
    {
        /// <summary>A complete mutation-converged index supplies the complete store order without Fractal identity revalidation.<br/></summary>
        IndexDirectCompleteStore = 0,

        /// <summary>The request retains arbitrary-candidate validation and authoritative Fractal/Inheto fallback behavior.<br/></summary>
        DefensiveCandidateOrdering = 1,

        /// <summary>A complete mutation-converged condition primitive already supplies the requested target-index order without candidate validation or a second index traversal.<br/></summary>
        IndexDirectConditionOrder = 2
    }

    /// <summary>
    /// Reports the unified Abraxas execution route selected for one property-ordered identity request.<br/>
    /// The payload is created only when <c>OrderingExecutionPlanned</c> has a subscriber.<br/>
    /// </summary>
    public sealed class OrderingExecutionPlanEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes one immutable ordering-plan diagnostic payload.<br/>
        /// </summary>
        /// <param name="operationName">Public RecordStore operation being planned.<br/></param>
        /// <param name="planKind">Selected unified storage route.<br/></param>
        /// <param name="propertyPaths">Requested property paths or calculated-selector labels in lexicographic priority order.<br/></param>
        /// <param name="completeStorePopulation">Whether the request represents the complete logical store population.<br/></param>
        /// <param name="reason">Concise explanation of the selected route.<br/></param>
        public OrderingExecutionPlanEventArgs(
            string operationName,
            OrderingExecutionPlanKind planKind,
            IReadOnlyList<string> propertyPaths,
            bool completeStorePopulation,
            string reason)
        {
            ArgumentException.ThrowIfNullOrEmpty(operationName);
            OperationName = operationName;
            PlanKind = planKind;
            PropertyPaths = propertyPaths ?? throw new ArgumentNullException(nameof(propertyPaths));
            CompleteStorePopulation = completeStorePopulation;
            Reason = string.IsNullOrWhiteSpace(reason)
                ? throw new ArgumentException("An ordering plan selection reason is required.", nameof(reason))
                : reason;
        }

        /// <summary>Gets the public RecordStore operation that selected the route.<br/></summary>
        public string OperationName { get; }

        /// <summary>Gets the selected unified storage route.<br/></summary>
        public OrderingExecutionPlanKind PlanKind { get; }

        /// <summary>Gets the ordered property paths or calculated-selector labels in lexicographic priority order.<br/></summary>
        public IReadOnlyList<string> PropertyPaths { get; }

        /// <summary>Gets whether the request represents the complete logical store population.<br/></summary>
        public bool CompleteStorePopulation { get; }

        /// <summary>Gets the concise route-selection explanation.<br/></summary>
        public string Reason { get; }
    }

    /// <summary>
    /// Identifies the concrete work route selected after FastFind's ordinary Abraxas condition planner has chosen a semantic plan.<br/>
    /// These values describe execution mechanics only; condition meaning and index trust remain governed by <see cref="ConditionExecutionPlanKind"/>.<br/>
    /// </summary>
    public enum FastFindExecutionRoute
    {
        /// <summary>A complete maintained LibraDex cursor supplies authoritative identities directly; Fractal payload reads occur only when the requested FastFind projection requires stored objects.<br/></summary>
        IndexDirectCursor = 0,

        /// <summary>LibraDex supplies a reduced candidate population whose complete residual semantics are evaluated through Fractal point reads.<br/></summary>
        IndexCandidateResidual = 1,

        /// <summary>An unavoidable Fractal scan uses the natural identity walk and synchronous payload evaluation.<br/></summary>
        RecordNaturalSequential = 2,

        /// <summary>An unavoidable Fractal scan uses one shared physical snapshot divided into disjoint byte-balanced pooled-window readers.<br/></summary>
        RecordPhysicalPartitions = 3,

        /// <summary>An unavoidable Fractal scan uses one sequential pooled-block producer feeding exact unordered evaluation workers.<br/></summary>
        RecordPooledBlockProducer = 4
    }

    internal enum FastFindFractalParallelStrategy
    {
        PhysicalPartitions = 0,
        PooledBlockProducer = 1
    }

    /// <summary>
    /// Describes the physical sequence observed while FastFind emitted results; it does not promise a stable or repeatable order.<br/>
    /// Every FastFind public operation is unordered by contract, including routes that happen to traverse one deterministic source cursor.<br/>
    /// </summary>
    public enum FastFindResultOrder
    {
        /// <summary>The selected source cursor or sequential Fractal reader supplied the observed sequence; callers must still treat that sequence as unordered.<br/></summary>
        SourceOrder = 0,

        /// <summary>Concurrent workers published matches by completion; callers must treat the sequence as unordered.<br/></summary>
        WorkerCompletionOrder = 1
    }

    /// <summary>
    /// Reports bounded work completed by one exact FastFind worker.<br/>
    /// The diagnostic contains counts and durations only; it never retains source keys, identities, record payloads, or worker task objects.<br/>
    /// </summary>
    public sealed class FastFindWorkerExecution
    {
        internal FastFindWorkerExecution(
            int workerIndex,
            int managedThreadId,
            int workItemsClaimed,
            long sourceItemsExamined,
            long matchesPublished,
            TimeSpan duration,
            TimeSpan sourceWaitDuration,
            long sourceWaitCount,
            TimeSpan publicationWaitDuration,
            long publicationWaitCount,
            long allocatedBytes)
        {
            WorkerIndex = workerIndex;
            ManagedThreadId = managedThreadId;
            WorkItemsClaimed = workItemsClaimed;
            SourceItemsExamined = sourceItemsExamined;
            MatchesPublished = matchesPublished;
            Duration = duration;
            SourceWaitDuration = sourceWaitDuration;
            SourceWaitCount = sourceWaitCount;
            PublicationWaitDuration = publicationWaitDuration;
            PublicationWaitCount = publicationWaitCount;
            AllocatedBytes = allocatedBytes;
        }

        /// <summary>Gets the zero-based stable worker ordinal assigned by the coordinator.<br/></summary>
        public int WorkerIndex { get; }
        /// <summary>Gets the managed thread identifier observed when this physical worker began.<br/></summary>
        public int ManagedThreadId { get; }
        /// <summary>Gets the number of source work items claimed by this worker; work units depend on the execution route and are not necessarily individual records.<br/></summary>
        public int WorkItemsClaimed { get; }
        /// <summary>Gets the number of source identities or physical records examined by this worker.<br/></summary>
        public long SourceItemsExamined { get; }
        /// <summary>Gets the number of matches this worker published to the result channel.<br/></summary>
        public long MatchesPublished { get; }
        /// <summary>Gets the worker's measured lifetime through completion, including any source and publication waits.<br/></summary>
        public TimeSpan Duration { get; }
        /// <summary>Gets time spent awaiting source work from a bounded producer channel.<br/></summary>
        public TimeSpan SourceWaitDuration { get; }
        /// <summary>Gets the number of incomplete source-channel waits observed by this worker.<br/></summary>
        public long SourceWaitCount { get; }
        /// <summary>Gets time spent awaiting capacity in the bounded result-publication channel.<br/></summary>
        public TimeSpan PublicationWaitDuration { get; }
        /// <summary>Gets the number of incomplete result-publication waits observed by this worker.<br/></summary>
        public long PublicationWaitCount { get; }
        /// <summary>Gets managed bytes allocated on this worker thread during its measured lifetime; this is not retained memory or whole-operation allocation.<br/></summary>
        public long AllocatedBytes { get; }
    }

    /// <summary>
    /// Reports measured work for one consumed FastFind operation after its iterator completes or is abandoned.<br/>
    /// The event is opt-in; no diagnostic state, counters, or immutable payload is allocated when <c>FastFindExecutionCompleted</c> has no subscriber.<br/>
    /// </summary>
    public sealed class FastFindExecutionEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes one immutable FastFind execution diagnostic.<br/>
        /// </summary>
        /// <param name="operationName">Public FastFind operation that executed.<br/></param>
        /// <param name="planKind">Semantic condition plan selected by Abraxas.<br/></param>
        /// <param name="route">Concrete work route used after planning.<br/></param>
        /// <param name="resultOrder">Observed physical emission sequence; never an ordering guarantee.<br/></param>
        /// <param name="requestedWorkers">Exact worker count selected by the FastFind call and configuration contract.<br/></param>
        /// <param name="actualWorkers">Actual physical workers; successful FastFind execution requires this to equal <paramref name="requestedWorkers"/>.<br/></param>
        /// <param name="partitionCount">Number of disjoint physical partitions, or zero when the route is not partitioned.<br/></param>
        /// <param name="sourceIdentities">Identities emitted by a direct or candidate source, or physical records examined by a Fractal scan.<br/></param>
        /// <param name="payloadReads">Fractal payloads read for residual or complete semantic evaluation.<br/></param>
        /// <param name="matches">Matches published to the FastFind consumer.<br/></param>
        /// <param name="planningDuration">Condition materialization and plan-selection duration before traversal.<br/></param>
        /// <param name="sourcePreparationDuration">Physical snapshot/partition preparation duration, or zero for cursor and point-read routes.<br/></param>
        /// <param name="executionDuration">Traversal, evaluation, and result-publication duration after planning.<br/></param>
        /// <param name="totalDuration">Complete measured FastFind iterator lifetime.<br/></param>
        /// <param name="completed">Whether the consumer drained the iterator successfully rather than stopping early or observing a failure.<br/></param>
        public FastFindExecutionEventArgs(
            string operationName,
            ConditionExecutionPlanKind planKind,
            FastFindExecutionRoute route,
            FastFindResultOrder resultOrder,
            int requestedWorkers,
            int actualWorkers,
            int partitionCount,
            long sourceIdentities,
            long payloadReads,
            long matches,
            TimeSpan planningDuration,
            TimeSpan sourcePreparationDuration,
            TimeSpan executionDuration,
            TimeSpan totalDuration,
            bool completed)
            : this(
                operationName,
                planKind,
                route,
                resultOrder,
                requestedWorkers,
                actualWorkers,
                partitionCount,
                sourceIdentities,
                payloadReads,
                matches,
                planningDuration,
                semanticPlanningDuration: planningDuration,
                topologyPartitioningDuration: TimeSpan.Zero,
                workerPreparationDuration: TimeSpan.Zero,
                sourcePreparationDuration,
                executionDuration,
                totalDuration,
                topologyRoutersRead: 0,
                topologyTargetsSelected: 0,
                timeToFirstResult: TimeSpan.Zero,
                timeToFirst10Results: TimeSpan.Zero,
                timeToFirst100Results: TimeSpan.Zero,
                timeToFirst1000Results: TimeSpan.Zero,
                workers: Array.Empty<FastFindWorkerExecution>(),
                completed)
        {
        }

        /// <summary>
        /// Initializes one immutable FastFind diagnostic with separated semantic, topology, and worker-cursor preparation costs while retaining the original topology-aware constructor contract.<br/>
        /// Result-latency milestones and per-worker summaries were added later and therefore default to empty values for callers compiled against this shape.<br/>
        /// </summary>
        /// <param name="operationName">Public FastFind operation that executed.<br/></param>
        /// <param name="planKind">Semantic condition plan selected by Abraxas.<br/></param>
        /// <param name="route">Concrete work route used after planning.<br/></param>
        /// <param name="resultOrder">Observed physical emission sequence; never an ordering guarantee.<br/></param>
        /// <param name="requestedWorkers">Exact worker count selected by the FastFind call.<br/></param>
        /// <param name="actualWorkers">Observed physical workers.<br/></param>
        /// <param name="partitionCount">Disjoint worker partition count.<br/></param>
        /// <param name="sourceIdentities">Source identities emitted or physical records examined.<br/></param>
        /// <param name="payloadReads">Fractal payload reads required by evaluation or projection.<br/></param>
        /// <param name="matches">Matches emitted to the consumer.<br/></param>
        /// <param name="planningDuration">Complete semantic plus physical plan-selection duration before traversal.<br/></param>
        /// <param name="semanticPlanningDuration">Condition binding, materialization, and route-selection duration before topology splitting.<br/></param>
        /// <param name="topologyPartitioningDuration">Time spent deriving direct-index physical partitions.<br/></param>
        /// <param name="workerPreparationDuration">Time from worker creation through every worker cursor reaching its first stable position.<br/></param>
        /// <param name="sourcePreparationDuration">Fractal snapshot/partition preparation duration when applicable.<br/></param>
        /// <param name="executionDuration">Traversal, evaluation, and publication duration after plan selection.<br/></param>
        /// <param name="totalDuration">Complete measured iterator lifetime.<br/></param>
        /// <param name="topologyRoutersRead">Router pages read to derive a direct-index frontier.<br/></param>
        /// <param name="topologyTargetsSelected">Disjoint continuation targets distributed across direct-index workers.<br/></param>
        /// <param name="completed">Whether the iterator drained successfully.<br/></param>
        public FastFindExecutionEventArgs(
            string operationName,
            ConditionExecutionPlanKind planKind,
            FastFindExecutionRoute route,
            FastFindResultOrder resultOrder,
            int requestedWorkers,
            int actualWorkers,
            int partitionCount,
            long sourceIdentities,
            long payloadReads,
            long matches,
            TimeSpan planningDuration,
            TimeSpan semanticPlanningDuration,
            TimeSpan topologyPartitioningDuration,
            TimeSpan workerPreparationDuration,
            TimeSpan sourcePreparationDuration,
            TimeSpan executionDuration,
            TimeSpan totalDuration,
            int topologyRoutersRead,
            int topologyTargetsSelected,
            bool completed)
            : this(
                operationName,
                planKind,
                route,
                resultOrder,
                requestedWorkers,
                actualWorkers,
                partitionCount,
                sourceIdentities,
                payloadReads,
                matches,
                planningDuration,
                semanticPlanningDuration,
                topologyPartitioningDuration,
                workerPreparationDuration,
                sourcePreparationDuration,
                executionDuration,
                totalDuration,
                topologyRoutersRead,
                topologyTargetsSelected,
                timeToFirstResult: TimeSpan.Zero,
                timeToFirst10Results: TimeSpan.Zero,
                timeToFirst100Results: TimeSpan.Zero,
                timeToFirst1000Results: TimeSpan.Zero,
                workers: Array.Empty<FastFindWorkerExecution>(),
                completed)
        {
        }

        /// <summary>
        /// Initializes one immutable FastFind diagnostic with separated semantic, topology, and worker-cursor preparation costs.<br/>
        /// The original constructor remains source-compatible and maps its undivided planning value to semantic planning.<br/>
        /// </summary>
        /// <param name="operationName">Public FastFind operation that executed.<br/></param>
        /// <param name="planKind">Semantic condition plan selected by Abraxas.<br/></param>
        /// <param name="route">Concrete work route used after planning.<br/></param>
        /// <param name="resultOrder">Observed physical emission sequence; never an ordering guarantee.<br/></param>
        /// <param name="requestedWorkers">Exact worker count selected by the FastFind call.<br/></param>
        /// <param name="actualWorkers">Observed physical workers.<br/></param>
        /// <param name="partitionCount">Disjoint worker partition count.<br/></param>
        /// <param name="sourceIdentities">Source identities emitted or physical records examined.<br/></param>
        /// <param name="payloadReads">Fractal payload reads required by evaluation or projection.<br/></param>
        /// <param name="matches">Matches emitted to the consumer.<br/></param>
        /// <param name="planningDuration">Complete semantic plus physical plan-selection duration before traversal.<br/></param>
        /// <param name="semanticPlanningDuration">Condition binding, materialization, and route-selection duration before topology splitting.<br/></param>
        /// <param name="topologyPartitioningDuration">Time spent deriving direct-index physical partitions.<br/></param>
        /// <param name="workerPreparationDuration">Time from worker creation through every worker cursor reaching its first stable position.<br/></param>
        /// <param name="sourcePreparationDuration">Fractal snapshot/partition preparation duration when applicable.<br/></param>
        /// <param name="executionDuration">Traversal, evaluation, and publication duration after plan selection.<br/></param>
        /// <param name="totalDuration">Complete measured iterator lifetime.<br/></param>
        /// <param name="topologyRoutersRead">Router pages read to derive a direct-index frontier.<br/></param>
        /// <param name="topologyTargetsSelected">Disjoint continuation targets distributed across direct-index workers.<br/></param>
        /// <param name="timeToFirstResult">Elapsed operation time before the consumer observed its first result, or zero when fewer than one result was produced.<br/></param>
        /// <param name="timeToFirst10Results">Elapsed operation time before the consumer observed ten results, or zero when fewer than ten results were produced.<br/></param>
        /// <param name="timeToFirst100Results">Elapsed operation time before the consumer observed one hundred results, or zero when fewer than one hundred results were produced.<br/></param>
        /// <param name="timeToFirst1000Results">Elapsed operation time before the consumer observed one thousand results, or zero when fewer than one thousand results were produced.<br/></param>
        /// <param name="workers">Immutable bounded summaries for every exact physical worker that participated.<br/></param>
        /// <param name="completed">Whether the iterator drained successfully.<br/></param>
        public FastFindExecutionEventArgs(
            string operationName,
            ConditionExecutionPlanKind planKind,
            FastFindExecutionRoute route,
            FastFindResultOrder resultOrder,
            int requestedWorkers,
            int actualWorkers,
            int partitionCount,
            long sourceIdentities,
            long payloadReads,
            long matches,
            TimeSpan planningDuration,
            TimeSpan semanticPlanningDuration,
            TimeSpan topologyPartitioningDuration,
            TimeSpan workerPreparationDuration,
            TimeSpan sourcePreparationDuration,
            TimeSpan executionDuration,
            TimeSpan totalDuration,
            int topologyRoutersRead,
            int topologyTargetsSelected,
            TimeSpan timeToFirstResult,
            TimeSpan timeToFirst10Results,
            TimeSpan timeToFirst100Results,
            TimeSpan timeToFirst1000Results,
            IReadOnlyList<FastFindWorkerExecution> workers,
            bool completed)
        {
            ArgumentException.ThrowIfNullOrEmpty(operationName);
            OperationName = operationName;
            PlanKind = planKind;
            Route = route;
            ResultOrder = resultOrder;
            RequestedWorkers = requestedWorkers;
            ActualWorkers = actualWorkers;
            PartitionCount = partitionCount;
            SourceIdentities = sourceIdentities;
            PayloadReads = payloadReads;
            Matches = matches;
            PlanningDuration = planningDuration;
            SemanticPlanningDuration = semanticPlanningDuration;
            TopologyPartitioningDuration = topologyPartitioningDuration;
            WorkerPreparationDuration = workerPreparationDuration;
            SourcePreparationDuration = sourcePreparationDuration;
            ExecutionDuration = executionDuration;
            TotalDuration = totalDuration;
            TopologyRoutersRead = Math.Max(0, topologyRoutersRead);
            TopologyTargetsSelected = Math.Max(0, topologyTargetsSelected);
            TimeToFirstResult = timeToFirstResult;
            TimeToFirst10Results = timeToFirst10Results;
            TimeToFirst100Results = timeToFirst100Results;
            TimeToFirst1000Results = timeToFirst1000Results;
            Workers = workers ?? throw new ArgumentNullException(nameof(workers));
            Completed = completed;
        }

        /// <summary>Gets the public FastFind operation whose consumed iterator was measured.<br/></summary>
        public string OperationName { get; }
        /// <summary>Gets the semantic condition plan selected for this execution.<br/></summary>
        public ConditionExecutionPlanKind PlanKind { get; }
        /// <summary>Gets the concrete cursor, candidate, or physical-record route used after planning.<br/></summary>
        public FastFindExecutionRoute Route { get; }
        /// <summary>Gets the observed physical emission sequence for diagnostics; FastFind remains unordered for every value.<br/></summary>
        public FastFindResultOrder ResultOrder { get; }
        /// <summary>Gets the exact worker count requested by the call and its configuration contract, not merely a concurrency ceiling.<br/></summary>
        public int RequestedWorkers { get; }
        /// <summary>Gets the observed physical worker count; successful complete execution requires the requested count, while abandoned or failed execution may report fewer workers.<br/></summary>
        public int ActualWorkers { get; }
        /// <summary>Gets the number of physical partitions recorded for the route, or zero when no partition set was used.<br/></summary>
        public int PartitionCount { get; }
        /// <summary>Gets source identities emitted by an index route or physical records examined by a record-scan route.<br/></summary>
        public long SourceIdentities { get; }
        /// <summary>Gets recorded authoritative payload reads required for semantic evaluation or result projection.<br/></summary>
        public long PayloadReads { get; }
        /// <summary>Gets the number of matches emitted to the consuming iterator before completion or abandonment.<br/></summary>
        public long Matches { get; }
        /// <summary>Gets the measured plan-selection duration before traversal; the detailed planning durations provide additional phase observations and should not be blindly added to this value.<br/></summary>
        public TimeSpan PlanningDuration { get; }
        /// <summary>Gets the measured condition binding, operand materialization, and semantic route-selection duration before topology splitting.<br/></summary>
        public TimeSpan SemanticPlanningDuration { get; }
        /// <summary>Gets the measured direct-index topology-partition preparation duration, or zero when that preparation was not recorded.<br/></summary>
        public TimeSpan TopologyPartitioningDuration { get; }
        /// <summary>Gets the measured worker-startup duration through the point where every worker cursor is ready, or zero when that phase was not recorded.<br/></summary>
        public TimeSpan WorkerPreparationDuration { get; }
        /// <summary>Gets the measured physical record snapshot and partition-preparation duration, or zero when that preparation was not recorded.<br/></summary>
        public TimeSpan SourcePreparationDuration { get; }
        /// <summary>Gets the measured iterator lifetime after the recorded planning boundary, including traversal, publication, and consumer pacing.<br/></summary>
        public TimeSpan ExecutionDuration { get; }
        /// <summary>Gets the complete measured iterator lifetime, not just storage CPU time; it includes time while the consumer holds an active iteration.<br/></summary>
        public TimeSpan TotalDuration { get; }
        /// <summary>Gets the number of router pages read to derive direct-index partitions, or zero when none were recorded.<br/></summary>
        public int TopologyRoutersRead { get; }
        /// <summary>Gets the number of disjoint continuation targets selected for direct-index workers, or zero when none were recorded.<br/></summary>
        public int TopologyTargetsSelected { get; }
        /// <summary>Gets elapsed operation time when the consumer observed the first result, or zero when that milestone was not recorded.<br/></summary>
        public TimeSpan TimeToFirstResult { get; }
        /// <summary>Gets elapsed operation time when the consumer observed ten results, or zero when that milestone was not recorded.<br/></summary>
        public TimeSpan TimeToFirst10Results { get; }
        /// <summary>Gets elapsed operation time when the consumer observed one hundred results, or zero when that milestone was not recorded.<br/></summary>
        public TimeSpan TimeToFirst100Results { get; }
        /// <summary>Gets elapsed operation time when the consumer observed one thousand results, or zero when that milestone was not recorded.<br/></summary>
        public TimeSpan TimeToFirst1000Results { get; }
        /// <summary>Gets the bounded per-worker summaries recorded at worker completion; compatibility constructors supply an empty list.<br/></summary>
        public IReadOnlyList<FastFindWorkerExecution> Workers { get; }
        /// <summary>Gets whether the consumer drained the iterator successfully; false also covers early disposal, cancellation, and failure.<br/></summary>
        public bool Completed { get; }
    }

    /// <summary>
    /// Collects opt-in FastFind counters without placing mutable diagnostic state on a reusable RecordStore facade.<br/>
    /// One observer belongs to one consumed iterator, and worker updates use atomic counters because physical partitions and candidate evaluators may complete concurrently.<br/>
    /// </summary>
    internal sealed class FastFindExecutionObserver
    {
        private readonly long started = Stopwatch.GetTimestamp();
        private long planningTicks;
        private long semanticPlanningTicks;
        private long topologyPartitioningTicks;
        private long workerPreparationTicks;
        private long sourcePreparationTicks;
        private long sourceIdentities;
        private long payloadReads;
        private long matches;
        private long firstResultTicks;
        private long first10ResultTicks;
        private long first100ResultTicks;
        private long first1000ResultTicks;
        private readonly ConcurrentDictionary<int, byte> workerThreadIds = new();
        private readonly ConcurrentQueue<FastFindWorkerExecution> workers = new();

        /// <summary>
        /// Creates one operation-scoped observer after an event subscriber has requested diagnostics.<br/>
        /// </summary>
        /// <param name="operationName">Public FastFind operation being consumed.<br/></param>
        /// <param name="requestedWorkers">Exact physical worker count required before the route is selected.<br/></param>
        internal FastFindExecutionObserver(string operationName, int requestedWorkers)
        {
            OperationName = string.IsNullOrWhiteSpace(operationName)
                ? throw new ArgumentException("A FastFind operation name is required.", nameof(operationName))
                : operationName;
            RequestedWorkers = requestedWorkers;
        }

        internal string OperationName { get; }
        internal int RequestedWorkers { get; }
        internal ConditionExecutionPlanKind PlanKind { get; private set; }
        internal FastFindExecutionRoute Route { get; private set; }
        internal FastFindResultOrder ResultOrder { get; private set; }
        internal int ActualWorkers { get; private set; } = 1;
        internal int PartitionCount { get; private set; }
        internal int TopologyRoutersRead { get; private set; }
        internal int TopologyTargetsSelected { get; private set; }

        /// <summary>
        /// Freezes the elapsed condition-binding and semantic route-selection work before any direct-index topology split begins.<br/>
        /// Repeated calls are harmless so all plan branches can mark the boundary without maintaining route-specific bookkeeping.<br/>
        /// </summary>
        internal void SetSemanticPlanningComplete()
        {
            long elapsed = Stopwatch.GetTimestamp() - started;
            _ = Interlocked.CompareExchange(ref semanticPlanningTicks, Math.Max(0, elapsed), 0);
        }

        /// <summary>
        /// Records the direct-index topology split independently from semantic planning.<br/>
        /// Router/target counts prove whether the planner used a bounded frontier or performed an unexpectedly broad physical walk.<br/>
        /// </summary>
        /// <param name="elapsedTicks">Stopwatch ticks spent deriving the partition set.<br/></param>
        /// <param name="routersRead">Router pages read by the shape-native planner.<br/></param>
        /// <param name="targetsSelected">Disjoint continuation targets assigned across workers.<br/></param>
        internal void SetTopologyPartitioning(long elapsedTicks, int routersRead, int targetsSelected)
        {
            topologyPartitioningTicks = Math.Max(0, elapsedTicks);
            TopologyRoutersRead = Math.Max(0, routersRead);
            TopologyTargetsSelected = Math.Max(0, targetsSelected);
        }

        /// <summary>
        /// Records exact-worker startup through the point where every worker owns and primes its coherent physical cursor.<br/>
        /// This is deliberately separate from topology selection so thread scheduling and first-page acquisition cannot masquerade as partition-planner cost.<br/>
        /// </summary>
        /// <param name="elapsedTicks">Stopwatch ticks from worker creation through the all-readers-ready barrier.<br/></param>
        internal void SetWorkerPreparation(long elapsedTicks)
        {
            workerPreparationTicks = Math.Max(0, elapsedTicks);
        }

        /// <summary>
        /// Records the semantic plan and initial execution mechanics immediately before source traversal begins.<br/>
        /// </summary>
        /// <param name="planKind">Selected complete, candidate, or Fractal semantic plan.<br/></param>
        /// <param name="route">Concrete FastFind work route.<br/></param>
        /// <param name="resultOrder">Order emitted before caller-side projection.<br/></param>
        /// <param name="actualWorkers">Actual physical workers selected for the route; this must equal the requested count for a successful FastFind execution.<br/></param>
        /// <param name="partitionCount">Known physical partition count, or zero before partition preparation.<br/></param>
        internal void SetPlan(
            ConditionExecutionPlanKind planKind,
            FastFindExecutionRoute route,
            FastFindResultOrder resultOrder,
            int actualWorkers,
            int partitionCount = 0)
        {
            if (actualWorkers != RequestedWorkers)
            {
                throw new InvalidOperationException(
                    $"FastFind requested exactly {RequestedWorkers} workers, but route planning selected {actualWorkers}. Silent worker-count collapse is not permitted.");
            }

            PlanKind = planKind;
            Route = route;
            ResultOrder = resultOrder;
            ActualWorkers = actualWorkers;
            PartitionCount = Math.Max(0, partitionCount);
            if (Interlocked.Read(ref semanticPlanningTicks) == 0)
                SetSemanticPlanningComplete();
            planningTicks = Stopwatch.GetTimestamp() - started;
        }

        /// <summary>
        /// Records the one-time physical snapshot and partition-preparation duration together with the actual non-empty worker count.<br/>
        /// </summary>
        /// <param name="elapsedTicks">Stopwatch ticks consumed by source preparation.<br/></param>
        /// <param name="partitionCount">Actual non-empty physical partition count.<br/></param>
        internal void SetPhysicalPreparation(long elapsedTicks, int partitionCount)
        {
            if (partitionCount != RequestedWorkers)
            {
                throw new InvalidOperationException(
                    $"FastFind requested exactly {RequestedWorkers} workers, but physical preparation supplied {partitionCount} partitions.");
            }

            sourcePreparationTicks = Math.Max(0, elapsedTicks);
            PartitionCount = Math.Max(0, partitionCount);
            ActualWorkers = partitionCount;
        }

        /// <summary>
        /// Records one dedicated FastFind worker after its physical thread begins executing.<br/>
        /// Concurrent workers synchronize behind route-specific start barriers, so distinct managed-thread identities are an observed worker count rather than a planned maximum.<br/>
        /// </summary>
        internal void RecordWorkerStarted()
        {
            if (RequestedWorkers > 1)
                workerThreadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
        }

        /// <summary>
        /// Starts one worker-owned diagnostic accumulator on the physical worker thread.<br/>
        /// The returned object is mutated only by that worker and publishes one immutable summary at completion, avoiding shared per-row atomics.<br/>
        /// </summary>
        /// <param name="workerIndex">Zero-based stable worker ordinal assigned by the coordinator.<br/></param>
        /// <returns>A worker-local accumulator for source, match, work-item, and channel-wait measurements.<br/></returns>
        internal FastFindWorkerExecutionObserver BeginWorker(int workerIndex)
        {
            RecordWorkerStarted();
            return new FastFindWorkerExecutionObserver(this, workerIndex);
        }

        /// <summary>Records one identity emitted by a direct or candidate source, or one physical record examined by a Fractal scan.<br/></summary>
        internal void RecordSourceIdentity() => Interlocked.Increment(ref sourceIdentities);

        /// <summary>Records one Fractal payload made available for semantic evaluation or projection.<br/></summary>
        internal void RecordPayloadRead() => Interlocked.Increment(ref payloadReads);

        /// <summary>Records one match emitted to the FastFind consumer.<br/></summary>
        internal void RecordMatch()
        {
            long count = Interlocked.Increment(ref matches);
            long elapsed = Stopwatch.GetTimestamp() - started;
            if (count == 1)
                _ = Interlocked.CompareExchange(ref firstResultTicks, elapsed, 0);
            else if (count == 10)
                _ = Interlocked.CompareExchange(ref first10ResultTicks, elapsed, 0);
            else if (count == 100)
                _ = Interlocked.CompareExchange(ref first100ResultTicks, elapsed, 0);
            else if (count == 1000)
                _ = Interlocked.CompareExchange(ref first1000ResultTicks, elapsed, 0);
        }

        internal void CompleteWorker(FastFindWorkerExecution worker) => workers.Enqueue(worker);

        /// <summary>
        /// Freezes the current counters into one immutable event payload after the iterator completes or is abandoned.<br/>
        /// </summary>
        /// <param name="completed">Whether the iterator was drained successfully.<br/></param>
        /// <returns>An immutable diagnostic payload safe for event subscribers to retain.<br/></returns>
        internal FastFindExecutionEventArgs Complete(bool completed)
        {
            long totalTicks = Stopwatch.GetTimestamp() - started;
            long boundedPlanningTicks = Math.Clamp(planningTicks, 0, totalTicks);
            int observedWorkers = RequestedWorkers == 1 ? 1 : workerThreadIds.Count;
            if (completed && observedWorkers != RequestedWorkers)
            {
                throw new InvalidOperationException(
                    $"FastFind completed after requesting exactly {RequestedWorkers} workers, but diagnostics observed {observedWorkers} distinct physical worker threads.");
            }
            FastFindWorkerExecution[] completedWorkers = workers.OrderBy(static worker => worker.WorkerIndex).ToArray();
            return new FastFindExecutionEventArgs(
                OperationName,
                PlanKind,
                Route,
                ResultOrder,
                RequestedWorkers,
                observedWorkers,
                PartitionCount,
                Interlocked.Read(ref sourceIdentities),
                Interlocked.Read(ref payloadReads),
                Interlocked.Read(ref matches),
                Stopwatch.GetElapsedTime(0, boundedPlanningTicks),
                Stopwatch.GetElapsedTime(0, Math.Max(0, Interlocked.Read(ref semanticPlanningTicks))),
                Stopwatch.GetElapsedTime(0, Math.Max(0, Interlocked.Read(ref topologyPartitioningTicks))),
                Stopwatch.GetElapsedTime(0, Math.Max(0, Interlocked.Read(ref workerPreparationTicks))),
                Stopwatch.GetElapsedTime(0, Math.Max(0, sourcePreparationTicks)),
                Stopwatch.GetElapsedTime(0, totalTicks - boundedPlanningTicks),
                Stopwatch.GetElapsedTime(0, totalTicks),
                TopologyRoutersRead,
                TopologyTargetsSelected,
                ToElapsed(firstResultTicks),
                ToElapsed(first10ResultTicks),
                ToElapsed(first100ResultTicks),
                ToElapsed(first1000ResultTicks),
                completedWorkers,
                completed);
        }

        private static TimeSpan ToElapsed(long ticks)
            => ticks <= 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(0, ticks);
    }

    /// <summary>
    /// Accumulates diagnostics owned by one physical FastFind worker and publishes one immutable summary when that worker stops.<br/>
    /// All counters are ordinary fields because one worker thread owns this object; only final publication crosses threads.<br/>
    /// </summary>
    internal sealed class FastFindWorkerExecutionObserver
    {
        private readonly FastFindExecutionObserver owner;
        private readonly long started = Stopwatch.GetTimestamp();
        private readonly long allocatedBytesAtStart = GC.GetAllocatedBytesForCurrentThread();
        private readonly int managedThreadId = Environment.CurrentManagedThreadId;
        private int completed;
        private long matchesPublished;
        private long sourceWaitTicks;
        private long sourceWaitCount;
        private long publicationWaitTicks;
        private long publicationWaitCount;

        internal FastFindWorkerExecutionObserver(FastFindExecutionObserver owner, int workerIndex)
        {
            this.owner = owner;
            WorkerIndex = workerIndex;
        }

        internal int WorkerIndex { get; }

        internal void RecordMatchPublished() => matchesPublished++;

        /// <summary>
        /// Records one incomplete wait for work from a bounded source channel.<br/>
        /// Immediate reads remain uncounted so diagnostics distinguish genuine producer starvation from ordinary polling.<br/>
        /// </summary>
        /// <param name="elapsedTicks">Stopwatch ticks spent awaiting readable source work.<br/></param>
        internal void RecordSourceWait(long elapsedTicks)
        {
            if (elapsedTicks <= 0)
                return;

            sourceWaitTicks += elapsedTicks;
            sourceWaitCount++;
        }

        internal void RecordPublicationWait(long elapsedTicks)
        {
            if (elapsedTicks <= 0)
                return;

            publicationWaitTicks += elapsedTicks;
            publicationWaitCount++;
        }

        internal void Complete(int workItemsClaimed, long sourceItemsExamined)
        {
            if (Interlocked.Exchange(ref completed, 1) != 0)
                return;

            owner.CompleteWorker(new FastFindWorkerExecution(
                WorkerIndex,
                managedThreadId,
                Math.Max(0, workItemsClaimed),
                Math.Max(0, sourceItemsExamined),
                matchesPublished,
                Stopwatch.GetElapsedTime(started),
                Stopwatch.GetElapsedTime(0, Math.Max(0, sourceWaitTicks)),
                sourceWaitCount,
                Stopwatch.GetElapsedTime(0, Math.Max(0, publicationWaitTicks)),
                publicationWaitCount,
                Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - allocatedBytesAtStart)));
        }
    }
}
