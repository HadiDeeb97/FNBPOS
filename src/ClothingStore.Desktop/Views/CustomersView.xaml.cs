using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClothingStore.Desktop.ViewModels;

namespace ClothingStore.Desktop.Views;

public partial class CustomersView : UserControl
{
    public CustomersView()
    {
        InitializeComponent();
        // Grid columns aren't part of the visual tree, so the column can't bind to the view model.
        DataContextChanged += (_, _) => TotalSpentColumn.Visibility =
            DataContext is CustomersViewModel { CanSeeSpending: true } ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is CustomersViewModel vm && vm.EditCommand.CanExecute(null)) vm.EditCommand.Execute(null);
    }
}
