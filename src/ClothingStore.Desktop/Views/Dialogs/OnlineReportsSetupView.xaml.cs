using System.Windows.Controls;

namespace ClothingStore.Desktop.Views.Dialogs;

public partial class OnlineReportsSetupView : UserControl
{
    public OnlineReportsSetupView()
    {
        InitializeComponent();
        Loaded += (_, _) => TokenBox.Focus();
    }
}
