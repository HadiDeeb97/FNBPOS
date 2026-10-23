using System.Windows.Controls;
using System.Windows.Input;
using ClothingStore.Desktop.ViewModels;

namespace ClothingStore.Desktop.Views;

public partial class ProductsView : UserControl
{
    public ProductsView()
    {
        InitializeComponent();
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ProductsViewModel { CanEdit: true } vm && vm.EditCommand.CanExecute(null)) vm.EditCommand.Execute(null);
    }
}
