using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClothingStore.Core.Entities;
using ClothingStore.Desktop.ViewModels;

namespace ClothingStore.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    /// <summary>Section list on the left: scrolls that section to the top.</summary>
    private void GoTo(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: FrameworkElement section }) return;
        var top = section.TransformToAncestor(Sections).Transform(new Point(0, 0)).Y;
        Scroller.ScrollToVerticalOffset(top);
    }

    /// <summary>Double-click on a backup row (not the header or scroll bar) shows that file.</summary>
    private void OnBackupDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || e.OriginalSource is not DependencyObject source) return;
        if (ItemsControl.ContainerFromElement(grid, source) is DataGridRow { Item: BackupRecord record } && DataContext is SettingsViewModel vm)
            vm.ShowBackupFileCommand.Execute(record);
    }
}
