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
    // A settings folder of its own, so widths the developer dragged in the app do not change these results.
    private static SmartVoiceAgent.Ui.ViewModels.MainWindowViewModel CreateViewModel() =>
        new(new JsonSettingsService(Path.Combine(Path.GetTempPath(), "kam-responsive-tests", Guid.NewGuid().ToString("N"))));

    public static TheoryData<IValueConverter> OneWayConverters =>
    [
        new ResponsiveColumnConverter(),
        new BoolToStatusBrushConverter(),
        new StringIsNotNullOrEmptyConverter(),
        new CompactModeConverter(),
        new MediumModeConverter(),
        new ExpandedModeConverter(),
        new LogPanelVisibilityConverter(),
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
    [InlineData(1279, 0)]
    [InlineData(1280, 290)]
    [InlineData(1600, 290)]
    public void ActivityPanelWidth_UsesCalmerRailWidthsBesideTheChat(double windowWidth, double expectedWidth)
    {
        SetWindowWidth(windowWidth);
        var viewModel = CreateViewModel();

        viewModel.ActivityPanelWidth.Value.Should().Be(expectedWidth);
        viewModel.IsActivityPanelShown.Should().Be(expectedWidth > 0);
    }

    [Fact]
    public void ActivityPanel_ClosesAndReopensFromItsToggle()
    {
        SetWindowWidth(1600);
        var viewModel = CreateViewModel();

        viewModel.ToggleActivityPanelCommand.Execute(null);

        viewModel.IsActivityPanelShown.Should().BeFalse();
        viewModel.ActivityPanelWidth.Value.Should().Be(0);

        viewModel.ToggleActivityPanelCommand.Execute(null);

        viewModel.IsActivityPanelShown.Should().BeTrue();
    }

    [Fact]
    public void ActivityPanel_OpensOnNarrowWindowsWhenAsked()
    {
        SetWindowWidth(1100);
        var viewModel = CreateViewModel();
        viewModel.IsActivityPanelShown.Should().BeFalse();

        viewModel.ToggleActivityPanelCommand.Execute(null);

        viewModel.IsActivityPanelShown.Should().BeTrue();
        viewModel.ActivityPanelWidth.Value.Should().Be(290);

        viewModel.ToggleActivityPanelCommand.Execute(null);

        viewModel.IsActivityPanelShown.Should().BeFalse();
        viewModel.ActivityPanelWidth.Value.Should().Be(0);
    }

    [Theory]
    [InlineData(800)]
    [InlineData(1000)]
    public void ActivityPanel_StaysClosedWhereTheThreadWouldBeTooNarrow(double width)
    {
        SetWindowWidth(width);
        var viewModel = CreateViewModel();

        viewModel.CanOpenActivityPanel.Should().BeFalse("rail, list and a 290 px pane would leave the thread too little room");
        viewModel.ToggleActivityPanelCommand.Execute(null);

        viewModel.IsActivityPanelShown.Should().BeFalse();
        viewModel.ActivityPanelWidth.Value.Should().Be(0);
    }

    [Fact]
    public void ActivityPanel_PinFromANarrowWindowEndsWhenTheWindowGrows()
    {
        SetWindowWidth(1100);
        var viewModel = CreateViewModel();
        viewModel.ToggleActivityPanelCommand.Execute(null);
        viewModel.IsActivityPanelShown.Should().BeTrue();

        SetWindowWidth(1600);
        SetWindowWidth(1100);

        viewModel.IsActivityPanelShown.Should().BeFalse();
        viewModel.CanOpenActivityPanel.Should().BeTrue();
    }

    [Fact]
    public void ActivityPanel_StaysOffTheThreadBelow760Px()
    {
        SetWindowWidth(1100);
        var viewModel = CreateViewModel();
        viewModel.ToggleActivityPanelCommand.Execute(null);

        SetWindowWidth(700);

        viewModel.IsActivityPanelShown.Should().BeFalse();
        viewModel.CanOpenActivityPanel.Should().BeFalse();
        SetWindowWidth(1600);
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
