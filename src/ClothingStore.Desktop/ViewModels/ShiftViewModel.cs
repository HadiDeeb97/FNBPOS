using System.Globalization;
using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Security;
using ClothingStore.Data.Services;
using ClothingStore.Desktop.Converters;
using ClothingStore.Desktop.Infrastructure;
using ClothingStore.Desktop.Services;
using ClothingStore.Desktop.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClothingStore.Core.Localization;

namespace ClothingStore.Desktop.ViewModels;

/// <summary>Cash drawer: open with a float, pay-ins/outs, X report, count and close (Z report).</summary>
public sealed partial class ShiftViewModel(
    IDialogService dialogs, ShiftService shifts, SettingsService settings, Session session, PrintService print,
    BackupService backups)
    : ViewModelBase(dialogs), IPageViewModel
{
    public string Title => Loc.T("Nav.CashDrawer");
    public Session Session => session;

    [ObservableProperty]
    public partial string OpeningFloatText { get; set; } = 100m.ToString("0.00", CultureInfo.CurrentCulture); // read back with the same culture

    [ObservableProperty]
    public partial string OpeningFloatLbpText { get; set; } = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLbp))]
    public partial ShiftSummary? Summary { get; set; }

    /// <summary>Lebanese pounds are counted separately from dollars.</summary>
    public bool ShowLbp => settings.Current.ActiveLbpRate > 0 || Summary?.ShowLbp == true;

    [ObservableProperty]
    public partial CashCurrency MovementCurrency { get; set; } = CashCurrency.Usd;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VariancePreviewLbp))]
    public partial string CountedCashLbpText { get; set; } = "";

    public string VariancePreviewLbp => Summary is not null && TryParseLbp(CountedCashLbpText, out var counted)
        ? Loc.T("Shift.VariancePreview", CurrencyFormat.Lbp(counted - Summary.ExpectedCashLbp))
        : "";

    [ObservableProperty]
    public partial CashMovementType MovementType { get; set; } = CashMovementType.PayOut;

    [ObservableProperty]
    public partial string MovementAmountText { get; set; } = "";

    [ObservableProperty]
    public partial string? MovementReason { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VariancePreview))]
    public partial string CountedCashText { get; set; } = "";

    [ObservableProperty]
    public partial string? CloseNotes { get; set; }

    [ObservableProperty]
    public partial List<Shift> History { get; set; } = [];

    [ObservableProperty]
    public partial Shift? SelectedHistory { get; set; }

    public string VariancePreview => Summary is not null && TryParse(CountedCashText, out var counted)
        ? Loc.T("Shift.VariancePreview", CurrencyFormat.Format(counted - Summary.ExpectedCash))
        : "";

    public async Task OnNavigatedToAsync() => await RefreshAsync();

    // ---- Whose drawer ----------------------------------------------------------------------
    // The drawer belongs to this PC. If another cashier's is still open here, this user continues it (hand-over) or
    // counts and closes it before opening their own. A manager can also count and close a drawer left open on another PC.

    /// <summary>A drawer being counted and closed that isn't this user's own (null when closing their own).</summary>
    [ObservableProperty]
    public partial Shift? ClosingShift { get; set; }

    /// <summary>The drawer shown on the left: this user's, or the one being closed.</summary>
    private Shift? ActiveShift => ClosingShift ?? session.CurrentShift;

    public bool ShowOpenForm => !session.HasOpenShift && session.OtherDrawer is null && ClosingShift is null;
    public bool ShowOtherDrawer => !session.HasOpenShift && session.OtherDrawer is not null && ClosingShift is null;
    public bool ShowActiveDrawer => session.HasOpenShift || ClosingShift is not null;
    public bool IsClosingOther => ClosingShift is not null;
    public bool IsOwnDrawer => session.HasOpenShift && ClosingShift is null;

    public string OtherDrawerText => session.OtherDrawer is { } d
        ? Loc.T("Shift.OtherDrawerOpen", Name(d), d.OpenedAt.ToString("g"))
        : "";

    public string ClosingText => ClosingShift is { } d
        ? Loc.T("Shift.ClosingOther", Name(d), d.TillName ?? "-", d.OpenedAt.ToString("g"))
        : "";

    private static string Name(Shift d) => (d.CurrentUser ?? d.User)?.FullName ?? "?";

    private void NotifyDrawerState()
    {
        foreach (var name in new[] { nameof(ShowOpenForm), nameof(ShowOtherDrawer), nameof(ShowActiveDrawer), nameof(IsClosingOther),
                     nameof(IsOwnDrawer), nameof(OtherDrawerText), nameof(ClosingText), nameof(VariancePreview), nameof(VariancePreviewLbp) })
            OnPropertyChanged(name);
        CloseSelectedDrawerCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Continue the other cashier's drawer: sales go into it from now on (recorded as a hand-over).</summary>
    [RelayCommand]
    private Task ContinueDrawerAsync() => RunAsync(async () =>
    {
        if (session.OtherDrawer is not { } drawer) return;
        session.CurrentShift = await shifts.TakeOverAsync(drawer.Id, session.User.Id);
        session.OtherDrawer = null;
        Dialogs.Toast(Loc.T("Shift.TookOver", Name(drawer)));
        await LoadAsync();
    });

    /// <summary>Count the other cashier's drawer and close it, then open this user's own.</summary>
    [RelayCommand]
    private async Task CountOtherDrawerAsync()
    {
        if (session.OtherDrawer is not { } drawer) return;
        ClosingShift = drawer;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task StopClosingAsync()
    {
        ClosingShift = null;
        await RefreshAsync();
    }

    /// <summary>Managers: count and close a drawer left open (on any PC) from the list.</summary>
    [RelayCommand(CanExecute = nameof(CanCloseSelectedDrawer))]
    private async Task CloseSelectedDrawerAsync()
    {
        if (SelectedHistory is not { Status: ShiftStatus.Open } drawer) return;
        ClosingShift = drawer;
        await RefreshAsync();
    }

    private bool CanCloseSelectedDrawer() =>
        SelectedHistory is { Status: ShiftStatus.Open } d && d.Id != session.CurrentShift?.Id
        && (session.Can(Permission.ViewAllShifts) || d.Id == session.OtherDrawer?.Id);

    partial void OnSelectedHistoryChanged(Shift? value) => CloseSelectedDrawerCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private Task RefreshAsync() => RunAsync(LoadAsync);

    private async Task LoadAsync()
    {
        var till = await shifts.GetTillShiftAsync(session.User.Id, session.TillId, session.TillName);
        session.CurrentShift = till.IsMine ? till.Shift : null;
        session.OtherDrawer = till.IsOtherCashiers ? till.Shift : null;
        if (ClosingShift is { } closing && (closing.Id == session.CurrentShift?.Id)) ClosingShift = null;
        Summary = ActiveShift is { } shift ? await shifts.GetSummaryAsync(shift.Id) : null;
        if (Summary?.ClosedAt is not null) // closed elsewhere meanwhile
        {
            ClosingShift = null;
            Summary = null;
        }
        var userFilter = session.Can(Permission.ViewAllShifts) ? (int?)null : session.User.Id;
        History = await shifts.GetShiftsAsync(DateTime.Today.AddDays(-60), DateTime.Today.AddDays(1), userFilter);
        NotifyDrawerState();
    }

    [RelayCommand]
    private async Task OpenShiftAsync()
    {
        if (!TryParse(OpeningFloatText, out var amount))
        {
            Dialogs.Warning(Loc.T("Shift.EnterFloat"));
            return;
        }
        var amountLbp = 0m;
        if (ShowLbp && !string.IsNullOrWhiteSpace(OpeningFloatLbpText) && !TryParseLbp(OpeningFloatLbpText, out amountLbp))
        {
            Dialogs.Warning(Loc.T("Shift.EnterFloat"));
            return;
        }
        if (await RunAsync(() => shifts.OpenShiftAsync(session.User.Id, amount, amountLbp, session.TillId, session.TillName)))
        {
            Dialogs.Toast(Loc.T("Shift.Opened"));
            CashDrawer.OpenFor(DrawerReason.Shift); // to put the float in
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task AddMovementAsync()
    {
        if (session.CurrentShift is not { } shift) return;
        if (!(MovementCurrency == CashCurrency.Lbp ? TryParseLbp(MovementAmountText, out var amount) : TryParse(MovementAmountText, out amount)))
        {
            Dialogs.Warning(Loc.T("Shift.EnterAmount"));
            return;
        }
        if (await RunAsync(() => shifts.AddCashMovementAsync(shift.Id, MovementType, amount, MovementReason ?? "", session.User.Id, MovementCurrency)))
        {
            CashDrawer.OpenFor(DrawerReason.CashMovement);
            MovementAmountText = "";
            MovementReason = null;
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private void PrintXReport()
    {
        if (Summary is null) return;
        Dialogs.ShowDialog(new TextPreviewViewModel(Dialogs, print, Loc.T("Shift.XReport"), ReceiptBuilder.ShiftReport(Summary, settings.Current)));
    }

    [RelayCommand]
    private async Task CloseShiftAsync()
    {
        if (ActiveShift is not { } shift || Summary is null) return;
        var closingOther = ClosingShift is not null;
        if (!TryParse(CountedCashText, out var counted))
        {
            Dialogs.Warning(Loc.T("Shift.EnterCount"));
            return;
        }

        decimal? countedLbp = null;
        if (ShowLbp)
        {
            if (!TryParseLbp(CountedCashLbpText, out var lbp))
            {
                Dialogs.Warning(Loc.T("Shift.EnterCountLbp"));
                return;
            }
            countedLbp = lbp;
        }

        var message = Loc.T("Shift.CloseConfirm",
            CurrencyFormat.Format(Summary.ExpectedCash), CurrencyFormat.Format(counted), Balance(counted - Summary.ExpectedCash, CurrencyFormat.Format));
        if (countedLbp is { } c)
            message = Loc.T("Shift.CloseConfirmBoth",
                CurrencyFormat.Format(Summary.ExpectedCash), CurrencyFormat.Format(counted), Balance(counted - Summary.ExpectedCash, CurrencyFormat.Format),
                CurrencyFormat.Lbp(Summary.ExpectedCashLbp), CurrencyFormat.Lbp(c), Balance(c - Summary.ExpectedCashLbp, CurrencyFormat.Lbp));
        if (!Dialogs.Confirm(message, Loc.T("Shift.CloseShift"))) return;

        ShiftSummary? closed = null;
        if (!await RunAsync(async () => closed = await shifts.CloseShiftAsync(shift.Id, counted, CloseNotes, countedLbp, session.User.Id))) return;

        if (closingOther)
        {
            // The cash just counted stays in the drawer: start the next shift with it.
            if (shift.Id == session.OtherDrawer?.Id || shift.TillId == session.TillId)
            {
                OpeningFloatText = counted.ToString("0.00", CultureInfo.CurrentCulture);
                OpeningFloatLbpText = (countedLbp ?? 0).ToString("0", CultureInfo.CurrentCulture);
            }
            ClosingShift = null;
            session.OtherDrawer = null;
        }
        else
        {
            session.CurrentShift = null;
        }
        CashDrawer.OpenFor(DrawerReason.Shift); // to take the counted cash out
        CountedCashText = "";
        CountedCashLbpText = "";
        CloseNotes = null;
        // Stop showing the closed drawer's figures straight away, then load the page again before the Z report and
        // the backup, which can take a while on a slow PC.
        Summary = null;
        NotifyDrawerState();
        await RefreshAsync();
        Dialogs.ShowDialog(new TextPreviewViewModel(Dialogs, print, Loc.T("Shift.ZReport"), ReceiptBuilder.ShiftReport(closed!, settings.Current)));
        await BackUpAfterCloseAsync();
    }

    /// <summary>
    /// Runs without blocking the page: the next cashier may want to open their drawer straight away, and a backup can
    /// take minutes on a big database.
    /// </summary>
    private async Task BackUpAfterCloseAsync()
    {
        try
        {
            await backups.BackupAfterShiftCloseAsync(session.User.Id);
        }
        catch (Exception ex)
        {
            Dialogs.Warning(Loc.T("Shift.BackupFailed", ex.GetBaseException().Message));
        }
    }

    [RelayCommand]
    private async Task ViewHistoryReportAsync()
    {
        if (SelectedHistory is null) return;
        ShiftSummary? summary = null;
        if (await RunAsync(async () => summary = await shifts.GetSummaryAsync(SelectedHistory.Id)))
            Dialogs.ShowDialog(new TextPreviewViewModel(Dialogs, print, Loc.T("Shift.ShiftNumber", SelectedHistory.Id), ReceiptBuilder.ShiftReport(summary!, settings.Current)));
    }

    private static string Balance(decimal variance, Func<decimal, string> format) =>
        variance == 0
            ? Loc.T("Shift.Balances")
            : Loc.T(variance > 0 ? "Shift.Over" : "Shift.Short", format(Math.Abs(variance)));

    /// <summary>Cash in the drawer (float, count, pay in/out) is never negative: a minus sign is refused, as for pounds.</summary>
    private const NumberStyles CashStyle = NumberStyles.Number & ~NumberStyles.AllowLeadingSign & ~NumberStyles.AllowTrailingSign;

    private static bool TryParse(string? text, out decimal value) =>
        decimal.TryParse(text, CashStyle, CultureInfo.CurrentCulture, out value) ||
        decimal.TryParse(text, CashStyle, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// Pounds are whole numbers: any grouping marks are ignored ("500,000", "500.000", "500 000" are all 500,000), so
    /// a PC set to a comma-decimal format doesn't read 500,000 as 500. Arabic digits are accepted too.
    /// </summary>
    private static bool TryParseLbp(string? text, out decimal value)
    {
        value = 0;
        var t = (text ?? "").Trim();
        if (t.Length == 0 || t.StartsWith('-')) return false;
        var digits = new string(t.Select(c => c is >= '٠' and <= '٩' ? (char)('0' + (c - '٠')) : c).Where(char.IsAsciiDigit).ToArray());
        return digits.Length > 0 && decimal.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
