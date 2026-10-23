using System.Globalization;
using System.IO;
using ClothingStore.Core.Licensing;
using ClothingStore.Core.Localization;
using ClothingStore.Desktop.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClothingStore.Desktop.ViewModels.Dialogs;

/// <summary>Shows why the app needs a license, this PC's ID for the vendor, and takes the new key.</summary>
public sealed partial class ActivationViewModel(IDialogService dialogs, LicenseManager license, bool required) : DialogViewModelBase(dialogs)
{
    public override string Title => Loc.T("License.Title");
    public string MachineId => license.MachineId;
    public string Contact => license.Config.Contact;
    public bool HasContact => Contact.Length > 0;

    /// <summary>At startup there is no way past this screen without a valid key; from the menu it can be closed.</summary>
    public bool Required { get; } = required;

    public string Reason => license.Status.State switch
    {
        LicenseState.Missing => Loc.T("License.Reason.Missing"),
        LicenseState.TrialEnded => Loc.T("License.Reason.TrialEnded", LicenseStatus.TrialDays),
        LicenseState.Trial => Loc.T("License.TrialDaysLeft", license.Status.DaysLeft + 1, Contact),
        LicenseState.Invalid => Loc.T("License.Reason.Invalid"),
        LicenseState.WrongMachine => Loc.T("License.Reason.WrongMachine"),
        LicenseState.Expired => Loc.T("License.Reason.Expired", license.Status.License!.ExpiresOn.ToString("d", CultureInfo.CurrentCulture)),
        LicenseState.ExpiringSoon => Loc.T("License.ExpiresIn", license.Status.DaysLeft,
            license.Status.License!.ExpiresOn.ToString("d", CultureInfo.CurrentCulture), Contact),
        LicenseState.Valid when LicenseIssuer.IsLifetime(license.Status.License!.ExpiresOn) =>
            Loc.T("License.Reason.ValidLifetime", license.Status.License.Licensee),
        LicenseState.Valid => Loc.T("License.Reason.Valid", license.Status.License!.Licensee,
            license.Status.License.ExpiresOn.ToString("d", CultureInfo.CurrentCulture)),
        _ => Loc.T("License.NotConfigured"),
    };

    [ObservableProperty]
    public partial string Key { get; set; } = "";

    [ObservableProperty]
    public partial string? Error { get; set; }

    [RelayCommand]
    private void CopyMachineId()
    {
        try
        {
            System.Windows.Clipboard.SetText(MachineId);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    [RelayCommand]
    private void LoadFile()
    {
        var path = Dialogs.OpenFile(Loc.T("License.OpenTitle"), Loc.T("License.Filter"));
        if (path is null) return;
        try
        {
            Key = File.ReadAllText(path).Trim();
        }
        catch (IOException ex)
        {
            Error = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ActivateAsync()
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            Error = Loc.T("License.EnterKey");
            return;
        }
        LicenseStatus? status = null;
        if (!await RunAsync(async () => status = await license.ActivateAsync(Key))) return;
        Error = status!.State switch
        {
            LicenseState.Invalid => Loc.T("License.Reason.Invalid"),
            LicenseState.WrongMachine => Loc.T("License.Reason.WrongMachine"),
            LicenseState.Expired => Loc.T("License.Reason.Expired", status.License!.ExpiresOn.ToString("d", CultureInfo.CurrentCulture)),
            _ => null,
        };
        if (status.CanRun) Close(true);
    }

    [RelayCommand]
    private void Cancel() => Close(false);
}
