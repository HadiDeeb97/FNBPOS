using System.Windows.Controls;

namespace ClothingStore.Desktop.Views.Dialogs;

public partial class ExchangeRateView : UserControl
{
    public ExchangeRateView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RateBox.Focus();
            RateBox.SelectAll();
        };
    }
}
