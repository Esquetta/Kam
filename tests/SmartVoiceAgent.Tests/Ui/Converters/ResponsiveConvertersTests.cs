using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Controls;
using Avalonia.Media;
using FluentAssertions;
using SmartVoiceAgent.Ui.Converters;
using SmartVoiceAgent.Ui.Services;
using System.Globalization;
using System.Reflection;

namespace SmartVoiceAgent.Tests.Ui.Converters;

public class ResponsiveConvertersTests
{
    public static TheoryData<IValueConverter> OneWayConverters =>
    [
        new ResponsiveColumnConverter(),
        new BoolToStatusBrushConverter(),
        new StringIsNotNullOrEmptyConverter(),
        new CompactModeConverter(),
        new MediumModeConverter(),
        new ExpandedModeConverter(),
        new LogPanelVisibilityConverter(),
        new LogPanelWidthConverter(),
        new ResponsiveScaleConverter(),
        new ResponsiveFontSizeConverter(),
        new ReducedMotionConverter(),
        new BoolToOpacityConverter(),
        new BoolToBoxShadowConverter(),
        new BoolToStatusTextConverter(),
        new StringEqualsConverter(),
        new BrushToGlowBrushConverter()
    ];

    [Theory]
    [MemberData(nameof(OneWayConverters))]
    public void ConvertBack_ForOneWayConverters_IsSafeNoOp(IValueConverter converter)
    {
        var result = converter.ConvertBack(
            value: true,
            targetType: typeof(object),
            parameter: null,
            culture: CultureInfo.InvariantCulture);

        result.Should().BeSameAs(BindingOperations.DoNothing);
    }

    [Theory]
    [InlineData(900, 0)]
    [InlineData(1280, 400)]
    [InlineData(1600, 520)]
    public void LogPanelWidthConverter_Convert_UsesCalmerRailWidths(double windowWidth, double expectedWidth)
    {
        SetWindowWidth(windowWidth);
        var converter = new LogPanelWidthConverter();

        var result = converter.Convert(
            value: true,
            targetType: typeof(GridLength),
            parameter: null,
            culture: CultureInfo.InvariantCulture);

        result.Should().BeOfType<GridLength>();
        ((GridLength)result).Value.Should().Be(expectedWidth);
    }

    [Theory]
    [InlineData(900, 1)]
    [InlineData(1024, 2)]
    [InlineData(1199, 2)]
    [InlineData(1200, 3)]
    [InlineData(1280, 3)]
    [InlineData(1366, 3)]
    [InlineData(1440, 3)]
    public void WindowStateManager_GridColumns_KeepsDesktopCommandCenterCompact(double windowWidth, int expectedColumns)
    {
        SetWindowWidth(windowWidth);

        WindowStateManager.Instance.GridColumns.Should().Be(expectedColumns);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BoolToStatusBrushConverter_Convert_WhenResourceIsMissing_UsesTransparentFallback(bool value)
    {
        var converter = new BoolToStatusBrushConverter();

        var result = converter.Convert(
            value: value,
            targetType: typeof(IBrush),
            parameter: "MissingTrueBrush|MissingFalseBrush",
            culture: CultureInfo.InvariantCulture);

        result.Should().BeSameAs(Brushes.Transparent);
    }

    private static void SetWindowWidth(double width)
    {
        typeof(WindowStateManager)
            .GetProperty("WindowWidth", BindingFlags.Instance | BindingFlags.Public)!
            .GetSetMethod(nonPublic: true)!
            .Invoke(WindowStateManager.Instance, [width]);
    }
}
