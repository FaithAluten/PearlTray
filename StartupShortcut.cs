using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PearlTray
{
    public sealed class StartupShortcut
    {
        private readonly string directory;
        private readonly string stateDirectory;

        public StartupShortcut(string directory, string stateDirectory)
        {
            this.directory = directory;
            this.stateDirectory = stateDirectory;
        }

        public void Initialize()
        {
            string marker = Path.Combine(stateDirectory, "startup-initialized");
            if (File.Exists(marker)) return;
            Directory.CreateDirectory(stateDirectory);
            if (!Enabled) SetEnabled(true);
            using (File.Open(marker, FileMode.OpenOrCreate, FileAccess.Write)) { }
        }

        private string LinkPath
        {
            get
            {
                if (String.IsNullOrEmpty(directory))
                    throw new IOException("找不到当前用户的 Windows 启动文件夹。");
                return Path.Combine(directory, "PearlTray.lnk");
            }
        }

        public bool Enabled { get { return File.Exists(LinkPath); } }

        public void SetEnabled(bool enabled)
        {
            string path = LinkPath;
            if (!enabled)
            {
                File.Delete(path);
                return;
            }

            Directory.CreateDirectory(directory);
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
            object shortcut = null;
            try
            {
                shortcut = ((dynamic)shell).CreateShortcut(path);
                dynamic link = shortcut;
                link.TargetPath = System.Windows.Forms.Application.ExecutablePath;
                link.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
                link.Save();
                if (!Enabled) throw new IOException("Windows 未保存启动快捷方式。");
            }
            finally
            {
                if (shortcut != null) Marshal.ReleaseComObject(shortcut);
                Marshal.ReleaseComObject(shell);
            }
        }
    }
}
