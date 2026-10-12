using System;

namespace MarkLens.Npp
{
    /// <summary>
    /// Temporarily marks the source lines behind a preview selection with a translucent
    /// light-yellow box. The mark stays while the caret moves inside it and disappears
    /// when the caret leaves it, the text is edited, or another document is activated.
    /// </summary>
    internal static class SourceHighlight
    {
        private const int SCI_INDICSETSTYLE = 2080, SCI_INDICSETFORE = 2082, SCI_INDICSETUNDER = 2510;
        private const int SCI_INDICSETALPHA = 2523, SCI_INDICSETOUTLINEALPHA = 2558;
        private const int SCI_SETINDICATORCURRENT = 2500, SCI_INDICATORFILLRANGE = 2504, SCI_INDICATORCLEARRANGE = 2505;
        private const int SCI_GOTOPOS = 2025, SCI_SETFIRSTVISIBLELINE = 2613, SCI_VISIBLEFROMDOCLINE = 2220;
        private const int INDIC_FULLBOX = 16;
        private const int NPPM_ALLOCATEINDICATOR = 2024 + 113;
        private const int FallbackIndicator = 9;
        private const long LightYellow = 0x80F0FF; // BGR of #FFF080

        private static int _indicator = -1;
        private static IntPtr _view;       // Scintilla view that shows the mark
        private static IntPtr _buffer;     // Notepad++ buffer it belongs to
        private static long _start, _end;  // marked byte range
        private static bool _active;
        private static bool _placing;      // ignore the caret move we cause ourselves

        private static int Indicator
        {
            get
            {
                if (_indicator >= 0)
                    return _indicator;
                // Ask Notepad++ for an indicator nobody else uses.
                var slot = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
                try
                {
                    bool ok = Win32.SendMessage(Editor.NppHandle, NPPM_ALLOCATEINDICATOR, new IntPtr(1), slot) != IntPtr.Zero;
                    _indicator = ok ? System.Runtime.InteropServices.Marshal.ReadInt32(slot) : FallbackIndicator;
                }
                finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(slot); }
                return _indicator;
            }
        }

        private static IntPtr Send(IntPtr sci, int msg, long w = 0, long l = 0)
            => Win32.SendMessage(sci, msg, new IntPtr(w), new IntPtr(l));

        private static void Style(IntPtr sci)
        {
            int ind = Indicator;
            Send(sci, SCI_INDICSETSTYLE, ind, INDIC_FULLBOX);
            Send(sci, SCI_INDICSETFORE, ind, LightYellow);
            Send(sci, SCI_INDICSETALPHA, ind, 90);
            Send(sci, SCI_INDICSETOUTLINEALPHA, ind, 140);
            Send(sci, SCI_INDICSETUNDER, ind, 1); // text stays drawn on top
        }

        /// <summary>
        /// Marks lines [startLine, endLine] and scrolls them into view when they are off screen.
        /// The caret and keyboard focus stay where they are (Show in Source).
        /// </summary>
        public static void Mark(long startLine, long endLine)
        {
            Clear();
            IntPtr sci = Editor.CurrentScintilla;
            long start = Editor.PositionFromLine(startLine);
            long end = Editor.LineEndPosition(endLine);

            Style(sci);
            Send(sci, SCI_SETINDICATORCURRENT, Indicator);
            Send(sci, SCI_INDICATORFILLRANGE, start, Math.Max(1, end - start));

            _view = sci;
            _buffer = Editor.CurrentBufferId;
            _start = start;
            _end = end;
            _active = true;

            _placing = true;
            try
            {
                Send(sci, SciMsg.SCI_ENSUREVISIBLE, startLine);
                long top = Editor.FirstVisibleDocLine;
                long onScreen = (long)Send(sci, SciMsg.SCI_LINESONSCREEN);
                if (startLine < top || endLine > top + Math.Max(1, onScreen) - 1)
                    ScrollToContext(sci, startLine);
            }
            finally { _placing = false; }
        }

        /// <summary>Moves the caret to the start of lines [startLine, endLine], marks them and focuses the editor.</summary>
        public static void Show(long startLine, long endLine)
        {
            Mark(startLine, endLine);
            IntPtr sci = _view;
            _placing = true;
            try
            {
                Send(sci, SCI_GOTOPOS, _start);
                ScrollToContext(sci, startLine);
                Send(sci, SciMsg.SCI_GRABFOCUS);
            }
            finally { _placing = false; }
        }

        /// <summary>Shows the block with a little context above it.</summary>
        private static void ScrollToContext(IntPtr sci, long startLine)
        {
            long first = Math.Max(0, startLine - 3);
            Send(sci, SCI_SETFIRSTVISIBLELINE, (long)Send(sci, SCI_VISIBLEFROMDOCLINE, first));
        }

        public static void Clear()
        {
            if (!_active)
                return;
            _active = false;
            // Only touch the document if it is still the one in that view.
            if (_view != IntPtr.Zero && Editor.CurrentBufferId == _buffer)
            {
                long length = (long)Send(_view, SciMsg.SCI_GETLENGTH);
                Send(_view, SCI_SETINDICATORCURRENT, Indicator);
                Send(_view, SCI_INDICATORCLEARRANGE, 0, length);
            }
            _view = IntPtr.Zero;
        }

        /// <summary>Caret/selection moved in <paramref name="sci"/>.</summary>
        public static void OnSelectionChanged(IntPtr sci)
        {
            if (!_active || _placing || sci != _view)
                return;
            long caret = (long)Send(sci, SciMsg.SCI_GETCURRENTPOS);
            long anchor = (long)Send(sci, 2009 /* SCI_GETANCHOR */);
            if (caret < _start || caret > _end || anchor < _start || anchor > _end)
                Clear();
        }

        /// <summary>Text was inserted or deleted in <paramref name="sci"/>.</summary>
        public static void OnTextChanged(IntPtr sci)
        {
            if (_active && sci == _view)
                Clear();
        }

        /// <summary>
        /// Another document became active. A mark left in the previous document is removed
        /// as soon as that document is shown again (indicators belong to the document).
        /// </summary>
        public static void OnBufferActivated()
        {
            if (_active && Editor.CurrentBufferId != _buffer)
            {
                _active = false;
                _pendingClearBuffer = _buffer;
                _view = IntPtr.Zero;
                return;
            }
            if (_pendingClearBuffer != IntPtr.Zero && Editor.CurrentBufferId == _pendingClearBuffer)
            {
                IntPtr sci = Editor.CurrentScintilla;
                Send(sci, SCI_SETINDICATORCURRENT, Indicator);
                Send(sci, SCI_INDICATORCLEARRANGE, 0, (long)Send(sci, SciMsg.SCI_GETLENGTH));
                _pendingClearBuffer = IntPtr.Zero;
            }
        }

        private static IntPtr _pendingClearBuffer;
    }
}
