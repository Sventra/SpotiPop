using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SpotiPop;

/// <summary>Corner radius that exactly rounds a bar to a capsule (half its height); "left" rounds only the left end.</summary>
public sealed class PillRadiusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var radius = value is double height ? height / 2 : 0;
        return parameter as string == "left" ? new CornerRadius(radius, 0, 0, radius) : new CornerRadius(radius);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
