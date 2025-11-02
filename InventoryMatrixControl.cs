using System;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace StyleWatcherWin
{
    public sealed class InventoryMatrixControl : UserControl
    {
        private readonly DataGridView _grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false };
        private int _lowThreshold = 10;

        public InventoryMatrixControl()
        {
            Controls.Add(_grid);
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
            _grid.RowHeadersVisible = false;
            _grid.CellPainting += Grid_CellPainting;
            _grid.CellToolTipTextNeeded += Grid_CellToolTipTextNeeded;
        }

        public void SetThreshold(int lowThreshold) => _lowThreshold = lowThreshold;

        public void Bind(InventoryRow[] rows)
        {
            var sizes  = InventoryPivot.CollectSortedSizes(rows);
            var colors = InventoryPivot.CollectSortedColors(rows);

            var table = new DataTable();
            table.Columns.Add("颜色/尺码", typeof(string));
            foreach (var s in sizes) table.Columns.Add(s, typeof(string));

            foreach (var color in colors)
            {
                var dr = table.NewRow();
                dr[0] = color;
                foreach (var s in sizes)
                {
                    var cell = rows.FirstOrDefault(r => r.Color == color && r.Size == s);
                    dr[s] = cell is null ? "-" : $"{cell.QtyIn}/{cell.QtyOut}";
                }
                table.Rows.Add(dr);
            }

            _grid.DataSource = table;
        }

        private void Grid_CellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 1) return;
            var val = _grid[e.ColumnIndex, e.RowIndex].Value?.ToString();
            if (string.IsNullOrWhiteSpace(val) || val == "-") return;

            var parts = val.Split('/');
            if (parts.Length != 2) return;
            if (!int.TryParse(parts[0], out var inQty)) return;
            if (!int.TryParse(parts[1], out var outQty)) return;

            var color = _grid[0, e.RowIndex].Value?.ToString();
            var size  = _grid.Columns[e.ColumnIndex].HeaderText;
            e.ToolTipText = $"颜色：{color}\n尺码：{size}\n在库：{inQty}\n可用：{outQty}";
        }

        private void Grid_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 1) return;
            var val = _grid[e.ColumnIndex, e.RowIndex].Value?.ToString();
            if (string.IsNullOrWhiteSpace(val) || val == "-") return;

            var parts = val.Split('/');
            if (parts.Length != 2) return;
            if (!int.TryParse(parts[0], out var inQty)) return;
            if (!int.TryParse(parts[1], out var outQty)) return;

            var back = _grid.DefaultCellStyle.BackColor;
            var fore = _grid.DefaultCellStyle.ForeColor;

            if (outQty < 0 || inQty < 0)
            {
                back = Color.FromArgb(255, 224, 224);
                fore = Color.DarkRed;
            }
            else if (outQty == 0)
            {
                back = Color.Gainsboro;
                fore = Color.DimGray;
            }
            else if (outQty < _lowThreshold)
            {
                back = Color.FromArgb(255, 243, 205);
                fore = Color.DarkOrange;
            }

            e.CellStyle.BackColor = back;
            e.CellStyle.ForeColor = fore;
        }
    }
}
