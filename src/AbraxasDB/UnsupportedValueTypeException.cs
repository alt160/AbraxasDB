namespace AbraxasDB 
{
/// <summary>Represents an unsupported value-type operation through the retained Abraxas compatibility exception contract.<br/></summary>
public class UnsupportedValueTypeException : Exception
{
    //======  CONSTRUCTORS  ======
    /// <summary>
    /// Creates a compatibility failure for an unsupported value-type operation.<br/>
    /// </summary>
    public UnsupportedValueTypeException() : base() { }

    /// <summary>
    /// Creates a compatibility failure for an unsupported value-type operation.<br/>
    /// </summary>
    /// <param name="message">Developer-facing failure detail.<br/></param>
    public UnsupportedValueTypeException(string message) : base(message) { }

    /// <summary>
    /// Creates a compatibility failure for an unsupported value-type operation.<br/>
    /// </summary>
    /// <param name="message">Developer-facing failure detail.<br/></param>
    /// <param name="inner">Underlying exception that caused the failure.<br/></param>
    public UnsupportedValueTypeException(string message, Exception inner)
        : base(message, inner) { }
}}
