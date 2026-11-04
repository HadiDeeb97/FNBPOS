using System.Collections.ObjectModel;
using UniPos.Core.Entities;
using UniPos.Core.Localization;
using UniPos.Core.Security;
using UniPos.Data.Services;
using UniPos.Desktop.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace UniPos.Desktop.ViewModels;

/// <summary>An option being edited on the Modifiers page.</summary>
public sealed partial class OptionRow : ObservableObject
{
    public int Id { get; init; }
    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial decimal Price { get; set; }
    [ObservableProperty] public partial decimal Cost { get; set; }
    [ObservableProperty] public partial bool SoldOut { get; set; }

    public Modifier ToEntity() => new() { Id = Id, Name = Name ?? "", Price = Price, Cost = Cost, SoldOut = SoldOut };
}

/// <summary>A group in the list on the left.</summary>
public sealed record GroupRow(ModifierGroup Group, int ItemCount)
{
    public string Name => Group.Name;
    public string Rule => ModifierGroupsViewModel.RuleText(Group);
    public string Options => string.Join(", ", Group.Options.Select(o => o.Name));
    public string ItemsText => Loc.T("Modifiers.UsedBy", ItemCount);
}

/// <summary>
/// Modifier groups ("Size", "Fillings", "Remove") with their options and prices, shared by the menu items that ask
/// them. Which items ask a group is chosen in the item's editor.
/// </summary>
public sealed partial class ModifierGroupsViewModel(IDialogService dialogs, ModifierService modifiers, Session session)
    : ViewModelBase(dialogs), IPageViewModel
{
    public string Title => Loc.T("Nav.Modifiers");
    public bool CanEdit => session.Can(Permission.ManageProducts);

    /// <summary>"Pick 1", "Optional · pick up to 3"...: what the till asks.</summary>
    public static string RuleText(ModifierGroup g) => (g.MinChoices, g.MaxChoices) switch
    {
        (1, 1) => Loc.T("Picker.PickOne"),
        (0, 1) => Loc.T("Picker.PickOneOptional"),
        (0, 0) => Loc.T("Picker.PickAny"),
        (0, var max) => Loc.T("Picker.PickUpTo", max),
        (var min, 0) => Loc.T("Picker.PickAtLeast", min),
        var (min, max) when min == max => Loc.T("Picker.PickExactly", min),
        var (min, max) => Loc.T("Picker.PickRange", min, max),
    };

    [ObservableProperty]
    public partial List<GroupRow> Groups { get; set; } = [];

    [ObservableProperty]
    public partial GroupRow? SelectedGroup { get; set; }

    // The form on the right.
    private int _editingId;
    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial int MinChoices { get; set; }
    [ObservableProperty] public partial int MaxChoices { get; set; } = 1;
    [ObservableProperty] public partial bool IsActive { get; set; } = true;
    public ObservableCollection<OptionRow> Options { get; } = [];

    [ObservableProperty]
    public partial OptionRow? SelectedOption { get; set; }

    public bool IsNew => _editingId == 0;

    public async Task OnNavigatedToAsync() => await LoadAsync();

    partial void OnSelectedGroupChanged(GroupRow? value)
    {
        if (value is not null) Edit(value.Group);
    }

    private void Edit(ModifierGroup? group)
    {
        _editingId = group?.Id ?? 0;
        Name = group?.Name ?? "";
        MinChoices = group?.MinChoices ?? 0;
        MaxChoices = group?.MaxChoices ?? 1;
        IsActive = group?.IsActive ?? true;
        Options.Clear();
        foreach (var o in group?.Options ?? [])
            Options.Add(new OptionRow { Id = o.Id, Name = o.Name, Price = o.Price, Cost = o.Cost, SoldOut = o.SoldOut });
        if (group is null) Options.Add(new OptionRow());
        OnPropertyChanged(nameof(IsNew));
    }

    private async Task LoadAsync(int? selectId = null)
    {
        try
        {
            var groups = await modifiers.GetGroupsAsync(includeInactive: true);
            var counts = await modifiers.CountItemsAsync();
            Groups = groups.Select(g => new GroupRow(g, counts.GetValueOrDefault(g.Id))).ToList();
            SelectedGroup = Groups.FirstOrDefault(g => g.Group.Id == (selectId ?? _editingId)) ?? Groups.FirstOrDefault();
            if (SelectedGroup is null) Edit(null);
        }
        catch (Exception ex)
        {
            ReportLoadError(Loc.T("Modifiers.LoadFailed"), ex);
        }
    }

    [RelayCommand]
    private void New()
    {
        SelectedGroup = null;
        Edit(null);
    }

    /// <summary>Quick rules: one required answer (a size), or any number of optional ones (extras, "no onions").</summary>
    [RelayCommand]
    private void UseRule(string rule)
    {
        (MinChoices, MaxChoices) = rule switch
        {
            "one" => (1, 1),
            "optional-one" => (0, 1),
            _ => (0, 0),
        };
    }

    [RelayCommand]
    private void AddOption()
    {
        var row = new OptionRow();
        Options.Add(row);
        SelectedOption = row;
    }

    [RelayCommand]
    private void RemoveOption(OptionRow? row)
    {
        if (row is not null) Options.Remove(row);
    }

    [RelayCommand]
    private void MoveOptionUp(OptionRow? row)
    {
        var i = row is null ? -1 : Options.IndexOf(row);
        if (i > 0) Options.Move(i, i - 1);
    }

    [RelayCommand]
    private void MoveOptionDown(OptionRow? row)
    {
        var i = row is null ? -1 : Options.IndexOf(row);
        if (i >= 0 && i < Options.Count - 1) Options.Move(i, i + 1);
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        var saved = await modifiers.SaveGroupAsync(new ModifierGroup
        {
            Id = _editingId, Name = Name ?? "", MinChoices = MinChoices, MaxChoices = MaxChoices, IsActive = IsActive,
            Options = Options.Select(o => o.ToEntity()).ToList(),
        });
        _editingId = saved.Id;
        await LoadAsync(saved.Id);
        Dialogs.Toast(Loc.T("Common.Saved"));
    });

    [RelayCommand]
    private Task DeleteAsync() => RunAsync(async () =>
    {
        if (SelectedGroup is not { } row || _editingId == 0) return;
        var question = row.ItemCount > 0 ? Loc.T("Modifiers.DeleteUsedConfirm", row.Name, row.ItemCount) : Loc.T("Modifiers.DeleteConfirm", row.Name);
        if (!Dialogs.Confirm(question)) return;
        await modifiers.DeleteGroupAsync(row.Group.Id);
        _editingId = 0;
        await LoadAsync();
        Dialogs.Toast(Loc.T("Modifiers.Deleted", row.Name), ToastKind.Info);
    });
}
