using System.IO;

namespace MarkLens
{
    /// <summary>
    /// The optional client libraries ship in assets\vendor. A library counts as installed only
    /// when all of its files are present; a missing one simply behaves as if switched off.
    /// </summary>
    internal static class Libraries
    {
        public const string ReleasesUrl = "https://github.com/maarten-grassmid/marklens/releases";

        private static string Vendor => Path.Combine(Main.PluginDirectory, "assets", "vendor");

        private static bool Present(params string[] relativePaths)
        {
            foreach (var p in relativePaths)
            {
                var full = Path.Combine(Vendor, p);
                if (!File.Exists(full) && !Directory.Exists(full))
                    return false;
            }
            return true;
        }

        public static bool Highlight => Present(@"highlight\highlight.min.js", @"highlight\github.min.css", @"highlight\github-dark.min.css");
        public static bool Math => Present(@"katex\katex.min.js", @"katex\katex.min.css", @"katex\fonts");
        public static bool Mermaid => Present(@"mermaid\mermaid.min.js");

        /// <summary>The settings as they can actually be honoured with the libraries on disk.</summary>
        public static Settings Effective(Settings s)
        {
            var e = s.Clone();
            e.SyntaxHighlighting = s.SyntaxHighlighting && Highlight;
            e.Math = s.Math && Math;
            e.Mermaid = s.Mermaid && Mermaid;
            return e;
        }
    }
}
