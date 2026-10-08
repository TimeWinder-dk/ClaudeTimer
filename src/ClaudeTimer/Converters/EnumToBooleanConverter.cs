using System.Globalization;
using System.Windows.Data;

namespace ClaudeTimer.Converters;

/// <summary>Binder en gruppe RadioButtons til en enum-værdi via ConverterParameter.</summary>
public sealed class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is string name && value.ToString() == name;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is string name
            ? Enum.Parse(targetType, name)
            : System.Windows.Data.Binding.DoNothing;
}
