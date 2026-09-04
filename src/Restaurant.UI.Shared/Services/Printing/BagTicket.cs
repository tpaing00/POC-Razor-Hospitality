using System.Globalization;
using Restaurant.Shared.Models.Dtos;

namespace Restaurant.UI.Shared.Services.Printing;

/// <summary>
/// The bag ticket, as bytes. Handbook Part II-A · Bag ticket is the specification;
/// this is the whole of the implementation.
///
/// **It is a pure function and that is deliberate.** It takes a DTO and a local
/// timestamp and returns a <c>byte[]</c>. It opens nothing, reads no clock, touches no
/// platform and holds no state, so it is the one part of printing that can be tested
/// with no printer in the room — which is exactly why the layout and the command
/// sequence live here rather than being assembled inside a socket write.
///
/// **It is a bag ticket and not a customer receipt**, and the difference is not
/// cosmetic. A receipt states what was tendered: subtotal, tax, the tender taken, the
/// change given. GAP-08 records that the POC has no payments domain — nothing records
/// how a check settled and there is no tax rule to apply — so a receipt printed from
/// this data would make claims about money that nothing behind it supports. What the
/// data can honestly produce is what was ordered, when, and what was asked for, on a
/// label that goes on a bag.
/// </summary>
public static class BagTicket
{
    /// <summary>The width of the quantity column, plus its two trailing spaces.</summary>
    private const int QuantityColumn = 4;

    /// <summary>
    /// The terminal's local rendering of when the order was placed.
    ///
    /// <c>OrdersController</c> writes <c>DateTime.UtcNow</c> into <c>Order.CreatedAt</c>,
    /// and a JSON round trip commonly hands it back with <c>Kind</c> unspecified. Both
    /// of those are UTC in fact, so both are converted; a value that already says it is
    /// local is left alone. This is the one place the assumption is made, so it is the
    /// one place to correct if the API ever starts storing local time.
    /// </summary>
    public static DateTime LocalPlacedAt(DateTime createdAt) => createdAt.Kind switch
    {
        DateTimeKind.Local => createdAt,
        DateTimeKind.Utc => createdAt.ToLocalTime(),
        _ => DateTime.SpecifyKind(createdAt, DateTimeKind.Utc).ToLocalTime()
    };

    /// <summary>
    /// One label for one order.
    /// </summary>
    /// <param name="order">The <c>OrderDto</c> the API returned from
    /// <c>POST /api/orders</c> — the first moment the order has a number at all.</param>
    /// <param name="placedLocal">When the order was placed, in the terminal's own time.
    /// Passed in rather than read off a clock so this function stays deterministic and
    /// the tests can state an expected label byte for byte.</param>
    /// <param name="columns">Characters per line. 48 is 80mm at Font A, which is what
    /// the TSP143IV prints unexpanded.</param>
    public static byte[] Build(OrderDto order, DateTime placedLocal, int columns = StarLine.Columns)
    {
        ArgumentNullException.ThrowIfNull(order);

        var w = new TicketWriter(columns);

        w.Raw(StarLine.Initialize);
        w.Raw(StarLine.CharacterSetUsa);

        // ─── Header, hardware-centred ─────────────────────────────────────────
        // Centred by ESC GS a 1 rather than by padding with spaces, because the
        // header's first line is double width and the printer is the only party
        // that knows how many columns that leaves. Padding here would centre the
        // expanded line against 48 columns it does not have.
        w.Raw(StarLine.AlignCenter);

        // The number is the biggest thing on the label because it is what a person
        // matches against a bag at arm's length. Order.Id, not Order.OrderNumber:
        // the check header already reads "Order #482", and ORD-20260904-A1B2C3D4 at
        // double width is 21 of the 24 available columns and unreadable as an
        // identifier anyway. The long form goes underneath at normal size, where it
        // is for tracing rather than for matching.
        w.Raw(StarLine.EmphasisOn);
        w.Raw(StarLine.Expand(1, 1));
        w.Line($"#{order.Id}");
        w.Raw(StarLine.ExpandNone);
        w.Raw(StarLine.EmphasisOff);

        if (!string.IsNullOrWhiteSpace(order.OrderNumber))
        {
            w.Line(order.OrderNumber);
        }

        // Time, and the table where there is one. TableNumber is dropped rather than
        // rendered empty: an order with no table is a real thing here — the floor
        // plan that would pick one is not built — and "TABLE" over nothing is a line
        // of label that says nothing.
        var stamp = placedLocal.ToString("HH:mm", CultureInfo.InvariantCulture);
        w.Line(string.IsNullOrWhiteSpace(order.TableNumber)
            ? stamp
            : $"{stamp}   TABLE {order.TableNumber}");

        w.Raw(StarLine.AlignLeft);
        w.Rule();

        // ─── Lines ────────────────────────────────────────────────────────────
        if (order.Items.Count == 0)
        {
            // A check with no lines still prints a header and a cut. A mis-tap
            // produces a short label somebody throws away, which is better than a
            // job that ends without a cut and leaves the next one joined to it.
            w.Line("NO ITEMS ON THIS CHECK");
        }
        else
        {
            foreach (var item in order.Items)
            {
                WriteLine(w, item, columns);
            }
        }

        w.Rule();

        // The footer is the line count and nothing else. No subtotal, no tax, no
        // total — see the class comment.
        var count = order.Items.Sum(i => i.Quantity);
        w.Line(count == 1 ? "1 ITEM" : $"{count} ITEMS");

        w.Raw(StarLine.CutFull);
        return w.ToArray();
    }

    /// <summary>
    /// The label the setup screen's test control prints. It carries no order, because
    /// its whole job is to answer "does this pairing reach that printer" before
    /// service rather than during it — and it says what it is, so a label found on a
    /// bench is not mistaken for a check.
    /// </summary>
    public static byte[] BuildTestLabel(string terminalId, DateTime nowLocal, int columns = StarLine.Columns)
    {
        var w = new TicketWriter(columns);

        w.Raw(StarLine.Initialize);
        w.Raw(StarLine.CharacterSetUsa);
        w.Raw(StarLine.AlignCenter);

        w.Raw(StarLine.EmphasisOn);
        w.Raw(StarLine.Expand(1, 1));
        w.Line("TEST");
        w.Raw(StarLine.ExpandNone);
        w.Raw(StarLine.EmphasisOff);

        w.Line(nowLocal.ToString("HH:mm  dd MMM yyyy", CultureInfo.InvariantCulture));
        w.Raw(StarLine.AlignLeft);
        w.Rule();

        // The width check. If the rule lines above and below reach the edge of the
        // label and this row's last character is on the same edge, the column count
        // is right for this stock; if they wrap, it is not, and the number to change
        // is StarLine.Columns.
        w.Line(terminalId);
        w.Line(Ruler(columns));
        w.Rule();
        w.Line("This printer is paired.");

        w.Raw(StarLine.CutFull);
        return w.ToArray();
    }

    /// <summary>
    /// A column ruler: a digit every ten characters, filled with dots. Printed on the
    /// test label so a wrong column count is visible on the paper rather than only in
    /// a wrapped dish name three services later.
    /// </summary>
    internal static string Ruler(int columns)
    {
        var chars = new char[columns];
        for (var i = 0; i < columns; i++)
        {
            chars[i] = (i + 1) % 10 == 0
                ? (char)('0' + ((i + 1) / 10) % 10)
                : '.';
        }

        return new string(chars);
    }

    /// <summary>
    /// One item: <c>{qty}  {name}</c>, wrapped to the name's own column, with
    /// <c>SpecialInstructions</c> beneath it prefixed <c>*</c> and indented further.
    ///
    /// The instruction line is omitted entirely when the field is empty. A "Notes:"
    /// label over nothing costs label length on linerless stock and tells an
    /// assembler nothing.
    /// </summary>
    private static void WriteLine(TicketWriter w, OrderItemDto item, int columns)
    {
        var name = string.IsNullOrWhiteSpace(item.MenuItemName) ? "ITEM" : item.MenuItemName.Trim();
        var quantity = item.Quantity.ToString(CultureInfo.InvariantCulture);

        // The quantity sits right-aligned in its own column so a run of lines reads
        // as a column of numbers rather than as ragged text. A three-figure quantity
        // widens the column for its own line only; nothing wraps because of it.
        var lead = quantity.Length >= QuantityColumn - 1
            ? quantity + " "
            : quantity.PadLeft(QuantityColumn - 2) + "  ";

        var indent = new string(' ', lead.Length);
        var wrapped = Wrap(name, columns - lead.Length);

        for (var i = 0; i < wrapped.Count; i++)
        {
            w.Line((i == 0 ? lead : indent) + wrapped[i]);
        }

        if (string.IsNullOrWhiteSpace(item.SpecialInstructions))
        {
            return;
        }

        const string marker = "* ";
        var noteIndent = indent + marker;
        var noteHang = new string(' ', noteIndent.Length);
        var note = Wrap(item.SpecialInstructions.Trim(), columns - noteIndent.Length);

        for (var i = 0; i < note.Count; i++)
        {
            w.Line((i == 0 ? noteIndent : noteHang) + note[i]);
        }
    }

    /// <summary>
    /// Greedy word wrap at <paramref name="width"/>. A word longer than the line is
    /// broken rather than allowed to overflow, because the printer's own wrap would
    /// put the overflow at column zero and break the indent the layout depends on.
    /// </summary>
    internal static IReadOnlyList<string> Wrap(string value, int width)
    {
        if (width < 1)
        {
            width = 1;
        }

        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = string.Empty;

        foreach (var word in words)
        {
            var candidate = word;

            while (candidate.Length > width)
            {
                if (current.Length > 0)
                {
                    lines.Add(current);
                    current = string.Empty;
                }

                lines.Add(candidate[..width]);
                candidate = candidate[width..];
            }

            if (current.Length == 0)
            {
                current = candidate;
            }
            else if (current.Length + 1 + candidate.Length <= width)
            {
                current = current + " " + candidate;
            }
            else
            {
                lines.Add(current);
                current = candidate;
            }
        }

        if (current.Length > 0 || lines.Count == 0)
        {
            lines.Add(current);
        }

        return lines;
    }

    /// <summary>
    /// A byte sink that knows the column count. Nothing here formats — it appends
    /// command sequences and appends text lines through
    /// <see cref="StarLine.Text(string)"/>, which is where the ASCII rule is enforced.
    /// </summary>
    private sealed class TicketWriter(int columns)
    {
        private readonly List<byte> _bytes = new(512);

        public void Raw(byte[] command) => _bytes.AddRange(command);

        public void Line(string text) => _bytes.AddRange(StarLine.Text(text));

        public void Rule() => Line(new string('-', columns));

        public byte[] ToArray() => _bytes.ToArray();
    }
}
