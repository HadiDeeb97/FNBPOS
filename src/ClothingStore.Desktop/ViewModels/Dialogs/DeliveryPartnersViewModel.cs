using System.Globalization;
using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Localization;
using ClothingStore.Data.Services;
using ClothingStore.Desktop.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClothingStore.Desktop.ViewModels.Dialogs;

/// <summary>A delivery company or driver in the list, with what it still owes the store.</summary>
public sealed record DeliveryPartnerRow(DeliveryPartner Partner, decimal Owed, int OrdersOwed);

/// <summary>Delivery companies and drivers: add them with their phone, contact, address and usual delivery fee.</summary>
public sealed partial class DeliveryPartnersViewModel(IDialogService dialogs, DeliveryService deliveries) : DialogViewModelBase(dialogs)
{
    public override System.Windows.Size? WindowSize => new(width: 900, height: 560);

    public override string Title => Loc.T("Partners.Title");

    public IReadOnlyList<DeliveryPartnerKind> Kinds { get; } = Enum.GetValues<DeliveryPartnerKind>();

    [ObservableProperty]
    public partial List<DeliveryPartnerRow> Rows { get; set; } = [];

    [ObservableProperty]
    public partial DeliveryPartnerRow? SelectedRow { get; set; }

    /// <summary>Working copy shown in the form.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNew))]
    public partial DeliveryPartner Editing { get; set; } = new();

    [ObservableProperty]
    public partial string DefaultFeeText { get; set; } = "";

    public bool IsNew => Editing.Id == 0;

    /// <summary>True when something was saved or removed, so the caller refreshes its lists.</summary>
    public bool Changed { get; private set; }

    /// <summary>Name of the partner saved last, so the online order window can pick it.</summary>
    public string? LastSavedName { get; private set; }

    public override Task OnOpenedAsync() => RunAsync(() => LoadAsync());

    partial void OnSelectedRowChanged(DeliveryPartnerRow? value)
    {
        if (value is null) return;
        var p = value.Partner;
        Editing = new DeliveryPartner
        {
            Id = p.Id, Name = p.Name, Kind = p.Kind, ContactName = p.ContactName, Phone = p.Phone, Phone2 = p.Phone2,
            Address = p.Address, DefaultFee = p.DefaultFee, Notes = p.Notes, IsActive = p.IsActive,
        };
        DefaultFeeText = p.DefaultFee is { } fee ? fee.ToString("0.##", CultureInfo.CurrentCulture) : "";
    }

    private async Task LoadAsync(int? selectId = null)
    {
        var partners = await deliveries.GetPartnersAsync(includeInactive: true);
        var balances = (await deliveries.GetBalancesAsync()).ToDictionary(b => b.Courier, StringComparer.OrdinalIgnoreCase);
        Rows = partners.Select(p => balances.TryGetValue(p.Name, out var b)
            ? new DeliveryPartnerRow(p, b.Owed, b.Orders)
            : new DeliveryPartnerRow(p, 0, 0)).ToList();
        SelectedRow = Rows.FirstOrDefault(r => r.Partner.Id == selectId) ?? Rows.FirstOrDefault();
        if (SelectedRow is null) New();
    }

    [RelayCommand]
    private void New()
    {
        SelectedRow = null;
        Editing = new DeliveryPartner();
        DefaultFeeText = "";
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        decimal? fee = null;
        if (!string.IsNullOrWhiteSpace(DefaultFeeText))
        {
            if (!(decimal.TryParse(DefaultFeeText, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed) ||
                  decimal.TryParse(DefaultFeeText, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed)) || parsed < 0)
            {
                Dialogs.Warning(Loc.T("OnlineSale.BadFee"));
                return;
            }
            fee = parsed;
        }
        Editing.DefaultFee = fee;
        var saved = await deliveries.SavePartnerAsync(Editing);
        Changed = true;
        LastSavedName = saved.IsActive ? saved.Name : null;
        await LoadAsync(saved.Id);
        Dialogs.Toast(Loc.T("Common.Saved"));
    });

    [RelayCommand]
    private Task DeleteAsync() => RunAsync(async () =>
    {
        if (Editing.Id == 0 || !Dialogs.Confirm(Loc.T("Partners.DeleteConfirm", Editing.Name))) return;
        if (!await deliveries.DeletePartnerAsync(Editing.Id))
            Dialogs.Toast(Loc.T("Partners.Deactivated"), ToastKind.Info);
        Changed = true;
        await LoadAsync();
    });

    [RelayCommand]
    private void Done() => Close(Changed);
}
