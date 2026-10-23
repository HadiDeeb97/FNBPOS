using System.Globalization;
using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Security;
using ClothingStore.Data.Services;
using ClothingStore.Desktop.Infrastructure;
using ClothingStore.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClothingStore.Core.Localization;

namespace ClothingStore.Desktop.ViewModels.Dialogs;

public enum PromptKind { Text, Decimal, Integer }

public sealed partial class PromptViewModel(IDialogService dialogs, string title, string message, string initialValue, PromptKind kind)
    : DialogViewModelBase(dialogs)
{
    public override string Title { get; } = title;
    public string Message { get; } = message;

    [ObservableProperty]
    public partial string Value { get; set; } = initialValue;

    public decimal DecimalValue { get; private set; }

    [RelayCommand]
    private void Ok()
    {
        if (kind == PromptKind.Text)
        {
            if (string.IsNullOrWhiteSpace(Value)) return;
            Close(true);
            return;
        }

        if (!decimal.TryParse(Value, NumberStyles.Number, CultureInfo.CurrentCulture, out var number) &&
            !decimal.TryParse(Value, NumberStyles.Number, CultureInfo.InvariantCulture, out number))
        {
            Dialogs.Warning(Loc.T("Prompt.InvalidNumber"));
            return;
        }
        if (kind == PromptKind.Integer && number != Math.Truncate(number))
        {
            Dialogs.Warning(Loc.T("Prompt.InvalidWholeNumber"));
            return;
        }
        DecimalValue = number;
        Close(true);
    }

    [RelayCommand]
    private void Cancel() => Close(false);
}

public sealed partial class ChangePasswordViewModel(IDialogService dialogs, UserService users, Session session, bool forced)
    : DialogViewModelBase(dialogs)
{
    public override string Title => Loc.T(IsForced ? "Password.ChooseNew" : "Shell.ChangePassword");
    public bool IsForced { get; } = forced;

    public string CurrentPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
    public string ConfirmPassword { get; set; } = "";

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (NewPassword != ConfirmPassword) throw new BusinessRuleException(Loc.T("Password.Mismatch"));
        await users.ChangePasswordAsync(session.User.Id, CurrentPassword, NewPassword);
        session.User.MustChangePassword = false;
        Close(true);
    });

    [RelayCommand]
    private void Cancel() => Close(false);
}

/// <summary>Asks a manager to enter their credentials to authorise an action.</summary>
public sealed partial class ManagerApprovalViewModel(IDialogService dialogs, UserService users, string reason, Permission permission)
    : DialogViewModelBase(dialogs)
{
    public override string Title => Loc.T("Approval.Title");
    public string Reason { get; } = reason;

    [ObservableProperty]
    public partial string Username { get; set; } = "";

    public string Password { get; set; } = "";

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public User? ApprovedBy { get; private set; }

    [RelayCommand]
    private Task ApproveAsync() => RunAsync(async () =>
    {
        ApprovedBy = await users.AuthorizeAsync(Username, Password, permission);
        if (ApprovedBy is null)
        {
            ErrorMessage = Loc.T("Approval.Invalid");
            return;
        }
        Close(true);
    });

    [RelayCommand]
    private void Cancel() => Close(false);
}

/// <summary>
/// Shows monospaced text (receipt, Z report, PO) with a Print button and how many copies to print. Print goes to this
/// PC's default receipt printer when one is set; "Choose printer" always asks.
/// </summary>
public sealed partial class TextPreviewViewModel(IDialogService dialogs, PrintService print, string title, IReadOnlyList<string> lines,
    string? closeText = null, int copies = 1, byte[]? logo = null) : DialogViewModelBase(dialogs)
{
    public override string Title { get; } = title;

    /// <summary>Store logo shown (and printed) above the receipt, when the setting is on; null otherwise.</summary>
    public System.Windows.Media.ImageSource? Logo { get; } = logo is { Length: > 0 } ? Branding.Decode(logo) : null;
    public string Text { get; } = string.Join(Environment.NewLine, lines);
    public string CloseText { get; } = closeText ?? Loc.T("Common.Close");

    [ObservableProperty]
    public partial int Copies { get; set; } = Math.Clamp(copies, 1, 99);

    partial void OnCopiesChanged(int value)
    {
        if (value is < 1 or > 99) Copies = Math.Clamp(value, 1, 99);
        OnPropertyChanged(nameof(PrintButtonText));
    }

    /// <summary>"Print" or "Print 3 copies", so the count is visible on the button too.</summary>
    public string PrintButtonText => Copies <= 1 ? Loc.T("Common.Print") : Loc.T("Print.PrintCopies", Copies);

    /// <summary>"Printer: …" when receipts go straight to a default printer, else null.</summary>
    public string? PrinterText { get; } = LocalPreferences.Current.ReceiptPrinter is { Length: > 0 } p
        ? Loc.T("Print.PrinterIs", p)
        : Loc.T("Print.NoDefaultPrinter");

    [RelayCommand]
    private void MoreCopies() => Copies++;

    [RelayCommand]
    private void FewerCopies() => Copies--;

    [RelayCommand]
    private void Print() => Send(choosePrinter: false);

    [RelayCommand]
    private void PrintTo() => Send(choosePrinter: true);

    private void Send(bool choosePrinter)
    {
        try
        {
            if (print.PrintText(lines, Title, Copies, choosePrinter, logo))
                Dialogs.Toast(Loc.T("Print.Sent", Copies));
        }
        catch (Exception ex)
        {
            Dialogs.Error(Loc.T("Common.PrintFailed"), ex);
        }
    }

    [RelayCommand]
    private void Done() => Close(true);
}

public sealed partial class CustomerEditorViewModel : DialogViewModelBase
{
    private readonly CustomerService _customers;

    public CustomerEditorViewModel(IDialogService dialogs, CustomerService customers, Customer? existing = null) : base(dialogs)
    {
        _customers = customers;
        Customer = existing is null
            ? new Customer()
            : new Customer
            {
                Id = existing.Id, FirstName = existing.FirstName, LastName = existing.LastName, Phone = existing.Phone,
                Email = existing.Email, Birthday = existing.Birthday, Notes = existing.Notes, IsActive = existing.IsActive,
                LoyaltyPoints = existing.LoyaltyPoints, StoreCredit = existing.StoreCredit, Address = existing.Address, RegionId = existing.RegionId,
            };
    }

    public override string Title => Loc.T(Customer.Id == 0 ? "Customers.New" : "Customers.Edit");
    public Customer Customer { get; }
    public Customer? Saved { get; private set; }

    /// <summary>States / governorates to pick from (Lebanon's to start with; "Add state" adds more).</summary>
    [ObservableProperty]
    public partial List<Region> Regions { get; set; } = [];

    [ObservableProperty]
    public partial Region? SelectedRegion { get; set; }

    partial void OnSelectedRegionChanged(Region? value) => Customer.RegionId = value?.Id;

    public override async Task OnOpenedAsync()
    {
        try
        {
            Regions = await _customers.GetRegionsAsync();
            SelectedRegion = Regions.FirstOrDefault(r => r.Id == Customer.RegionId);
        }
        catch (Exception ex)
        {
            Dialogs.Error(Loc.T("Common.SomethingWentWrong"), ex);
        }
    }

    [RelayCommand]
    private async Task AddRegionAsync()
    {
        var name = Dialogs.Prompt(Loc.T("Customers.AddState"), Loc.T("Customers.AddStatePrompt"));
        if (string.IsNullOrWhiteSpace(name)) return;
        Region? region = null;
        if (!await RunAsync(async () => region = await _customers.AddRegionAsync(name))) return;
        Regions = await _customers.GetRegionsAsync();
        SelectedRegion = Regions.FirstOrDefault(r => r.Id == region!.Id);
    }

    [RelayCommand]
    private void ClearRegion() => SelectedRegion = null;

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        Saved = await _customers.SaveAsync(Customer);
        Close(true);
    });

    [RelayCommand]
    private void Cancel() => Close(false);
}

public sealed partial class CustomerPickerViewModel(IDialogService dialogs, CustomerService customers) : DialogViewModelBase(dialogs)
{
    public override System.Windows.Size? WindowSize => new(width: 680, height: 480);

    public override string Title => Loc.T("Customers.Select");

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial List<Customer> Results { get; set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectCommand))]
    public partial Customer? SelectedCustomer { get; set; }

    public Customer? Selected { get; private set; }

    private readonly LatestSearch _search = new();

    public override Task OnOpenedAsync() => SearchAsync();

    partial void OnSearchTextChanged(string value) => _ = SearchAsync(immediately: false);

    [RelayCommand]
    private async Task SearchAsync() => await SearchAsync(immediately: true);

    private async Task SearchAsync(bool immediately)
    {
        var text = SearchText;
        try
        {
            Func<CancellationToken, Task<List<Customer>>> load = ct => customers.SearchAsync(text, max: 100, ct: ct);
            Action<List<Customer>> apply = found =>
            {
                Results = found;
                SelectedCustomer = Results.FirstOrDefault();
            };
            await (immediately ? _search.RunNowAsync(load, apply) : _search.RunAsync(load, apply));
        }
        catch (Exception ex)
        {
            ReportLoadError(Loc.T("Customers.SearchFailed"), ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSelect))]
    private void Select()
    {
        Selected = SelectedCustomer;
        Close(true);
    }

    private bool CanSelect() => SelectedCustomer is not null;

    [RelayCommand]
    private void New()
    {
        var editor = new CustomerEditorViewModel(Dialogs, customers, new Customer { Phone = LooksLikePhone(SearchText) ? SearchText.Trim() : null });
        if (!Dialogs.ShowDialog(editor) || editor.Saved is null) return;
        Selected = editor.Saved;
        Close(true);
    }

    [RelayCommand]
    private void Cancel() => Close(false);

    private static bool LooksLikePhone(string text) => text.Length > 0 && text.All(c => char.IsDigit(c) || c is '+' or '-' or ' ');
}

public sealed partial class HeldSalesViewModel(IDialogService dialogs, SalesService sales) : DialogViewModelBase(dialogs)
{
    public override System.Windows.Size? WindowSize => new(width: 540, height: 400);

    public override string Title => Loc.T("Held.Title");

    [ObservableProperty]
    public partial List<HeldSale> Items { get; set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResumeCommand), nameof(DeleteCommand))]
    public partial HeldSale? SelectedItem { get; set; }

    public HeldSale? Chosen { get; private set; }

    public override async Task OnOpenedAsync()
    {
        Items = await sales.GetHeldAsync();
        SelectedItem = Items.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Resume()
    {
        Chosen = SelectedItem;
        Close(true);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task DeleteAsync() => RunAsync(async () =>
    {
        if (SelectedItem is null || !Dialogs.Confirm(Loc.T("Held.DiscardConfirm", SelectedItem.Label))) return;
        await sales.DeleteHeldAsync(SelectedItem.Id);
        await OnOpenedAsync();
    });

    private bool HasSelection() => SelectedItem is not null;

    [RelayCommand]
    private void Cancel() => Close(false);
}
