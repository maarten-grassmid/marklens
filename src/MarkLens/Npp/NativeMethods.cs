using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MarkLens.Npp
{
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class DllExportAttribute : Attribute
    {
        public DllExportAttribute(string name) { Name = name; }
        public string Name { get; }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NppData
    {
        public IntPtr NppHandle;
        public IntPtr ScintillaMainHandle;
        public IntPtr ScintillaSecondHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NMHDR
    {
        public IntPtr hwndFrom;
        public IntPtr idFrom;
        public uint code;
    }

    /// <summary>Docking dialog descriptor (tTbData in Docking.h).</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct TbData
    {
        public IntPtr hClient;
        public IntPtr pszName;
        public int dlgID;
        public uint uMask;
        public IntPtr hIconTab;
        public IntPtr pszAddInfo;
        public RECT rcFloat;
        public int iPrevCont;
        public IntPtr pszModuleName;
    }

    internal static class NppMsg
    {
        private const int WM_USER = 0x400;
        private const int NPPMSG = WM_USER + 1000;
        private const int RUNCOMMAND_USER = WM_USER + 3000;

        public const int NPPM_GETCURRENTSCINTILLA = NPPMSG + 4;
        public const int NPPM_DMMSHOW = NPPMSG + 30;
        public const int NPPM_DMMHIDE = NPPMSG + 31;
        public const int NPPM_DMMREGASDCKDLG = NPPMSG + 33;
        public const int NPPM_SAVECURRENTFILE = NPPMSG + 38;
        public const int NPPM_SETMENUITEMCHECK = NPPMSG + 40;
        public const int NPPM_GETPLUGINSCONFIGDIR = NPPMSG + 46;
        public const int NPPM_GETCURRENTBUFFERID = NPPMSG + 60;
        public const int NPPM_DOOPEN = NPPMSG + 77;
        public const int NPPM_ISDARKMODEENABLED = NPPMSG + 107;

        public const int NPPM_GETFULLCURRENTPATH = RUNCOMMAND_USER + 1;
        public const int NPPM_GETCURRENTDIRECTORY = RUNCOMMAND_USER + 2;
        public const int NPPM_GETFILENAME = RUNCOMMAND_USER + 3;
        public const int NPPM_GETEXTPART = RUNCOMMAND_USER + 5;
        public const int NPPM_GETNPPDIRECTORY = RUNCOMMAND_USER + 7;

        // Notifications
        private const uint NPPN_FIRST = 1000;
        public const uint NPPN_READY = NPPN_FIRST + 1;
        public const uint NPPN_FILESAVED = NPPN_FIRST + 8;
        public const uint NPPN_SHUTDOWN = NPPN_FIRST + 9;
        public const uint NPPN_BUFFERACTIVATED = NPPN_FIRST + 10;
        public const uint NPPN_LANGCHANGED = NPPN_FIRST + 11;
        public const uint NPPN_FILERENAMED = NPPN_FIRST + 23;
        public const uint NPPN_DARKMODECHANGED = NPPN_FIRST + 27;

        // Docking
        public const uint DWS_USEOWNDARKMODE = 0x00000400;
        public const uint DWS_DF_CONT_RIGHT = 1u << 28;
        public const uint DMN_CLOSE = 1050 + 1;
    }

    internal static class SciMsg
    {
        public const int SCI_GETLENGTH = 2006;
        public const int SCI_GETCURRENTPOS = 2008;
        public const int SCI_BEGINUNDOACTION = 2078;
        public const int SCI_ENDUNDOACTION = 2079;
        public const int SCI_GETLINEENDPOSITION = 2136;
        public const int SCI_GETCODEPAGE = 2137;
        public const int SCI_GETREADONLY = 2140;
        public const int SCI_GETSELECTIONSTART = 2143;
        public const int SCI_GETSELECTIONEND = 2145;
        public const int SCI_GETFIRSTVISIBLELINE = 2152;
        public const int SCI_GETLINECOUNT = 2154;
        public const int SCI_SETSEL = 2160;
        public const int SCI_LINEFROMPOSITION = 2166;
        public const int SCI_POSITIONFROMLINE = 2167;
        public const int SCI_REPLACETARGET = 2194;
        public const int SCI_VISIBLEFROMDOCLINE = 2220;
        public const int SCI_DOCLINEFROMVISIBLE = 2221;
        public const int SCI_ENSUREVISIBLE = 2232;
        public const int SCI_LINESONSCREEN = 2370;
        public const int SCI_GRABFOCUS = 2400;
        public const int SCI_GETCHARACTERPOINTER = 2520;
        public const int SCI_SETFIRSTVISIBLELINE = 2613;
        public const int SCI_SETTARGETRANGE = 2686;

        public const uint SCN_UPDATEUI = 2007;
        public const uint SCN_MODIFIED = 2008;

        public const int SC_MOD_INSERTTEXT = 0x1;
        public const int SC_MOD_DELETETEXT = 0x2;
        public const int SC_UPDATE_SELECTION = 0x2;
        public const int SC_UPDATE_V_SCROLL = 0x4;
        public const int SC_CP_UTF8 = 65001;
    }

    internal static class Win32
    {
        public const int WM_NOTIFY = 0x004E;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, StringBuilder lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref TbData lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, out int lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder retVal, int size, string filePath);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern bool WritePrivateProfileString(string section, string key, string val, string filePath);
    }
}
