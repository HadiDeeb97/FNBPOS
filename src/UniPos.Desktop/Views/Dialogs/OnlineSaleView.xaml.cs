using System.Windows.Controls;

namespace UniPos.Desktop.Views.Dialogs;

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
