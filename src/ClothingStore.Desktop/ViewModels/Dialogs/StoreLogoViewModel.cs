using System.IO;
using System.Windows.Media;
using ClothingStore.Core;
using ClothingStore.Core.Localization;
using ClothingStore.Data.Services;
using ClothingStore.Desktop.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClothingStore.Desktop.ViewModels.Dialogs;

/// <summary>Managers and admins pick the store logo; it becomes the icon of every window on every till.</summary>
public sealed partial class StoreLogoViewModel(IDialogService dialogs, BrandingService branding, Session session) : DialogViewModelBase(dialogs)
{
    private byte[]? _pending;
    private bool _changed;

    public override string Title => Loc.T("Logo.Title");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    public partial ImageSource? Preview { get; set; } = Branding.Instance.Icon;

    public bool HasPreview => Preview is not null;

    [RelayCommand]
    private void Choose()
    {
        var path = Dialogs.OpenFile(Loc.T("Logo.ChooseTitle"), Loc.T("Logo.Filter"));
        if (path is null) return;
        try
        {
            _pending = Branding.NormalizeLogo(File.ReadAllBytes(path));
            Preview = Branding.Decode(_pending);
            _changed = true;
        }
        catch (BusinessRuleException ex)
        {
            Dialogs.Warning(ex.Message);
        }
        catch (IOException ex)
        {
            Dialogs.Warning(ex.Message);
        }
    }

    [RelayCommand]
    private void Remove()
    {
        _pending = null;
        Preview = null;
        _changed = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!_changed)
        {
            Close(false);
            return;
        }
        DateTime? version = null;
        if (!await RunAsync(async () => version = await branding.SetLogoAsync(_pending, session.User.Id))) return;
        Branding.Instance.Apply(_pending, version);
        Dialogs.Toast(Loc.T(_pending is null ? "Logo.Removed" : "Logo.Saved"));
        Close(true);
    }

    [RelayCommand]
    private void Cancel() => Close(false);
}
