
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
#pragma warning disable 0618
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;

namespace StyleWatcherWin
{
    public class InventoryTabPage : TabPage
    {
        public event Action<int, int, Dictionary<string, int>>? SummaryUpdated;

        private sealed class InvRow { public string Name=""; public string Color=""; public string Size=""; public string Warehouse=""; public int Available; public int OnHand; }
        private sealed class InvSnapshot
        {
            public List<InvRow> Rows { get; } = new();
            public int TotalAvailable => Rows.Sum(r => r.Available);
            public int TotalOnHand => Rows.Sum(r => r.OnHand);
            public IEnumerable<string> ColorsNonZero() => Rows.GroupBy(r => r.Color).Select(g => new { c = g.Key, v = g.Sum(x => x.Available) }).Where(x => !string.IsNullOrWhiteSpace(x.c) && x.v != 0).OrderByDescending(x => x.v).Select(x => x.c);
            public IEnumerable<string> SizesNonZero()  => Rows.GroupBy(r => r.Size ).Select(g => new { s = g.Key, v = g.Sum(x => x.Available) }).Where(x => !string.IsNullOrWhiteSpace(x.s) && x.v != 0).OrderByDescending(x => x.v).Select(x => x.s);
            public Dictionary<string, int> ByWarehouse() => Rows.GroupBy(r => r.Warehouse).ToDictionary(g => g.Key, g => g.Sum(x => x.Available));
        }

        private static readonly HttpClient _http = new();

        private readonly AppConfig _cfg;
        private InvSnapshot _all = new();
        private string _styleName = "";

        private readonly PlotView _pvHeat = new() { Dock = DockStyle.Fill, BackColor = Color.White };
        private readonly PlotView _pvColor = new() { Dock = DockStyle.Fill, BackColor = Color.White };
        private readonly PlotView _pvSize  = new() { Dock = DockStyle.Fill, BackColor = Color.White };
        private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells };
        private readonly TabControl _subTabs = new() { Dock = DockStyle.Fill };

        private (string? color, string? size)? _activeCell = null;
        private Label _lblAvail = new();
        private Label _lblOnHand = new();

        public InventoryTabPage(AppConfig cfg)
        {
            _cfg = cfg;
            Text = "库存";
            BackColor = Theme.Surface;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            Controls.Add(root);

            var tools = BuildTopTools();
            root.Controls.Add(tools, 0, 0);

            var four = BuildFourArea();
            root.Controls.Add(four, 0, 1);

            root.Controls.Add(_subTabs, 0, 2);
        }

        private Control BuildTopTools()
        {
            var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _lblAvail = new Label { AutoSize = true, Font = Theme.FontBase, ForeColor = Theme.Text };
            _lblOnHand = new Label { AutoSize = true, Font = Theme.FontBase, Margin = new Padding(16, 0, 0, 0), ForeColor = Theme.Text };

            p.Controls.Add(_lblAvail, 0, 0);
            p.Controls.Add(_lblOnHand, 1, 0);
            p.Controls.Add(new Panel { Dock = DockStyle.Fill }, 2, 0);

            var btnReload = new Button { Text = "刷新", AutoSize = true };
            Theme.StylePrimaryButton(btnReload);
            btnReload.Click += async (s, e) => await ReloadAsync(_styleName);
            p.Controls.Add(btnReload, 3, 0);
            return p;
        }

        private Control BuildFourArea()
        {
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            PrepareGridColumns(_grid);
            Theme.StyleDataGrid(_grid);

            grid.Controls.Add(_pvSize, 0, 0);
            grid.Controls.Add(_pvColor, 1, 0);
            grid.Controls.Add(_pvHeat, 0, 1);
            grid.Controls.Add(_grid, 1, 1);

            return grid;
        }

        public async Task LoadAsync(string styleName)
        {
            _styleName = styleName ?? "";
            await ReloadAsync(_styleName);
        }
        public Task LoadInventoryAsync(string styleName) => LoadAsync(styleName);

        private async Task ReloadAsync(string styleName)
        {
            _activeCell = null;
            _all = await FetchInventoryAsync(styleName);
            RenderAll(_all);
        }

        private void RenderAll(InvSnapshot snap)
        {
            _lblAvail.Text = $"可用合计：{snap.TotalAvailable}";
            _lblOnHand.Text = $"现有合计：{snap.TotalOnHand}";

            RenderHeatmap(snap, _pvHeat, "颜色×尺码 可用数热力图");
            RenderBarsByKey(snap, _pvColor, g => g.Color, "颜色可用（降序）");
            RenderBarsByKey(snap, _pvSize,  g => g.Size,  "尺码可用（降序）");
            RenderWarehouseTabs(snap);

            try { SummaryUpdated?.Invoke(snap.TotalAvailable, snap.TotalOnHand, snap.ByWarehouse()); } catch { }
            BindGrid(_grid, snap.Rows);
        }

        private async Task<InvSnapshot> FetchInventoryAsync(string styleName)
        {
            var s = new InvSnapshot();
            if (string.IsNullOrWhiteSpace(styleName)) return s;

            try
            {
                var baseUrl = (_cfg?.inventory?.url_base ?? "");
                var url = baseUrl.Contains("style_name=") ? baseUrl + Uri.EscapeDataString(styleName)
                    : baseUrl.TrimEnd('/') + "?style_name=" + Uri.EscapeDataString(styleName);

                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                var resp = await _http.SendAsync(req);
                var raw = await resp.Content.ReadAsStringAsync();

                List<string>? lines = null;
                try { lines = JsonSerializer.Deserialize<List<string>>(raw); } catch { }
                if (lines == null) lines = raw.Replace("\r\n", "\n").Split('\n').ToList();

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var seg = line.Replace('，', ',').Split(',');
                    if (seg.Length < 6) continue;
                    s.Rows.Add(new InvRow
                    {
                        Name = seg[0].Trim(),
                        Color = seg[1].Trim(),
                        Size = seg[2].Trim(),
                        Warehouse = seg[3].Trim(),
                        Available = int.TryParse(seg[4].Trim(), out var a) ? a : 0,
                        OnHand = int.TryParse(seg[5].Trim(), out var h) ? h : 0
                    });
                }
            }
            catch { }
            return s;
        }

        private void RenderBarsByKey(InvSnapshot snap, PlotView pv, Func<InvRow, string> keySelector, string title)
        {
            var model = new PlotModel { Title = title };
            var data = snap.Rows.GroupBy(keySelector).Select(g => new { Key = g.Key, V = g.Sum(x => x.Available) })
                                .OrderByDescending(x => x.V).ToList();

            var cat = new CategoryAxis { Position = AxisPosition.Left, StartPosition = 1, EndPosition = 0 };
            foreach (var d in data) cat.Labels.Add(d.Key);

            var val = new LinearAxis { Position = AxisPosition.Bottom, MinorGridlineStyle = LineStyle.Dot, MajorGridlineStyle = LineStyle.Solid };
            var series = new BarSeries { LabelFormatString = "{0}", LabelPlacement = LabelPlacement.Inside, LabelMargin = 6 };
            foreach (var d in data) series.Items.Add(new BarItem(d.V));

            model.Axes.Add(cat); model.Axes.Add(val); model.Series.Add(series);
            pv.Model = model;

            if (data.Count > 0)
            {
                cat.Minimum = -0.5;
                cat.Maximum = Math.Min(9.5, data.Count - 0.5);
            }
        }

        private void RenderHeatmap(InvSnapshot snap, PlotView pv, string title)
        {
            var colors = snap.ColorsNonZero().ToList();
            var sizes = snap.SizesNonZero().ToList();
            var ci = colors.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i);
            var si = sizes.Select((s, i) => (s, i)).ToDictionary(x => x.s, x => x.i);

            var data = new double[colors.Count, sizes.Count];
            foreach (var g in snap.Rows.GroupBy(r => new { r.Color, r.Size }))
            {
                if (!ci.ContainsKey(g.Key.Color) || !si.ContainsKey(g.Key.Size)) continue;
                data[ci[g.Key.Color], si[g.Key.Size]] = g.Sum(x => x.Available);
            }

            var model = new PlotModel { Title = title };
            var vals = new List<double>();
            foreach (var v in data) if (v > 0) vals.Add(v);
            vals.Sort();
            var minPos = vals.Count > 0 ? vals.First() : 1.0;
            var p95 = vals.Count > 0 ? Percentile(vals, 0.95) : 1.0;
            if (p95 <= 0) p95 = minPos;

            var palette = OxyPalette.Interpolate(256,
                OxyColor.FromRgb(229, 245, 224),
                OxyColor.FromRgb(161, 217, 155),
                OxyColor.FromRgb(255, 224, 102),
                OxyColor.FromRgb(253, 174, 97),
                OxyColor.FromRgb(244, 109, 67),
                OxyColor.FromRgb(215, 48, 39)
            );
            var caxis = new LinearColorAxis
            {
                Position = AxisPosition.Right,
                Palette = palette,
                Minimum = minPos,
                Maximum = p95,
                LowColor = OxyColor.FromRgb(242, 242, 242),
                HighColor = OxyColor.FromRgb(153, 0, 0)
            };
            model.Axes.Add(caxis);

            var axX = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Minimum = -0.5, Maximum = Math.Max(colors.Count - 0.5, 0.5),
                MajorStep = 1, MinorStep = 1,
                IsZoomEnabled = true, IsPanEnabled = true,
                LabelFormatter = d => { var k = (int)Math.Round(d); return (k >= 0 && k < colors.Count) ? colors[k] : ""; }
            };
            var axY = new LinearAxis
            {
                Position = AxisPosition.Left,
                Minimum = -0.5, Maximum = Math.Max(sizes.Count - 0.5, 0.5),
                MajorStep = 1, MinorStep = 1,
                IsZoomEnabled = true, IsPanEnabled = true,
                LabelFormatter = d => { var k = (int)Math.Round(d); return (k >= 0 && k < sizes.Count) ? sizes[k] : ""; }
            };
            model.Axes.Add(axX); model.Axes.Add(axY);

            var hm = new HeatMapSeries
            {
                X0 = -0.5, X1 = colors.Count - 0.5,
                Y0 = -0.5, Y1 = sizes.Count - 0.5,
                Interpolate = false, RenderMethod = HeatMapRenderMethod.Rectangles,
                Data = data
            };
            model.Series.Add(hm);
            pv.Model = model;
        }

        private static double Percentile(List<double> sorted, double p)
        {
            if (sorted.Count == 0) return 0;
            if (p <= 0) return sorted.First();
            if (p >= 1) return sorted.Last();
            var idx = (sorted.Count - 1) * p;
            var lo = (int)Math.Floor(idx);
            var hi = (int)Math.Ceiling(idx);
            if (lo == hi) return sorted[lo];
            var frac = idx - lo;
            return sorted[lo] * (1 - frac) + sorted[hi] * frac;
        }

        private void RenderWarehouseTabs(InvSnapshot snap)
        {
            _subTabs.SuspendLayout();
            _subTabs.TabPages.Clear();

            foreach (var g in snap.Rows.GroupBy(r => r.Warehouse).OrderByDescending(x => x.Sum(z => z.Available)))
            {
                var tp = new TabPage($"{g.Key}（{g.Sum(z => z.Available)}）") { BackColor = Theme.Surface };
                var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells };
                Theme.StyleDataGrid(grid);
                PrepareGridColumns(grid);
                BindGrid(grid, g.ToList());
                tp.Controls.Add(grid);
                _subTabs.TabPages.Add(tp);
            }

            _subTabs.ResumeLayout();
        }

        private static void PrepareGridColumns(DataGridView grid)
        {
            grid.Columns.Clear();
            grid.RowHeadersVisible = false;
            grid.Columns.Add("Name", "款式"); grid.Columns.Add("Color", "颜色"); grid.Columns.Add("Size", "尺码"); grid.Columns.Add("Warehouse", "仓库"); grid.Columns.Add("Available", "可用"); grid.Columns.Add("OnHand", "现有");
        }

        private static void BindGrid(DataGridView grid, IEnumerable<InvRow> rows)
        {
            grid.Rows.Clear();
            foreach (var r in rows)
                grid.Rows.Add(r.Name, r.Color, r.Size, r.Warehouse, r.Available, r.OnHand);
        }

        // Public helpers used by ResultForm for chips
        public IEnumerable<string> OfferedSizes()
        {
            return _all.Rows.Select(r => r.Size).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s);
        }
        public IEnumerable<string> CurrentZeroSizes()
        {
            var agg = _all.Rows.GroupBy(r => r.Size).ToDictionary(g => g.Key, g => g.Sum(x => x.Available));
            foreach (var kv in agg) if (kv.Value == 0) yield return kv.Key;
        }
    }
}
