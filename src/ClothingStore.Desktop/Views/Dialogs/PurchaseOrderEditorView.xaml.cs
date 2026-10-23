using System.Windows.Controls;
using System.Windows.Input;
using ClothingStore.Desktop.ViewModels;

namespace ClothingStore.Desktop.Views.Dialogs;

public partial class PurchaseOrderEditorView : UserControl
{
    public PurchaseOrderEditorView()
    {
        InitializeComponent();
    }

    private void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PurchaseOrderEditorViewModel vm) vm.AddResultCommand.Execute(null);
    }
}
