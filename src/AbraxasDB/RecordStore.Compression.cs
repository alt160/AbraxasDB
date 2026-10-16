using System;
using Inheto;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Preserves the FOSS edition's direct raw INHETO-to-Fractal write path without allocating a payload wrapper.<br/>
        /// </summary>
        /// <param name="recordId">Stable identity of the record receiving the persisted payload.<br/></param>
        /// <param name="rawPayload">Complete uncompressed INHETO payload.<br/></param>
        /// <returns>The original payload bytes unchanged.<br/></returns>
        private static ReadOnlyMemory<byte> EncodeRecordPayload(ulong recordId, ReadOnlyMemory<byte> rawPayload)
            => rawPayload;

        /// <summary>
        /// Creates one INHETO reader directly over the FOSS edition's raw Fractal application-record payload.<br/>
        /// </summary>
        /// <param name="recordId">Stable identity of the record supplying the persisted payload.<br/></param>
        /// <param name="storedPayload">Borrowed raw INHETO payload.<br/></param>
        /// <returns>An INHETO reader positioned over the supplied payload.<br/></returns>
        private InhetoBinary CreateRecordBinary(ulong recordId, ReadOnlyMemory<byte> storedPayload)
            => InhetoBinary.FromStream(new BufferStream(storedPayload), NativeDeserializationOptions);

        /// <summary>
        /// Creates one INHETO reader from an exported FOSS stored payload.<br/>
        /// FOSS payloads are raw INHETO bytes, so source identity metadata is retained only for transfer diagnostics and destination conflict resolution.<br/>
        /// </summary>
        /// <param name="sourceRecordId">Original record identity retained by the transfer package.<br/></param>
        /// <param name="sourceStoreInstanceId">Original durable store identity retained by the transfer package.<br/></param>
        /// <param name="storedPayload">Owned raw INHETO bytes from the transfer package.<br/></param>
        /// <returns>An INHETO reader over the source payload.<br/></returns>
        private InhetoBinary CreateImportedStoredRecordBinary(
            ulong sourceRecordId,
            Guid sourceStoreInstanceId,
            ReadOnlyMemory<byte> storedPayload)
            => InhetoBinary.FromStream(new BufferStream(storedPayload), NativeDeserializationOptions);

        /// <summary>
        /// Indicates that the FOSS raw payload representation has no portable encrypted-envelope form eligible for physical replay.<br/>
        /// </summary>
        /// <param name="storedPayload">Exact raw stored payload retained by a validated transfer package.<br/></param>
        /// <returns>Always <see langword="false"/> because FOSS writes no encrypted portability binding.<br/></returns>
        private static bool IsPortableStoredPayload(ReadOnlyMemory<byte> storedPayload)
            => false;

        /// <summary>
        /// Rebinds one reusable INHETO reader directly to the FOSS edition's raw Fractal payload.<br/>
        /// </summary>
        /// <param name="binary">Reusable INHETO reader to rebind.<br/></param>
        /// <param name="recordId">Stable identity of the record supplying the persisted payload.<br/></param>
        /// <param name="storedPayload">Borrowed raw INHETO payload.<br/></param>
        private void RebindRecordBinary(InhetoBinary binary, ulong recordId, ReadOnlyMemory<byte> storedPayload)
        {
            ArgumentNullException.ThrowIfNull(binary);
            binary.RebindBorrowedPayload(storedPayload, NativeDeserializationOptions);
        }
    }
}
