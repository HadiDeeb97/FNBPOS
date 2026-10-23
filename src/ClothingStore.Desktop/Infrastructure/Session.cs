using ClothingStore.Core;
using ClothingStore.Core.Entities;
using ClothingStore.Core.Localization;
using ClothingStore.Core.Security;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>The signed-in user and their open cash-drawer shift.</summary>
public sealed partial class Session : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedIn), nameof(UserDisplay))]
    public partial User? CurrentUser { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOpenShift), nameof(ShiftDisplay))]
    public partial Shift? CurrentShift { get; set; }

    /// <summary>
    /// Another cashier's drawer still open on this PC (they signed out or closed the app without closing it): this
    /// user can continue it or count and close it before opening their own.
    /// </summary>
    [ObservableProperty]
    public partial Shift? OtherDrawer { get; set; }

    /// <summary>This PC (till): its PC ID, and its name for people to read. The cash drawer belongs to the till.</summary>
    public string TillId { get; set; } = Environment.MachineName;
    public string TillName { get; } = Environment.MachineName;

    public bool IsSignedIn => CurrentUser is not null;
    public bool HasOpenShift => CurrentShift is { Status: ShiftStatus.Open };

    public User User => CurrentUser ?? throw new InvalidOperationException("No user is signed in.");

    public string UserDisplay => CurrentUser is null ? "" : $"{CurrentUser.FullName} · {Loc.EnumText(CurrentUser.Role)}";

    public string ShiftDisplay => CurrentShift is null
        ? Loc.T("Shell.NoOpenShift")
        : Loc.T("Shell.ShiftOpenSince", CurrentShift.OpenedAt);

    public bool Can(Permission permission) => CurrentUser is not null && Permissions.Has(CurrentUser.Role, permission);

    public void SignOut()
    {
        CurrentShift = null;
        OtherDrawer = null;
        CurrentUser = null;
    }
}
