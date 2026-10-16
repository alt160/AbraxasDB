using System;
using System.Collections;
using System.Collections.Generic;

namespace AbraxasDB;

/// <summary>
/// Provides typed record persistence, indexed selection, selected-value access, and hydration within an owning RecordBase.<br/>
/// </summary>
/// <typeparam name="T">CLR record shape used by this typed store facade.<br/></typeparam>
public partial class RecordStore<T>
{
    /// <summary>
    /// Represents a materialized set of groups (and their counts) produced by a grouped query.
    /// This is a stable container intended to be retained and used for later drilldowns.
    /// </summary>
    public sealed class GroupedRecords : IReadOnlyList<GroupInfo>
    {
        private readonly IReadOnlyList<GroupInfo> groups;

        internal GroupedRecords(
            IReadOnlyList<GroupInfo> groups,
            Condition<T>.EndCondition? where,
            string? name)
        {
            this.groups = groups ?? throw new ArgumentNullException(nameof(groups));

            Where = where;
            Name = name ?? string.Empty;
        }

        /// <summary>
        /// Optional developer-provided label for this grouped result-set.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Base filter used to produce the grouped result-set.
        /// </summary>
        public Condition<T>.EndCondition? Where { get; }

        /// <summary>
        /// Gets the number of retained groups, not the total number of records in those groups.<br/>
        /// </summary>
        public int Count => groups.Count;

        /// <summary>
        /// Gets one retained group by its zero-based position without rerunning the grouped query.<br/>
        /// </summary>
        /// <param name="index">Zero-based position in the retained group list.<br/></param>
        /// <returns>The retained group at the requested position.<br/></returns>
        public GroupInfo this[int index] => groups[index];

        /// <summary>
        /// Enumerates the retained group instances without rerunning the grouped query.<br/>
        /// </summary>
        /// <returns>An enumerator over the stored group list.<br/></returns>
        public IEnumerator<GroupInfo> GetEnumerator() => groups.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
