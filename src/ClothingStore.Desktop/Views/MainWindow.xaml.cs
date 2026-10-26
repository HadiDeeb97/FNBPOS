using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ClothingStore.Desktop.Infrastructure;

namespace ClothingStore.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        WindowAppearance.Apply(this);
        FitRestoredSizeToScreen();
        if (LocalPreferences.Current.FullScreen) SetFullScreen(true);
        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += (_, _) => PreparePagesWhenIdle();
    }

    /// <summary>The pages used all day, whose screens are built ahead of time.</summary>
    private static readonly Type[] PreparedPages =
    [
        typeof(ViewModels.SalesViewModel), typeof(ViewModels.SalesHistoryViewModel),
        typeof(ViewModels.ProductsViewModel), typeof(ViewModels.CustomersViewModel),
    ];

    /// <summary>
    /// After the window is up, builds the screens of the pages used all day (<see cref="PreparedPages"/>) one at a time
    /// whenever the app is idle, so they open quickly the first time too. The others are built when first opened:
    /// building every screen ahead kept them all in memory even on tills that never open them. Nothing is loaded from
    /// the database until a page is opened.
    /// </summary>
    private void PreparePagesWhenIdle()
    {
        if (DataContext is not ViewModels.MainViewModel main
            || App.Services.GetService(typeof(NavigationService)) is not NavigationService navigation) return;
        var queue = new Queue<Type>(main.NavGroups.SelectMany(g => g.Items).Select(i => i.PageType).Where(PreparedPages.Contains));
        void Next()
        {
            if (queue.Count == 0 || !IsLoaded) return;
            try
            {
                PageHost.Prepare(navigation.GetPage(queue.Dequeue()));
            }
            catch (Exception)
            {
                // Best effort: the page is simply built when it is opened.
            }
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, Next);
        }
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, Next);
    }

    /// <summary>
    /// Shortcuts that work the same on every page: Ctrl+F jumps to the page's search box, F5 refreshes the page.
    /// (The register's own F-keys are handled by the register.)
    /// </summary>
    /// <summary>
    /// A page's own shortcuts (the register's F4–F12) work wherever the keyboard focus is in the window, not only
    /// after clicking inside the page (e.g. right after using the sidebar or the top bar).
    /// </summary>
    private bool RunPageShortcut(KeyEventArgs e)
    {
        if (VisualTreeHelper.GetChildrenCount(PageHost) == 0 || FindView(PageHost) is not { } view) return false;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        foreach (var binding in view.InputBindings.OfType<KeyBinding>())
        {
            if (binding.Key != key || binding.Modifiers != Keyboard.Modifiers || binding.Command is not { } command) continue;
            // Inside the page the binding fires by itself.
            if (view.IsKeyboardFocusWithin) return false;
            if (command.CanExecute(binding.CommandParameter)) command.Execute(binding.CommandParameter);
            return true;
        }
        return false;
    }

    private static UIElement? FindView(DependencyObject host)
    {
        var child = VisualTreeHelper.GetChild(host, 0);
        // ContentControl → ContentPresenter → the page's UserControl.
        while (child is not null and not System.Windows.Controls.UserControl && VisualTreeHelper.GetChildrenCount(child) > 0)
            child = VisualTreeHelper.GetChild(child, 0);
        return child as UIElement;
    }

    private static async Task ReloadAsync(IPageViewModel page)
    {
        try
        {
            await page.OnNavigatedToAsync();
        }
        catch (Exception ex) when (App.Services.GetService(typeof(IDialogService)) is IDialogService dialogs)
        {
            dialogs.Error(ClothingStore.Core.Localization.Loc.T("Common.SomethingWentWrong"), ex);
        }
    }

    /// <summary>
    /// The size used when the window is restored down from maximised: never bigger than the screen (on a 1366 × 768 laptop
    /// the default 800 px height put the bottom of the POS under the taskbar).
    /// </summary>
    private void FitRestoredSizeToScreen()
    {
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, area.Width);
        MinHeight = Math.Min(MinHeight, area.Height);
        Width = Math.Max(MinWidth, Math.Min(Width, area.Width - 40));
        Height = Math.Max(MinHeight, Math.Min(Height, area.Height - 40));
    }

    // ---- Full screen -------------------------------------------------------------------------
    // Hides the title bar and the Windows taskbar so the till shows only the POS. Remembered on this PC.

    private bool _fullScreen;

    private void OnFullScreenClick(object sender, RoutedEventArgs e) => ToggleFullScreen();

    private void ToggleFullScreen()
    {
        SetFullScreen(!_fullScreen);
        LocalPreferences.Current.FullScreen = _fullScreen;
        LocalPreferences.Current.Save();
    }

    private void SetFullScreen(bool on)
    {
        _fullScreen = on;
        // Going through Normal makes a borderless maximized window cover the taskbar too.
        WindowState = WindowState.Normal;
        WindowStyle = on ? WindowStyle.None : WindowStyle.SingleBorderWindow;
        ResizeMode = on ? ResizeMode.NoResize : ResizeMode.CanResize;
        WindowState = WindowState.Maximized;
        FullScreenButton.Content = on ? "\uE73F" : "\uE740";
        FullScreenButton.ToolTip = ClothingStore.Core.Localization.Loc.T(on ? "Shell.ExitFullScreen" : "Shell.FullScreen");
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            e.Handled = true;
            ToggleFullScreen();
        }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = SearchField.FocusFirst(PageHost);
        }
        else if (e.Key == Key.F1 && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            if (App.Services.GetService(typeof(IDialogService)) is IDialogService dialogs)
                dialogs.Info(ClothingStore.Core.Localization.Loc.T("Shell.ShortcutsHelp"), ClothingStore.Core.Localization.Loc.T("Shell.ShortcutsTitle"));
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract or Key.D0 or Key.NumPad0)
        {
            // Text size of this PC, as in a web browser.
            e.Handled = true;
            if (e.Key is Key.OemPlus or Key.Add) UiScale.Larger();
            else if (e.Key is Key.OemMinus or Key.Subtract) UiScale.Smaller();
            else UiScale.Reset();
        }
        else if (RunPageShortcut(e))
        {
            e.Handled = true;
        }
        else if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None && PageHost.Page is IPageViewModel page)
        {
            // Reloads the page's data, as when opening it from the sidebar (the register keeps its cart).
            e.Handled = true;
            _ = ReloadAsync(page);
        }
    }
}
