using System;
using System.Drawing;
using System.Windows.Forms;

namespace PearlTray
{
    public sealed class WebhookSettingsForm : SettingsWindow
    {
        public WebhookSettings SavedSettings { get; private set; }

        public WebhookSettingsForm(WebhookSettings current)
            : base("PearlTray · Webhook 设置", "Webhook", new Size(640, 772), false)
        {
            current = current ?? new WebhookSettings();
            ShowInTaskbar = false;
            SetScrollName("webhookScroll");

            AddSection("请求", "\uE774");
            var url = AddTextField("Webhook URL", current.Url, false);
            url.Name = "webhookUrl";
            Fields.Controls.Add(FieldLabel("请求方法"));
            var methods = new TableLayoutPanel
            {
                Dock = DockStyle.Top, ColumnCount = 2, Height = 40,
                Margin = new Padding(0, 0, 0, 14)
            };
            methods.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            methods.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            string method = current.Method == "POST" ? "POST" : "GET";
            var get = new SettingsButton("GET", "", method == "GET")
            {
                Dock = DockStyle.Fill, Margin = new Padding(0, 0, 5, 0),
                AccessibleName = "GET", Name = "webhookGet"
            };
            var post = new SettingsButton("POST", "", method == "POST")
            {
                Dock = DockStyle.Fill, Margin = new Padding(5, 0, 0, 0),
                AccessibleName = "POST", Name = "webhookPost"
            };
            methods.Controls.Add(get, 0, 0);
            methods.Controls.Add(post, 1, 0);
            Fields.Controls.Add(methods);

            AddSection("请求内容", "\uE8A5");
            var contentType = AddTextField("内容类型（Content-Type）", current.ContentType, false);
            var headers = AddTextField("请求头（JSON）", current.Headers, true);
            headers.Name = "webhookHeaders";
            var body = AddTextField("正文（{{title}} / {{message}}）", current.Body, true);
            body.Name = "webhookBody";
            body.Parent.Height = 110;
            Action updateMethod = delegate
            {
                get.SetSelected(method == "GET");
                post.SetSelected(method == "POST");
                body.Enabled = contentType.Enabled = method == "POST";
                body.Parent.Enabled = contentType.Parent.Enabled = method == "POST";
            };
            get.Click += delegate { method = "GET"; updateMethod(); };
            post.Click += delegate { method = "POST"; updateMethod(); };
            Tip(body.Parent, "POST 请求的正文模板");
            Tip(contentType.Parent, "POST 请求的 Content-Type");
            updateMethod();

            AddSection("身份验证", "\uE72E");
            var username = AddTextField("用户名（可选）", current.Username, false);
            var password = AddTextField("密码（可选）", current.Password, false);
            AddReveal(password);

            Error.Name = "webhookError";
            Error.AccessibleName = "Webhook 设置错误";
            SaveButton.Name = "webhookSave";
            DismissButton.Name = "webhookCancel";
            SaveButton.Click += delegate
            {
                var value = new WebhookSettings
                {
                    Url = url.Text.Trim(), Method = method, ContentType = contentType.Text.Trim(),
                    Headers = headers.Text, Body = body.Text, Username = username.Text, Password = password.Text
                };
                try
                {
                    value.Validate();
                    SavedSettings = value;
                    DialogResult = DialogResult.OK;
                    Close();
                }
                catch (ArgumentException validation) { Error.Text = validation.Message; }
            };
            ResumeLayout(true);
        }
    }
}
