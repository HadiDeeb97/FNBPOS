namespace ClothingStore.Core.Entities;

/// <summary>
/// The store's logo (single row), used as the app's window/taskbar icon and on the sign-in screen and sidebar.
/// Kept out of <see cref="StoreSettings"/> so the image isn't loaded every time settings are read.
/// </summary>
public class StoreLogo : Entity
{
    /// <summary>PNG, already resized by the app (at most 256 x 256).</summary>
    public byte[] Image { get; set; } = [];
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public int? UpdatedByUserId { get; set; }
}
