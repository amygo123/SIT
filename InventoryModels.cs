
using System;

namespace StyleWatcherWin
{
    public sealed class InventoryRow
    {
        public string Style { get; init; } = "";
        public string Color { get; init; } = "";
        public string Size  { get; init; } = "";
        public string Warehouse { get; init; } = "";
        public int QtyIn { get; init; }
        public int QtyOut { get; init; }
    }

    public sealed class InventorySnapshot
    {
        public string Input { get; init; } = "";
        public DateTime FetchedAt { get; init; } = DateTime.Now;
        public InventoryRow[] Rows { get; init; } = Array.Empty<InventoryRow>();
    }
}
