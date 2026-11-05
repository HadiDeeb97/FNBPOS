using UniPos.Core.Entities;
using UniPos.Core.Localization;
using UniPos.Core.Text;

namespace UniPos.Core.Receipts;

/// <summary>
/// The text of a kitchen ticket: the station, the order's number and how it is served first (what the kitchen looks for),
/// then each item with its options and note. No prices.
/// </summary>
public static class KitchenTicketFormatter
{
    /// <param name="language">The kitchen's language (the receipt language).</param>
    public static IReadOnlyList<string> Format(KitchenTicket ticket, string stationName, string? receiptNumber, int width, string language)
    {
        string T(string key, params object?[] args) => Loc.Format(language, key, args);
        var rule = new string('-', width);
        var lines = new List<string>
        {
            ReceiptFormatter.Center(stationName.ToUpperInvariant(), width),
            new string('=', width),
        };
        if (ticket.PrintCount > 0) lines.Add(ReceiptFormatter.Center(T("Ticket.Reprint"), width));
        if (ticket.OrderNumber is { } number) lines.Add(ReceiptFormatter.Center(T("Ticket.Order", number), width));
        lines.Add(ReceiptFormatter.Center(Loc.Get(language, $"Enum.OrderType.{ticket.OrderType}").ToUpperInvariant(), width));
        if (!string.IsNullOrWhiteSpace(ticket.CallName)) lines.Add(ReceiptFormatter.Center(ticket.CallName.Trim(), width));
        lines.Add(ReceiptFormatter.Pair(DateText.Time(ticket.CreatedAt), receiptNumber ?? "", width));
        lines.Add(rule);

        foreach (var item in ticket.Lines)
        {
            foreach (var l in ReceiptFormatter.Wrap($"{item.Quantity} x {item.ProductName}", width)) lines.Add(l);
            if (item.Options.Length > 0)
                foreach (var l in ReceiptFormatter.Wrap(item.Options, width - 4)) lines.Add("    " + l);
            if (!string.IsNullOrWhiteSpace(item.Note))
                foreach (var l in ReceiptFormatter.Wrap("! " + item.Note.Trim(), width - 4)) lines.Add("    " + l);
        }
        lines.Add(rule);
        lines.Add(T("Ticket.Items", ticket.Lines.Sum(l => l.Quantity)));
        return lines;
    }
}
