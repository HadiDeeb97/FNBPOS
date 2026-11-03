using System.Windows.Controls;

namespace UniPos.Desktop.Views.Dialogs;

public partial class MessageDialogView : UserControl
{
    public MessageDialogView()
    {
        InitializeComponent();
        Loaded += (_, _) => PrimaryButton.Focus();
    }
}
