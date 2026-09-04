namespace Restaurant.Shared.Models;

/// <summary>
/// One printer the venue owns. Handbook Part II-B · Printers.
///
/// **This is a record of ownership, not of health.** A row says the venue has a
/// printer called <c>Kitchen</c> at <c>192.168.1.50:9100</c> whose job is kitchen
/// chits. It does not say the printer is switched on, has paper, or answered anything
/// — a database survives a power cut and the printer does not, so a stored connection
/// state would be a claim nothing behind it can support. That is why there is no
/// <c>Status</c> column and no <c>LastSeen</c>: the only thing that says a printer
/// answers is a test label, reported at the moment it is fired.
///
/// **It is also not a per-device selection.** Which printer a given terminal prints to
/// is that terminal's own stored preference (<c>IPrinterPreference</c>), and nothing
/// here overrides it. A manager re-pointing a tablet from the office would leave the
/// person holding it with no way to know why the label went somewhere else. Reading
/// those choices back into one venue-wide view is the back office's <c>Devices</c>
/// destination, which needs a terminal identity that does not exist (GAP-13).
///
/// **Scope.** There is no venue or organization entity (GAP-13), so every row here
/// belongs to the single implicit venue — exactly as every row in <see cref="MenuItem"/>,
/// <see cref="Table"/> and <see cref="Order"/> already does. This table adds no new
/// single-location assumption; it inherits the one the schema already carries. It
/// deliberately does not carry a <c>LocationId</c> pointing at a table that does not
/// exist: a scope key nothing can populate is not scope, it is a promise. Adding the
/// key belongs to the schema-wide decision GAP-13 records, taken for every table at
/// once.
/// </summary>
public class Printer
{
    public int Id { get; set; }

    /// <summary>
    /// What a person calls it — <c>Kitchen</c>, <c>Bar</c>, <c>Front counter</c>. The
    /// only field a human matches a row against at a glance, and the one that carries
    /// where the printer physically is. There is deliberately no second
    /// <c>Location</c> string: two free-text fields for the same fact are two things
    /// to keep in step, and with no floor model to place a printer in (GAP-13) the
    /// second one would only ever be a longer name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>What it is for. See <see cref="PrinterRole"/> — nothing routes on it
    /// yet (GAP-06).</summary>
    public PrinterRole Role { get; set; } = PrinterRole.Receipts;

    /// <summary>How it is reached, and half of the id a connection is routed by.</summary>
    public PrinterTransportKind Transport { get; set; } = PrinterTransportKind.Network;

    /// <summary>
    /// Host and port over the network, a MAC address over Bluetooth. Stored in the
    /// normalized form <see cref="PrinterAddress"/> produces, so two people typing
    /// <c>192.168.1.50</c> and <c>192.168.1.50:9100</c> collide on the unique index
    /// rather than registering the same printer twice.
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Whether the venue is using this printer today.
    ///
    /// A printer out for repair on Monday should not need its address retyped on
    /// Friday, and deleting the row is the wrong tool for a week. Removal is for a
    /// printer the venue no longer has.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
