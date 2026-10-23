using System.ComponentModel;
using System.Windows;
using ClothingStore.Desktop.Infrastructure;
using ClothingStore.Desktop.ViewModels;

namespace ClothingStore.Desktop.Views;

/// <summary>Full screen on the customer's monitor; never takes the keyboard from the register.</summary>
public partial class CustomerDisplayWindow : Window
{
    private readonly CustomerDisplayViewModel _vm;

    public CustomerDisplayWindow(CustomerDisplayViewModel vm, ScreenInfo screen)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        WindowAppearance.Apply(this);
        ScreenPlacement.FullScreenOn(this, screen);

        // Keep the item just scanned (or changed) in view when the list is longer than the screen.
        vm.Sales.PropertyChanged += OnSalesChanged;
        Closed += (_, _) => vm.Sales.PropertyChanged -= OnSalesChanged;
    }

    private void OnSalesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SalesViewModel.SelectedItem) && _vm.Sales.SelectedItem is { } item)
            Dispatcher.BeginInvoke(() => Lines.ScrollIntoView(item));
    }
}
