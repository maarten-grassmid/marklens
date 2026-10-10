using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using MarkLens.Npp;

namespace MarkLens
{
    /// <summary>
    /// Native entry points called by Notepad++. This class deliberately references no
    /// third-party types so the assembly resolver below is installed before Markdig or
    /// WebView2 are loaded from the plugin folder.
    /// </summary>
    internal static class PluginExports
    {
        private static readonly IntPtr NamePtr;

        static PluginExports()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromPluginFolder;
            NamePtr = Marshal.StringToHGlobalUni(Main.PluginName);
        }

        private static Assembly ResolveFromPluginFolder(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name;
            var dir = Path.GetDirectoryName(typeof(PluginExports).Assembly.Location);
            var candidate = Path.Combine(dir, name + ".dll");
            if (!File.Exists(candidate))
                return null;

            // Files extracted from a downloaded zip carry the "mark of the web", which makes the
            // runtime refuse to load them from disk. The plugin folder is trusted, so load the
            // bytes instead when that happens.
            try
            {
                return Assembly.UnsafeLoadFrom(candidate);
            }
            catch (FileLoadException)
            {
                return Assembly.Load(File.ReadAllBytes(candidate));
            }
        }

        [DllExport("isUnicode")]
        private static int IsUnicode() => 1;

        [DllExport("setInfo")]
        private static void SetInfo(NppData data)
        {
            Editor.Data = data;
            Main.Initialize();
        }

        [DllExport("getFuncsArray")]
        private static IntPtr GetFuncsArray(ref int count)
        {
            count = Main.Menu.Count;
            return Main.Menu.NativePointer;
        }

        [DllExport("messageProc")]
        private static IntPtr MessageProc(uint message, IntPtr wParam, IntPtr lParam) => new IntPtr(1);

        [DllExport("getName")]
        private static IntPtr GetName() => NamePtr;

        [DllExport("beNotified")]
        private static void BeNotified(IntPtr notification)
        {
            try
            {
                Main.OnNotification(notification);
            }
            catch (Exception ex)
            {
                Main.ReportError(ex);
            }
        }
    }
}
