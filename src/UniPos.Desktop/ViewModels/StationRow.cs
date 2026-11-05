using UniPos.Core.Entities;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UniPos.Desktop.ViewModels;

/// <summary>A station in Settings: its name, the store's printer for it, and this PC's own printer for it (if any).</summary>
public sealed partial class StationRow : ObservableObject
{
    public int Id { get; set; }

    [ObservableProperty] public partial string Name { get; set; } = "";

    /// <summary>The printer every till uses for it; "" for none yet.</summary>
    [ObservableProperty] public partial string StorePrinter { get; set; } = "";

    /// <summary>This PC's own printer for it; "" to use the store's.</summary>
    [ObservableProperty] public partial string PcPrinter { get; set; } = "";

    [ObservableProperty] public partial bool IsActive { get; set; } = true;

    public Station ToEntity() => new() { Id = Id, Name = Name ?? "", PrinterName = StorePrinter, IsActive = IsActive };
}
