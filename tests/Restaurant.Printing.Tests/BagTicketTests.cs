using System.Text;
using Restaurant.Shared.Models.Dtos;
using Restaurant.UI.Shared.Services.Printing;
using Xunit;

namespace Restaurant.Printing.Tests;

/// <summary>
/// The bag ticket's byte stream, tested without hardware.
///
/// **What these tests can and cannot prove.** They prove that
/// <see cref="BagTicket.Build"/> emits the sequence this build says it emits: the
/// commands in the right order, the layout at the right columns, the ASCII rule
/// enforced, the wrap holding its indent. They prove nothing whatever about whether a
/// TSP143IV interprets those bytes as intended — no printer was reachable when this was
/// written, and the command constants in <see cref="StarLine"/> are transcribed from
/// the Star Line Mode specification rather than confirmed against a unit. If the owner's
/// first label comes out wrong, the fault is in those constants and not in the
/// assembly, and that is exactly the division these tests are drawn along.
/// </summary>
public class BagTicketTests
{
    private static readonly DateTime Placed = new(2026, 9, 4, 14, 32, 0, DateTimeKind.Local);

    private static OrderDto Check() => new()
    {
        Id = 482,
        OrderNumber = "ORD-20260904-A1B2C3D4",
        TableNumber = "12",
        CreatedAt = Placed,
        Items =
        {
            new OrderItemDto
            {
                MenuItemId = 1,
                MenuItemName = "Ribeye",
                Quantity = 2,
                UnitPrice = 42.00m,
                Subtotal = 84.00m,
                SpecialInstructions = "no garlic, sauce on side"
            },
            new OrderItemDto
            {
                MenuItemId = 2,
                MenuItemName = "Caesar Salad",
                Quantity = 1,
                UnitPrice = 12.00m,
                Subtotal = 12.00m
            }
        }
    };

    /// <summary>The printable text of a ticket, one entry per line, with every command
    /// sequence stripped. This is what a person would read off the label.</summary>
    private static List<string> Lines(byte[] ticket)
    {
        var text = new StringBuilder();

        for (var i = 0; i < ticket.Length; i++)
        {
            if (ticket[i] != 0x1B)
            {
                text.Append((char)ticket[i]);
                continue;
            }

            // Skip the command, whose length depends on which one it is. Only the
            // commands this ticket actually emits need to be recognised here.
            i += ticket[i + 1] switch
            {
                0x40 => 1,          // ESC @
                0x45 or 0x46 => 1,  // ESC E / ESC F
                0x52 => 2,          // ESC R n
                0x64 => 2,          // ESC d n
                0x69 => 3,          // ESC i n1 n2
                0x1D => 3,          // ESC GS a n
                0x1E => 3,          // ESC RS a n
                _ => 1
            };
        }

        return text.ToString().Split('\n', StringSplitOptions.None)
            .Where(l => l.Length > 0 || true)
            .ToList();
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            var hit = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    hit = false;
                    break;
                }
            }

            if (hit)
            {
                return true;
            }
        }

        return false;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            var hit = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    hit = false;
                    break;
                }
            }

            if (hit)
            {
                return i;
            }
        }

        return -1;
    }

    // ─── The command envelope ────────────────────────────────────────────────

    [Fact]
    public void Ticket_opens_with_initialize()
    {
        var ticket = BagTicket.Build(Check(), Placed);

        // A ticket that inherits expansion or emphasis from the job before it is a
        // ticket that only prints correctly when it prints second.
        Assert.Equal(0x1B, ticket[0]);
        Assert.Equal(0x40, ticket[1]);
    }

    [Fact]
    public void Ticket_ends_with_a_full_cut()
    {
        var ticket = BagTicket.Build(Check(), Placed);
        var tail = ticket[^StarLine.CutFull.Length..];

        Assert.Equal(StarLine.CutFull, tail);
    }

    [Fact]
    public void Header_is_hardware_centred_and_the_body_is_left()
    {
        var ticket = BagTicket.Build(Check(), Placed);

        var centre = IndexOf(ticket, StarLine.AlignCenter);
        var left = IndexOf(ticket, StarLine.AlignLeft);

        // Centring is the printer's job, not the layout's: expanded text halves the
        // usable columns and only the printer knows that. If this ever inverted, the
        // header would be padded against 48 columns it does not have.
        Assert.True(centre > 0, "the header is centred");
        Assert.True(left > centre, "the body returns to left alignment after the header");
    }

    [Fact]
    public void Order_number_is_emphasised_and_double_size_and_the_expansion_is_cancelled()
    {
        var ticket = BagTicket.Build(Check(), Placed);

        var expandOn = IndexOf(ticket, StarLine.Expand(1, 1));
        var expandOff = IndexOf(ticket, StarLine.ExpandNone);
        var number = IndexOf(ticket, Encoding.ASCII.GetBytes("#482"));

        Assert.True(expandOn >= 0, "the number is expanded");
        Assert.True(Contains(ticket, StarLine.EmphasisOn));
        Assert.True(Contains(ticket, StarLine.EmphasisOff));
        Assert.InRange(number, expandOn, expandOff);
    }

    // ─── The layout ──────────────────────────────────────────────────────────

    [Fact]
    public void Header_carries_the_short_number_the_long_number_the_time_and_the_table()
    {
        var lines = Lines(BagTicket.Build(Check(), Placed));

        Assert.Equal("#482", lines[0]);
        Assert.Equal("ORD-20260904-A1B2C3D4", lines[1]);
        Assert.Equal("14:32   TABLE 12", lines[2]);
    }

    [Fact]
    public void An_order_with_no_table_drops_the_table_and_keeps_the_time()
    {
        var order = Check();
        order.TableNumber = null;

        var lines = Lines(BagTicket.Build(order, Placed));

        // "TABLE" over nothing costs label length on linerless stock and says nothing.
        Assert.Equal("14:32", lines[2]);
    }

    [Fact]
    public void Each_line_is_quantity_then_name_and_instructions_hang_beneath_it()
    {
        var lines = Lines(BagTicket.Build(Check(), Placed));

        Assert.Contains(" 2  Ribeye", lines);
        Assert.Contains("    * no garlic, sauce on side", lines);
        Assert.Contains(" 1  Caesar Salad", lines);
    }

    [Fact]
    public void An_empty_instruction_prints_no_line_at_all()
    {
        var lines = Lines(BagTicket.Build(Check(), Placed));

        // The Caesar Salad has no instructions. Nothing marked with the * prefix may
        // follow it — a "Notes:" label over nothing is a line that says nothing.
        var salad = lines.FindIndex(l => l.Contains("Caesar Salad"));
        Assert.True(salad >= 0);
        Assert.DoesNotContain("*", lines[salad + 1]);
    }

    [Fact]
    public void The_footer_counts_covers_rather_than_lines()
    {
        var lines = Lines(BagTicket.Build(Check(), Placed));

        // Two ribeyes and one salad is three items, not two lines.
        Assert.Contains("3 ITEMS", lines);
    }

    [Fact]
    public void One_item_is_singular()
    {
        var order = Check();
        order.Items.RemoveAt(0);

        Assert.Contains("1 ITEM", Lines(BagTicket.Build(order, Placed)));
    }

    [Fact]
    public void A_check_with_no_lines_still_prints_a_header_and_a_cut()
    {
        var order = Check();
        order.Items.Clear();

        var ticket = BagTicket.Build(order, Placed);
        var lines = Lines(ticket);

        // A mis-tap produces a short label somebody throws away, which is better than
        // a job that ends without a cut and leaves the next one joined to it.
        Assert.Equal("#482", lines[0]);
        Assert.Contains("NO ITEMS ON THIS CHECK", lines);
        Assert.Equal(StarLine.CutFull, ticket[^StarLine.CutFull.Length..]);
    }

    // ─── Width ───────────────────────────────────────────────────────────────

    [Fact]
    public void No_printed_line_exceeds_the_column_count()
    {
        var order = Check();
        order.Items.Add(new OrderItemDto
        {
            MenuItemName = "Slow-braised short rib with horseradish mash and charred spring onion",
            Quantity = 3,
            SpecialInstructions = "hold the horseradish entirely, extra onion on the side please, and cut it in half"
        });

        foreach (var line in Lines(BagTicket.Build(order, Placed)))
        {
            Assert.True(line.Length <= StarLine.Columns, $"'{line}' is {line.Length} columns");
        }
    }

    [Fact]
    public void A_wrapped_name_hangs_at_the_name_column()
    {
        var order = Check();
        order.Items.Clear();
        order.Items.Add(new OrderItemDto
        {
            MenuItemName = "Slow-braised short rib with horseradish mash and charred spring onion",
            Quantity = 3
        });

        var lines = Lines(BagTicket.Build(order, Placed));
        var first = lines.FindIndex(l => l.StartsWith(" 3  "));

        Assert.True(first >= 0);
        // A two-line dish still reads as one line item, because the continuation is
        // indented to the name's own column rather than starting under the quantity.
        Assert.StartsWith("    ", lines[first + 1]);
        Assert.False(lines[first + 1].StartsWith("     "), "the hang is the name column, not deeper");
    }

    [Fact]
    public void A_word_longer_than_the_line_is_broken_rather_than_overflowed()
    {
        var wrapped = BagTicket.Wrap(new string('X', 100), 20);

        Assert.All(wrapped, l => Assert.True(l.Length <= 20));
        Assert.Equal(100, wrapped.Sum(l => l.Length));
    }

    [Fact]
    public void The_test_label_ruler_is_exactly_the_column_count()
    {
        var ruler = BagTicket.Ruler(StarLine.Columns);

        Assert.Equal(StarLine.Columns, ruler.Length);
        // A digit every ten characters: if this wraps on the paper, the printer is not
        // 48 columns wide and StarLine.Columns is the number to change.
        Assert.Equal('1', ruler[9]);
        Assert.Equal('2', ruler[19]);
        Assert.Equal('4', ruler[39]);
    }

    // ─── ASCII ───────────────────────────────────────────────────────────────

    [Fact]
    public void Every_byte_of_text_is_printable_ascii()
    {
        var order = Check();
        order.Items.Clear();
        order.Items.Add(new OrderItemDto
        {
            MenuItemName = "Crème Brûlée",
            Quantity = 1,
            SpecialInstructions = "no crème — 日本語 — “quoted”"
        });

        var ticket = BagTicket.Build(order, Placed);
        var lines = Lines(ticket);

        Assert.Contains(" 1  Creme Brulee", lines);

        foreach (var line in lines)
        {
            foreach (var ch in line)
            {
                Assert.InRange(ch, (char)0x20, (char)0x7E);
            }
        }
    }

    [Fact]
    public void A_character_with_no_transliteration_prints_as_a_question_mark()
    {
        // Visible on the label rather than silent. The alternative is picking a code
        // page and hoping the printer is on it.
        Assert.Equal("???", StarLine.Transliterate("日本語"));
        Assert.Equal("Creme Brulee", StarLine.Transliterate("Crème Brûlée"));
        Assert.Equal("'quoted' - dash", StarLine.Transliterate("‘quoted’ — dash"));
    }

    // ─── Time ────────────────────────────────────────────────────────────────

    [Fact]
    public void A_utc_created_at_is_rendered_in_the_terminals_own_time()
    {
        var utc = new DateTime(2026, 9, 4, 14, 32, 0, DateTimeKind.Utc);

        // The API writes DateTime.UtcNow, and a label printed in a venue has to read
        // the venue's clock. Both Utc and Unspecified are treated as UTC, because both
        // of them are UTC in fact after a JSON round trip.
        Assert.Equal(utc.ToLocalTime(), BagTicket.LocalPlacedAt(utc));
        Assert.Equal(
            utc.ToLocalTime(),
            BagTicket.LocalPlacedAt(DateTime.SpecifyKind(utc, DateTimeKind.Unspecified)));
        Assert.Equal(Placed, BagTicket.LocalPlacedAt(Placed));
    }

    // ─── The known check, byte for byte ──────────────────────────────────────

    [Fact]
    public void A_known_check_produces_a_known_stream()
    {
        // The whole point of the exercise: one check whose bytes are written out here
        // in full, so any change to the command sequence or the layout has to be
        // deliberate. Read it as the specification of what goes down the socket.
        var order = new OrderDto
        {
            Id = 7,
            OrderNumber = "ORD-1",
            TableNumber = "3",
            CreatedAt = Placed,
            Items = { new OrderItemDto { MenuItemName = "Fries", Quantity = 1 } }
        };

        var expected = new List<byte>();
        void Cmd(byte[] c) => expected.AddRange(c);
        void Txt(string t) => expected.AddRange(StarLine.Text(t));

        Cmd(StarLine.Initialize);
        Cmd(StarLine.CharacterSetUsa);
        Cmd(StarLine.AlignCenter);
        Cmd(StarLine.EmphasisOn);
        Cmd(StarLine.Expand(1, 1));
        Txt("#7");
        Cmd(StarLine.ExpandNone);
        Cmd(StarLine.EmphasisOff);
        Txt("ORD-1");
        Txt("14:32   TABLE 3");
        Cmd(StarLine.AlignLeft);
        Txt(new string('-', 48));
        Txt(" 1  Fries");
        Txt(new string('-', 48));
        Txt("1 ITEM");
        Cmd(StarLine.CutFull);

        Assert.Equal(expected.ToArray(), BagTicket.Build(order, Placed));
    }
}
