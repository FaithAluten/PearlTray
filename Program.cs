using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PearlTray
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            string config = SettingsStore.DefaultPath;
            if (args.Length == 2 && args[0] == "--config") config = Path.GetFullPath(args[1]);
            bool created;
            string identity = Convert.ToBase64String(System.Security.Cryptography.SHA256.Create()
                .ComputeHash(System.Text.Encoding.UTF8.GetBytes(config.ToUpperInvariant())))
                .Replace("/", "_");
            using (var mutex = new Mutex(true, @"Local\PearlTray." + identity, out created))
            {
                if (!created)
                {
                    MessageBox.Show("PearlTray 已在运行，请在托盘中右键数字打开设置。",
                        "PearlTray", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try
                {
                    using (var context = new TrayContext(new SettingsStore(config)))
                        Application.Run(context);
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }

    public sealed class TrayContext : ApplicationContext
    {
        public const string ExchangeUrl = "https://safetrade.com/exchange/PEARL-USDT";
        private readonly SettingsStore store;
        private readonly TickerClient client;
        private readonly ExchangeRateClient exchangeRates;
        private readonly AccountClient accounts;
        private readonly WalletClient walletClient;
        private decimal? walletBalance;
        private string walletStatus = "";
        private readonly WebhookClient webhooks;
        private HashrateMonitor hashrateMonitor = new HashrateMonitor();
        private WalletMonitor walletMonitor = new WalletMonitor();
        private string walletNotificationStatus = "";
        private string notificationStatus = "";
        private readonly List<ToolStripItem> workerItems = new List<ToolStripItem>();
        private List<AccountWorker> workers;
        private string workerStatus = "";
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripSeparator workerSeparator;
        private readonly ToolStripMenuItem refreshItem;
        private readonly ToolStripMenuItem usdtItem;
        private readonly ToolStripMenuItem cnyItem;
        private readonly ToolStripMenuItem rateItem;
        private readonly ToolStripMenuItem[] decimalItems = new ToolStripMenuItem[3];
        private readonly System.Windows.Forms.Timer timer;
        private readonly System.Windows.Forms.Timer rateTimer;
        private readonly Control dispatcher;
        private CancellationTokenSource request;
        private AppSettings settings;
        private SettingsForm settingsForm;
        private Icon currentIcon;
        private string lastPrice;
        private DateTime? lastUpdated;
        private string status = "等待配置";
        private bool warning;
        private bool exiting;
        private int revision;
        private ExchangeRateQuote cnyRate;
        private CancellationTokenSource rateRequest;
        private DateTime nextRateAttempt;
        private bool rateWarning;
        private string rateStatus = "正在获取汇率";
        private int rateRevision;

        public TrayContext(SettingsStore store)
            : this(store, new ExchangeRateClient())
        {
        }

        public TrayContext(SettingsStore store, ExchangeRateClient exchangeRates)
            : this(store, exchangeRates, new AccountClient())
        {
        }

        public TrayContext(SettingsStore store, ExchangeRateClient exchangeRates, AccountClient accounts)
            : this(store, exchangeRates, accounts, new WebhookClient()) { }

        public TrayContext(SettingsStore store, ExchangeRateClient exchangeRates, AccountClient accounts,
            WebhookClient webhooks)
            : this(store, exchangeRates, accounts, webhooks, new TickerClient()) { }

        public TrayContext(SettingsStore store, ExchangeRateClient exchangeRates, AccountClient accounts,
            WebhookClient webhooks, TickerClient client)
            : this(store, exchangeRates, accounts, webhooks, client, new WalletClient()) { }

        public TrayContext(SettingsStore store, ExchangeRateClient exchangeRates, AccountClient accounts,
            WebhookClient webhooks, TickerClient client, WalletClient walletClient)
        {
            this.store = store;
            this.client = client;
            this.exchangeRates = exchangeRates;
            this.accounts = accounts;
            this.walletClient = walletClient;
            this.webhooks = webhooks;
            // Capture a UI handle before scheduling startup and asynchronous continuations.
            dispatcher = new Control();
            var handle = dispatcher.Handle;
            menu = new TrayMenu();
            workerSeparator = new ToolStripSeparator { Visible = false };
            menu.Items.Add(workerSeparator);
            refreshItem = new TrayActionItem("立即刷新", "\uE72C");
            refreshItem.Click += async delegate
            {
                nextRateAttempt = DateTime.MinValue;
                await RefreshAsync();
            };
            menu.Items.Add(refreshItem);
            var configure = new TrayActionItem("设置…", "\uE713");
            configure.Click += delegate { OpenSettings(false); };
            menu.Items.Add(configure);
            var currency = new TrayActionItem("显示单位", "\uE8AB");
            currency.DropDown = new TraySubMenu();
            currency.DropDown.OwnerItem = currency;
            usdtItem = new ToolStripMenuItem("USDT") { Checked = true };
            cnyItem = new ToolStripMenuItem("人民币 (CNY)");
            usdtItem.Click += async delegate { await SelectCurrencyAsync("USDT"); };
            cnyItem.Click += async delegate { await SelectCurrencyAsync("CNY"); };
            rateItem = new ToolStripMenuItem("汇率：尚未获取") { Enabled = false };
            currency.DropDownItems.Add(usdtItem);
            currency.DropDownItems.Add(cnyItem);
            currency.DropDownItems.Add(new ToolStripSeparator());
            for (int i = 0; i < decimalItems.Length; i++)
            {
                int places = i;
                decimalItems[i] = new ToolStripMenuItem("保留 " + i + " 位小数");
                decimalItems[i].Click += delegate { SelectDecimalPlaces(places); };
                currency.DropDownItems.Add(decimalItems[i]);
            }
            currency.DropDownItems.Add(new ToolStripSeparator());
            currency.DropDownItems.Add(rateItem);
            menu.Items.Add(currency);
            menu.Items.Add(new ToolStripSeparator());
            var quit = new TrayActionItem("退出", "\uE7E8");
            quit.Click += delegate { ExitThread(); };
            menu.Items.Add(quit);
            tray = new NotifyIcon { Text = "PearlTray" };
            tray.MouseClick += TrayClick;
            tray.MouseUp += TrayMouseUp;
            timer = new System.Windows.Forms.Timer();
            timer.Tick += async delegate { await RefreshAsync(); };
            rateTimer = new System.Windows.Forms.Timer { Interval = 60000 };
            rateTimer.Tick += async delegate { await RefreshExchangeRateAsync(); };
            UpdateIcon();
            tray.Visible = true;
            SystemEvents.UserPreferenceChanged += ThemeChanged;
            dispatcher.BeginInvoke(new Action(Start));
        }

        private void Start()
        {
            try { settings = store.Load(); }
            catch
            {
                MessageBox.Show("无法读取已保存的配置，将使用默认设置获取价格。",
                    "PearlTray", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            ApplySettings(settings ?? new AppSettings());
        }

        private void OpenSettings(bool firstRun)
        {
            if (settingsForm != null)
            {
                settingsForm.Activate();
                return;
            }
            using (var form = new SettingsForm(settings, store, firstRun, workers, workerStatus))
            {
                settingsForm = form;
                try
                {
                    if (form.ShowDialog() == DialogResult.OK)
                        ApplySettings(form.SavedSettings);
                    else if (firstRun)
                        ExitThread();
                }
                finally { settingsForm = null; }
            }
        }

        private async void ApplySettings(AppSettings value)
        {
            timer.Stop();
            revision++;
            if (request != null) request.Cancel();
            if (settings == null || settings.ExchangeRateUrl != value.ExchangeRateUrl)
            {
                rateRevision++;
                var previousRateRequest = rateRequest;
                rateRequest = null;
                if (previousRateRequest != null) previousRateRequest.Cancel();
                cnyRate = null;
                rateWarning = false;
                rateStatus = "正在获取汇率";
                nextRateAttempt = DateTime.MinValue;
            }
            if (settings == null || settings.WalletAddress != value.WalletAddress ||
                settings.HashrateThresholds != value.HashrateThresholds)
                hashrateMonitor = new HashrateMonitor();
            if (settings == null || settings.WalletAddress != value.WalletAddress ||
                settings.WalletThreshold != value.WalletThreshold)
                walletMonitor = new WalletMonitor();
            settings = value;
            notificationStatus = "";
            walletNotificationStatus = "";
            workers = null;
            walletBalance = null;
            walletStatus = "";
            workerStatus = String.IsNullOrEmpty(settings.WalletAddress) ? "" : "正在获取矿机";
            lastPrice = null;
            lastUpdated = null;
            warning = false;
            status = "正在获取价格";
            UpdateIcon();
            var rateRefresh = RefreshExchangeRateAsync();
            rateTimer.Start();
            timer.Interval = checked(settings.RefreshSeconds * 1000);
            await RefreshAsync();
            if (!exiting) timer.Start();
            await rateRefresh;
        }

        private async Task SelectCurrencyAsync(string currency)
        {
            if (settings == null || exiting) return;
            string previous = settings.DisplayCurrency;
            settings.DisplayCurrency = currency;
            try { store.Save(settings); }
            catch
            {
                settings.DisplayCurrency = previous;
                MessageBox.Show("无法保存显示单位，请检查配置目录的写入权限。",
                    "PearlTray", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            UpdateIcon();
            await RefreshExchangeRateAsync();
        }

        private void SelectDecimalPlaces(int places)
        {
            if (settings == null || exiting) return;
            int previous = settings.DecimalPlaces;
            settings.DecimalPlaces = places;
            try { store.Save(settings); }
            catch
            {
                settings.DecimalPlaces = previous;
                MessageBox.Show("无法保存小数位数，请检查配置目录的写入权限。",
                    "PearlTray", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            UpdateIcon();
        }

        private async Task RefreshExchangeRateAsync()
        {
            if (exiting || settings == null || settings.DisplayCurrency != "CNY" ||
                rateRequest != null || DateTime.UtcNow < nextRateAttempt) return;
            var active = new CancellationTokenSource();
            rateRequest = active;
            int generation = rateRevision;
            try
            {
                var quote = await exchangeRates.FetchAsync(settings.ExchangeRateUrl, active.Token);
                if (exiting || generation != rateRevision) return;
                cnyRate = quote;
                rateWarning = cnyRate.Date < DateTime.UtcNow.Date.AddDays(-2);
                rateStatus = rateWarning ? "汇率数据较旧" : "";
                nextRateAttempt = DateTime.UtcNow.AddHours(1);
            }
            catch
            {
                if (exiting || generation != rateRevision) return;
                rateWarning = true;
                rateStatus = cnyRate == null ? "汇率获取失败，暂无法换算" : "汇率刷新失败，使用上次汇率";
                nextRateAttempt = DateTime.UtcNow.AddMinutes(1);
            }
            finally
            {
                if (Object.ReferenceEquals(rateRequest, active)) rateRequest = null;
                active.Dispose();
                if (!exiting) UpdateIcon();
            }
        }

        private async Task RefreshAsync()
        {
            if (settings == null || exiting) return;
            if (request != null && !request.IsCancellationRequested) return;
            var active = new CancellationTokenSource();
            request = active;
            int generation = revision;
            refreshItem.Enabled = false;
            AppSettings snapshot = settings;
            try
            {
                await Task.WhenAll(RefreshPriceAsync(snapshot, generation, active.Token),
                    RefreshAccountAsync(snapshot, generation, active.Token),
                    RefreshWalletAsync(snapshot, generation, active.Token));
            }
            finally
            {
                if (Object.ReferenceEquals(request, active)) request = null;
                active.Dispose();
                if (!exiting && generation == revision)
                {
                    refreshItem.Enabled = true;
                    UpdateIcon();
                }
            }
            if (!exiting && generation == revision) await RefreshExchangeRateAsync();
        }

        private async Task RefreshWalletAsync(AppSettings snapshot, int generation, CancellationToken cancellation)
        {
            if (String.IsNullOrEmpty(snapshot.WalletAddress)) return;
            try
            {
                decimal result = await walletClient.FetchAsync(snapshot.WalletAddress, cancellation);
                if (exiting || generation != revision) return;
                walletBalance = result;
                walletStatus = "";
            }
            catch
            {
                if (exiting || generation != revision) return;
                walletStatus = walletBalance.HasValue ? "余额刷新失败，显示上次数据" :
                    "余额获取失败，将在下个周期重试";
                if (!exiting && generation == revision) UpdateIcon();
                return;
            }
            await NotifyWalletAsync(snapshot, generation, cancellation);
            if (!exiting && generation == revision) UpdateIcon();
        }

        private async Task NotifyWalletAsync(AppSettings snapshot, int generation, CancellationToken cancellation)
        {
            walletNotificationStatus = "";
            var monitor = walletMonitor;
            string message = monitor.Evaluate(WalletMonitor.ParseThreshold(snapshot.WalletThreshold),
                walletBalance.Value);
            if (message == null) return;
            if (snapshot.WindowsNotifications && monitor.NeedsNotification(false))
            {
                try
                {
                    tray.ShowBalloonTip(10000, WalletMonitor.Title, message, ToolTipIcon.Info);
                    monitor.MarkSent(false);
                }
                catch { walletNotificationStatus = "钱包 Windows 系统通知发送失败"; }
            }
            if (!snapshot.WebhookNotifications || !monitor.NeedsNotification(true)) return;
            if (exiting || generation != revision || cancellation.IsCancellationRequested) return;
            try
            {
                await webhooks.SendAsync(snapshot.Webhook, WalletMonitor.Title, message, cancellation);
                if (exiting || generation != revision) return;
                monitor.MarkSent(true);
            }
            catch
            {
                if (exiting || generation != revision || cancellation.IsCancellationRequested) return;
                walletNotificationStatus = "钱包 Webhook 通知失败，将在下个周期重试";
            }
        }

        private async Task RefreshAccountAsync(AppSettings snapshot, int generation, CancellationToken cancellation)
        {
            if (String.IsNullOrEmpty(snapshot.WalletAddress)) return;
            try
            {
                var result = await accounts.FetchAsync(snapshot.WalletAddress, cancellation);
                if (exiting || generation != revision) return;
                workers = result;
                workerStatus = result.Count == 0 ? "暂无在线矿机" : "";
            }
            catch
            {
                if (exiting || generation != revision) return;
                workerStatus = workers == null ? "矿机获取失败，将在下个周期重试" :
                    "矿机刷新失败，显示上次数据";
                if (!exiting && generation == revision) UpdateIcon();
                return;
            }
            await NotifyHashrateAsync(snapshot, generation, cancellation);
            if (!exiting && generation == revision) UpdateIcon();
        }

        private async Task NotifyHashrateAsync(AppSettings snapshot, int generation, CancellationToken cancellation)
        {
            notificationStatus = "";
            List<HashrateAlert> alerts;
            var monitor = hashrateMonitor;
            try
            {
                alerts = monitor.Evaluate(snapshot, workers);
            }
            catch (ArgumentException error)
            {
                notificationStatus = error.Message;
                return;
            }
            var windowsAlerts = alerts.FindAll(alert =>
                monitor.NeedsNotification(false));
            if (snapshot.WindowsNotifications && windowsAlerts.Count > 0)
            {
                try
                {
                    string message = String.Join("\n", windowsAlerts.ConvertAll(alert => alert.Message));
                    tray.ShowBalloonTip(10000, windowsAlerts[0].Title, message,
                        windowsAlerts[0].IsRecovery ? ToolTipIcon.Info : ToolTipIcon.Warning);
                    monitor.MarkSent(false);
                }
                catch { notificationStatus = "Windows 系统通知发送失败"; }
            }
            if (!snapshot.WebhookNotifications) return;
            foreach (var alert in alerts)
            {
                if (exiting || generation != revision || cancellation.IsCancellationRequested) return;
                if (!monitor.NeedsNotification(true)) continue;
                try
                {
                    await webhooks.SendAsync(snapshot.Webhook, alert.Title, alert.Message, cancellation);
                    if (exiting || generation != revision) return;
                    monitor.MarkSent(true);
                }
                catch
                {
                    if (exiting || generation != revision || cancellation.IsCancellationRequested) return;
                    notificationStatus = "Webhook 通知失败，将在下个周期重试";
                }
            }
        }

        private async Task RefreshPriceAsync(AppSettings snapshot, int generation, CancellationToken cancellation)
        {
            try
            {
                TickerResult result = await client.FetchAsync(snapshot, cancellation);
                if (exiting || generation != revision) return;
                lastPrice = result.Last;
                warning = result.IsStale;
                if (!result.IsStale) lastUpdated = DateTime.Now;
                status = result.IsStale ? "上游刷新失败，当前为缓存价格" : "已更新";
            }
            catch (Exception error)
            {
                if (exiting || generation != revision) return;
                warning = true;
                if (error is OperationCanceledException)
                    status = "请求超时，将在下个周期重试";
                else if (error is System.Net.Http.HttpRequestException)
                    status = error.Message.StartsWith("接口") || error.Message.StartsWith("认证") ||
                        error.Message.StartsWith("请求") ? error.Message : "网络连接失败，将在下个周期重试";
                else if (error is InvalidDataException)
                    status = error.Message;
                else
                    status = "获取价格失败，将在下个周期重试";
            }
            if (!exiting && generation == revision) UpdateIcon();
        }

        private void UpdateIcon()
        {
            if (exiting) return;
            bool cny = settings != null && settings.DisplayCurrency == "CNY";
            string shownPrice = lastPrice;
            string iconPrice = lastPrice;
            string displayStatus = status;
            bool displayWarning = warning || (cny && rateWarning);
            if (cny)
            {
                if (cnyRate == null)
                {
                    shownPrice = null;
                    displayStatus = rateStatus;
                }
                else
                {
                    if (rateWarning) displayStatus = rateStatus + "；" + status;
                    try
                    {
                        if (lastPrice != null)
                        {
                            shownPrice = ExchangeRateClient.ConvertToCny(lastPrice, cnyRate.CnyPerUsdt);
                            iconPrice = ExchangeRateClient.ConvertToCny(lastPrice, cnyRate.CnyPerUsdt, true);
                        }
                    }
                    catch (OverflowException)
                    {
                        shownPrice = null;
                        displayWarning = true;
                        displayStatus = "价格超出换算范围";
                    }
                }
            }
            string text = shownPrice == null ? (displayWarning ? "!" : "--") :
                TrayIconRenderer.Compact(iconPrice, settings == null ? 2 : settings.DecimalPlaces);
            Icon next = TrayIconRenderer.Create(text, displayWarning);
            tray.Icon = next;
            if (currentIcon != null) currentIcon.Dispose();
            currentIcon = next;
            string unit = cny ? "CNY" : "USDT";
            string tooltip = "PEARL/" + unit + "  " + (shownPrice ?? "--") + "\n" + displayStatus;
            if (cny && cnyRate != null)
                tooltip += "\n1 USDT≈" + cnyRate.CnyPerUsdt.ToString("0.####",
                    CultureInfo.InvariantCulture) + "元 (" + cnyRate.Date.ToString("MM-dd") + ")";
            if (lastUpdated.HasValue) tooltip += "\n更新 " + lastUpdated.Value.ToString("MM-dd HH:mm:ss");
            tray.Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip;
            foreach (var item in workerItems)
            {
                menu.Items.Remove(item);
                item.Dispose();
            }
            workerItems.Clear();
            int position = 0;
            var workerRows = new List<ToolStripItem>();
            if (settings != null && !String.IsNullOrEmpty(settings.WalletAddress))
            {
                workerRows.Add(new MetricMenuItem("Pearl Wallet  ", walletBalance.HasValue ?
                    WalletClient.FormatBalance(walletBalance.Value) : "--", " PRL")
                    { ValueColor = Color.FromArgb(229, 188, 100) });
                if (!String.IsNullOrEmpty(walletStatus))
                    workerRows.Add(new ToolStripMenuItem(walletStatus) { Enabled = false, AutoToolTip = false });
            }
            if (workers != null)
            {
                foreach (var worker in workers)
                {
                    workerRows.Add(new WorkerMenuHeader(worker.Name));
                    foreach (var gpu in worker.Gpus)
                        workerRows.Add(new MetricMenuItem(AccountClient.DisplayGpuName(gpu.Name) +
                            "  ", (gpu.Hashrate / 1000000000000m).ToString("0.00",
                                CultureInfo.InvariantCulture), " TH/s"));
                }
            }
            if (!String.IsNullOrEmpty(workerStatus))
                workerRows.Add(new ToolStripMenuItem(workerStatus) { Enabled = false, AutoToolTip = false });
            if (!String.IsNullOrEmpty(notificationStatus))
                workerRows.Add(new ToolStripMenuItem(notificationStatus) { Enabled = false, AutoToolTip = false });
            if (!String.IsNullOrEmpty(walletNotificationStatus))
                workerRows.Add(new ToolStripMenuItem(walletNotificationStatus) { Enabled = false, AutoToolTip = false });
            if (settingsForm != null)
                settingsForm.UpdateHashrates(workers, workerStatus);
            foreach (var item in workerRows)
            {
                workerItems.Add(item);
                menu.Items.Insert(position++, item);
            }
            workerSeparator.Visible = workerRows.Count > 0;
            usdtItem.Checked = !cny;
            cnyItem.Checked = cny;
            for (int i = 0; i < decimalItems.Length; i++)
                decimalItems[i].Checked = i == (settings == null ? 2 : settings.DecimalPlaces);
            rateItem.Text = cnyRate == null ? "汇率：尚未获取" : "1 USDT ≈ " +
                cnyRate.CnyPerUsdt.ToString("0.####", CultureInfo.InvariantCulture) +
                " 元（" + cnyRate.Date.ToString("yyyy-MM-dd") + "）";
            if (rateWarning) rateItem.Text += " · " + rateStatus;
            if (menu.Visible) ((TrayMenu)menu).RefreshWorkerScroll();
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        private void TrayMouseUp(object sender, MouseEventArgs args)
        {
            if (args.Button != MouseButtons.Right || exiting) return;
            // Foreground ownership lets outside clicks dismiss the tray menu.
            SetForegroundWindow(menu.Handle);
            menu.Show(Cursor.Position, ToolStripDropDownDirection.AboveRight);
        }

        private void TrayClick(object sender, MouseEventArgs args)
        {
            if (args.Button != MouseButtons.Left) return;
            try { Process.Start(new ProcessStartInfo(ExchangeUrl) { UseShellExecute = true }); }
            catch
            {
                MessageBox.Show("无法打开默认浏览器。", "PearlTray",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ThemeChanged(object sender, UserPreferenceChangedEventArgs args)
        {
            if (!exiting && dispatcher.IsHandleCreated)
            {
                try { dispatcher.BeginInvoke(new Action(UpdateIcon)); }
                catch (InvalidOperationException) { }
            }
        }

        protected override void ExitThreadCore()
        {
            if (exiting) return;
            exiting = true;
            timer.Stop();
            rateTimer.Stop();
            if (request != null) request.Cancel();
            if (rateRequest != null) rateRequest.Cancel();
            tray.Visible = false;
            base.ExitThreadCore();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                exiting = true;
                SystemEvents.UserPreferenceChanged -= ThemeChanged;
                timer.Dispose();
                rateTimer.Dispose();
                if (request != null) request.Cancel();
                if (rateRequest != null) rateRequest.Cancel();
                tray.Dispose();
                menu.Dispose();
                if (currentIcon != null) currentIcon.Dispose();
                client.Dispose();
                exchangeRates.Dispose();
                accounts.Dispose();
                walletClient.Dispose();
                webhooks.Dispose();
                dispatcher.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
