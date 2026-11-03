using UniPos.Desktop.Infrastructure;
using System.Windows.Controls;
using System.Windows.Input;
using UniPos.Desktop.ViewModels;

namespace UniPos.Desktop.Views;

public partial class UsersView : UserControl
{
    public UsersView()
    {
        InitializeComponent();
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RowClick.IsOnRow(sender, e) && DataContext is UsersViewModel vm && vm.EditCommand.CanExecute(null)) vm.EditCommand.Execute(null);
    }
}
