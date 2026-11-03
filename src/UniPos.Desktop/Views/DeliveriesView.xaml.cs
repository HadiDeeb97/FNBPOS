using System.Windows.Controls;

namespace UniPos.Desktop.Views;

public partial class DeliveriesView : UserControl
{
    public DeliveriesView()
    {
        InitializeComponent();
        Loaded += (_, _) => ScanBox.Focus(); // ready for the scanner
    }
}
