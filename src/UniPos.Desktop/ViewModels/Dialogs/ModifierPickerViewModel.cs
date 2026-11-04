using UniPos.Core.Entities;
using UniPos.Core.Localization;
using UniPos.Core.Menu;
using UniPos.Desktop.Converters;
using UniPos.Desktop.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace UniPos.Desktop.ViewModels.Dialogs;

/// <summary>One option button in the picker.</summary>
public sealed partial class OptionChoice(Modifier option, Action<OptionChoice> toggle) : ObservableObject
{
    public Modifier Option { get; } = option;
    public string Name => Option.Name;
    public string PriceText => Option.Price == 0 ? "" : "+" + CurrencyFormat.Format(Option.Price);
    public bool CanPick => !Option.SoldOut;
    public string? SoldOutText => Option.SoldOut ? Loc.T("Menu.SoldOut") : null;

    [ObservableProperty]
    public partial bool IsPicked { get; set; }

    [RelayCommand]
    private void Toggle() => toggle(this);
}

/// <summary>One question in the picker ("Size · pick 1") with its option buttons.</summary>
public sealed class GroupChoice
{
    public GroupChoice(ModifierGroup group, Action changed)
    {
        Group = group;
        Options = group.Options.Where(o => o.IsActive).OrderBy(o => o.SortOrder).ThenBy(o => o.Id)
            .Select(o => new OptionChoice(o, choice => Toggle(choice, changed))).ToList();
        Rule = (group.MinChoices, group.MaxChoices) switch
        {
            (1, 1) => Loc.T("Picker.PickOne"),
            (0, 1) => Loc.T("Picker.PickOneOptional"),
            (0, 0) => Loc.T("Picker.PickAny"),
            (0, var max) => Loc.T("Picker.PickUpTo", max),
            (var min, 0) => Loc.T("Picker.PickAtLeast", min),
            var (min, max) when min == max => Loc.T("Picker.PickExactly", min),
            var (min, max) => Loc.T("Picker.PickRange", min, max),
        };
        // A required single choice (a size) starts on its first option, the usual one: one tap less for most orders.
        if (group is { MinChoices: 1, MaxChoices: 1 } && Options.FirstOrDefault(o => o.CanPick) is { } usual) usual.IsPicked = true;
    }

    public ModifierGroup Group { get; }
    public string Name => Group.Name;
    public string Rule { get; }
    public IReadOnlyList<OptionChoice> Options { get; }

    private void Toggle(OptionChoice choice, Action changed)
    {
        if (!choice.CanPick) return;
        if (choice.IsPicked)
        {
            // A required single choice is changed by picking another, not emptied.
            if (!(Group.MaxChoices == 1 && Group.MinChoices == 1)) choice.IsPicked = false;
        }
        else if (Group.MaxChoices == 1)
        {
            foreach (var other in Options) other.IsPicked = false; // the new one replaces the old
            choice.IsPicked = true;
        }
        else if (Group.MaxChoices == 0 || Options.Count(o => o.IsPicked) < Group.MaxChoices)
        {
            choice.IsPicked = true;
        }
        changed();
    }
}

/// <summary>
/// Asks the options of an item as it is added to the order: each of its groups with big buttons, the price as options
/// are picked, and "Add" only once the choice is complete (a size picked, no more than three fillings...).
/// </summary>
public sealed partial class ModifierPickerViewModel : DialogViewModelBase
{
    private readonly Product _product;

    public ModifierPickerViewModel(IDialogService dialogs, Product product) : base(dialogs)
    {
        _product = product;
        Groups = ModifierRules.ActiveGroups(product).Select(g => new GroupChoice(g, Refresh)).ToList();
        Refresh();
    }

    public override string Title => _product.Name;
    public IReadOnlyList<GroupChoice> Groups { get; }

    /// <summary>The options picked, in the item's order.</summary>
    public IReadOnlyList<Modifier> Picked => ModifierRules.Ordered(_product, PickedIds);

    private List<int> PickedIds => Groups.SelectMany(g => g.Options).Where(o => o.IsPicked).Select(o => o.Option.Id).ToList();

    [ObservableProperty]
    public partial string PriceText { get; private set; } = "";

    /// <summary>What still has to be picked (or is picked too often); null when the item can be added.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string? Problem { get; private set; }

    private void Refresh()
    {
        Problem = ModifierRules.Problem(_product, PickedIds);
        PriceText = CurrencyFormat.Format(ModifierRules.Price(_product, Picked));
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add() => Close(true);

    private bool CanAdd() => Problem is null;

    [RelayCommand]
    private void Cancel() => Close(false);
}
