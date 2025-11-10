using System;
using System.Collections.Generic;
using System.Linq;

namespace StyleWatcherWin
{
    public static class Aggregations
    {
        // —— 数据模型 —— //
        public struct SalesItem
        {
            public DateTime Date;
            public string Size;
            public string Color;
            public int Qty;
        }

        // —— 构建按日聚合序列 —— //
        public static List<(DateTime day, int qty)> BuildDateSeries(IEnumerable<SalesItem> sales, int windowDays)
        {
            var list = sales.ToList();
            if (list.Count == 0) return new List<(DateTime, int)>();

            var minDay = list.Min(x => x.Date.Date);
            var maxDay = list.Max(x => x.Date.Date);

            // 若指定窗口，则只取最近 windowDays 天
            if (windowDays > 0)
            {
                var from = maxDay.AddDays(1 - windowDays);
                if (from > minDay) minDay = from;
            }

            var dict = list
                .GroupBy(x => x.Date.Date)
                .ToDictionary(g => g.Key, g => g.Sum(z => z.Qty));

            var result = new List<(DateTime, int)>();
            for (var day = minDay; day <= maxDay; day = day.AddDays(1))
            {
                dict.TryGetValue(day, out var qty);
                result.Add((day, qty));
            }
            return result;
        }

        // —— 数字格式化（K/M） —— //
        public static string FormatNumber(double v)
        {
            if (Math.Abs(v) >= 1_000_000) return (v / 1_000_000d).ToString("0.##") + "M";
            if (Math.Abs(v) >= 1_000) return (v / 1_000d).ToString("0.##") + "K";
            return v.ToString("0");
        }
    }
}
