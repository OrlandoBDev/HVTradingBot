using System.Globalization;
using HVTradingBot.App.Core.Startup;

namespace HVTradingBot.App.Pages;

/// <summary>Step state → symbol on the startup screen.</summary>
public sealed class StepStateToSymbolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        StepState.Running => "◐",
        StepState.Succeeded => "✓",
        StepState.Failed => "✕",
        _ => "○"
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Step state → symbol color.</summary>
public sealed class StepStateToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        StepState.Running => Color.FromArgb("#3B82F6"),
        StepState.Succeeded => Color.FromArgb("#22C55E"),
        StepState.Failed => Color.FromArgb("#EF4444"),
        _ => Color.FromArgb("#94A3B8")
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
