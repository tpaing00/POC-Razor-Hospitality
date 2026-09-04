namespace Restaurant.Shared.Models;

/// <summary>
/// What a printer is <em>for</em>. The field a venue with three printers actually
/// tells them apart by.
///
/// **Why a role and not just a name.** The address distinguishes two printers for a
/// machine and the name distinguishes them for a person reading one screen, but
/// neither says what the printer's job is, and the job is what every consumer beyond
/// this screen needs: a chit goes to the kitchen, a drink goes to the bar, a receipt
/// goes to the counter. A name is free text a venue can write anything in, so nothing
/// can be decided from it.
///
/// **Nothing routes on this today, and the screen says so.** Sending a line to the
/// station that cooks it needs a station on <c>OrderItem</c>, which does not exist
/// (GAP-06). So a role is a record of intent — correct, stored, and read by nobody yet.
/// Two printers may carry the same role and nothing chooses between them, because
/// nothing is choosing at all.
///
/// The four values are jobs somebody in a venue already names. They are stored as
/// integers, so the order is fixed and a new job is appended rather than inserted.
/// </summary>
public enum PrinterRole
{
    /// <summary>The customer's receipt at the counter.</summary>
    Receipts = 0,

    /// <summary>The hot line's chit.</summary>
    Kitchen = 1,

    /// <summary>Drinks.</summary>
    Bar = 2,

    /// <summary>Adhesive bag and order labels — what the terminal's bag ticket is.</summary>
    Labels = 3
}
