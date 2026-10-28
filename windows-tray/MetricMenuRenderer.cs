using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PearlTray
{
    internal static class TrayMenuFonts
    {
        private static readonly PrivateFontCollection regularFonts = new PrivateFontCollection();
        private static readonly PrivateFontCollection emphasisFonts = new PrivateFontCollection();
        public static readonly Font Regular = Load(regularFonts, "Regular");
        public static readonly Font Emphasis = Load(emphasisFonts, "ExtraBold");
        private static readonly Font chinese = new Font("Microsoft YaHei UI", 9.75f);
        private static readonly Font chineseEmphasis = new Font("Microsoft YaHei UI", 9.75f, FontStyle.Bold);

        public static Font ForText(string text, bool emphasis)
        {
            foreach (char character in text)
                if (character >= '\u2E80')
                    return emphasis ? chineseEmphasis : chinese;
            return emphasis ? Emphasis : Regular;
        }

        [DllImport("gdi32.dll")]
        private static extern IntPtr AddFontMemResourceEx(IntPtr data, uint size,
            IntPtr reserved, ref uint count);

        [DllImport("gdi32.dll")]
        private static extern bool RemoveFontMemResourceEx(IntPtr handle);

        private static Font Load(PrivateFontCollection collection, string weight)
        {
            using (Stream stream = typeof(TrayMenuFonts).Assembly.GetManifestResourceStream(
                "PearlTray.JetBrainsMono-" + weight + ".ttf"))
            {
                if (stream == null) throw new InvalidOperationException("Menu font resource is missing.");
                byte[] data;
                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    data = buffer.ToArray();
                }
                IntPtr memory = Marshal.AllocCoTaskMem(data.Length);
                Marshal.Copy(data, 0, memory, data.Length);
                uint count = 0;
                IntPtr handle = AddFontMemResourceEx(memory, (uint)data.Length, IntPtr.Zero, ref count);
                if (handle == IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(memory);
                    throw new InvalidOperationException("Could not register the menu font.");
                }
                // GDI TextRenderer and GDI+ both need the private font for the process lifetime.
                collection.AddMemoryFont(memory, data.Length);
                var font = new Font(collection.Families[0], 9.75f, FontStyle.Regular, GraphicsUnit.Point);
                AppDomain.CurrentDomain.ProcessExit += delegate
                {
                    font.Dispose();
                    collection.Dispose();
                    RemoveFontMemResourceEx(handle);
                    Marshal.FreeCoTaskMem(memory);
                };
                return font;
            }
        }
    }

    public sealed class WorkerMenuHeader : ToolStripMenuItem
    {
        public WorkerMenuHeader(string name) : base(name)
        {
            Enabled = false;
            AutoToolTip = false;
            Font = TrayMenuFonts.ForText(name, true);
        }
    }

    public sealed class TrayActionItem : ToolStripMenuItem
    {
        public string Glyph { get; private set; }

        public TrayActionItem(string text, string glyph) : base(text)
        {
            Glyph = glyph;
        }
    }

    public sealed class MetricMenuItem : ToolStripMenuItem
    {
        public int ValueStart { get; private set; }
        public int ValueLength { get; private set; }

        public MetricMenuItem(string label, string value, string suffix)
        {
            Enabled = false;
            AutoToolTip = false;
            SetMetric(label, value, suffix);
        }

        public void SetMetric(string label, string value, string suffix)
        {
            ValueStart = label.Length;
            ValueLength = value == "--" ? 0 : value.Length;
            Text = label + value + suffix;
        }

        public override Size GetPreferredSize(Size constrainingSize)
        {
            Size size = base.GetPreferredSize(constrainingSize);
            // Reserve a real column gap in addition to the native menu's text gutter.
            size.Width += Font.Height * 3;
            return size;
        }
    }

    public sealed class TrayMenu : ContextMenuStrip
    {
        private readonly List<ToolStripItem> scrollRows = new List<ToolStripItem>();
        private readonly ToolStripMenuItem scrollUp;
        private readonly ToolStripMenuItem scrollDown;
        private int firstRow;
        private int visibleRows;
        private int viewportHeight;
        private int wheelDelta;
        private bool arranging;

        public TrayMenu()
        {
            TrayMenuStyle.Initialize(this);
            scrollUp = new ScrollMenuItem("\u25B2", delegate { ScrollRows(-1); });
            scrollDown = new ScrollMenuItem("\u25BC", delegate { ScrollRows(1); });
            scrollUp.AutoToolTip = scrollDown.AutoToolTip = false;
        }

        private sealed class ScrollMenuItem : ToolStripMenuItem
        {
            private readonly Action scroll;
            public ScrollMenuItem(string text, Action scroll) : base(text) { this.scroll = scroll; }
            protected override void OnClick(EventArgs e) { scroll(); }
        }

        protected override void OnOpening(System.ComponentModel.CancelEventArgs e)
        {
            firstRow = 0;
            wheelDelta = 0;
            RefreshWorkerScroll();
            base.OnOpening(e);
        }

        public void RefreshWorkerScroll()
        {
            if (arranging) return;
            arranging = true;
            SuspendLayout();
            try
            {
                foreach (var row in scrollRows)
                    if (!row.IsDisposed && row.Owner == this) row.Available = true;
                scrollRows.Clear();
                Items.Remove(scrollUp);
                Items.Remove(scrollDown);
                scrollDown.Margin = Padding.Empty;
                MinimumSize = Size.Empty;
                TrayMenuStyle.Prepare(this);
                int height = Padding.Vertical;
                bool workerSection = true;
                foreach (ToolStripItem item in Items)
                {
                    if (item is ToolStripSeparator) workerSection = false;
                    if (!item.Available) continue;
                    height += item.Height + item.Margin.Vertical;
                    if (workerSection) scrollRows.Add(item);
                }
                int limit = Math.Min(TrayMenuStyle.Scale(this, 640),
                    Screen.FromPoint(Visible ? Location : Cursor.Position).WorkingArea.Height - 24);
                if (height <= limit || scrollRows.Count == 0)
                {
                    scrollRows.Clear();
                    firstRow = 0;
                    return;
                }
                int fixedHeight = height;
                int rowHeight = 1;
                foreach (var row in scrollRows)
                {
                    fixedHeight -= row.Height + row.Margin.Vertical;
                    rowHeight = Math.Max(rowHeight, row.Height + row.Margin.Vertical);
                }
                Items.Insert(0, scrollUp);
                Items.Insert(scrollRows.Count + 1, scrollDown);
                TrayMenuStyle.Prepare(this);
                fixedHeight += scrollUp.Height + scrollDown.Height;
                visibleRows = Math.Max(1, Math.Min(scrollRows.Count,
                    (limit - fixedHeight) / rowHeight));
                viewportHeight = visibleRows * rowHeight;
                // Keep the commands in place when worker headers and GPU rows differ in height.
                MinimumSize = new Size(MinimumSize.Width, fixedHeight + viewportHeight);
                firstRow = Math.Max(0, Math.Min(firstRow, scrollRows.Count - visibleRows));
                ShowScrollRows();
            }
            finally
            {
                ResumeLayout(true);
                arranging = false;
            }
        }

        private void ShowScrollRows()
        {
            int height = 0;
            for (int i = 0; i < scrollRows.Count; i++)
            {
                scrollRows[i].Available = i >= firstRow && i < firstRow + visibleRows;
                if (scrollRows[i].Available)
                    height += scrollRows[i].Height + scrollRows[i].Margin.Vertical;
            }
            scrollDown.Margin = new Padding(0, Math.Max(0, viewportHeight - height), 0, 0);
            scrollUp.Enabled = firstRow > 0;
            scrollDown.Enabled = firstRow + visibleRows < scrollRows.Count;
        }

        private void ScrollRows(int count)
        {
            if (scrollRows.Count == 0) return;
            int next = Math.Max(0, Math.Min(firstRow + count, scrollRows.Count - visibleRows));
            if (next == firstRow) return;
            firstRow = next;
            SuspendLayout();
            try { ShowScrollRows(); }
            finally { ResumeLayout(true); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (scrollRows.Count == 0)
            {
                base.OnMouseWheel(e);
                return;
            }
            wheelDelta += e.Delta;
            int steps = wheelDelta / SystemInformation.MouseWheelScrollDelta;
            wheelDelta %= SystemInformation.MouseWheelScrollDelta;
            int lines = SystemInformation.MouseWheelScrollLines;
            if (lines < 0) lines = visibleRows;
            ScrollRows(-steps * lines);
            var handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
        }

        protected override void OnItemClicked(ToolStripItemClickedEventArgs e)
        {
            if (e.ClickedItem == scrollUp || e.ClickedItem == scrollDown) return;
            base.OnItemClicked(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                scrollUp.Dispose();
                scrollDown.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            TrayMenuStyle.Round(this);
        }
    }

    public sealed class TraySubMenu : ToolStripDropDownMenu
    {
        public TraySubMenu() { TrayMenuStyle.Initialize(this); }

        protected override void OnOpening(System.ComponentModel.CancelEventArgs e)
        {
            TrayMenuStyle.Prepare(this);
            base.OnOpening(e);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            TrayMenuStyle.Round(this);
        }
    }

    internal static class TrayMenuStyle
    {
        public static void Initialize(ToolStripDropDownMenu menu)
        {
            menu.Font = TrayMenuFonts.Regular;
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = false;
            menu.Renderer = new MetricMenuRenderer();
            menu.DropShadowEnabled = true;
        }

        public static int Scale(ToolStrip menu, int value)
        {
            return (int)Math.Round(value * menu.Font.Height / 16f);
        }

        public static void Prepare(ToolStripDropDownMenu menu)
        {
            var palette = new SettingsPalette(SettingsPalette.SystemLight());
            menu.BackColor = palette.Background;
            menu.ForeColor = palette.Text;
            menu.Padding = new Padding(Scale(menu, 6));
            menu.MinimumSize = new Size(Scale(menu, 244), 0);
            int itemWidth = menu.MinimumSize.Width - Scale(menu, 9);
            foreach (ToolStripItem item in menu.Items)
            {
                item.Font = TrayMenuFonts.ForText(item.Text, item is WorkerMenuHeader);
                item.Padding = item is ToolStripSeparator ?
                    new Padding(0, Scale(menu, 3), 0, Scale(menu, 3)) :
                    new Padding(Scale(menu, item is TrayActionItem ? 34 : 12),
                        Scale(menu, item is WorkerMenuHeader ? 9 : 7),
                        Scale(menu, 22), Scale(menu, item is WorkerMenuHeader ? 4 : 7));
                item.Margin = item is WorkerMenuHeader ?
                    new Padding(0, Scale(menu, 3), 0, 0) : Padding.Empty;
                itemWidth = Math.Max(itemWidth, item.GetPreferredSize(Size.Empty).Width);
            }
            // Native dropdowns do not stretch short items to the menu's minimum width.
            foreach (ToolStripItem item in menu.Items)
            {
                Size preferred = item.GetPreferredSize(Size.Empty);
                item.AutoSize = false;
                item.Size = new Size(itemWidth, preferred.Height);
            }
            Round(menu);
        }

        public static GraphicsPath Rounded(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (diameter <= 0) return path;
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void Round(ToolStrip menu)
        {
            if (menu.Width < 2 || menu.Height < 2) return;
            using (var path = Rounded(menu.ClientRectangle, Scale(menu, 8)))
            {
                Region previous = menu.Region;
                menu.Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
        }
    }

    public sealed class MetricMenuRenderer : ToolStripProfessionalRenderer
    {
        public MetricMenuRenderer() { RoundedEdges = false; }

        private static bool Light(ToolStrip menu) { return menu.BackColor.GetBrightness() >= 0.5f; }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(e.ToolStrip.BackColor);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = TrayMenuStyle.Rounded(bounds, TrayMenuStyle.Scale(e.ToolStrip, 8)))
            using (var pen = new Pen(Light(e.ToolStrip) ? Color.FromArgb(218, 218, 218) :
                Color.FromArgb(68, 68, 68)))
                e.Graphics.DrawPath(pen, path);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Enabled || !e.Item.Selected) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int inset = TrayMenuStyle.Scale(e.ToolStrip, 2);
            int width = Math.Min(e.Item.Width, e.ToolStrip.Width - e.Item.Bounds.Left);
            using (var path = TrayMenuStyle.Rounded(new Rectangle(inset, 1,
                width - inset * 2, e.Item.Height - 2), TrayMenuStyle.Scale(e.ToolStrip, 4)))
            using (var brush = new SolidBrush(Light(e.ToolStrip) ? Color.FromArgb(237, 239, 242) :
                Color.FromArgb(51, 53, 56)))
                e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int inset = TrayMenuStyle.Scale(e.ToolStrip, 8);
            using (var pen = new Pen(Light(e.ToolStrip) ? Color.FromArgb(228, 228, 228) :
                Color.FromArgb(62, 62, 62)))
                e.Graphics.DrawLine(pen, inset, e.Item.Height / 2,
                    e.Item.Width - inset, e.Item.Height / 2);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item.Owner.ForeColor;
            int inset = TrayMenuStyle.Scale(e.Item.Owner, 12);
            int width = Math.Min(e.Item.Width, e.Item.Owner.Width - e.Item.Bounds.Left);
            e.ArrowRectangle = new Rectangle(width - inset - 8,
                (e.Item.Height - 10) / 2, 8, 10);
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            bool light = Light(e.ToolStrip);
            Color muted = light ? Color.FromArgb(111, 115, 121) : Color.FromArgb(156, 161, 169);
            Color accent = Color.FromArgb(118, 185, 0);
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            var metric = e.Item as MetricMenuItem;
            if (metric == null)
            {
                int left = TrayMenuStyle.Scale(e.ToolStrip, e.Item is TrayActionItem ? 36 : 12);
                Rectangle textBounds = new Rectangle(left, 0, Math.Max(0,
                    e.Item.Width - left - TrayMenuStyle.Scale(e.ToolStrip, 28)), e.Item.Height);
                if (e.Item is WorkerMenuHeader)
                {
                    TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, textBounds,
                        muted, flags | TextFormatFlags.EndEllipsis);
                    return;
                }
                e.TextColor = e.Item.Enabled ? e.ToolStrip.ForeColor : muted;
                var action = e.Item as TrayActionItem;
                if (action != null)
                {
                    using (var font = new Font("Segoe MDL2 Assets", e.TextFont.Size, FontStyle.Regular))
                        TextRenderer.DrawText(e.Graphics, action.Glyph, font,
                            new Rectangle(TrayMenuStyle.Scale(e.ToolStrip, 12), 0,
                                TrayMenuStyle.Scale(e.ToolStrip, 16), e.Item.Height),
                            e.TextColor, flags | TextFormatFlags.HorizontalCenter);
                }
                // Currency selections share the text gutter and use a trailing check.
                var item = e.Item as ToolStripMenuItem;
                if (item != null && item.Checked)
                {
                    int x = e.Item.Width - TrayMenuStyle.Scale(e.ToolStrip, 22);
                    int y = e.Item.Height / 2;
                    int step = TrayMenuStyle.Scale(e.ToolStrip, 3);
                    using (var pen = new Pen(accent, Math.Max(1.5f, step / 2f)))
                        e.Graphics.DrawLines(pen, new[] { new Point(x - step, y),
                            new Point(x, y + step), new Point(x + step * 2, y - step) });
                }
                TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, textBounds,
                    e.TextColor, flags | TextFormatFlags.EndEllipsis);
                return;
            }

            string prefix = metric.Text.Substring(0, metric.ValueStart).TrimEnd();
            string value = metric.Text.Substring(metric.ValueStart, metric.ValueLength);
            string suffix = metric.Text.Substring(metric.ValueStart + metric.ValueLength);
            int insetX = TrayMenuStyle.Scale(e.ToolStrip, 12);
            int right = e.ToolStrip.ClientSize.Width - e.Item.Bounds.Left -
                e.ToolStrip.Padding.Right - insetX;
            Font valueFont = TrayMenuFonts.Emphasis;
            {
                int suffixWidth = TextRenderer.MeasureText(e.Graphics, suffix, e.TextFont,
                    Size.Empty, flags).Width;
                int valueWidth = value.Length == 0 ? 0 :
                    TextRenderer.MeasureText(e.Graphics, value, valueFont, Size.Empty, flags).Width;
                int valueX = right - suffixWidth - valueWidth;
                var labelBounds = new Rectangle(insetX, 0,
                    Math.Max(0, valueX - insetX - TrayMenuStyle.Scale(e.ToolStrip, 20)), e.Item.Height);
                TextRenderer.DrawText(e.Graphics, prefix, e.TextFont, labelBounds,
                    e.ToolStrip.ForeColor, flags | TextFormatFlags.EndEllipsis);
                if (valueWidth > 0)
                    TextRenderer.DrawText(e.Graphics, value, valueFont,
                        new Rectangle(valueX, 0, valueWidth, e.Item.Height), accent, flags);
                TextRenderer.DrawText(e.Graphics, suffix, e.TextFont,
                    new Rectangle(right - suffixWidth, 0, suffixWidth, e.Item.Height), muted, flags);
            }
        }
    }
}
