namespace AbraxasDB
{

    public partial class Selector<T>
    {
        /// <summary>
        /// Common base for calendar and time-of-day selectors and their component projections.<br/>
        /// </summary>
        /// <remarks>
        /// Construction records expression intent; it does not execute a query or read record values.<br/>
        /// </remarks>
        public abstract class StructuredDateTimeSelector : ISelector,ISelectorStructure
        {
            private protected readonly Builder parentBuilder;
            private protected readonly ValueOrExpression<string> selectorExpression;
            private readonly IBackendNeutralSelectorDescriptor? semanticDescriptor;

            string? ISelectorStructure.SimplePropertyPath => parentBuilder.SimplePropertyPath;
            string? ISelectorStructure.TypedSourcePropertyPath => parentBuilder.TypedSourcePropertyPath;
            Type? ISelectorStructure.ResultType => this switch
            {
                DateTimeSelector => typeof(DateTime),
                DateOnlySelector => typeof(DateOnly),
                TimeOnlySelector => typeof(TimeOnly),
                _ => null
            };
            IBackendNeutralSelectorDescriptor? ISelectorStructure.SemanticDescriptor => semanticDescriptor;

            private protected StructuredDateTimeSelector(
                Builder parentBuilder,
                ValueOrExpression<string> selectorExpression,
                bool addBase = true,
                IBackendNeutralSelectorDescriptor? semanticDescriptor = null)
            {
                this.parentBuilder = addBase || semanticDescriptor is not null
                    ? parentBuilder.AsComputed()
                    : parentBuilder;
                this.selectorExpression = selectorExpression;
                this.semanticDescriptor = semanticDescriptor;
                if (addBase) parentBuilder.MarkComputed();
            }

            private protected IntegralSelector<long> ApplyLongOp(BuiltInSelectorOperationKind operationKind)
            {
                IBackendNeutralSelectorDescriptor? source = GetSemanticSourceDescriptor();
                Type sourceType = this switch
                {
                    DateTimeSelector => typeof(DateTime),
                    DateOnlySelector => typeof(DateOnly),
                    TimeOnlySelector => typeof(TimeOnly),
                    _ => throw new NotSupportedException("Unknown structured temporal selector source type.")
                };
                IBackendNeutralSelectorDescriptor? descriptor = source is null
                    ? null
                    : new BuiltInSelectorDescriptor(source, operationKind, sourceType, typeof(long));
                parentBuilder.MarkComputed();
                return new IntegralSelector<long>(parentBuilder, selectorExpression, addBase: false, descriptor);
            }

            private protected IntegralSelector<long> ExtractBits(BuiltInSelectorOperationKind operationKind)
            {
                return ApplyLongOp(operationKind);
            }

            /// <summary>
            /// Resolves the structured temporal value that a component projection should consume.<br/>
            /// </summary>
            /// <returns>An immutable direct or computed source descriptor, or null when no backend-neutral identity exists.<br/></returns>
            private protected IBackendNeutralSelectorDescriptor? GetSemanticSourceDescriptor()
            {
                if (semanticDescriptor is not null)
                    return semanticDescriptor;
                string? path = parentBuilder.TypedSourcePropertyPath;
                return string.IsNullOrWhiteSpace(path)
                    ? null
                    : new DirectPropertySelectorDescriptor(path);
            }

        }
    }
}
