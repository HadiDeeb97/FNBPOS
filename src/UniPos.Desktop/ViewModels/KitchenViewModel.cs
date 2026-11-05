using UniPos.Core.Localization;
using UniPos.Core.Text;
using UniPos.Data.Services;
using UniPos.Desktop.Infrastructure;
using UniPos.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace UniPos.Desktop.ViewModels;

/// <summary>A ticket in the Kitchen page's list.</summary>
public sealed record TicketListRow(TicketRow Row)
{
    public string Time => DateText.Time(Row.Ticket.CreatedAt);
    public string Order => Row.Ticket.OrderNumber is { } n ? n.ToString() : Row.ReceiptNumber;
    public string Station => Row.Station;
    public string Items => string.Join(", ", Row.Ticket.Lines.Select(l => $"{l.Quantity} × {l.ProductName}"));
    public bool IsPrinted => Row.Ticket.IsPrinted;
    public string Status => Row.Ticket.IsPrinted
        ? Loc.T("Kitchen.Printed", DateText.Time(Row.Ticket.PrintedAt!.Value))
        : Row.Ticket.PrintError ?? Loc.T("Kitchen.Waiting");
}

/// <summary>
/// Today's kitchen tickets: the ones that didn't print first (printer off, out of paper, none set), to print again
/// once the printer is back, and any ticket can be printed again for the kitchen.
/// </summary>
public sealed partial class KitchenViewModel(IDialogService dialogs, KitchenService kitchen, KitchenPrinter printer)
    : ViewModelBase(dialogs), IPageViewModel
{
    public string Title => Loc.T("Nav.Kitchen");

    /// <summary>Tickets were printed from here: the number of waiting ones in the menu changes.</summary>
    public static event EventHandler? Printed;

    [ObservableProperty]
    public partial List<TicketListRow> Tickets { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    [NotifyCanExecuteChangedFor(nameof(PrintAgainCommand))]
    public partial TicketListRow? SelectedTicket { get; set; }

    [ObservableProperty]
    public partial bool WaitingOnly { get; set; }

    public int WaitingCount => Tickets.Count(t => !t.IsPrinted);

    /// <summary>The selected ticket as it prints.</summary>
    public string Preview => SelectedTicket is null ? "" : string.Join("\n", printer.Preview(SelectedTicket.Row));

    public async Task OnNavigatedToAsync() => await RefreshAsync();

    partial void OnWaitingOnlyChanged(bool value) => _ = RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var selected = SelectedTicket?.Row.Ticket.Id;
        try
        {
            var today = DateTime.Today;
            Tickets = (await kitchen.GetAsync(today, today.AddDays(1), WaitingOnly)).Select(r => new TicketListRow(r)).ToList();
            SelectedTicket = Tickets.FirstOrDefault(t => t.Row.Ticket.Id == selected) ?? Tickets.FirstOrDefault();
            OnPropertyChanged(nameof(WaitingCount));
        }
        catch (Exception ex)
        {
            ReportLoadError(Loc.T("Kitchen.LoadFailed"), ex);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task PrintAgainAsync() => RunAsync(async () =>
    {
        if (SelectedTicket is not { } ticket) return;
        await Report(await printer.PrintAsync([ticket.Row]), 1);
    });

    /// <summary>Every ticket of today that hasn't printed yet, e.g. once the printer is back on.</summary>
    [RelayCommand]
    private Task PrintWaitingAsync() => RunAsync(async () =>
    {
        var waiting = Tickets.Where(t => !t.IsPrinted).Select(t => t.Row).ToList();
        if (waiting.Count == 0) return;
        await Report(await printer.PrintAsync(waiting), waiting.Count);
    });

    private async Task Report(int failed, int tried)
    {
        if (failed == 0) Dialogs.Toast(Loc.T("Kitchen.PrintedCount", tried));
        else Dialogs.Toast(Loc.T("Kitchen.NotPrinted", failed), ToastKind.Warning);
        await RefreshAsync();
        Printed?.Invoke(this, EventArgs.Empty);
    }

    private bool HasSelection() => SelectedTicket is not null;
}
