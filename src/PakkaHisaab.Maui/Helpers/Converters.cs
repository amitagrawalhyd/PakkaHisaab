using System.Globalization;

namespace PakkaHisaab.Maui.Helpers;

/// <summary>Converts a resource KEY (bound from a view model) into localized text.
/// Because conversion runs on each rebind, pair with language-change refreshes where needed.</summary>
public class TranslateKeyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key ? LocalizationResourceManager.Instance[key] : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class InvertBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;
}

/// <summary>True when the string is neither null nor empty — handy for optional labels.</summary>
public class NotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && !string.IsNullOrEmpty(s);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
