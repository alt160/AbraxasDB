namespace AbraxasDB
{

    public partial class Selector<T>
    {
        /// <summary>
        /// A composable Boolean value selector for record-store operations.<br/>
        /// </summary>
        /// <remarks>
        /// Construction records expression intent; it does not execute a query or read record values.<br/>
        /// </remarks>
        public class BooleanSelector : IValue<bool>,ISelectorStructure
        {
            private readonly Builder parentBuilder;
            private readonly ValueOrExpression<string> selectorExpression;

            string? ISelectorStructure.SimplePropertyPath => parentBuilder.SimplePropertyPath;
            string? ISelectorStructure.TypedSourcePropertyPath => parentBuilder.TypedSourcePropertyPath;
            Type? ISelectorStructure.ResultType => typeof(bool);

            internal BooleanSelector(Builder parentBuilder, ValueOrExpression<string> selectorExpression, bool addBase = true)
            {
                this.parentBuilder = addBase ? parentBuilder.AsComputed() : parentBuilder;
                this.selectorExpression = selectorExpression;
                if (addBase) parentBuilder.MarkComputed();
            }

        }
    }
}
