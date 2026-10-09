using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MarkLens.Npp
{
    /// <summary>Thin wrapper around Notepad++ and its active Scintilla view.</summary>
    internal static class Editor
    {
        public static NppData Data;

        public static IntPtr NppHandle => Data.NppHandle;

        public static IntPtr CurrentScintilla
        {
            get
            {
                Win32.SendMessage(Data.NppHandle, NppMsg.NPPM_GETCURRENTSCINTILLA, IntPtr.Zero, out int which);
                return which == 0 ? Data.ScintillaMainHandle : Data.ScintillaSecondHandle;
            }
        }

        public static IntPtr Sci(int msg, long wParam = 0, long lParam = 0)
            => Win32.SendMessage(CurrentScintilla, msg, new IntPtr(wParam), new IntPtr(lParam));

        public static IntPtr Npp(int msg, long wParam = 0, long lParam = 0)
            => Win32.SendMessage(Data.NppHandle, msg, new IntPtr(wParam), new IntPtr(lParam));

        private static string NppString(int msg)
        {
            var sb = new StringBuilder(32768);
            Win32.SendMessage(Data.NppHandle, msg, new IntPtr(sb.Capacity), sb);
            return sb.ToString();
        }

        public static string CurrentFilePath => NppString(NppMsg.NPPM_GETFULLCURRENTPATH);
        public static string CurrentDirectory => NppString(NppMsg.NPPM_GETCURRENTDIRECTORY);
        public static string CurrentFileName => NppString(NppMsg.NPPM_GETFILENAME);
        public static string CurrentExtension => NppString(NppMsg.NPPM_GETEXTPART);
        public static string PluginConfigDirectory => NppString(NppMsg.NPPM_GETPLUGINSCONFIGDIR);

        public static IntPtr CurrentBufferId => Npp(NppMsg.NPPM_GETCURRENTBUFFERID);

        public static bool IsDarkMode => Npp(NppMsg.NPPM_ISDARKMODEENABLED) != IntPtr.Zero;

        /// <summary>True when the active buffer exists on disk (not an unsaved "new 1" tab).</summary>
        public static bool CurrentFileExistsOnDisk
        {
            get
            {
                var p = CurrentFilePath;
                return !string.IsNullOrEmpty(p) && Path.IsPathRooted(p) && File.Exists(p);
            }
        }

        private static Encoding DocumentEncoding
        {
            get
            {
                int cp = (int)Sci(SciMsg.SCI_GETCODEPAGE);
                if (cp == SciMsg.SC_CP_UTF8)
                    return Encoding.UTF8;
                return cp == 0 ? Encoding.Default : SafeEncoding(cp);
            }
        }

        private static Encoding SafeEncoding(int cp)
        {
            try { return Encoding.GetEncoding(cp); } catch { return Encoding.UTF8; }
        }

        /// <summary>
        /// Reads the whole document using the gap-free character pointer, which avoids
        /// an extra copy through SCI_GETTEXT and is fast even for very large files.
        /// </summary>
        public static string GetText()
        {
            long length = (long)Sci(SciMsg.SCI_GETLENGTH);
            if (length <= 0)
                return string.Empty;
            IntPtr ptr = Sci(SciMsg.SCI_GETCHARACTERPOINTER);
            var bytes = new byte[length];
            Marshal.Copy(ptr, bytes, 0, (int)length);
            return DocumentEncoding.GetString(bytes);
        }

        public static long LineCount => (long)Sci(SciMsg.SCI_GETLINECOUNT);

        public static long PositionFromLine(long line) => (long)Sci(SciMsg.SCI_POSITIONFROMLINE, line);
        public static long LineEndPosition(long line) => (long)Sci(SciMsg.SCI_GETLINEENDPOSITION, line);

        /// <summary>Returns text of lines [startLine, endLine] (inclusive, without the trailing EOL).</summary>
        public static string GetLines(long startLine, long endLine)
        {
            long start = PositionFromLine(startLine);
            long end = LineEndPosition(endLine);
            return GetRange(start, end);
        }

        public static string GetRange(long start, long end)
        {
            if (end <= start)
                return string.Empty;
            IntPtr ptr = Sci(SciMsg.SCI_GETCHARACTERPOINTER);
            var bytes = new byte[end - start];
            Marshal.Copy(new IntPtr(ptr.ToInt64() + start), bytes, 0, bytes.Length);
            return DocumentEncoding.GetString(bytes);
        }

        /// <summary>Counts text insertions/deletions in any view, to detect "nothing else changed since".</summary>
        public static long ModificationCount { get; private set; }

        public static void NoteModification() => ModificationCount++;

        public static bool CanUndo => Sci(2174 /* SCI_CANUNDO */) != IntPtr.Zero;

        /// <summary>Undoes the last action without moving the caret, selection or scroll position.</summary>
        public static void UndoKeepingView()
        {
            long anchor = (long)Sci(2009 /* SCI_GETANCHOR */);
            long caret = (long)Sci(SciMsg.SCI_GETCURRENTPOS);
            long first = (long)Sci(SciMsg.SCI_GETFIRSTVISIBLELINE);
            Sci(2176 /* SCI_UNDO */);
            Sci(SciMsg.SCI_SETSEL, anchor, caret);
            Sci(SciMsg.SCI_SETFIRSTVISIBLELINE, first);
        }

        public static byte ByteAt(long pos)
        {
            IntPtr ptr = Sci(SciMsg.SCI_GETCHARACTERPOINTER);
            return Marshal.ReadByte(new IntPtr(ptr.ToInt64() + pos));
        }

        /// <summary>Replaces exactly one byte with an ASCII byte, as one undo step.</summary>
        public static void ReplaceAsciiByte(long pos, byte value)
        {
            var buf = Marshal.AllocHGlobal(2);
            try
            {
                Marshal.WriteByte(buf, 0, value);
                Marshal.WriteByte(buf, 1, 0);
                Sci(SciMsg.SCI_BEGINUNDOACTION);
                Sci(SciMsg.SCI_SETTARGETRANGE, pos, pos + 1);
                Win32.SendMessage(CurrentScintilla, SciMsg.SCI_REPLACETARGET, new IntPtr(1), buf);
                Sci(SciMsg.SCI_ENDUNDOACTION);
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        public static bool IsReadOnly => Sci(SciMsg.SCI_GETREADONLY) != IntPtr.Zero;

        public static long FirstVisibleDocLine
        {
            get
            {
                long visible = (long)Sci(SciMsg.SCI_GETFIRSTVISIBLELINE);
                return (long)Sci(SciMsg.SCI_DOCLINEFROMVISIBLE, visible);
            }
        }

        public static void ScrollToDocLine(long line)
        {
            long visible = (long)Sci(SciMsg.SCI_VISIBLEFROMDOCLINE, line);
            Sci(SciMsg.SCI_SETFIRSTVISIBLELINE, visible);
        }

        public static void SaveCurrentFile() => Npp(NppMsg.NPPM_SAVECURRENTFILE);

        public static void OpenFile(string path)
            => Win32.SendMessage(Data.NppHandle, NppMsg.NPPM_DOOPEN, IntPtr.Zero, path);

        public static void SetMenuChecked(int cmdId, bool check)
            => Npp(NppMsg.NPPM_SETMENUITEMCHECK, cmdId, check ? 1 : 0);
    }

    internal sealed class Win32Window : System.Windows.Forms.IWin32Window
    {
        public Win32Window(IntPtr handle) { Handle = handle; }
        public IntPtr Handle { get; }
    }
}
