using System;
using System.Globalization;

namespace PearlTray
{
    public static class PriceMonitor
    {
        public const string Title = "PEARL 价格提醒";

        public static decimal ParseThreshold(string value)
        {
            decimal threshold;
            if (!Decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                out threshold) || (threshold == 0 && value.Trim('0', '.').Length != 0))
                throw new ArgumentException("价格提醒阈值须为非负数字（USDT），填 0 关闭。");
            return threshold;
        }

        public static string Evaluate(AppSettings settings, string last)
        {
            decimal price = Decimal.Parse(last, CultureInfo.InvariantCulture);
            decimal high = ParseThreshold(settings.PriceHighThreshold);
            decimal low = ParseThreshold(settings.PriceLowThreshold);
            string match = null;
            if (high > 0 && price >= high)
                match = "达到上限 " + settings.PriceHighThreshold + " USDT";
            if (low > 0 && price <= low)
                match = (match == null ? "" : match + "，") + "达到下限 " + settings.PriceLowThreshold + " USDT";
            return match == null ? null : "PEARL 现价 " + last + " USDT，" + match + "。";
        }
    }
}
