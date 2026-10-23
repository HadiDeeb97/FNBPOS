using System.Windows;
using System.Windows.Controls;
using ClothingStore.Desktop.ViewModels;

namespace ClothingStore.Desktop.Views.Dialogs;

public partial class UserEditorView : UserControl
{
    public UserEditorView()
    {
        InitializeComponent();
        Loaded += (_, _) => UserBox.Focus();
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is UserEditorViewModel vm) vm.Password = PasswordBox.Password;
    }
}
