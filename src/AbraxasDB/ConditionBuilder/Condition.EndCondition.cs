using System;

namespace AbraxasDB
{

    public static partial class Condition<T>
    {
        /// <summary>
        /// Retains a completed condition handle backed by its mutable builder; construction alone does not execute a query.<br/>
        /// </summary>
        public class EndCondition : IConditionFragment
        {
            private bool isGroup = false;
            /// <summary>
            /// Binds this fluent condition stage to the supplied mutable builder and source, when present.<br/>
            /// </summary>
            /// <param name="builder">Mutable condition builder shared by this stage's fluent operations.<br/></param>
            public EndCondition(Builder builder)
            {
                this.builder = builder;
            }

            /// <summary>
            /// The mutable builder retained by this completed condition; direct replacement changes subsequent execution and descriptor capture.<br/>
            /// </summary>
            public Builder builder;

            /// <summary>
            /// Gets the completed condition's explicit parameters in first-appearance order.<br/>
            /// The returned collection supports named access, iteration, mutation, and restoration of creation-time defaults.<br/>
            /// </summary>
            public ConditionParameterCollection Parameters => builder.Parameters;

            /// <summary>
            /// Raised during this condition's operand materialization before store-owned materialization hooks; a handler may replace the value with a compatible value.<br/>
            /// Handlers run on the executing thread; no UI-thread dispatch is provided.<br/>
            /// </summary>
            public event EventHandler<ParameterMaterializedEventArgs>? ParameterMaterialized;

            /// <summary>
            /// Creates a completed condition handle and appends its terminal fragment to the supplied builder; no query executes.<br/>
            /// </summary>
            /// <param name="builder">Mutable builder to which the completed-condition fragment is appended.<br/></param>
            /// <returns>The completed handle retaining that builder.<br/></returns>
            public static EndCondition AddToBuilder(Builder builder)
            {
                var isGroup = builder.groupCounter > 0 ? true : false;
                var ret = new EndCondition(builder);
                builder.AddFragment(ret);
                return ret;
            }

            internal void RaiseParameterMaterialized(ParameterMaterializedEventArgs args)
            {
                ParameterMaterialized?.Invoke(this, args);
            }

            /// <summary>
            /// Creates a clone of this end condition.<br/>
            /// Clones the underlying builder to keep fragment consistency.<br/>
            /// Preserves grouping state and parameter materialization handlers.<br/>
            /// </summary>
            public EndCondition Clone()
            {
                var clone = new EndCondition(builder.Clone())
                {
                    isGroup = isGroup
                };

                clone.ParameterMaterialized = ParameterMaterialized;
                return clone;
            }

            /// <summary>
            /// Captures this completed runtime condition as a versioned, backend-neutral semantic descriptor.<br/>
            /// Static operands are embedded while deferred operands become typed, inspectable binding positions in deterministic left-to-right order.<br/>
            /// </summary>
            /// <returns>An immutable descriptor that can be rendered, serialized, inspected, or strictly rehydrated.<br/></returns>
            public ConditionDescriptor<T> ToDescriptor()
                => ConditionDescriptor<T>.FromCondition(this);

            /// <summary>
            /// Serializes this completed runtime condition into its versioned Inheto condition-package representation.<br/>
            /// A condition containing deferred operands remains a valid incomplete package whose required bindings can be inspected before rehydration.<br/>
            /// </summary>
            /// <returns>Caller-owned Inheto bytes containing the semantic condition document and ordered binding contract.<br/></returns>
            public byte[] Serialize()
                => ToDescriptor().Serialize();

            /// <summary>
            /// Produces deterministic invariant diagnostic text from this condition's backend-neutral semantic descriptor.<br/>
            /// The output preserves semantic operation names, typed literals, and unresolved binding positions for logging, support, comparison, and technical diagnostics.<br/>
            /// This text is a one-way projection and must not be parsed or treated as an executable or persisted condition representation.<br/>
            /// </summary>
            /// <returns>Canonical diagnostic text regenerated from a freshly captured semantic descriptor.<br/></returns>
            public string ToCanonicalString()
                => ToDescriptor().ToCanonicalString();

            /// <summary>
            /// Produces concise human-readable text from this condition's backend-neutral semantic descriptor for display, reporting, and support surfaces.<br/>
            /// Deferred values remain visible as named or positional typed binding placeholders and are not evaluated while the text is produced.<br/>
            /// This text is descriptive and one-way; the semantic descriptor remains authoritative for serialization, identity, and rehydration.<br/>
            /// </summary>
            /// <returns>Human-readable condition text regenerated from a freshly captured semantic descriptor.<br/></returns>
            public string ToDisplayString()
                => ToDescriptor().ToDisplayString();

            /// <summary>
            /// Produces descriptor-backed condition text using the requested audience, layout, labeling, masking, culture, and value-formatting controls.<br/>
            /// Sensitive values are replaced before literal deserialization or caller formatting, and the output is never parsed or used as executable condition state.<br/>
            /// </summary>
            /// <param name="options">Presentation controls applied to this completed condition.<br/></param>
            /// <returns>Human-readable condition text regenerated from a freshly captured semantic descriptor.<br/></returns>
            public string ToDisplayString(ConditionTextOptions options)
            {
                ArgumentNullException.ThrowIfNull(options);
                return ToDescriptor().ToDisplayString(options);
            }

            /// <summary>
            /// Attempts to capture and render this condition using default end-user narrative formatting.<br/>
            /// Unsupported semantic shapes return <c>false</c>; resolver, formatter, and other caller-code exceptions remain visible to the application.<br/>
            /// </summary>
            /// <param name="text">Rendered text when supported; otherwise an empty string.<br/></param>
            /// <returns><c>true</c> when descriptor capture and display rendering succeed; otherwise <c>false</c>.<br/></returns>
            public bool TryToDisplayString(out string text)
                => TryToDisplayString(new ConditionTextOptions(), out text);

            /// <summary>
            /// Attempts to capture and render this condition using the requested presentation controls.<br/>
            /// Unsupported semantic shapes return <c>false</c>; resolver, formatter, and other caller-code exceptions remain visible to the application.<br/>
            /// </summary>
            /// <param name="options">Presentation controls applied to this completed condition.<br/></param>
            /// <param name="text">Rendered text when supported; otherwise an empty string.<br/></param>
            /// <returns><c>true</c> when descriptor capture and display rendering succeed; otherwise <c>false</c>.<br/></returns>
            public bool TryToDisplayString(ConditionTextOptions options, out string text)
            {
                ArgumentNullException.ThrowIfNull(options);
                ConditionDescriptor<T> descriptor;
                try
                {
                    descriptor = ToDescriptor();
                }
                catch (NotSupportedException)
                {
                    text = string.Empty;
                    return false;
                }
                return descriptor.TryToDisplayString(options, out text);
            }
        }

        /// <summary>
        /// Exposes one condition-owned operand materialization to a handler that may replace the value before execution.<br/>
        /// </summary>
        public sealed class ParameterMaterializedEventArgs : EventArgs
        {
            internal ParameterMaterializedEventArgs(
                EndCondition condition,
                RecordStore<T>? recordStore,
                RecordBase? recordBase,
                string? propertyPath,
                int? propertyPathOccurrence,
                string? parameterAlias,
                object? value)
            {
                Condition = condition;
                RecordStore = recordStore;
                RecordBase = recordBase;
                PropertyPath = propertyPath;
                PropertyPathOccurrence = propertyPathOccurrence;
                ParameterAlias = parameterAlias;
                Value = value;
            }

            /// <summary>
            /// Gets the completed condition whose operand is being materialized.<br/>
            /// </summary>
            public EndCondition Condition { get; }
            /// <summary>
            /// Gets the executing store when a store-owned context exists, otherwise null.<br/>
            /// </summary>
            public RecordStore<T>? RecordStore { get; }
            /// <summary>
            /// Gets the owning RecordBase when available, otherwise null.<br/>
            /// </summary>
            public RecordBase? RecordBase { get; }
            /// <summary>
            /// Gets the effective property path associated with this operand, or null when unavailable.<br/>
            /// </summary>
            public string? PropertyPath { get; }
            /// <summary>
            /// Gets the zero-based materialization occurrence for the effective path in this execution, or null when unavailable.<br/>
            /// </summary>
            public int? PropertyPathOccurrence { get; }
            /// <summary>
            /// Gets the optional named-parameter alias associated with this operand.<br/>
            /// </summary>
            public string? ParameterAlias { get; }
            /// <summary>
            /// Gets or sets the value returned to the materialization pipeline; replacements must be compatible with the consuming operator's required type.<br/>
            /// </summary>
            public object? Value { get; set; }
        }
    }
}
