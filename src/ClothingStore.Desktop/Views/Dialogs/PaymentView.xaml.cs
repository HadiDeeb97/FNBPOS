using System.Windows.Controls;

namespace ClothingStore.Desktop.Views.Dialogs;

public partial class PaymentView : UserControl
{
    public PaymentView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            UsdBox.Focus();
            UsdBox.SelectAll();
        };
    }
}
