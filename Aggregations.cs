using System;

namespace StyleWatcherWin
{
    /// <summary>
    /// 公共汇总与格式化工具。
    /// 当前仅保留在界面中真实用到的功能。
    /// </summary>
    public static class Aggregations
    {
        /// <summary>
        /// 数字格式化：支持 K / M 简写，其他情况下输出整数。
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
