namespace Restaurant.UI.Shared.Services.Printing;

/// <summary>
/// A Star Line Automatic Status Back block, decoded as far as it can honestly be
/// decoded.
///
/// **The bit positions below are transcribed from the Star Line Mode specification
/// and have not been checked against a printer.** That is the whole reason this type
/// exists as a separate, tested parser instead of four bit tests inlined into a socket
/// read: the tests below prove the parser does what this file says it does, and they
/// prove nothing about what a TSP143IV sends. If the owner's first paper-out test
/// reports the wrong thing, the correction is four constants here.
///
/// **An unrecognised block is <see cref="Unknown"/>, never healthy.** A parser that
/// answered "paper is fine" to bytes it did not understand would put a green chip on a
/// printer nobody asked — §12's rule about the invented battery, applied to a second
/// reading. The setup screen renders an unknown status as "the printer did not report
/// its condition", which is what actually happened.
/// </summary>
/// <param name="IsKnown">Whether the block parsed at all.</param>
/// <param name="Offline">The printer says it is offline.</param>
/// <param name="CoverOpen">The cover is open. On this stock that is also how a roll
/// change looks.</param>
/// <param name="PaperEmpty">The paper ran out.</param>
/// <param name="PaperNearEmpty">The roll is nearly gone. Not a fault — it is the one
/// reading that lets somebody change a roll between services rather than during
/// one.</param>
/// <param name="MechanicalError">The head or the cutter reported a fault.</param>
public readonly record struct StarLineStatus(
    bool IsKnown,
    bool Offline,
    bool CoverOpen,
    bool PaperEmpty,
    bool PaperNearEmpty,
    bool MechanicalError)
{
    /// <summary>Nothing came back, or what came back did not parse.</summary>
    public static readonly StarLineStatus Unknown = new(false, false, false, false, false, false);

    /// <summary>Whether the printer told us it cannot print right now.</summary>
    public bool CannotPrint => IsKnown && (PaperEmpty || CoverOpen || MechanicalError);

    /// <summary>
    /// The shortest ASB block Star defines is seven bytes. Anything shorter is a
    /// fragment of one, and a fragment is not decoded — a half-read block whose
    /// missing half carried the paper bit would read as healthy.
    /// </summary>
    private const int MinimumLength = 7;

    /// <summary>
    /// Decode the first complete status block in <paramref name="buffer"/>.
    ///
    /// Every byte of an ASB block has bit 0 clear and bit 7 clear, which is the shape
    /// check used here: it is what stops a run of printable text or a stray echo being
    /// decoded as a status. It is a weak check and it is meant to be — its job is to
    /// reject obvious non-status, not to validate.
    /// </summary>
    public static StarLineStatus Parse(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < MinimumLength)
        {
            return Unknown;
        }

        // Take the last complete block. The printer pushes one on every condition
        // change, so a buffer that accumulated two carries the current condition in
        // the second.
        var start = ((buffer.Length - MinimumLength) / MinimumLength) * MinimumLength;
        var block = buffer.Slice(start, MinimumLength);

        foreach (var b in block)
        {
            if ((b & 0x01) != 0 || (b & 0x80) != 0)
            {
                return Unknown;
            }
        }

        return new StarLineStatus(
            IsKnown: true,
            Offline: (block[0] & 0x08) != 0,
            CoverOpen: (block[1] & 0x20) != 0,
            MechanicalError: (block[2] & 0x08) != 0 || (block[2] & 0x04) != 0,
            PaperNearEmpty: (block[4] & 0x04) != 0,
            PaperEmpty: (block[4] & 0x08) != 0);
    }

    /// <summary>
    /// The condition this status puts the printer in, or null when it does not change
    /// anything the caller already knows. §10 governs the sentences: cause, then the
    /// next move, in one line.
    /// </summary>
    public PrinterCondition? ToCondition()
    {
        if (!IsKnown)
        {
            return null;
        }

        if (PaperEmpty)
        {
            return new PrinterCondition(
                PrinterState.PaperOut,
                "Paper out · load a roll and print again",
                StatusWasReadable: true);
        }

        if (CoverOpen)
        {
            return new PrinterCondition(
                PrinterState.PaperOut,
                "Printer cover is open · close it and print again",
                StatusWasReadable: true);
        }

        if (MechanicalError)
        {
            return new PrinterCondition(
                PrinterState.Failed,
                "The printer reported a mechanical fault · power it off and on, then print again",
                StatusWasReadable: true);
        }

        if (Offline)
        {
            return new PrinterCondition(
                PrinterState.Unreachable,
                "The printer reports itself offline · check it is switched on at the unit",
                StatusWasReadable: true);
        }

        return null;
    }
}
