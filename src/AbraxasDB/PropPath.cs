using System;
using System.Linq.Expressions;

namespace AbraxasDB
{
    /// <summary>
    /// Describes a neutral Abraxas property path.<br/>
    /// </summary>
    public readonly struct PropPath
    {
        /// <summary>
        /// Creates a normalized Abraxas property path.<br/>
        /// Paths are normalized to a leading-dot form such as <c>.Email</c>.<br/>
        /// </summary>
        /// <param name="path">Property path in either <c>Name</c> or <c>.Name</c> form.<br/></param>
        public PropPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Property path cannot be null or whitespace.", nameof(path));

            Path = NormalizePath(path);
        }

        /// <summary>
        /// Gets the normalized property path, always prefixed with <c>.</c>.<br/>
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Creates a property path from a lambda expression.<br/>
        /// </summary>
        /// <param name="expression">Expression that identifies a simple property path.<br/></param>
        /// <returns>Normalized Abraxas property path.<br/></returns>
        public static PropPath FromExpression(LambdaExpression expression)
        {
            ArgumentNullException.ThrowIfNull(expression);
            return new PropPath(expression.GetPropertyPath());
        }

        /// <summary>
        /// Converts a non-empty path string into a normalized leading-dot Abraxas property path.<br/>
        /// </summary>
        /// <param name="path">Property path in dotted or undotted form; surrounding whitespace is trimmed.<br/></param>
        /// <returns>A normalized property-path value.<br/></returns>
        /// <exception cref="ArgumentException">The path is null, empty, or whitespace.<br/></exception>
        public static implicit operator PropPath(string path) => new(path);

        /// <summary>
        /// Returns this value's stored normalized path.<br/>
        /// </summary>
        /// <returns>The normalized path, or null for an uninitialized default PropPath value.<br/></returns>
        public override string ToString() => Path;

        internal static string NormalizePath(string path)
        {
            var trimmed = path.Trim();
            if (trimmed.Length == 0)
                throw new ArgumentException("Property path cannot be empty.", nameof(path));

            return trimmed[0] == '.' ? trimmed : "." + trimmed;
        }
    }


}
