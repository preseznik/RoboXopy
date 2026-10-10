using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RoboXopy.Converters;

/// <summary>Two-way converter between an enum value and a bool (for radio buttons / choice cards).</summary>
public sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is not null && string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is not null)
        {
            var enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            return Enum.Parse(enumType, parameter.ToString()!);
        }

        return Binding.DoNothing;
    }
}

/// <summary>Visible when the value equals the parameter (or one of several separated by '|'); inverted with Invert.</summary>
public sealed class EnumToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var options = parameter?.ToString()?.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        var match = value is not null && options.Contains(value.ToString(), StringComparer.Ordinal);
        return match ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public bool UseHidden { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (Invert) flag = !flag;
        return flag ? Visibility.Visible : UseHidden ? Visibility.Hidden : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility v ? (v == Visibility.Visible) ^ Invert : Binding.DoNothing;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Visible for non-null values (and non-empty strings / collections); inverted with Invert.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            ICollection c => c.Count > 0,
            _ => true,
        };
        return hasValue ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Visible when a count is greater than zero; inverted with Invert.</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value switch
        {
            int i => i,
            long l => (int)Math.Min(l, int.MaxValue),
            _ => 0,
        };
        return count > 0 ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>True when all bound values are true.</summary>
public sealed class AllTrueMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.All(v => v is true);

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
