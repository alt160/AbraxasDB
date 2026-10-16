using System;

namespace AbraxasDB
{
    /// <summary>
    /// Thrown when a previous maintenance operation (Optimize or Compact) was detected as in-progress
    /// but has no recent heartbeat and is considered incomplete/stale. The startup logic throws this
    /// so calling code can decide how to recover.
    /// </summary>
    public class IncompleteMaintanenceException : Exception
    {
        /// <summary>
        /// Creates a failure describing incomplete or stale maintenance that requires caller-directed recovery.<br/>
        /// </summary>
        public IncompleteMaintanenceException() { }
        /// <summary>
        /// Creates a failure describing incomplete or stale maintenance that requires caller-directed recovery.<br/>
        /// </summary>
        /// <param name="message">Developer-facing failure detail.<br/></param>
        public IncompleteMaintanenceException(string message) : base(message) { }
        /// <summary>
        /// Creates a failure describing incomplete or stale maintenance that requires caller-directed recovery.<br/>
        /// </summary>
        /// <param name="message">Developer-facing failure detail.<br/></param>
        /// <param name="inner">Underlying exception that caused the failure.<br/></param>
        public IncompleteMaintanenceException(string message, Exception inner) : base(message, inner) { }
    }
}
