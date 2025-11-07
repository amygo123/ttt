using System;
using System.Collections.Generic;
using System.Linq;

namespace StyleWatcherWin
{
    /// <summary>
    /// 聚合与格式化工具。
    /// - SalesItem: ResultForm 等处使用的轻量销售行模型。
    /// - BuildDateSeries: 按天汇总销量（基于 SalesItem），用于趋势图 / 导出。
    /// - FormatNumber: 用于 KPI 的 K/M 缩写显示。
    /// 已移除所有 MA7 / MovingAverage 相关实现。
    /// </summary>
    public static class Aggregations
    {
        /// <summary>
        /// 概览页与图表使用的基础销售条目。
        /// 注意：这是一个轻量 DTO，不替代原始 SaleRecord。
        /// </summary>
        public struct SalesItem
        {
            public DateTime Date;
            public string Size;
            public string Color;
            public int Qty;
        }

        /// <summary>
        /// 将 SalesItem 列表按天聚合为最近 windowDays 天的序列（含末日）。
        /// 若集合为空或 windowDays 非正，返回空列表。
        /// </summary>
        public static List<(DateTime day, int qty)> BuildDateSeries(IEnumerable<SalesItem> items, int windowDays)
        {
            var result = new List<(DateTime day, int qty)>();
            if (items == null)
                return result;

            var list = items.ToList();
            if (list.Count == 0 || windowDays <= 0)
                return result;

            var maxDate = list.Max(r => r.Date.Date);
            var minDate = maxDate.AddDays(-(windowDays - 1));

            var byDate = list
                .Where(r => r.Date.Date >= minDate && r.Date.Date <= maxDate)
                .GroupBy(r => r.Date.Date)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));

            for (var d = minDate; d <= maxDate; d = d.AddDays(1))
            {
                byDate.TryGetValue(d, out var qty);
                result.Add((d, qty));
            }

            return result;
        }

        /// <summary>
        /// 数字格式化：>=1,000 用 K，>=1,000,000 用 M，否则显示整数。
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
