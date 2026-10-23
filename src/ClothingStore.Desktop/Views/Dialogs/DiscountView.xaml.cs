using System.Windows.Controls;

namespace ClothingStore.Desktop.Views.Dialogs;

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
