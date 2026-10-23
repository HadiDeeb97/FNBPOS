using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Localization;
using ClothingStore.Core.Pricing;
using ClothingStore.Data.Services;
using ClothingStore.Desktop.Converters;
using ClothingStore.Desktop.Infrastructure;
using ClothingStore.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClothingStore.Desktop.ViewModels.Dialogs;

public sealed partial class LabelRowViewModel(ProductVariant variant, int copies) : ObservableObject
{
    public ProductVariant Variant { get; } = variant;
    public string Name => Variant.Product?.Name ?? Variant.DisplayName;

    [ObservableProperty]
    public partial int Copies { get; set; } = copies;

    partial void OnCopiesChanged(int value)
    {
        if (value < 0) Copies = 0;
    }
}

public sealed record LabelPresetOption(LabelPreset Preset, string Name);

/// <summary>
/// Price labels for any number of items: started from a selection (products or stock rows), more can be scanned in.
/// Label size, layout and what is printed are adjustable with a live preview, and remembered on this PC.
/// </summary>
public sealed partial class LabelPrintViewModel : DialogViewModelBase
{
    public override System.Windows.Size? WindowSize => new(width: 1060, height: 660);

    private readonly PrintService _print;
    private readonly ProductService _products;
    private readonly StoreSettings _settings;

    public LabelPrintViewModel(
        IDialogService dialogs, PrintService print, ProductService products, SettingsService settings, IEnumerable<ProductVariant> variants)
        : base(dialogs)
    {
        _print = print;
        _products = products;
        _settings = settings.Current;
        Options = LocalPreferences.Current.Labels?.Clone() ?? new LabelOptions();
        Presets = LabelPreset.All.Select(p => new LabelPresetOption(p, PresetName(p))).ToList();
        foreach (var v in variants) AddVariant(v, 1);
        Rows.CollectionChanged += (_, _) => OnRowsChanged();
    }

    public override string Title => Loc.T("Labels.Title");
    public LabelOptions Options { get; }
    public ObservableCollection<LabelRowViewModel> Rows { get; } = [];
    public IReadOnlyList<LabelPresetOption> Presets { get; }
    public bool LbpAvailable => _settings.ActiveLbpRate > 0;

    /// <summary>Raised when anything that changes the look of a label changes, so the view redraws the preview.</summary>
    public event EventHandler? PreviewChanged;

    // ---- Items ------------------------------------------------------------------------------

    [ObservableProperty]
    public partial string AddText { get; set; } = "";

    [ObservableProperty]
    public partial string CopiesForAllText { get; set; } = "1";

    public int TotalLabels => Rows.Sum(r => Math.Max(0, r.Copies));
    public bool HasRows => Rows.Count > 0;

    [RelayCommand]
    private async Task AddAsync()
    {
        var text = AddText.Trim();
        if (text.Length == 0) return;

        List<ProductVariant> found = [];
        if (!await RunAsync(async () =>
            {
                var exact = await _products.FindByCodeAsync(text);
                found = exact is not null ? [exact] : await _products.SearchVariantsAsync(text, 60);
            }))
            return;

        if (found.Count == 0)
        {
            Dialogs.Warning(Loc.T("Labels.NothingFound", text));
            return;
        }
        foreach (var v in found) AddVariant(v, 1);
        AddText = "";
        if (found.Count > 1) Dialogs.Toast(Loc.T("Labels.AddedMany", found.Count));
    }

    [RelayCommand]
    private void SetAllCopies()
    {
        if (!int.TryParse(CopiesForAllText, out var n) || n < 0)
        {
            Dialogs.Warning(Loc.T("Labels.EnterCopies"));
            return;
        }
        foreach (var row in Rows) row.Copies = n;
    }

    [RelayCommand]
    private void CopiesFromStock()
    {
        foreach (var row in Rows) row.Copies = Math.Max(0, row.Variant.StockQuantity);
    }

    [RelayCommand]
    private void RemoveRow(LabelRowViewModel row) => Rows.Remove(row);

    [RelayCommand]
    private void ClearRows()
    {
        if (Rows.Count > 0 && Dialogs.Confirm(Loc.T("Labels.ClearConfirm"))) Rows.Clear();
    }

    private void AddVariant(ProductVariant variant, int copies)
    {
        var existing = Rows.FirstOrDefault(r => r.Variant.Id == variant.Id);
        if (existing is not null)
        {
            existing.Copies += copies;
            return;
        }
        var row = new LabelRowViewModel(variant, copies);
        row.PropertyChanged += (_, _) => OnRowsChanged();
        Rows.Add(row);
    }

    private void OnRowsChanged()
    {
        OnPropertyChanged(nameof(TotalLabels));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(Summary));
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- Layout -----------------------------------------------------------------------------

    public LabelPresetOption? SelectedPreset
    {
        get => Presets.FirstOrDefault(p => p.Preset.Id == Options.Preset) ?? Presets[^1];
        set
        {
            if (value is null || value.Preset.Id == Options.Preset) return;
            value.Preset.ApplyTo(Options);
            OnLayoutChanged(all: true);
        }
    }

    public bool Sheet { get => Options.Sheet; set => SetOption(Options.Sheet != value, () => Options.Sheet = value, custom: true); }
    public bool LabelPrinter { get => !Options.Sheet; set => Sheet = !value; }
    public double WidthMm { get => Options.WidthMm; set => SetOption(Options.WidthMm != value && value is >= 10 and <= 300, () => Options.WidthMm = value, custom: true); }
    public double HeightMm { get => Options.HeightMm; set => SetOption(Options.HeightMm != value && value is >= 8 and <= 300, () => Options.HeightMm = value, custom: true); }
    public double MarginTopMm { get => Options.MarginTopMm; set => SetOption(Options.MarginTopMm != value && value is >= 0 and <= 100, () => Options.MarginTopMm = value, custom: true); }
    public double MarginLeftMm { get => Options.MarginLeftMm; set => SetOption(Options.MarginLeftMm != value && value is >= 0 and <= 100, () => Options.MarginLeftMm = value, custom: true); }
    public double GapXMm { get => Options.GapXMm; set => SetOption(Options.GapXMm != value && value is >= 0 and <= 50, () => Options.GapXMm = value, custom: true); }
    public double GapYMm { get => Options.GapYMm; set => SetOption(Options.GapYMm != value && value is >= 0 and <= 50, () => Options.GapYMm = value, custom: true); }

    public bool ShowStoreName { get => Options.ShowStoreName; set => SetOption(true, () => Options.ShowStoreName = value); }
    public bool ShowName { get => Options.ShowName; set => SetOption(true, () => Options.ShowName = value); }
    public bool ShowVariant { get => Options.ShowVariant; set => SetOption(true, () => Options.ShowVariant = value); }
    public bool ShowSku { get => Options.ShowSku; set => SetOption(true, () => Options.ShowSku = value); }
    public bool ShowPrice { get => Options.ShowPrice; set => SetOption(true, () => Options.ShowPrice = value); }
    public bool ShowLbpPrice { get => Options.ShowLbpPrice; set => SetOption(true, () => Options.ShowLbpPrice = value); }
    public bool ShowBarcode { get => Options.ShowBarcode; set => SetOption(true, () => Options.ShowBarcode = value); }
    public bool ShowBarcodeText { get => Options.ShowBarcodeText; set => SetOption(true, () => Options.ShowBarcodeText = value); }
    public bool ShowBorder { get => Options.ShowBorder; set => SetOption(true, () => Options.ShowBorder = value); }
    public double TextScale { get => Options.TextScale; set => SetOption(Options.TextScale != value, () => Options.TextScale = Math.Clamp(value, 0.5, 2.0)); }

    /// <summary>Labels already peeled off the first sheet: printing starts at this position.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial int SkipLabels { get; set; }

    public string Summary
    {
        get
        {
            if (!Options.Sheet) return Loc.T("Labels.SummaryRoll", TotalLabels, Options.WidthMm, Options.HeightMm);
            var (columns, rows) = Options.Grid(LabelOptions.A4.Width, LabelOptions.A4.Height);
            var perSheet = columns * rows;
            var sheets = TotalLabels == 0 ? 0 : (int)Math.Ceiling((TotalLabels + Math.Min(SkipLabels, perSheet - 1)) / (double)perSheet);
            return Loc.T("Labels.SummarySheet", TotalLabels, columns, rows, sheets);
        }
    }

    /// <summary>Text for one label, laid out by <see cref="PrintService.CreateLabel"/>.</summary>
    public LabelData ToLabel(ProductVariant v)
    {
        var rate = _settings.ActiveLbpRate;
        return new LabelData(
            v.Product?.Name ?? v.DisplayName,
            v.Description,
            v.Sku,
            CurrencyFormat.Format(v.EffectivePrice),
            rate > 0 ? Lbp.Format(Lbp.ToPay(v.EffectivePrice, rate, _settings.LbpRounding)) : null,
            string.IsNullOrWhiteSpace(v.Barcode) ? v.Sku : v.Barcode,
            _settings.StoreName);
    }

    /// <summary>What the preview shows: the first item, or a sample when the list is empty.</summary>
    public LabelData PreviewLabel => Rows.Count > 0
        ? ToLabel(Rows[0].Variant)
        : new LabelData(Loc.T("Labels.SampleName"), Loc.T("Labels.SampleVariant"), "TEE-BLK-M", CurrencyFormat.Format(19.99m),
            LbpAvailable ? Lbp.Format(Lbp.ToPay(19.99m, _settings.ActiveLbpRate, _settings.LbpRounding)) : null, "2000000012345", _settings.StoreName);

    private void SetOption(bool changed, Action apply, bool custom = false, [CallerMemberName] string? name = null)
    {
        if (!changed) return;
        apply();
        if (custom && Options.Preset != "Custom" && !MatchesPreset()) Options.Preset = "Custom";
        OnPropertyChanged(name);
        OnLayoutChanged(all: custom);
    }

    private bool MatchesPreset() => LabelPreset.All.Any(p =>
        p.Id == Options.Preset && p.Sheet == Options.Sheet && p.WidthMm == Options.WidthMm && p.HeightMm == Options.HeightMm &&
        p.MarginTopMm == Options.MarginTopMm && p.MarginLeftMm == Options.MarginLeftMm && p.GapXMm == Options.GapXMm && p.GapYMm == Options.GapYMm);

    private void OnLayoutChanged(bool all)
    {
        if (all)
        {
            foreach (var name in new[]
                     {
                         nameof(SelectedPreset), nameof(Sheet), nameof(LabelPrinter), nameof(WidthMm), nameof(HeightMm),
                         nameof(MarginTopMm), nameof(MarginLeftMm), nameof(GapXMm), nameof(GapYMm),
                     })
                OnPropertyChanged(name);
        }
        OnPropertyChanged(nameof(Summary));
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string PresetName(LabelPreset p) => p.Id == "Custom"
        ? Loc.T(p.NameKey)
        : Loc.T(p.NameKey, p.WidthMm, p.HeightMm) + (p.Sheet ? "  " + Loc.T("Labels.PerA4", PerA4(p)) : "");

    private static int PerA4(LabelPreset p)
    {
        var o = new LabelOptions();
        p.ApplyTo(o);
        var (c, r) = o.Grid(LabelOptions.A4.Width, LabelOptions.A4.Height);
        return c * r;
    }

    // ---- Print ------------------------------------------------------------------------------

    /// <summary>"Printer: …" when labels go straight to a default printer, else null.</summary>
    public string? PrinterText { get; } = LocalPreferences.Current.LabelPrinter is { Length: > 0 } p ? Loc.T("Print.PrinterIs", p) : null;

    [RelayCommand]
    private void Print() => Send(choosePrinter: false);

    [RelayCommand]
    private void PrintTo() => Send(choosePrinter: true);

    private void Send(bool choosePrinter)
    {
        var labels = Rows.SelectMany(r => Enumerable.Repeat(ToLabel(r.Variant), Math.Max(0, r.Copies))).ToList();
        if (labels.Count == 0)
        {
            Dialogs.Warning(Loc.T("Labels.NeedCopies"));
            return;
        }

        LocalPreferences.Current.Labels = Options.Clone();
        LocalPreferences.Current.Save();
        try
        {
            if (_print.PrintLabels(labels, Options, Options.Sheet ? SkipLabels : 0, choosePrinter))
            {
                Dialogs.Toast(Loc.T("Labels.Sent", labels.Count));
                Close(true);
            }
        }
        catch (Exception ex)
        {
            Dialogs.Error(Loc.T("Common.PrintFailed"), ex);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        LocalPreferences.Current.Labels = Options.Clone(); // keep layout tweaks even without printing
        LocalPreferences.Current.Save();
        Close(false);
    }
}
