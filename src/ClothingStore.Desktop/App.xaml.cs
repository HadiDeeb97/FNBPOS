using System.Windows;
using System.Windows.Threading;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Licensing;
using ClothingStore.Core.Localization;
using ClothingStore.Data;
using ClothingStore.Data.Seeding;
using ClothingStore.Data.Services;
using ClothingStore.Desktop.Converters;
using ClothingStore.Desktop.Infrastructure;
using ClothingStore.Desktop.Services;
using ClothingStore.Desktop.ViewModels;
using ClothingStore.Desktop.ViewModels.Dialogs;
using ClothingStore.Desktop.Views;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClothingStore.Desktop;

public partial class App : Application
{
    private IHost? _host;
    private SplashWindow? _splash;

    public static IServiceProvider Services { get; private set; } = null!;
    /// <summary>Server and database name, for display (never includes credentials).</summary>
    public static string DatabaseName { get; private set; } = "";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        HorizontalScrolling.Enable(); // touchpad swipes and Shift + wheel scroll tables sideways
        Loc.SetLanguage(LocalPreferences.Current.Language);
        _splash = new SplashWindow();
        _splash.Show();

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = e.Args,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);

        var connectionString = builder.Configuration.GetConnectionString("Pos");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            StartupError(Loc.T("Startup.NoConnectionString"));
            return;
        }
        try
        {
            var csb = new SqlConnectionStringBuilder(connectionString);
            DatabaseName = Loc.T("Startup.DatabaseOn", csb.InitialCatalog, csb.DataSource);
            // SQL Server Express can take longer than the default 15 seconds to let the first login in (right after
            // Windows starts, or when a debugger slows things down), so allow 30 unless the setting says otherwise.
            if (!connectionString.Contains("Timeout", StringComparison.OrdinalIgnoreCase) && csb.ConnectTimeout < 30)
            {
                csb.ConnectTimeout = 30;
                connectionString = csb.ConnectionString;
            }
        }
        catch (ArgumentException ex)
        {
            StartupError(Loc.T("Startup.BadConnectionString", ex.Message));
            return;
        }

        builder.Services.AddPosData(connectionString);
        ConfigureServices(builder.Services);
        _host = builder.Build();
        Services = _host.Services;
        // The cash drawer belongs to this PC: identify it by the same PC ID the license uses.
        Services.GetRequiredService<Session>().TillId = Services.GetRequiredService<LicenseManager>().MachineId;

        _splash?.SetStatus(Loc.T("Startup.Connecting"));
        await WaitForServerAsync(connectionString);

        // Every start backs up the existing database. Before an upgrade the backup is taken first, so there is a copy
        // from before this version changes anything; otherwise it runs in the background and doesn't hold up opening.
        BackupRecord? startupBackup = null;
        var upgrading = await Services.GetRequiredService<DatabaseInitializer>().NeedsUpgradeAsync();
        if (upgrading)
        {
            try
            {
                _splash?.SetStatus(Loc.T("Startup.BackingUp"));
                startupBackup = await Services.GetRequiredService<BackupService>().BackupOnStartupAsync();
            }
            catch (Exception)
            {
                // Can't even reach the server: the next step reports that properly.
            }
            _splash?.SetStatus(Loc.T("Startup.Connecting"));
        }

        if (!await PrepareDatabaseAsync(builder.Configuration.GetValue<bool?>("Pos:SeedDemoData") ?? SeedDemoDataByDefault)) return;

        try
        {
            var settings = Services.GetRequiredService<SettingsService>();
            settings.SettingsChanged += (_, _) => CurrencyFormat.Symbol = settings.Current.CurrencySymbol;
            await _host.StartAsync(); // starts the automatic backup worker
            if (!upgrading) BackUpInBackground();

            // Compile the screens' queries while the sign-in screen is up, so first visits are fast too.
            var warmUp = Services.GetRequiredService<QueryWarmUp>();
            _ = Task.Run(() => warmUp.RunAsync());
        }
        catch (Exception ex)
        {
            StartupError(Loc.T("Startup.CannotConnect", DatabaseName, ex.GetBaseException().Message));
            return;
        }

        CloseSplash();
        if (startupBackup is { Succeeded: false })
            Services.GetRequiredService<IDialogService>().Warning(Loc.T("Startup.BackupFailed", startupBackup.Error));
        if (!await EnsureLicensedAsync()) return;
        ShowLogin();
    }

    private const int StartupAttempts = 4;

    /// <summary>
    /// A shop's new database starts empty: demo products, customers and users (with known passwords) only when
    /// "Pos:SeedDemoData" is set, or in a development build.
    /// </summary>
#if DEBUG
    private const bool SeedDemoDataByDefault = true;
#else
    private const bool SeedDemoDataByDefault = false;
#endif

    /// <summary>
    /// Upgrades the database and loads the settings the first screens need. A PC that is short of memory or busy
    /// (right after Windows starts, Windows Update, a debugger) can make SQL Server answer too slowly the first time,
    /// so a timeout or dropped connection is retried a few times before anything is shown. If it still fails, the
    /// cashier can try again instead of having to start the app again. Returns false when the app was closed.
    /// </summary>
    private async Task<bool> PrepareDatabaseAsync(bool seedDemo)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await Services.GetRequiredService<DatabaseInitializer>().InitializeAsync(seedDemo);
                CurrencyFormat.Symbol = (await Services.GetRequiredService<SettingsService>().GetAsync()).CurrencySymbol;
                try { await Branding.Instance.RefreshAsync(Services.GetRequiredService<BrandingService>()); }
                catch { /* no logo is fine */ }
                WatchLogoForShortcuts();
                return true;
            }
            catch (Exception ex) when (attempt < StartupAttempts && DbErrors.IsTransient(ex))
            {
                StartupLog.Write(ex);
                SqlConnection.ClearAllPools(); // don't reuse a connection that broke
                _splash?.SetStatus(Loc.T("Startup.SlowRetry", attempt + 1, StartupAttempts));
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                var log = StartupLog.Write(ex);
                if (!AskTryAgain(Loc.T("Startup.CannotConnect", DatabaseName, ex.GetBaseException().Message), log)) return false;
                attempt = 0;
                SqlConnection.ClearAllPools();
            }
        }
    }

    private bool _watchingLogo;

    /// <summary>Puts the store logo (or the program's icon) on this PC's shortcuts now and whenever the logo changes.</summary>
    private void WatchLogoForShortcuts()
    {
        if (_watchingLogo) return;
        _watchingLogo = true;
        ShortcutIcons.Update(Branding.Instance.LogoImage, Loc.T("Shell.AppName"));
        Branding.Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Branding.Icon)) ShortcutIcons.Update(Branding.Instance.LogoImage, Loc.T("Shell.AppName"));
        };
    }

    /// <summary>Shows a start-up failure with "Try again?"; on No the app closes.</summary>
    private bool AskTryAgain(string message, string? logPath)
    {
        CloseSplash();
        var text = message + "\n\n" + (logPath is null ? "" : Loc.T("Startup.DetailsSaved", logPath) + "\n\n") + Loc.T("Startup.TryAgain");
        var retry = MessageBox.Show(text, Loc.T("Shell.AppName"), MessageBoxButton.YesNo, MessageBoxImage.Error, MessageBoxResult.Yes,
            Loc.IsRightToLeft ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : MessageBoxOptions.None) == MessageBoxResult.Yes;
        if (!retry)
        {
            Shutdown(1);
            return false;
        }
        _splash = new SplashWindow();
        _splash.Show();
        _splash.SetStatus(Loc.T("Startup.Connecting"));
        return true;
    }

    /// <summary>
    /// Checks this PC's license; without a valid one, only the activation screen is shown (with this PC's ID to send
    /// to the vendor). Returns false when the app was closed instead.
    /// </summary>
    private async Task<bool> EnsureLicensedAsync()
    {
        var license = Services.GetRequiredService<LicenseManager>();
        LicenseStatus status;
        while (true)
        {
            try
            {
                status = await license.CheckAsync();
                CloseSplash(); // shown again by "Try again"
                break;
            }
            catch (Exception ex)
            {
                if (!AskTryAgain(Loc.T("Startup.CannotConnect", DatabaseName, ex.GetBaseException().Message), StartupLog.Write(ex))) return false;
            }
        }
        if (status.State is LicenseState.ExpiringSoon or LicenseState.Trial && LocalPreferences.Current.LicenseWarnedOn != DateTime.Today)
        {
            // Besides the banner, say it once a day in a box nobody can miss.
            Services.GetRequiredService<IDialogService>().Warning(license.BannerText, Loc.T("License.Title"));
            LocalPreferences.Current.LicenseWarnedOn = DateTime.Today;
            LocalPreferences.Current.Save();
        }
        if (status.CanRun) return true;

        var dialogs = Services.GetRequiredService<IDialogService>();
        if (dialogs.ShowDialog(new ActivationViewModel(dialogs, license, required: true))) return true;
        Shutdown();
        return false;
    }

    private void CloseSplash()
    {
        _splash?.Close();
        _splash = null;
        MainWindow = null; // the splash was the first window, so WPF made it the main window
    }

    /// <summary>
    /// Waits for SQL Server to accept connections (up to about a minute and a half), so starting the POS right after
    /// Windows, while the SQL Server service is still starting, waits instead of failing. Wrong passwords and similar
    /// errors don't wait: the next step reports them.
    /// </summary>
    private async Task WaitForServerAsync(string connectionString)
    {
        var probe = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master", ConnectTimeout = 10, Pooling = false };
        const int attempts = 6;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                await using var connection = new SqlConnection(probe.ConnectionString);
                await connection.OpenAsync();
                return;
            }
            catch (SqlException ex) when (attempt < attempts && ex.Number is not (18456 or 18452 or 18470 or 4060))
            {
                _splash?.SetStatus(Loc.T("Startup.WaitingForServer", attempt, attempts - 1));
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
                return; // the database step that follows shows the real error
            }
        }
    }

    /// <summary>
    /// The start-up backup when nothing is being upgraded. It waits a couple of minutes first: a backup reads the whole
    /// database, and running it while the screens load their first data made those loads time out on busy PCs.
    /// </summary>
    private void BackUpInBackground()
    {
        var backups = Services.GetRequiredService<BackupService>();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(2));
                if (await backups.BackupOnStartupAsync() is { Succeeded: false } failed)
                    await Dispatcher.InvokeAsync(() =>
                        Services.GetRequiredService<IDialogService>().Warning(Loc.T("Startup.BackupFailed", failed.Error)));
            }
            catch (Exception)
            {
                // Logged in the backup history when possible; never take the app down over it.
            }
        });
    }

    private void StartupError(string message)
    {
        CloseSplash();
        MessageBox.Show(message, Loc.T("Shell.AppName"), MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK,
            Loc.IsRightToLeft ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : MessageBoxOptions.None);
        Shutdown(1);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<Session>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<NavigationService>();
        services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<NavigationService>());
        services.AddSingleton<PrintService>();
        services.AddSingleton<LicenseManager>();
        services.AddHostedService<AutoBackupWorker>();

        services.AddTransient<LoginViewModel>();
        services.AddTransient<MainViewModel>();

        // The register keeps its cart while you visit other screens.
        services.AddSingleton<SalesViewModel>();
        services.AddSingleton<CustomerDisplayViewModel>();
        services.AddTransient<ReturnsViewModel>();
        services.AddTransient<DeliveriesViewModel>();
        services.AddTransient<SalesHistoryViewModel>();
        services.AddTransient<CustomersViewModel>();
        services.AddTransient<ProductsViewModel>();
        services.AddTransient<InventoryViewModel>();
        services.AddTransient<PurchaseOrdersViewModel>();
        services.AddTransient<SuppliersViewModel>();
        services.AddTransient<CategoriesViewModel>();
        services.AddTransient<ShiftViewModel>();
        services.AddTransient<ReportsViewModel>();
        services.AddTransient<UsersViewModel>();
        services.AddTransient<SettingsViewModel>();
    }

    private void ShowLogin()
    {
        var session = Services.GetRequiredService<Session>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        while (true)
        {
            var viewModel = Services.GetRequiredService<LoginViewModel>();
            var login = new LoginWindow(viewModel);
            var signedIn = login.ShowDialog() == true;
            if (viewModel.RequestedLanguage is { } language)
            {
                // The sign-in screen was switched to another language: show it again in that language.
                SetLanguage(language);
                continue;
            }
            if (!signedIn)
            {
                Shutdown();
                return;
            }
            break;
        }

        if (session.User.PreferredLanguage is { } preferred) SetLanguage(preferred);

        if (session.User.MustChangePassword)
        {
            var change = new ChangePasswordViewModel(dialogs, Services.GetRequiredService<UserService>(), session, forced: true);
            if (!dialogs.ShowDialog(change))
            {
                session.SignOut();
                ShowLogin();
                return;
            }
        }

        ShowMain(startPage: null);
    }

    private static void SetLanguage(string language)
    {
        Loc.SetLanguage(language);
        LocalPreferences.Current.Language = Loc.Language;
        LocalPreferences.Current.Save();
    }

    /// <summary>Opens the main window; it is rebuilt (without signing out) to change language or reset the layout.</summary>
    private void ShowMain(Type? startPage)
    {
        var session = Services.GetRequiredService<Session>();
        var vm = Services.GetRequiredService<MainViewModel>();
        var main = new MainWindow { DataContext = vm };
        var next = AfterMain.Exit;
        var resetLayout = false;

        vm.SignOutRequested += (_, _) =>
        {
            next = AfterMain.SignIn;
            main.Close();
        };
        vm.LanguageChangeRequested += (_, language) =>
        {
            SetLanguage(language);
            next = AfterMain.Rebuild;
            main.Close();
        };
        vm.LayoutResetRequested += (_, _) =>
        {
            resetLayout = true;
            next = AfterMain.Rebuild;
            main.Close();
        };
        main.Closing += (_, e) =>
        {
            // Closing the app (not signing out or switching language) with the drawer still open: same choice as signing out.
            if (next == AfterMain.Exit && (!vm.ConfirmLeavingCart() || !vm.ConfirmLeavingDrawerOpen())) e.Cancel = true;
        };
        main.Closed += (_, _) =>
        {
            var page = vm.CurrentPage?.GetType();
            vm.Dispose();
            switch (next)
            {
                case AfterMain.SignIn:
                    Services.GetRequiredService<NavigationService>().Reset();
                    session.SignOut();
                    Dispatcher.BeginInvoke(ShowLogin);
                    break;
                case AfterMain.Rebuild:
                    // Closing saved the current sizes; forget them only after that.
                    if (resetLayout) LayoutMemory.ResetAll();
                    Services.GetRequiredService<NavigationService>().Reset();
                    Dispatcher.BeginInvoke(() => ShowMain(page));
                    break;
                default:
                    Shutdown();
                    break;
            }
        };
        MainWindow = main;
        main.Show();
        CustomerDisplay.Apply(rebuild: true); // drawn again in case the language changed
        _ = vm.StartAsync(startPage);
    }

    private enum AfterMain
    {
        Exit,
        SignIn,
        Rebuild,
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        try
        {
            Services.GetRequiredService<IDialogService>().Error(Loc.T("Startup.UnexpectedError"), e.Exception);
        }
        catch
        {
            MessageBox.Show(e.Exception.GetBaseException().Message, Loc.T("Shell.AppName"), MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // Failing during start-up or while switching windows would otherwise leave a process running with no window
        // (the app only shuts down explicitly), so a second start would look like it does nothing.
        if (!Windows.OfType<Window>().Any(w => w.IsVisible && w is not SplashWindow && !CustomerDisplay.IsDisplayWindow(w)))
        {
            _splash?.Close();
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
