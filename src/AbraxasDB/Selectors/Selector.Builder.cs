namespace AbraxasDB
{

    public partial class Selector<T>
    {
        /// <summary>
        /// Builder retains the minimal structural state needed by backend-neutral selector fluency.<br/>
        /// Complete computed meaning resides in immutable selector descriptors rather than a parallel rendered-part list.<br/>
        /// </summary>
        internal sealed class Builder
        {
            private readonly string? simplePropertyPath;
            private readonly string? typedSourcePropertyPath;

            internal string? SimplePropertyPath => simplePropertyPath;
            internal string? TypedSourcePropertyPath => typedSourcePropertyPath;

            /// <summary>
            /// Create a new empty builder.
            /// </summary>
            internal Builder()
            {
            }

            /// <summary>
            /// Creates immutable direct/typed source-path state used by property factories and typed/computed transitions.<br/>
            /// </summary>
            /// <param name="simplePropertyPath">Unchanged direct path, or null for typed/computed views.<br/></param>
            /// <param name="typedSourcePropertyPath">Retained typed source path, or null for computed values.<br/></param>
            private Builder(string? simplePropertyPath, string? typedSourcePropertyPath)
            {
                this.simplePropertyPath = simplePropertyPath;
                this.typedSourcePropertyPath = typedSourcePropertyPath;
            }

            /// <summary>
            /// Marks the selector as computed rather than an unchanged direct property access.<br/>
            /// Computed meaning is retained by the selector's immutable semantic descriptor, so this method only invalidates direct-path shortcuts.<br/>
            /// </summary>
            internal Builder AsComputed() => new Builder(simplePropertyPath: null, typedSourcePropertyPath: null);

            /// <summary>
            /// Records that the current selector structure represents exactly one direct property access.<br/>
            /// Subsequent parts, casts, functions, concatenations, or grouping syntax automatically clear this descriptor so backend-neutral consumers never discard computed semantics.<br/>
            /// </summary>
            /// <param name="propPath">The property path emitted by the selector factory.<br/></param>
            internal static Builder ForProperty(string propPath)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(propPath);
                string normalized = new PropPath(propPath).Path;
                return new Builder(normalized, normalized);
            }

            /// <summary>
            /// Marks the current selector as a typed view rather than an unchanged direct property selector.<br/>
            /// The original typed source path remains available for backend-neutral planning while exact direct-path identity is cleared.<br/>
            /// </summary>
            internal Builder AsTypedView() => new Builder(simplePropertyPath: null, typedSourcePropertyPath);

            /// <summary>
            /// Retains source compatibility for assembly-owned selector nodes whose computed meaning is carried by an immutable semantic descriptor.<br/>
            /// The builder itself is immutable; this method intentionally performs no mutation.<br/>
            /// </summary>
            internal void MarkComputed()
            {
            }

            /// <summary>
            /// Retains source compatibility for assembly-owned typed-view transitions already materialized by the selector constructor.<br/>
            /// The builder itself is immutable; this method intentionally performs no mutation.<br/>
            /// </summary>
            internal void MarkTypedView()
            {
            }
        }
    }
}
