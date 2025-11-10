using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using ClosedXML.Excel;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Annotations;
using OxyPlot.Series;
using OxyPlot.WindowsForms;

namespace StyleWatcherWin
{
    public class ResultForm : Form
    {
        private readonly AppConfig _cfg;
        private readonly HttpClient _http = new HttpClient();

        // Header
        private readonly TextBox _input = new TextBox();
        private readonly Button _btnParse = new Button();
        private readonly Button _btnExport = new Button();

        // KPI 面板
        private readonly FlowLayoutPanel _kpiPanel = new FlowLayoutPanel();
        private Label _kpiSales7;
        private Label _kpiInv;
        private Label _kpiDoc;
        private Label _kpiGrade;
        private Label _kpiMinPrice;
        private Label _kpiBreakeven;
        private FlowLayoutPanel _kpiMissingFlow;

        // Tabs
        private readonly TabControl _tabs = new TabControl();

        // 概览图
        private readonly PlotView _plotTrend = new PlotView();
        private readonly PlotView _plotSize = new PlotView();
        private readonly PlotView _plotColor = new PlotView();
        private readonly PlotView _plotWarehouse = new PlotView(); // 预留库存占比

        // 明细
        private readonly DataGridView _grid = new DataGridView();
        private readonly BindingSource _binding = new BindingSource();

        // 库存页
        private readonly InventoryTabPage _inventoryPage;

        // 状态栏
        private readonly Label _status = new Label();

        // 数据
        private List<Aggregations.SalesItem> _sales = new List<Aggregations.SalesItem>();
        private int _trendWindow = 30;
        private string _currentStyleName = "";

        public ResultForm(AppConfig cfg)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _inventoryPage = new InventoryTabPage(_cfg);

            Text = _cfg.ui?.title ?? "随手查";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.Manual;

            if (_cfg.ui?.width > 0 && _cfg.ui?.height > 0)
            {
                Width = _cfg.ui.width;
                Height = _cfg.ui.height;
            }
            else
            {
                Width = 900;
                Height = 600;
            }

            BuildLayout();
        }

        #region Public API 给 TrayApp 调用

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

        public async void ApplyRawText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;

            try
            {
                _status.Text = "解析中...";
                await ReloadAsync(raw);
            }
            catch (Exception ex)
            {
                _status.Text = "解析失败: " + ex.Message;
            }
        }

        public void FocusInput()
        {
            if (!_input.IsDisposed)
            {
                _input.Focus();
                _input.SelectAll();
            }
        }

        public void ShowAndFocusCentered()
        {
            var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 768);
            Left = screen.Left + (screen.Width - Width) / 2;
            Top = screen.Top + (screen.Height - Height) / 2;
            Show();
            Activate();
            FocusInput();
        }

        public void ShowNoActivateAtCursor()
        {
            // 简化实现：在鼠标附近显示，但不搞复杂 Win32
            var p = Cursor.Position;
            Left = p.X - Width / 2;
            Top = p.Y - 40;
            if (Top < 0) Top = 0;
            if (Left < 0) Left = 0;

            Show();
            FocusInput();
        }

        #endregion

        #region 初始化布局

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));   // Header
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));   // KPI
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // Tabs
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));   // Status

            Controls.Add(root);

            BuildHeader(root);
            BuildKpis(root);
            BuildTabs(root);
            BuildStatus(root);
        }

        private void BuildHeader(TableLayoutPanel root)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));

            _input.Dock = DockStyle.Fill;
            _input.Margin = new Padding(8, 8, 4, 8);

            _btnParse.Text = "解析";
            _btnParse.Dock = DockStyle.Fill;
            _btnParse.Margin = new Padding(4, 8, 4, 8);
            _btnParse.Click += async (_, __) => await ReloadAsync(_input.Text);

            _btnExport.Text = "导出";
            _btnExport.Dock = DockStyle.Fill;
            _btnExport.Margin = new Padding(4, 8, 8, 8);
            _btnExport.Click += (_, __) => ExportToExcel();

            panel.Controls.Add(_input, 0, 0);
            panel.Controls.Add(_btnParse, 1, 0);
            panel.Controls.Add(_btnExport, 2, 0);

            root.Controls.Add(panel, 0, 0);
        }

        private void BuildKpis(TableLayoutPanel root)
        {
            _kpiPanel.Dock = DockStyle.Fill;
            _kpiPanel.FlowDirection = FlowDirection.LeftToRight;
            _kpiPanel.WrapContents = true;
            _kpiPanel.Padding = new Padding(8, 4, 8, 4);
            _kpiPanel.AutoScroll = false;

            _kpiSales7 = CreateKpiCard("近7日销量");
            _kpiInv = CreateKpiCard("可用库存总量");
            _kpiDoc = CreateKpiCard("库存天数");
            _kpiGrade = CreateKpiCard("定级");
            _kpiMinPrice = CreateKpiCard("最低价");
            _kpiBreakeven = CreateKpiCard("保本价");
            CreateMissingKpiCard("缺货尺码"); // 缺码 KPI 在最后

            root.Controls.Add(_kpiPanel, 0, 1);
        }

        private void BuildTabs(TableLayoutPanel root)
        {
            _tabs.Dock = DockStyle.Fill;

            // 概览 Tab
            var tabOverview = new TabPage("概览");
            tabOverview.Padding = new Padding(6);

            var overviewLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2
            };
            overviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));   // 趋势
            overviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));   // 直方图
            overviewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            overviewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            _plotTrend.Dock = DockStyle.Fill;
            _plotSize.Dock = DockStyle.Fill;
            _plotColor.Dock = DockStyle.Fill;
            _plotWarehouse.Dock = DockStyle.Fill;

            overviewLayout.Controls.Add(_plotTrend, 0, 0);
            overviewLayout.SetColumnSpan(_plotTrend, 2); // 趋势占上方整行

            overviewLayout.Controls.Add(_plotSize, 0, 1);
            overviewLayout.Controls.Add(_plotColor, 1, 1);

            tabOverview.Controls.Add(overviewLayout);

            // 明细 Tab
            var tabDetail = new TabPage("明细");
            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = true;
            _grid.AutoGenerateColumns = true;
            _grid.DataSource = _binding;
            tabDetail.Controls.Add(_grid);

            // 库存 Tab
            var tabInv = new TabPage("库存");
            _inventoryPage.Dock = DockStyle.Fill;
            tabInv.Controls.Add(_inventoryPage);

            _tabs.TabPages.Add(tabOverview);
            _tabs.TabPages.Add(tabDetail);
            _tabs.TabPages.Add(tabInv);

            root.Controls.Add(_tabs, 0, 2);
        }

        private void BuildStatus(TableLayoutPanel root)
        {
            _status.Dock = DockStyle.Fill;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Padding = new Padding(6, 0, 6, 0);
            _status.Font = new Font(Font.FontFamily, 8f);
            root.Controls.Add(_status, 0, 3);
        }

        #endregion

        #region KPI 工具

        private Label CreateKpiCard(string title)
        {
            var panel = new Panel
            {
                Width = 130,
                Height = 70,
                Margin = new Padding(4),
                Padding = new Padding(6),
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
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(Font.FontFamily, 11, FontStyle.Bold)
            };

            panel.Controls.Add(lblValue);
            panel.Controls.Add(lblTitle);
            _kpiPanel.Controls.Add(panel);

            return lblValue;
        }

        private void CreateMissingKpiCard(string title)
        {
            var panel = new Panel
            {
                Width = 220,
                Height = 70,
                Margin = new Padding(4),
                Padding = new Padding(6),
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblTitle = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 18,
                ForeColor = Color.DimGray
            };

            _kpiMissingFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = true
            };

            panel.Controls.Add(_kpiMissingFlow);
            panel.Controls.Add(lblTitle);
            _kpiPanel.Controls.Add(panel);
        }

        private static void SetKpiValue(Label target, string value)
        {
            if (target == null) return;
            target.Text = value ?? "—";
        }

        private void SetMissingSizes(IEnumerable<string> sizes)
        {
            _kpiMissingFlow.Controls.Clear();
            if (sizes == null) return;

            foreach (var s in sizes.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                var lbl = new Label
                {
                    Text = s,
                    AutoSize = true,
                    Margin = new Padding(2),
                    Padding = new Padding(4, 2, 4, 2),
                    BackColor = Color.FromArgb(240, 240, 240)
                };
                _kpiMissingFlow.Controls.Add(lbl);
            }
        }

        #endregion

        #region 数据加载 & 渲染

        private async Task ReloadAsync(string raw)
        {
            // 使用 Parser 解析原始文本（保持你原有行为）
            var parsed = Parser.Parse(raw);
            var rows = parsed.Records ?? new List<SaleRecord>();

            // 转为 Aggregations.SalesItem
            _sales = rows.Select(r => new Aggregations.SalesItem
            {
                Date = r.Date,
                Size = r.Size ?? "",
                Color = r.Color ?? "",
                Qty = r.Qty
            }).ToList();

            _currentStyleName = parsed.StyleName ?? parsed.Title ?? "";

            RenderGrid(rows);
            RenderCharts(_sales);
            UpdateKpisFromSales(_sales);

            _status.Text = $"共 {_sales.Count} 条销量明细";
        }

        private void RenderGrid(IEnumerable<SaleRecord> rows)
        {
            var list = rows.ToList();
            _binding.DataSource = list;
            _grid.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);
        }

        private static List<Aggregations.SalesItem> CleanSalesForVisuals(IEnumerable<Aggregations.SalesItem> src)
        {
            if (src == null) return new List<Aggregations.SalesItem>();

            return src
                .Where(s => s.Qty != 0)
                .Select(s => new Aggregations.SalesItem
                {
                    Date = s.Date.Date,
                    Size = s.Size ?? "",
                    Color = s.Color ?? "",
                    Qty = s.Qty
                })
                .ToList();
        }

        private void RenderCharts(List<Aggregations.SalesItem> salesItems)
        {
            var cleaned = CleanSalesForVisuals(salesItems);

            RenderTrendChart(cleaned);
            RenderSizeChart(cleaned);
            RenderColorChart(cleaned);
            // 仓库占比图由库存页/后续需求驱动，这里保留 _plotWarehouse 占位即可
        }

        private void RenderTrendChart(List<Aggregations.SalesItem> salesItems)
        {
            var series = Aggregations.BuildDateSeries(salesItems, _trendWindow);
            if (series.Count == 0)
            {
                _plotTrend.Model = null;
                return;
            }

            var model = new PlotModel
            {
                Title = $"近{_trendWindow}天销量"
            };

            var xAxis = new DateTimeAxis
            {
                Position = AxisPosition.Bottom,
                StringFormat = "MM-dd",
                IntervalType = DateTimeIntervalType.Days,
                MinorIntervalType = DateTimeIntervalType.Days
            };

            var yAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                Minimum = 0
            };

            var line = new LineSeries
            {
                MarkerType = MarkerType.Circle,
                MarkerSize = 3,
                MarkerStroke = OxyColors.White,
                MarkerFill = OxyColors.SkyBlue
            };

            foreach (var p in series)
            {
                line.Points.Add(new DataPoint(DateTimeAxis.ToDouble(p.day), p.qty));
            }

            model.Series.Add(line);

            // 数值标注：在每个点上方显示数量
            foreach (var p in series)
            {
                model.Annotations.Add(new TextAnnotation
                {
                    Text = p.qty.ToString(),
                    Position = new DataPoint(DateTimeAxis.ToDouble(p.day), p.qty),
                    TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
                    TextVerticalAlignment = OxyPlot.VerticalAlignment.Bottom,
                    Stroke = OxyColors.Undefined,
                    FontSize = 9
                });
            }

            model.Axes.Add(xAxis);
            model.Axes.Add(yAxis);

            _plotTrend.Model = model;
        }

        private void RenderSizeChart(List<Aggregations.SalesItem> salesItems)
        {
            var items = salesItems
                .Where(s => !string.IsNullOrWhiteSpace(s.Size))
                .GroupBy(s => s.Size)
                .Select(g => new { Key = g.Key, Qty = g.Sum(x => x.Qty) })
                .OrderByDescending(x => x.Qty)
                .ToList();

            if (items.Count == 0)
            {
                _plotSize.Model = null;
                return;
            }

            var model = new PlotModel
            {
                Title = "尺码销量",
                PlotMargins = new OxyThickness(80, 10, 40, 40) // 左标签 + 右侧数值留白
            };

            var catAxis = new CategoryAxis { Position = AxisPosition.Left };
            var valAxis = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Minimum = 0
            };

            var series = new BarSeries
            {
                LabelFormatString = "{0}",
                LabelPlacement = LabelPlacement.Outside,
                LabelMargin = 6
            };

            foreach (var it in items)
            {
                catAxis.Labels.Add(it.Key);
                series.Items.Add(new BarItem(it.Qty));
            }

            model.Axes.Add(catAxis);
            model.Axes.Add(valAxis);
            model.Series.Add(series);

            _plotSize.Model = model;
        }

        private void RenderColorChart(List<Aggregations.SalesItem> salesItems)
        {
            var items = salesItems
                .Where(s => !string.IsNullOrWhiteSpace(s.Color))
                .GroupBy(s => s.Color)
                .Select(g => new { Key = g.Key, Qty = g.Sum(x => x.Qty) })
                .OrderByDescending(x => x.Qty)
                .ToList();

            if (items.Count == 0)
            {
                _plotColor.Model = null;
                return;
            }

            var model = new PlotModel
            {
                Title = "颜色销量",
                PlotMargins = new OxyThickness(80, 10, 40, 40)
            };

            var catAxis = new CategoryAxis { Position = AxisPosition.Left };
            var valAxis = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Minimum = 0
            };

            var series = new BarSeries
            {
                LabelFormatString = "{0}",
                LabelPlacement = LabelPlacement.Outside,
                LabelMargin = 6
            };

            foreach (var it in items)
            {
                catAxis.Labels.Add(it.Key);
                series.Items.Add(new BarItem(it.Qty));
            }

            model.Axes.Add(catAxis);
            model.Axes.Add(valAxis);
            model.Series.Add(series);

            _plotColor.Model = model;
        }

        #endregion

        #region 导出

        private void ExportToExcel()
        {
            if (_sales == null || _sales.Count == 0)
                return;

            using var wb = new XLWorkbook();

            // 明细
            var ws1 = wb.AddWorksheet("明细");
            ws1.Cell(1, 1).Value = "日期";
            ws1.Cell(1, 2).Value = "尺码";
            ws1.Cell(1, 3).Value = "颜色";
            ws1.Cell(1, 4).Value = "数量";

            var r = 2;
            foreach (var s in _sales)
            {
                ws1.Cell(r, 1).Value = s.Date.ToString("yyyy-MM-dd");
                ws1.Cell(r, 2).Value = s.Size;
                ws1.Cell(r, 3).Value = s.Color;
                ws1.Cell(r, 4).Value = s.Qty;
                r++;
            }
            ws1.Columns().AdjustToContents();

            // 趋势（仅日期+数量）
            var ws2 = wb.AddWorksheet("趋势");
            ws2.Cell(1, 1).Value = "日期";
            ws2.Cell(1, 2).Value = "数量";

            var series = Aggregations.BuildDateSeries(_sales, _trendWindow);
            var rr = 2;
            foreach (var p in series)
            {
                ws2.Cell(rr, 1).Value = p.day.ToString("yyyy-MM-dd");
                ws2.Cell(rr, 2).Value = p.qty;
                rr++;
            }
            ws2.Columns().AdjustToContents();

            // 保存
            using var dlg = new SaveFileDialog
            {
                Filter = "Excel 文件|*.xlsx",
                FileName = $"{(_currentStyleName ?? "导出")}_{DateTime.Now:yyyyMMddHHmm}.xlsx"
            };

            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                wb.SaveAs(dlg.FileName);
                _status.Text = "已导出: " + dlg.FileName;
            }
        }

        #endregion
    }
}
