using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ClothingStore.Core.Localization;
using ClothingStore.Desktop.Infrastructure;
using ClothingStore.Desktop.ViewModels;

namespace ClothingStore.Desktop.Views;

public partial class SalesView : UserControl
{
    public SalesView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is SalesViewModel old)
            {
                old.FocusSearchRequested -= OnFocusSearchRequested;
                old.PropertyChanged -= OnViewModelPropertyChanged;
            }
            if (e.NewValue is SalesViewModel vm)
            {
                vm.FocusSearchRequested += OnFocusSearchRequested;
                vm.PropertyChanged += OnViewModelPropertyChanged;
            }
        };
        Loaded += (_, _) =>
        {
            // The screen is kept between visits: listen again after being taken off screen (Unloaded stops listening).
            if (DataContext is SalesViewModel current)
            {
                current.FocusSearchRequested -= OnFocusSearchRequested;
                current.PropertyChanged -= OnViewModelPropertyChanged;
                current.FocusSearchRequested += OnFocusSearchRequested;
                current.PropertyChanged += OnViewModelPropertyChanged;
            }
            FocusSearch();
            // After the saved panel sizes are restored.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (!_widthsRead)
                {
                    // The cashier's own widths (kept apart: the saved panel layout also holds widths this view narrowed).
                    var layout = LocalPreferences.Current.Layout;
                    if (ReadWidth(layout, BrowseWidthKey) is >= 200 and var browse) _browseWidth = new GridLength(browse);
                    else if (BrowseColumn.Width is { IsAbsolute: true, Value: >= 200 } restored) _browseWidth = restored;
                    if (ReadWidth(layout, PanelWidthKey) is { } panel && panel >= PanelColumn.MinWidth) _panelWidth = new GridLength(panel);
                    else if (PanelColumn.Width is { IsAbsolute: true } restoredPanel && restoredPanel.Value >= PanelColumn.MinWidth) _panelWidth = restoredPanel;
                    _widthsRead = true;
                }
                FitPanels(VisibleSize.Width(RegisterGrid));
            });
        };
        // Not SizeChanged: a grid that doesn't fit keeps its size and gets cut off instead (see VisibleSize).
        LayoutUpdated += (_, _) =>
        {
            if (!_widthsRead || !IsLoaded) return; // not before the saved panel sizes are known
            var width = VisibleSize.Width(RegisterGrid);
            if (Math.Abs(width - _fittedWidth) >= 0.5) FitPanels(width);
        };
        BrowseSplitter.DragCompleted += (_, _) =>
        {
            if (BrowseColumn.ActualWidth < 200) return;
            _browseWidth = new GridLength(BrowseColumn.ActualWidth);
            SaveWidth(BrowseWidthKey, BrowseColumn.ActualWidth);
        };
        PanelSplitter.DragCompleted += (_, _) =>
        {
            _panelWidth = new GridLength(PanelColumn.ActualWidth);
            SaveWidth(PanelWidthKey, PanelColumn.ActualWidth);
            FitPanels(VisibleSize.Width(RegisterGrid));
        };
        Unloaded += (_, _) =>
        {
            if (DataContext is SalesViewModel vm)
            {
                vm.FocusSearchRequested -= OnFocusSearchRequested;
                vm.PropertyChanged -= OnViewModelPropertyChanged;
            }
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F2)
            {
                FocusSearch();
                e.Handled = true;
            }
        };
    }

    private SalesViewModel? ViewModel => DataContext as SalesViewModel;

    private const double BrowseMinWidth = 220;
    private const string BrowseWidthKey = "register:browseWidth";
    private const string PanelWidthKey = "register:panelWidth";

    private static double? ReadWidth(Dictionary<string, string> layout, string key) =>
        layout.TryGetValue(key, out var text)
        && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value : null;

    private static void SaveWidth(string key, double width)
    {
        LocalPreferences.Current.Layout[key] = width.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        LocalPreferences.Current.Save();
    }

    /// <summary>Panel widths as the cashier left them (dragged or remembered), used whenever there is room.</summary>
    private GridLength _browseWidth = new(330), _panelWidth = new(380);
    private bool _widthsRead;
    private double _fittedWidth = -1;

    /// <summary>The cashier wants the product list but there's no room for it in this window.</summary>
    private bool _browseSqueezed;

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SalesViewModel.IsBrowseVisible)) FitPanels(VisibleSize.Width(RegisterGrid));
    }

    /// <summary>
    /// Shows the product list when the cashier wants it and it fits beside the cart and the payment panel (the panel gives
    /// up width, down to its minimum, to make room). In a narrower window (not maximised, a small screen) the list steps
    /// aside by itself and comes back when there is room, so the totals and the Pay button are never cut off.
    /// <paramref name="total"/> is the width really visible (VisibleSize), not the width the grid was laid out at.
    /// </summary>
    private void FitPanels(double total)
    {
        if (total <= 0) return;
        _fittedWidth = total;
        var splitter = PanelSplitter.Width;
        var cart = CartColumn.MinWidth;
        var panelMin = PanelColumn.MinWidth;
        var panelWanted = Math.Max(panelMin, _panelWidth.Value);
        var wanted = ViewModel?.IsBrowseVisible ?? true;

        // Room for the list with the panel at its width, else with the panel narrowed (never below its minimum).
        var roomFull = total - cart - panelWanted - 2 * splitter;
        var roomNarrow = total - cart - panelMin - 2 * splitter;
        var show = wanted && roomNarrow >= BrowseMinWidth;
        double panel;
        if (show)
        {
            var browse = Math.Min(Math.Max(_browseWidth.Value, BrowseMinWidth), Math.Max(roomFull, BrowseMinWidth));
            BrowseColumn.MinWidth = BrowseMinWidth;
            BrowseColumn.Width = new GridLength(browse);
            panel = Math.Clamp(total - cart - browse - 2 * splitter, panelMin, panelWanted);
        }
        else
        {
            BrowseColumn.MinWidth = 0;
            BrowseColumn.Width = new GridLength(0);
            panel = Math.Clamp(total - cart - splitter, panelMin, panelWanted);
        }
        PanelColumn.Width = new GridLength(panel);
        BrowsePanel.Visibility = BrowseSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        // The "show products" button stays while the list isn't on screen, also when it's only missing for lack of room.
        _browseSqueezed = wanted && !show;
        ShowBrowseButton.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnShowBrowseClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        if (!vm.IsBrowseVisible) vm.ToggleBrowseCommand.Execute(null); // fits the panels again at once
        if (_browseSqueezed) ToastHost.Instance.Show(Loc.T("Register.NoRoomForProducts"), ToastKind.Info);
        FocusSearch(); // the next scan goes to the scan box
    }

    private void OnFocusSearchRequested(object? sender, EventArgs e) => FocusSearch();

    private void FocusSearch() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        });

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        switch (e.Key)
        {
            case Key.Down when vm.IsSearchOpen:
                ResultsGrid.Focus();
                if (ResultsGrid.SelectedIndex < 0 && ResultsGrid.Items.Count > 0) ResultsGrid.SelectedIndex = 0;
                e.Handled = true;
                break;
            case Key.Escape when vm.IsSearchOpen:
                vm.CloseSearchCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Add or Key.OemPlus when vm.SearchText.Length == 0:
                vm.IncreaseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Subtract or Key.OemMinus when vm.SearchText.Length == 0:
                vm.DecreaseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Delete when vm.SearchText.Length == 0:
                vm.RemoveItemCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnResultsKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        if (e.Key == Key.Enter)
        {
            vm.AddResultCommand.Execute(ResultsGrid.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CloseSearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnResultDoubleClick(object sender, MouseButtonEventArgs e) =>
        ViewModel?.AddResultCommand.Execute(ResultsGrid.SelectedItem);

    private void OnCartKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        if (e.Key == Key.Delete)
        {
            vm.RemoveItemCommand.Execute(null);
            e.Handled = true;
        }
    }
}
