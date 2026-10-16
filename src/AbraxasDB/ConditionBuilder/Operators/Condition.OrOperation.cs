
namespace AbraxasDB
{

    public static partial class Condition<T>
    {
        /// <summary>
        /// Structural OR marker retained by the mutable condition builder.<br/>
        /// </summary>
        /// <remarks>
        /// Condition authoring is separate from execution; mutable builder instances should not be edited concurrently.<br/>
        /// </remarks>
        public class OrOperation : IConditionFragment
        {
            /// <summary>
            /// Creates the structural OR marker.<br/>
            /// </summary>
            /// <param name="builder">Compatibility constructor argument; this marker does not retain or modify the supplied builder.<br/></param>
            public OrOperation(Builder builder)
            {
            }
        }
    }
}
