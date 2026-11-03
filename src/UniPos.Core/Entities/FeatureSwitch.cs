using UniPos.Core.Features;

namespace UniPos.Core.Entities;

/// <summary>
/// A feature switched on or off differently from the store's kind of place: for the whole store (no
/// <see cref="TillId"/>) or for one till. Without a row the venue's default applies (<see cref="Presets"/>).
/// </summary>
public class FeatureSwitch : Entity
{
    public const string On = "on";
    public const string Off = "off";

    public Feature Feature { get; set; }

    /// <summary>The till (PC ID) this applies to; null for the whole store.</summary>
    public string? TillId { get; set; }

    /// <summary><see cref="On"/> or <see cref="Off"/> (text, so a feature with more than two choices fits too).</summary>
    public string Value { get; set; } = On;

    public DateTime ChangedAt { get; set; } = DateTime.Now;
    public int? ChangedByUserId { get; set; }

    public bool IsOn => Value == On;
}
