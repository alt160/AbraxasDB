using System;
using System.Numerics;
using System.Text;
using Inheto;

namespace AbraxasDB
{
    public partial class RecordStore<T>
    {
        /// <summary>
        /// Provides an allocation-free typed view over one selected value in the reader's current borrowed row.<br/>
        /// The accessor carries only the current reader engine and one cached Inheto path plan; it does not materialize a row container or retain payload bytes.<br/>
        /// Built-in <c>As...</c> members name intrinsic value families; <see cref="As{TValue}"/> requests an exact declared CLR type, and <see cref="AsCustom{TValue}"/> requires an existing Inheto binary deserializer registration.<br/>
        /// The accessor expires when its owning reader advances, skips, resets, or is disposed.<br/>
        /// </summary>
        public readonly ref struct ValueAccessor
        {
            private readonly ReaderCore core;
            private readonly PropPathCheck path;

            /// <summary>
            /// Creates one current-row value accessor over an already checked path plan.<br/>
            /// No developer code is invoked and no payload value is decoded by construction.<br/>
            /// </summary>
            /// <param name="core">Positioned reader engine that owns the borrowed row lifetime.<br/></param>
            /// <param name="path">Cached type-level Inheto path plan for the selected value.<br/></param>
            internal ValueAccessor(ReaderCore core, PropPathCheck path)
            {
                this.core = core;
                this.path = path;
            }

            /// <summary>Gets the canonical Inheto path represented by this accessor.<br/></summary>
            public string Path => path.CanonicalPath;

            /// <summary>
            /// Gets whether the current serialized row has no stored source from which this value can be resolved.<br/>
            /// An explicitly stored null remains distinct and is reported by <see cref="IsNull"/>.<br/>
            /// </summary>
            public bool IsMissing
                => core.Binary.CheckPropPath(path).Kind == PropPathBlobMatchKind.SerializedSourceMissing;

            /// <summary>
            /// Gets whether the current serialized row explicitly routes this value or its deepest serialized source through Inheto's null representation.<br/>
            /// Missing values remain distinct and are reported by <see cref="IsMissing"/>.<br/>
            /// </summary>
            public bool IsNull
                => core.Binary.CheckPropPath(path).Kind == PropPathBlobMatchKind.SerializedSourceNull;

            /// <summary>Reads the current value as a Boolean without admitting unrelated CLR target types.<br/></summary>
            public bool? AsBoolean => ReadKnown<bool>();

            /// <summary>Reads the current value as an unsigned 8-bit integer.<br/></summary>
            public byte? AsByte => ReadKnown<byte>();

            /// <summary>Reads the current value as a signed 8-bit integer.<br/></summary>
            public sbyte? AsSByte => ReadKnown<sbyte>();

            /// <summary>Reads the current value as a signed 16-bit integer.<br/></summary>
            public short? AsInt16 => ReadKnown<short>();

            /// <summary>Reads the current value as an unsigned 16-bit integer.<br/></summary>
            public ushort? AsUInt16 => ReadKnown<ushort>();

            /// <summary>Reads the current value as a signed 32-bit integer.<br/></summary>
            public int? AsInt32 => ReadKnown<int>();

            /// <summary>Reads the current value as an unsigned 32-bit integer.<br/></summary>
            public uint? AsUInt32 => ReadKnown<uint>();

            /// <summary>Reads the current value as a signed 64-bit integer.<br/></summary>
            public long? AsInt64 => ReadKnown<long>();

            /// <summary>Reads the current value as an unsigned 64-bit integer.<br/></summary>
            public ulong? AsUInt64 => ReadKnown<ulong>();

            /// <summary>Reads the current value as a signed 128-bit integer.<br/></summary>
            public Int128? AsInt128 => ReadKnown<Int128>();

            /// <summary>Reads the current value as an unsigned 128-bit integer.<br/></summary>
            public UInt128? AsUInt128 => ReadKnown<UInt128>();

            /// <summary>Reads the current value as a 16-bit floating-point number.<br/></summary>
            public Half? AsHalf => ReadKnown<Half>();

            /// <summary>Reads the current value as a 32-bit floating-point number.<br/></summary>
            public float? AsSingle => ReadKnown<float>();

            /// <summary>Reads the current value as a 64-bit floating-point number.<br/></summary>
            public double? AsDouble => ReadKnown<double>();

            /// <summary>Reads the current value as a base-10 Decimal value.<br/></summary>
            public decimal? AsDecimal => ReadKnown<decimal>();

            /// <summary>Reads the current value as an arbitrary-width integer intrinsically supported by Inheto.<br/></summary>
            public BigInteger? AsBigInteger => ReadKnown<BigInteger>();

            /// <summary>Reads the current value as one UTF-16 code unit.<br/></summary>
            public char? AsChar => ReadKnown<char>();

            /// <summary>Reads the current value as one Unicode scalar value.<br/></summary>
            public Rune? AsRune => ReadKnown<Rune>();

            /// <summary>Reads the current value as a managed string, returning null for null or missing data.<br/></summary>
            public string? AsString => ReadKnown<string>();

            /// <summary>
            /// Reads the selected value as its exact declared CLR type without an object-valued result or a second conversion step.<br/>
            /// A reversible current index key supplies the value directly; otherwise the normal Inheto value read is used.<br/>
            /// The existing named <c>As...</c> members remain available for dynamic callers.<br/>
            /// </summary>
            /// <typeparam name="TValue">Exact declared type of the selected member path.<br/></typeparam>
            /// <returns>The typed current value, or null/default when the member is null or missing.<br/></returns>
            public TValue? As<TValue>() => ReadKnown<TValue>();

            /// <summary>Reads the current value as owned binary data, returning null for null or missing data.<br/></summary>
            public byte[]? AsBinary => ReadKnown<byte[]>();

            /// <summary>Reads the current value as a globally unique identifier.<br/></summary>
            public Guid? AsGuid => ReadKnown<Guid>();

            /// <summary>Reads the current value as a <see cref="DateTime"/> while preserving its stored Inheto clock and kind information.<br/></summary>
            public DateTime? AsDateTime => ReadKnown<DateTime>();

            /// <summary>Reads the current value as a <see cref="DateTimeOffset"/> while preserving the offset stored by Inheto.<br/></summary>
            public DateTimeOffset? AsDateTimeOffset => ReadKnown<DateTimeOffset>();

            /// <summary>Reads the current value as a calendar-only date.<br/></summary>
            public DateOnly? AsDateOnly => ReadKnown<DateOnly>();

            /// <summary>Reads the current value as a clock-only time.<br/></summary>
            public TimeOnly? AsTimeOnly => ReadKnown<TimeOnly>();

            /// <summary>Reads the current value as an elapsed duration.<br/></summary>
            public TimeSpan? AsTimeSpan => ReadKnown<TimeSpan>();

            /// <summary>Reads the current value as a URI intrinsically supported by Inheto.<br/></summary>
            public Uri? AsUri => ReadKnown<Uri>();

            /// <summary>Reads the current value as a version intrinsically supported by Inheto.<br/></summary>
            public Version? AsVersion => ReadKnown<Version>();

            /// <summary>Reads the current value as a two-component SIMD vector.<br/></summary>
            public Vector2? AsVector2 => ReadKnown<Vector2>();

            /// <summary>Reads the current value as a three-component SIMD vector.<br/></summary>
            public Vector3? AsVector3 => ReadKnown<Vector3>();

            /// <summary>Reads the current value as a four-component SIMD vector.<br/></summary>
            public Vector4? AsVector4 => ReadKnown<Vector4>();

            /// <summary>Reads the current value as a complex number.<br/></summary>
            public Complex? AsComplex => ReadKnown<Complex>();

            /// <summary>Reads the current value as a quaternion.<br/></summary>
            public Quaternion? AsQuaternion => ReadKnown<Quaternion>();

            /// <summary>Reads the current value as a geometric plane.<br/></summary>
            public Plane? AsPlane => ReadKnown<Plane>();

            /// <summary>Reads the current value as a 3-by-2 matrix.<br/></summary>
            public Matrix3x2? AsMatrix3x2 => ReadKnown<Matrix3x2>();

            /// <summary>Reads the current value as a 4-by-4 matrix.<br/></summary>
            public Matrix4x4? AsMatrix4x4 => ReadKnown<Matrix4x4>();

            /// <summary>
            /// Borrows the natural UTF-8 payload of a stored string without decoding or copying it.<br/>
            /// The span expires when the owning reader advances, skips, resets, or is disposed.<br/>
            /// Null and missing values throw; use <see cref="TryGetUtf8Span"/> when either state is expected.<br/>
            /// </summary>
            public ReadOnlySpan<byte> AsUtf8Span
            {
                get
                {
                    EnsureCompatible(typeof(string));
                    if (core.Binary.TryReadStringUtf8Payload(path.CanonicalPath, core.Options, out ReadOnlyMemory<byte> payload))
                        return payload.Span;
                    throw new InvalidOperationException($"Value path '{path.CanonicalPath}' is null or missing in the current row.");
                }
            }

            /// <summary>
            /// Attempts to borrow the natural UTF-8 payload of a stored string without decoding or copying it.<br/>
            /// A present empty string succeeds with an empty span; null and missing values return <see langword="false"/>.<br/>
            /// </summary>
            /// <param name="value">Receives the current borrowed UTF-8 bytes when present.<br/></param>
            /// <returns><see langword="true"/> for a present string; otherwise <see langword="false"/>.<br/></returns>
            public bool TryGetUtf8Span(out ReadOnlySpan<byte> value)
            {
                EnsureCompatible(typeof(string));
                if (core.Binary.TryReadStringUtf8Payload(path.CanonicalPath, core.Options, out ReadOnlyMemory<byte> payload))
                {
                    value = payload.Span;
                    return true;
                }
                value = default;
                return false;
            }

            /// <summary>
            /// Reads the current value as one of Abraxas's explicitly admitted intrinsic numeric types.<br/>
            /// Implementing generic-math interfaces is not sufficient: the requested CLR type must belong to the closed built-in numeric set shared with the named <c>As...</c> accessors.<br/>
            /// </summary>
            /// <typeparam name="TNumber">Exact intrinsic numeric CLR type expected at this value path.<br/></typeparam>
            /// <returns>The current numeric value, or null/default for null or missing data.<br/></returns>
            public TNumber? AsNumeric<TNumber>() where TNumber : struct, INumberBase<TNumber>
            {
                if (!IsKnownNumeric(typeof(TNumber)))
                    throw new NotSupportedException($"Numeric value type '{typeof(TNumber).FullName}' is not an intrinsic Abraxas binary numeric type.");
                return ReadKnown<TNumber>();
            }

            /// <summary>
            /// Reads the current value as the exact enum type declared by its path.<br/>
            /// The enum's underlying integral representation is handled by Inheto; unrelated enum types are rejected before value conversion.<br/>
            /// </summary>
            /// <typeparam name="TEnum">Exact enum CLR type expected at this value path.<br/></typeparam>
            /// <returns>The current enum value, or null/default for null or missing data.<br/></returns>
            public TEnum? AsEnum<TEnum>() where TEnum : struct, Enum
                => ReadKnown<TEnum>();

            /// <summary>
            /// Reads an application-defined value through the binary deserializer already registered with this store's Inheto options.<br/>
            /// Member-specific registration is preferred over type-wide registration, matching ordinary Inheto deserialization and Abraxas custom-index adoption.<br/>
            /// No fallback object activation or arbitrary CLR projection is attempted when a registration is absent.<br/>
            /// </summary>
            /// <typeparam name="TValue">Exact application value type declared by this path and registered with Inheto.<br/></typeparam>
            /// <returns>The converted application value, or null/default for null or missing data.<br/></returns>
            public TValue? AsCustom<TValue>()
            {
                EnsureCompatible(typeof(TValue));
                if (!core.Options.HasBinaryDeserializer(typeof(TValue), path.CanonicalPath))
                {
                    throw new InvalidOperationException(
                        $"Value path '{path.CanonicalPath}' has no member-specific or type-wide Inheto binary deserializer registered for '{typeof(TValue).FullName}'.");
                }
                return ReadPrepared<TValue>();
            }

            /// <summary>Reads one known intrinsic type after enforcing exact declared-path compatibility.<br/></summary>
            private TValue? ReadKnown<TValue>()
            {
                EnsureCompatible(typeof(TValue));
                if (core.TryProjectCurrentIndexValue(path.CanonicalPath, out TValue? projected))
                    return projected;
                return ReadPrepared<TValue>();
            }

            /// <summary>Executes the cached Inheto path plan and preserves null or missing states as the requested type's default value.<br/></summary>
            private TValue? ReadPrepared<TValue>()
            {
                PropPathReadState state = core.Binary.TryReadPropPath(path, out TValue? value, core.Options, throwOnInvocationError: true);
                return state switch
                {
                    PropPathReadState.Value => value,
                    PropPathReadState.Null or PropPathReadState.SourceNull or PropPathReadState.SourceMissing => default,
                    PropPathReadState.Invalid => throw new InvalidOperationException($"Value path '{path.CanonicalPath}' is not valid for '{typeof(T).FullName}'."),
                    PropPathReadState.InvocationFailed => throw new InvalidOperationException($"Value path '{path.CanonicalPath}' could not be evaluated for the current row."),
                    _ => throw new InvalidOperationException($"Value path '{path.CanonicalPath}' returned unsupported read state '{state}'.")
                };
            }

            /// <summary>Requires the declared path result to match the selected built-in or registered application type.<br/></summary>
            private void EnsureCompatible(Type expectedType)
            {
                Type actual = Nullable.GetUnderlyingType(path.ResultType!) ?? path.ResultType!;
                Type expected = Nullable.GetUnderlyingType(expectedType) ?? expectedType;
                if (actual != expected)
                {
                    throw new InvalidCastException(
                        $"Value path '{path.CanonicalPath}' resolves to '{actual.FullName}', not requested '{expected.FullName}'.");
                }
            }

            /// <summary>Tests membership in the deliberately closed intrinsic numeric set.<br/></summary>
            private static bool IsKnownNumeric(Type type)
                => type == typeof(byte) || type == typeof(sbyte) ||
                   type == typeof(short) || type == typeof(ushort) ||
                   type == typeof(int) || type == typeof(uint) ||
                   type == typeof(long) || type == typeof(ulong) ||
                   type == typeof(Int128) || type == typeof(UInt128) ||
                   type == typeof(Half) || type == typeof(float) ||
                   type == typeof(double) || type == typeof(decimal) ||
                   type == typeof(BigInteger);
        }
    }
}
