using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MarkLens.Npp;
using MarkLens.UI;

namespace MarkLens
{
    internal static class Main
    {
        public const string PluginName = "MarkLens";
        public const string PluginFolderName = "MarkLens";
        // Also the item name Notepad++ matches in contextMenu.xml (Npp\ContextMenuXml.cs).
        public const string ShowInPreviewCommand = "Show in Preview";

        public static readonly PluginMenu Menu = new PluginMenu();
        // Loaded lazily: Notepad++ re-opens a panel that was visible at last exit by
        // invoking its menu command *before* NPPN_READY is sent.
        private static Settings _settings;
        public static Settings Settings
        {
            get => _settings ?? (_settings = Settings.Load());
            private set => _settings = value;
        }
        public static string PluginDirectory => Path.GetDirectoryName(typeof(Main).Assembly.Location);

        private static int _idxToggle, _idxToc, _idxSync;
        private static ViewerPanel _panel;
        private static bool _ready;

        public static void Initialize()
        {
            // tools\smoke-test.ps1 opens the viewer through this menu entry by name.
            _idxToggle = Menu.Add("Markdown Viewer", TogglePanel, false,
                new ShortcutKey(true, false, true, Keys.M));
            _idxToc = Menu.Add("Table of Contents", ToggleToc);
            _idxSync = Menu.Add("Synchronize Scrolling", ToggleSync);
            Menu.Add(ShowInPreviewCommand, ShowInPreview, false,
                new ShortcutKey(true, true, true, Keys.M));
            Menu.AddSeparator();
            Menu.Add("Export as HTML...", () => WithPanel(p => p.ExportHtml()));
            Menu.Add("Export as PDF...", () => WithPanel(p => p.ExportPdf()));
            Menu.Add("Print...", () => WithPanel(p => p.Print()));
            Menu.AddSeparator();
            Menu.Add("Settings...", ShowSettings);
            Menu.Add("About", ShowAbout);
        }

        public static void OnNotification(IntPtr notification)
        {
            var header = Marshal.PtrToStructure<NMHDR>(notification);
            uint code = header.code;

            if (code == NppMsg.NPPN_READY)
            {
                _ready = true;
                Editor.SetMenuChecked(Menu.CommandId(_idxToc), Settings.ShowToc);
                Editor.SetMenuChecked(Menu.CommandId(_idxSync), Settings.SyncScroll);
                return;
            }
            if (!_ready)
                return;

            switch (code)
            {
                case NppMsg.NPPN_SHUTDOWN:
                    Settings.Save();
                    _panel?.Shutdown();
                    // Only sent once Notepad++ really closes, so a cancelled exit never restarts.
                    if (_restartOnShutdown)
                        Restarter.StartAfterExit();
                    break;
                case NppMsg.NPPN_CANCELSHUTDOWN:
                    // The user cancelled a save prompt: no restart, now or at a later exit.
                    _restartOnShutdown = false;
                    break;
                case NppMsg.NPPN_BUFFERACTIVATED:
                    SourceHighlight.OnBufferActivated();
                    _panel?.OnBufferChanged();
                    break;
                case NppMsg.NPPN_LANGCHANGED:
                case NppMsg.NPPN_FILERENAMED:
                    _panel?.OnBufferChanged();
                    break;
                case NppMsg.NPPN_FILESAVED:
                    _panel?.OnDocumentSaved();
                    break;
                case NppMsg.NPPN_DARKMODECHANGED:
                    _panel?.ApplyTheme();
                    break;
                case SciMsg.SCN_MODIFIED:
                {
                    int modType = Marshal.ReadInt32(notification, 40);
                    if ((modType & (SciMsg.SC_MOD_INSERTTEXT | SciMsg.SC_MOD_DELETETEXT)) == 0)
                        break;
                    Editor.NoteModification();
                    SourceHighlight.OnTextChanged(header.hwndFrom);
                    if (_panel != null && _panel.IsActive && header.hwndFrom == Editor.CurrentScintilla)
                        _panel.OnTextChanged();
                    break;
                }
                case SciMsg.SCN_UPDATEUI:
                {
                    int updated = Marshal.ReadInt32(notification, 144);
                    if ((updated & SciMsg.SC_UPDATE_SELECTION) != 0)
                        SourceHighlight.OnSelectionChanged(header.hwndFrom);
                    // Not only on SC_UPDATE_V_SCROLL: the panel compares the top line itself, which
                    // also catches jumps (Find, Go To) whose scroll arrives in another update.
                    if (_panel != null && _panel.IsActive && header.hwndFrom == Editor.CurrentScintilla)
                        _panel.OnEditorScrolled();
                    break;
                }
            }
        }

        private static void TogglePanel()
        {
            Guard(() =>
            {
                if (_panel == null)
                {
                    _panel = new ViewerPanel(Settings);
                    _panel.PanelClosed += () => Editor.SetMenuChecked(Menu.CommandId(_idxToggle), false);
                    _panel.DockInto(_idxToggle);
                    Editor.SetMenuChecked(Menu.CommandId(_idxToggle), true);
                    return;
                }

                bool show = !_panel.IsActive;
                _panel.SetVisible(show);
                Editor.SetMenuChecked(Menu.CommandId(_idxToggle), show);
            });
        }

        private static void ToggleToc()
        {
            Settings.ShowToc = !Settings.ShowToc;
            Editor.SetMenuChecked(Menu.CommandId(_idxToc), Settings.ShowToc);
            Settings.Save();
            _panel?.ApplySettings();
        }

        public static void SetToc(bool show)
        {
            if (Settings.ShowToc == show)
                return;
            ToggleToc();
        }

        private static void ToggleSync()
        {
            Settings.SyncScroll = !Settings.SyncScroll;
            Editor.SetMenuChecked(Menu.CommandId(_idxSync), Settings.SyncScroll);
            Settings.Save();
            _panel?.ApplySettings();
            if (Settings.SyncScroll)
                _panel?.Resync();
        }

        /// <summary>Editor to preview: scrolls the preview to the block at the caret or selection.</summary>
        private static void ShowInPreview()
        {
            WithPanel(p =>
            {
                var (start, end) = Editor.SelectionLines();
                p.RevealLines((int)start, (int)end);
            });
        }

        public static void ShowSettings()
        {
            Guard(() =>
            {
                using (var dlg = new SettingsForm(Settings))
                {
                    if (dlg.ShowDialog(new Win32Window(Editor.NppHandle)) != DialogResult.OK)
                        return;
                    Settings = dlg.Result;
                    Settings.Save();
                    Editor.SetMenuChecked(Menu.CommandId(_idxToc), Settings.ShowToc);
                    Editor.SetMenuChecked(Menu.CommandId(_idxSync), Settings.SyncScroll);
                    _panel?.UpdateSettings(Settings);

                    if (dlg.ContextMenuItemWanted != dlg.ContextMenuItemInitially)
                    {
                        ContextMenuXml.Set(dlg.ContextMenuItemWanted);
                        string what = dlg.ContextMenuItemWanted
                            ? $"\"{ShowInPreviewCommand}\" was added to the editor's right-click menu."
                            : $"\"{ShowInPreviewCommand}\" was removed from the editor's right-click menu.";
                        const string why = "Notepad++ reads its right-click menu only when it starts, so the change " +
                                           "shows after a restart.";
                        using (var restart = new RestartDialog(what, why))
                        {
                            if (restart.ShowDialog(new Win32Window(Editor.NppHandle)) == DialogResult.Yes)
                                RestartNotepadPlusPlus();
                        }
                    }
                }
            });
        }

        private static bool _restartOnShutdown;

        /// <summary>
        /// Asks Notepad++ to exit as if the user chose File > Exit (save prompts included). The
        /// relaunch is started from NPPN_SHUTDOWN, so cancelling a save prompt cancels the restart.
        /// </summary>
        private static void RestartNotepadPlusPlus()
        {
            const int WM_COMMAND = 0x0111, IDM_FILE_EXIT = 41011;
            _restartOnShutdown = true;
            Win32.PostMessage(Editor.NppHandle, WM_COMMAND, new IntPtr(IDM_FILE_EXIT), IntPtr.Zero);
        }

        private static void ShowAbout()
        {
            var version = typeof(Main).Assembly.GetName().Version;
            MessageBox.Show(new Win32Window(Editor.NppHandle),
                $"{PluginName} {version.Major}.{version.Minor}.{version.Build}\n\n" +
                "A fast Markdown viewer for Notepad++ with selectable text, table of contents,\n" +
                "edit and show in source, show in preview, scroll sync and optional code highlighting,\n" +
                "Mermaid and math.\n\n" +
                "https://github.com/maarten-grassmid/marklens",
                PluginName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static void WithPanel(Action<ViewerPanel> action)
        {
            Guard(() =>
            {
                if (_panel == null || !_panel.IsActive)
                {
                    MessageBox.Show(new Win32Window(Editor.NppHandle), "Open the Markdown Viewer first.",
                        PluginName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                action(_panel);
            });
        }

        private static void Guard(Action action)
        {
            try { action(); }
            catch (Exception ex) { ReportError(ex); }
        }

        public static void ReportError(Exception ex)
        {
            try
            {
                var log = Path.Combine(Path.GetTempPath(), PluginFolderName + ".log");
                File.AppendAllText(log, $"[{DateTime.Now:O}] {ex}\r\n");
            }
            catch
            {
                // logging must never take Notepad++ down
            }
            // A repeating failure (e.g. on every render) must not become a stream of dialogs.
            if (_showingError || DateTime.UtcNow - _lastErrorShown < TimeSpan.FromSeconds(30))
                return;
            _showingError = true;
            try
            {
                MessageBox.Show(new Win32Window(Editor.NppHandle),
                    ex.Message + "\n\nDetails were written to " + Path.Combine(Path.GetTempPath(), PluginFolderName + ".log"),
                    PluginName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
                // never let error reporting itself throw into Notepad++
            }
            finally
            {
                _showingError = false;
                _lastErrorShown = DateTime.UtcNow;
            }
        }

        private static bool _showingError;
        private static DateTime _lastErrorShown = DateTime.MinValue;
    }
}
