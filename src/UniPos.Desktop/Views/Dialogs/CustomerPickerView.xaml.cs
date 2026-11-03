using UniPos.Desktop.Infrastructure;
using System.Windows.Controls;
using System.Windows.Input;
using UniPos.Desktop.ViewModels.Dialogs;

namespace UniPos.Desktop.Views.Dialogs;

public partial class CustomerPickerView : UserControl
{
    public CustomerPickerView()
    {
        InitializeComponent();
        Loaded += (_, _) => SearchBox.Focus();
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RowClick.IsOnRow(sender, e) && DataContext is CustomerPickerViewModel vm && vm.SelectCommand.CanExecute(null)) vm.SelectCommand.Execute(null);
    }
}
