using System;
using System.Drawing;
using System.IO;
using PearlTray;

internal static class CreateIcon
{
    private static void Main(string[] args)
    {
        using (var icon = TrayIconRenderer.Create("P", Color.FromArgb(28, 112, 82)))
        using (var file = File.Create(args[0])) icon.Save(file);
    }
}
