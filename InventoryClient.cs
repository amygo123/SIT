using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StyleWatcherWin
{
    public sealed class InventoryClient
    {
        private readonly HttpClient _http;
        private readonly string _apiUrl;
        private readonly int _timeoutSeconds;

        public InventoryClient(string apiUrl, int? timeoutSeconds = 4)
        {
            _http = new HttpClient();
            _apiUrl = apiUrl.TrimEnd('/');
            _timeoutSeconds = Math.Max(1, timeoutSeconds ?? 4);
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
                var arr = JsonSerializer.Deserialize<string[]>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) 
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
                // 非期待格式：忽略
            }
            return list;
        }
    }
}
