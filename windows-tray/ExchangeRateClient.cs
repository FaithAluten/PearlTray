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
    public sealed class ExchangeRateQuote
    {
        public decimal CnyPerUsdt { get; set; }
        public DateTime Date { get; set; }
    }

    public sealed class ExchangeRateClient : IDisposable
    {
        private readonly HttpClient http;
        private const string DefaultFallback =
            "https://latest.currency-api.pages.dev/v1/currencies/usdt.json";

        public ExchangeRateClient()
            : this(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        {
        }

        public ExchangeRateClient(HttpMessageHandler handler)
        {
            // This client is separate from the authenticated ticker client.
            http = new HttpClient(handler);
            http.Timeout = TimeSpan.FromSeconds(10);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PearlTray/1.2");
        }

        public Task<ExchangeRateQuote> FetchAsync(CancellationToken cancellation)
        {
            return FetchAsync(AppSettings.DefaultExchangeRateUrl, cancellation);
        }

        public async Task<ExchangeRateQuote> FetchAsync(string apiUrl, CancellationToken cancellation)
        {
            Uri uri;
            if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
                !String.IsNullOrEmpty(uri.UserInfo) || !String.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("汇率接口 URL 无效。");
            // A custom source must not silently fall back to a different provider.
            string[] endpoints = String.Equals(apiUrl, AppSettings.DefaultExchangeRateUrl,
                StringComparison.Ordinal) ? new[] { apiUrl, DefaultFallback } : new[] { apiUrl };
            foreach (string endpoint in endpoints)
            {
                try
                {
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(10));
                        using (var response = await http.GetAsync(endpoint,
                            HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                        {
                            response.EnsureSuccessStatusCode();
                            return Parse(await TickerClient.ReadBodyAsync(response, timeout.Token)
                                .ConfigureAwait(false));
                        }
                    }
                }
                catch (Exception error)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (!(error is HttpRequestException) && !(error is OperationCanceledException) &&
                        !(error is InvalidDataException) && !(error is IOException)) throw;
                }
            }
            throw new InvalidDataException("汇率获取失败");
        }

        public static ExchangeRateQuote Parse(string json)
        {
            try
            {
                var root = new JavaScriptSerializer { MaxJsonLength = 65536 }
                    .DeserializeObject(json.TrimStart('\uFEFF')) as Dictionary<string, object>;
                object dateValue;
                object currencies;
                object rateValue;
                DateTime date;
                decimal rate;
                if (root == null || !root.TryGetValue("date", out dateValue) ||
                    !(dateValue is string) || !DateTime.TryParseExact((string)dateValue, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ||
                    date > DateTime.UtcNow.Date.AddDays(1) ||
                    !root.TryGetValue("usdt", out currencies) ||
                    !(currencies is Dictionary<string, object>) ||
                    !((Dictionary<string, object>)currencies).TryGetValue("cny", out rateValue) ||
                    (!(rateValue is decimal) && !(rateValue is double) &&
                        !(rateValue is int) && !(rateValue is long)) ||
                    !Decimal.TryParse(Convert.ToString(rateValue, CultureInfo.InvariantCulture),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out rate) || rate <= 0 || rate > 10000)
                    throw new InvalidDataException("汇率数据无效");
                return new ExchangeRateQuote { CnyPerUsdt = rate, Date = date };
            }
            catch (ArgumentException) { throw new InvalidDataException("汇率数据无效"); }
            catch (InvalidOperationException) { throw new InvalidDataException("汇率数据无效"); }
        }

        public static string ConvertToCny(string usdtPrice, decimal rate)
        {
            return ConvertToCny(usdtPrice, rate, false);
        }

        public static string ConvertToCny(string usdtPrice, decimal rate, bool preservePrecision)
        {
            if (rate <= 0) throw new ArgumentOutOfRangeException("rate");
            decimal amount = checked(Decimal.Parse(usdtPrice, CultureInfo.InvariantCulture) * rate);
            if (preservePrecision || (amount > 0 && amount < 0.01m))
                return amount.ToString("0.############################", CultureInfo.InvariantCulture);
            return Math.Round(amount, 2, MidpointRounding.AwayFromZero)
                .ToString("0.00", CultureInfo.InvariantCulture);
        }

        public void Dispose()
        {
            http.Dispose();
        }
    }
}
