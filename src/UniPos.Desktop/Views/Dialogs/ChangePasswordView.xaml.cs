using System.Windows;
using System.Windows.Controls;
using UniPos.Desktop.ViewModels.Dialogs;

namespace UniPos.Desktop.Views.Dialogs;

public partial class ChangePasswordView : UserControl
{
    public ChangePasswordView()
    {
        InitializeComponent();
        Loaded += (_, _) => CurrentBox.Focus();
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ChangePasswordViewModel vm) return;
        vm.CurrentPassword = CurrentBox.Password;
        vm.NewPassword = NewBox.Password;
        vm.ConfirmPassword = ConfirmBox.Password;
    }
}
