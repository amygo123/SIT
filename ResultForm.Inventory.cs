
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StyleWatcherWin
{
    // ZERO-TOUCH add-on: this partial extends your existing ResultForm
    public partial class ResultForm
    {
        // --- inventory fields ---
        readonly HttpClient _invHttp = new HttpClient();
        InventoryClient? _invClient;
        readonly Dictionary<string, InventorySnapshot> _invCache = new();
        Panel? _invPanel;
        FlowLayoutPanel? _invPrimaryChips;
        Label? _invSummary;
        InventoryMatrixControl? _invMatrix;
        string[] _invPrimaryWarehouses = Array.Empty<string>();
        string _invCurrentWarehouse = "__ALL__";

        // Config (no change to your Config.cs needed; you can adjust here if desired)
        const string InventoryApiUrl = "http://192.168.40.97:8000/inventory";
        const int InventoryTimeoutSeconds = 4;
        const int InventoryLowThreshold = 10;
        const int InventoryCacheTtlSeconds = 300;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                // Initialize once when handle is ready
                if (_invClient == null)
                {
                    _invClient = new InventoryClient(_invHttp, InventoryApiUrl, InventoryTimeoutSeconds);
                    BuildInventoryUi();

                    // Hook existing query button: after your main query finishes, also refresh inventory.
                    if (_btnQuery != null)
                    {
                        _btnQuery.Click -= BtnQuery_InventoryHookAsync; // avoid duplicate
                        _btnQuery.Click += BtnQuery_InventoryHookAsync;
                    }
                }
            }
            catch { /* swallow to avoid breaking main UI */ }
        }

        private async void BtnQuery_InventoryHookAsync(object? sender, EventArgs e)
        {
            var input = _boxInput?.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(input))
            {
                await RefreshInventoryAsync(input, force:true);
            }
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                var input = _boxInput?.Text?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(input))
                    await RefreshInventoryAsync(input, force:false);
            }
            catch { /* no-op */ }
        }

        void BuildInventoryUi()
        {
            // Bottom docked inventory panel (same tab)
            _invPanel = new Panel { Dock = DockStyle.Bottom, Height = 280, Padding = new Padding(12), BackColor = Color.White };
            Controls.Add(_invPanel);

            _invPrimaryChips = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, AutoScroll = true };
            _invPanel.Controls.Add(_invPrimaryChips);

            _invSummary = new Label { Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Font = UIStyle.Body };
            _invPanel.Controls.Add(_invSummary);

            _invMatrix = new InventoryMatrixControl { Dock = DockStyle.Fill };
            _invMatrix.SetThreshold(InventoryLowThreshold);
            _invPanel.Controls.Add(_invMatrix);

            var moreBtn = new Button { Text = "更多仓库…", Dock = DockStyle.Bottom, Height = 28 };
            moreBtn.Click += (s, e) => ShowWarehousePicker();
            _invPanel.Controls.Add(moreBtn);
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
                if (_invSummary != null) _invSummary.Text = $"库存：拉取失败（{ex.GetType().Name}）。保留上次数据。";
                if (_invCache.TryGetValue(input, out var last)) await RenderInventoryAsync(last);
            }
        }

        Task RenderInventoryAsync(InventorySnapshot snap)
        {
            var rows = snap.Rows;

            _invPrimaryWarehouses = InventoryPivot.Top3PrimaryWarehouses(rows);
            _invPrimaryChips!.Controls.Clear();
            int i = 0;
            foreach (var wh in _invPrimaryWarehouses)
            {
                i++;
                var btn = new Button { AutoSize = true, Height = 28, Text = $"{i}. {wh}", Margin = new Padding(4) };
                btn.Click += (s, e) => { _invCurrentWarehouse = wh; BindMatrix(); UpdateSummary(); };
                _invPrimaryChips.Controls.Add(btn);
            }
            var allBtn = new Button { AutoSize = true, Height = 28, Text = "综合视图（全部仓）", Margin = new Padding(4) };
            allBtn.Click += (s, e) => { _invCurrentWarehouse = "__ALL__"; BindMatrix(); UpdateSummary(); };
            _invPrimaryChips.Controls.Add(allBtn);

            _invCurrentWarehouse = "__ALL__";
            BindMatrix();
            UpdateSummary();

            return Task.CompletedTask;

            void BindMatrix()
            {
                var view = InventoryPivot.FilterByWarehouse(rows, _invCurrentWarehouse);
                _invMatrix!.Bind(view);
            }
            void UpdateSummary()
            {
                var view = InventoryPivot.FilterByWarehouse(rows, _invCurrentWarehouse);
                var (sumIn, sumOut, low, abn) = InventoryPivot.Summaries(view, InventoryLowThreshold);
                _invSummary!.Text = $"库存 —— 仓库：{(_invCurrentWarehouse == "__ALL__" ? "全部" : _invCurrentWarehouse)}｜在库合计：{sumIn}｜可用合计：{sumOut}｜低库存项：{low}｜异常项：{abn}｜最近更新：{snap.FetchedAt:HH:mm:ss}";
            }
        }

        void ShowWarehousePicker()
        {
            if (_invCache.TryGetValue(_boxInput?.Text?.Trim() ?? "", out var snap))
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
                    await RefreshInventoryAsync(_boxInput?.Text?.Trim() ?? "", force: false);
                };
                dlg.Controls.Add(list);
                dlg.ShowDialog(this);
            }
        }
    }
}
