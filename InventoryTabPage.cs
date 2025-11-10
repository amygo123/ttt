using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;

namespace StyleWatcherWin
{
    public class InventoryTabPage : TabPage
    {
        private readonly AppConfig _cfg;
        private readonly DataGridView _grid;
        private readonly PlotView _pvColor;
        private readonly PlotView _pvSize;

        public InventoryTabPage(AppConfig cfg)
        {
            _cfg = cfg ?? AppConfig.Load();
            Text = "库存";

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells
            };

            _pvColor = new PlotView { Dock = DockStyle.Fill };
            _pvSize = new PlotView { Dock = DockStyle.Fill };

            root.Controls.Add(_grid, 0, 0);
            root.Controls.Add(_pvColor, 0, 1);
            root.Controls.Add(_pvSize, 0, 2);

            Controls.Add(root);
        }

        public Task LoadInventoryAsync(string styleName)
        {
            return ReloadAsync(styleName);
        }

        private async Task ReloadAsync(string styleName)
        {
            if (string.IsNullOrWhiteSpace(styleName))
            {
                _grid.DataSource = null;
                _pvColor.Model = null;
                _pvSize.Model = null;
                return;
            }

            try
            {
                var json = await FetchInventoryAsync(styleName);
                var rows = ParseInventory(json);

                _grid.DataSource = rows;

                RenderBarsByColor(rows);
                RenderBarsBySize(rows);
            }
            catch
            {
                // 显示层面不抛异常，保持 UI 稳定
            }
        }

        private async Task<string> FetchInventoryAsync(string styleName)
        {
            var baseUrl = _cfg.inventory?.url_base;
            if (string.IsNullOrWhiteSpace(baseUrl))
                return "[]";

            string url;
            if (baseUrl.Contains("style_name="))
                url = baseUrl + Uri.EscapeDataString(styleName);
            else
            {
                var connector = baseUrl.Contains("?") ? "&" : "?";
                url = baseUrl + connector + "style_name=" + Uri.EscapeDataString(styleName);
            }

            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(1, _cfg.timeout_seconds))
            };

            try
            {
                var resp = await http.GetAsync(url);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsStringAsync();
            }
            catch
            {
                return "[]";
            }
        }

        private class InvRow
        {
            public string Name { get; set; }
            public string Color { get; set; }
            public string Size { get; set; }
            public string Warehouse { get; set; }
            public int Available { get; set; }
            public int OnHand { get; set; }
        }

        private List<InvRow> ParseInventory(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<InvRow>();

            try
            {
                if (json.TrimStart().StartsWith("["))
                {
                    var arr = JsonSerializer.Deserialize<List<InvRow>>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return arr ?? new List<InvRow>();
                }

                // 简单按行解析：Name,Color,Size,Warehouse,Available,OnHand
                var list = new List<InvRow>();
                var lines = json.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split(',');
                    if (parts.Length < 5) continue;
                    int.TryParse(parts[4], out var ava);
                    int onHand = 0;
                    if (parts.Length > 5)
                        int.TryParse(parts[5], out onHand);

                    list.Add(new InvRow
                    {
                        Name = parts[0],
                        Color = parts[1],
                        Size = parts[2],
                        Warehouse = parts[3],
                        Available = ava,
                        OnHand = onHand
                    });
                }

                return list;
            }
            catch
            {
                return new List<InvRow>();
            }
        }

        private void RenderBarsByColor(List<InvRow> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                _pvColor.Model = null;
                return;
            }

            var data = rows
                .GroupBy(r => r.Color ?? string.Empty)
                .Select(g => new { Key = g.Key, V = g.Sum(x => x.Available) })
                .Where(x => x.V > 0 && !string.IsNullOrWhiteSpace(x.Key))
                .OrderByDescending(x => x.V)
                .ToList();

            var model = new PlotModel { Title = "按颜色可用库存" };

            var cat = new CategoryAxis
            {
                Position = AxisPosition.Left,
                StartPosition = 1,
                EndPosition = 0,
                IsZoomEnabled = true,
                IsPanEnabled = true
            };
            foreach (var d in data)
            {
                cat.Labels.Add(d.Key);
            }

            var val = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Minimum = 0,
                MaximumPadding = 0.2,
                IsZoomEnabled = true,
                IsPanEnabled = true
            };

            var series = new BarSeries
            {
                LabelFormatString = "{0}",
                LabelPlacement = LabelPlacement.Outside,
                LabelMargin = 2
            };

            foreach (var d in data)
            {
                series.Items.Add(new BarItem(d.V));
            }

            model.Axes.Add(cat);
            model.Axes.Add(val);
            model.Series.Add(series);

            _pvColor.Model = model;
        }

        private void RenderBarsBySize(List<InvRow> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                _pvSize.Model = null;
                return;
            }

            var data = rows
                .GroupBy(r => r.Size ?? string.Empty)
                .Select(g => new { Key = g.Key, V = g.Sum(x => x.Available) })
                .Where(x => x.V > 0 && !string.IsNullOrWhiteSpace(x.Key))
                .OrderByDescending(x => x.V)
                .ToList();

            var model = new PlotModel { Title = "按尺码可用库存" };

            var cat = new CategoryAxis
            {
                Position = AxisPosition.Left,
                StartPosition = 1,
                EndPosition = 0,
                IsZoomEnabled = true,
                IsPanEnabled = true
            };
            foreach (var d in data)
            {
                cat.Labels.Add(d.Key);
            }

            var val = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Minimum = 0,
                MaximumPadding = 0.2,
                IsZoomEnabled = true,
                IsPanEnabled = true
            };

            var series = new BarSeries
            {
                LabelFormatString = "{0}",
                LabelPlacement = LabelPlacement.Outside,
                LabelMargin = 2
            };

            foreach (var d in data)
            {
                series.Items.Add(new BarItem(d.V));
            }

            model.Axes.Add(cat);
            model.Axes.Add(val);
            model.Series.Add(series);

            _pvSize.Model = model;
        }
    }
}
