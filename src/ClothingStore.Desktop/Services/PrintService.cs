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
/// <remarks><see cref="Size"/> and <see cref="Color"/>, when given, let the size/colour line follow the chosen format.</remarks>
public sealed record LabelData(string Title, string Variant, string Sku, string Price, string? PriceLbp, string Code, string? StoreName = null,
    string? Size = null, string? Color = null);

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
    public bool PrintLabels(IReadOnlyList<LabelData> labels, LabelOptions options, BarcodeType barcodes, int skip = 0, bool choosePrinter = false)
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

        // The printer's own resolution, so each bar is a whole number of its dots (a label printer is usually 203 dpi).
        double dpi = dialog.PrintTicket.PageResolution?.X is int x and > 0 ? x : options.Sheet ? 600 : 203;

        // Copies chosen in the print dialog: the ticket was reset to 1, so the set is sent that many times.
        for (var i = 0; i < Math.Max(1, copies); i++)
        {
            var document = BuildLabelDocument(labels, options, barcodes, skip, dialog.PrintableAreaWidth, dialog.PrintableAreaHeight, dpi);
            dialog.PrintDocument(document.DocumentPaginator, "Price labels");
        }
        return true;
    }

    public static FixedDocument BuildLabelDocument(IReadOnlyList<LabelData> labels, LabelOptions o, BarcodeType barcodes, int skip,
        double pageWidth, double pageHeight, double dpi)
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new Size(pageWidth, pageHeight);

        if (!o.Sheet)
        {
            foreach (var label in labels)
            {
                var page = NewPage(pageWidth, pageHeight);
                page.Children.Add(CreateLabel(label, o, Math.Min(o.WidthPx, pageWidth), Math.Min(o.HeightPx, pageHeight), barcodes, dpi));
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
            var element = CreateLabel(label, o, o.WidthPx, o.HeightPx, barcodes, dpi);
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

    private static readonly FontFamily LabelFont = new("Segoe UI");
    private static readonly FontFamily NarrowLabelFont = new("Bahnschrift SemiCondensed, Arial Narrow, Segoe UI");

    /// <summary>One label at the given size in px, for a printer of <paramref name="dpi"/> dots per inch. Also used for the live preview (96).</summary>
    public static FrameworkElement CreateLabel(LabelData label, LabelOptions o, double width, double height, BarcodeType barcodes, double dpi = 96)
    {
        var symbol = o.ShowBarcode ? BarcodeSymbol.For(label.Code, barcodes) : null;
        var at = LabelGeometry.For(o, width, height, symbol?.TotalModules ?? 0, dpi);
        var canvas = new Canvas { Width = width, Height = height, ClipToBounds = true };

        // ---- Text: in its own box, which cuts it off rather than letting it push the barcode ----
        // Text grows with the label so small and large labels both look balanced.
        var scale = Math.Clamp(height / 140.0, 0.55, 1.8) * Math.Clamp(o.TextScale, 0.5, 2.0);
        var font = o.NarrowFont ? NarrowLabelFont : LabelFont;
        var (align, textAlign) = o.Align switch
        {
            LabelAlign.Left => (HorizontalAlignment.Left, TextAlignment.Left),
            LabelAlign.Right => (HorizontalAlignment.Right, TextAlignment.Right),
            _ => (HorizontalAlignment.Center, TextAlignment.Center),
        };
        TextBlock Text(string text, double size, FontWeight weight, FontFamily? family = null) => new()
        {
            Text = text,
            FontSize = Math.Max(5, size * scale),
            FontWeight = weight,
            FontFamily = family ?? font,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = align,
            TextAlignment = textAlign,
        };

        var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (o.ShowStoreName && !string.IsNullOrWhiteSpace(label.StoreName)) lines.Children.Add(Text(label.StoreName, 7.5, FontWeights.SemiBold));
        if (o.ShowName) lines.Children.Add(Text(label.Title, 10.5, FontWeights.Bold));
        var variant = label.Size is not null || label.Color is not null ? LabelText.Variant(label.Size, label.Color, o.VariantFormat) : label.Variant;
        if (o.ShowVariant && !string.IsNullOrWhiteSpace(variant)) lines.Children.Add(Text(variant, 9, FontWeights.Normal));
        if (o.ShowSku) lines.Children.Add(Text(label.Sku, 8, FontWeights.Normal, Mono));
        if (o.ShowPrice) lines.Children.Add(Price(Text(label.Price, o.PriceStyle == LabelPriceStyle.Normal ? 17 : 24, FontWeights.Bold), o.PriceStyle));
        if (o.ShowLbpPrice && !string.IsNullOrWhiteSpace(label.PriceLbp)) lines.Children.Add(Text(label.PriceLbp, 9.5, FontWeights.SemiBold));
        var textBox = new Grid { Width = at.Text.Width, Height = at.Text.Height, ClipToBounds = true, Children = { lines } };
        Canvas.SetLeft(textBox, at.Text.X);
        Canvas.SetTop(textBox, at.Text.Y);
        canvas.Children.Add(textBox);

        // ---- Barcode: drawn to whole printer dots in its box, centred; upright ones turned a quarter ----
        if (symbol is not null && at.Barcode is { } box)
        {
            var upright = at.Arrangement == LabelArrangement.Upright;
            var (length, thickness) = upright ? (box.Height, box.Width) : (box.Width, box.Height);
            var code = Barcode(symbol, length, thickness, dpi, o.Digits);
            if (upright) code.LayoutTransform = new RotateTransform(-90);
            var (drawnWidth, drawnHeight) = upright ? (code.Height, code.Width) : (code.Width, code.Height);
            Canvas.SetLeft(code, box.X + (box.Width - drawnWidth) / 2);
            Canvas.SetTop(code, box.Y + (box.Height - drawnHeight) / 2);
            canvas.Children.Add(code);
        }

        return new Border
        {
            Width = width,
            Height = height,
            Background = Brushes.White,
            BorderBrush = o.Border == LabelBorder.None ? Brushes.Transparent : Brushes.Gray,
            BorderThickness = new Thickness(o.Border == LabelBorder.None ? 0 : 0.6),
            CornerRadius = new CornerRadius(o.Border == LabelBorder.Rounded ? 3 : 0),
            ClipToBounds = true,
            Child = canvas,
        };
    }

    /// <summary>The price as it is, or white on a black box (it stands out; label printers print it solid black).</summary>
    private static FrameworkElement Price(TextBlock price, LabelPriceStyle style)
    {
        if (style != LabelPriceStyle.Inverted) return price;
        price.Foreground = Brushes.White;
        var align = price.HorizontalAlignment;
        price.HorizontalAlignment = HorizontalAlignment.Center;
        return new Border
        {
            Background = Brushes.Black,
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(price.FontSize * 0.35, 0, price.FontSize * 0.35, price.FontSize * 0.05),
            Margin = new Thickness(0, 1, 0, 1),
            HorizontalAlignment = align,
            Child = price,
        };
    }

    /// <summary>
    /// A barcode <paramref name="length"/> long at most (margins included) and <paramref name="thickness"/> tall. Every
    /// bar is a whole number of printer dots wide, so all bars of one width print alike (bars stretched to an arbitrary
    /// width come out uneven and scan badly). EAN-13 digits in shop style sit as on products ("0 123456 789012") with the
    /// guard bars running down between the groups; Code 128 text, or the one-line style, is centred under the bars.
    /// </summary>
    private static FrameworkElement Barcode(BarcodeSymbol symbol, double length, double thickness, double dpi, LabelDigits digits)
    {
        var module = symbol.ModuleDots(length / 96.0, dpi) * 96.0 / dpi;
        var width = symbol.TotalModules * module;
        var quiet = symbol.QuietModules * module;
        var shop = digits == LabelDigits.Shop && symbol.Type == BarcodeType.Ean13;
        var canvas = new Canvas { Width = width, Height = thickness, SnapsToDevicePixels = true };

        // Digits about 8 modules tall, as on products, but never more than a third of the barcode.
        var fontSize = digits == LabelDigits.Hidden ? 0 : Math.Min(module * (shop ? 11 : 9), thickness * 0.3);
        var textHeight = fontSize * 1.05;
        var bars = Math.Max(thickness * 0.4, thickness - textHeight - (shop ? 0 : 1));
        var guards = shop ? bars + textHeight * 0.5 : bars;

        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var bar in symbol.Bars())
            group.Children.Add(new RectangleGeometry(new Rect(quiet + bar.X * module, 0, bar.Width * module, bar.Guard ? guards : bars)));
        group.Freeze();
        canvas.Children.Add(new Path { Data = group, Fill = Brushes.Black });

        if (fontSize <= 0) return canvas;
        if (shop)
        {
            foreach (var (digit, center) in symbol.ShopDigits())
            {
                var text = new TextBlock
                {
                    Text = digit.ToString(), FontFamily = Mono, FontSize = fontSize, Width = module * 7, TextAlignment = TextAlignment.Center,
                };
                Canvas.SetLeft(text, quiet + (center - 3.5) * module);
                Canvas.SetTop(text, bars - fontSize * 0.08);
                canvas.Children.Add(text);
            }
        }
        else
        {
            var text = new TextBlock { Text = symbol.Code, FontFamily = Mono, FontSize = fontSize, Width = width, TextAlignment = TextAlignment.Center };
            Canvas.SetTop(text, bars + 1);
            canvas.Children.Add(text);
        }
        return canvas;
    }
}
