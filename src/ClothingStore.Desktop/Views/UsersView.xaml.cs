using System.Windows.Controls;
using System.Windows.Input;
using ClothingStore.Desktop.ViewModels;

namespace ClothingStore.Desktop.Views;

public partial class UsersView : UserControl
{
    public UsersView()
    {
        InitializeComponent();
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is UsersViewModel vm && vm.EditCommand.CanExecute(null)) vm.EditCommand.Execute(null);
    }
}
