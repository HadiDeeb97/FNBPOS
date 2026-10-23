using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ClothingStore.Desktop.ViewModels;
using ClothingStore.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>
/// Opens the customer display on its screen when this PC has one switched on (Settings → Customer display), and moves
/// or closes it when screens are plugged in or out. A missing screen is skipped quietly; it never stops the register.
/// </summary>
public static class CustomerDisplay
{
    private static CustomerDisplayWindow? _window;
    private static ScreenInfo? _screen;
    private static bool _watching;

    /// <summary>Shows, moves or closes the display to match the settings; <paramref name="rebuild"/> redraws it (language changed).</summary>
    public static void Apply(bool rebuild = false)
    {
        try
        {
            var prefs = LocalPreferences.Current;
            // Listening even while its screen is missing, so plugging it in (or switching it on) later opens it.
            if (prefs.CustomerDisplay) WatchScreens();
            var screen = prefs.CustomerDisplay ? Screens.ForCustomerDisplay(prefs.CustomerDisplayScreen) : null;
            if (screen is null)
            {
                Close();
                return;
            }
            if (!rebuild && _window is not null && screen == _screen)
            {
                // Same screen: put it back over it anyway (Windows moves it to the till's screen while the customer's
                // screen drops out, e.g. asleep or switched to another input). Never takes the keyboard.
                ScreenPlacement.Cover(_window, screen);
                return;
            }

            Close();
            _window = new CustomerDisplayWindow(App.Services.GetRequiredService<CustomerDisplayViewModel>(), screen);
            _screen = screen;
            _window.Show();
        }
        catch (Exception ex)
        {
            StartupLog.Write(ex); // a display problem is noted, never shown to the cashier mid-sale
            Close();
        }
    }

    public static void Close()
    {
        var window = _window;
        _window = null;
        _screen = null;
        window?.Close();
    }

    /// <summary>Windows that only show things (the display and the screen numbers): never an owner for dialogs.</summary>
    public static bool IsDisplayWindow(Window window) => window is CustomerDisplayWindow || window.Tag is IdentifyTag;

    /// <summary>Shows each screen's number large on it for a few seconds, to tell which one faces the customer.</summary>
    public static void Identify()
    {
        foreach (var screen in Screens.All())
        {
            var window = new Window
            {
                Tag = new IdentifyTag(),
                WindowStyle = WindowStyle.None, // must come before AllowsTransparency
                AllowsTransparency = true,
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0F, 0x17, 0x2A)),
                Topmost = true,
                Content = new Viewbox
                {
                    Margin = new Thickness(80),
                    Child = new TextBlock
                    {
                        Text = screen.Number.ToString(),
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.Bold,
                        FontSize = 200,
                    },
                },
            };
            ScreenPlacement.FullScreenOn(window, screen);
            window.Show();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                window.Close();
            };
            timer.Start();
        }
    }

    private sealed class IdentifyTag;

    private static void WatchScreens()
    {
        if (_watching) return;
        _watching = true;
        // Raised on a system thread (often several times) when any screen is plugged in or out or changes resolution.
        // Only a change to the customer's screen redraws the display.
        SystemEvents.DisplaySettingsChanged += (_, _) =>
            Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => Apply());
    }
}
