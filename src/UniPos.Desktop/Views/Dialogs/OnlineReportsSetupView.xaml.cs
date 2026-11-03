using System.Windows.Controls;

namespace UniPos.Desktop.Views.Dialogs;

public partial class OnlineReportsSetupView : UserControl
{
    public OnlineReportsSetupView()
    {
        InitializeComponent();
        Loaded += (_, _) => TokenBox.Focus();
    }
}
