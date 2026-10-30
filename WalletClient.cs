using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PearlTray
{
    public sealed class WalletClient : IDisposable
    {
        public const string BaseUrl = "https://blockbook.pearlresearch.ai/api/v2/address/";
        private const int MaximumResponseBytes = 256 * 1024;
        private readonly HttpClient http;

        public WalletClient()
            : this(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { }

        public WalletClient(HttpMessageHandler handler)
        {
            http = new HttpClient(handler);
            http.Timeout = TimeSpan.FromSeconds(20);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PearlTray/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }

        public async Task<decimal> FetchAsync(string wallet, CancellationToken cancellation)
        {
            if (String.IsNullOrWhiteSpace(wallet)) throw new ArgumentException("钱包地址为空。");
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                using (var response = await http.GetAsync(BaseUrl + Uri.EscapeDataString(wallet) +
                    "?details=basic", HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException("钱包接口暂不可用 (HTTP " +
                            (int)response.StatusCode + ")。");
                    if (response.Content.Headers.ContentLength > MaximumResponseBytes)
                        throw new InvalidDataException("钱包接口返回内容过大。");
                    return Parse(await TickerClient.ReadBodyAsync(response, timeout.Token, MaximumResponseBytes)
                        .ConfigureAwait(false));
                }
            }
        }

        public static decimal Parse(string json)
        {
            try
            {
                var root = new JavaScriptSerializer { MaxJsonLength = MaximumResponseBytes }
                    .DeserializeObject(json.TrimStart('\uFEFF')) as Dictionary<string, object>;
                object raw;
                decimal atomic;
                if (root == null || !root.TryGetValue("balance", out raw) ||
                    (!(raw is string) && !(raw is int) && !(raw is long) && !(raw is decimal)) ||
                    !Decimal.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture),
                        NumberStyles.None, CultureInfo.InvariantCulture, out atomic))
                    throw new InvalidDataException();
                // Blockbook reports confirmed balances in atomic units (10^8 per PRL).
                return atomic / 100000000m;
            }
            catch (Exception error)
            {
                if (!(error is ArgumentException) && !(error is InvalidOperationException) &&
                    !(error is InvalidDataException)) throw;
                throw new InvalidDataException("钱包接口未返回有效的余额。", error);
            }
        }

        public static string FormatBalance(decimal balance)
        {
            return Math.Round(balance, 2, MidpointRounding.AwayFromZero)
                .ToString("0.00", CultureInfo.InvariantCulture);
        }

        public void Dispose() { http.Dispose(); }
    }
}
