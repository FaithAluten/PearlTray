using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PearlTray;

internal static class Tests
{
    private static int passed;
    private static int failed;
    private static readonly string Artifacts = Path.GetFullPath(
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "artifacts"));

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs error)
        {
            failed++;
            Console.WriteLine("FAIL UI exception: " + error.Exception.GetType().FullName +
                ": " + error.Exception.Message + "\n" + error.Exception.StackTrace);
        };
        if (args.Length == 1 && args[0] == "--live-ticker")
        {
            using (var client = new TickerClient())
                Console.WriteLine("PEARL/USDT: " + client.FetchAsync(new AppSettings(),
                    CancellationToken.None).GetAwaiter().GetResult().Last);
            return 0;
        }
        if (args.Length == 1 && args[0] == "--live-network")
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            using (var client = new NetworkClient())
                Console.WriteLine("Network hashrate: " + NetworkClient.FormatHashrate(
                    client.FetchAsync(CancellationToken.None).GetAwaiter().GetResult()));
            return 0;
        }
        if (args.Length == 1 && args[0] == "--live-rate")
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            using (var client = new ExchangeRateClient())
            {
                var quote = client.FetchAsync(CancellationToken.None).GetAwaiter().GetResult();
                Console.WriteLine("USDT/CNY: {0}; data date: {1:yyyy-MM-dd}", quote.CnyPerUsdt, quote.Date);
            }
            return 0;
        }
        Directory.CreateDirectory(Artifacts);
        if (args.Length == 1 && args[0] == "--menu-scroll")
        {
            Check("Long worker menus scroll while keeping commands visible", WorkerMenuScrolling);
            return failed == 0 ? 0 : 1;
        }
        if (args.Length == 1 && args[0] == "--settings-summary")
        {
            Check("Live hashrate summary stays compact and shows complete units", SettingsSummary);
            return failed == 0 ? 0 : 1;
        }
        if (args.Length == 1 && args[0] == "--preview-settings")
        {
            using (var form = new SettingsForm(ValidSettings(),
                new SettingsStore(Path.Combine(Artifacts, "preview-settings.json")), false))
                form.ShowDialog();
            return 0;
        }
        if (args.Length == 1 && args[0] == "--render-settings")
        {
            using (var form = new SettingsForm(ValidSettings(),
                new SettingsStore(Path.Combine(Artifacts, "preview-settings.json")), false))
            {
                form.Show();
                Application.DoEvents();
                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(Artifacts, "settings-preview.png"), ImageFormat.Png);
                }
                form.Close();
            }
            return 0;
        }
        if (args.Length == 2 && args[0] == "--compare-font")
        {
            ExportFontComparison(args[1]);
            return 0;
        }
        Check("Reads last, not high", delegate
        {
            Equal("1.76", TickerClient.ParseLast(
                "{\"id\":\"pearlusdt\",\"high\":\"1.93\",\"last\":\"1.76\"}"));
        });
        Check("Accepts numeric JSON", delegate { Equal("1.93", TickerClient.ParseLast("{\"last\":1.93}")); });
        Check("Reads public ticker.last instead of bid, ask or top-level last", delegate
        {
            Equal("1.65", TickerClient.ParseLast(
                "{\"at\":\"1793167251\",\"last\":\"99\",\"ticker\":{\"buy\":\"1.66\"," +
                "\"sell\":\"1.64\",\"high\":\"1.79\",\"last\":\"1.65\"}}"));
            foreach (string json in new[] { "{\"ticker\":null,\"last\":\"99\"}",
                "{\"ticker\":[],\"last\":\"99\"}", "{\"ticker\":{\"high\":\"1.79\"}}" })
                Throws<InvalidDataException>(delegate { TickerClient.ParseLast(json); });
        });
        Check("Default settings require no credentials", delegate { new AppSettings().Validate(); });
        Check("Default settings stay beside the executable regardless of working directory", delegate
        {
            string originalDirectory = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = Artifacts;
                Equal(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json"),
                    SettingsStore.DefaultPath);
            }
            finally { Environment.CurrentDirectory = originalDirectory; }
        });
        Check("Preserves meaningful precision", delegate
        {
            Equal("0.00001234", TickerClient.ParseLast("{\"last\":\"0.0000123400\"}"));
        });
        Check("Accepts zero", delegate { Equal("0", TickerClient.ParseLast("{\"last\":\"0\"}")); });
        Check("Accepts a UTF-8 BOM", delegate { Equal("1.76", TickerClient.ParseLast("\uFEFF{\"last\":\"1.76\"}")); });
        Check("Accepts exponent", delegate { Equal("0.0001", TickerClient.ParseLast("{\"last\":\"1e-4\"}")); });
        string[] bad = {
            "not json", "{\"high\":\"1.93\"}", "{\"last\":null}", "{\"last\":true}",
            "{\"last\":{}}", "{\"last\":[]}", "{\"last\":\"NaN\"}", "{\"last\":\"-1\"}",
            "{\"last\":\"1,234\"}", "{\"last\":\"1e-29\"}", "{\"last\":\"\"}",
            "{\"last\":\"1.76\"}%"
        };
        foreach (string input in bad)
        {
            string sample = input;
            Check("Rejects invalid payload " + sample, delegate
            {
                Throws<InvalidDataException>(delegate { TickerClient.ParseLast(sample); });
            });
        }
        Check("Defaults to 60 seconds", delegate { Equal(60, new AppSettings().RefreshSeconds); });
        Check("Defaults to USDT", delegate { Equal("USDT", new AppSettings().DisplayCurrency); });
        Check("Tray decimal places default and validation", delegate
        {
            Equal(2, new AppSettings().DecimalPlaces);
            foreach (int places in new[] { -1, 3 })
            {
                var settings = ValidSettings();
                settings.DecimalPlaces = places;
                Throws<ArgumentException>(settings.Validate);
                Throws<ArgumentOutOfRangeException>(delegate { TrayIconRenderer.Compact("1.76", places); });
            }
        });
        Check("Tray precision rounds and keeps trailing zeros", delegate
        {
            Equal("2", TrayIconRenderer.Compact("1.76", 0));
            Equal("1.8", TrayIconRenderer.Compact("1.76", 1));
            Equal("1.76", TrayIconRenderer.Compact("1.76", 2));
            Equal("1.80", TrayIconRenderer.Compact("1.8", 2));
            Equal("0", TrayIconRenderer.Compact("0.004", 0));
            Equal("0.0", TrayIconRenderer.Compact("0.004", 1));
            Equal("0.00", TrayIconRenderer.Compact("0.004", 2));
            Equal("1.01", TrayIconRenderer.Compact("1.005", 2));
            Equal("1.3K", TrayIconRenderer.Compact("1250", 1));
            Equal("1.00M", TrayIconRenderer.Compact("1000000", 2));
            Equal("1B", TrayIconRenderer.Compact("1000000000", 0));
            Equal("1.2", TrayIconRenderer.Compact(
                ExchangeRateClient.ConvertToCny("1.249", 1m, true), 1));
        });
        Check("Rejects unsupported currency", delegate
        {
            var settings = ValidSettings(); settings.DisplayCurrency = "USD";
            Throws<ArgumentException>(settings.Validate);
        });
        Check("Old settings ignore retired credentials and retain preferences", delegate
        {
            string path = Path.Combine(Artifacts, "old-settings.json");
            var store = new SettingsStore(path);
            store.Save(ValidSettings());
            var json = new System.Web.Script.Serialization.JavaScriptSerializer();
            var values = json.Deserialize<System.Collections.Generic.Dictionary<string, object>>(
                File.ReadAllText(path));
            values.Remove("DisplayCurrency");
            values.Remove("ExchangeRateUrl");
            values.Remove("WalletAddress");
            values.Remove("DecimalPlaces");
            values.Remove("HashrateThresholds");
            values.Remove("ThresholdWorkerNames");
            values.Remove("WindowsNotifications");
            values.Remove("WebhookNotifications");
            values.Remove("Webhook");
            values.Remove("ProtectedWebhookPassword");
            values["ApiUrl"] = "https://old.example/ticker";
            values["ProtectedToken"] = "retired-token-does-not-need-decryption";
            values["RefreshSeconds"] = 1;
            File.WriteAllText(path, json.Serialize(values));
            Equal("USDT", store.Load().DisplayCurrency);
            Equal(5, store.Load().RefreshSeconds);
            Equal(AppSettings.DefaultExchangeRateUrl, store.Load().ExchangeRateUrl);
            Equal("", store.Load().WalletAddress);
            Equal(2, store.Load().DecimalPlaces);
            Equal("0", store.Load().HashrateThresholds);
            Equal(true, store.Load().WindowsNotifications);
            Equal(false, store.Load().WebhookNotifications);
            Equal("GET", store.Load().Webhook.Method);
            File.Delete(path);
        });
        Check("Validates editable exchange rate URL", delegate
        {
            foreach (string url in new[] { "", "file:///c:/rate.json", "https://user:pass@example.test/rate",
                "https://example.test/rate#fragment" })
            {
                var settings = ValidSettings();
                settings.ExchangeRateUrl = url;
                Throws<ArgumentException>(settings.Validate);
            }
        });
        Check("Rejects polling faster than five seconds", delegate
        {
            foreach (int interval in new[] { 1, 2, 3, 4 })
            {
                var settings = ValidSettings(); settings.RefreshSeconds = interval;
                Throws<ArgumentException>(settings.Validate);
            }
        });
        Check("Rejects zero interval", delegate
        {
            var settings = ValidSettings(); settings.RefreshSeconds = 0;
            Throws<ArgumentException>(settings.Validate);
        });
        Check("Credential-free settings round trip and atomic update", delegate
        {
            string path = Path.Combine(Artifacts, "settings-test.json");
            var store = new SettingsStore(path);
            var settings = ValidSettings();
            store.Save(settings);
            Equal(60, store.Load().RefreshSeconds);
            if (File.ReadAllText(path).Contains("ProtectedToken") ||
                File.ReadAllText(path).Contains("ApiUrl"))
                throw new Exception("Retired price credentials stored.");
            settings.RefreshSeconds = 120;
            settings.DecimalPlaces = 0;
            store.Save(settings);
            Equal(120, store.Load().RefreshSeconds);
            Equal(0, store.Load().DecimalPlaces);
            settings.DecimalPlaces = 1;
            store.Save(settings);
            Equal(1, store.Load().DecimalPlaces);
            File.Delete(path);
        });
        Check("GET public ticker with browser UA and no authorization", delegate
        {
            var handler = new FakeHandler(delegate(HttpRequestMessage message, CancellationToken cancellation)
            {
                Equal("GET", message.Method.Method);
                Equal(null, message.Headers.Authorization);
                Equal(TickerClient.BrowserUserAgent, message.Headers.UserAgent.ToString());
                Equal("https://safetrade.com/api/v2/peatio/public/markets/pearlusdt/tickers",
                    message.RequestUri.AbsoluteUri);
                return Task.FromResult(Response(HttpStatusCode.OK, "{\"ticker\":{\"last\":\"1.76\"}}"));
            });
            using (var client = new TickerClient(handler))
            {
                var result = client.FetchAsync(ValidSettings(), CancellationToken.None).GetAwaiter().GetResult();
                Equal("1.76", result.Last);
                Equal(false, result.IsStale);
            }
        });
        Check("Honors upstream stale-cache header", delegate
        {
            var response = Response(HttpStatusCode.OK, "{\"last\":\"1.76\"}");
            response.Headers.Add("X-Upstream-State", "refresh-failed");
            using (var client = new TickerClient(new FakeHandler(
                delegate { return Task.FromResult(response); })))
                Equal(true, client.FetchAsync(ValidSettings(), CancellationToken.None)
                    .GetAwaiter().GetResult().IsStale);
        });
        Check("401 produces useful error", delegate
        {
            using (var client = ClientWith(HttpStatusCode.Unauthorized, "{}"))
            {
                var error = Throws<HttpRequestException>(delegate
                {
                    client.FetchAsync(ValidSettings(), CancellationToken.None).GetAwaiter().GetResult();
                });
                if (!error.Message.Contains("401")) throw new Exception("Missing status code.");
                if (error.Message.Contains("test-token")) throw new Exception("Token in error.");
            }
        });
        Check("Redirect is not accepted as ticker JSON", delegate
        {
            using (var client = ClientWith(HttpStatusCode.Found, "{\"last\":\"9\"}"))
                Throws<HttpRequestException>(delegate
                {
                    client.FetchAsync(ValidSettings(), CancellationToken.None).GetAwaiter().GetResult();
                });
        });
        Check("Rejects oversized response", delegate
        {
            using (var client = ClientWith(HttpStatusCode.OK, new string('x', 65537)))
                Throws<InvalidDataException>(delegate
                {
                    client.FetchAsync(ValidSettings(), CancellationToken.None).GetAwaiter().GetResult();
                });
        });
        Check("Cancellation stops in-flight fetch", delegate
        {
            using (var cancellation = new CancellationTokenSource())
            using (var client = new TickerClient(new FakeHandler(async delegate(
                HttpRequestMessage message, CancellationToken token)
            {
                await Task.Delay(10000, token);
                return Response(HttpStatusCode.OK, "{\"last\":\"1.76\"}");
            })))
            {
                cancellation.CancelAfter(50);
                Throws<OperationCanceledException>(delegate
                {
                    client.FetchAsync(ValidSettings(), cancellation.Token).GetAwaiter().GetResult();
                });
            }
        });
        Check("Cancellation aborts a stalled response body", delegate
        {
            using (var cancellation = new CancellationTokenSource())
            using (var client = new TickerClient(new FakeHandler(delegate
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new StalledStream())
                });
            })))
            {
                Task<TickerResult> fetch = client.FetchAsync(ValidSettings(), cancellation.Token);
                cancellation.CancelAfter(50);
                if (Task.WhenAny(fetch, Task.Delay(2000)).GetAwaiter().GetResult() != fetch)
                    throw new Exception("Response stream did not stop on cancellation.");
                Throws<OperationCanceledException>(delegate { fetch.GetAwaiter().GetResult(); });
            }
        });
        Check("Curl stops when headers or body stall and request is cancelled", delegate
        {
            foreach (bool headers in new[] { true, false })
            using (var server = new LocalTickerServer())
            using (var client = CreateTicker())
            using (var cancellation = new CancellationTokenSource())
            {
                server.StallHeaders = headers;
                server.StallBody = !headers;
                var fetch = client.FetchAsync(new AppSettings(), cancellation.Token);
                if (!SpinWait.SpinUntil(delegate { return server.Requests > 0; }, 2000))
                    throw new Exception("Curl did not start the request.");
                cancellation.Cancel();
                if (Task.WhenAny(fetch, Task.Delay(2000)).GetAwaiter().GetResult() != fetch)
                    throw new Exception("Curl did not stop on cancellation.");
                Throws<OperationCanceledException>(delegate { fetch.GetAwaiter().GetResult(); });
            }
        });
        Check("Curl rejects oversized payloads", delegate
        {
            using (var server = new LocalTickerServer())
            using (var client = CreateTicker())
            {
                server.Price = new string('1', 70000);
                Throws<InvalidDataException>(delegate
                {
                    client.FetchAsync(new AppSettings(), CancellationToken.None).GetAwaiter().GetResult();
                });
            }
        });
        Check("Public 403 and 429 errors never ask for a token", delegate
        {
            foreach (HttpStatusCode status in new[] { HttpStatusCode.Forbidden, (HttpStatusCode)429 })
            using (var client = ClientWith(status, "<html>blocked</html>"))
            {
                var error = Throws<HttpRequestException>(delegate
                {
                    client.FetchAsync(new AppSettings(), CancellationToken.None).GetAwaiter().GetResult();
                });
                Equal(true, error.Message.Contains(((int)status).ToString()));
                Equal(false, error.Message.Contains("Token"));
            }
        });
        Check("First launch fetches automatically without a settings file", delegate
        {
            string path = Path.Combine(Artifacts, Guid.NewGuid().ToString("N") + ".json");
            using (var server = new LocalTickerServer())
            using (var context = CreateContext(new SettingsStore(path)))
            {
                PumpUntil(delegate { return Field<string>(context, "lastPrice") == "1.76"; });
                Equal(null, Field<SettingsForm>(context, "settingsForm"));
                Equal(false, server.Authorized);
                context.ExitThread();
            }
        });
        Check("Browser target matches the requested market", delegate
        {
            Equal("https://safetrade.com/exchange/PEARL-USDT", TrayContext.ExchangeUrl);
        });
        Check("Network uses HeroMiners network field without ticker credentials", delegate
        {
            using (var client = new NetworkClient(new FakeHandler(delegate(HttpRequestMessage message,
                CancellationToken cancellation)
            {
                Equal("https://pearl.herominers.com/api/stats", message.RequestUri.AbsoluteUri);
                Equal(null, message.Headers.Authorization);
                return Task.FromResult(Response(HttpStatusCode.OK,
                    "{\"network\":{\"networkHashps\":71408960020907940000},\"pool\":{\"hashrate\":123}}"));
            })))
            {
                decimal result = client.FetchAsync(CancellationToken.None).GetAwaiter().GetResult();
                Equal(71408960020907940000m, result);
                Equal("71.41 EH/s", NetworkClient.FormatHashrate(result));
            }
        });
        Check("Network rejects missing and invalid hashrates", delegate
        {
            foreach (string json in new[] { "null", "not json", "{}", "{\"network\":null}",
                "{\"network\":{\"networkHashps\":true}}", "{\"network\":{\"networkHashps\":-1}}",
                "{\"network\":{\"networkHashps\":\"71.41\"}}" })
                Throws<InvalidDataException>(delegate { NetworkClient.Parse(json); });
        });
        Check("Parses direct USDT/CNY quote", delegate
        {
            Equal(6.66m, ExchangeRateClient.Parse(RateJson("6.66")).CnyPerUsdt);
        });
        Check("Converts USDT to two-decimal CNY", delegate
        {
            Equal("11.72", ExchangeRateClient.ConvertToCny("1.76", 6.66m));
            Equal("12.85", ExchangeRateClient.ConvertToCny("1.93", 6.66m));
            Equal("1.01", ExchangeRateClient.ConvertToCny("0.1", 10.05m));
            Equal("0.00", ExchangeRateClient.ConvertToCny("0", 6.66m));
        });
        Check("Small CNY prices are not rounded to zero", delegate
        {
            Equal("0.0000666", ExchangeRateClient.ConvertToCny("0.00001", 6.66m));
        });
        foreach (string input in new[] { "{}", "null", "not json", RateJson("0"),
            RateJson("-6.66"), RateJson("\"NaN\""), "{\"date\":\"wrong\",\"usdt\":{\"cny\":6.66}}",
            "{\"date\":\"2099-01-01\",\"usdt\":{\"cny\":6.66}}" })
        {
            string sample = input;
            Check("Rejects invalid exchange rate " + sample, delegate
            {
                Throws<InvalidDataException>(delegate { ExchangeRateClient.Parse(sample); });
            });
        }
        Check("Rate fallback works without sending ticker credentials", delegate
        {
            int requests = 0;
            using (var client = new ExchangeRateClient(new FakeHandler(delegate(HttpRequestMessage request,
                CancellationToken cancellation)
            {
                if (request.Headers.Authorization != null) throw new Exception("Token leaked to rate provider.");
                requests++;
                if (requests == 1)
                {
                    Equal("cdn.jsdelivr.net", request.RequestUri.Host);
                    return Task.FromResult(Response(HttpStatusCode.ServiceUnavailable, "{}"));
                }
                Equal("latest.currency-api.pages.dev", request.RequestUri.Host);
                return Task.FromResult(Response(HttpStatusCode.OK, RateJson("6.66")));
            })))
            {
                Equal(6.66m, client.FetchAsync(CancellationToken.None).GetAwaiter().GetResult().CnyPerUsdt);
                Equal(2, requests);
            }
        });
        Check("Custom exchange rate URL is requested without a token", delegate
        {
            using (var client = new ExchangeRateClient(new FakeHandler(delegate(HttpRequestMessage request,
                CancellationToken cancellation)
            {
                Equal("https://custom.test/usdt-cny?source=mine", request.RequestUri.AbsoluteUri);
                Equal(null, request.Headers.Authorization);
                return Task.FromResult(Response(HttpStatusCode.OK, RateJson("7.25")));
            })))
                Equal(7.25m, client.FetchAsync("https://custom.test/usdt-cny?source=mine",
                    CancellationToken.None).GetAwaiter().GetResult().CnyPerUsdt);
        });
        Check("Custom rate source never silently falls back", delegate
        {
            int requests = 0;
            using (var client = new ExchangeRateClient(new FakeHandler(delegate
            {
                requests++;
                return Task.FromResult(Response(HttpStatusCode.ServiceUnavailable, "{}"));
            })))
            {
                Throws<InvalidDataException>(delegate
                {
                    client.FetchAsync("https://custom.test/rate", CancellationToken.None)
                        .GetAwaiter().GetResult();
                });
                Equal(1, requests);
            }
        });
        Check("Cancelled rate request does not attempt fallback", delegate
        {
            int requests = 0;
            using (var cancellation = new CancellationTokenSource())
            using (var client = new ExchangeRateClient(new FakeHandler(async delegate(HttpRequestMessage request,
                CancellationToken token)
            {
                requests++;
                await Task.Delay(10000, token);
                return Response(HttpStatusCode.OK, RateJson("6.66"));
            })))
            {
                cancellation.CancelAfter(50);
                Throws<OperationCanceledException>(delegate
                {
                    client.FetchAsync(cancellation.Token).GetAwaiter().GetResult();
                });
                Equal(1, requests);
            }
        });
        Check("Typical prices are not shortened", delegate
        {
            Equal("1.76", TrayIconRenderer.Compact("1.76"));
            Equal("1.93", TrayIconRenderer.Compact("1.93"));
        });
        Check("Long prices fit compact representation", delegate
        {
            Equal("1.23K", TrayIconRenderer.Compact("1234.567"));
            Equal("1e-5", TrayIconRenderer.Compact("0.00001234"));
        });
        Check("Real multi-resolution ICO decodes", delegate
        {
            using (var icon = TrayIconRenderer.Create("1.76", false))
            using (var file = File.Create(Path.Combine(Artifacts, "PearlTray.ico"))) icon.Save(file);
            using (var icon = new Icon(Path.Combine(Artifacts, "PearlTray.ico"), new Size(16, 16)))
            {
                Equal(16, icon.Width);
                Equal(16, icon.Height);
            }
        });
        Check("Rendered numbers are visible at all tray scales", delegate
        {
            foreach (int size in new[] { 16, 20, 24, 32, 40, 48, 64 })
                using (var bitmap = TrayIconRenderer.Render("1.76", size, Color.White))
                {
                    int visible = 0;
                    for (int y = 0; y < size; y++)
                        for (int x = 0; x < size; x++)
                            if (bitmap.GetPixel(x, y).A > 20) visible++;
                    if (visible < size) throw new Exception("Icon is blank at " + size);
                }
            ExportPreview();
        });
        WindowsFormsSynchronizationContext.AutoInstall = false;
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        Check("Menu fonts are embedded and shared by both menu levels", delegate
        {
            using (var main = new TrayMenu())
            using (var child = new TraySubMenu())
            using (var header = new WorkerMenuHeader("worker-01"))
            {
                Equal("JetBrains Mono", main.Font.FontFamily.Name);
                Equal(9.75f, main.Font.SizeInPoints);
                Equal(main.Font, child.Font);
                Equal("JetBrains Mono ExtraBold", header.Font.FontFamily.Name);
                using (var graphics = Graphics.FromHwnd(IntPtr.Zero))
                {
                    const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
                    Equal(TextRenderer.MeasureText(graphics, "111111", main.Font, Size.Empty, flags).Width,
                        TextRenderer.MeasureText(graphics, "WWWWWW", main.Font, Size.Empty, flags).Width);
                }
                main.Items.Add(new TrayActionItem("立即刷新", "\uE72C"));
                main.Show(new Point(100, 100));
                Application.DoEvents();
                Equal("Microsoft YaHei UI", main.Items[0].Font.FontFamily.Name);
                main.Close();
            }
        });
        Check("Long worker menus scroll while keeping commands visible", WorkerMenuScrolling);
        Check("Account workers preserve GPU names and convert H/s to TH/s", delegate
        {
            var workers = AccountClient.Parse(AccountJson());
            Equal(2, workers.Count);
            Equal("nj-01", workers[0].Name);
            Equal(2, workers[0].Gpus.Count);
            Equal("NVIDIA GeForce RTX 4070 Ti", workers[0].Gpus[0].Name);
            Equal("164.18 TH/s", AccountClient.FormatHashrate(workers[0].Gpus[0].Hashrate));
            Equal("106.04 TH/s", AccountClient.FormatHashrate(workers[0].Gpus[1].Hashrate));
            Equal("0.00 TH/s", AccountClient.FormatHashrate(0));
            Equal("0.43 TH/s", AccountClient.FormatHashrate(426666666666.667m));
            var lines = AccountClient.DisplayLines(workers);
            Equal("- nj-01", lines[0]);
            Equal("    RTX 4070 Ti  164.18 TH/s", lines[1]);
            Equal("- nj-02", lines[3]);
            workers.RemoveAt(1);
            Equal("nj-01", AccountClient.DisplayLines(workers)[0]);
            Equal("RTX 4070 Ti  164.18 TH/s",
                AccountClient.DisplayLines(workers)[1]);
            Equal(0, AccountClient.Parse("{\"connected_workers\":[]}").Count);
        });
        Check("Account parser rejects malformed data", delegate
        {
            foreach (string json in new[] { "null", "not json", "{}", "{\"connected_workers\":null}",
                "{\"connected_workers\":[{}]}", AccountJson().Replace("164176224118316", "-1"),
                AccountJson().Replace("164176224118316", "true"),
                AccountJson().Replace("164176224118316", "\"NaN\"") })
                Throws<InvalidDataException>(delegate { AccountClient.Parse(json); });
        });
        Check("Wallet endpoint encodes path and never sends price credentials", delegate
        {
            using (var client = new AccountClient(new FakeHandler(delegate(HttpRequestMessage message,
                CancellationToken cancellation)
            {
                Equal("GET", message.Method.Method);
                Equal("https://pearlhash.xyz/api/account/wallet%2Btest", message.RequestUri.AbsoluteUri);
                Equal(null, message.Headers.Authorization);
                return Task.FromResult(Response(HttpStatusCode.OK, AccountJson()));
            })))
                Equal(2, client.FetchAsync("wallet+test", CancellationToken.None).GetAwaiter().GetResult().Count);
        });
        Check("Account transaction history can exceed the ticker response limit", delegate
        {
            string json = AccountJson().Insert(1, "\"balance_transactions\":[\"" +
                new string('x', 320000) + "\"],");
            Equal(2, AccountClient.Parse(json).Count);
            foreach (bool streamed in new[] { false, true })
                using (var client = new AccountClient(new FakeHandler(delegate
                {
                    var response = Response(HttpStatusCode.OK, json);
                    if (streamed)
                        response.Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(json)));
                    return Task.FromResult(response);
                })))
                    Equal(2, client.FetchAsync("wallet", CancellationToken.None).GetAwaiter().GetResult().Count);
        });
        Check("Account errors and oversized responses are rejected", delegate
        {
            foreach (var code in new[] { HttpStatusCode.NotFound, HttpStatusCode.Found })
                using (var client = new AccountClient(new FakeHandler(
                    delegate { return Task.FromResult(Response(code, "{}")); })))
                    Throws<HttpRequestException>(delegate
                    {
                        client.FetchAsync("wallet", CancellationToken.None).GetAwaiter().GetResult();
                    });
            foreach (bool streamed in new[] { false, true })
                using (var client = new AccountClient(new FakeHandler(delegate
                {
                    var response = Response(HttpStatusCode.OK, new string('x', 4 * 1024 * 1024 + 1));
                    if (streamed)
                        response.Content = new StreamContent(
                            new MemoryStream(new byte[4 * 1024 * 1024 + 1]));
                    return Task.FromResult(response);
                })))
                    Throws<InvalidDataException>(delegate
                    {
                        client.FetchAsync("wallet", CancellationToken.None).GetAwaiter().GetResult();
                    });
        });
        Check("Account cancellation stops stalled bodies", delegate
        {
            using (var cancellation = new CancellationTokenSource())
            using (var client = new AccountClient(new FakeHandler(delegate
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StreamContent(new StalledStream()) });
            })))
            {
                var task = client.FetchAsync("wallet", cancellation.Token);
                cancellation.CancelAfter(50);
                if (Task.WhenAny(task, Task.Delay(2000)).GetAwaiter().GetResult() != task)
                    throw new Exception("Account response did not cancel.");
                Throws<OperationCanceledException>(delegate { task.GetAwaiter().GetResult(); });
            }
        });
        Check("Wallet refresh, failure, recovery, empty workers, settings changes", AccountLifecycle);
        Check("Old wallet responses cannot overwrite the new wallet", WalletChangeLifecycle);
        Check("Tray lifecycle: fetch, periodic refresh, failure, recovery, exit", TrayLifecycle);
        Check("Currency menu: convert, persist, cache, fail, recover, switch back", CurrencyLifecycle);
        Check("Missing rate never displays USDT as CNY", MissingRateLifecycle);
        Check("Changing rate URL invalidates the old source and refetches", RateSourceLifecycle);
        Check("Saving preferences preserves selected currency", delegate
        {
            string path = Path.Combine(Artifacts, "form-settings.json");
            var store = new SettingsStore(path);
            var settings = ValidSettings();
            using (var form = new SettingsForm(settings, store, false))
            {
                settings.DisplayCurrency = "CNY";
                settings.DecimalPlaces = 1;
                Field<TextBox>(form, "exchangeRateUrl").Text = " https://custom.test/rate ";
                Field<TextBox>(form, "walletAddress").Text = " wallet-test ";
                form.GetType().GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(form, new object[] { null, EventArgs.Empty });
                Equal("CNY", store.Load().DisplayCurrency);
                Equal(1, store.Load().DecimalPlaces);
                Equal("https://custom.test/rate", store.Load().ExchangeRateUrl);
                Equal("wallet-test", store.Load().WalletAddress);
            }
            File.Delete(path);
        });
        Check("Total threshold accepts a single nonnegative TH/s value", delegate
        {
            Equal(0m, HashrateMonitor.ParseThreshold("0"));
            Equal(12.5m, HashrateMonitor.ParseThreshold(" 12.5 "));
            foreach (string value in new[] { "", "-1", "1,2", "1,", "1,,2", "NaN", "1，2", "1e3",
                "79228162514264337593543950335" })
                Throws<ArgumentException>(delegate { HashrateMonitor.ParseThreshold(value); });
        });
        Check("GPU totals use unrounded values and remain safe for large inputs", delegate
        {
            Equal(376.254083031764m, HashrateMonitor.Total(AccountClient.Parse(AccountJson())));
            var large = AccountClient.Parse(AccountJson());
            foreach (var worker in large)
                foreach (var gpu in worker.Gpus) gpu.Hashrate = Decimal.MaxValue;
            Equal(true, HashrateMonitor.Total(large) > 0);
        });
        Check("Total threshold deduplicates notifications, recovers and detects no online workers", delegate
        {
            var settings = ValidSettings();
            settings.HashrateThresholds = "380";
            var workers = AccountClient.Parse(AccountJson());
            var monitor = new HashrateMonitor();
            var alerts = monitor.Evaluate(settings, workers);
            Equal(1, alerts.Count);
            Equal(376.254083031764m, alerts[0].Actual);
            Equal(true, alerts[0].Message.StartsWith("总算力过低"));
            monitor.MarkSent(true);
            Equal(false, monitor.NeedsNotification(true));
            Equal(true, monitor.NeedsNotification(false));
            monitor.MarkSent(false);
            workers.Reverse();
            Equal(376.254083031764m, monitor.Evaluate(settings, workers)[0].Actual);
            workers[0].Gpus[0].Hashrate = 120000000000000m;
            Equal(0, monitor.Evaluate(settings, workers).Count);
            Equal(true, monitor.NeedsNotification(true));
            Equal(true, monitor.NeedsNotification(false));
            workers.RemoveAt(0);
            alerts = monitor.Evaluate(settings, workers);
            Equal(1, alerts.Count);
            Equal(HashrateMonitor.Total(workers), alerts[0].Actual);
            workers.Clear();
            alerts = monitor.Evaluate(settings, workers);
            Equal(0m, alerts[0].Actual);
            monitor.MarkSent(true);
            settings.HashrateThresholds = "0";
            Equal(0, monitor.Evaluate(settings, workers).Count);
            Equal(true, monitor.NeedsNotification(true));
        });
        Check("Total threshold ignores worker count and names and compares unrounded totals", delegate
        {
            var settings = ValidSettings();
            settings.HashrateThresholds = "100";
            var monitor = new HashrateMonitor();
            var workers = AccountClient.Parse(AccountJson());
            workers[1].Name = workers[0].Name;
            Equal(0, monitor.Evaluate(settings, workers).Count);
            settings.HashrateThresholds = "376.254083031764";
            Equal(0, monitor.Evaluate(settings, workers).Count);
            settings.HashrateThresholds = "376.254083031765";
            Equal(1, monitor.Evaluate(settings, workers).Count);
        });
        Check("Notification settings persist with encrypted webhook password", delegate
        {
            string path = Path.Combine(Artifacts, "notification-settings.json");
            var store = new SettingsStore(path);
            var settings = ValidSettings();
            settings.HashrateThresholds = "380";
            settings.WindowsNotifications = false;
            settings.WebhookNotifications = true;
            settings.Webhook.Url = "https://notify.test/hook";
            settings.Webhook.Method = "POST";
            settings.Webhook.Password = "WEBHOOK_TEST_SECRET";
            store.Save(settings);
            var loaded = store.Load();
            Equal(settings.HashrateThresholds, loaded.HashrateThresholds);
            Equal(false, File.ReadAllText(path).Contains("ThresholdWorkerNames"));
            Equal(false, loaded.WindowsNotifications);
            Equal(true, loaded.WebhookNotifications);
            Equal(settings.Webhook.Password, loaded.Webhook.Password);
            Equal(false, File.ReadAllText(path).Contains(settings.Webhook.Password));
            File.Delete(path);
        });
        Check("Legacy per-worker thresholds migrate to a total without worker bindings", delegate
        {
            string path = Path.Combine(Artifacts, "legacy-threshold-settings.json");
            var store = new SettingsStore(path);
            store.Save(ValidSettings());
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            var values = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
            values["HashrateThresholds"] = "270, 110";
            values["ThresholdWorkerNames"] = new[] { "nj-01", "nj-02" };
            File.WriteAllText(path, serializer.Serialize(values));
            var loaded = store.Load();
            Equal("380", loaded.HashrateThresholds);
            store.Save(loaded);
            Equal(false, File.ReadAllText(path).Contains("ThresholdWorkerNames"));
            Equal("380", store.Load().HashrateThresholds);
            File.Delete(path);
        });
        Check("Webhook GET escapes message and sends only its own credentials", delegate
        {
            var settings = new WebhookSettings { Url = "https://notify.test/hook?text={{message}}&title={{title}}",
                Headers = "{\"X-Key\":\"hook-key\"}", Username = "user", Password = "pass" };
            using (var client = new WebhookClient(new FakeHandler(delegate(HttpRequestMessage request,
                CancellationToken cancellation)
            {
                Equal("GET", request.Method.Method);
                Equal("?text=" + Uri.EscapeDataString("矿机 & #\n低算力") + "&title=Alert",
                    request.RequestUri.Query);
                Equal("Basic", request.Headers.Authorization.Scheme);
                Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("user:pass")),
                    request.Headers.Authorization.Parameter);
                Equal(true, request.Headers.Contains("X-Key"));
                Equal(null, request.Content);
                return Task.FromResult(Response(HttpStatusCode.NoContent, ""));
            })))
                client.SendAsync(settings, "Alert", "矿机 & #\n低算力", CancellationToken.None).GetAwaiter().GetResult();
        });
        Check("Webhook JSON POST safely substitutes quotes, newline and backslash", delegate
        {
            string title = "a\"b";
            string message = "矿机\\test\n掉线";
            var settings = new WebhookSettings { Url = "https://notify.test/hook", Method = "POST" };
            using (var client = new WebhookClient(new FakeHandler(async delegate(HttpRequestMessage request,
                CancellationToken cancellation)
            {
                Equal("POST", request.Method.Method);
                Equal(null, request.Headers.Authorization);
                Equal("application/json", request.Content.Headers.ContentType.MediaType);
                var body = new System.Web.Script.Serialization.JavaScriptSerializer()
                    .Deserialize<System.Collections.Generic.Dictionary<string, string>>(
                        await request.Content.ReadAsStringAsync().ConfigureAwait(false));
                Equal(title, body["title"]);
                Equal(message, body["message"]);
                return Response(HttpStatusCode.OK, "{}");
            })))
                client.SendAsync(settings, title, message, CancellationToken.None).GetAwaiter().GetResult();
        });
        Check("Webhook supports plain body and rejects bad configuration or HTTP failure", delegate
        {
            var settings = new WebhookSettings { Url = "https://notify.test/hook", Method = "POST",
                ContentType = "text/plain", Body = "{{title}}\n{{message}}" };
            Equal("title\nbody", settings.RenderBody("title", "body"));
            using (var client = new WebhookClient(new FakeHandler(delegate
            {
                return Task.FromResult(Response(HttpStatusCode.ServiceUnavailable, "secret error"));
            })))
                Throws<HttpRequestException>(delegate
                {
                    client.SendAsync(settings, "title", "body", CancellationToken.None).GetAwaiter().GetResult();
                });
            foreach (string header in new[] { "[]", "{\"X\":42}", "{\"X\":\"a\\r\\nb\"}" })
            {
                settings.Headers = header;
                Throws<ArgumentException>(settings.Validate);
            }
            settings.Headers = "{}";
            settings.Url = "file:///C:/hook";
            Throws<ArgumentException>(settings.Validate);
        });
        Check("Webhook cancellation stops an in-flight notification", delegate
        {
            using (var cancellation = new CancellationTokenSource())
            using (var client = new WebhookClient(new FakeHandler(async delegate(HttpRequestMessage request,
                CancellationToken token)
            {
                await Task.Delay(10000, token).ConfigureAwait(false);
                return Response(HttpStatusCode.OK, "{}");
            })))
            {
                cancellation.CancelAfter(30);
                Throws<OperationCanceledException>(delegate
                {
                    client.SendAsync(new WebhookSettings { Url = "https://notify.test/hook" },
                        "title", "body", cancellation.Token).GetAwaiter().GetResult();
                });
            }
        });
        Check("Notification lifecycle deduplicates, retries and ignores failed account fetch", NotificationLifecycle);
        Check("Settings display current totals and preserve notification fields", delegate
        {
            string path = Path.Combine(Artifacts, "alert-form.json");
            var settings = ValidSettings();
            settings.HashrateThresholds = "380";
            using (var form = new SettingsForm(settings, new SettingsStore(path), false,
                AccountClient.Parse(AccountJson()), ""))
            {
                Equal(true, Field<Label>(form, "totals").Text.Contains("376.25"));
                Equal(true, Field<CheckBox>(form, "windowsNotifications").Checked);
                Equal(false, Field<CheckBox>(form, "webhookNotifications").Checked);
                form.Show();
                Application.DoEvents();
                Equal(true, Field<Label>(form, "totals").Bounds.Height >= 20);
                Equal(true, Field<Label>(form, "totals").Bounds.Width >= 150);
                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(Artifacts, "settings-alerts-preview.png"), ImageFormat.Png);
                }
                form.GetType().GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(form, new object[] { null, EventArgs.Empty });
                Equal("380", new SettingsStore(path).Load().HashrateThresholds);
            }
            using (var form = new WebhookSettingsForm(new WebhookSettings()))
            {
                form.Show();
                Application.DoEvents();
                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(Artifacts, "webhook-settings-preview.png"), ImageFormat.Png);
                }
                form.Close();
            }
            File.Delete(path);
        });
        Check("Webhook layout keeps fields and actions usable at different scales", WebhookLayout);
        Check("Settings themes, scaling and validation keep all actions accessible", SettingsLayout);
        Check("Live hashrate summary stays compact and shows complete units", SettingsSummary);
        Check("Webhook method switches preserve drafts and password visibility", WebhookEditing);
        Console.WriteLine("{0} passed, {1} failed.", passed, failed);
        return failed == 0 ? 0 : 1;
    }

    private static void WebhookLayout()
    {
        foreach (bool light in new[] { true, false })
        foreach (float scale in new[] { 1F, 1.25F, 1.5F, 2F })
        {
            using (var form = new WebhookSettingsForm(new WebhookSettings()))
            {
                ScalePreview(form, scale);
                form.Show();
                form.SetAppearance(light);
                Application.DoEvents();
                var scroll = (Panel)form.Controls.Find("webhookScroll", true)[0];
                var layout = (TableLayoutPanel)scroll.Controls[0];
                var save = (Button)form.Controls.Find("webhookSave", true)[0];
                var cancel = (Button)form.Controls.Find("webhookCancel", true)[0];
                var body = (TextBox)form.Controls.Find("webhookBody", true)[0];
                Equal(true, body.AcceptsReturn);
                AssertWebhookLayout(form, layout, save, cancel);
                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(Artifacts, "webhook-" + (light ? "light-" : "dark-") +
                        (int)(scale * 100) + ".png"),
                        ImageFormat.Png);
                }
                form.ClientSize = new Size((int)(460 * scale), (int)(340 * scale));
                Application.DoEvents();
                Equal(true, scroll.VerticalScroll.Visible);
                AssertWebhookLayout(form, layout, save, cancel);
                save.PerformClick();
                Application.DoEvents();
                Equal(null, form.SavedSettings);
                var error = (Label)form.Controls.Find("webhookError", true)[0];
                Equal(true, error.Text.Contains("URL"));
                Equal(true, error.Height >= error.Font.Height);
                AssertWebhookLayout(form, layout, save, cancel);
                if (scale == 1F || scale == 2F)
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(Path.Combine(Artifacts, "webhook-compact-" + (light ? "light-" : "dark-") +
                            (int)(scale * 100) + ".png"),
                            ImageFormat.Png);
                    }
                ((Button)form.Controls.Find("webhookPost", true)[0]).PerformClick();
                Equal(true, body.Enabled);
                body.Focus();
                Application.DoEvents();
                var bodyBounds = scroll.RectangleToClient(body.Parent.RectangleToScreen(body.Parent.ClientRectangle));
                Equal(true, scroll.ClientRectangle.Contains(bodyBounds));
                var url = (TextBox)form.Controls.Find("webhookUrl", true)[0];
                url.Text = "https://notify.test/hook";
                save.PerformClick();
                Equal("https://notify.test/hook", form.SavedSettings.Url);
            }
        }
    }

    private static void SettingsLayout()
    {
        foreach (bool light in new[] { true, false })
        foreach (float scale in new[] { 1F, 1.25F, 1.5F, 2F })
        {
            string path = Path.Combine(Artifacts, "layout-settings.json");
            using (var form = new SettingsForm(ValidSettings(), new SettingsStore(path), false,
                AccountClient.Parse(AccountJson()), ""))
            {
                ScalePreview(form, scale);
                form.Show();
                form.SetAppearance(light);
                Application.DoEvents();
                var scroll = (Panel)form.Controls.Find("settingsScroll", true)[0];
                var layout = (TableLayoutPanel)scroll.Controls[0];
                var save = (Button)form.Controls.Find("settingsSave", true)[0];
                var cancel = (Button)form.Controls.Find("settingsCancel", true)[0];
                AssertWebhookLayout(form, layout, save, cancel);
                Equal(null, form.GetType().GetField("token", BindingFlags.Instance | BindingFlags.NonPublic));
                Equal(null, form.GetType().GetField("url", BindingFlags.Instance | BindingFlags.NonPublic));
                Equal(5m, Field<NumericUpDown>(form, "interval").Minimum);
                if (scale == 1F)
                    CaptureForm(form, "settings-" + (light ? "light" : "dark") + ".png");
                form.ClientSize = new Size((int)(480 * scale), (int)(380 * scale));
                Field<TextBox>(form, "exchangeRateUrl").Text = "invalid";
                save.PerformClick();
                Application.DoEvents();
                Equal(null, form.SavedSettings);
                Equal(true, Field<Label>(form, "error").Visible);
                Equal(true, scroll.VerticalScroll.Visible);
                AssertWebhookLayout(form, layout, save, cancel);
                AssertEditorsFit(layout);
                Field<TextBox>(form, "thresholds").Focus();
                Application.DoEvents();
                var thresholdFrame = Field<TextBox>(form, "thresholds").Parent;
                var bounds = scroll.RectangleToClient(thresholdFrame.RectangleToScreen(thresholdFrame.ClientRectangle));
                Equal(true, scroll.ClientRectangle.Contains(bounds));
                if (scale == 1F || scale == 2F)
                    CaptureForm(form, "settings-compact-" + (light ? "light-" : "dark-") +
                        (int)(scale * 100) + ".png");
                Field<TextBox>(form, "exchangeRateUrl").Text = AppSettings.DefaultExchangeRateUrl;
                save.PerformClick();
                Equal(AppSettings.DefaultExchangeRateUrl, new SettingsStore(path).Load().ExchangeRateUrl);
                File.Delete(path);
            }
        }
    }

    private static void SettingsSummary()
    {
        foreach (float scale in new[] { 1F, 1.25F, 1.5F, 1.75F, 2F })
        {
            var settings = ValidSettings();
            settings.HashrateThresholds = "250";
            var workers = AccountClient.Parse(AccountJson());
            workers.RemoveAt(1);
            using (var form = new SettingsForm(settings, new SettingsStore(
                Path.Combine(Artifacts, "summary-unused.json")), false, workers, ""))
            {
                ScalePreview(form, scale);
                form.Show();
                Application.DoEvents();
                var total = Field<Label>(form, "totals");
                Equal(0, form.Controls.Find("machineTotals", true).Length);
                var summary = (TableLayoutPanel)total.Parent;
                var multiple = AccountClient.Parse(AccountJson());
                multiple[0].Name = "nj-01-mining-workstation-with-a-long-worker-name";
                multiple[1].Name = "nj-02-mining-workstation-with-a-long-worker-name";
                var samples = new[] { workers, multiple, null, workers };
                float actualScale = form.Font.SizeInPoints / 9.75f;
                foreach (int width in new[] { 680, 480, 820, 680 })
                foreach (var sample in samples)
                {
                    form.ClientSize = new Size((int)(width * actualScale),
                        form.ClientSize.Height);
                    form.UpdateHashrates(sample, sample == null ? "矿机获取失败，将在下个周期重试" : "");
                    Application.DoEvents();
                    if (width == 680 && sample == workers && (scale == 1F || scale == 1.75F))
                        using (var bitmap = new Bitmap(summary.Width, summary.Height))
                        {
                            summary.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                            bitmap.Save(Path.Combine(Artifacts, "summary-" +
                                Path.GetFileNameWithoutExtension(Application.ExecutablePath) + "-" +
                                (int)(scale * 100) + ".png"), ImageFormat.Png);
                        }
                    var label = summary.GetControlFromPosition(0, 0);
                    if (Math.Abs(label.Top + label.Height / 2 - (total.Top + total.Height / 2)) >
                        form.Font.Height / 2)
                        throw new Exception("Summary label and value are not vertically aligned.");
                    int preferredHeight = summary.GetPreferredSize(new Size(summary.Width, 0)).Height;
                    if (summary.Height > preferredHeight + form.Font.Height)
                        throw new Exception("Summary retains excess height: " + summary.Height +
                            ", preferred " + preferredHeight);
                    Equal(1, summary.RowCount);
                    Equal(2, summary.Controls.Count);
                    using (var graphics = total.CreateGraphics())
                    {
                        var text = TextRenderer.MeasureText(graphics, total.Text, total.Font, Size.Empty,
                            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                        if (total.Width < text.Width || total.Height < text.Height ||
                            !summary.ClientRectangle.Contains(total.Bounds))
                            throw new Exception("Total or TH/s unit is clipped: " + total.Bounds +
                                "; text " + text);
                    }
                    Equal(sample == null ? "-- TH/s" :
                        HashrateMonitor.Total(sample).ToString("0.00", CultureInfo.InvariantCulture) + " TH/s",
                        total.Text);
                }
            }
        }
    }

    private static void WebhookEditing()
    {
        var original = new WebhookSettings
        {
            Url = "https://notify.test/hook", Method = "POST",
            Username = "demo", Password = "test-password"
        };
        using (var form = new WebhookSettingsForm(original))
        {
            form.Show();
            Application.DoEvents();
            var body = (TextBox)form.Controls.Find("webhookBody", true)[0];
            var password = (TextBox)FindEditor(form, "密码（可选）");
            var reveal = (Button)((SettingsField)password.Parent).Adornment;
            Equal(true, password.UseSystemPasswordChar);
            reveal.PerformClick();
            Equal(false, password.UseSystemPasswordChar);
            reveal.PerformClick();
            Equal(true, password.UseSystemPasswordChar);
            body.Text = "{\"text\":\"{{message}}\"}";
            ((Button)form.Controls.Find("webhookGet", true)[0]).PerformClick();
            Equal(false, body.Enabled);
            ((Button)form.Controls.Find("webhookPost", true)[0]).PerformClick();
            Equal(true, body.Enabled);
            Equal("{\"text\":\"{{message}}\"}", body.Text);
            ((Button)form.Controls.Find("webhookCancel", true)[0]).PerformClick();
            Equal(null, form.SavedSettings);
            Equal(new WebhookSettings().Body, original.Body);
            Equal("POST", original.Method);
            Equal("test-password", original.Password);
        }
    }

    private static Control FindEditor(Control parent, string name)
    {
        foreach (Control child in parent.Controls)
        {
            if (child.AccessibleName == name) return child;
            var result = FindEditor(child, name);
            if (result != null) return result;
        }
        return null;
    }

    private static void ScalePreview(Form form, float scale)
    {
        if (scale == 1F) return;
        var fonts = new List<KeyValuePair<Control, Font>>();
        CollectFonts(form, fonts);
        form.AutoScaleMode = AutoScaleMode.None;
        form.Scale(new SizeF(scale, scale));
        var allocated = new List<Font>();
        foreach (var item in fonts)
        {
            var font = new Font(item.Value.FontFamily, item.Value.SizeInPoints * scale,
                item.Value.Style, GraphicsUnit.Point);
            allocated.Add(font);
            item.Key.Font = font;
        }
        form.Disposed += delegate { foreach (var font in allocated) font.Dispose(); };
    }

    private static void CollectFonts(Control control, List<KeyValuePair<Control, Font>> fonts)
    {
        fonts.Add(new KeyValuePair<Control, Font>(control, control.Font));
        foreach (Control child in control.Controls) CollectFonts(child, fonts);
    }

    private static void CaptureForm(Form form, string name)
    {
        using (var bitmap = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(Artifacts, name), ImageFormat.Png);
        }
    }

    private static void AssertEditorsFit(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is TextBox || child is NumericUpDown)
            {
                if (!child.Parent.ClientRectangle.Contains(child.Bounds) ||
                    child.Height < child.Font.Height)
                    throw new Exception("Input clipped: " + child.AccessibleName + " " + child.Bounds);
            }
            AssertEditorsFit(child);
        }
    }

    private static void AssertWebhookLayout(Form form, TableLayoutPanel fields, Button save, Button cancel)
    {
        int previousBottom = 0;
        foreach (Control field in fields.Controls)
        {
            if (field.Top < previousBottom || field.Left < 0 || field.Right > fields.ClientSize.Width ||
                field.Bottom > fields.ClientSize.Height)
                throw new Exception("Webhook field clipped or overlaps at " + field.AccessibleName);
            if (field.Height < field.Font.Height)
                throw new Exception("Webhook field text clipped at " + field.AccessibleName);
            previousBottom = field.Bottom;
        }
        AssertEditorsFit(fields);
        foreach (var button in new[] { save, cancel })
        {
            var bounds = form.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
            if (!form.ClientRectangle.Contains(bounds))
                throw new Exception("Webhook action clipped at " + button.Text + ": " + bounds +
                    " in " + form.ClientRectangle + "; parent " + button.Parent.Bounds +
                    "; footer " + button.Parent.Parent.Bounds);
            if (button.Width < button.PreferredSize.Width || button.Height < button.PreferredSize.Height)
                throw new Exception("Webhook button text clipped at " + button.Text);
        }
    }

    private static void NotificationLifecycle()
    {
        string path = Path.Combine(Artifacts, "notification-lifecycle.json");
        int notifications = 0;
        bool accountFails = false;
        bool webhookFails = true;
        bool recovered = false;
        using (var server = new LocalTickerServer())
        {
            var settings = new AppSettings {
                WalletAddress = "wallet", HashrateThresholds = "380", WindowsNotifications = false,
                WebhookNotifications = true, Webhook = new WebhookSettings { Url = "https://notify.test/hook" } };
            var store = new SettingsStore(path);
            store.Save(settings);
            using (var context = new TrayContext(store, new ExchangeRateClient(),
                new AccountClient(new FakeHandler(delegate
                {
                    return Task.FromResult(Response(accountFails ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK,
                        recovered ? AccountJson().Replace("106038929456724", "120000000000000") : AccountJson()));
                })),
                new WebhookClient(new FakeHandler(delegate
                {
                    notifications++;
                    return Task.FromResult(Response(webhookFails ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK, "{}"));
                })), CreateTicker()))
            {
                var refresh = Field<ToolStripMenuItem>(context, "refreshItem");
                PumpUntil(delegate { return notifications == 1 && refresh.Enabled; });
                Equal(true, WorkerMenuText(context).Contains("Webhook 通知失败"));
                Equal("380", store.Load().HashrateThresholds);
                accountFails = true;
                refresh.PerformClick();
                PumpUntil(delegate { return refresh.Enabled; });
                Equal(1, notifications);
                accountFails = false;
                webhookFails = false;
                refresh.PerformClick();
                PumpUntil(delegate { return refresh.Enabled; });
                Equal(2, notifications);
                refresh.PerformClick();
                PumpUntil(delegate { return refresh.Enabled; });
                Equal(2, notifications);
                recovered = true;
                refresh.PerformClick();
                PumpUntil(delegate { return refresh.Enabled; });
                recovered = false;
                refresh.PerformClick();
                PumpUntil(delegate { return refresh.Enabled; });
                Equal(3, notifications);
                context.ExitThread();
            }
        }
        File.Delete(path);
    }

    private static string AccountJson()
    {
        return "{\"connected_workers\":[" +
            "{\"worker_name\":\"nj-01\",\"gpu_info\":[" +
            "{\"name\":\"NVIDIA GeForce RTX 4070 Ti\",\"hashrate\":164176224118316}," +
            "{\"name\":\"NVIDIA GeForce RTX 5060 Ti\",\"hashrate\":106038929456724}]}," +
            "{\"worker_name\":\"nj-02\",\"gpu_info\":[" +
            "{\"name\":\"NVIDIA GeForce RTX 5060 Ti\",\"hashrate\":106038929456724}]}]}";
    }

    private static void AccountLifecycle()
    {
        string path = Path.Combine(Artifacts, "account-settings.json");
        int requests = 0;
        bool fails = false;
        bool empty = false;
        using (var server = new LocalTickerServer())
        {
            var settings = new AppSettings {
                WalletAddress = "wallet-test", RefreshSeconds = 5 };
            var store = new SettingsStore(path);
            store.Save(settings);
            using (var context = CreateContext(store, new ExchangeRateClient(),
                new AccountClient(new FakeHandler(delegate
                {
                    requests++;
                    return Task.FromResult(Response(fails ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK,
                        fails ? "{}" : empty ? "{\"connected_workers\":[]}" : AccountJson()));
                }))))
            {
                var refresh = Field<ToolStripMenuItem>(context, "refreshItem");
                var menu = Field<ContextMenuStrip>(context, "menu");
                var tray = Field<NotifyIcon>(context, "tray");
                PumpUntil(delegate { return WorkerMenuText(context).Contains("164.18 TH/s") &&
                    Field<string>(context, "lastPrice") == "1.76" && refresh.Enabled; });
                Equal("nj-01", menu.Items[0].Text);
                Equal("nj-02", menu.Items[3].Text);
                Equal(true, menu.Renderer is MetricMenuRenderer);
                foreach (ToolStripItem item in menu.Items)
                    if (item.Text.Contains("PRL/") || item.Text.Contains("全网算力"))
                        throw new Exception("Removed metrics are still visible.");
                var gpuItem = (MetricMenuItem)menu.Items[1];
                Equal("RTX 4070 Ti  164.18 TH/s", gpuItem.Text);
                Equal("164.18", gpuItem.Text.Substring(gpuItem.ValueStart, gpuItem.ValueLength));
                Equal(true, WorkerMenuText(context).Contains("nj-02"));
                Equal(true, tray.Text.Contains("PEARL/USDT  1.76\n已更新"));
                Equal(true, tray.Text.Contains("更新 "));
                Equal(false, tray.Text.Contains("nj-"));
                Equal(false, tray.Text.Contains("TH/s"));
                ExportAccountPreview(context);
                PumpUntil(delegate { return requests >= 2 && refresh.Enabled; });
                fails = true;
                refresh.PerformClick();
                PumpUntil(delegate { return WorkerMenuText(context).Contains("显示上次数据"); });
                Equal("1.76", Field<string>(context, "lastPrice"));
                Equal(true, WorkerMenuText(context).Contains("nj-01"));
                Equal(false, tray.Text.Contains("矿机"));
                fails = false;
                empty = true;
                PumpUntil(delegate { return refresh.Enabled; });
                refresh.PerformClick();
                PumpUntil(delegate { return WorkerMenuText(context).Contains("暂无在线矿机"); });
                Equal(false, WorkerMenuText(context).Contains("nj-01"));
                empty = false;
                server.Status = 401;
                PumpUntil(delegate { return refresh.Enabled; });
                refresh.PerformClick();
                PumpUntil(delegate { return tray.Text.Contains("401") &&
                    WorkerMenuText(context).Contains("nj-02") && refresh.Enabled; });
                settings.WalletAddress = "";
                context.GetType().GetMethod("ApplySettings", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(context, new object[] { settings });
                PumpUntil(delegate { return refresh.Enabled; });
                Equal(6, menu.Items.Count);
                Equal(false, Field<ToolStripSeparator>(context, "workerSeparator").Visible);
                Equal(false, WorkerMenuText(context).Contains("nj-01"));
                int before = requests;
                refresh.PerformClick();
                PumpUntil(delegate { return refresh.Enabled; });
                Equal(before, requests);
                menu.Items[menu.Items.Count - 1].PerformClick();
            }
        }
        File.Delete(path);
    }

    private static void WalletChangeLifecycle()
    {
        string path = Path.Combine(Artifacts, "wallet-change-settings.json");
        var oldResponse = new TaskCompletionSource<HttpResponseMessage>();
        int requests = 0;
        using (var server = new LocalTickerServer())
        {
            var store = new SettingsStore(path);
            var settings = new AppSettings {
                WalletAddress = "old-wallet" };
            store.Save(settings);
            using (var context = CreateContext(store, new ExchangeRateClient(),
                new AccountClient(new FakeHandler(delegate(HttpRequestMessage message, CancellationToken token)
                {
                    requests++;
                    if (message.RequestUri.AbsolutePath.EndsWith("old-wallet")) return oldResponse.Task;
                    return Task.FromResult(Response(HttpStatusCode.OK, AccountJson().Replace("nj-", "new-")));
                }))))
            {
                PumpUntil(delegate { return requests == 1; });
                var next = store.Load();
                next.WalletAddress = "new-wallet";
                context.GetType().GetMethod("ApplySettings", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(context, new object[] { next });
                PumpUntil(delegate { return WorkerMenuText(context).Contains("new-01"); });
                oldResponse.SetResult(Response(HttpStatusCode.OK, AccountJson()));
                PumpUntil(delegate { return Field<ToolStripMenuItem>(context, "refreshItem").Enabled; });
                Application.DoEvents();
                Equal(false, WorkerMenuText(context).Contains("nj-01"));
                Equal(true, WorkerMenuText(context).Contains("new-01"));
            }
        }
        File.Delete(path);
    }

    private static void WorkerMenuScrolling()
    {
        using (var menu = new TrayMenu())
        {
            var rows = new List<ToolStripItem>();
            for (int i = 0; i < 30; i++)
            {
                rows.Add(new WorkerMenuHeader("worker-" + i.ToString("00")));
                rows.Add(new MetricMenuItem("RTX 5090  ", "420.19", " TH/s"));
            }
            menu.Items.AddRange(rows.ToArray());
            menu.Items.Add(new ToolStripSeparator());
            var refresh = new TrayActionItem("Refresh", "\uE72C");
            var settings = new TrayActionItem("Settings", "\uE713");
            var quit = new TrayActionItem("Exit", "\uE7E8");
            menu.Items.AddRange(new ToolStripItem[] { refresh, settings, quit });
            MethodInfo wheel = typeof(TrayMenu).GetMethod("OnMouseWheel",
                BindingFlags.Instance | BindingFlags.NonPublic);
            menu.Show(new Point(100, 100));
            Application.DoEvents();
            Equal(true, rows[0].Available);
            Equal(false, rows[rows.Count - 1].Available);
            if (menu.Height > Screen.FromControl(menu).WorkingArea.Height - 24)
                throw new Exception("Worker menu exceeds the available screen height.");
            Size originalSize = menu.Size;
            Rectangle commandBounds = quit.Bounds;
            using (var bitmap = new Bitmap(menu.Width, menu.Height))
            {
                menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(Artifacts, "worker-menu-scroll-top.png"), ImageFormat.Png);
            }
            MethodInfo windowMessage = typeof(TrayMenu).GetMethod("WndProc",
                BindingFlags.Instance | BindingFlags.NonPublic);
            windowMessage.Invoke(menu, new object[] { Message.Create(menu.Handle, 0x020A,
                new IntPtr(-60 << 16), IntPtr.Zero) });
            Equal(true, rows[0].Available);
            windowMessage.Invoke(menu, new object[] { Message.Create(menu.Handle, 0x020A,
                new IntPtr(-60 << 16), IntPtr.Zero) });
            if (SystemInformation.MouseWheelScrollLines != 0)
                Equal(false, rows[0].Available);
            for (int i = 0; i < rows.Count; i++)
                wheel.Invoke(menu, new object[] { new HandledMouseEventArgs(
                    MouseButtons.None, 0, 30, 60, -120) });
            Application.DoEvents();
            if (SystemInformation.MouseWheelScrollLines != 0)
                Equal(true, rows[rows.Count - 1].Available);
            Equal(true, menu.Visible);
            Equal(originalSize, menu.Size);
            Equal(commandBounds, quit.Bounds);
            foreach (var command in new[] { refresh, settings, quit })
            {
                Equal(true, command.Available);
                if (command.Bounds.Top < 0 || command.Bounds.Bottom > menu.ClientSize.Height)
                    throw new Exception("Command is outside the menu viewport: " + command.Text);
            }
            using (var bitmap = new Bitmap(menu.Width, menu.Height))
            {
                menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(Artifacts, "worker-menu-scroll-bottom.png"), ImageFormat.Png);
            }
            menu.RefreshWorkerScroll();
            if (SystemInformation.MouseWheelScrollLines != 0)
                Equal(true, rows[rows.Count - 1].Available);
            menu.Close();
            menu.Show(new Point(100, 100));
            Application.DoEvents();
            if (!rows[0].Available || rows[rows.Count - 1].Available)
                throw new Exception("Reopening did not reset the worker viewport.");
            // Scroll buttons must leave the dropdown open.
            var down = Field<ToolStripMenuItem>(menu, "scrollDown");
            down.PerformClick();
            Application.DoEvents();
            if (rows[0].Available || !menu.Visible)
                throw new Exception("Scroll arrow failed: firstRow=" + Field<int>(menu, "firstRow") +
                    "; visible=" + menu.Visible + "; enabled=" + down.Enabled);
            menu.Close();
            foreach (var row in rows) menu.Items.Remove(row);
            menu.Show(new Point(100, 100));
            Application.DoEvents();
            Equal(4, menu.Items.Count);
            foreach (var row in rows) row.Dispose();
            menu.Close();
        }
    }

    private static string WorkerMenuText(TrayContext context)
    {
        var text = new StringBuilder();
        foreach (var item in Field<System.Collections.Generic.List<ToolStripItem>>(context, "workerItems"))
            text.AppendLine(item.Text);
        return text.ToString();
    }

    private static void ExportAccountPreview(TrayContext context)
    {
        var menu = Field<ContextMenuStrip>(context, "menu");
        menu.Show(new Point(100, 100));
        Application.DoEvents();
        using (var bitmap = new Bitmap(menu.Width, menu.Height))
        {
            menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(Artifacts, "account-menu.png"), ImageFormat.Png);
            foreach (int index in new[] { 1, 2, 4 })
            {
                Rectangle row = Rectangle.Intersect(menu.Items[index].Bounds,
                    new Rectangle(Point.Empty, bitmap.Size));
                int accentPixels = 0;
                for (int y = row.Top; y < row.Bottom; y++)
                    for (int x = row.Left; x < row.Right; x++)
                    {
                        Color pixel = bitmap.GetPixel(x, y);
                        if (pixel.G > pixel.R + 30 && pixel.R > pixel.B + 60)
                            accentPixels++;
                    }
                if (accentPixels < 5)
                    throw new Exception("Metric row has no colored value: " + menu.Items[index].Text);
            }
        }
        var refresh = Field<ToolStripMenuItem>(context, "refreshItem");
        refresh.Select();
        Application.DoEvents();
        using (var bitmap = new Bitmap(menu.Width, menu.Height))
        {
            menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(Artifacts, "account-menu-hover.png"), ImageFormat.Png);
        }
        var currency = Field<ToolStripMenuItem>(context, "usdtItem").OwnerItem as ToolStripMenuItem;
        currency.ShowDropDown();
        Application.DoEvents();
        using (var bitmap = new Bitmap(currency.DropDown.Width, currency.DropDown.Height))
        {
            currency.DropDown.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(Artifacts, "currency-menu.png"), ImageFormat.Png);
        }
        currency.HideDropDown();
        menu.BackColor = Color.FromArgb(250, 250, 250);
        menu.ForeColor = Color.FromArgb(32, 32, 32);
        using (var bitmap = new Bitmap(menu.Width, menu.Height))
        {
            menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(Artifacts, "account-menu-light.png"), ImageFormat.Png);
        }
        menu.Close();
    }

    private static void TrayLifecycle()
    {
        string path = Path.Combine(Artifacts, "lifecycle-settings.json");
        using (var server = new LocalTickerServer())
        {
            var settings = new AppSettings { RefreshSeconds = 5 };
            var store = new SettingsStore(path);
            store.Save(settings);
            using (var context = CreateContext(store))
            {
                var tray = Field<NotifyIcon>(context, "tray");
                var menu = Field<ContextMenuStrip>(context, "menu");
                var refresh = Field<ToolStripMenuItem>(context, "refreshItem");
                PumpUntil(delegate { return tray.Text.Contains("1.76") && tray.Text.Contains("已更新"); });
                Equal(false, server.Authorized);
                Equal("设置…", menu.Items[2].Text);
                Equal("显示单位", menu.Items[3].Text);
                Equal("退出", menu.Items[5].Text);
                PumpUntil(delegate { return server.Requests >= 2; });
                server.Status = 401;
                refresh.PerformClick();
                PumpUntil(delegate { return tray.Text.Contains("401"); });
                if (!tray.Text.Contains("1.76")) throw new Exception("Last price lost on failure.");
                Equal(true, Field<bool>(context, "warning"));
                server.Status = 200;
                server.Price = "1.82";
                refresh.PerformClick();
                PumpUntil(delegate { return tray.Text.Contains("1.82") && tray.Text.Contains("已更新"); });
                Equal(false, Field<bool>(context, "warning"));
                menu.Items[menu.Items.Count - 1].PerformClick();
                Equal(false, tray.Visible);
            }
        }
        File.Delete(path);
    }

    private static void AssertTrayIcon(TrayContext context, string text)
    {
        using (var expected = TrayIconRenderer.Create(text, false))
        using (var expectedBytes = new MemoryStream())
        using (var actualBytes = new MemoryStream())
        {
            expected.Save(expectedBytes);
            Field<Icon>(context, "currentIcon").Save(actualBytes);
            Equal(Convert.ToBase64String(expectedBytes.ToArray()),
                Convert.ToBase64String(actualBytes.ToArray()));
        }
    }

    private static void CurrencyLifecycle()
    {
        string path = Path.Combine(Artifacts, "currency-settings.json");
        int rateRequests = 0;
        bool rateFails = false;
        using (var server = new LocalTickerServer())
        {
            var store = new SettingsStore(path);
            store.Save(new AppSettings { RefreshSeconds = 60 });
            var rates = new ExchangeRateClient(new FakeHandler(delegate
            {
                rateRequests++;
                return Task.FromResult(Response(rateFails ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK,
                    rateFails ? "{}" : RateJson("6.66")));
            }));
            using (var context = CreateContext(store, rates))
            {
                var tray = Field<NotifyIcon>(context, "tray");
                var cny = Field<ToolStripMenuItem>(context, "cnyItem");
                var usdt = Field<ToolStripMenuItem>(context, "usdtItem");
                var refresh = Field<ToolStripMenuItem>(context, "refreshItem");
                PumpUntil(delegate { return tray.Text.Contains("1.76") && refresh.Enabled; });
                var decimals = Field<ToolStripMenuItem[]>(context, "decimalItems");
                Equal(true, decimals[2].Checked);
                for (int places = 0; places < 3; places++)
                {
                    decimals[places].PerformClick();
                    Equal(places, store.Load().DecimalPlaces);
                    for (int index = 0; index < 3; index++)
                        Equal(index == places, decimals[index].Checked);
                    AssertTrayIcon(context, new[] { "2", "1.8", "1.76" }[places]);
                }
                Equal(0, rateRequests);
                cny.PerformClick();
                PumpUntil(delegate { return tray.Text.Contains("11.72"); });
                Equal(true, cny.Checked);
                Equal(false, usdt.Checked);
                Equal("CNY", store.Load().DisplayCurrency);
                Equal(1, rateRequests);
                Equal(true, tray.Text.Contains("CNY"));
                Equal(true, tray.Text.Contains("6.66"));
                decimals[0].PerformClick();
                AssertTrayIcon(context, "12");
                decimals[1].PerformClick();
                AssertTrayIcon(context, "11.7");
                decimals[2].PerformClick();
                AssertTrayIcon(context, "11.72");
                Equal(true, Field<ToolStripMenuItem>(context, "rateItem").Text.Contains("6.66"));
                usdt.PerformClick();
                Equal(true, tray.Text.Contains("1.76"));
                cny.PerformClick();
                Equal(1, rateRequests);
                rateFails = true;
                refresh.PerformClick();
                PumpUntil(delegate { return Field<bool>(context, "rateWarning"); });
                Equal(true, tray.Text.Contains("11.72"));
                Equal(true, tray.Text.Contains("上次汇率"));
                rateFails = false;
                refresh.PerformClick();
                PumpUntil(delegate { return !Field<bool>(context, "rateWarning"); });
                Equal(true, tray.Text.Contains("11.72"));
                context.ExitThread();
            }
            using (var restarted = CreateContext(store, new ExchangeRateClient(new FakeHandler(delegate
            {
                return Task.FromResult(Response(HttpStatusCode.OK, RateJson("6.66")));
            }))))
            {
                var tray = Field<NotifyIcon>(restarted, "tray");
                PumpUntil(delegate { return tray.Text.Contains("CNY") && tray.Text.Contains("11.72"); });
                Field<ToolStripMenuItem>(restarted, "usdtItem").PerformClick();
                Equal("USDT", store.Load().DisplayCurrency);
                Equal(true, tray.Text.Contains("1.76"));
                restarted.ExitThread();
            }
        }
        File.Delete(path);
    }

    private static void MissingRateLifecycle()
    {
        string path = Path.Combine(Artifacts, "missing-rate-settings.json");
        using (var server = new LocalTickerServer())
        {
            var store = new SettingsStore(path);
            store.Save(new AppSettings {
                RefreshSeconds = 60, DisplayCurrency = "CNY" });
            using (var context = CreateContext(store, new ExchangeRateClient(new FakeHandler(delegate
            {
                return Task.FromResult(Response(HttpStatusCode.ServiceUnavailable, "{}"));
            }))))
            {
                var tray = Field<NotifyIcon>(context, "tray");
                PumpUntil(delegate { return tray.Text.Contains("汇率获取失败") &&
                    Field<string>(context, "lastPrice") == "1.76"; });
                Equal(false, tray.Text.Contains("1.76"));
                Equal(true, tray.Text.Contains("--"));
                Field<ToolStripMenuItem>(context, "usdtItem").PerformClick();
                Equal(true, tray.Text.Contains("1.76"));
                context.ExitThread();
            }
        }
        File.Delete(path);
    }

    private static string RateJson(string rate)
    {
        return "{\"date\":\"" + DateTime.UtcNow.ToString("yyyy-MM-dd") +
            "\",\"usdt\":{\"cny\":" + rate + "}}";
    }

    private static void RateSourceLifecycle()
    {
        string path = Path.Combine(Artifacts, "rate-source-settings.json");
        bool secondSourceFails = true;
        using (var server = new LocalTickerServer())
        {
            var settings = new AppSettings
            {
                DisplayCurrency = "CNY",
                ExchangeRateUrl = "https://source-one.test/rate"
            };
            var store = new SettingsStore(path);
            store.Save(settings);
            using (var context = CreateContext(store, new ExchangeRateClient(new FakeHandler(
                delegate(HttpRequestMessage request, CancellationToken cancellation)
                {
                    if (request.RequestUri.Host == "source-one.test")
                        return Task.FromResult(Response(HttpStatusCode.OK, RateJson("6.66")));
                    Equal("source-two.test", request.RequestUri.Host);
                    return Task.FromResult(Response(secondSourceFails ? HttpStatusCode.ServiceUnavailable :
                        HttpStatusCode.OK, secondSourceFails ? "{}" : RateJson("8")));
                }))))
            {
                var tray = Field<NotifyIcon>(context, "tray");
                PumpUntil(delegate { return tray.Text.Contains("11.72"); });
                settings.ExchangeRateUrl = "https://source-two.test/rate";
                store.Save(settings);
                context.GetType().GetMethod("ApplySettings", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(context, new object[] { settings });
                PumpUntil(delegate { return tray.Text.Contains("汇率获取失败"); });
                Equal(false, tray.Text.Contains("11.72"));
                Equal(null, Field<ExchangeRateQuote>(context, "cnyRate"));
                secondSourceFails = false;
                var refresh = Field<ToolStripMenuItem>(context, "refreshItem");
                PumpUntil(delegate { return refresh.Enabled; });
                refresh.PerformClick();
                PumpUntil(delegate { return tray.Text.Contains("14.08"); });
                Equal("https://source-two.test/rate", store.Load().ExchangeRateUrl);
                context.ExitThread();
            }
        }
        File.Delete(path);
    }

    private static TrayContext CreateContext(SettingsStore store, ExchangeRateClient rates = null,
        AccountClient accounts = null)
    {
        return new TrayContext(store, rates ?? new ExchangeRateClient(), accounts ?? new AccountClient(),
            new WebhookClient(), CreateTicker());
    }

    private static TickerClient CreateTicker()
    {
        return new TickerClient(new LocalTickerHandler(LocalTickerServer.CurrentUrl));
    }

    private static void ExportFontComparison(string previousPath)
    {
        var previous = Assembly.LoadFile(Path.GetFullPath(previousPath))
            .GetType("PearlTray.TrayIconRenderer").GetMethod("Render");
        using (var sheet = new Bitmap(720, 320))
        using (var graphics = Graphics.FromImage(sheet))
        using (var label = new Font("Segoe UI", 10))
        using (var small = new Font("Segoe UI", 8))
        {
            graphics.Clear(Color.FromArgb(28, 28, 28));
            for (int row = 0; row < 4; row++)
            {
                bool old = row % 2 == 0;
                string value = row < 2 ? "1.76" : "11.73";
                graphics.DrawString((old ? "Before" : "After - Arial Bold") + "   " + value,
                    label, Brushes.White, 20, row * 80 + 24);
                int index = 0;
                foreach (int size in new[] { 16, 20, 24, 32 })
                {
                    int x = 260 + index * 110;
                    using (var bitmap = old ? (Bitmap)previous.Invoke(null,
                        new object[] { value, size, Color.White }) :
                        TrayIconRenderer.Render(value, size, Color.White))
                        graphics.DrawImageUnscaled(bitmap, x, row * 80 + (48 - size) / 2 + 4);
                    graphics.DrawString(size + " px", small, Brushes.LightGray, x - 4, row * 80 + 56);
                    index++;
                }
            }
            sheet.Save(Path.Combine(Artifacts, "tray-font-comparison.png"), ImageFormat.Png);
        }
    }

    private static T Field<T>(object target, string name)
    {
        return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }

    private static void PumpUntil(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(8);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new Exception("Tray state did not update in time.");
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static void ExportPreview()
    {
        using (var sheet = new Bitmap(720, 320))
        using (var graphics = Graphics.FromImage(sheet))
        using (var font = new Font("Segoe UI", 12))
        {
            graphics.Clear(Color.FromArgb(243, 245, 246));
            string[] labels = { "Light taskbar", "Dark taskbar", "Refresh failed" };
            for (int row = 0; row < 3; row++)
            {
                bool light = row == 0;
                graphics.FillRectangle(light ? Brushes.White : new SolidBrush(Color.FromArgb(28, 28, 28)),
                    12, 12 + row * 100, 696, 88);
                graphics.DrawString(labels[row], font, light ? Brushes.Black : Brushes.White, 26, 42 + row * 100);
                int index = 0;
                foreach (int size in new[] { 16, 20, 24, 32 })
                {
                    int x = 240 + index * 112;
                    using (var bitmap = TrayIconRenderer.Render("1.76", size,
                        TrayIconRenderer.Foreground(row == 2, light)))
                        graphics.DrawImageUnscaled(bitmap, x, 34 + row * 100);
                    using (var labelFont = new Font("Segoe UI", 8))
                        graphics.DrawString(size + " px", labelFont,
                            light ? Brushes.Black : Brushes.LightGray, x - 4, 72 + row * 100);
                    index++;
                }
            }
            sheet.Save(Path.Combine(Artifacts, "tray-preview.png"), ImageFormat.Png);
        }
    }

    private static AppSettings ValidSettings()
    {
        return new AppSettings();
    }

    private static HttpResponseMessage Response(HttpStatusCode code, string json)
    {
        return new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static TickerClient ClientWith(HttpStatusCode code, string json)
    {
        return new TickerClient(new FakeHandler(delegate { return Task.FromResult(Response(code, json)); }));
    }

    private static void Check(string name, Action action)
    {
        try { action(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!Object.Equals(expected, actual))
            throw new Exception("Expected " + expected + ", got " + actual);
    }

    private static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T error) { return error; }
        throw new Exception("Expected " + typeof(T).Name);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback;
        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback)
        {
            this.callback = callback;
        }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return callback(request, cancellationToken);
        }
    }

    private sealed class LocalTickerHandler : DelegatingHandler
    {
        private readonly Uri address;

        public LocalTickerHandler(string url) : base(new CurlTickerHandler())
        {
            address = new Uri(url);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellation)
        {
            Equal(TickerClient.PublicTickerUrl, request.RequestUri.AbsoluteUri);
            Equal(null, request.Headers.Authorization);
            request.RequestUri = address;
            return base.SendAsync(request, cancellation);
        }
    }

    private sealed class StalledStream : Stream
    {
        private readonly TaskCompletionSource<int> pending = new TaskCompletionSource<int>();
        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellation)
        {
            return pending.Task;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) pending.TrySetException(new ObjectDisposedException("StalledStream"));
            base.Dispose(disposing);
        }
        public override void Flush() { throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }

    private sealed class LocalTickerServer : IDisposable
    {
        private readonly TcpListener listener;
        private readonly Task serving;
        private volatile bool stopped;
        public volatile int Status = 200;
        public volatile bool StallHeaders;
        public volatile bool StallBody;
        private int requests;
        public int Requests { get { return Volatile.Read(ref requests); } }
        public volatile bool Authorized;
        public volatile string Price = "1.76";
        public string Url { get; private set; }
        public static string CurrentUrl { get; private set; }

        public LocalTickerServer()
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/ticker";
            CurrentUrl = Url;
            serving = Task.Run(async delegate
            {
                while (!stopped)
                {
                    try
                    {
                        using (var connection = await listener.AcceptTcpClientAsync())
                        using (var stream = connection.GetStream())
                        using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
                        {
                            string line;
                            while (!String.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                                if (line.StartsWith("Authorization:",
                                    StringComparison.OrdinalIgnoreCase)) Authorized = true;
                            Interlocked.Increment(ref requests);
                            if (StallHeaders)
                            {
                                await stream.ReadAsync(new byte[1], 0, 1);
                                continue;
                            }
                            byte[] body = Encoding.UTF8.GetBytes("{\"ticker\":{\"high\":\"1.93\",\"last\":\"" + Price + "\"}}");
                            byte[] headers = Encoding.ASCII.GetBytes("HTTP/1.1 " + Status +
                                (Status == 200 ? " OK" : " Unauthorized") +
                                "\r\nContent-Type: application/json\r\nConnection: close\r\nContent-Length: " +
                                body.Length + "\r\n\r\n");
                            await stream.WriteAsync(headers, 0, headers.Length);
                            if (StallBody)
                            {
                                await stream.ReadAsync(new byte[1], 0, 1);
                                continue;
                            }
                            await stream.WriteAsync(body, 0, body.Length);
                        }
                    }
                    catch (ObjectDisposedException) { if (!stopped) throw; }
                    catch (SocketException) { if (!stopped) throw; }
                    catch (IOException) { }
                }
            });
        }

        public void Dispose()
        {
            stopped = true;
            listener.Stop();
            if (!serving.Wait(2000)) throw new Exception("Local ticker server did not stop.");
        }
    }
}
