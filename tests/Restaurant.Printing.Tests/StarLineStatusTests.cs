using Restaurant.UI.Shared.Services.Printing;
using Xunit;

namespace Restaurant.Printing.Tests;

/// <summary>
/// The Automatic Status Back parser.
///
/// **These tests prove the parser does what its own file says it does, and nothing
/// about what a TSP143IV sends.** The bit positions in <see cref="StarLineStatus"/> are
/// transcribed from the Star Line Mode specification and were never checked against a
/// printer, because none was reachable. That is precisely why the parse is a separate
/// tested type rather than four bit tests inlined into a socket read: if the owner's
/// paper-out test reports the wrong thing, the correction is four constants in one
/// file, and these tests will tell them immediately whether they changed what they
/// meant to.
///
/// The behaviour that matters most here is the negative one: an unrecognised block is
/// Unknown, never healthy.
/// </summary>
public class StarLineStatusTests
{
    /// <summary>A block with every reported condition clear. Bit 0 and bit 7 are clear
    /// on every byte, which is the shape an ASB block has.</summary>
    private static byte[] Clean() => new byte[] { 0x20, 0x40, 0x40, 0x40, 0x40, 0x40, 0x40 };

    [Fact]
    public void Nothing_at_all_is_unknown()
    {
        Assert.False(StarLineStatus.Parse(Array.Empty<byte>()).IsKnown);
        Assert.False(StarLineStatus.Parse(new byte[] { 0x20, 0x40 }).IsKnown);
    }

    [Fact]
    public void A_block_that_is_not_shaped_like_a_status_is_unknown()
    {
        // Printable text echoed back down the socket must not decode as a condition.
        var text = System.Text.Encoding.ASCII.GetBytes("READYREADY");

        Assert.False(StarLineStatus.Parse(text).IsKnown);
    }

    [Fact]
    public void An_unknown_status_reports_no_condition_rather_than_health()
    {
        // The whole rule in one assertion: a parser that answered "paper is fine" to
        // bytes it did not understand would put a green chip on a printer nobody asked.
        var unknown = StarLineStatus.Unknown;

        Assert.Null(unknown.ToCondition());
        Assert.False(unknown.CannotPrint);
        Assert.False(unknown.IsKnown);
    }

    [Fact]
    public void A_clean_block_parses_and_reports_no_fault()
    {
        var status = StarLineStatus.Parse(Clean());

        Assert.True(status.IsKnown);
        Assert.False(status.CannotPrint);
        Assert.Null(status.ToCondition());
    }

    [Fact]
    public void Paper_empty_becomes_the_paper_out_state()
    {
        var block = Clean();
        block[4] |= 0x08;

        var status = StarLineStatus.Parse(block);
        var condition = status.ToCondition();

        Assert.True(status.PaperEmpty);
        Assert.True(status.CannotPrint);
        Assert.NotNull(condition);
        Assert.Equal(PrinterState.PaperOut, condition!.State);
        Assert.Contains("load a roll", condition.Message);
        Assert.True(condition.StatusWasReadable);
    }

    [Fact]
    public void A_near_empty_roll_is_not_a_fault()
    {
        var block = Clean();
        block[4] |= 0x04;

        var status = StarLineStatus.Parse(block);

        // It is the one reading that lets somebody change a roll between services
        // rather than during one, so it must not stop a print.
        Assert.True(status.PaperNearEmpty);
        Assert.False(status.CannotPrint);
        Assert.Null(status.ToCondition());
    }

    [Fact]
    public void An_open_cover_reads_as_paper_out_and_says_what_to_do()
    {
        var block = Clean();
        block[1] |= 0x20;

        var condition = StarLineStatus.Parse(block).ToCondition();

        Assert.Equal(PrinterState.PaperOut, condition!.State);
        Assert.Contains("cover is open", condition.Message);
    }

    [Fact]
    public void A_mechanical_fault_is_its_own_state()
    {
        var block = Clean();
        block[2] |= 0x08;

        Assert.Equal(PrinterState.Failed, StarLineStatus.Parse(block).ToCondition()!.State);
    }

    [Fact]
    public void An_offline_printer_reads_as_unreachable()
    {
        var block = Clean();
        block[0] |= 0x08;

        Assert.Equal(PrinterState.Unreachable, StarLineStatus.Parse(block).ToCondition()!.State);
    }

    [Fact]
    public void Two_stacked_blocks_decode_as_the_later_one()
    {
        // The printer pushes a block on every condition change, so a buffer that
        // accumulated two carries the current condition in the second. Reading the
        // first would report a fault that has since been cleared.
        var first = Clean();
        first[4] |= 0x08;

        var buffer = first.Concat(Clean()).ToArray();

        Assert.False(StarLineStatus.Parse(buffer).PaperEmpty);
    }
}
