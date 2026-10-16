namespace AbraxasDB
{

    public static partial class Condition<T>
    {
        /// <summary>
        /// Continues the current mutable condition through Boolean composition or exposes its completed handle.<br/>
        /// </summary>
        public class ContinueOrEndCondition
        {
            internal Builder builder;
            /// <summary>
            /// Binds this fluent condition stage to the supplied mutable builder and source, when present.<br/>
            /// </summary>
            /// <param name="builder">Mutable condition builder shared by this stage's fluent operations.<br/></param>
            public ContinueOrEndCondition(Builder builder)
            {
                this.builder = builder;
            }

            /// <summary>
            /// Gets the explicit parameters referenced by the condition built so far.<br/>
            /// Function-based deferred expressions remain executable behavior and are intentionally excluded.<br/>
            /// </summary>
            public ConditionParameterCollection Parameters => builder.Parameters;

            /// <summary>
            /// Appends Boolean OR to the current builder and returns the next clause; this mutates the in-progress condition.<br/>
            /// </summary>
            public Clause OR { get { builder.AddFragment(new OrOperation(builder)); return new Clause(builder); } }
            /// <summary>
            /// Appends Boolean AND to the current builder and returns the next clause; this mutates the in-progress condition.<br/>
            /// </summary>
            public Clause AND { get { builder.AddFragment(new AndOperation(builder)); return new Clause(builder); } }

            /// <summary>
            /// Appends the terminal fragment and returns a completed handle over the same builder; it is not a query execution.<br/>
            /// </summary>
            public EndCondition EndCondition { get { return EndCondition.AddToBuilder(builder); } }

            /// <summary>
            /// This is a convenience alias for <see cref="EndCondition"/>
            /// </summary>
            public EndCondition ec => EndCondition;

            /// <summary>
            /// Clones the underlying builder and returns an independent fluent continuation retaining the existing condition structure.<br/>
            /// </summary>
            /// <returns>A continuation over the cloned builder.<br/></returns>
            public ContinueOrEndCondition Clone()
            {
                var cloned = new ContinueOrEndCondition(builder.Clone());
                return cloned;
            }

            /// <summary>
            /// Combines the condition built so far with one completed condition using Boolean AND.<br/>
            /// The supplied condition is cloned and retained as one grouped right operand, so its internal OR operations preserve their intended precedence and later reuse remains isolated.<br/>
            /// </summary>
            /// <param name="condition">Completed condition to use as the grouped right operand.<br/></param>
            /// <returns>A continuation that can end the combined condition or append another Boolean operation.<br/></returns>
            public ContinueOrEndCondition And(EndCondition condition)
            {
                ArgumentNullException.ThrowIfNull(condition);
                builder.AddFragment(new AndOperation(builder));
                builder.AddGroup(condition);
                return new ContinueOrEndCondition(builder);
            }

            /// <summary>
            /// Combines the condition built so far with one completed condition using Boolean OR.<br/>
            /// The supplied condition is cloned and retained as one grouped right operand, so its internal AND operations preserve their intended precedence and later reuse remains isolated.<br/>
            /// </summary>
            /// <param name="condition">Completed condition to use as the grouped right operand.<br/></param>
            /// <returns>A continuation that can end the combined condition or append another Boolean operation.<br/></returns>
            public ContinueOrEndCondition Or(EndCondition condition)
            {
                ArgumentNullException.ThrowIfNull(condition);
                builder.AddFragment(new OrOperation(builder));
                builder.AddGroup(condition);
                return new ContinueOrEndCondition(builder);
            }

        }
    }
}
