namespace AbraxasDB
{

    public static partial class Condition<T>
    {
    }
        /// <summary>
        /// Provides runtime-shaped access and representation for immediate or deferred condition operands; implementations define their equality and factory behavior.<br/>
        /// </summary>
        public interface IValueOrExpression
        {
            /// <summary>
            /// Compares this operand representation with another object according to the implementing wrapper's equality rules.<br/>
            /// </summary>
            /// <param name="obj">Object compared using the implementation's equality rules.<br/></param>
            /// <returns>True when the implementation considers the object equal, otherwise false.<br/></returns>
            bool Equals(object? obj);
            /// <summary>
            /// Returns the implementing wrapper's representation hash code.<br/>
            /// </summary>
            /// <returns>The implementation-defined representation hash.<br/></returns>
            int GetHashCode();
            /// <summary>
            /// Reads the operand's current runtime-shaped value; deferred implementations may invoke application code.<br/>
            /// </summary>
            /// <returns>The current operand value, which may be null.<br/></returns>
            object? GetValue();
            /// <summary>
            /// Returns the implementing wrapper's readable value or factory representation.<br/>
            /// </summary>
            /// <returns>A readable representation, which may be null.<br/></returns>
            string? ToString();
        }
}
