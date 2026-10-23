using System.Globalization;
using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Localization;
using ClothingStore.Data.Services;
using ClothingStore.Desktop.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClothingStore.Desktop.ViewModels.Dialogs;

/// <summary>Marks the sale on the register as an online order: where it came from, delivery fee, address/notes.</summary>
public sealed partial class OnlineSaleViewModel : DialogViewModelBase
{
    public OnlineSaleViewModel(
        IDialogService dialogs, DeliveryService deliveries, SalesChannel channel, decimal deliveryFee, string? notes, string? courier,
        IReadOnlyList<DeliveryPartner> partners, string? deliveryReference = null)
        : base(dialogs)
    {
        _deliveries = deliveries;
        DeliveryReference = deliveryReference;
        var isNew = channel == SalesChannel.InStore;
        Partners = WithCurrent(partners, courier);
        SelectedPartner = Find(courier ?? (isNew ? LocalPreferences.Current.LastCourier : null));
        Channel = isNew ? SalesChannel.WhatsApp : channel;
        var fee = isNew ? SelectedPartner?.DefaultFee ?? LocalPreferences.Current.LastDeliveryFee ?? 0 : deliveryFee;
        DeliveryFeeText = fee == 0 ? "" : fee.ToString("0.##", CultureInfo.CurrentCulture);
        Notes = notes;
        _ready = true;
    }

    private readonly DeliveryService _deliveries;
    private readonly bool _ready;

    /// <summary>Saved delivery companies and drivers to choose from (search by typing; nothing else can be entered).</summary>
    [ObservableProperty]
    public partial List<DeliveryPartner> Partners { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CourierInfo), nameof(HasPartner))]
    public partial DeliveryPartner? SelectedPartner { get; set; }

    public bool HasPartner => SelectedPartner is not null;

    /// <summary>Name saved on the sale.</summary>
    public string? Courier => SelectedPartner?.Name;

    /// <summary>An order already handed to a company that has since been switched off still shows it.</summary>
    private static List<DeliveryPartner> WithCurrent(IReadOnlyList<DeliveryPartner> partners, string? courier)
    {
        var list = partners.ToList();
        if (!string.IsNullOrWhiteSpace(courier) && list.All(p => !string.Equals(p.Name, courier, StringComparison.OrdinalIgnoreCase)))
            list.Add(new DeliveryPartner { Name = courier.Trim() });
        return list;
    }

    private DeliveryPartner? Find(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : Partners.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Kind, contact and phones of the chosen company / driver.</summary>
    public string? CourierInfo => SelectedPartner is { } p
        ? string.Join("  ·  ", new[] { p.Id == 0 ? null : Loc.T($"Enum.DeliveryPartnerKind.{p.Kind}"), p.ContactName, p.Phone, p.Phone2 }
            .Where(x => !string.IsNullOrWhiteSpace(x)))
        : null;

    partial void OnSelectedPartnerChanged(DeliveryPartner? oldValue, DeliveryPartner? newValue)
    {
        // Picking a partner fills in its usual fee (not while the window is opening with the order's own fee, and not
        // when the same partner is picked again after the list reloads, which would wipe a fee the cashier typed).
        var samePartner = oldValue is not null && newValue is not null
                          && string.Equals(oldValue.Name, newValue.Name, StringComparison.OrdinalIgnoreCase);
        if (_ready && !samePartner && newValue?.DefaultFee is { } fee)
            DeliveryFeeText = fee == 0 ? "" : fee.ToString("0.##", CultureInfo.CurrentCulture);
    }

    [RelayCommand]
    private void ClearPartner() => SelectedPartner = null;

    /// <summary>Add or edit companies and drivers without leaving the order.</summary>
    [RelayCommand]
    private async Task ManagePartnersAsync()
    {
        var chosen = SelectedPartner?.Name;
        var manager = new DeliveryPartnersViewModel(Dialogs, _deliveries);
        Dialogs.ShowDialog(manager);
        if (!manager.Changed) return;
        try
        {
            Partners = WithCurrent(await _deliveries.GetPartnersAsync(), chosen);
            SelectedPartner = Find(manager.LastSavedName ?? chosen);
        }
        catch (Exception ex)
        {
            Dialogs.Error(Loc.T("Common.SomethingWentWrong"), ex);
        }
    }

    public override string Title => Loc.T("OnlineSale.Title");

    public IReadOnlyList<SalesChannel> Channels { get; } = Enum.GetValues<SalesChannel>().Where(c => c != SalesChannel.InStore).ToList();

    [ObservableProperty]
    public partial SalesChannel Channel { get; set; }

    [ObservableProperty]
    public partial string DeliveryFeeText { get; set; }

    [ObservableProperty]
    public partial string? Notes { get; set; }

    /// <summary>The delivery company's invoice / tracking number: type it or scan the barcode on its slip.</summary>
    [ObservableProperty]
    public partial string? DeliveryReference { get; set; }

    public decimal DeliveryFee { get; private set; }

    [RelayCommand]
    private void PickChannel(SalesChannel channel) => Channel = channel;

    [RelayCommand]
    private void Save()
    {
        decimal fee = 0;
        if (!string.IsNullOrWhiteSpace(DeliveryFeeText) &&
            !(decimal.TryParse(DeliveryFeeText, NumberStyles.Number, CultureInfo.CurrentCulture, out fee) ||
              decimal.TryParse(DeliveryFeeText, NumberStyles.Number, CultureInfo.InvariantCulture, out fee)) || fee < 0)
        {
            Dialogs.Warning(Loc.T("OnlineSale.BadFee"));
            return;
        }
        DeliveryFee = Money.Round(fee);
        Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
        DeliveryReference = string.IsNullOrWhiteSpace(DeliveryReference) ? null : DeliveryReference.Trim().ToUpperInvariant();
        LocalPreferences.Current.LastDeliveryFee = DeliveryFee;
        LocalPreferences.Current.LastCourier = Courier;
        LocalPreferences.Current.Save();
        Close(true);
    }

    [RelayCommand]
    private void Cancel() => Close(false);
}
