using System.Globalization;

namespace Mailtide.UI;

public static class WindowLayout
{
    public const string PreferenceKey = "window.bounds";

    public static string Encode(int x, int y, double width, double height, bool maximized)
    {
        var culture = CultureInfo.InvariantCulture;
        return string.Join(
            ",",
            x.ToString(culture),
            y.ToString(culture),
            width.ToString("0.#", culture),
            height.ToString("0.#", culture),
            maximized ? "1" : "0");
    }

    public static bool TryParse(
        string? value,
        out int x,
        out int y,
        out double width,
        out double height,
        out bool maximized)
    {
        x = 0;
        y = 0;
        width = 0;
        height = 0;
        maximized = false;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(',');
        if (parts.Length != 5)
        {
            return false;
        }

        var culture = CultureInfo.InvariantCulture;
        if (!int.TryParse(parts[0], NumberStyles.Integer, culture, out x)
            || !int.TryParse(parts[1], NumberStyles.Integer, culture, out y)
            || !double.TryParse(parts[2], NumberStyles.Float, culture, out width)
            || !double.TryParse(parts[3], NumberStyles.Float, culture, out height)
            || (parts[4] is not "0" and not "1"))
        {
            return false;
        }

        maximized = parts[4] == "1";
        return width >= 720 && height >= 480;
    }
}
