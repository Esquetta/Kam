using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SmartVoiceAgent.Ui.ViewModels.PageModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SmartVoiceAgent.Ui.Views;

public partial class PluginsView : UserControl
{
    // Below this page width the search moves under the filters.
    private const double NarrowWidth = 620;

    public PluginsView()
    {
        InitializeComponent();
        Page.SizeChanged += (_, e) => Page.Classes.Set("narrow", e.NewSize.Width < NarrowWidth);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is PluginsViewModel viewModel)
        {
            viewModel.PickFolderAsync = PickFolderAsync;
        }
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanPickFolder: true } storage)
        {
            return null;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }
}
