using System.Globalization;

namespace Mailtide.UI;

public static class PaneLayout
{
    public const string PreferenceKey = "shell.panes";

    public const double MinNav = 160;
    public const double MinList = 220;
    public const double MaxNav = 640;
    public const double MaxList = 720;

    public static string Encode(double navWidth, double listWidth)
    {
        var culture = CultureInfo.InvariantCulture;
        return navWidth.ToString("0.#", culture) + "," + listWidth.ToString("0.#", culture);
    }

    public static bool TryParse(string? value, out double navWidth, out double listWidth)
    {
        navWidth = 0;
        listWidth = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(',');
        if (parts.Length != 2)
        {
            return false;
        }

        var culture = CultureInfo.InvariantCulture;
        if (!double.TryParse(parts[0], NumberStyles.Float, culture, out navWidth)
            || !double.TryParse(parts[1], NumberStyles.Float, culture, out listWidth))
        {
            return false;
        }

        if (navWidth < MinNav || listWidth < MinList || navWidth > MaxNav || listWidth > MaxList)
        {
            return false;
        }

        return true;
    }
}
