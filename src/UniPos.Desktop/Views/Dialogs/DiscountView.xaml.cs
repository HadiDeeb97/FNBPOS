using System.Windows.Controls;

namespace UniPos.Desktop.Views.Dialogs;

public partial class DiscountView : UserControl
{
    public DiscountView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }
}
