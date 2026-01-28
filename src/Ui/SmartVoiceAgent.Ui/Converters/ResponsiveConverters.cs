using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SmartVoiceAgent.Ui.Services;
using System;
using System.Globalization;

namespace SmartVoiceAgent.Ui.Converters;

/// <summary>
/// Converts a value based on the current responsive breakpoint
/// </summary>
public class ResponsiveColumnConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Returns number of columns based on current breakpoint
        return WindowStateManager.Instance.GridColumns;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Returns a value based on whether the window is in compact mode
/// </summary>
public class CompactModeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return WindowStateManager.Instance.IsCompact;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Returns a value based on whether the window is in medium mode
/// </summary>
public class MediumModeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return WindowStateManager.Instance.IsMedium;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Returns a value based on whether the window is in expanded mode
/// </summary>
public class ExpandedModeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return WindowStateManager.Instance.IsExpanded;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Returns visibility based on log panel visibility
/// </summary>
public class LogPanelVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return WindowStateManager.Instance.IsLogPanelVisible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Returns GridLength for log panel column based on current breakpoint
/// </summary>
public class LogPanelWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var manager = WindowStateManager.Instance;
        
        if (!manager.IsLogPanelVisible)
            return new GridLength(0);
        
        if (manager.IsMedium)
            return new GridLength(320);
        
        return new GridLength(450);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Scales a numeric value based on window width
/// </summary>
public class ResponsiveScaleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double baseValue)
            return value ?? 0;

        var manager = WindowStateManager.Instance;
        
        // Scale down for compact mode
        if (manager.IsCompact)
            return baseValue * 0.7;
        
        // Slight scale down for medium
        if (manager.IsMedium)
            return baseValue * 0.85;
        
        return baseValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Returns a font size based on the current breakpoint
/// </summary>
public class ResponsiveFontSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double baseSize)
            return value ?? 12.0;

        var manager = WindowStateManager.Instance;
        
        if (manager.IsCompact)
            return Math.Max(baseSize * 0.75, 10); // Minimum 10pt
        
        if (manager.IsMedium)
            return baseSize * 0.9;
        
        return baseSize;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Conditionally applies animations based on ReducedMotion preference
/// </summary>
public class ReducedMotionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return WindowStateManager.Instance.ReducedMotion;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Inverts a boolean value
/// </summary>
public class BooleanNegationConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : false;
    }
}
