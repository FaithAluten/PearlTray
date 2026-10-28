using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PearlTray
{
    public sealed class WebhookSettings
    {
        public string Url { get; set; }
        public string Method { get; set; }
        public string ContentType { get; set; }
        public string Headers { get; set; }
        public string Body { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }

        public WebhookSettings()
        {
            Url = "";
            Method = "GET";
            ContentType = "application/json";
            Headers = "{}";
            Body = "{\"message\":\"{{message}}\",\"title\":\"{{title}}\"}";
            Username = "";
            Password = "";
        }

        public WebhookSettings CopyWithoutPassword()
        {
            return new WebhookSettings { Url = Url, Method = Method, ContentType = ContentType,
                Headers = Headers, Body = Body, Username = Username, Password = "" };
        }

        public Dictionary<string, string> ParseHeaders()
        {
            try
            {
                var values = new JavaScriptSerializer().DeserializeObject(
                    String.IsNullOrWhiteSpace(Headers) ? "{}" : Headers) as Dictionary<string, object>;
                if (values == null) throw new ArgumentException();
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in values)
                {
                    if (!(pair.Value is string) || String.IsNullOrWhiteSpace(pair.Key) ||
                        pair.Key.IndexOfAny(new[] { '\r', '\n', ':' }) >= 0 ||
                        ((string)pair.Value).IndexOfAny(new[] { '\r', '\n' }) >= 0)
                        throw new ArgumentException();
                    result.Add(pair.Key, (string)pair.Value);
                }
                return result;
            }
            catch (Exception error)
            {
                if (!(error is ArgumentException) && !(error is InvalidOperationException)) throw;
                throw new ArgumentException("Webhook 请求头须为 JSON 字符串对象。");
            }
        }

        public void Validate()
        {
            Uri uri;
            string candidate = (Url ?? "").Replace("{{title}}", "title").Replace("{{message}}", "message");
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out uri) ||
                (uri.Scheme != "https" && uri.Scheme != "http") ||
                !String.IsNullOrEmpty(uri.UserInfo) || !String.IsNullOrEmpty(uri.Fragment))
                throw new ArgumentException("请输入有效的 HTTP 或 HTTPS Webhook URL。");
            if (Method != "GET" && Method != "POST")
                throw new ArgumentException("Webhook 方法须为 GET 或 POST。");
            MediaTypeHeaderValue type;
            if (!MediaTypeHeaderValue.TryParse(ContentType, out type))
                throw new ArgumentException("Webhook Content-Type 无效。");
            ParseHeaders();
            if (Method == "POST" && ContentType.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                try { new JavaScriptSerializer().DeserializeObject(RenderBody("测试", "测试")); }
                catch (ArgumentException) { throw new ArgumentException("Webhook 正文须为有效的 JSON 模板。"); }
            }
        }

        public string RenderBody(string title, string message)
        {
            if (ContentType.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var serializer = new JavaScriptSerializer();
                title = serializer.Serialize(title);
                message = serializer.Serialize(message);
                title = title.Substring(1, title.Length - 2);
                message = message.Substring(1, message.Length - 2);
            }
            return (Body ?? "").Replace("{{message}}", message).Replace("{{title}}", title);
        }
    }

    public sealed class WebhookClient : IDisposable
    {
        private readonly HttpClient http;
        public WebhookClient() : this(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { }
        public WebhookClient(HttpMessageHandler handler)
        {
            http = new HttpClient(handler);
            http.Timeout = TimeSpan.FromSeconds(20);
        }

        public async Task SendAsync(WebhookSettings settings, string title, string message,
            CancellationToken cancellation)
        {
            settings.Validate();
            string url = settings.Method == "GET" ? settings.Url
                .Replace("{{message}}", Uri.EscapeDataString(message))
                .Replace("{{title}}", Uri.EscapeDataString(title)) : settings.Url;
            using (var request = new HttpRequestMessage(new HttpMethod(settings.Method), url))
            {
                if (settings.Method == "POST")
                {
                    request.Content = new StringContent(settings.RenderBody(title, message), Encoding.UTF8);
                    request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(settings.ContentType);
                }
                foreach (var header in settings.ParseHeaders())
                {
                    if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                    {
                        if (request.Content == null)
                            throw new ArgumentException("GET 不支持正文请求头。");
                        request.Content.Headers.Remove(header.Key);
                        if (!request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value))
                            throw new ArgumentException("Webhook 请求头无效。");
                    }
                }
                if (!String.IsNullOrEmpty(settings.Username) && !String.IsNullOrEmpty(settings.Password))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                        Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.Username + ":" + settings.Password)));
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                        timeout.Token).ConfigureAwait(false))
                        if (!response.IsSuccessStatusCode)
                            throw new HttpRequestException("Webhook 通知失败 (HTTP " + (int)response.StatusCode + ")。");
                }
            }
        }
        public void Dispose() { http.Dispose(); }
    }
}
