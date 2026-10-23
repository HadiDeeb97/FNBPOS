using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClothingStore.Core;
using ClothingStore.Core.Localization;
using ClothingStore.Data.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClothingStore.Desktop.Infrastructure;

/// <summary>The store logo, shown in the sidebar and sign-in screen and used as every window's icon.</summary>
public sealed partial class Branding : ObservableObject
{
    public static Branding Instance { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLogo))]
    public partial ImageSource? Icon { get; private set; }

    public bool HasLogo => Icon is not null;

    /// <summary>The logo as stored (PNG), for the shortcut icons; null when there is none.</summary>
    public byte[]? LogoImage { get; private set; }

    /// <summary>When the shown logo was saved; compared with the database to pick up a change made on another till.</summary>
    public DateTime? Version { get; private set; }

    /// <summary>Loads the logo from the database (at startup, and when another till changed it).</summary>
    public async Task RefreshAsync(BrandingService branding, bool onlyIfChanged = false)
    {
        if (onlyIfChanged && await branding.GetLogoVersionAsync() == Version) return;
        var logo = await branding.GetLogoAsync();
        Apply(logo?.Image, logo?.UpdatedAt);
    }

    /// <summary>Shows a logo that was just saved.</summary>
    public void Apply(byte[]? image, DateTime? version)
    {
        Version = version;
        try
        {
            SetLogo(image);
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or ArgumentException)
        {
            SetLogo(null); // a damaged image never stops the app
        }
    }

    /// <summary>Largest side of the stored logo; plenty for a taskbar icon and the sign-in screen.</summary>
    public const int LogoSize = 256;

    /// <summary>
    /// Turns any picture file (PNG, JPG, BMP, GIF, ICO) into a square transparent PNG of at most 256 x 256,
    /// centred, so it works as a window icon and stays small in the database.
    /// </summary>
    public static byte[] NormalizeLogo(byte[] file)
    {
        BitmapSource source;
        try
        {
            using var input = new MemoryStream(file);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            source = decoder.Frames.OrderByDescending(f => f.PixelWidth * f.PixelHeight).First(); // biggest icon frame
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException or IOException)
        {
            throw new BusinessRuleException(Loc.T("Err.LogoUnreadable"));
        }

        var scale = Math.Min(1.0, (double)LogoSize / Math.Max(source.PixelWidth, source.PixelHeight));
        var width = Math.Max(1, source.PixelWidth * scale);
        var height = Math.Max(1, source.PixelHeight * scale);
        var side = (int)Math.Ceiling(Math.Max(width, height));

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            dc.DrawImage(source, new System.Windows.Rect((side - width) / 2, (side - height) / 2, width, height));
        }
        var target = new RenderTargetBitmap(side, side, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    /// <summary>Applies a PNG/JPEG logo (null removes it) to the open windows too.</summary>
    public void SetLogo(byte[]? image)
    {
        Icon = image is { Length: > 0 } ? Decode(image) : null;
        LogoImage = Icon is null ? null : image;
        foreach (System.Windows.Window window in System.Windows.Application.Current.Windows)
            window.Icon = Icon;
    }

    public static BitmapImage Decode(byte[] image)
    {
        var bitmap = new BitmapImage();
        using var stream = new MemoryStream(image);
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
