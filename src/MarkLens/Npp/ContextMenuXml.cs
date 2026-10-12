using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MarkLens.Npp
{
    /// <summary>
    /// Adds or removes the "Show in Preview" entry in Notepad++'s editor right-click menu.
    /// Plugins cannot extend that menu at run time; Notepad++ builds it from contextMenu.xml
    /// in its user configuration folder when it starts. The file is the single source of
    /// truth, so the Settings checkbox always reflects what is actually installed.
    /// </summary>
    internal static class ContextMenuXml
    {
        private const string FileName = "contextMenu.xml";
        private const string ClosingTag = "</ScintillaContextMenu>";

        // Notepad++'s menu separator; ours sits directly above the entry and goes with it.
        private const string SeparatorXml = "<Item id=\"0\" />";
        private const string SeparatorPattern = "<Item\\s+id=\"0\"\\s*/>";

        private static string ItemXml =>
            $"<Item PluginEntryName=\"{Main.PluginName}\" PluginCommandItemName=\"{Main.ShowInPreviewCommand}\" />";

        // Attribute order and extra attributes (FolderName, ItemNameAs...) do not matter.
        private static Regex ItemPattern => new Regex(
            "<Item\\b(?=[^>]*\\bPluginEntryName=\"" + Regex.Escape(Main.PluginName) + "\")" +
            "(?=[^>]*\\bPluginCommandItemName=\"" + Regex.Escape(Main.ShowInPreviewCommand) + "\")[^>]*/>");

        /// <summary>
        /// contextMenu.xml lives in the user configuration folder, two levels above the plugin
        /// config folder in every install mode (AppData, portable, -settingsDir, cloud).
        /// </summary>
        public static string FilePath
        {
            get
            {
                try
                {
                    var pluginConfig = Editor.PluginConfigDirectory;
                    var userConfig = Directory.GetParent(pluginConfig)?.Parent?.FullName;
                    if (userConfig != null && File.Exists(Path.Combine(userConfig, FileName)))
                        return Path.Combine(userConfig, FileName);
                }
                catch (ArgumentException)
                {
                    // fall through to the documented locations
                }
                var nppDir = Editor.NppDirectory;
                if (!string.IsNullOrEmpty(nppDir) && File.Exists(Path.Combine(nppDir, "doLocalConf.xml")))
                    return Path.Combine(nppDir, FileName);
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Notepad++", FileName);
            }
        }

        public static bool Exists => File.Exists(FilePath);

        public static bool Contains()
        {
            var path = FilePath;
            return File.Exists(path) && ItemPattern.IsMatch(Read(path, out _));
        }

        /// <summary>Adds (true) or removes (false) the entry. Takes effect when Notepad++ restarts.</summary>
        public static void Set(bool enable)
        {
            var path = FilePath;
            if (!File.Exists(path))
                throw new FileNotFoundException("Notepad++'s right-click menu file was not found.", path);

            string text = Read(path, out bool bom);
            string eol = text.Contains("\r\n") ? "\r\n" : "\n";
            var pattern = ItemPattern;
            string updated;

            if (enable)
            {
                if (pattern.IsMatch(text))
                    return;
                int close = text.IndexOf(ClosingTag, StringComparison.Ordinal);
                if (close < 0)
                    throw new InvalidDataException($"{path} has no {ClosingTag} element.");
                int lineStart = text.LastIndexOf('\n', Math.Max(0, close - 1)) + 1;
                string closingIndent = text.Substring(lineStart, close - lineStart);
                bool onOwnLine = closingIndent.Trim().Length == 0;
                // Match the indentation of the items above the closing tag.
                string indent = onOwnLine ? closingIndent + "\t" : "\t\t";
                string lines = indent + SeparatorXml + eol + indent + ItemXml + eol;
                updated = onOwnLine
                    ? text.Insert(lineStart, lines)
                    : text.Insert(close, eol + lines);
            }
            else
            {
                // An entry on a line of its own goes with its line; one sharing a line goes alone.
                // Our separator line directly above the entry is removed with it.
                var wholeLine = new Regex("(?m)(^[ \\t]*" + SeparatorPattern + "[ \\t]*\\r?\\n)?" +
                                          "^[ \\t]*" + pattern + "[ \\t]*\\r?\\n");
                updated = pattern.Replace(wholeLine.Replace(text, string.Empty), string.Empty);
                if (updated == text)
                    return;
            }

            Write(path, updated, bom);
        }

        private static string Read(string path, out bool bom)
        {
            var bytes = File.ReadAllBytes(path);
            bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            return new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        }

        /// <summary>Writes through a temporary file so a failure never leaves a truncated menu file.</summary>
        private static void Write(string path, string text, bool bom)
        {
            string temp = path + ".marklens.tmp";
            File.WriteAllText(temp, text, new UTF8Encoding(bom));
            File.Replace(temp, path, null);
        }
    }
}
