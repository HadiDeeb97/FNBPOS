using UniPos.Core;
using UniPos.Core.Entities;
using UniPos.Core.Features;
using UniPos.Core.Localization;
using UniPos.Core.Security;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UniPos.Desktop.Infrastructure;

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

    /// <summary>
    /// The features switched on at this till. Loaded at sign-in, picked up again within a minute when an admin changes
    /// them; menus and screens hide what is off.
    /// </summary>
    [ObservableProperty]
    public partial FeatureSet Features { get; set; } = FeatureSet.All;

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
