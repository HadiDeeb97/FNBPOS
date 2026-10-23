namespace ClothingStore.Core.Entities;

public class User : Entity
{
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Cashier;
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastLoginAt { get; set; }

    /// <summary>"en" or "ar"; null = whatever the sign-in screen was set to.</summary>
    public string? PreferredLanguage { get; set; }
}
