using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using ClothingStore.Data.Services;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>
/// Times product searches. One that takes over a second is written to
/// %LocalAppData%\ClothingStorePOS\slow-searches.log with what was going on: how long a bare round trip to the database
/// takes right after it (slow too: the PC or SQL Server is struggling; fast: the search itself), how full the PC's
/// memory is, and how long the till had been idle (the first search after a pause is slower).
/// </summary>
public static class SearchTiming
{
    public static readonly TimeSpan SlowAfter = TimeSpan.FromSeconds(1);

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClothingStorePOS", "slow-searches.log");

    private static DateTime _lastSearch = DateTime.MinValue;

    /// <summary>Runs <paramref name="search"/> and records it if slow.</summary>
    public static async Task<T> TimeAsync<T>(string where, string text, Func<Task<T>> search, ProductService products, Func<T, int> count)
    {
        var idle = _lastSearch == DateTime.MinValue ? (TimeSpan?)null : DateTime.Now - _lastSearch;
        var watch = Stopwatch.StartNew();
        var result = await search();
        watch.Stop();
        _lastSearch = DateTime.Now;
        if (watch.Elapsed >= SlowAfter) _ = RecordAsync(where, text, watch.Elapsed, count(result), idle, products);
        return result;
    }

    private static async Task RecordAsync(string where, string text, TimeSpan took, int results, TimeSpan? idle, ProductService products)
    {
        try
        {
            string ping;
            try { ping = $"{(await products.PingAsync()).TotalMilliseconds:0} ms"; }
            catch (Exception ex) { ping = "failed: " + ex.GetBaseException().Message; }

            var memory = MemoryLoad();
            var screen = await ScreenDelayAsync();
            var line = string.Join(" | ",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                where,
                $"\"{text}\"",
                $"took {took.TotalMilliseconds:0} ms",
                $"{results} result(s)",
                $"database round trip after: {ping}",
                memory is { } m ? $"memory {m.LoadPercent}% used, {m.AvailableMb:N0} MB free" : "memory ?",
                $"this app {AppMemoryMb():N0} MB",
                SqlServerMemoryMb() is { } sql ? $"SQL Server {sql:N0} MB" : "SQL Server elsewhere",
                $"screen busy {screen.TotalMilliseconds:0} ms",
                idle is { } i ? $"idle before: {i.TotalSeconds:0} s" : "first search since start");

            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 512 * 1024) File.Delete(LogPath);
            await File.AppendAllTextAsync(LogPath, line + Environment.NewLine);
        }
        catch (Exception)
        {
            // Diagnostics only: never disturb the till.
        }
    }

    /// <summary>Memory this app holds (private bytes): a figure that keeps growing during the day points at the app.</summary>
    private static long AppMemoryMb()
    {
        using var self = Process.GetCurrentProcess();
        return self.PrivateMemorySize64 / (1024 * 1024);
    }

    /// <summary>Memory SQL Server uses on this PC (it can take most of it and leave the rest of the PC starved); null when it runs on another PC.</summary>
    private static long? SqlServerMemoryMb()
    {
        try
        {
            var processes = Process.GetProcessesByName("sqlservr");
            try
            {
                return processes.Length == 0 ? null : processes.Sum(p => p.WorkingSet64) / (1024 * 1024);
            }
            finally
            {
                foreach (var p in processes) p.Dispose();
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>How long the screen takes to get round to a small job: long means it was busy (drawing, or the PC was swapping).</summary>
    private static async Task<TimeSpan> ScreenDelayAsync()
    {
        if (System.Windows.Application.Current?.Dispatcher is not { } dispatcher) return TimeSpan.Zero;
        var watch = Stopwatch.StartNew();
        await dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        return watch.Elapsed;
    }

    private static (uint LoadPercent, ulong AvailableMb)? MemoryLoad()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (status.MemoryLoad, status.AvailPhys / (1024 * 1024)) : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
