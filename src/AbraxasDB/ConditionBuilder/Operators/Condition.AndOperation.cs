
namespace AbraxasDB
{

    public static partial class Condition<T>
    {
        /// <summary>
        /// Structural AND marker retained by the mutable condition builder.<br/>
        /// </summary>
        /// <remarks>
        /// Condition authoring is separate from execution; mutable builder instances should not be edited concurrently.<br/>
        /// </remarks>
        public class AndOperation : IConditionFragment
        {
            /// <summary>
            /// Creates the structural AND marker.<br/>
            /// </summary>
            /// <param name="builder">Compatibility constructor argument; this marker does not retain or modify the supplied builder.<br/></param>
            public AndOperation(Builder builder)
            {
            }
        }
    }
}
