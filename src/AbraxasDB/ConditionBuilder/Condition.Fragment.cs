namespace AbraxasDB
{
    public static partial class Condition<T>
    {
        /// <summary>
        /// Marks one backend-neutral node in an Abraxas condition builder.<br/>
        /// </summary>
        internal interface IConditionFragment
        {
        }

        /// <summary>
        /// Provides backend-neutral cloning for a condition fragment that owns mutable or nested condition state.<br/>
        /// Immutable fragments may omit this capability and are safely shared by builder clones.<br/>
        /// </summary>
        internal interface ICloneableConditionFragment : IConditionFragment
        {
            /// <summary>
            /// Creates an independent fragment for a cloned condition tree.<br/>
            /// </summary>
            /// <returns>A fragment preserving the original backend-neutral condition intent.<br/></returns>
            IConditionFragment CloneConditionFragment();
        }

        /// <summary>
        /// </summary>
        internal interface IConditionLeaf : IConditionFragment
        {
        }
    }
}
