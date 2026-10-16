using System;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Gets a value indicating whether this store currently owns one or more volatile LibraDex indexes.<br/>
        /// </summary>
        public bool MemoryIndexingExists
        {
            get
            {
                using var operation = EnterStoreOperationScope();
                return HasVolatileLibraDexIndexes();
            }
        }
    }
}
