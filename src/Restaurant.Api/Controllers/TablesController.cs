using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant.Api.Data;
using Restaurant.Shared.Models;

namespace Restaurant.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TablesController : ControllerBase
    {
        private readonly RestaurantDbContext _context;

        public TablesController(RestaurantDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Table>>> GetTables()
        {
            var tables = await _context.Tables
                .OrderBy(t => t.TableNumber)
                .ToListAsync();

            return Ok(tables);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Table>> GetTable(int id)
        {
            var table = await _context.Tables.FindAsync(id);

            if (table == null)
                return NotFound();

            return Ok(table);
        }

        [HttpGet("available")]
        public async Task<ActionResult<IEnumerable<Table>>> GetAvailableTables()
        {
            var tables = await _context.Tables
                .Where(t => !t.IsOccupied)
                .OrderBy(t => t.TableNumber)
                .ToListAsync();

            return Ok(tables);
        }

        [HttpPost]
        public async Task<ActionResult<Table>> CreateTable(Table table)
        {
            // Check if table number already exists
            if (await _context.Tables.AnyAsync(t => t.TableNumber == table.TableNumber))
                return BadRequest($"Table {table.TableNumber} already exists");

            _context.Tables.Add(table);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetTable), new { id = table.Id }, table);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTable(int id, Table table)
        {
            if (id != table.Id)
                return BadRequest();

            var existingTable = await _context.Tables.FindAsync(id);
            if (existingTable == null)
                return NotFound();

            existingTable.TableNumber = table.TableNumber;
            existingTable.Capacity = table.Capacity;
            existingTable.Location = table.Location;
            existingTable.IsOccupied = table.IsOccupied;

            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpPatch("{id}/toggle-occupancy")]
        public async Task<IActionResult> ToggleOccupancy(int id)
        {
            var table = await _context.Tables.FindAsync(id);
            if (table == null)
                return NotFound();

            table.IsOccupied = !table.IsOccupied;
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTable(int id)
        {
            var table = await _context.Tables.FindAsync(id);
            if (table == null)
                return NotFound();

            _context.Tables.Remove(table);
            await _context.SaveChangesAsync();

            return NoContent();
        }

    }
}
