using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using ClothingStore.Core.Barcodes;
using ClothingStore.Desktop.Infrastructure;

namespace ClothingStore.Desktop.Services;

/// <summary>Text for one price label; which parts are printed is up to <see cref="LabelOptions"/>.</summary>
public sealed record LabelData(string Title, string Variant, string Sku, string Price, string? PriceLbp, string Code, string? StoreName = null);

/// <summary>Prints receipts/reports (monospaced text, auto-fitted to the printer width) and barcode price tags.</summary>
public sealed class PrintService
{
    private static readonly FontFamily Mono = new("Consolas, Courier New");

    /// <summary>
    /// Prints receipt-width text. With a default receipt printer set on this PC (Settings → Printers) it goes straight
    /// there; otherwise, or with <paramref name="choosePrinter"/>, the Windows print dialog asks which printer.
    /// </summary>
    /// <param name="logo">Store logo (PNG) printed centred above the text, or null.</param>
    public bool PrintText(IReadOnlyList<string> lines, string jobName, int copies = 1, bool choosePrinter = false, byte[]? logo = null)
    {
        var dialog = OpenPrinter(choosePrinter ? null : LocalPreferences.Current.ReceiptPrinter, ref copies);
        if (dialog is null) return false;

        const double padding = 16;
        var width = dialog.PrintableAreaWidth;
        var longest = Math.Max(20, lines.Count == 0 ? 20 : lines.Max(l => l.Length));
        // Consolas glyphs are ~0.55em wide: shrink the font for narrow receipt printers, cap for A4.
        var fontSize = Math.Clamp((width - padding * 2) / (longest * 0.56), 6, 12);

        // One job per copy: many receipt printer drivers ignore the copy count in the print ticket.
        for (var i = 0; i < copies; i++)
        {
            var doc = BuildTextDocument(lines, fontSize);
            if (logo is { Length: > 0 } && LogoBlock(logo, width - padding * 2) is { } block) doc.Blocks.InsertBefore(doc.Blocks.FirstBlock, block);
            doc.PageWidth = width;
            doc.PageHeight = dialog.PrintableAreaHeight;
            doc.ColumnWidth = width;
            doc.PagePadding = new Thickness(padding);
            dialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, jobName);
        }
        return true;
    }

    /// <summary>Printers installed on this PC (local and shared connections), for the Settings page.</summary>
    public static IReadOnlyList<string> InstalledPrinters()
    {
        try
        {
            return Queues().Select(q => q.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is PrintSystemException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return []; // print spooler stopped
        }
    }

    private static IEnumerable<PrintQueue> Queues() =>
        new LocalPrintServer().GetPrintQueues([EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections]);

    private static PrintQueue? FindQueue(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        try
        {
            return Queues().FirstOrDefault(q => string.Equals(q.FullName, name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is PrintSystemException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// A print dialog aimed at <paramref name="printer"/> without showing it, or (no printer set, or it is no longer
    /// installed) shown so the user picks one. Null when the user cancels. <paramref name="copies"/> comes back as the
    /// number of copies to print, one job each; the ticket itself is left at one copy.
    /// </summary>
    private static PrintDialog? OpenPrinter(string? printer, ref int copies, Action<PrintDialog>? configure = null)
    {
        copies = Math.Clamp(copies, 1, 99);
        var dialog = new PrintDialog();
        var queue = FindQueue(printer);
        if (queue is not null)
        {
            dialog.PrintQueue = queue;
            try { dialog.PrintTicket = queue.UserPrintTicket ?? queue.DefaultPrintTicket ?? dialog.PrintTicket; }
            catch (PrintSystemException) { /* keep the dialog's ticket */ }
        }
        configure?.Invoke(dialog);

        if (queue is null)
        {
            dialog.PrintTicket.CopyCount = copies;
            if (dialog.ShowDialog() != true) return null;
            copies = Math.Clamp(dialog.PrintTicket.CopyCount ?? copies, 1, 99);
        }
        dialog.PrintTicket.CopyCount = 1;
        return dialog;
    }

    /// <summary>The logo centred, about half the paper wide (and never taller than ~1.5 cm on an 80 mm roll).</summary>
    private static BlockUIContainer? LogoBlock(byte[] logo, double paperWidth)
    {
        try
        {
            var image = new Image
            {
                Source = Branding.Decode(logo),
                MaxWidth = Math.Min(paperWidth * 0.5, 180),
                MaxHeight = 110,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return new BlockUIContainer(image) { Margin = new Thickness(0, 0, 0, 6) };
        }
        catch (Exception ex) when (ex is NotSupportedException or System.IO.IOException or ArgumentException)
        {
            return null; // an unreadable logo never stops a receipt
        }
    }

    public static FlowDocument BuildTextDocument(IEnumerable<string> lines, double fontSize = 12)
    {
        var paragraph = new Paragraph { FontFamily = Mono, FontSize = fontSize, Margin = new Thickness(0) };
        var first = true;
        foreach (var line in lines)
        {
            if (!first) paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new Run(line));
            first = false;
        }
        return new FlowDocument(paragraph) { FontFamily = Mono, PagePadding = new Thickness(0) };
    }

    /// <summary>
    /// Prints price labels, to this PC's default label printer when one is set. Sheet mode lays them out in a grid (skipping <paramref name="skip"/> positions already used on
    /// the first sheet); label-printer mode prints one label per page at the label size.
    /// </summary>
    public bool PrintLabels(IReadOnlyList<LabelData> labels, LabelOptions options, int skip = 0, bool choosePrinter = false)
    {
        if (labels.Count == 0) return false;
        var copies = 1;
        var dialog = OpenPrinter(choosePrinter ? null : LocalPreferences.Current.LabelPrinter, ref copies, d =>
        {
            if (options.Sheet) return;
            // Tell the driver the label size; most thermal label printers honour it.
            try { d.PrintTicket.PageMediaSize = new PageMediaSize(options.WidthPx, options.HeightPx); }
            catch { /* the driver's own page size is used */ }
        });
        if (dialog is null) return false;

        // Copies chosen in the print dialog: the ticket was reset to 1, so the set is sent that many times.
        for (var i = 0; i < Math.Max(1, copies); i++)
        {
            var document = BuildLabelDocument(labels, options, skip, dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
            dialog.PrintDocument(document.DocumentPaginator, "Price labels");
        }
        return true;
    }

    public static FixedDocument BuildLabelDocument(IReadOnlyList<LabelData> labels, LabelOptions o, int skip, double pageWidth, double pageHeight)
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new Size(pageWidth, pageHeight);

        if (!o.Sheet)
        {
            foreach (var label in labels)
            {
                var page = NewPage(pageWidth, pageHeight);
                page.Children.Add(CreateLabel(label, o, Math.Min(o.WidthPx, pageWidth), Math.Min(o.HeightPx, pageHeight)));
                AddPage(document, page);
            }
            return document;
        }

        var (columns, rows) = o.Grid(pageWidth, pageHeight);
        var perPage = columns * rows;
        skip = Math.Clamp(skip, 0, perPage - 1);
        var left = LabelOptions.MmToPx(o.MarginLeftMm);
        var top = LabelOptions.MmToPx(o.MarginTopMm);
        var stepX = o.WidthPx + LabelOptions.MmToPx(o.GapXMm);
        var stepY = o.HeightPx + LabelOptions.MmToPx(o.GapYMm);

        var position = skip;
        FixedPage? current = null;
        foreach (var label in labels)
        {
            if (current is null || position == perPage)
            {
                if (current is not null) AddPage(document, current);
                current = NewPage(pageWidth, pageHeight);
                if (position == perPage) position = 0;
            }
            var element = CreateLabel(label, o, o.WidthPx, o.HeightPx);
            FixedPage.SetLeft(element, left + position % columns * stepX);
            FixedPage.SetTop(element, top + position / columns * stepY);
            current.Children.Add(element);
            position++;
        }
        if (current is not null) AddPage(document, current);
        return document;
    }

    private static FixedPage NewPage(double width, double height) => new() { Width = width, Height = height, Background = Brushes.White };

    private static void AddPage(FixedDocument document, FixedPage page)
    {
        var size = new Size(page.Width, page.Height);
        page.Measure(size);
        page.Arrange(new Rect(size));
        page.UpdateLayout();
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
    }

    /// <summary>One label at the given size in px. Also used for the live preview.</summary>
    public static FrameworkElement CreateLabel(LabelData label, LabelOptions o, double width, double height)
    {
        // Text grows with the label so small and large labels both look balanced.
        var scale = Math.Clamp(height / 140.0, 0.55, 1.8) * Math.Clamp(o.TextScale, 0.5, 2.0);
        var pad = Math.Clamp(height * 0.05, 3, 10);

        TextBlock Text(string text, double size, FontWeight weight, FontFamily? font = null) => new()
        {
            Text = text,
            FontSize = Math.Max(5, size * scale),
            FontWeight = weight,
            FontFamily = font ?? new FontFamily("Segoe UI"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };

        var panel = new DockPanel { Margin = new Thickness(pad), LastChildFill = true };
        var top = new StackPanel();
        DockPanel.SetDock(top, Dock.Top);
        if (o.ShowStoreName && !string.IsNullOrWhiteSpace(label.StoreName)) top.Children.Add(Text(label.StoreName, 7.5, FontWeights.SemiBold));
        if (o.ShowName) top.Children.Add(Text(label.Title, 10.5, FontWeights.Bold));
        if (o.ShowVariant && !string.IsNullOrWhiteSpace(label.Variant)) top.Children.Add(Text(label.Variant, 9, FontWeights.Normal));
        if (o.ShowSku) top.Children.Add(Text(label.Sku, 8, FontWeights.Normal, Mono));
        if (o.ShowPrice) top.Children.Add(Text(label.Price, 17, FontWeights.Bold));
        if (o.ShowLbpPrice && !string.IsNullOrWhiteSpace(label.PriceLbp)) top.Children.Add(Text(label.PriceLbp, 9.5, FontWeights.SemiBold));
        panel.Children.Add(top);

        if (o.ShowBarcode && TryBarcode(label.Code, out var widths))
        {
            if (o.ShowBarcodeText)
            {
                var code = Text(label.Code, 7.5, FontWeights.Normal, Mono);
                DockPanel.SetDock(code, Dock.Bottom);
                panel.Children.Add(code);
            }
            // Bars fill whatever height is left; the quiet zone (10 modules each side) is kept as margin.
            var modules = widths.Sum() + 20;
            var barsWidth = width - pad * 2;
            var quiet = barsWidth * 10 / modules;
            panel.Children.Add(new Path
            {
                Data = BarsGeometry(widths, 100),
                Fill = Brushes.Black,
                Stretch = Stretch.Fill,
                Margin = new Thickness(quiet, 2, quiet, 0),
                MinHeight = 10,
                SnapsToDevicePixels = true,
            });
        }
        else
        {
            panel.Children.Add(new Border()); // fills the rest
        }

        return new Border
        {
            Width = width,
            Height = height,
            Background = Brushes.White,
            BorderBrush = o.ShowBorder ? Brushes.Gray : Brushes.Transparent,
            BorderThickness = new Thickness(o.ShowBorder ? 0.6 : 0),
            CornerRadius = new CornerRadius(o.ShowBorder ? 3 : 0),
            ClipToBounds = true,
            Child = panel,
        };
    }

    private static bool TryBarcode(string code, out IReadOnlyList<int> widths)
    {
        try
        {
            widths = string.IsNullOrWhiteSpace(code) ? [] : Code128.EncodeWidths(code);
            return widths.Count > 0;
        }
        catch (ArgumentException)
        {
            widths = []; // characters Code 128-B can't encode: print the label without bars
            return false;
        }
    }

    /// <summary>Bars only (no quiet zone), for a <see cref="Path"/> that stretches them to fit.</summary>
    private static Geometry BarsGeometry(IReadOnlyList<int> widths, double height)
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        double x = 0;
        for (var i = 0; i < widths.Count; i++)
        {
            if (i % 2 == 0) group.Children.Add(new RectangleGeometry(new Rect(x, 0, widths[i], height)));
            x += widths[i];
        }
        group.Freeze();
        return group;
    }
}
