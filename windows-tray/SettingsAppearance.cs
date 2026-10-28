using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PearlTray
{
    internal sealed class SettingsPalette
    {
        public readonly bool Light;
        public readonly Color Background, Surface, Text, Muted, Border, Hover, Error;
        public static readonly Color Accent = Color.FromArgb(118, 185, 0);

        public SettingsPalette(bool light)
        {
            Light = light;
            Background = light ? Color.FromArgb(252, 252, 252) : Color.FromArgb(32, 32, 32);
            Surface = light ? Color.White : Color.FromArgb(38, 38, 38);
            Text = light ? Color.FromArgb(35, 35, 35) : Color.FromArgb(238, 238, 238);
            Muted = light ? Color.FromArgb(111, 115, 121) : Color.FromArgb(156, 161, 169);
            Border = light ? Color.FromArgb(218, 218, 218) : Color.FromArgb(68, 68, 68);
            Hover = light ? Color.FromArgb(237, 239, 242) : Color.FromArgb(51, 53, 56);
            Error = light ? Color.FromArgb(173, 42, 35) : Color.FromArgb(255, 139, 130);
        }

        public static bool SystemLight()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return key == null || Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) == 1;
            }
            catch { return true; }
        }
    }

    public class SettingsWindow : Form
    {
        internal SettingsPalette Palette { get; private set; }
        protected readonly TableLayoutPanel Fields;
        protected readonly Label Error;
        protected readonly SettingsButton SaveButton;
        protected readonly SettingsButton DismissButton;
        private readonly Panel scroll;
        private readonly TableLayoutPanel footer;
        private readonly Panel header;
        private readonly ToolTip tips = new ToolTip();

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr window, string appName, string idList);

        protected SettingsWindow(string title, string section, Size size, bool firstRun)
        {
            SuspendLayout();
            Palette = new SettingsPalette(SettingsPalette.SystemLight());
            Text = title;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = TrayMenuFonts.ForText("设置", false);
            BackColor = Palette.Background;
            ForeColor = Palette.Text;
            ClientSize = size;
            MinimumSize = new Size(440, 320);
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            using (var icon = TrayIconRenderer.Create("P", SettingsPalette.Accent))
                Icon = (Icon)icon.Clone();

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                Margin = Padding.Empty, Padding = Padding.Empty
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(28, 20, 28, 16) };
            header.Paint += delegate(object sender, PaintEventArgs e) { PaintDivider(header, e, false); };
            var heading = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty
            };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var brand = new Label
            {
                Text = "PearlTray", Font = new Font(TrayMenuFonts.Emphasis.FontFamily, 18),
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty
            };
            var page = new Label
            {
                Text = section, AutoSize = true, Anchor = AnchorStyles.Right,
                Font = TrayMenuFonts.ForText(section, true), Margin = Padding.Empty
            };
            heading.Controls.Add(brand, 0, 0);
            heading.Controls.Add(page, 1, 0);
            header.Controls.Add(heading);
            root.Controls.Add(header, 0, 0);

            scroll = new Panel
            {
                Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty,
                Padding = new Padding(28, 18, 28, 4), TabIndex = 0
            };
            scroll.HandleCreated += delegate { ThemeNative(scroll, Palette.Light); };
            Fields = new TableLayoutPanel
            {
                Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty,
                Padding = Padding.Empty, TabIndex = 0
            };
            Fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            scroll.Controls.Add(Fields);
            root.Controls.Add(scroll, 0, 1);

            footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, AutoSize = true,
                Margin = Padding.Empty, Padding = new Padding(28, 12, 28, 18), TabIndex = 1
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            footer.Paint += delegate(object sender, PaintEventArgs e) { PaintDivider(footer, e, true); };
            Error = new Label
            {
                Dock = DockStyle.Top, AutoSize = true, ForeColor = Palette.Error,
                Margin = Padding.Empty, AccessibleName = "设置错误", TabStop = false, Visible = false
            };
            Error.TextChanged += delegate
            {
                Error.Visible = !String.IsNullOrEmpty(Error.Text);
                Error.Margin = String.IsNullOrEmpty(Error.Text) ? Padding.Empty : new Padding(0, 0, 0, ScaleValue(10));
            };
            footer.SizeChanged += delegate
            {
                Error.MaximumSize = new Size(Math.Max(1, footer.ClientSize.Width - footer.Padding.Horizontal), 0);
            };
            footer.Controls.Add(Error, 0, 0);
            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty
            };
            SaveButton = new SettingsButton("保存", "\uE74E", true) { Margin = Padding.Empty };
            DismissButton = new SettingsButton(firstRun ? "退出" : "取消", "", false)
            {
                DialogResult = DialogResult.Cancel, Margin = new Padding(0, 0, 10, 0)
            };
            actions.Controls.Add(SaveButton);
            actions.Controls.Add(DismissButton);
            footer.Controls.Add(actions, 0, 1);
            root.Controls.Add(footer, 0, 2);
            AcceptButton = SaveButton;
            CancelButton = DismissButton;
            tips.SetToolTip(page, section);
            tips.SetToolTip(brand, "PEARL / USDT");
        }

        public void SetAppearance(bool light)
        {
            Palette = new SettingsPalette(light);
            BackColor = Palette.Background;
            ForeColor = Palette.Text;
            ApplyAppearance(this);
            Error.ForeColor = Palette.Error;
            if (IsHandleCreated)
            {
                try
                {
                    int dark = light ? 0 : 1;
                    DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
            }
            Invalidate(true);
        }

        private void ApplyAppearance(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                var field = child as SettingsField;
                var button = child as SettingsButton;
                var toggle = child as SettingsToggle;
                var section = child as SettingsSection;
                if (field != null) field.Apply(Palette);
                else if (button != null) button.Apply(Palette);
                else if (toggle != null) toggle.Apply(Palette);
                else if (section != null) section.Apply(Palette);
                else
                {
                    child.BackColor = Palette.Background;
                    child.ForeColor = child.Tag as string == "muted" ? Palette.Muted :
                        child.Tag as string == "accent" ? SettingsPalette.Accent : Palette.Text;
                    ApplyAppearance(child);
                }
                if (child is TextBox || child is NumericUpDown || child is ScrollableControl)
                    ThemeNative(child, Palette.Light);
            }
        }

        internal static void ThemeNative(Control control, bool light)
        {
            if (!control.IsHandleCreated) return;
            SetWindowTheme(control.Handle, light ? "Explorer" : "DarkMode_Explorer", null);
            if (control is NumericUpDown)
                foreach (Control child in control.Controls)
                    ThemeNative(child, light);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SetAppearance(SettingsPalette.SystemLight());
            try
            {
                int round = 2;
                DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            SetAppearance(Palette.Light);
            var area = Screen.FromControl(this).WorkingArea;
            var available = new Size(Math.Max(1, area.Width - 32), Math.Max(1, area.Height - 32));
            MinimumSize = new Size(Math.Min(MinimumSize.Width, available.Width),
                Math.Min(MinimumSize.Height, available.Height));
            Size = new Size(Math.Min(Width, available.Width), Math.Min(Height, available.Height));
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x001A && IsHandleCreated && !IsDisposed)
                SetAppearance(SettingsPalette.SystemLight());
        }

        protected void SetScrollName(string name) { scroll.Name = name; }

        protected void AddSection(string text, string glyph)
        {
            Fields.Controls.Add(new SettingsSection(text, glyph)
            {
                Dock = DockStyle.Top, Height = 28,
                Margin = new Padding(0, Fields.Controls.Count == 0 ? 0 : 8, 0, 6)
            });
        }

        protected Label FieldLabel(string text)
        {
            return new Label
            {
                Text = text, Dock = DockStyle.Top, AutoSize = true,
                Font = TrayMenuFonts.ForText(text, false),
                Margin = new Padding(0, 0, 0, 5), TabStop = false
            };
        }

        protected TextBox AddTextField(string label, string value, bool multiline)
        {
            Fields.Controls.Add(FieldLabel(label));
            var editor = new TextBox
            {
                Text = value, AccessibleName = label, Multiline = multiline,
                Font = TrayMenuFonts.Regular, BorderStyle = BorderStyle.None
            };
            if (multiline)
            {
                editor.ScrollBars = ScrollBars.Vertical;
                editor.AcceptsReturn = true;
            }
            Fields.Controls.Add(Frame(editor, multiline ? 90 : 38));
            return editor;
        }

        protected SettingsField Frame(Control editor, int height)
        {
            return new SettingsField(editor)
            {
                Dock = DockStyle.Top, Height = height,
                Margin = new Padding(0, 0, 0, 10), TabIndex = Fields.Controls.Count
            };
        }

        protected SettingsButton AddReveal(TextBox editor, string subject = "密码")
        {
            editor.UseSystemPasswordChar = true;
            var reveal = new SettingsButton("", "\uE890", false)
            {
                Width = 32, Height = 30, AccessibleName = "显示" + subject, TabIndex = 1
            };
            var frame = (SettingsField)editor.Parent;
            frame.Adornment = reveal;
            reveal.Click += delegate
            {
                editor.UseSystemPasswordChar = !editor.UseSystemPasswordChar;
                reveal.Glyph = editor.UseSystemPasswordChar ? "\uE890" : "\uED1A";
                reveal.AccessibleName = (editor.UseSystemPasswordChar ? "显示" : "隐藏") + subject;
                tips.SetToolTip(reveal, reveal.AccessibleName);
            };
            tips.SetToolTip(reveal, reveal.AccessibleName);
            return reveal;
        }

        protected void Tip(Control control, string text) { tips.SetToolTip(control, text); }

        internal int ScaleValue(int value)
        {
            return ScaleValue(this, value);
        }

        internal static int ScaleValue(Control control, int value)
        {
            return Math.Max(1, (int)Math.Round(value * control.DeviceDpi / 96f *
                control.Font.SizeInPoints / 9.75f));
        }

        private void PaintDivider(Control control, PaintEventArgs e, bool top)
        {
            using (var pen = new Pen(Palette.Border))
                e.Graphics.DrawLine(pen, 0, top ? 0 : control.Height - 1,
                    control.Width, top ? 0 : control.Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tips.Dispose();
            base.Dispose(disposing);
        }
    }

    public sealed class SettingsField : Panel
    {
        private readonly Control editor;
        private SettingsPalette palette = new SettingsPalette(SettingsPalette.SystemLight());
        private Control adornment;
        private bool hover;
        public Control Adornment
        {
            get { return adornment; }
            set
            {
                adornment = value;
                Controls.Add(value);
                value.Enter += delegate { Invalidate(); };
                value.Leave += delegate { Invalidate(); };
                PerformLayout();
            }
        }

        public SettingsField(Control editor)
        {
            this.editor = editor;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Padding = new Padding(12, 9, 12, 9);
            TabStop = false;
            Controls.Add(editor);
            editor.Enter += delegate
            {
                Invalidate();
                ScrollableControl viewport = Parent as ScrollableControl;
                for (Control parent = Parent; parent != null; parent = parent.Parent)
                    if (parent is ScrollableControl && ((ScrollableControl)parent).AutoScroll)
                    {
                        viewport = (ScrollableControl)parent;
                        break;
                    }
                if (viewport != null && viewport.AutoScroll && IsHandleCreated)
                    BeginInvoke(new Action(delegate
                    {
                        if (!IsDisposed && !viewport.IsDisposed) viewport.ScrollControlIntoView(this);
                    }));
            };
            editor.Leave += delegate { Invalidate(); };
            editor.MouseEnter += delegate { hover = true; Invalidate(); };
            editor.MouseLeave += delegate { hover = false; Invalidate(); };
            editor.HandleCreated += delegate { SettingsWindow.ThemeNative(editor, palette.Light); };
            Click += delegate { editor.Focus(); };
            Apply(palette);
        }

        internal void Apply(SettingsPalette value)
        {
            palette = value;
            BackColor = value.Background;
            editor.BackColor = value.Surface;
            editor.ForeColor = value.Text;
            SettingsWindow.ThemeNative(editor, value.Light);
            var button = adornment as SettingsButton;
            if (button != null) button.Apply(value);
            Invalidate(true);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (editor == null) return;
            int extra = adornment == null ? 0 : adornment.Width + Padding.Right / 2;
            var textBox = editor as TextBox;
            int height = textBox != null && !textBox.Multiline ? textBox.PreferredHeight :
                editor is NumericUpDown ? editor.PreferredSize.Height : Height - Padding.Vertical;
            editor.SetBounds(Padding.Left, Math.Max(Padding.Top, (Height - height) / 2),
                Math.Max(1, Width - Padding.Horizontal - extra), Math.Max(1, height));
            if (adornment != null)
                adornment.Location = new Point(Width - Padding.Right - adornment.Width,
                    (Height - adornment.Height) / 2);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = TrayMenuStyle.Rounded(new Rectangle(1, 1, Width - 3, Height - 3),
                SettingsWindow.ScaleValue(this, 6)))
            using (var brush = new SolidBrush(Enabled ? palette.Surface : palette.Background))
            using (var pen = new Pen(ContainsFocus ? SettingsPalette.Accent :
                hover ? palette.Muted : palette.Border, ContainsFocus ? SettingsWindow.ScaleValue(this, 2) : 1))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    public sealed class SettingsButton : Button
    {
        private SettingsPalette palette = new SettingsPalette(SettingsPalette.SystemLight());
        private bool hover, pressed;
        private string glyph;
        public bool Primary { get; private set; }
        public string Glyph { get { return glyph; } set { glyph = value; Invalidate(); } }

        public SettingsButton(string text, string glyph, bool primary)
        {
            Text = text;
            this.glyph = glyph;
            Primary = primary;
            Size = new Size(100, 38);
            Font = TrayMenuFonts.ForText(text, primary);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        internal void Apply(SettingsPalette value) { palette = value; BackColor = value.Background; Invalidate(); }
        public void SetSelected(bool selected)
        {
            Primary = selected;
            AccessibleDescription = selected ? "已选择" : "未选择";
            Invalidate();
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? palette.Background : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool iconOnly = String.IsNullOrEmpty(Text);
            Color fill = Primary ? (pressed ? Color.FromArgb(95, 155, 0) :
                hover ? Color.FromArgb(134, 201, 20) : SettingsPalette.Accent) :
                hover || pressed ? palette.Hover : palette.Background;
            using (var path = TrayMenuStyle.Rounded(new Rectangle(1, 1, Width - 3, Height - 3),
                SettingsWindow.ScaleValue(this, 6)))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(Focused && ShowFocusCues ? SettingsPalette.Accent : palette.Border))
            {
                e.Graphics.FillPath(brush, path);
                if (!Primary && (!iconOnly || Focused && ShowFocusCues)) e.Graphics.DrawPath(pen, path);
            }
            Color color = !Enabled ? palette.Muted : Primary ? Color.FromArgb(20, 34, 0) : palette.Text;
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            int iconWidth = String.IsNullOrEmpty(Glyph) ? 0 : Font.Height;
            int gap = iconWidth > 0 && !iconOnly ? Math.Max(4, Font.Height / 3) : 0;
            int textWidth = iconOnly ? 0 : TextRenderer.MeasureText(e.Graphics, Text, Font, Size.Empty, flags).Width;
            int left = (Width - textWidth - iconWidth - gap) / 2;
            if (iconWidth > 0)
                using (var iconFont = new Font("Segoe MDL2 Assets", Font.Size))
                    TextRenderer.DrawText(e.Graphics, Glyph, iconFont,
                        new Rectangle(left, 0, iconWidth, Height), color, flags | TextFormatFlags.HorizontalCenter);
            if (!iconOnly)
                TextRenderer.DrawText(e.Graphics, Text, Font,
                    new Rectangle(left + iconWidth + gap, 0, textWidth, Height), color, flags);
            if (Focused && ShowFocusCues)
            {
                var focus = ClientRectangle;
                focus.Inflate(-5, -5);
                ControlPaint.DrawFocusRectangle(e.Graphics, focus, color, fill);
            }
        }
    }

    public sealed class SettingsToggle : CheckBox
    {
        private SettingsPalette palette = new SettingsPalette(SettingsPalette.SystemLight());
        private bool hover;
        public SettingsToggle(string text, bool value)
        {
            Text = text;
            Font = TrayMenuFonts.ForText(text, false);
            Checked = value;
            AutoSize = false;
            Height = 40;
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        internal void Apply(SettingsPalette value) { palette = value; BackColor = value.Background; Invalidate(); }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(palette.Background);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int unit = Math.Max(1, Font.Height);
            int width = unit * 2, height = unit + 2, top = (Height - height) / 2;
            var bounds = new Rectangle(1, top, width, height);
            using (var path = TrayMenuStyle.Rounded(bounds, height / 2))
            using (var brush = new SolidBrush(Checked ? SettingsPalette.Accent : hover ? palette.Hover : palette.Border))
                e.Graphics.FillPath(brush, path);
            int inset = SettingsWindow.ScaleValue(this, 3);
            using (var brush = new SolidBrush(Checked ? Color.FromArgb(20, 34, 0) :
                palette.Light ? Color.White : palette.Text))
                e.Graphics.FillEllipse(brush, Checked ? width - height + inset + 1 : inset + 1,
                    top + inset, height - inset * 2, height - inset * 2);
            TextRenderer.DrawText(e.Graphics, Text, Font,
                new Rectangle(width + unit, 0, Math.Max(1, Width - width - unit), Height),
                Enabled ? palette.Text : palette.Muted, TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
            {
                var focus = ClientRectangle;
                focus.Inflate(-1, -3);
                ControlPaint.DrawFocusRectangle(e.Graphics, focus);
            }
        }
    }

    public sealed class SettingsSection : Control
    {
        private readonly string glyph;
        private SettingsPalette palette = new SettingsPalette(SettingsPalette.SystemLight());
        public SettingsSection(string text, string glyph)
        {
            Text = text;
            this.glyph = glyph;
            Font = TrayMenuFonts.ForText(text, true);
            TabStop = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        internal void Apply(SettingsPalette value) { palette = value; BackColor = value.Background; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            int unit = Font.Height;
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
                TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            using (var font = new Font("Segoe MDL2 Assets", Font.Size))
                TextRenderer.DrawText(e.Graphics, glyph, font, new Rectangle(0, 0, unit, Height),
                    palette.Muted, flags | TextFormatFlags.HorizontalCenter);
            int left = unit + unit / 2;
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(left, 0, Width - left, Height),
                palette.Muted, flags);
            int line = left + TextRenderer.MeasureText(e.Graphics, Text, Font, Size.Empty, flags).Width + unit;
            if (line < Width)
                using (var pen = new Pen(palette.Border))
                    e.Graphics.DrawLine(pen, line, Height / 2, Width, Height / 2);
        }
    }
}
