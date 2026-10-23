using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SkinForge.UI.Controls;

/// <summary>Shared value converters, referenced as {x:Static c:Conv.Name}.</summary>
public static class Conv
{
    public static readonly IValueConverter Vis = new FuncConverter(v => v is true ? Visibility.Visible : Visibility.Collapsed);
    public static readonly IValueConverter InvVis = new FuncConverter(v => v is true ? Visibility.Collapsed : Visibility.Visible);
    public static readonly IValueConverter Not = new FuncConverter(v => v is not true);
    public static readonly IValueConverter NullCollapsed = new FuncConverter(v => v == null || v is "" ? Visibility.Collapsed : Visibility.Visible);
    public static readonly IValueConverter NullVisible = new FuncConverter(v => v == null || v is "" ? Visibility.Visible : Visibility.Collapsed);
    public static readonly IValueConverter ZeroVisible = new FuncConverter(v => v is int i && i == 0 ? Visibility.Visible : Visibility.Collapsed);
    public static readonly IValueConverter NonZeroVisible = new FuncConverter(v => v is int i && i != 0 ? Visibility.Visible : Visibility.Collapsed);
    /// <summary>Two-way radio-button helper: checked when the bound value equals the parameter, writes the parameter back when checked.</summary>
    public static readonly IValueConverter Eq = new EqualsConverter();
    /// <summary>Visible when the bound value equals the parameter (tab panels).</summary>
    public static readonly IValueConverter EqVis = new FuncParamConverter((v, p) => Equals(v?.ToString(), p?.ToString()) ? Visibility.Visible : Visibility.Collapsed);
    /// <summary>Multi-binding helper: true when both values are equal (e.g. dot index vs selected index).</summary>
    public static readonly IMultiValueConverter MultiEq = new MultiEqualsConverter();
    /// <summary>Visible only when every bound boolean is true.</summary>
    public static readonly IMultiValueConverter AndVis = new AndVisibilityConverter();
}

public sealed class FuncConverter(Func<object?, object?> map) : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => map(value);
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Equals(value?.ToString(), parameter?.ToString());
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter == null) return Binding.DoNothing;
        if (targetType.IsEnum) return Enum.Parse(targetType, parameter.ToString()!);
        return System.Convert.ChangeType(parameter, targetType, culture);
    }
}

public sealed class MultiEqualsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => values.Length == 2 && Equals(values[0], values[1]);
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class AndVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => values.All(v => v is true) ? Visibility.Visible : Visibility.Collapsed;
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class FuncParamConverter(Func<object?, object?, object?> map) : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => map(value, parameter);
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
