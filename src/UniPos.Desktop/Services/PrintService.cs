using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using UniPos.Core.Localization;
using UniPos.Desktop.Infrastructure;

namespace UniPos.Desktop.Services;

/// <summary>Prints receipts and reports (monospaced text, auto-fitted to the printer width).</summary>
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

    /// <summary>
    /// Prints text on <paramref name="printer"/> without ever asking (a kitchen ticket while the cashier serves the
    /// next customer). Returns why it couldn't (no printer set, not installed on this PC, off, out of paper...), or
    /// null when the job was handed to the printer.
    /// </summary>
    public static string? PrintTo(IReadOnlyList<string> lines, string? printer, string jobName)
    {
        if (string.IsNullOrWhiteSpace(printer)) return Loc.T("Kitchen.NoPrinter");
        var queue = FindQueue(printer);
        if (queue is null) return Loc.T("Kitchen.PrinterNotFound", printer);
        try
        {
            queue.Refresh();
            // A printer that is off or jammed would just keep the job in the queue: say so now, so it is printed again.
            if (queue.IsOffline || queue.IsNotAvailable) return Loc.T("Kitchen.PrinterOffline", printer);
            if (queue.IsOutOfPaper || queue.HasPaperProblem || queue.IsPaperJammed || queue.IsDoorOpened || queue.IsInError)
                return Loc.T("Kitchen.PrinterProblem", printer);

            var dialog = new PrintDialog { PrintQueue = queue };
            try { dialog.PrintTicket = queue.UserPrintTicket ?? queue.DefaultPrintTicket ?? dialog.PrintTicket; }
            catch (PrintSystemException) { /* keep the dialog's ticket */ }
            dialog.PrintTicket.CopyCount = 1;

            const double padding = 16;
            var width = dialog.PrintableAreaWidth;
            var longest = Math.Max(20, lines.Count == 0 ? 20 : lines.Max(l => l.Length));
            var doc = BuildTextDocument(lines, Math.Clamp((width - padding * 2) / (longest * 0.56), 6, 12));
            doc.PageWidth = width;
            doc.PageHeight = dialog.PrintableAreaHeight;
            doc.ColumnWidth = width;
            doc.PagePadding = new Thickness(padding);
            dialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, jobName);
            return null;
        }
        catch (Exception ex) when (ex is PrintSystemException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Loc.T("Kitchen.PrintFailed", printer, ex.Message);
        }
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
}
