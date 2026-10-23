namespace ClothingStore.Core.Entities;

/// <summary>Small named values the app keeps for itself (e.g. the last date it was used, for licensing).</summary>
public class AppState
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
