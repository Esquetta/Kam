using Avalonia.Data;
using Avalonia.Data.Converters;
using FluentAssertions;
using SmartVoiceAgent.Ui.Converters;
using System.Globalization;

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
}
