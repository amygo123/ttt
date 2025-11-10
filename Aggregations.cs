using System;
using System.Collections.Generic;
using System.Linq;

namespace StyleWatcherWin
{
    /// <summary>
    /// 聚合与格式化工具。
    /// - SalesItem：ResultForm 等用于图表/统计的轻量销售条目。
    /// - BuildDateSeries：基于 SalesItem 的按日汇总（用于趋势图、导出）。
    /// - FormatNumber：K/M 缩写格式化，用于 KPI 展示。
    /// 已彻底移除 MA7 / MovingAverage 相关逻辑。
    /// </summary>
    public static class Aggregations
    {
        /// <summary>
        /// 概览与图表示例使用的基础销售条目。
        /// 注意：这是轻量 DTO，不替代原始解析结构。
        /// </summary>
        public struct SalesItem
        {
            public DateTime Date;
            public string Size;
            public string Color;
            public int Qty;
        }

        /// <summary>
        /// 将 SalesItem 列表按天聚合为最近 windowDays 天（含末日）的时间序列。
        /// 若列表为空或 windowDays <= 0，则返回空列表。
        /// </summary>
        public static List<(DateTime day, int qty)> BuildDateSeries(IEnumerable<SalesItem> items, int windowDays)
        {
            var result = new List<(DateTime day, int qty)>();

            if (items == null)
                return result;

            var list = items.ToList();
            if (list.Count == 0 || windowDays <= 0)
                return result;

            // 以数据中的最大日期作为窗口结束日
            var maxDate = list.Max(x => x.Date.Date);
            var minDate = maxDate.AddDays(-(windowDays - 1));

            var byDate = list
                .Where(x => x.Date.Date >= minDate && x.Date.Date <= maxDate)
                .GroupBy(x => x.Date.Date)
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(s => s.Qty)
                );

            for (var d = minDate; d <= maxDate; d = d.AddDays(1))
            {
                byDate.TryGetValue(d, out var qty);
                result.Add((d, qty));
            }

            return result;
        }

        /// <summary>
        /// 数字格式化：
        /// - >= 1,000,000 显示为 xM
        /// - >= 1,000 显示为 xK
        /// - 否则显示整数
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
