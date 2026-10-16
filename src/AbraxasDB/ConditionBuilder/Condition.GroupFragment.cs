using System;

namespace AbraxasDB
{

    public static partial class Condition<T>
    {
        internal class GroupStartFragment : IConditionFragment
        {
            public GroupStartFragment( )
            {
             }
        }

        internal class GroupConditionFragment : ICloneableConditionFragment
        {
            private readonly Builder groupBuilder;
			internal Builder GroupBuilder => groupBuilder;
            public GroupConditionFragment(Builder groupBuilder)
            {
                ArgumentNullException.ThrowIfNull(groupBuilder);
                this.groupBuilder = groupBuilder;
            }

            /// <summary>
            /// Creates an independent nested group for backend-neutral condition-tree cloning.<br/>
            /// </summary>
            /// <returns>A group fragment containing a recursively cloned builder.<br/></returns>
            IConditionFragment ICloneableConditionFragment.CloneConditionFragment()
            {
                return new GroupConditionFragment(groupBuilder.Clone());
            }
        }

        internal class GroupEndFragment : IConditionFragment
        {
            public GroupEndFragment( )
            {
             }
        }
    }
}
