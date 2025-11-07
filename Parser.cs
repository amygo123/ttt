using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace StyleWatcherWin
{
    /// <summary>
    /// 单条销售记录。
    /// </summary>
    public class SaleRecord
    {
        public DateTime Date { get; set; }
        public string Name { get; set; } = "";
        public string Size { get; set; } = "";
        public string Color { get; set; } = "";
        public int Qty { get; set; }
    }

    /// <summary>
    /// 解析后的整体结果。
    /// </summary>
    public class ParsedPayload
    {
        public string Title { get; set; } = "";
        public string StyleName { get; set; } = "";
        public List<SaleRecord> Records { get; } = new();
    }

    /// <summary>
    /// 文本解析器。
    /// 这里只实现当前 UI 实际用到的能力：从文本中抽取日期/款式/尺码/颜色/数量列表。
    /// </summary>
    public static class PayloadParser
    {
        // 示例行：2025-01-02 ABC123 36 黑色 3件
        private static readonly Regex LineRegex = new(
            @"^(?<date>20\d{2}-\d{2}-\d{2})\s+(?<name>\S+)\s+(?<size>\S+)\s+(?<color>\S+)\s*(?<qty>\d+)\s*件?$",
            RegexOptions.Compiled);

        public static ParsedPayload Parse(string raw)
        {
            var result = new ParsedPayload();
            if (string.IsNullOrWhiteSpace(raw))
                return result;

            var text = raw.Replace("\\r\\n", "\n").Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = text
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();

            foreach (var line in lines)
            {
                var m = LineRegex.Match(line);
                if (!m.Success)
                {
                    // 第一行作为标题/款名兜底
                    if (string.IsNullOrEmpty(result.Title))
                        result.Title = line;
                    continue;
                }

                if (!DateTime.TryParseExact(
                        m.Groups["date"].Value,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var dt))
                    continue;

                var name = m.Groups["name"].Value.Trim();
                var size = m.Groups["size"].Value.Trim();
                var color = m.Groups["color"].Value.Trim();
                var qtyText = m.Groups["qty"].Value.Trim();

                if (!int.TryParse(qtyText, out var qty))
                    continue;

                result.Records.Add(new SaleRecord
                {
                    Date = dt,
                    Name = name,
                    Size = size,
                    Color = color,
                    Qty = qty
                });
            }

            if (string.IsNullOrEmpty(result.StyleName) && result.Records.Count > 0)
            {
                result.StyleName = result.Records[0].Name;
            }

            return result;
        }
    }

    /// <summary>
    /// 兼容旧引用：StyleWatcherWin.Parser.Parse(...)
    /// </summary>
    public static class Parser
    {
        public static ParsedPayload Parse(string text) => PayloadParser.Parse(text);
    }
}
