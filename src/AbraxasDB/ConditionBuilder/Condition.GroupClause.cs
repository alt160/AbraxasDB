namespace AbraxasDB
{

    public static partial class Condition<T>
    {
        /// <summary>
        /// Starts an independently authored grouped condition whose completed form can participate in larger Boolean expressions.<br/>
        /// </summary>
        public class GroupClause : Clause
        {
            internal GroupClause() : base(new Builder() { IsGroup=true})
            {
            }
        }
    }
}
