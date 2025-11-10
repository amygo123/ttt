using System;
using System.Collections.Generic;
using System.Linq;

namespace StyleWatcherWin
{
    public static class Aggregations
    {
        public struct SalesItem
        {
            public DateTime Date;
            public string Size;
            public string Color;
            public int Qty;
        }

        /// <summary>
        /// 构建按日聚合的销量序列。
        /// windowDays > 0 时，仅保留最近 windowDays 天的数据。
        /// </summary>
        public static List<(DateTime day, int qty)> BuildDateSeries(IEnumerable<SalesItem> sales, int windowDays)
        {
            if (sales == null)
                return new List<(DateTime, int)>();

            var list = sales.ToList();
            if (list.Count == 0)
                return new List<(DateTime, int)>();

            var minDay = list.Min(x => x.Date.Date);
            var maxDay = list.Max(x => x.Date.Date);

            if (windowDays > 0)
            {
                var from = maxDay.AddDays(1 - windowDays);
                if (from > minDay)
                    minDay = from;
            }

            var byDay = list
                .GroupBy(x => x.Date.Date)
                .ToDictionary(g => g.Key, g => g.Sum(z => z.Qty));

            var result = new List<(DateTime, int)>();
            for (var d = minDay; d <= maxDay; d = d.AddDays(1))
            {
                byDay.TryGetValue(d, out var qty);
                result.Add((d, qty));
            }

            return result;
        }

        /// <summary>
        /// 数字格式化为 K / M 简写。
        /// </summary>
        public static string FormatNumber(double v)
        {
            var av = Math.Abs(v);
            if (av >= 1_000_000d)
                return (v / 1_000_000d).ToString("0.##") + "M";
            if (av >= 1_000d)
                return (v / 1_000d).ToString("0.##") + "K";
            return v.ToString("0");
        }
    }
}
