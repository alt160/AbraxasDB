namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        private readonly StoreParameterFactory parameterFactory = new();

        /// <summary>
        /// Gets the factory for explicit caller-owned parameters reusable across compatible conditions.<br/>
        /// Conditions that reference a created parameter expose the same identity through their own ordered <c>Parameters</c> collection.<br/>
        /// </summary>
        public StoreParameterFactory Parameters => parameterFactory;

        /// <summary>
        /// Creates one caller-owned typed parameter for reusable Abraxas conditions and selectors.<br/>
        /// The current value is captured once when each execution is prepared, so every use of the same parameter observes one coherent value without rebuilding semantic intent.<br/>
        /// The returned parameter is not bound to this store and may be reused by other compatible stores; the caller owns synchronization when <see cref="ConditionParameter{TValue}.Value"/> can change concurrently with execution.<br/>
        /// Conditions retain this same parameter identity for named lookup and reset; creation alone does not add it to a condition.<br/>
        /// </summary>
        /// <typeparam name="TValue">Exact condition or selector operand type.<br/></typeparam>
        /// <param name="value">Initial value visible to execution and retained as the reset target.<br/></param>
        /// <param name="name">Optional non-whitespace, case-sensitive condition-local lookup name.<br/></param>
        /// <returns>A reusable Abraxas parameter accepted by condition and selector operand overloads.<br/></returns>
        public ConditionParameter<TValue> Parameter<TValue>(TValue value, string? name = null)
            => ConditionParameterIdentity.Create(value, name);

        /// <summary>
        /// Creates a caller-owned deferred membership parameter while preserving its element type for condition and selector IntelliSense.<br/>
        /// The current enumerable is copied into one typed execution snapshot when a reader or retrieval begins, so later collection mutations cannot change an in-flight operation.<br/>
        /// Assign a different enumerable to <c>Value</c> before the next execution to reuse the authored condition without rebuilding it.<br/>
        /// Reset restores the originally supplied enumerable reference, not a deep copy of its contents; coordinate mutations that can overlap snapshot creation.<br/>
        /// </summary>
        /// <typeparam name="TValue">The membership element type.<br/></typeparam>
        /// <param name="values">The caller-owned values visible to the next execution.<br/></param>
        /// <param name="name">Optional non-whitespace, case-sensitive condition-local lookup name.<br/></param>
        /// <returns>A reusable typed enumerable parameter with execution-frozen contents.<br/></returns>
        public ConditionParameter<IEnumerable<TValue>> Parameter<TValue>(
            IEnumerable<TValue> values,
            string? name = null)
            => ConditionParameterIdentity.CreateSet(values, name);
    }
}
