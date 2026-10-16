namespace AbraxasDB
{

    public partial class Selector<T>
    {
        /// <summary>
        /// A composable Guid value selector for record-store operations.<br/>
        /// </summary>
        /// <remarks>
        /// Construction records expression intent; it does not execute a query or read record values.<br/>
        /// </remarks>
        public class GuidSelector : IValue<Guid>,ISelectorStructure
        {
            private readonly Builder parentBuilder;
            private readonly ValueOrExpression<string> selectorExpression;
            private readonly IBackendNeutralSelectorDescriptor? semanticDescriptor;

            string? ISelectorStructure.SimplePropertyPath => parentBuilder.SimplePropertyPath;
            string? ISelectorStructure.TypedSourcePropertyPath => parentBuilder.TypedSourcePropertyPath;
            Type? ISelectorStructure.ResultType => typeof(Guid);
            IBackendNeutralSelectorDescriptor? ISelectorStructure.SemanticDescriptor => semanticDescriptor;

            internal GuidSelector(Builder parentBuilder, ValueOrExpression<string> selectorExpression, bool addBase = true)
                : this(parentBuilder, selectorExpression, addBase, semanticDescriptor: null)
            {
            }

            /// <summary>
            /// </summary>
            /// <param name="parentBuilder">Owning selector builder.<br/></param>
            /// <param name="selectorExpression">Retained base selector expression.<br/></param>
            /// <param name="addBase">Whether to append this selector as a base part.<br/></param>
            /// <param name="semanticDescriptor">Optional computed Guid-producing descriptor.<br/></param>
            internal GuidSelector(
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

            private BinarySelector ApplyBinaryOp(IBackendNeutralSelectorDescriptor? descriptor)
            {
                parentBuilder.MarkComputed();
                return new BinarySelector(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            private StringSelector ApplyStringTransform(IBackendNeutralSelectorDescriptor? descriptor)
            {
                parentBuilder.MarkComputed();
                return new StringSelector(
                    parentBuilder,
                    selectorExpression,
                    addBase: false,
                    isLiteral: false,
                    semanticDescriptor: descriptor);
            }

            /// <summary>
            /// Creates one built-in Guid transformation descriptor from the current direct or computed Guid source.<br/>
            /// </summary>
            /// <param name="operationKind">Guid operation to freeze.<br/></param>
            /// <param name="resultType">Exact CLR result type.<br/></param>
            /// <param name="firstIntegerOperand">Optional first integral argument.<br/></param>
            /// <param name="secondIntegerOperand">Optional second integral argument.<br/></param>
            /// <param name="textOperand">Optional immutable comparison argument.<br/></param>
            /// <returns>A complete descriptor, or null when the source has no backend-neutral identity.<br/></returns>
            private IBackendNeutralSelectorDescriptor? Describe(
                BuiltInSelectorOperationKind operationKind,
                Type resultType,
                long firstIntegerOperand = 0,
                long secondIntegerOperand = 0,
                string? textOperand = null)
            {
                IBackendNeutralSelectorDescriptor? source = semanticDescriptor;
                if (source is null)
                {
                    string? path = parentBuilder.TypedSourcePropertyPath;
                    if (!string.IsNullOrWhiteSpace(path))
                        source = new DirectPropertySelectorDescriptor(path);
                }
                return source is null
                    ? null
                    : new BuiltInSelectorDescriptor(
                        source,
                        operationKind,
                        typeof(Guid),
                        resultType,
                        firstIntegerOperand,
                        secondIntegerOperand,
                        textOperand);
            }

            /// <summary>
            /// Selects the GUID's 16-byte <c>Guid.ToByteArray()</c> representation.<br/>
            /// </summary>
            /// <returns>A selector representing the requested GUID transformation.<br/></returns>
            /// <remarks>
            /// This uses .NET GUID byte order, which is not the same as concatenating the displayed hexadecimal segments.<br/>
            /// </remarks>
            public BinarySelector AsBinary => new BinarySelector(
                parentBuilder,
                selectorExpression,
                addBase: false,
                Describe(BuiltInSelectorOperationKind.GuidAsBinary, typeof(byte[])));

            /// <summary>
            /// Selects the GUID's standard hyphenated or compact hexadecimal text.<br/>
            /// </summary>
            /// <param name="format">Hyphenated for the D format; Numeric for the 32 hexadecimal digits of the N format.<br/></param>
            /// <returns>A selector representing the requested GUID transformation.<br/></returns>
            public StringSelector AsString(GuidStringFormat format = GuidStringFormat.Hyphenated)
            {
                BuiltInSelectorOperationKind operationKind = format switch
                {
                    GuidStringFormat.Hyphenated => BuiltInSelectorOperationKind.GuidAsStringHyphenated,
                    GuidStringFormat.Numeric => BuiltInSelectorOperationKind.GuidAsStringNumeric,
                    _ => throw new ArgumentOutOfRangeException(nameof(format), "Unknown GUID string format.")
                };
                return ApplyStringTransform(Describe(operationKind, typeof(string)));
            }

            /// <summary>
            /// Selects bytes from one of the five displayed GUID segments.<br/>
            /// </summary>
            /// <param name="segment">One-based segment number from 1 through 5, with hexadecimal lengths 8, 4, 4, 4, and 12.<br/></param>
            /// <returns>A selector representing the requested GUID transformation.<br/></returns>
            /// <remarks>
            /// Bytes follow the displayed hexadecimal order rather than Guid.ToByteArray field ordering.<br/>
            /// </remarks>
            public BinarySelector Segment(int segment)
            {
                if (segment < 1 || segment > 5)
                    throw new ArgumentOutOfRangeException(nameof(segment), "segment must be in the range 1-5.");
                return ApplyBinaryOp(
                    Describe(BuiltInSelectorOperationKind.GuidSegment, typeof(byte[]), segment));
            }

            /// <summary>
            /// Selects and packs a nibble range from the GUID's 32 displayed hexadecimal digits.<br/>
            /// </summary>
            /// <param name="startNibble">Zero-based hexadecimal-digit offset from 0 through 31.<br/></param>
            /// <param name="nibbleCount">Positive count of hexadecimal digits; the selected range must fit within 32 digits.<br/></param>
            /// <returns>A selector representing the requested GUID transformation.<br/></returns>
            /// <remarks>
            /// Digits are packed high nibble first. An odd final digit occupies the high nibble and its low nibble is zero.<br/>
            /// </remarks>
            public BinarySelector Slice(int startNibble, int nibbleCount)
            {
                if (startNibble < 0 || startNibble > 31)
                    throw new ArgumentOutOfRangeException(nameof(startNibble), "startNibble must be in the range 0-31.");
                if (nibbleCount <= 0 || nibbleCount > 32)
                    throw new ArgumentOutOfRangeException(nameof(nibbleCount), "nibbleCount must be in the range 1-32.");
                if (startNibble + nibbleCount > 32)
                    throw new ArgumentOutOfRangeException(nameof(nibbleCount), "startNibble + nibbleCount must be <= 32.");

                return ApplyBinaryOp(
                    Describe(BuiltInSelectorOperationKind.GuidSlice, typeof(byte[]), startNibble, nibbleCount));
            }

        }
    }
}
