using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClothingStore.Desktop.Services;
using ClothingStore.Desktop.ViewModels.Dialogs;

namespace ClothingStore.Desktop.Views.Dialogs;

public partial class LabelPrintView : UserControl
{
    private LabelPrintViewModel? _vm;

    public LabelPrintView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as LabelPrintViewModel);
        Loaded += (_, _) => AddBox.Focus();
        Unloaded += (_, _) => Attach(null);
    }

    private void Attach(LabelPrintViewModel? vm)
    {
        if (_vm is not null) _vm.PreviewChanged -= OnPreviewChanged;
        _vm = vm;
        if (_vm is not null) _vm.PreviewChanged += OnPreviewChanged;
        RenderPreview();
    }

    private void OnPreviewChanged(object? sender, EventArgs e) => RenderPreview();

    /// <summary>Draws the first label exactly as it will print (same code path), scaled to fit the preview box.</summary>
    private void RenderPreview()
    {
        if (_vm is null)
        {
            PreviewHost.Content = null;
            return;
        }
        var o = _vm.Options;
        var label = PrintService.CreateLabel(_vm.PreviewLabel, o, o.WidthPx, o.HeightPx);
        PreviewHost.Content = new Viewbox
        {
            Stretch = Stretch.Uniform,
            Child = new Border
            {
                BorderBrush = Brushes.Silver,
                BorderThickness = new Thickness(o.ShowBorder ? 0 : 0.5),
                Child = label,
            },
        };
    }
}
