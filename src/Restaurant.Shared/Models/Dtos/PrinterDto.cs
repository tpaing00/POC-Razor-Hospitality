namespace Restaurant.Shared.Models.Dtos;

/// <summary>
/// One registered printer over the wire. Handbook Part II-B · Printers.
///
/// It carries every field of <see cref="Printer"/> except the timestamps' write side:
/// <see cref="UpdatedAt"/> comes back so a detail pane can answer "when did this
/// change", and neither timestamp is read from a client — a caller that could set
/// <c>CreatedAt</c> could rewrite the venue's history of its own hardware.
///
/// <see cref="Address"/> comes back **normalized**, which is not always the string that
/// was sent: <c>192.168.1.50</c> is stored and returned as <c>192.168.1.50:9100</c>, so
/// the field a client renders after a write is the field the server holds rather than
/// the one it typed.
/// </summary>
public class PrinterDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public PrinterRole Role { get; set; } = PrinterRole.Receipts;

    public PrinterTransportKind Transport { get; set; } = PrinterTransportKind.Network;

    public string Address { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
