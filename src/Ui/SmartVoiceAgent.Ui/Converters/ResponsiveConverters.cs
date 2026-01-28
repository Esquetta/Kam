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
/// Usage: Binding with ConverterParameter=baseValue (e.g., 260)
/// </summary>
public class ResponsiveScaleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Parse the base value from ConverterParameter
        if (!TryParseDouble(parameter, out var baseValue) || baseValue <= 0)
            return 0;

        var manager = WindowStateManager.Instance;
        double scale = 1.0;
        
        // Scale down for compact mode
        if (manager.IsCompact)
            scale = 0.7;
        // Slight scale down for medium
        else if (manager.IsMedium)
            scale = 0.85;
        
        var result = baseValue * scale;
        return Math.Max(result, 1); // Minimum 1 to avoid 0 or negative
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static bool TryParseDouble(object? value, out double result)
    {
        result = 0;
        if (value == null) return false;
        
        if (value is double d)
        {
            result = d;
            return true;
        }
        
        if (value is int i)
        {
            result = i;
            return true;
        }
        
        return double.TryParse(value.ToString(), out result);
    }
}

/// <summary>
/// Returns a font size based on the current breakpoint
/// Usage: Binding with ConverterParameter=baseFontSize (e.g., 56)
/// </summary>
public class ResponsiveFontSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Parse the base font size from ConverterParameter
        if (!TryParseDouble(parameter, out var baseSize) || baseSize <= 0)
            return 12.0; // Default fallback font size

        var manager = WindowStateManager.Instance;
        double scale = 1.0;
        
        if (manager.IsCompact)
            scale = 0.75;
        else if (manager.IsMedium)
            scale = 0.9;
        
        var result = baseSize * scale;
        return Math.Max(result, 10); // Minimum 10pt font size
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static bool TryParseDouble(object? value, out double result)
    {
        result = 0;
        if (value == null) return false;
        
        if (value is double d)
        {
            result = d;
            return true;
        }
        
        if (value is int i)
        {
            result = i;
            return true;
        }
        
        return double.TryParse(value.ToString(), out result);
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
