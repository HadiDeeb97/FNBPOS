using System.Text;
using ClothingStore.Core.Localization;

namespace ClothingStore.Core.Receipts;

/// <summary>
/// Lays a <see cref="ReceiptDocument"/> out as fixed-width text, suitable for 80mm thermal
/// printers (42 columns) or a monospaced print preview.
/// </summary>
public static class ReceiptFormatter
{
    public static IReadOnlyList<string> Format(ReceiptDocument doc, int width = 42)
    {
        width = Math.Clamp(width, 24, 80);
        var lines = new List<string>();
        string M(decimal v) => Money.Format(v, doc.CurrencySymbol);
        string R(string key) => Loc.Get(doc.Language, key);
        var rule = new string('-', width);

        lines.Add(Center(doc.StoreName.ToUpperInvariant(), width));
        foreach (var part in new[] { doc.StoreAddress, doc.StorePhone })
            if (!string.IsNullOrWhiteSpace(part))
                foreach (var l in part.Split('\n')) lines.Add(Center(l.Trim(), width));
        if (!string.IsNullOrWhiteSpace(doc.TaxNumber))
            lines.Add(Center($"{R("Receipt.TaxNo")} {doc.TaxNumber}", width));

        lines.Add(rule);
        lines.Add(Center(doc.Title.ToUpperInvariant(), width));
        if (doc.IsCopy) lines.Add(Center(R("Receipt.Copy"), width));
        lines.Add(rule);
        lines.Add(Pair(R("Receipt.No"), doc.Number, width));
        lines.Add(Pair(R("Receipt.Date"), doc.Date.ToString("yyyy-MM-dd HH:mm"), width));
        lines.Add(Pair(R("Receipt.Cashier"), doc.Cashier, width));
        if (!string.IsNullOrWhiteSpace(doc.Customer)) lines.Add(Pair(R("Receipt.Customer"), doc.Customer, width));
        if (!string.IsNullOrWhiteSpace(doc.Reference)) lines.Add(Pair(R("Receipt.Ref"), doc.Reference, width));
        lines.Add(rule);

        foreach (var item in doc.Lines)
        {
            foreach (var l in Wrap(item.Description, width)) lines.Add(l);
            if (!string.IsNullOrWhiteSpace(item.Detail)) lines.Add(Truncate("  " + item.Detail, width));
            lines.Add(Pair($"  {item.Quantity} x {M(item.UnitPrice)}", M(item.Total), width));
            if (item.Discount != 0) lines.Add(Pair("  " + R("Receipt.Discount"), M(-item.Discount), width));
        }

        lines.Add(rule);
        lines.Add(Pair(R("Receipt.Subtotal"), M(doc.Subtotal), width));
        if (doc.Discount != 0) lines.Add(Pair(R("Receipt.Discount"), M(-doc.Discount), width));
        var taxLabel = Loc.Format(doc.Language, doc.PricesIncludeTax ? "Receipt.TaxIncluded" : "Receipt.Tax", doc.TaxRate);
        lines.Add(Pair(taxLabel, M(doc.Tax), width));
        if (doc.DeliveryFee != 0) lines.Add(Pair(R("Receipt.Delivery"), M(doc.DeliveryFee), width));
        lines.Add(new string('=', width));
        lines.Add(Pair(doc.TotalLabel ?? R("Receipt.Total"), M(doc.Total), width));
        if (!string.IsNullOrWhiteSpace(doc.SecondaryTotal)) lines.Add(Pair("", doc.SecondaryTotal, width));
        lines.Add(new string('=', width));

        foreach (var p in doc.Payments) lines.Add(Pair(p.Label, p.AmountText ?? M(p.Amount), width));
        if (doc.Change > 0) lines.Add(Pair(R("Receipt.Change"), M(doc.Change), width));
        if (!string.IsNullOrWhiteSpace(doc.ChangeLbp)) lines.Add(Pair(doc.Change > 0 ? "" : R("Receipt.Change"), doc.ChangeLbp, width));

        if (doc.ExtraLines.Count > 0)
        {
            lines.Add(rule);
            foreach (var extra in doc.ExtraLines)
                foreach (var l in Wrap(extra, width)) lines.Add(l);
        }

        var itemCount = doc.Lines.Sum(l => l.Quantity);
        lines.Add(rule);
        lines.Add(Center(Loc.Format(doc.Language, "Receipt.Items", itemCount), width));

        if (!string.IsNullOrWhiteSpace(doc.Footer))
        {
            lines.Add("");
            foreach (var footerLine in doc.Footer.Split('\n'))
                foreach (var l in Wrap(footerLine.Trim(), width)) lines.Add(Center(l, width));
        }

        return lines;
    }

    public static string FormatText(ReceiptDocument doc, int width = 42) =>
        string.Join(Environment.NewLine, Format(doc, width));

    internal static string Pair(string left, string right, int width)
    {
        var space = width - right.Length - 1;
        if (space < 1) return Truncate(left + " " + right, width);
        return Truncate(left, space).PadRight(space) + " " + right;
    }

    internal static string Center(string text, int width)
    {
        text = Truncate(text, width);
        var pad = (width - text.Length) / 2;
        return new string(' ', pad) + text;
    }

    private static string Truncate(string text, int width) => text.Length <= width ? text : text[..width];

    internal static IEnumerable<string> Wrap(string text, int width)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield return "";
            yield break;
        }

        var line = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var w = word;
            while (w.Length > width)
            {
                if (line.Length > 0) { yield return line.ToString(); line.Clear(); }
                yield return w[..width];
                w = w[width..];
            }
            if (line.Length + w.Length + (line.Length > 0 ? 1 : 0) > width)
            {
                yield return line.ToString();
                line.Clear();
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(w);
        }
        if (line.Length > 0) yield return line.ToString();
    }
}
