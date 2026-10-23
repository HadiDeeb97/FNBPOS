using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ClothingStore.Desktop.Infrastructure;
using ClothingStore.Desktop.ViewModels.Dialogs;

namespace ClothingStore.Desktop.Views;

/// <summary>Hosts any <see cref="IDialogViewModel"/>; the view is picked by DataTemplate.</summary>
public partial class DialogWindow : Window
{
    private readonly IDialogViewModel _viewModel;

    public DialogWindow(IDialogViewModel viewModel)
    {
        InitializeComponent();
        WindowAppearance.Apply(this);
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
        Closed += (_, _) => viewModel.CloseRequested -= OnCloseRequested;
        PreviewKeyDown += OnPreviewKeyDown;

        if (viewModel is DialogViewModelBase { WindowSize: { } size }) OpenResizable(size);
        else OpenFitted();

        Loaded += async (_, _) =>
        {
            if (SizeToContent != SizeToContent.Manual) FitViewToScreen();
            MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            if (_viewModel is DialogViewModelBase dialog) await dialog.OnOpenedAsync();
        };
    }

    // Window frame and title bar around the content (estimate; only used to size the window when it opens).
    private static double FrameWidth => SystemParameters.ResizeFrameVerticalBorderWidth * 2 + 8;
    private static double FrameHeight => SystemParameters.WindowCaptionHeight + SystemParameters.ResizeFrameHorizontalBorderHeight * 2 + 8;

    /// <summary>
    /// A list or editor: opens at the size it asks for (smaller on a small screen) and can be resized and maximised; the
    /// view has no size of its own, so it always fills the window and its tables scroll inside. It can't be minimised:
    /// the window behind waits for the dialog, and a minimised dialog left the POS looking frozen.
    /// </summary>
    private void OpenResizable(Size size)
    {
        var area = SystemParameters.WorkArea;
        // The content is zoomed by this PC's text size, and so is the window around it.
        var zoom = UiScale.Current;
        size = new Size(size.Width * zoom, size.Height * zoom);
        var marginWidth = (Host.Margin.Left + Host.Margin.Right) * zoom + FrameWidth;
        var marginHeight = (Host.Margin.Top + Host.Margin.Bottom) * zoom + FrameHeight;
        SizeToContent = SizeToContent.Manual;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Width = Math.Min(size.Width + marginWidth, area.Width - 40);
        Height = Math.Min(size.Height + marginHeight, area.Height - 40);
        MinWidth = Math.Min(Width, Math.Round(size.Width * 0.75) + marginWidth);
        MinHeight = Math.Min(Height, Math.Round(size.Height * 0.75) + marginHeight);
        Scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled; // the view gets the window's height
        SourceInitialized += (_, _) => RemoveMinimizeButton();
    }

    /// <summary>
    /// A form or message: the window fits its content (growing if, say, a message appears) and keeps that size; it isn't
    /// resized or maximised. Never taller or wider than the screen: the form scrolls instead of losing its buttons.
    /// </summary>
    private void OpenFitted()
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        MaxWidth = SystemParameters.WorkArea.Width - 40;
    }

    /// <summary>
    /// Form views give a preferred width. On a small screen (a 1366×768 laptop) that could push them off the side, so
    /// shrink the view to what fits.
    /// </summary>
    private void FitViewToScreen()
    {
        if (VisualTreeHelper.GetChildrenCount(Host) == 0 || VisualTreeHelper.GetChild(Host, 0) is not FrameworkElement view) return;
        // The view is zoomed by the PC's text size: what fits on screen is that much smaller in its own units.
        var zoom = UiScale.Current;
        var maxWidth = (MaxWidth - FrameWidth) / zoom - Host.Margin.Left - Host.Margin.Right;
        var maxHeight = (MaxHeight - FrameHeight) / zoom - Host.Margin.Top - Host.Margin.Bottom;
        if (!double.IsNaN(view.Width) && view.Width > maxWidth) view.Width = Math.Max(300, maxWidth);
        if (!double.IsNaN(view.Height) && view.Height > maxHeight) view.Height = Math.Max(240, maxHeight);
    }

    private void RemoveMinimizeButton()
    {
        var handle = new WindowInteropHelper(this).Handle;
        SetWindowLong(handle, GwlStyle, GetWindowLong(handle, GwlStyle) & ~WsMinimizeBox);
        HwndSource.FromHwnd(handle)?.AddHook(OnWindowMessage);
    }

    private static IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmSysCommand && ((int)wParam & 0xFFF0) == ScMinimize) handled = true; // Win+Down, the window menu
        return IntPtr.Zero;
    }

    private const int GwlStyle = -16;
    private const int WsMinimizeBox = 0x00020000;
    private const int WmSysCommand = 0x0112;
    private const int ScMinimize = 0xF020;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        switch (e.Key)
        {
            case Key.Enter:
                // Enter clicks the default button without moving focus, so push what was typed in the focused box
                // first; otherwise a field bound on lost focus (a price, say) would be saved with its old value.
                if (focused is TextBox { AcceptsReturn: false } box)
                    box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                break;

            case Key.Escape:
                // Let an open drop-down close itself, and don't abandon a save that is still running
                // (it would finish anyway, but the caller would be told it was cancelled).
                if (IsDropDownOpen(focused) || _viewModel is ViewModelBase { IsBusy: true }) return;
                e.Handled = true;
                Close();
                break;
        }
    }

    private static bool IsDropDownOpen(DependencyObject? element)
    {
        for (var d = element; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is ComboBox { IsDropDownOpen: true } or DatePicker { IsDropDownOpen: true }) return true;
            if (d is Window) break;
        }
        // Items of an open drop-down live in a popup, outside the window's tree.
        return element is ComboBoxItem || element is FrameworkElement { TemplatedParent: ComboBox { IsDropDownOpen: true } };
    }

    private void OnCloseRequested(object? sender, bool result)
    {
        DialogResult = result;
    }
}
