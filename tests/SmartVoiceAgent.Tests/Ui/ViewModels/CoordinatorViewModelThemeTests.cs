using Avalonia.Media;
using Avalonia.Styling;
using FluentAssertions;
using SmartVoiceAgent.Ui.ViewModels.PageModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed class CoordinatorViewModelThemeTests
{
    [Fact]
    public void LabelColor_WhenThemeChanges_RefreshesOnlineTextBrush()
    {
        var viewModel = new CoordinatorViewModel();

        viewModel.RefreshThemeColors(ThemeVariant.Dark);
        BrushColor(viewModel.LabelColor).Should().Be(Color.Parse("#FAFAFA"));

        viewModel.RefreshThemeColors(ThemeVariant.Light);
        BrushColor(viewModel.LabelColor).Should().Be(Color.Parse("#18181B"));
    }

    private static Color BrushColor(IBrush brush)
    {
        brush.Should().BeOfType<SolidColorBrush>();
        return ((SolidColorBrush)brush).Color;
    }
}
