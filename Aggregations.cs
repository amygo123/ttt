using System;
using System.Collections.Generic;
using System.Linq;

namespace StyleWatcherWin
{
    /// <summary>
    /// 聚合与格式化工具。
    /// 仅保留当前工程真实使用的能力：
    /// - BuildDateSeries：按天汇总销量，用于趋势图/导出；
    /// - FormatNumber：K/M 缩写格式化。
    /// 不再包含任何 MA7 / 移动平均 相关实现。
    /// </summary>
    public static class Aggregations
    {
        /// <summary>
        /// 将销售明细聚合为按天序列，窗口为最近 windowDays 天（含当天）。
        /// 若 records 为空，返回空列表。
        /// </summary>
        public static List<(DateTime day, int qty)> BuildDateSeries(IEnumerable<SaleRecord> records, int windowDays)
        {
            var result = new List<(DateTime day, int qty)>();

            if (records == null)
                return result;

            var list = records.ToList();
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
        /// 数字格式化：>=1,000 用 K，>=1,000,000 用 M，否则原值。
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
