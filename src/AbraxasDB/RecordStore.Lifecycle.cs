namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        private readonly StoreRuntimeState storeRuntime;

        /// <summary>
        /// Enters one RecordBase-wide operation lease and verifies that this facade still belongs to the active store generation.<br/>
        /// Structural removal waits for admitted operations to finish, while stale facades fail before touching Fractal or LibraDex after removal completes.<br/>
        /// </summary>
        /// <returns>The operation lease that must remain held until the complete top-level operation finishes.<br/></returns>
        private IDisposable EnterStoreOperationScope()
            => rbase.EnterStoreOperationScope(storeRuntime);
    }
}
