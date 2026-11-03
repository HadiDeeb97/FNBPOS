using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>
/// Windows takes a shortcut's icon from the program file, before the app has loaded anything, so the store logo kept
/// in the database can't reach it by itself. When a logo is set it is saved as an .ico file and this PC's shortcuts to
/// the program (desktop, Start menu, pinned to the taskbar) are pointed at it; without a logo they go back to the
/// program's own icon. Best effort: anything that fails is simply left as it was.
/// </summary>
public static class ShortcutIcons
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClothingStorePOS");

    /// <summary>
    /// Each logo gets its own icon file ("store-logo-1a2b3c4d.ico"): Windows remembers icons by file name, so a new logo
    /// written over the old file kept showing the old picture.
    /// </summary>
    private static string IconPathFor(byte[] logo) =>
        Path.Combine(Folder, $"store-logo-{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(logo))[..8].ToLowerInvariant()}.ico");

    /// <summary>Applies the logo (PNG) to the shortcuts, or the program's icon when null. Runs in the background.</summary>
    public static void Update(byte[]? logo, string shortcutName)
    {
        // WPF imaging and the Windows shell objects want a single-threaded apartment.
        var thread = new Thread(() =>
        {
            try { Apply(logo, shortcutName); }
            catch (Exception) { /* never let shortcut icons trouble the app */ }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static void Apply(byte[]? logo, string shortcutName)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;

        string iconLocation;
        string? iconFile = null;
        if (logo is { Length: > 0 })
        {
            Directory.CreateDirectory(Folder);
            iconFile = IconPathFor(logo);
            if (!File.Exists(iconFile)) File.WriteAllBytes(iconFile, ToIco(logo));
            iconLocation = iconFile + ",0";
        }
        else
        {
            iconLocation = exe + ",0";
        }

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return;
        dynamic shell = Activator.CreateInstance(shellType)!;

#if !DEBUG
        EnsureDesktopShortcut(shell, exe, shortcutName);
#endif

        var changed = false;
        foreach (var path in ShortcutFiles())
        {
            try
            {
                dynamic shortcut = shell.CreateShortcut(path);
                if (!SamePath((string)shortcut.TargetPath, exe)) continue;
                if (string.Equals((string)shortcut.IconLocation, iconLocation, StringComparison.OrdinalIgnoreCase)) continue;
                shortcut.IconLocation = iconLocation;
                shortcut.Save();
                SHChangeNotify(0x00002000 /* SHCNE_UPDATEITEM */, 0x0005 /* SHCNF_PATHW */, path, null); // this shortcut's icon
                changed = true;
            }
            catch (Exception)
            {
                // e.g. a shortcut for all users without permission to change it
            }
        }
        if (changed) SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, null, null); // and every icon shown

        // Icon files of earlier logos are no longer used (one Windows still has open is left for next time).
        foreach (var old in Directory.Exists(Folder) ? Directory.GetFiles(Folder, "store-logo*.ico") : [])
        {
            if (string.Equals(old, iconFile, StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(old); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>An installed copy puts a shortcut on the desktop the first time (once: a deleted shortcut stays deleted).</summary>
    private static void EnsureDesktopShortcut(dynamic shell, string exe, string name)
    {
        if (LocalPreferences.Current.DesktopShortcutMade) return;
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var path = Path.Combine(desktop, string.Concat(name.Split(Path.GetInvalidFileNameChars())) + ".lnk");
        if (!File.Exists(path))
        {
            dynamic shortcut = shell.CreateShortcut(path);
            shortcut.TargetPath = exe;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exe);
            shortcut.IconLocation = exe + ",0";
            shortcut.Save();
        }
        LocalPreferences.Current.DesktopShortcutMade = true;
        LocalPreferences.Current.Save();
    }

    private static IEnumerable<string> ShortcutFiles()
    {
        string[] shallow =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"),
        ];
        string[] deep =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        ];
        foreach (var (folder, option) in shallow.Select(f => (f, SearchOption.TopDirectoryOnly))
                     .Concat(deep.Select(f => (f, SearchOption.AllDirectories))))
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
            string[] files;
            try { files = Directory.GetFiles(folder, "*.lnk", option); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var file in files) yield return file;
        }
    }

    private static bool SamePath(string? a, string b) =>
        !string.IsNullOrEmpty(a) && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>A Windows icon with the logo at 16 to 256 pixels (PNG-compressed frames, supported since Windows Vista).</summary>
    private static byte[] ToIco(byte[] png)
    {
        int[] sizes = [16, 24, 32, 48, 64, 256];
        var source = BitmapDecoder.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var frames = sizes.Select(size =>
        {
            var scaled = new TransformedBitmap(source, new ScaleTransform((double)size / source.PixelWidth, (double)size / source.PixelHeight));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(scaled));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return (Size: size, Data: stream.ToArray());
        }).ToList();

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write((short)0);            // reserved
        writer.Write((short)1);            // type: icon
        writer.Write((short)frames.Count);
        var offset = 6 + 16 * frames.Count;
        foreach (var (size, data) in frames)
        {
            writer.Write((byte)(size >= 256 ? 0 : size)); // 0 means 256
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);         // palette colours
            writer.Write((byte)0);         // reserved
            writer.Write((short)1);        // colour planes
            writer.Write((short)32);       // bits per pixel
            writer.Write(data.Length);
            writer.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in frames) writer.Write(data);
        writer.Flush();
        return output.ToArray();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, string? item1, string? item2);
}
