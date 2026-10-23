using System.Windows.Controls;

namespace ClothingStore.Desktop.Views.Dialogs;

public partial class OnlineSaleView : UserControl
{
    public OnlineSaleView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            // Ready to scan the courier's barcode straight away.
            ReferenceBox.Focus();
            ReferenceBox.SelectAll();
        };
    }
}
