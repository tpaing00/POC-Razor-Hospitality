using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Shared.Models;
using Restaurant.Shared.Models.Dtos;

namespace Restaurant.Api.Controllers
{
    /// <summary>
    /// The venue's printer registry. Handbook Part II-B · Printers.
    ///
    /// **What this is a record of.** Which printers the venue owns, what each one is
    /// for, and how each is reached. It is not a record of whether any of them is
    /// working: a row survives a power cut and the printer does not, so nothing here
    /// stores or returns a connection state, and the only thing that says a printer
    /// answers is a test label fired at the moment somebody asks.
    ///
    /// **What this is not.** It is not the per-device selection. Which printer a
    /// terminal prints to is that terminal's own stored preference, and no call here
    /// changes it. Reading those choices back into one venue-wide view is the
    /// <c>Devices</c> destination, which needs a terminal identity that does not exist
    /// (GAP-13).
    ///
    /// **Scope.** Every row belongs to the single implicit venue, because there is no
    /// venue entity to scope it to (GAP-13) — the same assumption <c>MenuItem</c>,
    /// <c>Table</c> and <c>Order</c> already carry. This controller adds no new
    /// single-location assumption and does not pretend to have closed the old one:
    /// there is no <c>locationId</c> parameter, because there would be nothing to
    /// check it against.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class PrintersController : ControllerBase
    {
        private readonly RestaurantDbContext _context;

        public PrintersController(RestaurantDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// The venue's printers, grouped by what they are for and named within that.
        /// </summary>
        /// <param name="includeInactive">Include printers marked out of service.
        /// False by default, because the common question is "what can I print to" and
        /// a printer in a repair shop is not an answer to it.</param>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<PrinterDto>>> GetPrinters(
            [FromQuery] bool includeInactive = false)
        {
            var query = _context.Printers.AsQueryable();

            if (!includeInactive)
                query = query.Where(p => p.IsActive);

            // Role then name, so the list reads as a venue thinks about it — the
            // kitchen's printers together, the bar's together — and so the order is the
            // same on every call rather than left to the database.
            // Projected in the query rather than through ToDto, because a method call
            // is not something EF can translate into SQL — it would either fail or
            // silently pull every row back to be mapped in memory.
            var printers = await query
                .OrderBy(p => p.Role)
                .ThenBy(p => p.Name)
                .Select(p => new PrinterDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Role = p.Role,
                    Transport = p.Transport,
                    Address = p.Address,
                    IsActive = p.IsActive,
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt
                })
                .ToListAsync();

            return Ok(printers);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<PrinterDto>> GetPrinter(int id)
        {
            var printer = await _context.Printers.FindAsync(id);

            if (printer == null)
                return NotFound();

            return Ok(ToDto(printer));
        }

        [HttpPost]
        public async Task<ActionResult<PrinterDto>> CreatePrinter(PrinterDto dto)
        {
            if (!TryClean(dto, out var name, out var address, out var problem))
                return BadRequest(problem);

            if (await ClashAsync(dto.Transport, address, excludingId: null) is { } clash)
                return Conflict(clash);

            var printer = new Printer
            {
                Name = name,
                Role = dto.Role,
                Transport = dto.Transport,
                Address = address,
                IsActive = dto.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            _context.Printers.Add(printer);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return Conflict(await ClashAsync(dto.Transport, address, excludingId: null)
                                ?? RaceLine(address));
            }

            // The stored row, not the posted one. The address is normalized on the way
            // in, so a client that echoed its own payload would render a different
            // string from the one the registry holds.
            return CreatedAtAction(nameof(GetPrinter), new { id = printer.Id }, ToDto(printer));
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<PrinterDto>> UpdatePrinter(int id, PrinterDto dto)
        {
            var printer = await _context.Printers.FindAsync(id);
            if (printer == null)
                return NotFound();

            if (!TryClean(dto, out var name, out var address, out var problem))
                return BadRequest(problem);

            if (await ClashAsync(dto.Transport, address, excludingId: id) is { } clash)
                return Conflict(clash);

            printer.Name = name;
            printer.Role = dto.Role;
            printer.Transport = dto.Transport;
            printer.Address = address;
            printer.IsActive = dto.IsActive;
            printer.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return Conflict(await ClashAsync(dto.Transport, address, excludingId: id)
                                ?? RaceLine(address));
            }

            // The updated row comes back rather than 204, because the address may have
            // been normalized and the caller has to render what is stored.
            return Ok(ToDto(printer));
        }

        /// <summary>
        /// Remove a printer the venue no longer has.
        ///
        /// Taking one out of service for a week is <c>IsActive</c>, not this — a
        /// deleted row takes its address with it and somebody retypes it on Friday.
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePrinter(int id)
        {
            var printer = await _context.Printers.FindAsync(id);
            if (printer == null)
                return NotFound();

            _context.Printers.Remove(printer);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>
        /// Validate and normalize what was posted. One sentence out, naming the fault
        /// and the next move (§10), because this is the message a person reads on the
        /// screen rather than a code a developer looks up.
        /// </summary>
        private static bool TryClean(
            PrinterDto dto,
            out string name,
            out string address,
            out string problem)
        {
            name = string.Empty;
            address = string.Empty;
            problem = string.Empty;

            if (!Enum.IsDefined(dto.Role))
            {
                problem = $"'{(int)dto.Role}' is not a printer role · use receipts, kitchen, bar or labels";
                return false;
            }

            if (!Enum.IsDefined(dto.Transport))
            {
                problem = $"'{(int)dto.Transport}' is not a transport · use network or Bluetooth";
                return false;
            }

            name = (dto.Name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                problem = "A name is required · call it what somebody on the floor calls it, like Kitchen or Front counter";
                return false;
            }

            if (name.Length > PrinterAddress.MaxNameLength)
            {
                problem = $"That name is longer than {PrinterAddress.MaxNameLength} characters · shorten it to what fits on a row";
                return false;
            }

            if (!PrinterAddress.TryNormalize(dto.Transport, dto.Address, out var normalized, out var why))
            {
                problem = why;
                return false;
            }

            address = normalized;
            return true;
        }

        /// <summary>
        /// The sentence for a duplicate, or null when there is none.
        ///
        /// It names the row that is already there, because "that address is taken" sends
        /// a person hunting through a list and "Kitchen is already registered at that
        /// address" ends the search. The unique index behind this is what makes the
        /// check true rather than merely likely.
        /// </summary>
        private async Task<string?> ClashAsync(PrinterTransportKind transport, string address, int? excludingId)
        {
            // No tracking: this reads one name to build a sentence with, and it runs
            // after a failed SaveChanges as well as before a good one, where pulling a
            // second copy of a row into the change tracker would be the last thing the
            // context needs.
            var existing = await _context.Printers
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Transport == transport
                                          && p.Address == address
                                          && (excludingId == null || p.Id != excludingId));

            if (existing == null)
                return null;

            return $"'{existing.Name}' is already registered at {address} · edit that printer, or give this one a different address";
        }

        /// <summary>
        /// The sentence for the race the check above cannot close.
        ///
        /// <see cref="ClashAsync"/> reads and then the write happens, so two callers
        /// registering one address at the same moment can both pass it. The unique
        /// index is what stops the second landing — and without this the second caller
        /// would get a 500 carrying a database error, for a situation that is not a
        /// fault and has an obvious next move. The re-read usually names the row that
        /// won; this line is for the case where even that has changed underneath.
        ///
        /// The failing context is refused as a whole, so nothing is half-written.
        /// </summary>
        private static string RaceLine(string address) =>
            $"Another printer was registered at {address} while this was being saved · reload the list, then edit that printer";

        private static PrinterDto ToDto(Printer printer) => new()
        {
            Id = printer.Id,
            Name = printer.Name,
            Role = printer.Role,
            Transport = printer.Transport,
            Address = printer.Address,
            IsActive = printer.IsActive,
            CreatedAt = printer.CreatedAt,
            UpdatedAt = printer.UpdatedAt
        };
    }
}
