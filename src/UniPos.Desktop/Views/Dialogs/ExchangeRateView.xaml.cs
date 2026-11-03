using System.Windows.Controls;

namespace UniPos.Desktop.Views.Dialogs;

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
