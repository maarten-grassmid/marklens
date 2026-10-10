using System;
using System.IO;
using System.Linq;
using System.Text;
using MarkLens.Npp;

namespace MarkLens
{
    internal enum ThemeMode { Auto, Light, Dark }

    /// <summary>Plugin settings persisted to an INI file in the Notepad++ plugin config directory.</summary>
    internal sealed class Settings
    {
        private const string Section = "Settings";

        // General
        public bool ShowToc = true;
        public bool SyncScroll = true;
        public bool RenderNonMarkdown = false;
        public ThemeMode Theme = ThemeMode.Auto;
        public string Extensions = ".md;.markdown;.mdown;.mkd;.mkdn;.mdwn;.mdtxt;.mdtext;.rmd;.mdx";
        public int UpdateDelayMs = 250;
        public string CustomCssPath = "";
        public double Zoom = 1.0;
        public int TocWidth = 240;

        // Rendering extras, all off by default
        public bool SyntaxHighlighting = false;
        public bool Mermaid = false;
        public bool Math = false;
        public bool Emoji = false;

        // Raw HTML in Markdown is shown as text unless the user opts in.
        public bool AllowHtml = false;

#if DEBUG
        // Developer option, debug builds only (INI only, never written back):
        // remote-debugging port for the preview. Release builds never open one.
        public int DevToolsPort = 0;
#endif

        public string FilePath { get; private set; }

        public string ConfigDirectory => Path.GetDirectoryName(FilePath);

        public static Settings Load()
        {
            var dir = Path.Combine(Editor.PluginConfigDirectory, Main.PluginFolderName);
            Directory.CreateDirectory(dir);
            var s = new Settings { FilePath = Path.Combine(dir, Main.PluginFolderName + ".ini") };
            if (!File.Exists(s.FilePath))
                return s;

            s.ShowToc = s.GetBool(nameof(ShowToc), s.ShowToc);
            s.SyncScroll = s.GetBool(nameof(SyncScroll), s.SyncScroll);
            s.RenderNonMarkdown = s.GetBool(nameof(RenderNonMarkdown), s.RenderNonMarkdown);
            s.Theme = Enum.TryParse(s.Get(nameof(Theme), s.Theme.ToString()), true, out ThemeMode t) ? t : ThemeMode.Auto;
            s.Extensions = s.Get(nameof(Extensions), s.Extensions);
            s.UpdateDelayMs = s.GetInt(nameof(UpdateDelayMs), s.UpdateDelayMs, 0, 5000);
            s.CustomCssPath = s.Get(nameof(CustomCssPath), s.CustomCssPath);
            s.Zoom = double.TryParse(s.Get(nameof(Zoom), "1"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var z) && z >= 0.25 && z <= 5 ? z : 1.0;
            s.TocWidth = s.GetInt(nameof(TocWidth), s.TocWidth, 120, 800);
            s.SyntaxHighlighting = s.GetBool(nameof(SyntaxHighlighting), s.SyntaxHighlighting);
            s.Mermaid = s.GetBool(nameof(Mermaid), s.Mermaid);
            s.Math = s.GetBool(nameof(Math), s.Math);
            s.Emoji = s.GetBool(nameof(Emoji), s.Emoji);
            s.AllowHtml = s.GetBool(nameof(AllowHtml), s.AllowHtml);
#if DEBUG
            s.DevToolsPort = s.GetInt(nameof(DevToolsPort), 0, 0, 65535);
#endif
            return s;
        }

        public void Save()
        {
            Set(nameof(ShowToc), ShowToc);
            Set(nameof(SyncScroll), SyncScroll);
            Set(nameof(RenderNonMarkdown), RenderNonMarkdown);
            Set(nameof(Theme), Theme.ToString());
            Set(nameof(Extensions), Extensions);
            Set(nameof(UpdateDelayMs), UpdateDelayMs.ToString());
            Set(nameof(CustomCssPath), CustomCssPath ?? "");
            Set(nameof(Zoom), Zoom.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            Set(nameof(TocWidth), TocWidth.ToString());
            Set(nameof(SyntaxHighlighting), SyntaxHighlighting);
            Set(nameof(Mermaid), Mermaid);
            Set(nameof(Math), Math);
            Set(nameof(Emoji), Emoji);
            Set(nameof(AllowHtml), AllowHtml);
        }

        public Settings Clone() => (Settings)MemberwiseClone();

        public bool IsMarkdownExtension(string ext)
        {
            if (string.IsNullOrEmpty(ext))
                return false;
            return Extensions.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.StartsWith(".") ? e : "." + e)
                .Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
        }

        private string Get(string key, string def)
        {
            var sb = new StringBuilder(4096);
            Win32.GetPrivateProfileString(Section, key, def, sb, sb.Capacity, FilePath);
            return sb.ToString();
        }

        private bool GetBool(string key, bool def)
        {
            var v = Get(key, def ? "1" : "0").Trim();
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        private int GetInt(string key, int def, int min, int max)
            => int.TryParse(Get(key, def.ToString()), out var v) ? System.Math.Max(min, System.Math.Min(max, v)) : def;

        private void Set(string key, string value) => Win32.WritePrivateProfileString(Section, key, value, FilePath);
        private void Set(string key, bool value) => Set(key, value ? "1" : "0");
    }
}
