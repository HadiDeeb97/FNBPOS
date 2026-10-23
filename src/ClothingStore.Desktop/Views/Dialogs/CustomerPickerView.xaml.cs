using System.Windows.Controls;
using System.Windows.Input;
using ClothingStore.Desktop.ViewModels.Dialogs;

namespace ClothingStore.Desktop.Views.Dialogs;

public partial class CustomerPickerView : UserControl
{
    public CustomerPickerView()
    {
        InitializeComponent();
        Loaded += (_, _) => SearchBox.Focus();
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is CustomerPickerViewModel vm && vm.SelectCommand.CanExecute(null)) vm.SelectCommand.Execute(null);
    }
}
