using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace JitenMPV.App.Converters;

public sealed class ParseEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => parameter is string s && value?.ToString() == s;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter is not string s) return BindingOperations.DoNothing;
        if (targetType.IsEnum) return Enum.Parse(targetType, s);
        return System.Convert.ChangeType(s, targetType, culture);
    }
}
