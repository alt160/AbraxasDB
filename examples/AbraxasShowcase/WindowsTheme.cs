using System.Runtime.InteropServices;

namespace AbraxasShowcase;

/// <summary>Windows-derived colors used to accent the neutral Visual Studio-style dark showcase surfaces.<br/></summary>
internal sealed record WindowsThemePalette(string Accent, string AccentContrast, string Source);

/// <summary>Reads the current DWM colorization color without depending on browser or registry color interpretation.<br/></summary>
internal static class WindowsTheme
{
    /// <summary>Returns the current Windows accent with a safe accessible fallback when DWM does not provide one.<br/></summary>
    /// <returns>CSS colors and provenance for the packaged WebView2 UI.<br/></returns>
    internal static WindowsThemePalette Current()
    {
        const string fallback = "#0078D4";
        try
        {
            if (DwmGetColorizationColor(out uint color, out _) != 0) return new WindowsThemePalette(fallback, "#FFFFFF", "Windows fallback");
            int red = (int)((color >> 16) & 0xFF);
            int green = (int)((color >> 8) & 0xFF);
            int blue = (int)(color & 0xFF);
            string accent = $"#{red:X2}{green:X2}{blue:X2}";
            double luminance = (0.2126 * red + 0.7152 * green + 0.0722 * blue) / 255d;
            return new WindowsThemePalette(accent, luminance > 0.62 ? "#111111" : "#FFFFFF", "Windows DWM accent");
        }
        catch (DllNotFoundException) { return new WindowsThemePalette(fallback, "#FFFFFF", "Windows fallback"); }
        catch (EntryPointNotFoundException) { return new WindowsThemePalette(fallback, "#FFFFFF", "Windows fallback"); }
    }

    /// <summary>Retrieves the active Windows DWM colorization color as an ARGB value.<br/></summary>
    /// <param name="colorizationColor">Receives the current ARGB colorization value.<br/></param>
    /// <param name="opaqueBlend">Receives whether Windows requests an opaque blend.<br/></param>
    /// <returns>Zero on success; otherwise a DWM error code.<br/></returns>
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetColorizationColor(out uint colorizationColor, [MarshalAs(UnmanagedType.Bool)] out bool opaqueBlend);
}
