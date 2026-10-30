using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PearlTray
{
    public sealed class HashrateAlert
    {
        public decimal Threshold { get; set; }
        public decimal Actual { get; set; }
        public bool IsRecovery { get; set; }
        public string Title
        {
            get { return IsRecovery ? "PearlTray 算力恢复" : "PearlTray 算力告警"; }
        }
        public string Message
        {
            get
            {
                return (IsRecovery ? "总算力恢复：当前 " : "总算力过低：当前 ") +
                    Actual.ToString("0.00", CultureInfo.InvariantCulture) + " TH/s，阈值 " +
                    Threshold.ToString("0.########", CultureInfo.InvariantCulture) + " TH/s。";
            }
        }
    }

    public sealed class HashrateMonitor
    {
        private bool windowsSent;
        private bool webhookSent;
        private bool wasLow;
        private bool recoveryPending;

        public static decimal ParseThreshold(string value)
        {
            decimal result;
            if (!Decimal.TryParse((value ?? "").Trim(), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out result) || result < 0 ||
                result > Decimal.MaxValue / 1000000000000m)
                throw new ArgumentException("总算力阈值须为单个非负数，单位 TH/s；0 关闭告警。");
            return result;
        }

        public static decimal Total(IEnumerable<AccountWorker> workers)
        {
            return workers.Sum(worker => worker.Gpus.Sum(gpu => gpu.Hashrate / 1000000000000m));
        }

        public List<HashrateAlert> Evaluate(AppSettings settings, List<AccountWorker> workers)
        {
            var threshold = ParseThreshold(settings.HashrateThresholds);
            var alerts = new List<HashrateAlert>();
            decimal actual = Total(workers);
            if (threshold == 0)
            {
                wasLow = false;
                recoveryPending = false;
                windowsSent = false;
                webhookSent = false;
                return alerts;
            }
            bool isLow = actual < threshold;
            if (isLow != wasLow)
            {
                recoveryPending = wasLow && !isLow;
                windowsSent = false;
                webhookSent = false;
            }
            wasLow = isLow;
            // Keep the current event available so each channel can retry independently.
            if (isLow || recoveryPending)
                alerts.Add(new HashrateAlert
                {
                    Threshold = threshold, Actual = actual, IsRecovery = recoveryPending
                });
            return alerts;
        }

        public bool NeedsNotification(bool webhook)
        {
            return !(webhook ? webhookSent : windowsSent);
        }

        public void MarkSent(bool webhook)
        {
            if (webhook) webhookSent = true;
            else windowsSent = true;
        }
    }
}
