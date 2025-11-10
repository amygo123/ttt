using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ClosedXML.Excel;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;

namespace StyleWatcherWin
{
    public class ResultForm : Form
    {
        private readonly AppConfig _cfg;
        private readonly TextBox _txtInput;
        private readonly Label _lblStatus;
        private readonly TabControl _tabs;
        private readonly DataGridView _grid;
        private readonly PlotView _plotTrend;
        private readonly InventoryTabPage _inventoryPage;

        private List<Aggregations.SalesItem> _sales = new List<Aggregations.SalesItem>();
        private int _trendWindow;
        private string _lastDisplayText = string.Empty;

        public ResultForm(AppConfig cfg)
        {
            _cfg = cfg ?? AppConfig.Load();

            Text = "StyleWatcher - 随手查";
            StartPosition = FormStartPosition.CenterScreen;
            Width = _cfg.window?.width > 0 ? _cfg.window.width : 1600;
            Height = _cfg.window?.height > 0 ? _cfg.window.height : 900;
            TopMost = _cfg.window?.alwaysOnTop ?? true;
            Font = new Font("Microsoft YaHei UI", _cfg.window?.fontSize > 0 ? _cfg.window.fontSize : 10f);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                ColumnCount = 1,
                RowCount = 3
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // 输入区域
            _txtInput = new TextBox
            {
                Multiline = true,
                Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", _cfg.window?.fontSize > 0 ? _cfg.window.fontSize : 11f)
            };
            _txtInput.KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && e.Modifiers == Keys.Control)
                {
                    e.SuppressKeyPress = true;
                    await ReloadAsync(_txtInput.Text);
                }
            };
            var pnlInput = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
            pnlInput.Controls.Add(_txtInput);
            root.Controls.Add(pnlInput, 0, 0);

            // 状态栏
            _lblStatus = new Label
            {
                Dock = DockStyle.Fill,
                Text = "就绪",
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0),
                ForeColor = Color.Gray
            };
            root.Controls.Add(_lblStatus, 0, 1);

            // Tabs
            _tabs = new TabControl
            {
                Dock = DockStyle.Fill
            };

            // 明细
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None
            };
            var tabDetail = new TabPage("明细");
            tabDetail.Controls.Add(_grid);
            _tabs.TabPages.Add(tabDetail);

            // 趋势
            _plotTrend = new PlotView
            {
                Dock = DockStyle.Fill
            };
            var tabTrend = new TabPage("趋势");
            tabTrend.Controls.Add(_plotTrend);
            _tabs.TabPages.Add(tabTrend);

            // 库存
            _inventoryPage = new InventoryTabPage(_cfg);
            _tabs.TabPages.Add(_inventoryPage);

            root.Controls.Add(_tabs, 0, 2);
            Controls.Add(root);

            // 默认趋势窗口
            _trendWindow = 7;
            if (_cfg.ui?.trendWindows != null && _cfg.ui.trendWindows.Length > 0)
                _trendWindow = Math.Max(1, _cfg.ui.trendWindows[0]);
        }

        public void SetLoading(string msg)
        {
            _lblStatus.Text = msg;
            _lblStatus.ForeColor = Color.DimGray;
            _lblStatus.Refresh();
        }

        public void ApplyRawText(string selection, string parsed)
        {
            _lastDisplayText = parsed ?? string.Empty;
            _txtInput.Text = _lastDisplayText;
            _txtInput.SelectionStart = _txtInput.TextLength;
            _txtInput.ScrollToCaret();
            _ = ReloadAsync(_lastDisplayText);
        }


        public void FocusInput()
        {
            if (_txtInput != null)
            {
                _txtInput.Focus();
                _txtInput.SelectAll();
            }
        }

        public void ShowAndFocusCentered()
        {
            StartPosition = FormStartPosition.Manual;
            var screen = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(
                Math.Max(0, (screen.Width - Width) / 2),
                Math.Max(0, (screen.Height - Height) / 2));
            Show();
            Activate();
            FocusInput();
        }

        public void ShowNoActivateAtCursor()
        {
            StartPosition = FormStartPosition.Manual;
            var p = Cursor.Position;
            Location = new Point(
                Math.Max(0, p.X - Width / 2),
                Math.Max(0, p.Y - 40));
            Show();
        }

        public System.Threading.Tasks.Task ReloadAsync(string? displayText = null)
        {
            displayText = displayText ?? _lastDisplayText;

            if (string.IsNullOrWhiteSpace(displayText))
            {
                _lblStatus.Text = "没有文本可解析。";
                _grid.DataSource = null;
                _plotTrend.Model = null;
                return;
            }

            try
            {
                _lblStatus.Text = "解析中...";
                _lblStatus.ForeColor = Color.DimGray;

                var parsed = Parser.Parse(displayText);
                if (parsed == null || parsed.Records == null || parsed.Records.Count == 0)
                {
                    _lblStatus.Text = "未解析出有效销售记录。";
                    _grid.DataSource = null;
                    _plotTrend.Model = null;
                    return;
                }

                // 明细表
                var list = parsed.Records.Select(r => new
                {
                    日期 = r.Date.ToString("yyyy-MM-dd"),
                    款号 = r.Name,
                    颜色 = r.Color,
                    尺码 = r.Size,
                    数量 = r.Qty
                }).ToList();
                _grid.DataSource = new BindingList<object>(list.Cast<object>().ToList());

                // 聚合为 SalesItem
                _sales = parsed.Records.Select(r => new Aggregations.SalesItem
                {
                    Date = r.Date.Date,
                    Size = r.Size,
                    Color = r.Color,
                    Qty = r.Qty
                }).ToList();

                RenderTrend();

                // 推断款号 & 加载库存
                var styleName = GuessStyleName(parsed);
                if (!string.IsNullOrWhiteSpace(styleName))
                {
                    _ = _inventoryPage.LoadInventoryAsync(styleName);
                    _lblStatus.Text = $"共 {parsed.Records.Count} 条记录，推断款号：{styleName}";
                }
                else
                {
                    _lblStatus.Text = $"共 {parsed.Records.Count} 条记录，未能推断款号。";
                }

                _lblStatus.ForeColor = Color.Gray;
                _lastDisplayText = displayText;
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "解析失败：" + ex.Message;
                _lblStatus.ForeColor = Color.Red;
            }
            return System.Threading.Tasks.Task.CompletedTask;
        
        }

        private static string GuessStyleName(ParsedPayload parsed)
        {
            if (parsed == null || parsed.Records == null)
                return string.Empty;

            var name = parsed.Records
                .Where(r => !string.IsNullOrWhiteSpace(r.Name))
                .GroupBy(r => r.Name.Trim())
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();

            return name ?? string.Empty;
        }

        private void RenderTrend()
        {
            if (_sales == null || _sales.Count == 0)
            {
                _plotTrend.Model = null;
                return;
            }

            var seriesData = Aggregations.BuildDateSeries(_sales, _trendWindow);
            if (seriesData.Count == 0)
            {
                _plotTrend.Model = null;
                return;
            }

            var model = new PlotModel { Title = "销量趋势" };

            var dateAxis = new DateTimeAxis
            {
                Position = AxisPosition.Bottom,
                StringFormat = "MM-dd",
                IntervalType = DateTimeIntervalType.Days,
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot
            };

            var valueAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                Minimum = 0,
                MajorGridlineStyle = LineStyle.Solid,
                MinorGridlineStyle = LineStyle.Dot
            };

            model.Axes.Add(dateAxis);
            model.Axes.Add(valueAxis);

            var line = new LineSeries
            {
                Title = "销量",
                MarkerType = MarkerType.Circle,
                MarkerSize = 3
            };

            foreach (var item in seriesData)
            {
                line.Points.Add(new DataPoint(DateTimeAxis.ToDouble(item.day), item.qty));
            }

            model.Series.Add(line);
            _plotTrend.Model = model;
        }

        public void ExportExcel()
        {
            try
            {
                if (_sales == null || _sales.Count == 0)
                {
                    MessageBox.Show("没有可导出的数据。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var fileName = $"StyleWatcher_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
                var path = Path.Combine(desktop, fileName);

                using var wb = new XLWorkbook();

                // 明细
                var ws1 = wb.AddWorksheet("明细");
                ws1.Cell(1, 1).Value = "日期";
                ws1.Cell(1, 2).Value = "款号";
                ws1.Cell(1, 3).Value = "颜色";
                ws1.Cell(1, 4).Value = "尺码";
                ws1.Cell(1, 5).Value = "数量";

                var row = 2;
                foreach (var s in _sales)
                {
                    ws1.Cell(row, 1).Value = s.Date.ToString("yyyy-MM-dd");
                    // 款号列可按解析结果补充，这里保持空/兼容
                    ws1.Cell(row, 3).Value = s.Color;
                    ws1.Cell(row, 4).Value = s.Size;
                    ws1.Cell(row, 5).Value = s.Qty;
                    row++;
                }
                ws1.Columns().AdjustToContents();

                // 趋势
                var ws2 = wb.AddWorksheet("趋势");
                ws2.Cell(1, 1).Value = "日期";
                ws2.Cell(1, 2).Value = "数量";
                var seriesData = Aggregations.BuildDateSeries(_sales, _trendWindow);
                var r = 2;
                foreach (var item in seriesData)
                {
                    ws2.Cell(r, 1).Value = item.day.ToString("yyyy-MM-dd");
                    ws2.Cell(r, 2).Value = item.qty;
                    r++;
                }
                ws2.Columns().AdjustToContents();

                // 口径说明
                var ws3 = wb.AddWorksheet("口径说明");
                ws3.Cell(1, 1).Value = "趋势窗口（天）";
                ws3.Cell(1, 2).Value = _trendWindow;
                ws3.Cell(2, 1).Value = "库存天数阈值";
                ws3.Cell(2, 2).Value = $"红<{_cfg.inventoryAlert?.docRed ?? 3}，黄<{_cfg.inventoryAlert?.docYellow ?? 7}";
                ws3.Cell(3, 1).Value = "销量基线天数";
                ws3.Cell(3, 2).Value = _cfg.inventoryAlert?.minSalesWindowDays ?? 7;
                ws3.Columns().AdjustToContents();

                wb.SaveAs(path);

                try
                {
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
                }
                catch { }

                MessageBox.Show("导出完成。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("导出失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
