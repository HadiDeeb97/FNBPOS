using System.Windows;
using System.Windows.Controls;
using UniPos.Desktop.ViewModels.Dialogs;

namespace UniPos.Desktop.Views.Dialogs;

public partial class ManagerApprovalView : UserControl
{
    public ManagerApprovalView()
    {
        InitializeComponent();
        Loaded += (_, _) => UserBox.Focus();
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ManagerApprovalViewModel vm) vm.Password = PasswordBox.Password;
    }
}
