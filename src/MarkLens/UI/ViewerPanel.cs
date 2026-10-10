using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using MarkLens.Npp;
using MarkLens.Rendering;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MarkLens.UI
{
    /// <summary>Dockable Notepad++ panel hosting the WebView2-based Markdown preview.</summary>
    internal sealed class ViewerPanel : Form
    {
        private const string AssetsHost = "assets.marklens";
        private const string DocHost = "doc.marklens";
        private static readonly string ViewerUrl = $"https://{AssetsHost}/viewer.html";
        private static bool _loaderPathSet;
        private const long MaxPreviewBytes = 64L * 1024 * 1024;

        private readonly WebView2 _web;
        private readonly Timer _renderTimer;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        private Settings _settings;
        private bool _webReady;
        private bool _pageReady;
        private int _renderVersion;
        private string _mappedDocDir;
        private string _lastRenderedText = string.Empty;
        private IntPtr _lastBufferId;
        private int _shownVersion = -1;
        private DateTime _suppressEditorScrollUntil;
        private IntPtr _namePtr, _modulePtr;

        public event Action PanelClosed;

        public bool IsActive { get; private set; }

        public ViewerPanel(Settings settings)
        {
            _settings = settings;
            Text = Main.PluginName;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            BackColor = BackgroundColor();
            // Notepad++ uses the client's initial size for the first docked width.
            Size = new Size(720, 600);

            // Not Dock = Fill: WinForms clamps a Form's idea of its own size like a top-level
            // window, so inside a tall docked/floating panel the fill layout fell short.
            // The WebView is sized from the real client rectangle instead (FitToWindow).
            _web = new WebView2 { DefaultBackgroundColor = BackgroundColor() };
            Controls.Add(_web);

            _renderTimer = new Timer { Interval = 50 };
            _renderTimer.Tick += (s, e) => { _renderTimer.Stop(); RenderNow(); };
        }

        // ------------------------------------------------------------------ docking

        public void DockInto(int menuIndex)
        {
            _namePtr = Marshal.StringToHGlobalUni(Main.PluginName);
            _modulePtr = Marshal.StringToHGlobalUni(Main.PluginFolderName + ".dll");
            var data = new TbData
            {
                hClient = Handle,
                pszName = _namePtr,
                dlgID = menuIndex,
                uMask = NppMsg.DWS_DF_CONT_RIGHT | NppMsg.DWS_USEOWNDARKMODE,
                pszModuleName = _modulePtr,
            };
            Win32.SendMessage(Editor.NppHandle, NppMsg.NPPM_DMMREGASDCKDLG, IntPtr.Zero, ref data);
            IsActive = true;
            FitToWindow();
            InitializeWebView();
        }

        public void SetVisible(bool visible)
        {
            Editor.Npp(visible ? NppMsg.NPPM_DMMSHOW : NppMsg.NPPM_DMMHIDE, 0, Handle.ToInt64());
            IsActive = visible;
            if (visible)
                ScheduleRender(immediate: true);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Win32.WM_NOTIFY)
            {
                var hdr = Marshal.PtrToStructure<NMHDR>(m.LParam);
                if (hdr.hwndFrom == Editor.NppHandle && hdr.code == NppMsg.DMN_CLOSE)
                {
                    IsActive = false;
                    PanelClosed?.Invoke();
                }
            }
            else if (m.Msg == WM_GETMINMAXINFO)
            {
                // Never let the default top-level limits cap a docked or floating panel.
                base.WndProc(ref m);
                Marshal.WriteInt32(m.LParam, 24, 32000); // ptMaxTrackSize.x
                Marshal.WriteInt32(m.LParam, 28, 32000); // ptMaxTrackSize.y
                return;
            }
            base.WndProc(ref m);
            if (m.Msg == WM_SIZE || m.Msg == WM_WINDOWPOSCHANGED)
                FitToWindow();
        }

        private const int WM_SIZE = 0x0005, WM_WINDOWPOSCHANGED = 0x0047, WM_GETMINMAXINFO = 0x0024;

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        /// <summary>Sizes the WebView to the window's actual client area.</summary>
        private void FitToWindow()
        {
            if (_web == null || !IsHandleCreated || !GetClientRect(Handle, out var rc))
                return;
            var bounds = new Rectangle(0, 0, rc.Right - rc.Left, rc.Bottom - rc.Top);
            if (_web.Bounds != bounds)
                _web.Bounds = bounds;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S))
            {
                Editor.SaveCurrentFile();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public void Shutdown()
        {
            _renderTimer.Stop();
            try { _web.Dispose(); } catch { /* process is exiting */ }
        }

        /// <summary>
        /// Notepad++ owns the message loop, so WinForms only installs its UI synchronization
        /// context lazily, and closing any modal dialog (Settings, Save As...) removes it again.
        /// Without it, code after an <c>await</c> resumes on a thread-pool thread and WebView2
        /// throws "can only be accessed from the UI thread". Call before every await.
        /// </summary>
        private static void EnsureUiContext()
        {
            if (!(System.Threading.SynchronizationContext.Current is WindowsFormsSynchronizationContext))
                System.Threading.SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        }

        // ------------------------------------------------------------------ WebView2 setup

        private async void InitializeWebView()
        {
            try
            {
                if (!_loaderPathSet)
                {
                    var loaderDir = Main.PluginDirectory;
                    if (!File.Exists(Path.Combine(loaderDir, "WebView2Loader.dll")) && File.Exists(Path.Combine(loaderDir, "x64", "WebView2Loader.dll")))
                        loaderDir = Path.Combine(loaderDir, "x64");
                    CoreWebView2Environment.SetLoaderDllFolderPath(loaderDir);
                    _loaderPathSet = true;
                }

                var userData = Path.Combine(_settings.ConfigDirectory, "WebView2");
                EnsureUiContext();
                CoreWebView2EnvironmentOptions options = null;
#if DEBUG
                if (_settings.DevToolsPort > 0)
                    options = new CoreWebView2EnvironmentOptions($"--remote-debugging-port={_settings.DevToolsPort}");
#endif
                var env = await CoreWebView2Environment.CreateAsync(null, userData, options);
                EnsureUiContext();
                await _web.EnsureCoreWebView2Async(env);

                var core = _web.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = true;
                core.Settings.AreDevToolsEnabled = Debugger.IsAttached;
                core.Settings.IsWebMessageEnabled = true;

                core.SetVirtualHostNameToFolderMapping(AssetsHost, Path.Combine(Main.PluginDirectory, "assets"),
                    CoreWebView2HostResourceAccessKind.Allow);
                MapDocumentFolder();

                core.WebMessageReceived += OnWebMessage;
                core.NavigationStarting += OnNavigationStarting;
                core.NewWindowRequested += (s, e) =>
                {
                    e.Handled = true;
                    if (e.IsUserInitiated)
                        OpenLink(e.Uri);
                };
                _web.ZoomFactor = _settings.Zoom;
                _web.ZoomFactorChanged += (s, e) => _settings.Zoom = _web.ZoomFactor;

                _webReady = true;
                core.Navigate(ViewerUrl);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                ShowFallback("The Microsoft Edge WebView2 Runtime is required.\r\n\r\n" +
                             "Download it from https://developer.microsoft.com/microsoft-edge/webview2/");
            }
            catch (Exception ex)
            {
                Main.ReportError(ex);
            }
        }

        private void ShowFallback(string message)
        {
            Controls.Clear();
            Controls.Add(new Label { Dock = DockStyle.Fill, Text = message, TextAlign = ContentAlignment.MiddleCenter });
        }

        private void MapDocumentFolder()
        {
            string dir = Editor.CurrentFileExistsOnDisk ? Editor.CurrentDirectory : Path.GetTempPath();
            if (string.Equals(dir, _mappedDocDir, StringComparison.OrdinalIgnoreCase))
                return;
            var core = _web.CoreWebView2;
            if (_mappedDocDir != null)
                core.ClearVirtualHostNameToFolderMapping(DocHost);
            core.SetVirtualHostNameToFolderMapping(DocHost, dir, CoreWebView2HostResourceAccessKind.Allow);
            _mappedDocDir = dir;
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (e.Uri.StartsWith(ViewerUrl, StringComparison.OrdinalIgnoreCase))
                return;
            e.Cancel = true;
            // Links are normally handled by the page's click handler; only honour
            // navigations the user actually triggered, never scripted ones.
            if (e.IsUserInitiated)
                OpenLink(e.Uri);
        }

        // ------------------------------------------------------------------ settings & theme

        public void UpdateSettings(Settings settings)
        {
            _settings = settings;
            ApplySettings();
            ScheduleRender(immediate: true);
        }

        public void ApplySettings()
        {
            if (!_pageReady)
                return;
            string css = string.Empty;
            if (!string.IsNullOrWhiteSpace(_settings.CustomCssPath) && File.Exists(_settings.CustomCssPath))
            {
                try { css = File.ReadAllText(_settings.CustomCssPath); } catch { /* ignore unreadable css */ }
            }
            Post(new Dictionary<string, object>
            {
                ["type"] = "settings",
                ["theme"] = IsDark() ? "dark" : "light",
                ["toc"] = _settings.ShowToc,
                ["tocWidth"] = _settings.TocWidth,
                ["sync"] = _settings.SyncScroll,
                ["highlight"] = Libraries.Effective(_settings).SyntaxHighlighting,
                ["mermaid"] = Libraries.Effective(_settings).Mermaid,
                ["math"] = Libraries.Effective(_settings).Math,
                ["customCss"] = css,
            });
        }

        public void ApplyTheme()
        {
            var bg = BackgroundColor();
            BackColor = bg;
            if (_webReady)
                _web.DefaultBackgroundColor = bg;
            ApplySettings();
        }

        private bool IsDark()
        {
            switch (_settings.Theme)
            {
                case ThemeMode.Dark: return true;
                case ThemeMode.Light: return false;
                default: return Editor.IsDarkMode;
            }
        }

        private Color BackgroundColor() => IsDark() ? Color.FromArgb(0x1e, 0x1f, 0x22) : Color.White;

        // ------------------------------------------------------------------ rendering

        public void OnBufferChanged() => ScheduleRender(immediate: true);

        public void OnDocumentSaved() => ScheduleRender(immediate: true);

        public void OnTextChanged() => ScheduleRender(immediate: false);

        private void ScheduleRender(bool immediate)
        {
            if (!IsActive)
                return;
            int delay = _settings.UpdateDelayMs;
            // Back off a little for very large documents so typing stays responsive.
            long length = (long)Editor.Sci(SciMsg.SCI_GETLENGTH);
            if (length > 1_000_000)
                delay = Math.Max(delay, 600);
            else if (length > 250_000)
                delay = Math.Max(delay, 350);

            _renderTimer.Stop();
            _renderTimer.Interval = immediate ? 1 : Math.Max(1, delay);
            _renderTimer.Start();
        }

        private bool CurrentIsMarkdown()
            => _settings.RenderNonMarkdown || _settings.IsMarkdownExtension(Editor.CurrentExtension);

        private async void RenderNow()
        {
            if (!_pageReady || !IsActive)
                return;
            try
            {
                int version = ++_renderVersion;
                var bufferId = Editor.CurrentBufferId;
                bool newDocument = bufferId != _lastBufferId;

                if (!CurrentIsMarkdown())
                {
                    _lastBufferId = bufferId;
                    _lastRenderedText = string.Empty;
                    _shownVersion = -1;
                    Post(new Dictionary<string, object>
                    {
                        ["type"] = "placeholder",
                        ["text"] = $"\"{Editor.CurrentFileName}\" is not a Markdown file.",
                        ["hint"] = "Markdown extensions can be configured in Plugins › MarkLens › Settings.",
                    });
                    return;
                }

                long length = (long)Editor.Sci(SciMsg.SCI_GETLENGTH);
                if (length > MaxPreviewBytes)
                {
                    _lastBufferId = bufferId;
                    _lastRenderedText = string.Empty;
                    _shownVersion = -1;
                    Post(new Dictionary<string, object>
                    {
                        ["type"] = "placeholder",
                        ["text"] = $"\"{Editor.CurrentFileName}\" is too large to preview ({length / (1024 * 1024)} MB).",
                        ["hint"] = $"Files up to {MaxPreviewBytes / (1024 * 1024)} MB are rendered.",
                    });
                    return;
                }

                string text = Editor.GetText();
                var effective = Libraries.Effective(_settings);
                var options = new RenderOptions { Math = effective.Math, Mermaid = effective.Mermaid, Emoji = effective.Emoji, Html = _settings.AllowHtml };
                EnsureUiContext();
                var result = await Task.Run(() => MarkdownRenderer.Render(text, options));
                if (version != _renderVersion || !IsActive)
                    return;

                MapDocumentFolder();
                _lastRenderedText = text;
                _lastBufferId = bufferId;
                Post(new Dictionary<string, object>
                {
                    ["type"] = "render",
                    ["html"] = result.Html,
                    ["docKey"] = bufferId.ToInt64().ToString(),
                    ["newDocument"] = newDocument,
                    ["title"] = Editor.CurrentFileName,
                    ["line"] = _settings.SyncScroll ? Editor.FirstVisibleDocLine : -1,
                    ["v"] = version,
                });
                _shownVersion = version;
            }
            catch (Exception ex)
            {
                Main.ReportError(ex);
            }
        }

        public void OnEditorScrolled()
        {
            if (!_pageReady || !_settings.SyncScroll || DateTime.UtcNow < _suppressEditorScrollUntil)
                return;
            if (!CurrentIsMarkdown())
                return;
            Post(new Dictionary<string, object> { ["type"] = "scrollTo", ["line"] = Editor.FirstVisibleDocLine });
        }

        private void Post(object message)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => Post(message)));
                return;
            }
            if (_webReady)
                _web.CoreWebView2.PostWebMessageAsJson(_json.Serialize(message));
        }

        // ------------------------------------------------------------------ messages from the page

        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (!e.Source.StartsWith(ViewerUrl, StringComparison.OrdinalIgnoreCase))
                return;
            // Leave the WebView2 callback before doing anything that may show modal UI.
            string json = e.WebMessageAsJson;
            BeginInvoke((Action)(() => HandleWebMessage(json)));
        }

        private void HandleWebMessage(string json)
        {
            try
            {
                var msg = _json.Deserialize<Dictionary<string, object>>(json);
                if (msg == null || !msg.TryGetValue("type", out var typeObj))
                    return;

                switch (typeObj as string)
                {
                    case "ready":
                        _pageReady = true;
                        _lastBufferId = IntPtr.Zero;
                        ApplySettings();
                        ScheduleRender(immediate: true);
                        break;
                    case "scroll":
                        if (_settings.SyncScroll && CurrentIsMarkdown())
                        {
                            _suppressEditorScrollUntil = DateTime.UtcNow.AddMilliseconds(200);
                            Editor.ScrollToDocLine(Int(msg, "line"));
                        }
                        break;
                    case "editSource":
                        // A stale page is simply re-rendered; the user can click again.
                        if (IsCurrentRender(msg))
                            EditSource(Int(msg, "start"), Int(msg, "end"));
                        break;
                    case "copyMarkdown":
                        if (IsCurrentRender(msg) && CheckLinesCurrent(Int(msg, "start"), Int(msg, "end")))
                            Clipboard.SetText(Editor.GetLines(Int(msg, "start"), Int(msg, "end")).Replace("\r\n", "\n").Replace("\n", "\r\n"));
                        break;
                    case "toggleTask":
                        if (IsCurrentRender(msg))
                            ToggleTask(Int(msg, "line"));
                        break;
                    case "openLink":
                        OpenLink(msg["href"] as string);
                        break;
                    case "toc":
                        Main.SetToc(msg.TryGetValue("show", out var show) && show is bool b && b);
                        break;
                    case "tocWidth":
                        _settings.TocWidth = Int(msg, "width");
                        break;
                    case "settings":
                        Main.ShowSettings();
                        break;
                    case "exportHtml":
                        ExportHtml();
                        break;
                    case "exportPdf":
                        ExportPdf();
                        break;
                    case "print":
                        Print();
                        break;
                }
            }
            catch (Exception ex)
            {
                Main.ReportError(ex);
            }
        }

        /// <summary>
        /// Line numbers in a page message are only meaningful for the exact render the page
        /// showed, in the buffer that is still active. Anything else is stale.
        /// </summary>
        private bool IsCurrentRender(Dictionary<string, object> msg)
        {
            bool ok = msg.ContainsKey("v") && Int(msg, "v") == _shownVersion && _shownVersion == _renderVersion
                      && Editor.CurrentBufferId == _lastBufferId;
            if (!ok)
                ScheduleRender(immediate: true);
            return ok;
        }

        private static int Int(Dictionary<string, object> msg, string key)
            => msg.TryGetValue(key, out var v) && v != null ? Convert.ToInt32(v) : 0;

        /// <summary>
        /// The preview may lag the editor by the debounce delay. Make sure the source lines
        /// the page refers to still hold the text that was rendered.
        /// </summary>
        private bool CheckLinesCurrent(int start, int end)
        {
            if (start < 0 || end < start || end >= Editor.LineCount)
                return false;
            var rendered = SliceLines(_lastRenderedText, start, end);
            if (rendered != null && rendered == Editor.GetLines(start, end))
                return true;
            ScheduleRender(immediate: true);
            return false;
        }

        /// <summary>Raw text of lines [start, end] without the final line ending, or null if out of range.</summary>
        private static string SliceLines(string text, int start, int end)
        {
            int line = 0, i = 0, from = start == 0 ? 0 : -1;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '\r' || c == '\n')
                {
                    if (line == end)
                        return from < 0 ? null : text.Substring(from, i - from);
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        i++;
                    line++;
                    if (line == start)
                        from = i + 1;
                }
                i++;
            }
            return line == end && from >= 0 ? text.Substring(from) : null;
        }

        /// <summary>
        /// Puts the caret at the start of the source behind a preview selection and marks those
        /// lines in the editor, so the user can see (and edit) exactly what produced it.
        /// </summary>
        private void EditSource(int start, int end)
        {
            if (!CheckLinesCurrent(start, end))
                return; // a fresh render is scheduled; the page is out of date
            SourceHighlight.Show(start, end);
        }

        // ASCII-only prefix, so its character length equals its byte length in any encoding.
        private static readonly Regex TaskPattern = new Regex(@"^([ \t]*(?:>[ \t]*)*(?:[-*+]|[0-9]+[.)])[ \t]+\[)([ xX])\]");

        private void ToggleTask(int line)
        {
            if (!CheckLinesCurrent(line, line) || Editor.IsReadOnly)
                return;
            var m = TaskPattern.Match(Editor.GetLines(line, line));
            if (!m.Success)
                return;
            // Replace only the single mark byte; the rest of the line is never re-encoded.
            long pos = Editor.PositionFromLine(line) + m.Groups[2].Index;
            byte current = Editor.ByteAt(pos);
            if (current != (byte)' ' && current != (byte)'x' && current != (byte)'X')
                return;

            // Clicking the same box again right after toggling it should restore the document
            // exactly, including its saved/unsaved state: undo our edit instead of adding another.
            var buffer = Editor.CurrentBufferId;
            if (_lastToggleBuffer == buffer && _lastTogglePos == pos &&
                _lastToggleModCount == Editor.ModificationCount && Editor.CanUndo)
            {
                _lastToggleBuffer = IntPtr.Zero;
                Editor.UndoKeepingView();
                return;
            }

            Editor.ReplaceAsciiByte(pos, current == (byte)' ' ? (byte)'x' : (byte)' ');
            _lastToggleBuffer = buffer;
            _lastTogglePos = pos;
            _lastToggleModCount = Editor.ModificationCount;
        }

        private IntPtr _lastToggleBuffer;
        private long _lastTogglePos = -1;
        private long _lastToggleModCount;

        private void OpenLink(string href)
        {
            if (string.IsNullOrEmpty(href))
                return;
            try
            {
                var docPrefix = $"https://{DocHost}/";
                if (href.StartsWith(docPrefix, StringComparison.OrdinalIgnoreCase) && _mappedDocDir != null)
                {
                    var uri = new Uri(href);
                    var relative = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')).Replace('/', Path.DirectorySeparatorChar);
                    var local = Path.GetFullPath(Path.Combine(_mappedDocDir, relative));
                    // Never execute or shell-open local files from a document link: text opens
                    // in Notepad++, anything else is only revealed in Explorer.
                    if (File.Exists(local))
                    {
                        if (_settings.IsMarkdownExtension(Path.GetExtension(local)) || IsTextFile(local))
                            Editor.OpenFile(local);
                        else
                            Process.Start("explorer.exe", "/select,\"" + local + "\"");
                    }
                    else if (Directory.Exists(local))
                    {
                        Process.Start("explorer.exe", "\"" + local + "\"");
                    }
                    return;
                }

                if (Uri.TryCreate(href, UriKind.Absolute, out var abs) &&
                    (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps || abs.Scheme == Uri.UriSchemeMailto))
                {
                    Process.Start(new ProcessStartInfo(abs.AbsoluteUri) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                Main.ReportError(ex);
            }
        }

        private static bool IsTextFile(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".txt" || ext == ".json" || ext == ".xml" || ext == ".yml" || ext == ".yaml" ||
                   ext == ".ini" || ext == ".cs" || ext == ".js" || ext == ".ts" || ext == ".py" ||
                   ext == ".css" || ext == ".html" || ext == ".htm" || ext == ".log" || ext == ".csv";
        }

        // ------------------------------------------------------------------ export

        public async void ExportHtml()
        {
            try
            {
                if (!_pageReady)
                    return;
                EnsureUiContext();
                string body = _json.Deserialize<string>(await _web.CoreWebView2.ExecuteScriptAsync("window.marklens.exportBody()"));
                using (var dlg = new SaveFileDialog
                {
                    Filter = "HTML file (*.html)|*.html",
                    FileName = Path.ChangeExtension(Editor.CurrentFileName, ".html"),
                    InitialDirectory = Editor.CurrentFileExistsOnDisk ? Editor.CurrentDirectory : null,
                })
                {
                    if (dlg.ShowDialog(new Win32Window(Editor.NppHandle)) != DialogResult.OK)
                        return;
                    File.WriteAllText(dlg.FileName, BuildStandaloneHtml(body), new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                Main.ReportError(ex);
            }
        }

        private string BuildStandaloneHtml(string body)
        {
            var assets = Path.Combine(Main.PluginDirectory, "assets");
            var css = new StringBuilder(File.ReadAllText(Path.Combine(assets, "content.css")));
            if (Libraries.Effective(_settings).SyntaxHighlighting)
            {
                var hl = Path.Combine(assets, "vendor", "highlight", "github.min.css");
                if (File.Exists(hl))
                    css.AppendLine().Append(File.ReadAllText(hl));
            }
            if (Libraries.Effective(_settings).Math)
            {
                var katex = Path.Combine(assets, "vendor", "katex", "katex.min.css");
                if (File.Exists(katex))
                    css.AppendLine().Append(File.ReadAllText(katex).Replace("url(fonts/", "url(https://cdn.jsdelivr.net/npm/katex@0.16.22/dist/fonts/"));
            }
            if (!string.IsNullOrWhiteSpace(_settings.CustomCssPath) && File.Exists(_settings.CustomCssPath))
                css.AppendLine().Append(File.ReadAllText(_settings.CustomCssPath));

            var title = System.Net.WebUtility.HtmlEncode(Path.GetFileNameWithoutExtension(Editor.CurrentFileName));
            return "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n" +
                   "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n" +
                   $"<title>{title}</title>\n<style>\n{css}\n</style>\n</head>\n" +
                   $"<body class=\"theme-light\">\n<article class=\"markdown-body\">\n{body}\n</article>\n</body>\n</html>\n";
        }

        public async void ExportPdf()
        {
            try
            {
                if (!_pageReady)
                    return;
                using (var dlg = new SaveFileDialog
                {
                    Filter = "PDF document (*.pdf)|*.pdf",
                    FileName = Path.ChangeExtension(Editor.CurrentFileName, ".pdf"),
                    InitialDirectory = Editor.CurrentFileExistsOnDisk ? Editor.CurrentDirectory : null,
                })
                {
                    if (dlg.ShowDialog(new Win32Window(Editor.NppHandle)) != DialogResult.OK)
                        return;
                    var settings = _web.CoreWebView2.Environment.CreatePrintSettings();
                    settings.ShouldPrintBackgrounds = true;
                    EnsureUiContext(); // the Save dialog above just removed it
                    bool ok = await _web.CoreWebView2.PrintToPdfAsync(dlg.FileName, settings);
                    if (!ok)
                        MessageBox.Show(this, "Exporting to PDF failed.", Main.PluginName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                Main.ReportError(ex);
            }
        }

        public void Print()
        {
            if (_pageReady)
                _web.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _renderTimer.Dispose();
                _web.Dispose();
            }
            if (_namePtr != IntPtr.Zero) Marshal.FreeHGlobal(_namePtr);
            if (_modulePtr != IntPtr.Zero) Marshal.FreeHGlobal(_modulePtr);
            _namePtr = _modulePtr = IntPtr.Zero;
            base.Dispose(disposing);
        }
    }
}
