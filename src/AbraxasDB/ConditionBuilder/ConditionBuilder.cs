using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Threading.Tasks;

namespace AbraxasDB
{

    public static partial class Condition<T>
    {

        /// <summary>
        /// Gets a fresh ungrouped condition entry point with its own builder.<br/>
        /// </summary>
        public static WhereClause Where { get { return new WhereClause(); } }

        /// <summary>
        /// Gets a fresh grouped condition entry point with its own builder.<br/>
        /// </summary>
        public static GroupClause Group { get { return new GroupClause(); } }

    }

}
