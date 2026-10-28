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
    public sealed class AccountWorker
    {
        public string Name { get; set; }
        public List<AccountGpu> Gpus { get; set; }
    }

    public sealed class AccountGpu
    {
        public string Name { get; set; }
        public decimal Hashrate { get; set; }
    }

    public sealed class AccountClient : IDisposable
    {
        public const string BaseUrl = "https://pearlhash.xyz/api/account/";
        // Account responses include transaction history as well as worker data.
        private const int MaximumResponseBytes = 4 * 1024 * 1024;
        private readonly HttpClient http;

        public AccountClient()
            : this(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { }

        public AccountClient(HttpMessageHandler handler)
        {
            http = new HttpClient(handler);
            http.Timeout = TimeSpan.FromSeconds(20);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PearlTray/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }

        public async Task<List<AccountWorker>> FetchAsync(string wallet, CancellationToken cancellation)
        {
            if (String.IsNullOrWhiteSpace(wallet)) throw new ArgumentException("钱包地址为空。");
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                using (var response = await http.GetAsync(BaseUrl + Uri.EscapeDataString(wallet),
                    HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException("矿机接口暂不可用 (HTTP " +
                            (int)response.StatusCode + ")。");
                    if (response.Content.Headers.ContentLength > MaximumResponseBytes)
                        throw new InvalidDataException("矿机接口返回内容过大。");
                    return Parse(await TickerClient.ReadBodyAsync(response, timeout.Token, MaximumResponseBytes)
                        .ConfigureAwait(false));
                }
            }
        }

        public static List<AccountWorker> Parse(string json)
        {
            try
            {
                var root = new JavaScriptSerializer { MaxJsonLength = MaximumResponseBytes }
                    .DeserializeObject(json.TrimStart('\uFEFF')) as Dictionary<string, object>;
                object raw;
                if (root == null || !root.TryGetValue("connected_workers", out raw) ||
                    !(raw is object[])) throw new InvalidDataException();
                var workers = new List<AccountWorker>();
                foreach (object entry in (object[])raw)
                {
                    var worker = entry as Dictionary<string, object>;
                    if (worker == null) throw new InvalidDataException();
                    var result = new AccountWorker { Name = ReadName(worker, "worker_name"),
                        Gpus = new List<AccountGpu>() };
                    if (!worker.TryGetValue("gpu_info", out raw) || !(raw is object[]))
                        throw new InvalidDataException();
                    foreach (object gpuEntry in (object[])raw)
                    {
                        var gpu = gpuEntry as Dictionary<string, object>;
                        decimal hashrate;
                        if (gpu == null || !gpu.TryGetValue("hashrate", out raw) ||
                            (!(raw is decimal) && !(raw is double) && !(raw is int) && !(raw is long)) ||
                            !Decimal.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture),
                                NumberStyles.Float, CultureInfo.InvariantCulture, out hashrate) ||
                            hashrate < 0) throw new InvalidDataException();
                        result.Gpus.Add(new AccountGpu { Name = ReadName(gpu, "name"), Hashrate = hashrate });
                    }
                    workers.Add(result);
                }
                return workers;
            }
            catch (Exception error)
            {
                if (!(error is ArgumentException) && !(error is InvalidOperationException) &&
                    !(error is InvalidDataException)) throw;
                throw new InvalidDataException("矿机接口未返回有效的矿机和算力数据。", error);
            }
        }

        private static string ReadName(Dictionary<string, object> value, string key)
        {
            object raw;
            if (!value.TryGetValue(key, out raw) || !(raw is string) ||
                String.IsNullOrWhiteSpace((string)raw)) throw new InvalidDataException();
            return ((string)raw).Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        }

        public static string FormatHashrate(decimal hashrate)
        {
            return (hashrate / 1000000000000m).ToString("0.00", CultureInfo.InvariantCulture) + " TH/s";
        }

        public static string DisplayGpuName(string name)
        {
            const string prefix = "NVIDIA GeForce ";
            string trimmed = name.Trim();
            return trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                trimmed.Length > prefix.Length ? trimmed.Substring(prefix.Length).TrimStart() : trimmed;
        }

        public static List<string> DisplayLines(List<AccountWorker> workers)
        {
            var lines = new List<string>();
            foreach (var worker in workers)
            {
                lines.Add((workers.Count == 1 ? "" : "- ") + worker.Name);
                foreach (var gpu in worker.Gpus)
                    lines.Add((workers.Count == 1 ? "" : "    ") + DisplayGpuName(gpu.Name) + "  " +
                        FormatHashrate(gpu.Hashrate));
            }
            return lines;
        }

        public void Dispose() { http.Dispose(); }
    }
}
