using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace SmartVoiceAgent.Ui.Converters;

/// <summary>
/// Turns an icon resource name such as <c>IconFolder</c> into its geometry, so a view model can pick an icon.
/// </summary>
public sealed class IconResourceConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string ?? parameter as string;
        return key is not null
            && Avalonia.Application.Current is { } application
            && application.TryGetResource(key, application.ActualThemeVariant, out var resource)
            && resource is Geometry geometry
                ? geometry
                : null;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return OneWayConverter.ConvertBackNoOp();
    }
}
