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
    /// 结果窗口：
    /// - 接收 TrayApp 传入的原始文本/解析结果
    /// - 展示 KPI、趋势图、尺码/颜色分布、库存信息
    /// - 支持导出明细
    /// - 无 MA7 / 移动平均线
    /// - 柱状图/折线图有数值标注
    /// - KPI 顺序：销量 / 库存 / 库存天数 / 定级 / 最低价 / 保本价 / 缺码
    /// </summary>
    public class ResultForm : Form
    {
        private readonly AppConfig _cfg;

        // 输入与操作
        private readonly TextBox _input = new();
        private readonly Button _btnQuery = new();
        private readonly Button _btnExport = new();

        // KPI
        private readonly FlowLayoutPanel _kpiPanel = new();
        private Label _kpiSales7 = null!;
        private Label _kpiInv = null!;
        private Label _kpiDoc = null!;
        private Label _kpiGrade = null!;
        private Label _kpiMinPrice = null!;
        private Label _kpiBreakeven = null!;
        private FlowLayoutPanel? _kpiMissingFlow;

        // Tab
        private readonly TabControl _tabs = new();

        // 概览图
        private readonly PlotView _plotTrend = new();
        private readonly PlotView _plotSize = new();
        private readonly PlotView _plotColor = new();
        private readonly PlotView _plotWarehouse = new();
        private readonly FlowLayoutPanel _trendSwitch = new();
        private int _trendWindow = 7;

        // 明细
        private readonly DataGridView _grid = new();
        private readonly BindingSource _binding = new();

        // 库存
        private readonly InventoryTabPage _inventoryPage;

        // 状态栏
        private readonly Label _status = new();

        // 数据
        private List<SaleRecord> _sales = new();
        private string _styleName = string.Empty;
        private string _lastDisplayText = string.Empty;

        public ResultForm(AppConfig cfg)
        {
            _cfg = cfg ?? new AppConfig();

            Text = "StyleWatcher";
            Font = new Font("Microsoft YaHei UI", _cfg.window.fontSize);
            Width = Math.Max(1200, _cfg.window.width);
            Height = Math.Max(800, _cfg.window.height);
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = _cfg.window.alwaysOnTop;
            BackColor = Color.White;

            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    Hide();
                    e.Handled = true;
                }
            };

            if (_cfg.ui?.trendWindows != null && _cfg.ui.trendWindows.Length > 0)
                _trendWindow = _cfg.ui.trendWindows[0];

            // 总体布局
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // header
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120)); // KPI
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // tabs
            Controls.Add(root);

            var header = BuildHeader();
            root.Controls.Add(header, 0, 0);

            BuildKpis();
            root.Controls.Add(_kpiPanel, 0, 1);

            _tabs.Dock = DockStyle.Fill;
            BuildTabs();
            root.Controls.Add(_tabs, 0, 2);

            // 状态栏
            _status.Dock = DockStyle.Bottom;
            _status.Height = 22;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.ForeColor = Color.DimGray;
            Controls.Add(_status);

            _inventoryPage = new InventoryTabPage(_cfg);
        }

        #region UI 构建

        private Control BuildHeader()
        {
            _input.BorderStyle = BorderStyle.FixedSingle;
            _input.Dock = DockStyle.Fill;
            _input.PlaceholderText = "可直接输入内容回车解析，或使用快捷键从托盘触发";
            _input.KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    await ManualQueryAsync(_input.Text);
                }
            };

            _btnQuery.Text = "解析";
            _btnQuery.AutoSize = true;
            _btnQuery.Click += async (s, e) => await ManualQueryAsync(_input.Text);

            _btnExport.Text = "导出";
            _btnExport.AutoSize = true;
            _btnExport.Click += (s, e) => ExportToExcel();

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(8, 4, 8, 4)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            layout.Controls.Add(_input, 0, 0);
            layout.Controls.Add(_btnQuery, 1, 0);
            layout.Controls.Add(_btnExport, 2, 0);

            return layout;
        }

        private void BuildKpis()
        {
            _kpiPanel.Dock = DockStyle.Fill;
            _kpiPanel.FlowDirection = FlowDirection.LeftToRight;
            _kpiPanel.WrapContents = true;
            _kpiPanel.Padding = new Padding(12, 6, 12, 6);
            _kpiPanel.AutoScroll = false;

            _kpiSales7 = CreateKpiCard("近7日销量");
            _kpiInv = CreateKpiCard("可用库存总量");
            _kpiDoc = CreateKpiCard("库存天数");
            _kpiGrade = CreateKpiCard("定级");
            _kpiMinPrice = CreateKpiCard("最低价");
            _kpiBreakeven = CreateKpiCard("保本价");
            CreateMissingKpiCard("缺货尺码");
        }

        private Label CreateKpiCard(string title)
        {
            var host = new Panel
            {
                Width = 180,
                Height = 64,
                Padding = new Padding(8, 4, 8, 4),
                Margin = new Padding(0, 0, 10, 6),
                BackColor = Color.FromArgb(250, 250, 250),
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblTitle = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 18,
                ForeColor = Color.DimGray
            };

            var lblValue = new Label
            {
                Text = "—",
                Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(47, 47, 47),
                TextAlign = ContentAlignment.MiddleLeft
            };

            host.Controls.Add(lblValue);
            host.Controls.Add(lblTitle);
            _kpiPanel.Controls.Add(host);

            return lblValue;
        }

        private void CreateMissingKpiCard(string title)
        {
            var host = new Panel
            {
                Width = 260,
                Height = 64,
                Padding = new Padding(8, 4, 8, 4),
                Margin = new Padding(0, 0, 10, 6),
                BackColor = Color.FromArgb(250, 250, 250),
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblTitle = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 18,
                ForeColor = Color.DimGray
            };

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

            host.Controls.Add(flow);
            host.Controls.Add(lblTitle);
            _kpiPanel.Controls.Add(host);
        }

        private void BuildTabs()
        {
            _tabs.TabPages.Clear();

            // 概览
            var overview = new TabPage("概览") { BackColor = Color.White };
            overview.Controls.Add(BuildOverviewLayout());
            _tabs.TabPages.Add(overview);

            // 明细
            var detail = new TabPage("明细") { BackColor = Color.White };
            BuildGrid();
            detail.Controls.Add(_grid);
            _tabs.TabPages.Add(detail);

            // 库存
            var inv = new TabPage("库存") { BackColor = Color.White };
            _inventoryPage.Dock = DockStyle.Fill;
            inv.Controls.Add(_inventoryPage);
            _tabs.TabPages.Add(inv);
        }

        private Control BuildOverviewLayout()
        {
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

            // 趋势窗口切换条
            _trendSwitch.FlowDirection = FlowDirection.LeftToRight;
            _trendSwitch.Dock = DockStyle.Top;
            _trendSwitch.Height = 26;
            _trendSwitch.Padding = new Padding(4, 2, 4, 0);

            var wins = (_cfg.ui?.trendWindows != null && _cfg.ui.trendWindows.Length > 0)
                ? _cfg.ui.trendWindows
                : new[] { 7, 14, 30 };

            if (Array.IndexOf(wins, _trendWindow) < 0)
                _trendWindow = wins[0];

            foreach (var w in wins)
            {
                var rb = new RadioButton
                {
                    Text = $"{w} 日",
                    Tag = w,
                    AutoSize = true,
                    Margin = new Padding(0, 2, 12, 0)
                };
                if (w == _trendWindow)
                    rb.Checked = true;
                rb.CheckedChanged += (s, e) =>
                {
                    if (rb.Checked)
                    {
                        _trendWindow = (int)rb.Tag;
                        if (_sales != null && _sales.Count > 0)
                            RenderCharts();
                    }
                };
                _trendSwitch.Controls.Add(rb);
            }

            var trendPanel = new Panel { Dock = DockStyle.Fill };
            _plotTrend.Dock = DockStyle.Fill;
            trendPanel.Controls.Add(_plotTrend);
            trendPanel.Controls.Add(_trendSwitch);
            _trendSwitch.BringToFront();

            layout.Controls.Add(trendPanel, 0, 0);
            layout.SetColumnSpan(trendPanel, 2);

            _plotWarehouse.Dock = DockStyle.Fill;
            _plotSize.Dock = DockStyle.Fill;
            _plotColor.Dock = DockStyle.Fill;

            layout.Controls.Add(_plotWarehouse, 0, 1);
            layout.Controls.Add(_plotSize, 1, 1);
            layout.Controls.Add(_plotColor, 0, 1); // 暂不额外拆行，保持简单布局

            return layout;
        }

        private void BuildGrid()
        {
            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = true;
            _grid.AutoGenerateColumns = false;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

            _grid.Columns.Clear();
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "日期",
                DataPropertyName = "Date",
                Width = 90,
                DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd" }
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "款式",
                DataPropertyName = "Name",
                Width = 140
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "尺码",
                DataPropertyName = "Size",
                Width = 60
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "颜色",
                DataPropertyName = "Color",
                Width = 80
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "数量",
                DataPropertyName = "Qty",
                Width = 60
            });

            _binding.DataSource = typeof(SaleRecord);
            _grid.DataSource = _binding;
        }

        #endregion

        #region 与 TrayApp 对齐的公开方法

        public void FocusInput()
        {
            try
            {
                if (!Visible) Show();
                WindowState = FormWindowState.Normal;
                Activate();
                _input.Focus();
                _input.SelectAll();
            }
            catch { }
        }

        public void ShowNoActivateAtCursor()
        {
            try
            {
                var cursor = Cursor.Position;
                if (!Visible) Show();
                WindowState = FormWindowState.Normal;
                StartPosition = FormStartPosition.Manual;
                Location = new Point(
                    Math.Max(0, cursor.X - Width / 2),
                    Math.Max(0, cursor.Y - Height / 2));
            }
            catch
            {
                Show();
            }
        }

        public void ShowAndFocusCentered()
        {
            ShowAndFocusCentered(_cfg.window.alwaysOnTop);
        }

        public void ShowAndFocusCentered(bool alwaysOnTop)
        {
            TopMost = alwaysOnTop;
            if (!Visible) Show();
            WindowState = FormWindowState.Normal;
            Activate();

            var area = Screen.FromControl(this).WorkingArea;
            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Top + (area.Height - Height) / 2;
            FocusInput();
        }

        public void SetLoading(string message)
        {
            _status.Text = message;
            SetKpiValue(_kpiSales7, "—");
            SetKpiValue(_kpiInv, "—");
            SetKpiValue(_kpiDoc, "—");
            SetKpiValue(_kpiGrade, "—");
            SetKpiValue(_kpiMinPrice, "—");
            SetKpiValue(_kpiBreakeven, "—");
            if (_kpiMissingFlow != null)
            {
                _kpiMissingFlow.Controls.Clear();
                _kpiMissingFlow.Controls.Add(new Label
                {
                    AutoSize = true,
                    Text = "—",
                    ForeColor = Color.FromArgb(47, 47, 47)
                });
            }
        }

        /// <summary>
        /// TrayApp：传入选中文本 + 已经格式化过的解析文本。
        /// </summary>
        public async void ApplyRawText(string selection, string parsed)
        {
            try
            {
                _input.Text = selection ?? string.Empty;
                _lastDisplayText = parsed ?? selection ?? string.Empty;
                await LoadTextAsync(parsed ?? selection ?? string.Empty);
            }
            catch
            {
                // 保底不炸
            }
        }

        /// <summary>
        /// 仅设置输入框文本（不立刻解析）。
        /// </summary>
        public void ApplyRawText(string text)
        {
            _input.Text = text ?? string.Empty;
        }

        #endregion

        #region 加载与解析

        
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
        await LoadTextAsync(raw);
    }
    catch (Exception ex)
    {
        _status.Text = "查询失败：" + ex.Message;
    }
}

public async Task LoadTextAsync(string raw)
            => await ReloadAsync(raw);

        private async Task ReloadAsync()
            => await ReloadAsync(_input.Text);

        private async Task ReloadAsync(string displayText)
        {
            await Task.Yield();

            if (string.IsNullOrWhiteSpace(displayText))
                displayText = _lastDisplayText;

            _lastDisplayText = displayText ?? string.Empty;

            var parsed = Parser.Parse(displayText ?? string.Empty);

            _sales = parsed.Records
                .OrderBy(r => r.Name)
                .ThenBy(r => r.Color)
                .ThenBy(r => r.Size)
                .ThenByDescending(r => r.Date)
                .ToList();

            _styleName = !string.IsNullOrWhiteSpace(parsed.StyleName)
                ? parsed.StyleName
                : (_sales.FirstOrDefault()?.Name ?? string.Empty);

            BindSalesGrid();
            UpdateKpisBase();
            RenderCharts();

            if (!string.IsNullOrWhiteSpace(_styleName))
            {
                await LoadInventoryAndPriceAsync(_styleName);
            }

            _status.Text = $"解析完成：明细 {_sales.Count} 条；款号：{(string.IsNullOrWhiteSpace(_styleName) ? "未知" : _styleName)}";
        }

        private void BindSalesGrid()
        {
            _binding.DataSource = new BindingList<SaleRecord>(_sales);
        }

        #endregion

        #region KPI 计算

        private void SetKpiValue(Label label, string value, Color? color = null)
        {
            if (label == null) return;
            label.Text = value ?? "—";
            label.ForeColor = color ?? Color.FromArgb(47, 47, 47);
        }

        private void UpdateKpisBase()
        {
            if (_sales.Count == 0)
            {
                SetKpiValue(_kpiSales7, "—");
                if (_kpiMissingFlow != null)
                {
                    _kpiMissingFlow.Controls.Clear();
                    _kpiMissingFlow.Controls.Add(new Label { AutoSize = true, Text = "—" });
                }
                return;
            }

            var maxDate = _sales.Max(s => s.Date.Date);
            var from = maxDate.AddDays(-6);
            var last7 = _sales
                .Where(s => s.Date.Date >= from && s.Date.Date <= maxDate)
                .Sum(s => s.Qty);
            SetKpiValue(_kpiSales7, Aggregations.FormatNumber(last7));

            // 缺码：先基于销量推断一次，待库存回来后再覆盖
            UpdateMissingBySales();
        }

        private void UpdateMissingBySales()
        {
            if (_kpiMissingFlow == null) return;
            _kpiMissingFlow.Controls.Clear();

            if (_sales.Count == 0)
            {
                _kpiMissingFlow.Controls.Add(new Label { AutoSize = true, Text = "—" });
                return;
            }

            var bySize = _sales
                .Where(s => !string.IsNullOrWhiteSpace(s.Size))
                .GroupBy(s => s.Size)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));

            if (bySize.Count == 0)
            {
                _kpiMissingFlow.Controls.Add(new Label { AutoSize = true, Text = "—" });
                return;
            }

            var max = bySize.Max(kv => kv.Value);
            var lacks = bySize
                .Where(kv => kv.Value < max / 3.0)
                .Select(kv => kv.Key)
                .OrderBy(x => x)
                .ToList();

            if (lacks.Count == 0)
            {
                _kpiMissingFlow.Controls.Add(new Label { AutoSize = true, Text = "无明显缺码" });
                return;
            }

            foreach (var s in lacks)
            {
                _kpiMissingFlow.Controls.Add(new Label
                {
                    AutoSize = true,
                    Text = s,
                    Margin = new Padding(0, 0, 6, 2),
                    Font = new Font(Font.FontFamily, 8.5f, FontStyle.Regular),
                    ForeColor = Color.FromArgb(47, 47, 47)
                });
            }
        }

        private void UpdateKpiFromInventory(int totalInv, IEnumerable<string> offeredSizes, IEnumerable<string> zeroSizes)
        {
            SetKpiValue(_kpiInv, totalInv > 0 ? Aggregations.FormatNumber(totalInv) : "—");

            if (_sales.Count == 0 || totalInv <= 0)
            {
                SetKpiValue(_kpiDoc, "—");
            }
            else
            {
                var first = _sales.Min(s => s.Date.Date);
                var last = _sales.Max(s => s.Date.Date);
                var days = Math.Max(
                    _cfg.inventoryAlert?.minSalesWindowDays ?? 7,
                    (last - first).TotalDays + 1);

                var avg = _sales.Sum(s => s.Qty) / Math.Max(1.0, days);
                if (avg <= 0)
                    SetKpiValue(_kpiDoc, "—");
                else
                {
                    var doc = totalInv / avg;
                    var txt = doc.ToString("0.#");
                    var red = _cfg.inventoryAlert?.docRed ?? 3;
                    var yellow = _cfg.inventoryAlert?.docYellow ?? 7;

                    Color? c = null;
                    if (doc <= red) c = Color.Red;
                    else if (doc <= yellow) c = Color.DarkOrange;

                    SetKpiValue(_kpiDoc, txt, c);
                }
            }

            // 用库存信息覆盖缺码：有供应但当前库存为 0 的尺码
            if (_kpiMissingFlow != null && offeredSizes != null && zeroSizes != null)
            {
                _kpiMissingFlow.Controls.Clear();
                var offered = new HashSet<string>(offeredSizes, StringComparer.OrdinalIgnoreCase);
                var zeros = new HashSet<string>(zeroSizes, StringComparer.OrdinalIgnoreCase);

                var lacks = offered.Intersect(zeros)
                    .OrderBy(x => x)
                    .ToList();

                if (lacks.Count == 0)
                {
                    _kpiMissingFlow.Controls.Add(new Label { AutoSize = true, Text = "无明显缺码" });
                }
                else
                {
                    foreach (var s in lacks)
                    {
                        _kpiMissingFlow.Controls.Add(new Label
                        {
                            AutoSize = true,
                            Text = s,
                            Margin = new Padding(0, 0, 6, 2),
                            Font = new Font(Font.FontFamily, 8.5f, FontStyle.Regular),
                            ForeColor = Color.FromArgb(200, 60, 60)
                        });
                    }
                }
            }
        }

        #endregion

        #region 图表渲染（含标注）

        private void RenderCharts()
        {
            if (_sales == null || _sales.Count == 0)
            {
                _plotTrend.Model = null;
                _plotWarehouse.Model = null;
                _plotSize.Model = null;
                _plotColor.Model = null;
                return;
            }

            RenderTrendChart();
            RenderSizeChart();
            RenderColorChart();

            // 仓库分布交给库存页做详细展示，这里不强画假数据。
            _plotWarehouse.Model = new PlotModel { Title = "仓库占比（见库存页）" };
        }

        private void RenderTrendChart()
        {
            var model = new PlotModel
            {
                Title = $"近{_trendWindow}日销量",
                PlotMargins = new OxyThickness(50, 10, 10, 40)
            };

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

            var maxDate = _sales.Max(s => s.Date.Date);
            var from = maxDate.AddDays(-(_trendWindow - 1));
            var data = _sales
                .Where(s => s.Date.Date >= from && s.Date.Date <= maxDate)
                .GroupBy(s => s.Date.Date)
                .OrderBy(g => g.Key)
                .Select(g => new { Date = g.Key, Qty = g.Sum(x => x.Qty) })
                .ToList();

            var line = new LineSeries { MarkerType = MarkerType.Circle, MarkerSize = 3 };
            foreach (var d in data)
            {
                var x = DateTimeAxis.ToDouble(d.Date);
                line.Points.Add(new DataPoint(x, d.Qty));
            }
            model.Series.Add(line);

            // 数值标注
            foreach (var p in line.Points)
            {
                model.Annotations.Add(new TextAnnotation
                {
                    TextPosition = new DataPoint(p.X, p.Y),
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

        private void RenderSizeChart()
        {
            var bySize = _sales
                .Where(s => !string.IsNullOrWhiteSpace(s.Size))
                .GroupBy(s => s.Size)
                .OrderBy(g => g.Key)
                .Select(g => new { Size = g.Key, Qty = g.Sum(x => x.Qty) })
                .ToList();

            var model = new PlotModel
            {
                Title = "尺码销量",
                PlotMargins = new OxyThickness(80, 10, 40, 40)
            };

            var cat = new CategoryAxis { Position = AxisPosition.Left };
            var val = new LinearAxis { Position = AxisPosition.Bottom, Minimum = 0 };
            var bar = new BarSeries
            {
                LabelFormatString = "{0}",
                LabelPlacement = LabelPlacement.Outside,
                LabelMargin = 4
            };

            foreach (var item in bySize)
            {
                cat.Labels.Add(item.Size);
                bar.Items.Add(new BarItem(item.Qty));
            }

            model.Axes.Add(cat);
            model.Axes.Add(val);
            model.Series.Add(bar);

            _plotSize.Model = model;
        }

        private void RenderColorChart()
        {
            var byColor = _sales
                .Where(s => !string.IsNullOrWhiteSpace(s.Color))
                .GroupBy(s => s.Color)
                .OrderBy(g => g.Key)
                .Select(g => new { Color = g.Key, Qty = g.Sum(x => x.Qty) })
                .ToList();

            var model = new PlotModel
            {
                Title = "颜色销量",
                PlotMargins = new OxyThickness(80, 10, 40, 40)
            };

            var cat = new CategoryAxis { Position = AxisPosition.Left };
            var val = new LinearAxis { Position = AxisPosition.Bottom, Minimum = 0 };
            var bar = new BarSeries
            {
                LabelFormatString = "{0}",
                LabelPlacement = LabelPlacement.Outside,
                LabelMargin = 4
            };

            foreach (var item in byColor)
            {
                cat.Labels.Add(item.Color);
                bar.Items.Add(new BarItem(item.Qty));
            }

            model.Axes.Add(cat);
            model.Axes.Add(val);
            model.Series.Add(bar);

            _plotColor.Model = model;
        }

        #endregion

        #region 库存与价格

        private async Task LoadInventoryAndPriceAsync(string styleName)
        {
            if (string.IsNullOrWhiteSpace(styleName))
                return;

            // 库存
            try
            {
                await _inventoryPage.LoadInventoryAsync(styleName);
                var total = _inventoryPage.TotalAvailable();
                var offered = _inventoryPage.OfferedSizes();
                var zeros = _inventoryPage.ZeroSizes();
                UpdateKpiFromInventory(total, offered, zeros);
            }
            catch
            {
                // 不阻断
            }

            // 价格 & 定级
            try
            {
                await LoadPriceAsync(styleName);
            }
            catch
            {
                SetKpiValue(_kpiGrade, "—");
                SetKpiValue(_kpiMinPrice, "—");
                SetKpiValue(_kpiBreakeven, "—");
            }
        }

        private async Task LoadPriceAsync(string styleName)
        {
            if (string.IsNullOrWhiteSpace(styleName))
                return;

            var url = "http://192.168.40.97:8002/lookup?name=" + Uri.EscapeDataString(styleName);
            using var http = new System.Net.Http.HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

            var resp = await http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return;

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == System.Text.Json.JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var item = root[0];

                string? grade = item.TryGetProperty("grade", out var g) ? g.GetString() : null;
                string? minPrice = item.TryGetProperty("min_price_one", out var m) ? m.GetRawText() : null;
                string? breakeven = item.TryGetProperty("breakeven_one", out var b) ? b.GetRawText() : null;

                SetKpiValue(_kpiGrade, string.IsNullOrWhiteSpace(grade) ? "—" : grade);
                SetKpiValue(_kpiMinPrice, string.IsNullOrWhiteSpace(minPrice) ? "—" : minPrice);
                SetKpiValue(_kpiBreakeven, string.IsNullOrWhiteSpace(breakeven) ? "—" : breakeven);
            }
        }

        #endregion

        #region 导出

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

        #endregion
    }
}
