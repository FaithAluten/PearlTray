using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PearlTray
{
    public sealed class TickerResult
    {
        public string Last { get; set; }
        public bool IsStale { get; set; }
    }

    public sealed class TickerClient : IDisposable
    {
        public const string PublicTickerUrl =
            "https://pool.kryptex.com/api/v1/coin/prl/price/chart?time_range=month";
        public const string BrowserUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
        private readonly HttpClient http;
        private const int MaximumResponseBytes = 65536;

        public TickerClient()
            : this(new CurlTickerHandler())
        {
        }

        public TickerClient(HttpMessageHandler handler)
        {
            http = new HttpClient(handler);
            http.Timeout = TimeSpan.FromSeconds(20);
            http.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);
        }

        public async Task<TickerResult> FetchAsync(AppSettings settings, CancellationToken cancellation)
        {
            settings.Validate();
            using (var request = new HttpRequestMessage(HttpMethod.Get, PublicTickerUrl))
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                // Keep a single timeout around headers and the complete streamed body.
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    using (var response = await http.SendAsync(request,
                        HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                            throw new HttpRequestException(DescribeStatus(response.StatusCode));
                        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
                            throw new InvalidDataException("接口返回内容过大。");
                        var result = new TickerResult
                        {
                            Last = ParseLast(await ReadBodyAsync(response, timeout.Token)
                                .ConfigureAwait(false))
                        };
                        IEnumerable<string> state;
                        if (response.Headers.TryGetValues("X-Upstream-State", out state))
                            foreach (string value in state)
                                if (String.Equals(value, "refresh-failed",
                                    StringComparison.OrdinalIgnoreCase)) result.IsStale = true;
                        return result;
                    }
                }
            }
        }

        internal static async Task<string> ReadBodyAsync(HttpResponseMessage response,
            CancellationToken cancellation, int maximumResponseBytes = MaximumResponseBytes)
        {
            // Some .NET Framework response streams ignore ReadAsync cancellation.
            using (cancellation.Register(response.Dispose))
            {
                try
                {
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var body = new MemoryStream())
                    {
                        var buffer = new byte[4096];
                        int read;
                        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length,
                            cancellation).ConfigureAwait(false)) != 0)
                        {
                            if (body.Length + read > maximumResponseBytes)
                                throw new InvalidDataException("接口返回内容过大。");
                            body.Write(buffer, 0, read);
                        }
                        return Encoding.UTF8.GetString(body.ToArray());
                    }
                }
                catch
                {
                    cancellation.ThrowIfCancellationRequested();
                    throw;
                }
            }
        }

        public static string ParseLast(string json)
        {
            object[] points;
            try
            {
                points = new JavaScriptSerializer { MaxJsonLength = MaximumResponseBytes }
                    .DeserializeObject(json.TrimStart('\uFEFF')) as object[];
            }
            catch (Exception error)
            {
                if (!(error is ArgumentException) && !(error is InvalidOperationException)) throw;
                throw new InvalidDataException("接口未返回有效的 JSON。");
            }
            object raw;
            // Match the price page: its default month chart displays the final point.
            var value = points == null || points.Length == 0 ? null :
                points[points.Length - 1] as Dictionary<string, object>;
            if (value == null || !value.TryGetValue("price", out raw) || raw == null ||
                (!(raw is string) && !(raw is decimal) && !(raw is double) &&
                 !(raw is int) && !(raw is long)))
                throw new InvalidDataException("接口缺少有效的 price 数值。");
            string text = Convert.ToString(raw, CultureInfo.InvariantCulture).Trim();
            decimal price;
            if (text.Length == 0 || text.Length > 28 ||
                !Decimal.TryParse(text, NumberStyles.AllowDecimalPoint |
                    NumberStyles.AllowLeadingSign | NumberStyles.AllowExponent,
                    CultureInfo.InvariantCulture, out price) || price < 0 ||
                (price == 0 && HasNonzeroDigit(text)))
                throw new InvalidDataException("接口中的 price 不是有效的非负价格。");
            return price.ToString("0.############################", CultureInfo.InvariantCulture);
        }

        private static bool HasNonzeroDigit(string text)
        {
            int exponent = text.IndexOfAny(new[] { 'e', 'E' });
            string significand = exponent < 0 ? text : text.Substring(0, exponent);
            foreach (char character in significand)
                if (character >= '1' && character <= '9') return true;
            return false;
        }

        private static string DescribeStatus(HttpStatusCode status)
        {
            int code = (int)status;
            if (code == 401 || code == 403) return "接口访问被拒绝 (HTTP " + code + ")，将在下个周期重试。";
            if (code >= 300 && code < 400) return "接口返回重定向，将在下个周期重试。";
            if (code == 429) return "请求过于频繁 (HTTP 429)，请增大刷新周期。";
            return "接口暂不可用 (HTTP " + code + ")。";
        }

        public void Dispose()
        {
            http.Dispose();
        }
    }

    public sealed class CurlTickerHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            string curlPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe");
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = curlPath,
                    Arguments = "--silent --show-error --max-time 20 --connect-timeout 10 " +
                        "--max-filesize 65536 --proto \"=http,https\" " +
                        "--user-agent \"" + TickerClient.BrowserUserAgent + "\" " +
                        "--header \"Accept: application/json\" --write-out \"\\n%{http_code}\" " +
                        "\"" + request.RequestUri.AbsoluteUri.Replace("\"", "%22") + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                try { process.Start(); }
                catch (Win32Exception)
                {
                    throw new HttpRequestException("请求失败，无法启动系统 curl.exe。");
                }
                using (cancellation.Register(delegate { Stop(process); }))
                {
                    try
                    {
                        Task<string> output = ReadOutputAsync(process.StandardOutput.BaseStream,
                            process, 65552, cancellation);
                        Task<string> errors = ReadOutputAsync(process.StandardError.BaseStream,
                            process, 8192, cancellation);
                        await Task.WhenAll(output, errors).ConfigureAwait(false);
                        process.WaitForExit();
                        cancellation.ThrowIfCancellationRequested();
                        if (process.ExitCode == 63)
                            throw new InvalidDataException("接口返回内容过大。");
                        if (process.ExitCode != 0)
                            throw new HttpRequestException("请求失败 (curl " + process.ExitCode +
                                ")，将在下个周期重试。");
                        string text = output.Result;
                        int separator = text.LastIndexOf('\n');
                        int status;
                        if (separator < 0 || !Int32.TryParse(text.Substring(separator + 1),
                            out status) || status < 100 || status > 599)
                            throw new HttpRequestException("接口未返回有效的 HTTP 状态。");
                        return new HttpResponseMessage((HttpStatusCode)status)
                        {
                            Content = new StringContent(text.Substring(0, separator), Encoding.UTF8,
                                "application/json")
                        };
                    }
                    catch
                    {
                        Stop(process);
                        cancellation.ThrowIfCancellationRequested();
                        throw;
                    }
                }
            }
        }

        private static async Task<string> ReadOutputAsync(Stream stream, Process process,
            int limit, CancellationToken cancellation)
        {
            using (var body = new MemoryStream())
            {
                var buffer = new byte[4096];
                int read;
                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellation)
                    .ConfigureAwait(false)) != 0)
                {
                    if (body.Length + read > limit)
                    {
                        Stop(process);
                        throw new InvalidDataException("接口返回内容过大。");
                    }
                    body.Write(buffer, 0, read);
                }
                return Encoding.UTF8.GetString(body.ToArray());
            }
        }

        private static void Stop(Process process)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
        }
    }
}
