using Fasterflect;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace AbraxasDB
{
    internal static class Utils
    {

        public static string GetPropertyPath(this Expression exp, bool allowExtensionMethods = false)
        {
            if (exp is LambdaExpression lambda)
                exp = lambda.Body;

            var members = new List<string>(4);

            if (!TryUnwind(exp, members, allowExtensionMethods))
                throw new InvalidOperationException("Expression does not represent a simple property path or non-void parameterless method calls.");

            members.Reverse();
            return $".{string.Join('.', members)}";
        }

        public static bool TryUnwind(this Expression exp, List<string> members, bool allowExtensionMethods)
        {
            // Strip all conversions/null-forgiving at every level
            while (exp is UnaryExpression u &&
                (u.NodeType == ExpressionType.Convert ||
                 u.NodeType == ExpressionType.ConvertChecked))
            {
                exp = u.Operand;
            }

            // Base: x
            if (exp is ParameterExpression)
                return true;

            // Property
            if (exp is MemberExpression mex)
            {
                members.Add(mex.Member.Name);
                return TryUnwind(mex.Expression!, members, allowExtensionMethods);
            }

            // Methods
            if (exp is MethodCallExpression mcall &&
                mcall.Method.ReturnType != typeof(void))
            {
                // Instance parameterless method
                if (mcall.Object != null && mcall.Arguments.Count == 0)
                {
                    members.Add(mcall.Method.Name);
                    return TryUnwind(mcall.Object, members, allowExtensionMethods);
                }

                // Extension method (static method with 1 argument that is the receiver)
                if (mcall.Object == null && mcall.Arguments.Count == 1)
                {
                    if (!allowExtensionMethods)
                        return false;   // Strict mode: reject extension methods

                    members.Add(mcall.Method.Name);
                    return TryUnwind(mcall.Arguments[0], members, allowExtensionMethods);
                }

                return false;
            }

            // Delegate invocation (rare, but legal)
            if (exp is InvocationExpression inv)
                return TryUnwind(inv.Expression, members, allowExtensionMethods);

            return false;
        }


        // ---------------------------------------------------------------------
        // Core helper: pack DateTime into structured 64-bit value
        // Layout:
        //  YearOffset (14 bits, @50)  = (year) & 0x3FFF
        //  Month      (4 bits,  @46)  = 1-12
        //  Day        (5 bits,  @41)
        //  Hour       (5 bits,  @36)
        //  Minute     (6 bits,  @30)
        //  Second     (6 bits,  @24)
        //  Millisecond(10 bits, @14)
        //  NanoSlice  (10 bits, @4)  = (ticks % 10_000) / 10   (0–999)
        //  Lower 4 bits reserved
        // ---------------------------------------------------------------------
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong MakeStructuredDateTime(this DateTime dt)
        {
            long ticks = dt.Ticks;

            int year = dt.Year;
            int month = dt.Month;
            int day = dt.Day;
            int hour = dt.Hour;
            int minute = dt.Minute;
            int second = dt.Second;

            int ms = (int)((ticks / 10_000) % 1000);
            int ns100 = (int)(ticks % 10_000);
            int ns_slice = ns100 / 10;              // 0–999

            int dow = (int)dt.DayOfWeek & 0b111; // fits 0–6 cleanly

            return
                ((ulong)year << 50) |
                ((ulong)month << 46) |
                ((ulong)day << 41) |
                ((ulong)hour << 36) |
                ((ulong)minute << 30) |
                ((ulong)second << 24) |
                ((ulong)ms << 14) |
                ((ulong)ns_slice << 4) |
                ((ulong)dow << 1);                 // 3 bits — leaves bit0 empty
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong MakeStructuredTimeOnly(this TimeOnly t)
        {
            long ticks = t.Ticks;

            int hour = t.Hour;
            int minute = t.Minute;
            int second = t.Second;

            int ms = (int)((ticks / 10_000) % 1000);
            int ns100 = (int)(ticks % 10_000);
            int nsSlice = ns100 / 10;

            return
                ((ulong)hour << 36) |
                ((ulong)minute << 30) |
                ((ulong)second << 24) |
                ((ulong)ms << 14) |
                ((ulong)nsSlice << 4);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong MakeStructuredDateOnly(this DateOnly d)
        {
            int year = d.Year;
            int month = d.Month;
            int day = d.Day;
            int dow = ((int)d.DayOfWeek) & 0b111;

            return
                ((ulong)year << 50) |
                ((ulong)month << 46) |
                ((ulong)day << 41) |
                ((ulong)dow << 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DateTime UnpackStructuredDateTime(ulong v)
        {
            int year = (int)((v >> 50) & 0x3FFF);
            int month = (int)((v >> 46) & 0xF);
            int day = (int)((v >> 41) & 0x1F);
            int hour = (int)((v >> 36) & 0x1F);
            int minute = (int)((v >> 30) & 0x3F);
            int second = (int)((v >> 24) & 0x3F);
            int ms = (int)((v >> 14) & 0x3FF);
            int nsSlice = (int)((v >> 4) & 0x3FF);
            int ticksExtra = nsSlice * 10;

            if (year == 0 || month == 0 || day == 0)
            {
                // Treat as time-only; use a sentinel date
                var dt = new DateTime(1, 1, 1, hour, minute, second, ms, DateTimeKind.Utc);
                return dt.AddTicks(ticksExtra);
            }

            var baseDt = new DateTime(year, month, day, hour, minute, second, ms, DateTimeKind.Utc);
            return baseDt.AddTicks(ticksExtra);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void UnpackComponents(
    ulong v,
    out int year,
    out int month,
    out int day,
    out int hour,
    out int minute,
    out int second,
    out int ms,
    out int ticksExtra)
        {
            year = (int)((v >> 50) & 0x3FFF);
            month = (int)((v >> 46) & 0xF);
            day = (int)((v >> 41) & 0x1F);
            hour = (int)((v >> 36) & 0x1F);
            minute = (int)((v >> 30) & 0x3F);
            second = (int)((v >> 24) & 0x3F);
            ms = (int)((v >> 14) & 0x3FF);

            int nsSlice = (int)((v >> 4) & 0x3FF); // 0–999
            ticksExtra = nsSlice * 10;            // 100ns units
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DateOnly UnpackStructuredDateOnly(ulong v)
        {
            UnpackComponents(v,
                out int year, out int month, out int day,
                out int _, out int _, out int _, out int _, out int _);

            // time-only encoded values produce 0/0/0 — we return the minimal legal date?
            if (year == 0 || month == 0 || day == 0)
                return new DateOnly(1, 1, 1); // sentinel; adjust if you want exceptions

            return new DateOnly(year, month, day);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TimeOnly UnpackStructuredTimeOnly(ulong v)
        {
            UnpackComponents(v,
                out int _, out int _, out int _,    // ignore Y/M/D
                out int hour, out int minute, out int second,
                out int ms, out int ticksExtra);

            var t = new TimeOnly(hour, minute, second, ms);
            return t.Add(TimeSpan.FromTicks(ticksExtra));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TimeSpan UnpackStructuredTimeSpan(ulong v)
        {
            UnpackComponents(v,
                out int _, out int _, out int _,
                out int hour, out int minute, out int second,
                out int ms, out int ticksExtra);

            long ticks =
                (long)hour * TimeSpan.TicksPerHour +
                (long)minute * TimeSpan.TicksPerMinute +
                (long)second * TimeSpan.TicksPerSecond +
                (long)ms * TimeSpan.TicksPerMillisecond +
                ticksExtra;

            return new TimeSpan(ticks);
        }


    }
}
