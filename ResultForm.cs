using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ClosedXML.Excel;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Annotations;
using OxyPlot.WindowsForms;

namespace StyleWatcherWin
{
    /// <summary>
    /// 主结果窗口：展示解析结果、趋势图、库存和价格信息。
    /// 实现要求：
    /// - 无移动平均线逻辑
    /// - 折线图和柱状图有数值标注
    /// - KPI 顺序：销量 / 库存 / 库存天数 / 定级 / 最低价 / 保本价 / 缺码
    /// - 对 TrayApp 暴露：ShowAndFocusCentered / SetLoading / ApplyRawText
    /// </summary>
    public class ResultForm : Form
    {
        private readonly AppConfig _cfg;

        // 原始文本 & 解析数据
        private string _rawInput = string.Empty;
        private string _rawResult = string.Empty;
        private ParsedPayload? _parsed;
        private List<SaleRecord> _sales = new();
        private string? _styleName;

        // 顶部控件
        private readonly TextBox _boxInput = new() { Dock = DockStyle.Fill, Multiline = false };
        private readonly Button _btnExport = new() { Text = "导出", AutoSize = true, Dock = DockStyle.Right };
        private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 20, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray };

        // KPI 面板
        private readonly FlowLayoutPanel _kpi = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(12, 8, 12, 8),
            AutoScroll = false
        };

        private Label _kpiSales7;
        private Label _kpiInv;
        private Label _kpiDoc;
        private Label _kpiGrade;
        private Label _kpiMinPrice;
        private Label _kpiBreakeven;
        private Label _kpiMissing;

        // 主区域
        private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };

        // 图表
        private readonly PlotView _plotTrend = new() { Dock = DockStyle.Fill };
        private readonly PlotView _plotWarehouse = new() { Dock = DockStyle.Fill };
        private readonly PlotView _plotSize = new() { Dock = DockStyle.Fill };
        private readonly PlotView _plotColor = new() { Dock = DockStyle.Fill };

        // 明细
        private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false };
        private readonly BindingSource _binding = new();

        // 库存页
        private readonly InventoryTabPage _invPage;

        // 趋势窗口
        private int _trendWindow = 7;

        public ResultForm(AppConfig cfg)
        {
            _cfg = cfg ?? new AppConfig();

            Text = "款式信息";
            StartPosition = FormStartPosition.CenterScreen;
            Width = _cfg.window.width;
            Height = _cfg.window.height;
            TopMost = _cfg.window.alwaysOnTop;

            // 设置趋势窗口默认值
            if (_cfg.ui?.trendWindows != null && _cfg.ui.trendWindows.Length > 0)
                _trendWindow = _cfg.ui.trendWindows[0];

            // 顶部
            var top = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 32,
                ColumnCount = 3,
                Padding = new Padding(8, 4, 8, 4)
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _boxInput.PlaceholderText = "可在此粘贴内容后回车，或使用快捷键触发";
            _boxInput.KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    await ManualQueryAsync(_boxInput.Text);
                }
            };

            var btnReload = new Button { Text = "解析", AutoSize = true, Dock = DockStyle.Right };
            btnReload.Click += async (s, e) => await ManualQueryAsync(_boxInput.Text);

            _btnExport.Click += (s, e) => ExportToExcel();

            top.Controls.Add(_boxInput, 0, 0);
            top.Controls.Add(btnReload, 1, 0);
            top.Controls.Add(_btnExport, 2, 0);

            // KPI 顺序：销量 / 库存 / 库存天数 / 定级 / 最低价 / 保本价 / 缺码
            _kpiSales7 = MakeKpi("近7日销量");
            _kpiInv = MakeKpi("可用库存总量");
            _kpiDoc = MakeKpi("库存天数");
            _kpiGrade = MakeKpi("定级");
            _kpiMinPrice = MakeKpi("最低价");
            _kpiBreakeven = MakeKpi("保本价");
            _kpiMissing = MakeKpi("缺货尺码");

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                RowCount = 2,
                ColumnCount = 1
            };
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            header.Controls.Add(top, 0, 0);
            header.Controls.Add(_kpi, 0, 1);

            // Tab 总体布局
            BuildGrid();
            _invPage = new InventoryTabPage(_cfg);

            BuildTabs();

            Controls.Add(_tabs);
            Controls.Add(header);
            Controls.Add(_status);
        }

        #region 对 TrayApp 暴露的方法

public void ShowAndFocusCentered(bool topMost)
{
    TopMost = topMost;
    if (!Visible) Show();
    WindowState = FormWindowState.Normal;
    Activate();

    var screen = Screen.FromControl(this).WorkingArea;
    Left = screen.Left + (screen.Width - Width) / 2;
    Top = screen.Top + (screen.Height - Height) / 2;
}

/// <summary>
/// 仅聚焦输入框（托盘快捷键使用）。
/// </summary>
public void FocusInput()
{
    try
    {
        if (!Visible) Show();
        Activate();
        _boxInput.Focus();
        _boxInput.SelectAll();
    }
    catch
    {
        // ignore
    }
}

/// <summary>
/// 在鼠标附近显示窗口（托盘图标点击使用），尽量不抢焦点。
/// </summary>
public void ShowNoActivateAtCursor()
{
    try
    {
        var cursor = Cursor.Position;
        if (!Visible)
            Show();
        WindowState = FormWindowState.Normal;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(
            Math.Max(0, cursor.X - Width / 2),
            Math.Max(0, cursor.Y - Height / 2));
    }
    catch
    {
        // ignore
    }
}

public void SetLoading(string message)
{
    _status.Text = message;
    SetKpi(_kpiSales7, "—");
    SetKpi(_kpiInv, "—");
    SetKpi(_kpiDoc, "—");
    SetKpi(_kpiGrade, "—");
    SetKpi(_kpiMinPrice, "—");
    SetKpi(_kpiBreakeven, "—");
    SetKpi(_kpiMissing, "—");
}

/// <summary>
/// TrayApp 调用：传入原始选中文本 + 解析服务返回的文本（已格式化）。
/// </summary>
public void ApplyRawText(string input, string result)
{
    _rawInput = input ?? string.Empty;
    _rawResult = result ?? string.Empty;

    try
    {
        _parsed = Parser.Parse(_rawResult);
    }
    catch
    {
        _parsed = Parser.Parse(_rawResult ?? string.Empty);
    }

    _sales = (_parsed?.Records ?? new List<SaleRecord>())
        .OrderBy(r => r.Date)
        .ToList();

    _styleName = _parsed?.StyleName
                 ?? _sales.FirstOrDefault()?.Name
                 ?? string.Empty;

    BindSales();
    UpdateKpisBase();
    RenderChartsSafe();
    _ = LoadInventoryAndPriceAsync(_styleName);

    _status.Text = $"解析完成：明细 {_sales.Count} 条；款号：{(_styleName ?? "未知")}";
}

#endregion

        #region 初始化子视图

        private Label MakeKpi(Label target, string title)
        {
            var panel = new Panel
            {
                Width = 180,
                Height = 64,
                Margin = new Padding(0, 0, 10, 6)
            };

            var lblTitle = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 20,
                ForeColor = Color.DimGray
            };
            var lblValue = new Label
            {
                Text = "—",
                Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 12, FontStyle.Bold),
                ForeColor = Color.Black
            };

            panel.Controls.Add(lblValue);
            panel.Controls.Add(lblTitle);
            _kpi.Controls.Add(panel);

            return target = lblValue;
        }

        private void BuildGrid()
        {
            _grid.AutoGenerateColumns = false;
            _grid.Columns.Clear();
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "日期",
                DataPropertyName = "Date",
                Width = 90,
                DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd" }
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "款式", DataPropertyName = "Name", Width = 140 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "尺码", DataPropertyName = "Size", Width = 60 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "颜色", DataPropertyName = "Color", Width = 80 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "数量", DataPropertyName = "Qty", Width = 60 });

            _binding.DataSource = typeof(SaleRecord);
            _grid.DataSource = _binding;
        }

        private void BuildTabs()
        {
            _tabs.TabPages.Clear();

            // 总览标签：趋势 + 三个占比柱状图
            var overview = new TabPage("概览");

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 2,
                Padding = new Padding(8)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            // 趋势上方工具条：趋势窗口切换
            var topTrendTools = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                Height = 28,
                Padding = new Padding(4, 2, 4, 0)
            };
            var wins = (_cfg.ui?.trendWindows != null && _cfg.ui.trendWindows.Length > 0)
                ? _cfg.ui.trendWindows
                : new[] { 7, 14, 30 };
            foreach (var w in wins)
            {
                var rb = new RadioButton
                {
                    Text = $"{w} 日",
                    Tag = w,
                    AutoSize = true,
                    Margin = new Padding(0, 2, 10, 0)
                };
                if (w == _trendWindow) rb.Checked = true;
                rb.CheckedChanged += (s, e) =>
                {
                    if (rb.Checked)
                    {
                        _trendWindow = (int)rb.Tag;
                        RenderChartsSafe();
                    }
                };
                topTrendTools.Controls.Add(rb);
            }

            var trendPanel = new Panel { Dock = DockStyle.Fill };
            trendPanel.Controls.Add(_plotTrend);
            trendPanel.Controls.Add(topTrendTools);
            topTrendTools.BringToFront();

            // 2x2: 趋势 / 仓库 / 尺码 / 颜色
            layout.Controls.Add(trendPanel, 0, 0);
            layout.SetColumnSpan(trendPanel, 2);

            layout.Controls.Add(_plotWarehouse, 0, 1);
            layout.Controls.Add(_plotSize, 1, 1);

            var colorPanel = new Panel { Dock = DockStyle.Fill };
            colorPanel.Controls.Add(_plotColor);

            // 扩展为第三行以容纳颜色分布
            layout.RowCount = 3;
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            layout.Controls.Add(colorPanel, 0, 2);
            layout.SetColumnSpan(colorPanel, 2);

            overview.Controls.Add(layout);
// 明细标签
            var detail = new TabPage("明细");
            detail.Controls.Add(_grid);

            // 库存标签
            var inv = new TabPage("库存");
            inv.Controls.Add(_invPage);

            _tabs.TabPages.Add(overview);
            _tabs.TabPages.Add(detail);
            _tabs.TabPages.Add(inv);
        }

        #endregion

        #region 数据绑定 & KPI

        private void BindSales()
        {
            _binding.DataSource = new BindingList<SaleRecord>(_sales);
        }

        private void SetKpi(Label lbl, string text) => lbl.Text = text;

        private void UpdateKpisBase()
        {
            if (_sales.Count == 0)
            {
                SetKpi(_kpiSales7, "—");
                SetKpi(_kpiInv, "—");
                SetKpi(_kpiDoc, "—");
                SetKpi(_kpiGrade, "—");
                SetKpi(_kpiMinPrice, "—");
                SetKpi(_kpiBreakeven, "—");
                SetKpi(_kpiMissing, "—");
                return;
            }

            // 近7日销量
            var today = _sales.Max(s => s.Date.Date);
            var from = today.AddDays(-6);
            var last7 = _sales.Where(s => s.Date.Date >= from && s.Date.Date <= today).Sum(s => s.Qty);
            SetKpi(_kpiSales7, Aggregations.FormatNumber(last7));

            // 缺码先按销售维度估一个（库存页加载完后会再刷新）
            SetKpi(_kpiMissing, CalcMissingBySales());
        }

        private string CalcMissingBySales()
        {
            if (_sales.Count == 0) return "—";
            var bySize = _sales
                .Where(s => !string.IsNullOrWhiteSpace(s.Size))
                .GroupBy(s => s.Size)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
            if (bySize.Count == 0) return "—";
            var max = bySize.Max(kv => kv.Value);
            var lacks = bySize.Where(kv => kv.Value < max / 3.0).Select(kv => kv.Key).ToList();
            return lacks.Count == 0 ? "无明显缺码" : string.Join("/", lacks);
        }

        private void UpdateKpiFromInventory(int totalInv, IEnumerable<string> offeredSizes, IEnumerable<string> zeroSizes)
        {
            SetKpi(_kpiInv, totalInv > 0 ? Aggregations.FormatNumber(totalInv) : "—");

            if (_sales.Count == 0 || totalInv <= 0)
            {
                SetKpi(_kpiDoc, "—");
            }
            else
            {
                // 日均销量基于配置窗口
                var first = _sales.Min(s => s.Date).Date;
                var last = _sales.Max(s => s.Date).Date;
                var days = Math.Max(_cfg.inventoryAlert?.minSalesWindowDays ?? 7,
                    (last - first).TotalDays + 1);
                var avg = _sales.Sum(s => s.Qty) / Math.Max(1.0, days);
                if (avg <= 0)
                    SetKpi(_kpiDoc, "—");
                else
                {
                    var doc = totalInv / avg;
                    var txt = doc.ToString("0.#");
                    var red = _cfg.inventoryAlert?.docRed ?? 3;
                    var yellow = _cfg.inventoryAlert?.docYellow ?? 7;
                    if (doc <= red) txt += " ⚠";
                    else if (doc <= yellow) txt += " !";
                    SetKpi(_kpiDoc, txt);
                }
            }

            // 基于库存信息重新算缺码（供应有但库存为 0 的尺码）
            var missing = MissingSizes(offeredSizes, zeroSizes).ToList();
            if (missing.Count > 0)
                SetKpi(_kpiMissing, string.Join("/", missing));
        }

        #endregion

        #region 图表渲染（含数值标注）

        private void RenderChartsSafe()
        {
            try
            {
                RenderCharts();
            }
            catch (Exception ex)
            {
                _status.Text = "绘制图表失败：" + ex.Message;
            }
        }

        private void RenderCharts()
        {
            if (_sales.Count == 0)
            {
                _plotTrend.Model = null;
                _plotWarehouse.Model = null;
                _plotSize.Model = null;
                _plotColor.Model = null;
                return;
            }

            // 趋势折线图
            {
                var model = new PlotModel { Title = $"近{_trendWindow}日销量", PlotMargins = new OxyThickness(50, 10, 10, 30) };
                var axisX = new DateTimeAxis
                {
                    Position = AxisPosition.Bottom,
                    StringFormat = "MM-dd",
                    IntervalType = DateTimeIntervalType.Days,
                    MinorIntervalType = DateTimeIntervalType.Days,
                    MajorGridlineStyle = LineStyle.Solid,
                    MinorGridlineStyle = LineStyle.Dot
                };
                var axisY = new LinearAxis
                {
                    Position = AxisPosition.Left,
                    Minimum = 0,
                    MajorGridlineStyle = LineStyle.Solid,
                    MinorGridlineStyle = LineStyle.Dot
                };
                model.Axes.Add(axisX);
                model.Axes.Add(axisY);

                var to = _sales.Max(s => s.Date.Date);
                var from = to.AddDays(-(_trendWindow - 1));
                var byDay = _sales
                    .Where(s => s.Date.Date >= from && s.Date.Date <= to)
                    .GroupBy(s => s.Date.Date)
                    .OrderBy(g => g.Key)
                    .Select(g => new { Date = g.Key, Qty = g.Sum(x => x.Qty) })
                    .ToList();

                var line = new LineSeries { MarkerType = MarkerType.Circle };
                foreach (var d in byDay)
                {
                    var x = DateTimeAxis.ToDouble(d.Date);
                    line.Points.Add(new DataPoint(x, d.Qty));
                }
                model.Series.Add(line);

                // 数值标注
                foreach (var p in line.Points)
                {
                    model.Annotations.Add(new OxyPlot.Annotations.TextAnnotation { TextPosition = new DataPoint(p.X, p.Y),
                        Text = p.Y.ToString("0"),
                        TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
                        TextVerticalAlignment = OxyPlot.VerticalAlignment.Bottom,
                        Stroke = OxyColors.Transparent,
                        FontSize = 9,
                        Offset = new ScreenVector(0, -4)
                    });
                }

                _plotTrend.Model = model;
            }

            // 仓库 / 尺码 / 颜色柱状图
            var cleaned = _sales;

            // 仓库：InventoryTabPage 内部有更细分视图，这里只展示整体情况时使用占位逻辑，
            // 如没有仓库字段可以按需要扩展。这里暂不绘制仓库分布，以防误导。
            _plotWarehouse.Model = new PlotModel { Title = "仓库占比（由库存页明细展示）" };

            // 尺码销量
            {
                var bySize = cleaned
                    .Where(s => !string.IsNullOrWhiteSpace(s.Size))
                    .GroupBy(s => s.Size)
                    .OrderBy(g => g.Key)
                    .Select(g => new { Size = g.Key, Qty = g.Sum(x => x.Qty) })
                    .ToList();

                var model = new PlotModel { Title = "尺码销量", PlotMargins = new OxyThickness(60, 6, 40, 40) };
                var catAxis = new CategoryAxis { Position = AxisPosition.Left };
                var valAxis = new LinearAxis { Position = AxisPosition.Bottom, Minimum = 0 };
                var series = new BarSeries
                {
                    LabelFormatString = "{0}",
                    LabelPlacement = LabelPlacement.Outside,
                    LabelMargin = 4
                };

                foreach (var item in bySize)
                {
                    catAxis.Labels.Add(item.Size);
                    series.Items.Add(new BarItem(item.Qty));
                }

                model.Axes.Add(catAxis);
                model.Axes.Add(valAxis);
                model.Series.Add(series);

                _plotSize.Model = model;
            }

            // 颜色销量
            {
                var byColor = cleaned
                    .Where(s => !string.IsNullOrWhiteSpace(s.Color))
                    .GroupBy(s => s.Color)
                    .OrderBy(g => g.Key)
                    .Select(g => new { Color = g.Key, Qty = g.Sum(x => x.Qty) })
                    .ToList();

                var model = new PlotModel { Title = "颜色销量", PlotMargins = new OxyThickness(60, 6, 40, 40) };
                var catAxis = new CategoryAxis { Position = AxisPosition.Left };
                var valAxis = new LinearAxis { Position = AxisPosition.Bottom, Minimum = 0 };
                var series = new BarSeries
                {
                    LabelFormatString = "{0}",
                    LabelPlacement = LabelPlacement.Outside,
                    LabelMargin = 4
                };

                foreach (var item in byColor)
                {
                    catAxis.Labels.Add(item.Color);
                    series.Items.Add(new BarItem(item.Qty));
                }

                model.Axes.Add(catAxis);
                model.Axes.Add(valAxis);
                model.Series.Add(series);

                _plotColor.Model = model;
            }
        }

        private static IEnumerable<string> MissingSizes(IEnumerable<string> offeredSizes, IEnumerable<string> zeroSizes)
        {
            if (offeredSizes == null || zeroSizes == null)
                yield break;

            var offered = new HashSet<string>(offeredSizes.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
            var zeros = new HashSet<string>(zeroSizes.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);

            foreach (var s in offered)
                if (zeros.Contains(s))
                    yield return s;
        }

        #endregion

        #region 库存 & 价格查询

        private async Task LoadInventoryAndPriceAsync(string? styleName)
        {
            if (string.IsNullOrWhiteSpace(styleName))
                return;

            // 库存
            try
            {
                await _invPage.LoadInventoryAsync(styleName);
                var total = _invPage.TotalAvailable();
                var offered = _invPage.OfferedSizes();
                var zeros = _invPage.CurrentZeroSizes();
                UpdateKpiFromInventory(total, offered, zeros);
            }
            catch
            {
                // 库存失败不阻断
            }

            // 价格 & 定级
            try
            {
                await LoadPriceAsync(styleName);
            }
            catch
            {
                SetKpi(_kpiGrade, "—");
                SetKpi(_kpiMinPrice, "—");
                SetKpi(_kpiBreakeven, "—");
            }
        }

        private async Task LoadPriceAsync(string styleName)
        {
            if (string.IsNullOrWhiteSpace(styleName))
                return;

            var url = "http://192.168.40.97:8002/lookup?name=" + Uri.EscapeDataString(styleName);
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var resp = await http.GetAsync(url);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var first = root[0];

                string? grade = first.TryGetProperty("grade", out var g) ? g.GetString() : null;
                string? minPrice = first.TryGetProperty("min_price_one", out var m) ? m.GetRawText() : null;
                string? breakeven = first.TryGetProperty("breakeven_one", out var b) ? b.GetRawText() : null;

                SetKpi(_kpiGrade, string.IsNullOrWhiteSpace(grade) ? "—" : grade);
                SetKpi(_kpiMinPrice, string.IsNullOrWhiteSpace(minPrice) ? "—" : minPrice);
                SetKpi(_kpiBreakeven, string.IsNullOrWhiteSpace(breakeven) ? "—" : breakeven);
            }
            else
            {
                SetKpi(_kpiGrade, "—");
                SetKpi(_kpiMinPrice, "—");
                SetKpi(_kpiBreakeven, "—");
            }
        }

        #endregion

        
private void ExportToExcel()
{
    try
    {
        if (_sales == null || _sales.Count == 0)
        {
            MessageBox.Show(this, "当前没有可导出的销售明细。", "提示",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var sfd = new SaveFileDialog
        {
            Filter = "Excel 文件|*.xlsx",
            FileName = string.IsNullOrWhiteSpace(_styleName)
                ? "销量明细.xlsx"
                : $"{_styleName}-销量明细.xlsx"
        };

        if (sfd.ShowDialog(this) != DialogResult.OK)
            return;

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("明细");

        // 表头
        ws.Cell(1, 1).Value = "日期";
        ws.Cell(1, 2).Value = "款式";
        ws.Cell(1, 3).Value = "尺码";
        ws.Cell(1, 4).Value = "颜色";
        ws.Cell(1, 5).Value = "数量";

        int row = 2;
        foreach (var r in _sales)
        {
            ws.Cell(row, 1).Value = r.Date;
            ws.Cell(row, 1).Style.DateFormat.Format = "yyyy-mm-dd";
            ws.Cell(row, 2).Value = r.Name;
            ws.Cell(row, 3).Value = r.Size;
            ws.Cell(row, 4).Value = r.Color;
            ws.Cell(row, 5).Value = r.Qty;
            row++;
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(sfd.FileName);
        _status.Text = "已导出：" + sfd.FileName;
    }
    catch (Exception ex)
    {
        MessageBox.Show(this, "导出失败：" + ex.Message, "错误",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

#region 手动查询入口（顶部输入框）

        private async Task ManualQueryAsync(string text)
        {
            text = text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                MessageBox.Show(this, "请输入要解析的文本。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SetLoading("查询中...");
            try
            {
                var raw = await ApiHelper.QueryAsync(_cfg, text);
                var pretty = Formatter.Prettify(raw);
                ApplyRawText(text, pretty);
            }
            catch (Exception ex)
            {
                SetLoading("查询失败：" + ex.Message);
            }
        }

        #endregion
    }
}
