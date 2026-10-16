namespace AbraxasDB
{

    public static partial class Condition<T>
    {
        /// <summary>
        /// Starts a fresh ungrouped condition builder for this record type.<br/>
        /// </summary>
        public class WhereClause : Clause
        {
            /// <summary>
            /// Binds this fluent condition stage to the supplied mutable builder and source, when present.<br/>
            /// </summary>
            public WhereClause() : base(new Builder())
            {
                base.builder.AddFragment(new WhereFragment());
            }
        }
    }
}
