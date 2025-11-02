
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StyleWatcherWin
{
    public partial class ResultForm : Form
    {
        // ====== 现有字段（保留你已有的） ======
        // 假设以下控件在你的现有代码里已定义（_btnQuery, _boxInput, _pvTrend）。
        // 如果命名不一致，仍可按 OnShown 强制拉取并显示面板。

        // ====== 库存功能字段（新增） ======
        readonly HttpClient _invHttp = new HttpClient();
        InventoryClient _invClient;
        readonly Dictionary<string, InventorySnapshot> _invCache = new();
        Panel _invPanel;
        FlowLayoutPanel _invPrimaryChips;
        Label _invSummary;
        InventoryMatrixControl _invMatrix;
        string[] _invPrimaryWarehouses = Array.Empty<string>();
        string _invCurrentWarehouse = "__ALL__";

        // 常量配置（不依赖 Config.cs，先确保跑起来；后续可迁移回配置）
        const string InventoryApiUrl = "http://192.168.40.97:8000/inventory";
        const int InventoryTimeoutSeconds = 4;
        const int InventoryLowThreshold = 10;
        const int InventoryCacheTtlSeconds = 300;

        public ResultForm()
        {
            InitializeComponent();

            // 尝试在构造后挂接
            try
            {
                _invClient = new InventoryClient(_invHttp, InventoryApiUrl, InventoryTimeoutSeconds);
                BuildInventoryUi_Robust();
                HookQueryButton();
            }
            catch { /* swallow */ }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                // 初次显示时也拉一次（如果输入有值）
                var input = TryGetInput();
                if (!string.IsNullOrWhiteSpace(input))
                {
                    _ = RefreshInventoryAsync(input, force:false);
                }
            }
            catch { /* no-op */ }
        }

        void HookQueryButton()
        {
            try
            {
                if (_btnQuery != null)
                {
                    _btnQuery.Click -= BtnQuery_InventoryHookAsync;
                    _btnQuery.Click += BtnQuery_InventoryHookAsync;
                }
            }
            catch { /* no-op */ }
        }

        async void BtnQuery_InventoryHookAsync(object sender, EventArgs e)
        {
            var input = TryGetInput();
            if (!string.IsNullOrWhiteSpace(input))
            {
                await RefreshInventoryAsync(input, force:true);
            }
        }

        string TryGetInput()
        {
            try
            {
                if (_boxInput != null) return _boxInput.Text?.Trim() ?? string.Empty;
            }
            catch {}
            return string.Empty;
        }

        // ====== 关键：稳健挂载库存面板，确保可见 ======
        void BuildInventoryUi_Robust()
        {
            // 构建子控件
            _invPanel = new Panel { Height = 300, Padding = new Padding(12), BackColor = Color.White };
            var header = new Panel { Height = 30, Dock = DockStyle.Top, BackColor = Color.FromArgb(255, 248, 220) }; // 浅橙色，醒目
            var title = new Label { Text = "库存（同页签）—— 已启用", Dock = DockStyle.Left, AutoSize = false, Width = 240, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold) };
            header.Controls.Add(title);
            _invPanel.Controls.Add(header);

            _invPrimaryChips = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, AutoScroll = true, BackColor = Color.White };
            _invPanel.Controls.Add(_invPrimaryChips);

            _invSummary = new Label { Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Font = SystemFonts.MessageBoxFont };
            _invPanel.Controls.Add(_invSummary);

            _invMatrix = new InventoryMatrixControl { Dock = DockStyle.Fill };
            _invMatrix.SetThreshold(InventoryLowThreshold);
            _invPanel.Controls.Add(_invMatrix);

            var moreBtn = new Button { Text = "更多仓库…", Dock = DockStyle.Bottom, Height = 28 };
            moreBtn.Click += (s, e) => ShowWarehousePicker();
            _invPanel.Controls.Add(moreBtn);

            // 选择挂载宿主：从 _pvTrend 开始往上找 TabPage/Panel/GroupBox；否则挂到窗体本身
            Control host = this;
            try
            {
                Control p = _pvTrend as Control;
                while (p != null)
                {
                    if (p is TabPage || p is Panel || p is GroupBox)
                    {
                        host = p;
                        break;
                    }
                    p = p.Parent;
                }
            }
            catch { /* fallback to form */ }

            host.Controls.Add(_invPanel);
            _invPanel.Dock = DockStyle.Bottom;
            _invPanel.BringToFront();

            // 调试信息：显示宿主类型，帮助快速定位（你会在黄色标题栏后看到）
            var hostInfo = $"宿主：{host.GetType().Name}｜子控件数：{host.Controls.Count}｜时间：{DateTime.Now:HH:mm:ss}";
            var info = new Label { Text = hostInfo, Dock = DockStyle.Top, Height = 18, ForeColor = Color.DimGray };
            header.Controls.Add(info);
            info.BringToFront();
        }

        async Task RefreshInventoryAsync(string input, bool force = false)
        {
            if (_invClient == null) return;
            if (string.IsNullOrWhiteSpace(input)) return;

            if (!force && _invCache.TryGetValue(input, out var cached))
            {
                if ((DateTime.Now - cached.FetchedAt).TotalSeconds <= InventoryCacheTtlSeconds)
                {
                    await RenderInventoryAsync(cached);
                    return;
                }
            }

            try
            {
                var rows = await _invClient.FetchAsync(input);
                var snap = new InventorySnapshot { Input = input, FetchedAt = DateTime.Now, Rows = rows.ToArray() };
                _invCache[input] = snap;
                await RenderInventoryAsync(snap);
            }
            catch (Exception ex)
            {
                _invSummary.Text = $"库存：拉取失败（{ex.GetType().Name}）。保留上次数据。";
                if (_invCache.TryGetValue(input, out var last)) await RenderInventoryAsync(last);
            }
        }

        Task RenderInventoryAsync(InventorySnapshot snap)
        {
            var rows = snap.Rows;

            _invPrimaryWarehouses = InventoryPivot.Top3PrimaryWarehouses(rows);
            _invPrimaryChips.Controls.Clear();
            int i = 0;
            foreach (var wh in _invPrimaryWarehouses)
            {
                i++;
                var btn = new Button
                {
                    AutoSize = true,
                    Height = 26,
                    Text = $"{i}. {wh}",
                    Margin = new Padding(4, 2, 4, 2)
                };
                btn.Click += (s, e) => { _invCurrentWarehouse = wh; BindMatrix(); UpdateSummary(); };
                _invPrimaryChips.Controls.Add(btn);
            }
            var allBtn = new Button { AutoSize = true, Height = 26, Text = "综合视图（全部仓）", Margin = new Padding(4, 2, 4, 2) };
            allBtn.Click += (s, e) => { _invCurrentWarehouse = "__ALL__"; BindMatrix(); UpdateSummary(); };
            _invPrimaryChips.Controls.Add(allBtn);

            _invCurrentWarehouse = "__ALL__";
            BindMatrix();
            UpdateSummary();

            return Task.CompletedTask;

            void BindMatrix()
            {
                var view = InventoryPivot.FilterByWarehouse(rows, _invCurrentWarehouse);
                _invMatrix.Bind(view);
            }
            void UpdateSummary()
            {
                var view = InventoryPivot.FilterByWarehouse(rows, _invCurrentWarehouse);
                var (sumIn, sumOut, low, abn) = InventoryPivot.Summaries(view, InventoryLowThreshold);
                _invSummary.Text = $"库存 —— 仓库：{(_invCurrentWarehouse == "__ALL__" ? "全部" : _invCurrentWarehouse)}｜在库合计：{sumIn}｜可用合计：{sumOut}｜低库存项：{low}｜异常项：{abn}｜更新：{snap.FetchedAt:HH:mm:ss}";
            }
        }

        void ShowWarehousePicker()
        {
            if (_invCache.TryGetValue(TryGetInput(), out var snap))
            {
                var rows = snap.Rows;
                var all = rows.GroupBy(r => r.Warehouse)
                              .Select(g => new { wh = g.Key, total = g.Sum(x => Math.Max(0, x.QtyOut)) })
                              .OrderByDescending(x => x.total)
                              .Select(x => x.wh)
                              .ToArray();
                using var dlg = new Form { Width = 420, Height = 520, Text = "选择仓库" };
                var list = new ListBox { Dock = DockStyle.Fill };
                list.Items.Add("综合视图（全部仓）");
                foreach (var wh in all) list.Items.Add(wh);
                list.DoubleClick += async (s, e) =>
                {
                    if (list.SelectedIndex == -1) return;
                    _invCurrentWarehouse = list.SelectedIndex == 0 ? "__ALL__" : list.SelectedItem!.ToString()!;
                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                    await RefreshInventoryAsync(TryGetInput(), force: false);
                };
                dlg.Controls.Add(list);
                dlg.ShowDialog(this);
            }
        }
    }

    // ====== 支持类（同文件放置，避免漏文件） ======

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

    public sealed class InventoryClient
    {
        private readonly HttpClient _http;
        private readonly string _apiUrl;
        private readonly int _timeoutSeconds;

        public InventoryClient(HttpClient http, string apiUrl, int timeoutSeconds = 4)
        {
            _http = http;
            _apiUrl = apiUrl.TrimEnd('/');
            _timeoutSeconds = Math.Max(1, timeoutSeconds);
        }

        public async Task<List<InventoryRow>> FetchAsync(string input, CancellationToken ct = default)
        {
            var list = new List<InventoryRow>();
            if (string.IsNullOrWhiteSpace(input)) return list;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

            var url = $"{_apiUrl}?style_name={Uri.EscapeDataString(input)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

            try
            {
                var arr = System.Text.Json.JsonSerializer.Deserialize<string[]>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) 
                          ?? Array.Empty<string>();
                foreach (var line in arr)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var norm = line.Replace('，', ',');
                    var parts = norm.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 6) continue;
                    var style = parts[0];
                    var color = parts[1];
                    var size  = parts[2];
                    var wh    = parts[3];
                    if (!int.TryParse(parts[4]?.Trim(), out var inQty))  continue;
                    if (!int.TryParse(parts[5]?.Trim(), out var outQty)) continue;
                    list.Add(new InventoryRow { Style = style, Color = color, Size = size, Warehouse = wh, QtyIn = inQty, QtyOut = outQty });
                }
            }
            catch
            {
                // ignore unexpected payload
            }
            return list;
        }
    }

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

        public static InventoryRow[] FilterByWarehouse(IEnumerable<InventoryRow> rows, string warehouseOrAll)
        {
            if (warehouseOrAll == "__ALL__") return rows.ToArray();
            return rows.Where(r => r.Warehouse == warehouseOrAll).ToArray();
        }
    }

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

            var table = new System.Data.DataTable();
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

        private void Grid_CellToolTipTextNeeded(object sender, DataGridViewCellToolTipTextNeededEventArgs e)
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

        private void Grid_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
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
