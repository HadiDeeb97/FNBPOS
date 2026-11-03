using System.Globalization;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using ClothingStore.Core.Barcodes;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Localization;
using ClothingStore.Core.Pricing;
using ClothingStore.Core.Text;
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

public sealed record LabelStyleOption(LabelStyle Style, string Name);

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
        Options = (LocalPreferences.Current.Labels?.Clone() ?? new LabelOptions()).Upgrade();
        Presets = LabelPreset.All.Select(p => new LabelPresetOption(p, PresetName(p))).ToList();
        Styles = LabelStyle.All.Select(s => new LabelStyleOption(s, Loc.T(s.NameKey))).ToList();
        foreach (var v in variants) AddVariant(v, 1);
        Rows.CollectionChanged += (_, _) => OnRowsChanged();
    }

    public override string Title => Loc.T("Labels.Title");
    public LabelOptions Options { get; }
    public ObservableCollection<LabelRowViewModel> Rows { get; } = [];
    public IReadOnlyList<LabelPresetOption> Presets { get; }
    public IReadOnlyList<LabelStyleOption> Styles { get; }
    public bool LbpAvailable => _settings.ActiveLbpRate > 0;

    /// <summary>EAN-13 or Code 128, as chosen in Settings.</summary>
    public BarcodeType BarcodeType => _settings.LabelBarcode;

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
            _typed.Clear(); // the boxes show the preset's sizes
            OnLayoutChanged(all: true);
        }
    }

    public bool Sheet { get => Options.Sheet; set => SetOption(Options.Sheet != value, () => Options.Sheet = value, custom: true); }

    // The size boxes keep exactly what is typed ("4" on the way to "45", "45." on the way to "45.5"); the label takes the
    // size as soon as it is a valid one. (Bound straight to the numbers, a box was put back to the old size at every
    // keystroke that wasn't a valid size yet, so a new size couldn't be typed at all.)
    public string WidthText { get => MmText(WidthMm); set => TypeMm(value, mm => WidthMm = mm); }
    public string HeightText { get => MmText(HeightMm); set => TypeMm(value, mm => HeightMm = mm); }
    public string MarginTopText { get => MmText(MarginTopMm); set => TypeMm(value, mm => MarginTopMm = mm); }
    public string MarginLeftText { get => MmText(MarginLeftMm); set => TypeMm(value, mm => MarginLeftMm = mm); }
    public string GapXText { get => MmText(GapXMm); set => TypeMm(value, mm => GapXMm = mm); }
    public string GapYText { get => MmText(GapYMm); set => TypeMm(value, mm => GapYMm = mm); }

    private readonly Dictionary<string, string> _typed = [];

    private string MmText(double mm, [CallerMemberName] string? name = null) =>
        _typed.TryGetValue(name!, out var text) ? text : mm.ToString("0.##", CultureInfo.CurrentCulture);

    private void TypeMm(string? text, Action<double> apply, [CallerMemberName] string? name = null)
    {
        _typed[name!] = text ?? "";
        if (TryMm(text, out var mm)) apply(mm); // out of range or unfinished: the label keeps its last good size
    }

    private static bool TryMm(string? text, out double mm) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out mm) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out mm);
    public bool LabelPrinter { get => !Options.Sheet; set => Sheet = !value; }
    public double WidthMm { get => Options.WidthMm; set => SetOption(Options.WidthMm != value && value is >= 10 and <= 300, () => Options.WidthMm = value, custom: true); }
    public double HeightMm { get => Options.HeightMm; set => SetOption(Options.HeightMm != value && value is >= 8 and <= 300, () => Options.HeightMm = value, custom: true); }
    public double MarginTopMm { get => Options.MarginTopMm; set => SetOption(Options.MarginTopMm != value && value is >= 0 and <= 100, () => Options.MarginTopMm = value, custom: true); }
    public double MarginLeftMm { get => Options.MarginLeftMm; set => SetOption(Options.MarginLeftMm != value && value is >= 0 and <= 100, () => Options.MarginLeftMm = value, custom: true); }
    public double GapXMm { get => Options.GapXMm; set => SetOption(Options.GapXMm != value && value is >= 0 and <= 50, () => Options.GapXMm = value, custom: true); }
    public double GapYMm { get => Options.GapYMm; set => SetOption(Options.GapYMm != value && value is >= 0 and <= 50, () => Options.GapYMm = value, custom: true); }

    public bool ShowStoreName { get => Options.ShowStoreName; set => SetOption(true, () => Options.ShowStoreName = value, style: true); }
    public bool ShowName { get => Options.ShowName; set => SetOption(true, () => Options.ShowName = value, style: true); }
    public bool ShowVariant { get => Options.ShowVariant; set => SetOption(true, () => Options.ShowVariant = value, style: true); }
    public bool ShowSku { get => Options.ShowSku; set => SetOption(true, () => Options.ShowSku = value); }
    public bool ShowPrice { get => Options.ShowPrice; set => SetOption(true, () => Options.ShowPrice = value, style: true); }
    public bool ShowLbpPrice { get => Options.ShowLbpPrice; set => SetOption(true, () => Options.ShowLbpPrice = value); }
    public bool ShowBarcode { get => Options.ShowBarcode; set => SetOption(true, () => Options.ShowBarcode = value, style: true); }

    // ---- Look ----

    /// <summary>The ready-made style: picking one sets the look options below (which can still be changed).</summary>
    public LabelStyleOption SelectedStyle
    {
        get => Styles.FirstOrDefault(s => s.Style.Id == Options.Style) ?? Styles[^1];
        set
        {
            if (value is null || value.Style.Id == Options.Style) return;
            value.Style.ApplyTo(Options);
            OnLayoutChanged(all: true);
        }
    }

    public LabelArrangement Arrangement { get => Options.Arrangement; set => SetOption(Options.Arrangement != value, () => Options.Arrangement = value, style: true); }
    public LabelAlign Align { get => Options.Align; set => SetOption(Options.Align != value, () => Options.Align = value, style: true); }
    public LabelBarcodeSize BarcodeSize { get => Options.BarcodeSize; set => SetOption(Options.BarcodeSize != value, () => Options.BarcodeSize = value, style: true); }
    public LabelDigits Digits { get => Options.Digits; set => SetOption(Options.Digits != value, () => Options.Digits = value); }
    public LabelPriceStyle PriceStyle { get => Options.PriceStyle; set => SetOption(Options.PriceStyle != value, () => Options.PriceStyle = value, style: true); }
    public LabelVariantFormat VariantFormat { get => Options.VariantFormat; set => SetOption(Options.VariantFormat != value, () => Options.VariantFormat = value); }
    public LabelBorder Border { get => Options.Border; set => SetOption(Options.Border != value, () => Options.Border = value); }
    public bool NarrowFont { get => Options.NarrowFont; set => SetOption(Options.NarrowFont != value, () => Options.NarrowFont = value); }
    public bool HoleSpace { get => Options.HoleSpace; set => SetOption(Options.HoleSpace != value, () => Options.HoleSpace = value, style: true); }

    /// <summary>"M / Black" and "Size M · Black", as the two choices for the size/colour line.</summary>
    public string VariantSlashText => LabelText.Variant("M", ColorNames.Common(Loc.IsRightToLeft)[0], LabelVariantFormat.Slash);
    public string VariantLabelledText => LabelText.Variant("M", ColorNames.Common(Loc.IsRightToLeft)[0], LabelVariantFormat.Labelled);
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
            _settings.StoreName, v.Size, v.Color);
    }

    /// <summary>What the preview shows: the first item, or a sample when the list is empty.</summary>
    public LabelData PreviewLabel => Rows.Count > 0
        ? ToLabel(Rows[0].Variant)
        : new LabelData(Loc.T("Labels.SampleName"), Loc.T("Labels.SampleVariant"), "TEE-BLK-M", CurrencyFormat.Format(19.99m),
            LbpAvailable ? Lbp.Format(Lbp.ToPay(19.99m, _settings.ActiveLbpRate, _settings.LbpRounding)) : null, Ean13.CreateInStore(1234), _settings.StoreName,
            "M", ColorNames.Common(Loc.IsRightToLeft)[0]);

    private void SetOption(bool changed, Action apply, bool custom = false, bool style = false, [CallerMemberName] string? name = null)
    {
        if (!changed) return;
        apply();
        if (custom && Options.Preset != "Custom" && !MatchesPreset()) Options.Preset = "Custom";
        // A look option changed by hand: the style list says "Custom" unless the options still match the style picked.
        if (style && Options.Style != LabelStyle.Custom && LabelStyle.All.FirstOrDefault(s => s.Id == Options.Style)?.Matches(Options) != true)
        {
            Options.Style = LabelStyle.Custom;
            OnPropertyChanged(nameof(SelectedStyle));
        }
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
                         nameof(WidthText), nameof(HeightText), nameof(MarginTopText), nameof(MarginLeftText), nameof(GapXText), nameof(GapYText),
                         nameof(SelectedStyle), nameof(Arrangement), nameof(Align), nameof(BarcodeSize), nameof(PriceStyle), nameof(HoleSpace),
                         nameof(ShowStoreName), nameof(ShowName), nameof(ShowVariant), nameof(ShowPrice), nameof(ShowBarcode),
                     })
                OnPropertyChanged(name);
        }
        OnPropertyChanged(nameof(Summary));
        PreviewChanged?.Invoke(this, EventArgs.Empty);
        SaveLayout();
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

        SaveLayout();
        try
        {
            if (_print.PrintLabels(labels, Options, BarcodeType, Options.Sheet ? SkipLabels : 0, choosePrinter))
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
    private void Cancel() => Close(false);

    /// <summary>
    /// The size and layout are this PC's from the moment they change, however the window is closed (Print, Cancel or the
    /// window's ×): the next labels start from them.
    /// </summary>
    private void SaveLayout()
    {
        LocalPreferences.Current.Labels = Options.Clone();
        LocalPreferences.Current.Save();
    }
}
