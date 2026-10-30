using System;
using System.Globalization;

namespace PearlTray
{
    public sealed class WalletMonitor
    {
        public const string Title = "PearlTray 钱包余额通知";
        private decimal? previous;
        private bool pending;
        private bool windowsSent;
        private bool webhookSent;

        public static decimal ParseThreshold(string value)
        {
            decimal result;
            if (!Decimal.TryParse((value ?? "").Trim(), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out result) || result < 0)
                throw new ArgumentException("钱包阈值须为单个非负数，单位 PRL；0 关闭通知。");
            return result;
        }

        public string Evaluate(decimal threshold, decimal balance)
        {
            if (threshold == 0)
            {
                previous = null;
                pending = false;
                windowsSent = false;
                webhookSent = false;
                return null;
            }
            if (balance < threshold)
                pending = false;
            else if (previous.HasValue && previous.Value < threshold)
            {
                pending = true;
                windowsSent = false;
                webhookSent = false;
            }
            previous = balance;
            // Retain a crossing for channels that failed to send until the balance drops again.
            return pending ? "钱包余额达到阈值：当前 " +
                balance.ToString("0.########", CultureInfo.InvariantCulture) + " PRL，阈值 " +
                threshold.ToString("0.########", CultureInfo.InvariantCulture) + " PRL。" : null;
        }

        public bool NeedsNotification(bool webhook)
        {
            return pending && !(webhook ? webhookSent : windowsSent);
        }

        public void MarkSent(bool webhook)
        {
            if (webhook) webhookSent = true;
            else windowsSent = true;
        }
    }
}
