using System;
using System.Collections.Generic;
using System.Linq;

namespace StyleWatcherWin
{
    public static class Aggregations
    {
        // 销售记录用于聚合的基础结构
        public struct SalesItem
        {
            public DateTime Date;
            public string Size;
            public string Color;
            public int Qty;
        }

        /// <summary>
        /// 构建按日聚合的销量序列。
        /// 根据传入的 windowDays 截取最近 N 天的数据（N <= 0 则不过滤）。
        /// </summary>
        public static List<(DateTime day, int qty)> BuildDateSeries(IEnumerable<SalesItem> sales, int windowDays)
        {
            if (sales == null) return new List<(DateTime, int)>();

            var list = sales.ToList();
            if (list.Count == 0) return new List<(DateTime, int)>();

            var minDay = list.Min(x => x.Date.Date);
            var maxDay = list.Max(x => x.Date.Date);

            // 若指定窗口天数，则仅保留最近 windowDays 天
            if (windowDays > 0)
            {
                var from = maxDay.AddDays(1 - windowDays);
                if (from > minDay)
                {
                    minDay = from;
                }
            }

            // 按日期聚合销量
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

        /// <summary>
        /// 数字格式化为 K / M 简写。
        /// </summary>
        public static string FormatNumber(double v)
        {
            if (Math.Abs(v) >= 1_000_000d)
                return (v / 1_000_000d).ToString("0.##") + "M";
            if (Math.Abs(v) >= 1_000d)
                return (v / 1_000d).ToString("0.##") + "K";
            return v.ToString("0");
        }
    }
}
