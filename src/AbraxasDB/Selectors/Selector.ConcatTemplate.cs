using System.Text;

namespace AbraxasDB
{
    public partial class Selector<T>
    {
        /// <summary>
        /// Builds one calculated string selector from literal text and embedded property paths such as <c>{.CompanyName} ({.Country})</c>.<br/>
        /// Double braces emit literal braces, optional <c>:format</c> suffixes use invariant formatting, and every path is resolved and normalized once when the selector is authored.<br/>
        /// Missing or null property values contribute an empty string at execution, matching the established selector-concatenation contract.<br/>
        /// </summary>
        /// <param name="template">Template containing one or more <c>{.PropPath}</c> placeholders; use <c>{{</c> and <c>}}</c> for literal braces.<br/></param>
        /// <returns>A durable string selector eligible for conditions, readers, ordering, grouping, and explicit calculated indexes.<br/></returns>
        public StringSelector Concat(string template)
        {
            ArgumentNullException.ThrowIfNull(template);

            var literal = new StringBuilder(template.Length);
            IBackendNeutralSelectorDescriptor? result = null;
            bool hasPlaceholder = false;
            int index = 0;
            while (index < template.Length)
            {
                char character = template[index];
                if (character == '{')
                {
                    if (index + 1 < template.Length && template[index + 1] == '{')
                    {
                        literal.Append('{');
                        index += 2;
                        continue;
                    }

                    int close = template.IndexOf('}', index + 1);
                    if (close < 0)
                        throw new FormatException($"Selector template has an unmatched opening brace at offset {index}.");
                    string placeholder = template[(index + 1)..close];
                    if (placeholder.IndexOf('{') >= 0)
                        throw new FormatException($"Selector template contains a nested opening brace at offset {index}.");

                    IBackendNeutralSelectorDescriptor value = CreateTemplateValueDescriptor(placeholder);
                    if (result is null)
                    {
                        result = new BuiltInSelectorDescriptor(
                            value,
                            BuiltInSelectorOperationKind.StringPrependLiteral,
                            typeof(string),
                            typeof(string),
                            textOperand: literal.ToString());
                    }
                    else
                    {
                        if (literal.Length != 0)
                        {
                            result = new BuiltInSelectorDescriptor(
                                result,
                                BuiltInSelectorOperationKind.StringConcatLiteral,
                                typeof(string),
                                typeof(string),
                                textOperand: literal.ToString());
                        }
                        result = new StringOperationSelectorDescriptor(
                            result,
                            StringOperationKind.ConcatSelector,
                            rightSource: value);
                    }

                    literal.Clear();
                    hasPlaceholder = true;
                    index = close + 1;
                    continue;
                }

                if (character == '}')
                {
                    if (index + 1 < template.Length && template[index + 1] == '}')
                    {
                        literal.Append('}');
                        index += 2;
                        continue;
                    }
                    throw new FormatException($"Selector template has an unmatched closing brace at offset {index}.");
                }

                literal.Append(character);
                index++;
            }

            if (!hasPlaceholder || result is null)
                throw new FormatException("Selector template must contain at least one property-path placeholder.");
            if (literal.Length != 0)
            {
                result = new BuiltInSelectorDescriptor(
                    result,
                    BuiltInSelectorOperationKind.StringConcatLiteral,
                    typeof(string),
                    typeof(string),
                    textOperand: literal.ToString());
            }

            return new StringSelector(
                new Builder(),
                (ValueOrExpression<string>)string.Empty,
                addBase: false,
                isLiteral: false,
                result);
        }

        /// <summary>
        /// Resolves one template placeholder into a direct string source or an invariant scalar-to-string transformation.<br/>
        /// The path and optional format are frozen into the semantic descriptor so record evaluation never reparses the authored template.<br/>
        /// </summary>
        /// <param name="placeholder">Text between one unescaped opening and closing brace.<br/></param>
        /// <returns>A validated backend-neutral descriptor producing a string value.<br/></returns>
        private static IBackendNeutralSelectorDescriptor CreateTemplateValueDescriptor(string placeholder)
        {
            if (placeholder.Length == 0)
                throw new FormatException("Selector template contains an empty placeholder.");

            int formatSeparator = placeholder.IndexOf(':');
            string path = formatSeparator < 0 ? placeholder : placeholder[..formatSeparator];
            string? format = formatSeparator < 0 || formatSeparator == placeholder.Length - 1
                ? null
                : placeholder[(formatSeparator + 1)..];
            if (!path.StartsWith(".", StringComparison.Ordinal))
                throw new FormatException($"Selector template placeholder '{placeholder}' must begin with a property-path dot.");

            Inheto.PropPathCheck check = Inheto.InhetoBinary.CheckPropPath(typeof(T), path);
            if (!check.IsValid || check.ResultType is null)
            {
                throw new ArgumentException(
                    $"Property path '{path}' is not resolvable from '{typeof(T).FullName}': {check.FailureKind}" +
                    (string.IsNullOrEmpty(check.FailedSegment) ? "." : $" at segment '{check.FailedSegment}'."),
                    nameof(placeholder));
            }

            Type resultType = Nullable.GetUnderlyingType(check.ResultType) ?? check.ResultType;
            if (!SelectorScalarContract.IsSupported(resultType) || resultType == typeof(byte[]))
                throw new NotSupportedException($"Property path '{check.CanonicalPath}' produces '{resultType.FullName}', which has no implicit template-text contract.");
            if (resultType == typeof(string))
            {
                if (format is not null)
                    throw new FormatException($"String property path '{check.CanonicalPath}' does not accept a template format.");
                return new DirectPropertySelectorDescriptor(check.CanonicalPath);
            }
            if ((resultType == typeof(bool) || resultType == typeof(char)) && format is not null)
                throw new FormatException($"Property path '{check.CanonicalPath}' produces '{resultType.Name}', which does not accept a template format.");

            return new BuiltInSelectorDescriptor(
                new DirectPropertySelectorDescriptor(check.CanonicalPath),
                BuiltInSelectorOperationKind.ScalarToInvariantString,
                resultType,
                typeof(string),
                textOperand: format);
        }
    }
}
