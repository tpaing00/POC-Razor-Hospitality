using System.Text;

namespace Restaurant.UI.Shared.Services.Printing;

/// <summary>
/// The Star Line Mode command bytes, and every one of them the build uses. One file,
/// each command named beside its byte sequence, so a byte that turns out to be wrong
/// is corrected here without a line of ticket layout moving.
///
/// **Why Star Line Mode rather than ESC/POS.** The TSP143IV supports both. Star Line
/// is the mode it is in out of the box; ESC/POS is an emulation reached by changing a
/// memory switch on the printer with Star's own utility. That switch is a step
/// somebody has to perform on the hardware before one byte of this works, and a step
/// nobody performs on the replacement unit two years later. Choosing the native mode
/// means a printer out of a box prints.
///
/// The two dialects are not interchangeable and the differences are exactly in the
/// commands this ticket uses:
///
/// <list type="bullet">
/// <item>alignment is <c>ESC GS a n</c> here and <c>ESC a n</c> in ESC/POS;</item>
/// <item>emphasis is <c>ESC E</c> / <c>ESC F</c> here and <c>ESC E n</c> in ESC/POS;</item>
/// <item>character expansion is <c>ESC i n1 n2</c> here and <c>GS ! n</c> in ESC/POS.</item>
/// </list>
///
/// So the choice is made once and held, and mixing them produces a label with command
/// bytes printed on it rather than a label that failed cleanly.
///
/// **These byte values are transcribed from the Star Line Mode command specification
/// and have not been checked against a printer.** No TSP143IV was reachable when this
/// was written. Everything above the transport is tested; these constants are the
/// part a person with the hardware verifies first, which is why they are isolated
/// here and why the test print exists.
/// </summary>
public static class StarLine
{
    private const byte Esc = 0x1B;
    private const byte Gs = 0x1D;
    private const byte Rs = 0x1E;

    /// <summary>Line feed. Ends every line of text.</summary>
    public const byte Lf = 0x0A;

    /// <summary>
    /// <c>ESC @</c> — initialize. Clears whatever the last job left set: expansion,
    /// emphasis, alignment, line spacing. Every ticket opens with it, because a
    /// ticket that inherits state from the one before it is a ticket that only
    /// prints correctly when it prints second.
    /// </summary>
    public static readonly byte[] Initialize = { Esc, 0x40 };

    /// <summary>
    /// <c>ESC R 0</c> — select the USA international character set. The ticket is
    /// ASCII by construction (<see cref="BagTicket"/>), so this is belt to the
    /// transliteration's braces: it pins the handful of code points that move
    /// between international sets — <c>#</c>, <c>$</c>, <c>@</c> — to the ones the
    /// layout assumes.
    /// </summary>
    public static readonly byte[] CharacterSetUsa = { Esc, 0x52, 0x00 };

    /// <summary><c>ESC GS a 0</c> — align left. Star Line, not ESC/POS's <c>ESC a n</c>.</summary>
    public static readonly byte[] AlignLeft = { Esc, Gs, 0x61, 0x00 };

    /// <summary><c>ESC GS a 1</c> — align center.</summary>
    public static readonly byte[] AlignCenter = { Esc, Gs, 0x61, 0x01 };

    /// <summary><c>ESC E</c> — emphasis on. No parameter byte, unlike ESC/POS.</summary>
    public static readonly byte[] EmphasisOn = { Esc, 0x45 };

    /// <summary><c>ESC F</c> — emphasis off.</summary>
    public static readonly byte[] EmphasisOff = { Esc, 0x46 };

    /// <summary>
    /// <c>ESC i n1 n2</c> — character expansion, n1 height and n2 width, 0 being
    /// normal and 1 being double. The order number takes <c>1 1</c>.
    ///
    /// Expanded text halves the usable columns, which is why the ticket centres
    /// through <see cref="AlignCenter"/> rather than by padding with spaces: the
    /// printer knows how wide its own characters became and the layout code does not
    /// have to.
    /// </summary>
    public static byte[] Expand(byte height, byte width) => new byte[] { Esc, 0x69, height, width };

    /// <summary><c>ESC i 0 0</c> — back to one-by-one.</summary>
    public static readonly byte[] ExpandNone = { Esc, 0x69, 0x00, 0x00 };

    /// <summary>
    /// <c>ESC d 2</c> — full cut, feeding to the cutting position first.
    ///
    /// Full rather than partial, and the reason is the stock. The SK is linerless
    /// adhesive: the label comes off the cutter and goes onto a bag, so a partial cut
    /// leaves a sticky tab joining this label to the next one. Feeding to the cutting
    /// position is what stops the last printed line being cut through — the cutter
    /// sits some millimetres above the head — and it means the ticket does not have
    /// to guess how many blank lines to feed, which on linerless stock is label
    /// somebody paid for.
    /// </summary>
    public static readonly byte[] CutFull = { Esc, 0x64, 0x02 };

    /// <summary>
    /// <c>ESC RS a 1</c> — enable Automatic Status Back. With ASB on, the printer
    /// pushes a status block up the same channel whenever its condition changes, and
    /// once when ASB is switched on. That is the only way this build learns that the
    /// paper ran out, because nothing else asks.
    /// </summary>
    public static readonly byte[] AutomaticStatusOn = { Esc, Rs, 0x61, 0x01 };

    /// <summary>
    /// The number of columns on 80mm stock at Font A, unexpanded. A literal rather
    /// than a token: §12's add-the-token rule is about values a stylesheet declares,
    /// and this is a property of a print head.
    /// </summary>
    public const int Columns = 48;

    /// <summary>
    /// One line of text, transliterated to printable ASCII and terminated.
    ///
    /// **The ticket is ASCII and this is where that is enforced.** The alternative is
    /// choosing a code page and hoping the printer is on it; a label reading
    /// <c>Crème Brûlée</c> as mojibake is worse than one reading <c>Creme Brulee</c>.
    /// Anything outside 0x20–0x7E is folded to its unaccented base where one exists
    /// and to <c>?</c> where none does. It is also why the printed ticket is the one
    /// surface in this product that does not use §10's middot: <c>·</c> is not ASCII,
    /// so the label separates with whitespace and rule lines.
    /// </summary>
    public static byte[] Text(string line)
    {
        var ascii = Transliterate(line);
        var bytes = new byte[ascii.Length + 1];
        Encoding.ASCII.GetBytes(ascii, 0, ascii.Length, bytes, 0);
        bytes[^1] = Lf;
        return bytes;
    }

    /// <summary>
    /// Printable ASCII, or the nearest thing to it. Decomposing to FormD strips the
    /// combining marks off every Latin letter that has one, which covers the accented
    /// vowels a menu actually carries; the named pairs below are the ones that do not
    /// decompose. Everything left over becomes <c>?</c>, which is visible on the
    /// label rather than silent.
    /// </summary>
    public static string Transliterate(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var folded = value
            .Replace("‘", "'").Replace("’", "'")
            .Replace("“", "\"").Replace("”", "\"")
            .Replace("–", "-").Replace("—", "-")
            .Replace("·", " ").Replace("•", "*")
            .Replace("æ", "ae").Replace("Æ", "AE")
            .Replace("œ", "oe").Replace("Œ", "OE")
            .Replace("ß", "ss")
            .Replace("ø", "o").Replace("Ø", "O")
            .Replace("đ", "d").Replace("Đ", "D")
            .Replace("£", "GBP").Replace("€", "EUR");

        var decomposed = folded.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var ch in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch)
                == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (ch is '\r' or '\n' or '\t')
            {
                builder.Append(' ');
            }
            else if (ch >= 0x20 && ch <= 0x7E)
            {
                builder.Append(ch);
            }
            else
            {
                builder.Append('?');
            }
        }

        return builder.ToString();
    }
}
