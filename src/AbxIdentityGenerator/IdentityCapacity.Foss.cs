namespace AbraxasDB
{
    /// <summary>
    /// Compile-time identity-capacity defaults for the FOSS Abraxas edition.<br/>
    /// The values restrict usable capacity without changing the physical 64-bit Fractal identity layout.<br/>
    /// </summary>
    internal static class IdentityCapacity
    {
        /// <summary>
        /// Gets the number of usable upper store-identity bits compiled into this edition.<br/>
        /// </summary>
        public const int StoreIdBitCount = 4;

        /// <summary>
        /// Gets the number of usable lower record-counter bits compiled into this edition.<br/>
        /// </summary>
        public const int RecordIdBitCount = 19;
    }
}
