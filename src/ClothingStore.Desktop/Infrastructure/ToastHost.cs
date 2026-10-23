using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace ClothingStore.Desktop.Infrastructure;

public enum ToastKind
{
    Success,
    Info,
    Warning,
}

public sealed record ToastItem(string Message, ToastKind Kind);

/// <summary>
/// Short notices that slide in at the bottom of the main window and disappear on their own, used for
/// confirmations ("Saved") instead of a dialog that has to be clicked away.
/// </summary>
public sealed class ToastHost
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(4);

    public static ToastHost Instance { get; } = new();

    public ObservableCollection<ToastItem> Items { get; } = [];

    public void Show(string message, ToastKind kind = ToastKind.Success)
    {
        var item = new ToastItem(message, kind);
        Items.Add(item);
        while (Items.Count > 4) Items.RemoveAt(0);

        var timer = new DispatcherTimer { Interval = Lifetime };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Items.Remove(item);
        };
        timer.Start();
    }

    public void Dismiss(ToastItem item) => Items.Remove(item);
}
