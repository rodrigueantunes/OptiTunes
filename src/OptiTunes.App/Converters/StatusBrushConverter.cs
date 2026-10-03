using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using OptiTunes.Core.Models;
using OptiTunes.Core.Validation;

namespace OptiTunes.App.Converters;

/// <summary>CheckStatus / IssueSeverity / bool (réussi) → pinceau du thème.</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            CheckStatus.Ok => "SuccessBrush",
            CheckStatus.Warning => "WarningBrush",
            CheckStatus.Error => "DangerBrush",
            IssueSeverity.Info => "PrimaryBrush",
            IssueSeverity.Warning => "WarningBrush",
            IssueSeverity.Error => "DangerBrush",
            true => "SuccessBrush",
            false => "DangerBrush",
            _ => "MutedBrush"
        };
        return Application.Current.Resources[key];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Taux d'occupation → bleu, orange au-delà de 90 %, rouge au-delà de 100 %.</summary>
public sealed class RateBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var rate = value is double d ? d : 0;
        var key = rate > 100.01 ? "DangerBrush" : rate >= 90 ? "WarningBrush" : "PrimaryBrush";
        return Application.Current.Resources[key];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true → Collapsed, false → Visible.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
