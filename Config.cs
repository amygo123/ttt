using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StyleWatcherWin
{
    /// <summary>
    /// 全局配置（仅包含当前代码实际使用的字段）。
    /// </summary>
    public class AppConfig
    {
        // 主解析服务
        public string api_url { get; set; } = "http://47.111.189.27:8089/qrcode/saleVolumeParser";
        public string method { get; set; } = "POST";
        public string json_key { get; set; } = "code";
        public int timeout_seconds { get; set; } = 6;

        // 全局热键
        public string hotkey { get; set; } = "Alt+S";

        // 主窗体
        public WindowCfg window { get; set; } = new WindowCfg();

        // HTTP 头
        public Headers headers { get; set; } = new Headers();

        // 库存服务
        public InventoryCfg inventory { get; set; } = new InventoryCfg();

        // 趋势窗口配置
        public UiCfg ui { get; set; } = new UiCfg();

        // 库存预警
        public InventoryAlertCfg inventoryAlert { get; set; } = new InventoryAlertCfg();

        public class WindowCfg
        {
            public int width { get; set; } = 1600;
            public int height { get; set; } = 900;
            public int fontSize { get; set; } = 13;
            public bool alwaysOnTop { get; set; } = true;
        }

        public class Headers
        {
            [JsonPropertyName("Content-Type")]
            public string Content_Type { get; set; } = "application/json";
            public string Authorization { get; set; } = string.Empty;
        }

        public class InventoryCfg
        {
            /// <summary>库存查询基础地址，ResultForm/InventoryTabPage 会在此基础上拼接款号。</summary>
            public string url_base { get; set; } = "http://192.168.40.97:8001/inventory";
            public int timeout_seconds { get; set; } = 6;
        }

        public class UiCfg
        {
            /// <summary>趋势窗口（天数选项）。</summary>
            public int[] trendWindows { get; set; } = new[] { 7, 14, 30 };
        }

        public class InventoryAlertCfg
        {
            public double docRed { get; set; } = 3;
            public double docYellow { get; set; } = 7;
            public int minSalesWindowDays { get; set; } = 7;
        }

        public static string ConfigPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(
                        json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (cfg != null) return cfg;
                }
            }
            catch
            {
                // ignore
            }

            var def = new AppConfig();
            Save(def);
            return def;
        }

        public static void Save(AppConfig cfg)
        {
            try
            {
                var json = JsonSerializer.Serialize(
                    cfg,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch
            {
                // 写失败不影响主流程
            }
        }
    }

    /// <summary>
    /// 与解析服务通讯的唯一入口（TrayApp 调用，ResultForm 不直接访问解析服务）。
    /// </summary>
    public static class ApiHelper
    {
        public static async System.Threading.Tasks.Task<string> QueryAsync(AppConfig cfg, string text)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (string.IsNullOrWhiteSpace(cfg.api_url))
                throw new InvalidOperationException("未配置 api_url");

            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(1, cfg.timeout_seconds))
            };

            using var req = new HttpRequestMessage(
                new HttpMethod(string.IsNullOrWhiteSpace(cfg.method) ? "POST" : cfg.method),
                cfg.api_url);

            var contentType = cfg.headers?.Content_Type;
            if (string.IsNullOrWhiteSpace(contentType))
                contentType = "application/json";

            var payload = $"{{\"{cfg.json_key}\":\"{Escape(text)}\"}}";
            req.Content = new StringContent(payload, Encoding.UTF8, contentType);

            if (!string.IsNullOrWhiteSpace(cfg.headers?.Authorization))
            {
                req.Headers.TryAddWithoutValidation("Authorization", cfg.headers.Authorization);
            }

            var resp = await http.SendAsync(req);
            var raw = await resp.Content.ReadAsStringAsync();

            // 如果返回 JSON 且存在 msg 字段，则优先取 msg
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("msg", out var msgEl))
                {
                    var v = msgEl.ToString();
                    if (!string.IsNullOrEmpty(v))
                        return v;
                }
            }
            catch
            {
                // ignore parse error
            }

            return raw;
        }

        private static string Escape(string? s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\r", "\\r")
                    .Replace("\n", "\\n");
        }
    }
}
