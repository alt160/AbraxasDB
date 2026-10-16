namespace AbraxasDB
{
    /// <summary>
    /// FOSS-only compact identity format.<br/>
    /// The logical identity is intentionally constructed as a <see cref="uint"/>, then expanded through the established Fractal physical layout.<br/>
    /// </summary>
    internal static class CompiledIdentityFormat
    {
        internal const int PhysicalRecordIdBitCount = 56;
        internal const byte MaximumStoreID = (byte)((1 << IdentityCapacity.StoreIdBitCount) - 1);
        internal const ulong MaximumCounter = (1ul << IdentityCapacity.RecordIdBitCount) - 1;

        /// <summary>
        /// Composes one FOSS logical <see cref="uint"/> identity and maps it into Fractal's physical <see cref="ulong"/> group-key layout.<br/>
        /// </summary>
        /// <param name="storeID">FOSS store identity packed into the compact logical value.<br/></param>
        /// <param name="counter">FOSS record counter packed into the compact logical value.<br/></param>
        /// <returns>Physical Fractal/LibraDex identity with the group key in its established upper eight bits.<br/></returns>
        internal static ulong Compose(byte storeID, ulong counter)
        {
            uint logicalIdentity = ((uint)storeID << IdentityCapacity.RecordIdBitCount) | checked((uint)counter);
            return ((ulong)(logicalIdentity >> IdentityCapacity.RecordIdBitCount) << PhysicalRecordIdBitCount) |
                (logicalIdentity & MaximumCounter);
        }

        /// <summary>
        /// Verifies that a physical identity is representable by the FOSS logical identity format.<br/>
        /// </summary>
        /// <param name="identity">Physical Fractal/LibraDex identity under admission validation.<br/></param>
        /// <param name="storeID">Expected FOSS store identity.<br/></param>
        /// <returns>True when the identity retains the expected store and a representable compact counter.<br/></returns>
        internal static bool IsValidPhysicalIdentity(ulong identity, byte storeID)
        {
            return (byte)(identity >> PhysicalRecordIdBitCount) == storeID &&
                (identity & ((1ul << PhysicalRecordIdBitCount) - 1)) <= MaximumCounter;
        }
    }
}
