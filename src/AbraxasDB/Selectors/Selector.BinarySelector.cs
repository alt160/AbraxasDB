namespace AbraxasDB
{

    /// <summary>
    /// Authors typed property and calculated expressions for record-store operations without hydrating record objects.<br/>
    /// </summary>
    /// <typeparam name="T">Record entity type to which the selector belongs.<br/></typeparam>
    /// <remarks>
    /// Construction records expression intent; it does not execute a query or read record values.<br/>
    /// </remarks>
    public partial class Selector<T>
    {
        /// <summary>
        /// A composable Binary value selector for record-store operations.<br/>
        /// </summary>
        /// <remarks>
        /// Construction records expression intent; it does not execute a query or read record values.<br/>
        /// </remarks>
        public class BinarySelector : IValue<byte[]>,ISelectorStructure
        {
            private readonly Builder parentBuilder;
            private readonly ValueOrExpression<string> selectorExpression;
            private readonly IBackendNeutralSelectorDescriptor? semanticDescriptor;

            string? ISelectorStructure.SimplePropertyPath => parentBuilder.SimplePropertyPath;
            string? ISelectorStructure.TypedSourcePropertyPath => parentBuilder.TypedSourcePropertyPath;
            Type? ISelectorStructure.ResultType => typeof(byte[]);
            IBackendNeutralSelectorDescriptor? ISelectorStructure.SemanticDescriptor => semanticDescriptor;

            internal BinarySelector(Builder parentBuilder, ValueOrExpression<string> selectorExpression, bool addBase = true)
                : this(parentBuilder, selectorExpression, addBase, semanticDescriptor: null)
            {
            }

            /// <summary>
            /// The descriptor is exposed only through <see cref="ISelectorStructure"/> so ordinary execution can consume it without changing the public selector fluency.<br/>
            /// </summary>
            /// <param name="addBase">Whether this instance should append its base selector part.<br/></param>
            /// <param name="semanticDescriptor">Optional backend-neutral computed-value descriptor frozen when the operation was authored.<br/></param>
            /// <param name="parentBuilder">The owning selector builder from which this direct or computed binary selector is constructed.<br/></param>
            /// <param name="selectorExpression">The base selector expression retained for rendering and direct-source selection.<br/></param>
            internal BinarySelector(
                Builder parentBuilder,
                ValueOrExpression<string> selectorExpression,
                bool addBase,
                IBackendNeutralSelectorDescriptor? semanticDescriptor)
            {
                this.parentBuilder = addBase || semanticDescriptor is not null
                    ? parentBuilder.AsComputed()
                    : parentBuilder;
                this.selectorExpression = selectorExpression;
                this.semanticDescriptor = semanticDescriptor;
                if (addBase) parentBuilder.MarkComputed();
            }

            /// <summary>
            /// Selects a byte range from a binary value.<br/>
            /// </summary>
            /// <param name="start">Zero-based source-byte offset.<br/></param>
            /// <param name="length">Requested byte count.<br/></param>
            /// <returns>A selector representing the requested binary transformation.<br/></returns>
            /// <remarks>
            /// The native slice descriptor requires a positive length and offsets/counts representable by Int64. Unsupported bounds do not acquire native slice semantics.<br/>
            /// </remarks>
            public BinarySelector Slice(ulong start, ulong length)
            {
                var sliceDescriptor = CreateByteArraySliceDescriptor(start, length);
                parentBuilder.MarkComputed();
                return new BinarySelector(parentBuilder, selectorExpression, addBase: false, sliceDescriptor);
            }

            /// <summary>
            /// Freezes a direct or computed binary source and one positive-length byte slice into the backend-neutral binary-slice descriptor.<br/>
            /// Computed sources retain their complete recursive semantics so explicit calculated indexes can match the exact expression.<br/>
            /// Zero-length slices and values outside the descriptor's signed range remain reference-only rather than claiming incomplete native semantics.<br/>
            /// </summary>
            /// <param name="start">Zero-based source-byte offset supplied to <see cref="Slice(ulong, ulong)"/>.<br/></param>
            /// <param name="length">Requested byte count supplied to <see cref="Slice(ulong, ulong)"/>.<br/></param>
            /// <returns>An immutable byte-array slice descriptor when the source and bounds are supported; otherwise null.<br/></returns>
            private IBackendNeutralSelectorDescriptor? CreateByteArraySliceDescriptor(ulong start, ulong length)
            {
                IBackendNeutralSelectorDescriptor? sourceDescriptor = GetSemanticSourceDescriptor();
                if (sourceDescriptor is null || length == 0 ||
                    start > long.MaxValue ||
                    length > long.MaxValue)
                {
                    return null;
                }

                var sourceSelector = ((ValueOrExpression<string>)string.Empty)
                    .WithSelectorDescriptor(sourceDescriptor);
                return new Condition<T>.BinarySliceSelectorDescriptor(
                    sourceSelector,
                    BinarySliceKind.ByteArray,
                    (long)start,
                    (long)length,
                    System.Text.Encoding.UTF8);
            }

            /// <summary>
            /// Returns the exact executable source represented by this selector before a new binary transformation is appended.<br/>
            /// A prior computed node is retained recursively; an untouched typed selector becomes one direct-property descriptor.<br/>
            /// </summary>
            /// <returns>The immutable source descriptor, or null when the retained builder has no backend-neutral source identity.<br/></returns>
            private IBackendNeutralSelectorDescriptor? GetSemanticSourceDescriptor()
            {
                if (semanticDescriptor is not null)
                    return semanticDescriptor;
                string? path = parentBuilder.TypedSourcePropertyPath;
                return string.IsNullOrWhiteSpace(path)
                    ? null
                    : new DirectPropertySelectorDescriptor(path);
            }

            /// <summary>
            /// Selects one byte from a binary value.<br/>
            /// </summary>
            /// <param name="index">Zero-based source-byte offset.<br/></param>
            /// <returns>A selector representing the requested binary transformation.<br/></returns>
            /// <remarks>
            /// The native descriptor supports offsets representable by Int64; source availability and bounds are evaluated when consumed.<br/>
            /// </remarks>
            public IntegralSelector<byte> GetByte(ulong index)
            {
                IBackendNeutralSelectorDescriptor? source = GetSemanticSourceDescriptor();
                IBackendNeutralSelectorDescriptor? descriptor = source is null || index > long.MaxValue
                    ? null
                    : new BuiltInSelectorDescriptor(
                        source,
                        BuiltInSelectorOperationKind.BinaryGetByte,
                        typeof(byte[]),
                        typeof(byte),
                        firstIntegerOperand: (long)index);
                parentBuilder.MarkComputed();
                return new IntegralSelector<byte>(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            /// <summary>
            /// Encodes the selected bytes as hexadecimal text.<br/>
            /// </summary>
            /// <returns>A selector representing the requested binary transformation.<br/></returns>
            public StringSelector ToHex()
            {
                IBackendNeutralSelectorDescriptor? source = GetSemanticSourceDescriptor();
                IBackendNeutralSelectorDescriptor? descriptor = source is null
                    ? null
                    : new BuiltInSelectorDescriptor(
                        source,
                        BuiltInSelectorOperationKind.BinaryToHex,
                        typeof(byte[]),
                        typeof(string));
                parentBuilder.MarkComputed();
                return new StringSelector(
                    parentBuilder,
                    selectorExpression,
                    addBase: false,
                    isLiteral: false,
                    semanticDescriptor: descriptor);
            }

            /// <summary>
            /// Encodes the selected bytes as Base64 text.<br/>
            /// </summary>
            /// <returns>A selector representing the requested binary transformation.<br/></returns>
            public StringSelector ToBase64()
            {
                IBackendNeutralSelectorDescriptor? source = GetSemanticSourceDescriptor();
                IBackendNeutralSelectorDescriptor? descriptor = source is null
                    ? null
                    : new BuiltInSelectorDescriptor(
                        source,
                        BuiltInSelectorOperationKind.BinaryToBase64,
                        typeof(byte[]),
                        typeof(string));
                parentBuilder.MarkComputed();
                return new StringSelector(
                    parentBuilder,
                    selectorExpression,
                    addBase: false,
                    isLiteral: false,
                    semanticDescriptor: descriptor);
            }

        }
    }
}
