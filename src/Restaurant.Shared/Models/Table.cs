using System;
using System.Collections.Generic;
using System.Text;

namespace Restaurant.Shared.Models
{
    public class Table
    {
        public int Id { get; set; }
        public string TableNumber { get; set; } = string.Empty;
        public int Capacity { get; set; }
        public bool IsOccupied { get; set; }
        public string? Location { get; set; }
    }
}
