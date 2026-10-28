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
    public sealed class NetworkClient : IDisposable
    {
        public const string ApiUrl = "https://pearl.herominers.com/api/stats";
        private readonly HttpClient http;

        public NetworkClient()
            : this(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { }

        public NetworkClient(HttpMessageHandler handler)
        {
            http = new HttpClient(handler);
            http.Timeout = TimeSpan.FromSeconds(20);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PearlTray/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }

        public async Task<decimal> FetchAsync(CancellationToken cancellation)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                using (var response = await http.GetAsync(ApiUrl,
                    HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    return Parse(await TickerClient.ReadBodyAsync(response, timeout.Token)
                        .ConfigureAwait(false));
                }
            }
        }

        public static decimal Parse(string json)
        {
            try
            {
                var root = new JavaScriptSerializer { MaxJsonLength = 65536 }
                    .DeserializeObject(json.TrimStart('\uFEFF')) as Dictionary<string, object>;
                object network;
                object raw;
                decimal hashrate;
                if (root == null || !root.TryGetValue("network", out network) ||
                    !(network is Dictionary<string, object>) ||
                    !((Dictionary<string, object>)network).TryGetValue("networkHashps", out raw) ||
                    (!(raw is decimal) && !(raw is double) && !(raw is int) && !(raw is long)) ||
                    !Decimal.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out hashrate) || hashrate < 0)
                    throw new InvalidDataException();
                return hashrate;
            }
            catch (Exception error)
            {
                if (!(error is ArgumentException) && !(error is InvalidOperationException) &&
                    !(error is InvalidDataException)) throw;
                throw new InvalidDataException("Invalid network hashrate response.", error);
            }
        }

        public static string FormatHashrate(decimal hashrate)
        {
            string[] units = { "H/s", "KH/s", "MH/s", "GH/s", "TH/s", "PH/s", "EH/s" };
            int unit = 0;
            while (hashrate >= 1000m && unit < units.Length - 1)
            {
                hashrate /= 1000m;
                unit++;
            }
            return hashrate.ToString("0.00", CultureInfo.InvariantCulture) + " " + units[unit];
        }

        public void Dispose()
        {
            http.Dispose();
        }
    }
}
