using System.Globalization;

namespace RoboXopy.Core.Formatting;

public static class ByteSize
{
    /// <summary>e.g. "512 B", "1.2 MB", "38 GB" (binary units).</summary>
    public static string Format(long bytes, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        var unit = 0;
        while (Math.Abs(value) >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Create(culture, $"{bytes:N0} B")
            : string.Create(culture, $"{value:0.#} {units[unit]}");
    }
}
