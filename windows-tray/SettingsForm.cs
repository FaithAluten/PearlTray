using System;
using System.Drawing;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;

namespace PearlTray
{
    public sealed class SettingsForm : SettingsWindow
    {
        private readonly TextBox exchangeRateUrl;
        private readonly TextBox walletAddress;
        private readonly NumericUpDown interval;
        private readonly Label error;
        private readonly SettingsStore store;
        private readonly AppSettings currentSettings;
        private readonly TextBox thresholds;
        private readonly CheckBox windowsNotifications;
        private readonly CheckBox webhookNotifications;
        private readonly Label totals;
        private readonly ToolTip totalTip = new ToolTip();
        private WebhookSettings webhook;

        public AppSettings SavedSettings { get; private set; }

        public SettingsForm(AppSettings current, SettingsStore store, bool firstRun)
            : this(current, store, firstRun, null, "") { }

        public SettingsForm(AppSettings current, SettingsStore store, bool firstRun,
            List<AccountWorker> workers, string workerStatus)
            : base(firstRun ? "PearlTray · 首次设置" : "PearlTray · 设置",
                firstRun ? "首次设置" : "设置", new Size(680, 800), firstRun)
        {
            this.store = store;
            current = current ?? new AppSettings();
            currentSettings = current;
            webhook = current.Webhook;
            StartPosition = FormStartPosition.CenterScreen;
            SetScrollName("settingsScroll");

            AddSection("行情与汇率", "\uE774");
            exchangeRateUrl = AddTextField("USDT/CNY 汇率接口 URL", current.ExchangeRateUrl, false);

            AddSection("矿机监控", "\uE950");
            walletAddress = AddTextField("钱包地址（可选）", current.WalletAddress, false);
            walletAddress.AccessibleName = "钱包地址";
            var monitorRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2,
                Margin = Padding.Empty
            };
            monitorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152));
            monitorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var intervalLabel = FieldLabel("刷新周期（秒）");
            var thresholdLabel = FieldLabel("总算力阈值（TH/s；0 关闭）");
            intervalLabel.Margin = new Padding(0, 0, 16, 5);
            monitorRow.Controls.Add(intervalLabel, 0, 0);
            monitorRow.Controls.Add(thresholdLabel, 1, 0);
            interval = new NumericUpDown
            {
                Minimum = 5, Maximum = 86400,
                Value = Math.Max(5, Math.Min(86400, current.RefreshSeconds)),
                Font = TrayMenuFonts.Regular, BorderStyle = BorderStyle.None,
                AccessibleName = "刷新周期（秒）"
            };
            var intervalFrame = Frame(interval, 38);
            intervalFrame.Margin = new Padding(0, 0, 16, 10);
            monitorRow.Controls.Add(intervalFrame, 0, 1);
            thresholds = new TextBox
            {
                Text = current.HashrateThresholds, Font = TrayMenuFonts.Regular,
                BorderStyle = BorderStyle.None, AccessibleName = "总算力阈值（TH/s）"
            };
            Tip(thresholds, "所有机器的总算力低于此值时告警；0 关闭告警。");
            monitorRow.Controls.Add(Frame(thresholds, 38), 1, 1);
            Fields.Controls.Add(monitorRow);

            var summary = new TableLayoutPanel
            {
                Name = "hashrateSummary", Dock = DockStyle.Top, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1,
                Margin = Padding.Empty
            };
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            summary.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var totalLabel = FieldLabel("总算力");
            totalLabel.Tag = "muted";
            totalLabel.Dock = DockStyle.Fill;
            totalLabel.Margin = Padding.Empty;
            totalLabel.TextAlign = ContentAlignment.MiddleLeft;
            totals = new Label
            {
                AutoSize = true, Anchor = AnchorStyles.Right, MinimumSize = new Size(150, 28),
                Font = TrayMenuFonts.Emphasis, TextAlign = ContentAlignment.MiddleRight,
                Margin = new Padding(16, 0, 0, 0), Tag = "accent"
            };
            summary.Controls.Add(totalLabel, 0, 0);
            summary.Controls.Add(totals, 1, 0);
            Fields.Controls.Add(summary);

            AddSection("通知方式", "\uE7F4");
            windowsNotifications = new SettingsToggle("Windows 系统通知", current.WindowsNotifications);
            Fields.Controls.Add(windowsNotifications);
            var notificationRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top, ColumnCount = 2, Height = 44, Margin = Padding.Empty
            };
            notificationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            notificationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
            webhookNotifications = new SettingsToggle("Webhook", current.WebhookNotifications);
            var webhookButton = new SettingsButton("配置", "\uE713", false)
            {
                Name = "webhookConfigure", Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 3),
                AccessibleName = "Webhook 设置"
            };
            webhookButton.Click += delegate
            {
                using (var form = new WebhookSettingsForm(webhook))
                    if (form.ShowDialog(this) == DialogResult.OK) webhook = form.SavedSettings;
            };
            notificationRow.Controls.Add(webhookNotifications, 0, 0);
            notificationRow.Controls.Add(webhookButton, 1, 0);
            Fields.Controls.Add(notificationRow);
            UpdateHashrates(workers, workerStatus);
            error = Error;
            SaveButton.Name = "settingsSave";
            DismissButton.Name = "settingsCancel";
            SaveButton.Click += Save;
            ResumeLayout(true);
        }

        public void UpdateHashrates(List<AccountWorker> workers, string status)
        {
            totals.Text = workers == null ? "-- TH/s" :
                HashrateMonitor.Total(workers).ToString("0.00", CultureInfo.InvariantCulture) + " TH/s";
            totalTip.SetToolTip(totals, status ?? "");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) totalTip.Dispose();
            base.Dispose(disposing);
        }

        private void Save(object sender, EventArgs args)
        {
            var settings = new AppSettings
            {
                RefreshSeconds = Decimal.ToInt32(interval.Value),
                DisplayCurrency = currentSettings.DisplayCurrency,
                DecimalPlaces = currentSettings.DecimalPlaces,
                ExchangeRateUrl = exchangeRateUrl.Text.Trim(),
                WalletAddress = walletAddress.Text.Trim(),
                HashrateThresholds = thresholds.Text.Trim(),
                WindowsNotifications = windowsNotifications.Checked,
                WebhookNotifications = webhookNotifications.Checked,
                Webhook = webhook
            };
            try
            {
                settings.Validate();
                store.Save(settings);
                SavedSettings = settings;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (ArgumentException validation) { error.Text = validation.Message; }
            catch (Exception) { error.Text = "无法保存配置，请检查配置文件所在目录的写入权限。"; }
        }
    }
}
