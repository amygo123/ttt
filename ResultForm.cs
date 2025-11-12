
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;

namespace StyleWatcherWin
{
    public class ResultForm : Form
    {
        // Mapping pie slice -> warehouse
        private readonly Dictionary<OxyPlot.Series.PieSlice, string> _warehouseSliceMap = new();

        private readonly AppConfig _cfg;

        // Header
        private readonly TextBox _input = new();
        private readonly Button _btnQuery = new();
        private readonly Button _btnExport = new();

        // KPI
        private readonly FlowLayoutPanel _kpi = new();
        private readonly Panel _kpiSales7 = new();
        private readonly Panel _kpiInv = new();
        private readonly Panel _kpiDoc = new();
        private readonly Panel _kpiMissing = new();
        private readonly Panel _kpiGrade = new();
        private readonly Panel _kpiMinPrice = new();
        private readonly Panel _kpiBreakeven = new();
        private FlowLayoutPanel? _kpiMissingFlow;

        // Tabs
        private readonly TabControl _tabs = new();

        // Overview
        private readonly FlowLayoutPanel _trendSwitch = new();
        private int _trendWindow = 7;
        private readonly PlotView _plotTrend = new();
        private readonly PlotView _plotSize = new();
        private readonly PlotView _plotColor = new();
        private readonly PlotView _plotWarehouse = new();

        // Status
        private readonly Label _status = new();

        // Detail
        private readonly DataGridView _grid = new();
        private readonly BindingSource _binding = new();
        private readonly TextBox _boxSearch = new();
        private readonly FlowLayoutPanel _filterChips = new();
        private readonly Timer _searchDebounce = new() { Interval = 220 };

        // Inventory page
        private InventoryTabPage? _invPage;

        // Caches
        private string _lastDisplayText = string.Empty;
        private List<Aggregations.SalesItem> _sales = new();
        private List<object> _gridMaster = new();

        // cached inventory totals from event
        private int _invAvailTotal = 0;
        private int _invOnHandTotal = 0;
        private Dictionary<string,int> _invWarehouse = new();

        public ResultForm(AppConfig cfg)
        {
            _cfg = cfg;
            Text = "随手查";
            Font = Theme.FontBase;
            Width = Math.Max(1600, _cfg.window.width);
            Height = Math.max(900, _cfg.window.height);
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = _cfg.window.alwaysOnTop;
            BackColor = Theme.Surface;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Hide(); };

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            var header = BuildHeader();
            root.Controls.Add(header, 0, 0);

            var content = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(content, 0, 1);

            // KPI bar
            _kpi.Dock = DockStyle.Fill;
            _kpi.FlowDirection = FlowDirection.LeftToRight;
            _kpi.WrapContents = true;
            _kpi.Padding = new Padding(12, 8, 12, 8);
            _kpi.Controls.Add(MakeKpi(_kpiSales7, "近7日销量", "—"));
            _kpi.Controls.Add(MakeKpi(_kpiInv, "可用库存总量", "—"));
            _kpi.Controls.Add(MakeKpi(_kpiDoc, "库存天数", "—"));
            _kpi.Controls.Add(MakeKpi(_kpiGrade, "定级", "—"));
            _kpi.Controls.Add(MakeKpi(_kpiMinPrice, "最低价", "—"));
            _kpi.Controls.Add(MakeKpi(_kpiBreakeven, "保本价", "—"));
            _kpi.Controls.Add(MakeKpiMissing(_kpiMissing, "缺货尺码"));
            content.Controls.Add(_kpi, 0, 0);

            // Tabs
            _tabs.Dock = DockStyle.Fill;
            BuildTabs();
            content.Controls.Add(_tabs, 0, 1);

            _searchDebounce.Tick += (s, e) => { _searchDebounce.Stop(); ApplyFilter(_boxSearch.Text); };
        }

        private Control BuildHeader()
        {
            var head = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new(12, 10, 12, 6),
                BackColor = Theme.Header
            };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _input.MinimumSize = new Size(440, 32);
            _input.Height = 30;
            _input.Font = Theme.FontBase;

            _btnQuery.Text = "查询";
            Theme.StylePrimaryButton(_btnQuery);
            _btnQuery.Click += async (s, e) =>
            {
                var txt = _input.Text ?? string.Empty;
                txt = txt.Trim();

                _btnQuery.Enabled = false;
                try
                {
                    if (string.IsNullOrEmpty(txt))
                    {
                        SetLoading("未检测到输入内容，请先在上方输入内容后再点击“查询”。");
                        return;
                    }

                    SetLoading("查询中...");
                    var raw = await ApiHelper.QueryAsync(_cfg, txt);
                    var result = Formatter.Prettify(raw);
                    ApplyRawText(txt, result);
                }
                catch (Exception ex)
                {
                    SetLoading($"错误：{ex.Message}");
                }
                finally
                {
                    _btnQuery.Enabled = true;
                }
            };

            _btnExport.Text = "导出Excel";
            Theme.StylePrimaryButton(_btnExport);
            _btnExport.Click += (s, e) => ExportExcel();

            head.Controls.Add(_input, 0, 0);
            head.Controls.Add(_btnQuery, 1, 0);
            head.Controls.Add(_btnExport, 2, 0);
            return head;
        }

        private Control MakeKpi(Panel host, string title, string value)
        {
            host.Width = 260; host.Height = 110;
            Theme.StyleCard(host);

            var inner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var t = new Label { Text = title, Dock = DockStyle.Fill, Height = 26, Font = Theme.FontBase, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.SubtleText };
            var v = new Label { Text = value, Dock = DockStyle.Fill, Font = Theme.FontKpi, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 2, 0, 0), ForeColor = Theme.Text };
            v.Name = "ValueLabel";

            inner.Controls.Add(t, 0, 0);
            inner.Controls.Add(v, 0, 1);
            host.Controls.Clear();
            host.Controls.Add(inner);
            return host;
        }

        private Control MakeKpiMissing(Panel host, string title)
        {
            host.Width = 260; host.Height = 110;
            Theme.StyleCard(host);

            var inner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var t = new Label { Text = title, Dock = DockStyle.Fill, Height = 26, Font = Theme.FontBase, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.SubtleText };
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = true,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            _kpiMissingFlow = flow;

            inner.Controls.Add(t, 0, 0);
            inner.Controls.Add(flow, 0, 1);
            host.Controls.Clear();
            host.Controls.Add(inner);
            return host;
        }

        private static Label? ValueLabelOf(Panel p)
        {
            var table = p.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
            if (table != null)
            {
                var labels = table.Controls.OfType<Label>().ToList();
                if (labels.Count > 0) return labels.Last();
            }
            return null;
        }

        private void SetKpiValue(Panel p, string value, Color? color = null)
        {
            var lbl = ValueLabelOf(p);
            if (lbl == null) return;
            lbl.Text = value ?? "—";
            lbl.ForeColor = color ?? Theme.Text;
        }

        // Public API used by TrayApp
        public void FocusInput() { try { if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; _input.Focus(); _input.SelectAll(); } catch { } }
        public void ShowNoActivateAtCursor() { try { StartPosition = FormStartPosition.Manual; var pt = Cursor.Position; Location = new Point(Math.Max(0, pt.X - Width / 2), Math.Max(0, pt.Y - Height / 2)); Show(); } catch { Show(); } }
        public void ShowAndFocusCentered() { ShowAndFocusCentered(_cfg.window.alwaysOnTop); }
        public void ShowAndFocusCentered(bool alwaysOnTop) { TopMost = alwaysOnTop; StartPosition = FormStartPosition.CenterScreen; Show(); Activate(); FocusInput(); }

        public void SetLoading(string message)
        {
            _status.Text = message ?? string.Empty;
            SetKpiValue(_kpiSales7, "—");
            SetKpiValue(_kpiInv, "—");
            SetKpiValue(_kpiDoc, "—");
            SetKpiValue(_kpiGrade, "—");
            SetKpiValue(_kpiMinPrice, "—");
            SetKpiValue(_kpiBreakeven, "—");
            SetMissingSizes(Array.Empty<string>());
        }

        public async void ApplyRawText(string selection, string parsed) { _input.Text = selection ?? string.Empty; _lastDisplayText = parsed ?? string.Empty; await LoadTextAsync(parsed ?? string.Empty); }
        public void ApplyRawText(string text) { _input.Text = text ?? string.Empty; }

        public async Task LoadTextAsync(string raw) => await ReloadAsync(raw);
        private async Task ReloadAsync() => await ReloadAsync(_input.Text);

        private async Task ReloadAsync(string displayText)
        {
            await Task.Yield();
            if (string.IsNullOrWhiteSpace(displayText))
                displayText = _lastDisplayText;

            var parsed = Parser.Parse(displayText ?? string.Empty);

            var newGrid = parsed.Records
                .OrderBy(r => r.Name).ThenBy(r => r.Color).ThenBy(r => r.Size).ThenByDescending(r => r.Date)
                .Select(r => (object)new { 日期 = r.Date.ToString("yyyy-MM-dd"), 款式 = r.Name, 颜色 = r.Color, 尺码 = r.Size, 数量 = r.Qty }).ToList();

            var newSales = parsed.Records.Select(r => new Aggregations.SalesItem
            {
                Date = r.Date, Size = r.Size ?? "", Color = r.Color ?? "", Qty = r.Qty
            }).ToList();

            _lastDisplayText = displayText ?? string.Empty;
            _sales = newSales;
            _gridMaster = newGrid;

            // KPI: fixed 7-day
            var sales7 = _sales.Where(x => x.Date >= DateTime.Today.AddDays(-6)).Sum(x => x.Qty);
            SetKpiValue(_kpiSales7, sales7.ToString());

            // Missing sizes (based on inventory signals)
            SetMissingSizes(MissingSizes(_sales.Select(s => s.Size), _invPage?.OfferedSizes() ?? Enumerable.Empty<string>(), _invPage?.CurrentZeroSizes() ?? Enumerable.Empty<string>()));

            RenderCharts(_sales);

            _binding.DataSource = new BindingList<object>(_gridMaster);
            _grid.ClearSelection();
            if (_grid.Columns.Contains("款式")) _grid.Columns["款式"].DisplayIndex = 0;
            if (_grid.Columns.Contains("颜色")) _grid.Columns["颜色"].DisplayIndex = 1;
            if (_grid.Columns.Contains("尺码")) _grid.Columns["尺码"].DisplayIndex = 2;
            if (_grid.Columns.Contains("日期")) _grid.Columns["日期"].DisplayIndex = 3;
            if (_grid.Columns.Contains("数量")) _grid.Columns["数量"].DisplayIndex = 4;

            // Guess style name and trigger inventory/price loads
            var styleName = parsed.Records
                .Select(r => r.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .GroupBy(n => n)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()
                ?.Key;

            if (!string.IsNullOrWhiteSpace(styleName))
            {
                try { _ = _invPage?.LoadInventoryAsync(styleName); } catch { }
                try { _ = LoadPriceAsync(styleName); } catch { }
            }
        }

        private void BuildTabs()
        {
            // Overview
            var overview = new TabPage("概览") { BackColor = Theme.Surface };

            var container = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            container.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // Top tools
            var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, AutoScroll = true, Padding = new Padding(12, 10, 12, 0) };
            _trendSwitch.FlowDirection = FlowDirection.LeftToRight;
            _trendSwitch.WrapContents = false;
            _trendSwitch.AutoSize = true;
            _trendSwitch.Padding = new Padding(0, 0, 12, 0);

            var wins = (_cfg.ui?.trendWindows ?? new int[] { 7, 14, 30 })
                        .Where(x => x > 0 && x <= 90).Distinct().OrderBy(x => x).ToArray();
            if (!wins.Contains(_trendWindow)) _trendWindow = wins.FirstOrDefault(7);

            foreach (var w in wins)
            {
                var rb = new RadioButton { Text = $"{w} 日", AutoSize = true, Tag = w, Margin = new Padding(0, 2, 18, 0) };
                if (w == _trendWindow) rb.Checked = true;
                rb.CheckedChanged += (s, e) => { var rbCtrl = s as RadioButton; if (rbCtrl == null || !rbCtrl.Checked) return; if (rbCtrl.Tag is int w2) { _trendWindow = w2; if (_sales != null && _sales.Count > 0) RenderCharts(_sales); } };
                tools.Controls.Add(rb);
            }
            container.Controls.Add(tools, 0, 0);

            // Main grid of plots
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 2, Padding = new Padding(12) };
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            _plotTrend.Dock = DockStyle.Fill;
            _plotWarehouse.Dock = DockStyle.Fill;
            _plotSize.Dock = DockStyle.Fill;
            _plotColor.Dock = DockStyle.Fill;

            grid.Controls.Add(_plotTrend, 0, 0);
            grid.Controls.Add(_plotWarehouse, 1, 0);
            grid.Controls.Add(_plotSize, 0, 1);
            grid.Controls.Add(_plotColor, 1, 1);

            container.Controls.Add(grid, 0, 1);
            overview.Controls.Add(container);
            _tabs.TabPages.Add(overview);

            // Detail tab
            var detail = new TabPage("销售明细") { BackColor = Theme.Surface };
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(12) };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _boxSearch.Dock = DockStyle.Fill; _boxSearch.PlaceholderText = "搜索（日期/款式/尺码/颜色/数量）";
            _boxSearch.Font = Theme.FontBase;
            _boxSearch.TextChanged += (s, e) => { _searchDebounce.Stop(); _searchDebounce.Start(); };
            panel.Controls.Add(_boxSearch, 0, 0);

            _filterChips.Dock = DockStyle.Fill; _filterChips.FlowDirection = FlowDirection.LeftToRight;
            panel.Controls.Add(_filterChips, 0, 1);

            _grid.Dock = DockStyle.Fill; _grid.ReadOnly = true; _grid.AllowUserToAddRows = false; _grid.AllowUserToDeleteRows = false;
            _grid.RowHeadersVisible = false; _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
            Theme.StyleDataGrid(_grid);
            _grid.DataSource = _binding;
            panel.Controls.Add(_grid, 0, 2);
            detail.Controls.Add(panel);
            _tabs.TabPages.Add(detail);

            // Inventory tab
            _invPage = new InventoryTabPage(_cfg);
            _invPage.SummaryUpdated += OnInventorySummary;
            _tabs.TabPages.Add(_invPage);
        }

        private static IEnumerable<string> MissingSizes(
            IEnumerable<string> _sizesFromSales,
            IEnumerable<string> sizesOfferedFromInv,
            IEnumerable<string> sizesZeroFromInv)
        {
            if (sizesOfferedFromInv == null || sizesZeroFromInv == null)
                yield break;

            var offered = new HashSet<string>(sizesOfferedFromInv.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
            var zeros = new HashSet<string>(sizesZeroFromInv.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);

            foreach (var s in offered)
                if (zeros.Contains(s))
                    yield return s;
        }

        private void SetMissingSizes(IEnumerable<string> sizes)
        {
            if (_kpiMissingFlow == null) return;
            _kpiMissingFlow.SuspendLayout();
            _kpiMissingFlow.Controls.Clear();

            foreach (var s in sizes)
            {
                var chip = new Label
                {
                    AutoSize = true,
                    Text = s,
                    Font = Theme.FontXs,
                    BackColor = Theme.ChipBack,
                    ForeColor = Theme.Text,
                    Padding = new Padding(6, 2, 6, 2),
                    Margin = new Padding(4, 2, 0, 2),
                    BorderStyle = BorderStyle.FixedSingle
                };
                _kpiMissingFlow.Controls.Add(chip);
            }

            if (_kpiMissingFlow.Controls.Count == 0)
            {
                var none = new Label
                {
                    AutoSize = true,
                    Text = "无",
                    Font = Theme.FontSm,
                    ForeColor = Theme.Text
                };
                _kpiMissingFlow.Controls.Add(none);
            }

            _kpiMissingFlow.ResumeLayout();
        }

        private void OnInventorySummary(int totalAvail, int totalOnHand, Dictionary<string,int> warehouseAgg)
        {
            _invAvailTotal = totalAvail;
            _invOnHandTotal = totalOnHand;
            _invWarehouse = warehouseAgg ?? new Dictionary<string,int>();

            SetKpiValue(_kpiInv, totalAvail.ToString());

            var baseDays = Math.Max(1, _cfg.inventoryAlert?.minSalesWindowDays ?? 7);
            var lastN = _sales.Where(x => x.Date >= DateTime.Today.AddDays(-(baseDays - 1))).Sum(x => x.Qty);
            var avg = lastN / (double)baseDays;

            string daysText = "—";
            Color? daysColor = null;
            if (avg > 0)
            {
                var d = Math.Round(totalAvail / avg, 1);
                daysText = d.ToString("0.0");

                var red = _cfg.inventoryAlert?.docRed ?? 3;
                var yellow = _cfg.inventoryAlert?.docYellow ?? 7;
                daysColor = d < red ? Theme.Danger : (d < yellow ? Theme.Caution : Theme.Good);
            }
            SetKpiValue(_kpiDoc, daysText, daysColor);

            RenderWarehousePieOverview(_invWarehouse);
        }

        private void RenderWarehousePieOverview(Dictionary<string,int> warehouseAgg)
        {
            _warehouseSliceMap.Clear();
            var model = new PlotModel { Title = "分仓库存占比" };
            var pie = new PieSeries { AngleSpan = 360, StartAngle = 0, StrokeThickness = 0.5, InsideLabelPosition = 0.6, InsideLabelFormat = "{1:0}%" };

            var list = warehouseAgg.Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && kv.Value > 0)
                                   .OrderByDescending(kv => kv.Value).ToList();
            var total = list.Sum(x => (double)x.Value);

            if (total <= 0) pie.Slices.Add(new PieSlice("无数据", 1));
            else
            {
                var keep = list.Where(kv => kv.Value / total >= 0.03).ToList();
                if (keep.Count < 3) keep = list.Take(3).ToList();

                var keepSet = new HashSet<string>(keep.Select(k => k.Key));
                double other = 0;
                foreach (var kv in list)
                {
                    if (keepSet.Contains(kv.Key))
                    {
                        var slice = new PieSlice(kv.Key, kv.Value);
                        pie.Slices.Add(slice);
                        _warehouseSliceMap[slice] = kv.Key;
                    }
                    else other += kv.Value;
                }
                if (other > 0)
                {
                    var sliceOther = new PieSlice("其他", other);
                    pie.Slices.Add(sliceOther);
                    _warehouseSliceMap[sliceOther] = "其他";
                }
            }

            model.Series.Add(pie);
            _plotWarehouse.Model = model;
        }

        private static List<Aggregations.SalesItem> CleanSalesForVisuals(IEnumerable<Aggregations.SalesItem> src)
        {
            var list = src.Where(s => !string.IsNullOrWhiteSpace(s.Color) && !string.IsNullOrWhiteSpace(s.Size)).ToList();
            var byColor = list.GroupBy(x => x.Color).ToDictionary(g => g.Key, g => g.Sum(z => z.Qty));
            var bySize = list.GroupBy(x => x.Size).ToDictionary(g => g.Key, g => g.Sum(z => z.Qty));
            list = list.Where(x => byColor.GetValueOrDefault(x.Color, 0) != 0 && bySize.GetValueOrDefault(x.Size, 0) != 0).ToList();
            return list;
        }

        private void RenderCharts(List<Aggregations.SalesItem> salesItems)
        {
            var cleaned = CleanSalesForVisuals(salesItems);

            // Trend
            var series = Aggregations.BuildDateSeries(cleaned, _trendWindow);
            var modelTrend = new PlotModel { Title = $"近{_trendWindow}日销量趋势", PlotMargins = new OxyThickness(50, 10, 10, 40) };

            var xAxis = new DateTimeAxis { Position = AxisPosition.Bottom, StringFormat = "MM-dd", IntervalType = DateTimeIntervalType.Days, MajorStep = 1, MinorStep = 1, IntervalLength = 60, IsZoomEnabled = false, IsPanEnabled = false, MajorGridlineStyle = LineStyle.Solid };
            var yAxis = new LinearAxis { Position = AxisPosition.Left, MinimumPadding = 0, AbsoluteMinimum = 0, MajorGridlineStyle = LineStyle.Solid };
            modelTrend.Axes.Add(xAxis); modelTrend.Axes.Add(yAxis);

            var line = new LineSeries { Title = "销量", MarkerType = MarkerType.Circle };
            foreach (var (day, qty) in series) line.Points.Add(new DataPoint(DateTimeAxis.ToDouble(day), qty));
            modelTrend.Series.Add(line);
            _plotTrend.Model = modelTrend;

            // Size bars
            var sizeAgg = cleaned.GroupBy(x => x.Size).Select(g => new { Key = g.Key, Qty = g.Sum(z => z.Qty) })
                                 .Where(a => !string.IsNullOrWhiteSpace(a.Key) && a.Qty != 0)
                                 .OrderByDescending(a => a.Qty).ToList();
            var modelSize = new PlotModel { Title = "尺码销量（降序）" };
            var catSize = new CategoryAxis { Position = AxisPosition.Left, StartPosition = 1, EndPosition = 0 };
            foreach (var d in sizeAgg) catSize.Labels.Add(d.Key);
            var valSize = new LinearAxis { Position = AxisPosition.Bottom, MinorGridlineStyle = LineStyle.Dot, MajorGridlineStyle = LineStyle.Solid };
            var seriesSize = new BarSeries { LabelFormatString = "{0}", LabelPlacement = LabelPlacement.Inside, LabelMargin = 6 };
            foreach (var d in sizeAgg) seriesSize.Items.Add(new BarItem(d.Qty));
            modelSize.Axes.Add(catSize); modelSize.Axes.Add(valSize); modelSize.Series.Add(seriesSize);
            _plotSize.Model = modelSize;

            // Color bars
            var colorAgg = cleaned.GroupBy(x => x.Color).Select(g => new { Key = g.Key, Qty = g.Sum(z => z.Qty) })
                                  .Where(a => !string.IsNullOrWhiteSpace(a.Key) && a.Qty != 0)
                                  .OrderByDescending(a => a.Qty).ToList();
            var modelColor = new PlotModel { Title = "颜色销量（降序）" };
            var catColor = new CategoryAxis { Position = AxisPosition.Left, StartPosition = 1, EndPosition = 0 };
            foreach (var d in colorAgg) catColor.Labels.Add(d.Key);
            var valColor = new LinearAxis { Position = AxisPosition.Bottom, MinorGridlineStyle = LineStyle.Dot, MajorGridlineStyle = LineStyle.Solid };
            var seriesColor = new BarSeries { LabelFormatString = "{0}", LabelPlacement = LabelPlacement.Inside, LabelMargin = 6 };
            foreach (var d in colorAgg) seriesColor.Items.Add(new BarItem(d.Qty));
            modelColor.Axes.Add(catColor); modelColor.Axes.Add(valColor); modelColor.Series.Add(seriesColor);
            _plotColor.Model = modelColor;
        }

        private void ApplyFilter(string text)
        {
            if (_gridMaster == null) return;
            var t = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(t)) { _binding.DataSource = new BindingList<object>(_gridMaster); return; }

            var parts = t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToArray();
            var filtered = _gridMaster.Where(row =>
            {
                var dict = row.GetType().GetProperties().ToDictionary(p => p.Name, p => (object?)p.GetValue(row, null));
                foreach (var p in parts)
                {
                    var hit = dict.Values.Any(v => v != null && v.ToString()!.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!hit) return false;
                }
                return true;
            }).ToList();

            _binding.DataSource = new BindingList<object>(filtered);
        }

        private void ExportExcel()
        {
            try
            {
                using var sfd = new SaveFileDialog
                {
                    Filter = "Excel (*.xlsx)|*.xlsx",
                    FileName = $"导出_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
                };
                if (sfd.ShowDialog(this) != DialogResult.OK) return;

                using var wb = new ClosedXML.Excel.XLWorkbook();
                var ws = wb.AddWorksheet("销售明细");
                // Header
                ws.Cell(1, 1).Value = "日期";
                ws.Cell(1, 2).Value = "款式";
                ws.Cell(1, 3).Value = "颜色";
                ws.Cell(1, 4).Value = "尺码";
                ws.Cell(1, 5).Value = "数量";

                var rows = _gridMaster ?? new List<object>();
                int r = 2;
                foreach (var row in rows)
                {
                    var props = row.GetType().GetProperties();
                    ws.Cell(r, 1).Value = props.FirstOrDefault(p => p.Name == "日期")?.GetValue(row);
                    ws.Cell(r, 2).Value = props.FirstOrDefault(p => p.Name == "款式")?.GetValue(row);
                    ws.Cell(r, 3).Value = props.FirstOrDefault(p => p.Name == "颜色")?.GetValue(row);
                    ws.Cell(r, 4).Value = props.FirstOrDefault(p => p.Name == "尺码")?.GetValue(row);
                    ws.Cell(r, 5).Value = props.FirstOrDefault(p => p.Name == "数量")?.GetValue(row);
                    r++;
                }

                ws.Columns().AdjustToContents();
                wb.SaveAs(sfd.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "导出失败：" + ex.Message, "导出", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private Task LoadPriceAsync(string styleName)
        {
            // Not modified; placeholder to keep signature consistent.
            return Task.CompletedTask;
        }
    }
}
