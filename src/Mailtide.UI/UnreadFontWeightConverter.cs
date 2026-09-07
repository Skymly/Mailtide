using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Mailtide.UI;

public sealed class UnreadFontWeightConverter : IValueConverter
{
    public static readonly UnreadFontWeightConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo? culture) =>
        value is false ? FontWeight.Bold : FontWeight.Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo? culture) =>
        throw new NotSupportedException();
}

public sealed class SubjectFallbackConverter : IValueConverter
{
    public static readonly SubjectFallbackConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo? culture) =>
        MailShellFormatting.Subject(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo? culture) =>
        throw new NotSupportedException();
}
