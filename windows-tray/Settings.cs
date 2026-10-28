using System;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace PearlTray
{
    public sealed class AppSettings
    {
        public const string DefaultExchangeRateUrl =
            "https://cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@latest/v1/currencies/usdt.json";

        public int RefreshSeconds { get; set; }
        public string DisplayCurrency { get; set; }
        public int DecimalPlaces { get; set; }
        public string ExchangeRateUrl { get; set; }
        public string WalletAddress { get; set; }
        public string HashrateThresholds { get; set; }
        public bool WindowsNotifications { get; set; }
        public bool WebhookNotifications { get; set; }
        public WebhookSettings Webhook { get; set; }

        public AppSettings()
        {
            RefreshSeconds = 60;
            DisplayCurrency = "USDT";
            DecimalPlaces = 2;
            ExchangeRateUrl = DefaultExchangeRateUrl;
            WalletAddress = "";
            HashrateThresholds = "0";
            WindowsNotifications = true;
            Webhook = new WebhookSettings();
        }

        public void Validate()
        {
            HashrateMonitor.ParseThreshold(HashrateThresholds);
            if (Webhook == null) throw new ArgumentException("Webhook 配置无效。");
            if (WebhookNotifications) Webhook.Validate();
            Uri uri;
            if (RefreshSeconds < 5 || RefreshSeconds > 86400)
                throw new ArgumentException("刷新周期应为 5 到 86400 秒。");
            if (WalletAddress != null && (WalletAddress.Length > 256 ||
                WalletAddress.IndexOfAny(new[] { '\r', '\n', '\t', ' ', '/', '\\', '?', '#' }) >= 0))
                throw new ArgumentException("请输入有效的钱包地址，或留空关闭矿机查询。");
            if (DisplayCurrency != "USDT" && DisplayCurrency != "CNY")
                throw new ArgumentException("显示单位应为 USDT 或人民币。");
            if (DecimalPlaces < 0 || DecimalPlaces > 2)
                throw new ArgumentException("小数位数应为 0、1 或 2。");
            if (!Uri.TryCreate(ExchangeRateUrl, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
                !String.IsNullOrEmpty(uri.UserInfo) || !String.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("请输入有效的 HTTP 或 HTTPS 汇率接口 URL。");
        }
    }

    public sealed class StoredSettings
    {
        public int RefreshSeconds { get; set; }
        public string DisplayCurrency { get; set; }
        public int? DecimalPlaces { get; set; }
        public string ExchangeRateUrl { get; set; }
        public string WalletAddress { get; set; }
        public string HashrateThresholds { get; set; }
        public bool? WindowsNotifications { get; set; }
        public bool WebhookNotifications { get; set; }
        public WebhookSettings Webhook { get; set; }
        public string ProtectedWebhookPassword { get; set; }
    }

    public sealed class SettingsStore
    {
        private readonly string path;
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PearlTray.settings.v1");

        public SettingsStore(string path)
        {
            this.path = Path.GetFullPath(path);
        }

        public static string DefaultPath
        {
            get
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
            }
        }

        public AppSettings Load()
        {
            if (!File.Exists(path)) return null;
            var stored = new JavaScriptSerializer().Deserialize<StoredSettings>(
                File.ReadAllText(path, Encoding.UTF8));
            if (stored == null)
                throw new InvalidDataException("配置文件无效，请重新填写。");
            var settings = new AppSettings
            {
                RefreshSeconds = stored.RefreshSeconds > 0 && stored.RefreshSeconds < 5
                    ? 5 : stored.RefreshSeconds,
                DisplayCurrency = stored.DisplayCurrency ?? "USDT",
                DecimalPlaces = stored.DecimalPlaces ?? 2,
                ExchangeRateUrl = stored.ExchangeRateUrl ?? AppSettings.DefaultExchangeRateUrl,
                WalletAddress = stored.WalletAddress ?? "",
                HashrateThresholds = MigrateThreshold(stored.HashrateThresholds),
                WindowsNotifications = stored.WindowsNotifications ?? true,
                WebhookNotifications = stored.WebhookNotifications,
                Webhook = stored.Webhook ?? new WebhookSettings()
            };
            if (!String.IsNullOrEmpty(stored.ProtectedWebhookPassword))
                settings.Webhook.Password = Encoding.UTF8.GetString(ProtectedData.Unprotect(
                    Convert.FromBase64String(stored.ProtectedWebhookPassword), Entropy,
                    DataProtectionScope.CurrentUser));
            settings.Validate();
            return settings;
        }

        public void Save(AppSettings settings)
        {
            settings.Validate();
            var stored = new StoredSettings
            {
                RefreshSeconds = settings.RefreshSeconds,
                DisplayCurrency = settings.DisplayCurrency,
                DecimalPlaces = settings.DecimalPlaces,
                ExchangeRateUrl = settings.ExchangeRateUrl,
                WalletAddress = settings.WalletAddress,
                HashrateThresholds = settings.HashrateThresholds,
                WindowsNotifications = settings.WindowsNotifications,
                WebhookNotifications = settings.WebhookNotifications,
                Webhook = settings.Webhook.CopyWithoutPassword(),
                ProtectedWebhookPassword = Convert.ToBase64String(ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(settings.Webhook.Password ?? ""), Entropy,
                    DataProtectionScope.CurrentUser))
            };
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(stored),
                    new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static string MigrateThreshold(string value)
        {
            if (value == null) return "0";
            // Older versions stored one threshold per worker.
            if (value.Contains(","))
                return value.Split(',').Sum(entry => HashrateMonitor.ParseThreshold(entry))
                    .ToString(CultureInfo.InvariantCulture);
            return value;
        }
    }
}
