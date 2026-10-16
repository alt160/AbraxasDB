namespace AbraxasDB;

/// <summary>
/// Converts an application-owned binary payload into one semantic value without requiring a byte-array copy.<br/>
/// The payload is borrowed only for the callback duration; copy any bytes that must outlive the call.<br/>
/// Codec registration binds this callback once; link registration retains its existing typed-result adaptation.<br/>
/// </summary>
/// <typeparam name="TValue">The application value restored from the payload.<br/></typeparam>
/// <param name="payload">The complete borrowed payload for the registered value or link.<br/></param>
/// <returns>The decoded application value.<br/></returns>
public delegate TValue BinaryDecoder<TValue>(ReadOnlySpan<byte> payload);

/// <summary>Controls malformed text in a reproducible code-page-based binary slice.<br/></summary>
public enum BinaryTextFallback
{
    /// <summary>Uses exception fallback; malformed bytes do not satisfy the existing slice predicate.<br/></summary>
    Strict = 0,
    /// <summary>Uses the registered .NET encoding's ordinary replacement fallback.<br/></summary>
    Replacement = 1
}

/// <summary>Declares the physical DateTime representation in developer-owned binary data; no representation is inferred.<br/></summary>
public enum BinaryDateTimeEncoding
{
    /// <summary>Eight-byte big-endian calendar SDT, retaining the existing ten-tick time quantum and calendar fields.<br/></summary>
    CalendarSdt = 0,
    /// <summary>Eight-byte big-endian precision SDT, retaining individual .NET ticks.<br/></summary>
    PrecisionSdt = 1,
    /// <summary>Eight-byte little-endian signed .NET ticks interpreted as UTC; does not store DateTime.Kind.<br/></summary>
    DotNetTicks = 2
}

/// <summary>Declares the physical DateOnly representation in developer-owned binary data.<br/></summary>
public enum BinaryDateOnlyEncoding
{
    /// <summary>Eight-byte big-endian calendar SDT containing date fields.<br/></summary>
    CalendarSdt = 0,
    /// <summary>Eight-byte big-endian precision SDT containing date fields.<br/></summary>
    PrecisionSdt = 1,
    /// <summary>Four-byte little-endian .NET day number.<br/></summary>
    DotNetDayNumber = 2
}

/// <summary>Declares the physical TimeOnly representation in developer-owned binary data.<br/></summary>
public enum BinaryTimeOnlyEncoding
{
    /// <summary>Eight-byte big-endian calendar SDT with the existing ten-tick time quantum.<br/></summary>
    CalendarSdt = 0,
    /// <summary>Eight-byte big-endian precision SDT retaining individual .NET ticks.<br/></summary>
    PrecisionSdt = 1,
    /// <summary>Eight-byte little-endian signed .NET ticks within the valid time-of-day range.<br/></summary>
    DotNetTicks = 2
}

/// <summary>Declares the physical signed duration representation in developer-owned binary data.<br/></summary>
public enum BinaryTimeSpanEncoding
{
    /// <summary>Eight-byte big-endian ordered signed ticks, preserving the complete TimeSpan range.<br/></summary>
    OrderedTicks = 0,
    /// <summary>Eight-byte little-endian signed .NET ticks, preserving the complete TimeSpan range.<br/></summary>
    DotNetTicks = 1
}

/// <summary>
/// Declares a binary DateTimeOffset representation for comparisons against its UTC DateTime instant.<br/>
/// The existing condition operator exposes UTC DateTime rather than the original offset.<br/>
/// </summary>
public enum BinaryDateTimeOffsetEncoding
{
    /// <summary>Eight-byte big-endian calendar SDT of the UTC instant; the original offset is not stored.<br/></summary>
    CalendarSdtUtc = 0,
    /// <summary>Eight-byte big-endian precision SDT of the UTC instant; the original offset is not stored.<br/></summary>
    PrecisionSdtUtc = 1,
    /// <summary>Local .NET ticks and offset ticks as two little-endian signed 64-bit values; conditions compare their UTC instant.<br/></summary>
    DotNetTicksAndOffset = 2
}

/// <summary>Adapts delegate types at registration without adding a callback wrapper on the decode path.<br/></summary>
internal static class BinaryDecoderBinding
{
    /// <summary>
    /// Rebinds the original target/method pairs to Inheto's equivalent span signature once.<br/>
    /// Invocation order, final-result semantics and exception propagation are retained for multicast callbacks.<br/>
    /// MethodInfo's virtual binding operation also supports DynamicMethod callbacks; invocation performs no reflection.<br/>
    /// </summary>
    /// <typeparam name="TValue">The application value restored by both delegate contracts.<br/></typeparam>
    /// <param name="decoder">The validated application callback; its borrowed payload lifetime remains unchanged.<br/></param>
    /// <returns>A directly bound native callback with no forwarding lambda.<br/></returns>
    internal static Inheto.DeserializationOptions.DeserializerDelegate<TValue> ToInheto<TValue>(BinaryDecoder<TValue> decoder)
    {
        Delegate[] entries = decoder.GetInvocationList();
        var bound = (Inheto.DeserializationOptions.DeserializerDelegate<TValue>)entries[0].Method.CreateDelegate(
            typeof(Inheto.DeserializationOptions.DeserializerDelegate<TValue>), entries[0].Target);
        for (int i = 1; i < entries.Length; i++)
        {
            var next = (Inheto.DeserializationOptions.DeserializerDelegate<TValue>)entries[i].Method.CreateDelegate(
                typeof(Inheto.DeserializationOptions.DeserializerDelegate<TValue>), entries[i].Target);
            bound = (Inheto.DeserializationOptions.DeserializerDelegate<TValue>)Delegate.Combine(bound, next);
        }
        return bound;
    }
}
