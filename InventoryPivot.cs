
using System;
using System.Collections.Generic;
using System.Linq;

namespace StyleWatcherWin
{
    public static class InventoryPivot
    {
        private static readonly string[] CanonicalSizeOrder = 
        {
            "XS","S","M","L","XL","2XL","3XL","4XL","5XL","6XL",
            "AXL-CP","A3XL-CP","A4XL-CP"
        };

        public static string[] CollectSortedSizes(IEnumerable<InventoryRow> rows)
        {
            var all = rows.Select(r => r.Size).Distinct().ToArray();
            var known = all.Where(s => CanonicalSizeOrder.Contains(s)).OrderBy(s => Array.IndexOf(CanonicalSizeOrder, s));
            var other = all.Where(s => !CanonicalSizeOrder.Contains(s)).OrderBy(s => s);
            return known.Concat(other).ToArray();
        }

        public static string[] CollectSortedColors(IEnumerable<InventoryRow> rows)
        {
            return rows
                .GroupBy(r => r.Color)
                .Select(g => (color: g.Key, total: g.Sum(x => Math.Max(0, x.QtyOut))))
                .OrderByDescending(t => t.total)
                .ThenBy(t => t.color)
                .Select(t => t.color)
                .ToArray();
        }

        public static (int sumIn, int sumOut, int lowCount, int abnormalCount) Summaries(IEnumerable<InventoryRow> rows, int lowThreshold)
        {
            int sumIn = 0, sumOut = 0, low = 0, abn = 0;
            foreach (var r in rows)
            {
                sumIn  += r.QtyIn;
                sumOut += r.QtyOut;
                if (r.QtyOut < 0 || r.QtyIn < 0) abn++;
                else if (r.QtyOut >= 0 && r.QtyOut < lowThreshold) low++;
            }
            return (sumIn, sumOut, low, abn);
        }

        public static string[] Top3PrimaryWarehouses(IEnumerable<InventoryRow> allRows)
        {
            return allRows
                .GroupBy(r => r.Warehouse)
                .Select(g => new { wh = g.Key, total = g.Sum(x => Math.Max(0, x.QtyOut)) })
                .OrderByDescending(x => x.total)
                .Take(3)
                .Select(x => x.wh)
                .ToArray();
        }

        public static InventoryRow[] FilterByWarehouse(IEnumerable<InventoryRow> rows, string? warehouseOrNullForAll)
        {
            if (warehouseOrNullForAll == null) return rows.ToArray();
            if (warehouseOrNullForAll == "__ALL__") return rows.ToArray();
            return rows.Where(r => r.Warehouse == warehouseOrNullForAll).ToArray();
        }
    }
}
