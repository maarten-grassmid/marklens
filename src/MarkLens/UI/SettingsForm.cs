using System;
using System.Drawing;
using System.Windows.Forms;

namespace MarkLens.UI
{
    internal sealed class SettingsForm : Form
    {
        private readonly Settings _result;

        private readonly ComboBox _theme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        private readonly CheckBox _toc = Check("Show table of contents sidebar");
        private readonly CheckBox _sync = Check("Synchronize scrolling between editor and viewer");
        private readonly CheckBox _nonMd = Check("Render files with any extension as Markdown");
        private readonly TextBox _extensions = new TextBox { Width = 330 };
        private readonly NumericUpDown _delay = new NumericUpDown { Minimum = 0, Maximum = 5000, Increment = 50, Width = 80 };
        private readonly TextBox _css = new TextBox { Width = 260 };

        private readonly CheckBox _highlight = Check("Syntax highlighting in code blocks (highlight.js)");
        private readonly CheckBox _mermaid = Check("Mermaid diagrams (```mermaid blocks)");
        private readonly CheckBox _math = Check("Math formulas with KaTeX ($inline$ and $$block$$)");
        private readonly CheckBox _emoji = Check("Emoji shortcodes (:smile: becomes a smiley)");
        private readonly CheckBox _html = Check("Render raw HTML in Markdown (can load remote images and frames)");

        public Settings Result => _result;

        public SettingsForm(Settings current)
        {
            _result = current.Clone();

            Text = $"{Main.PluginName} — Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Font = SystemFonts.MessageBoxFont;
            Padding = new Padding(12);

            _theme.Items.AddRange(new object[] { "Follow Notepad++", "Light", "Dark" });

            var general = Group("General");
            general.Controls.Add(Row(new Label { Text = "Theme:", AutoSize = true, Margin = new Padding(0, 6, 6, 0) }, _theme));
            general.Controls.Add(_toc);
            general.Controls.Add(_sync);
            general.Controls.Add(_nonMd);
            general.Controls.Add(new Label { Text = "Markdown file extensions (separated by ;):", AutoSize = true, Margin = new Padding(0, 8, 0, 2) });
            general.Controls.Add(_extensions);
            general.Controls.Add(Row(new Label { Text = "Live update delay (ms):", AutoSize = true, Margin = new Padding(0, 6, 6, 0) }, _delay));
            var browse = new Button { Text = "Browse...", AutoSize = true };
            browse.Click += (s, e) =>
            {
                using (var dlg = new OpenFileDialog { Filter = "Stylesheet (*.css)|*.css|All files (*.*)|*.*" })
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                        _css.Text = dlg.FileName;
                }
            };
            general.Controls.Add(new Label { Text = "Custom stylesheet (optional):", AutoSize = true, Margin = new Padding(0, 8, 0, 2) });
            general.Controls.Add(Row(_css, browse));

            var extras = Group("Highlighting && extras");
            extras.Controls.Add(new Label
            {
                Text = "Optional rendering features, bundled for offline use. Off by default.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 0, 0, 4),
            });
            extras.Controls.Add(_highlight);
            extras.Controls.Add(_mermaid);
            extras.Controls.Add(_math);
            extras.Controls.Add(_emoji);
            extras.Controls.Add(_html);

            // A missing library can't be switched on; point to a reinstall instead.
            bool anyMissing = false;
            foreach (var (box, installed) in new[] { (_highlight, Libraries.Highlight), (_mermaid, Libraries.Mermaid), (_math, Libraries.Math) })
            {
                if (installed)
                    continue;
                anyMissing = true;
                box.Enabled = false;
                box.Text += " (not installed)";
            }
            if (anyMissing)
            {
                var link = new LinkLabel
                {
                    Text = "Some libraries are missing. Reinstall MarkLens from the Releases page to restore them.",
                    AutoSize = true,
                    MaximumSize = new Size(420, 0),
                    Margin = new Padding(0, 6, 0, 0),
                };
                link.LinkArea = new LinkArea(link.Text.IndexOf("Releases page", StringComparison.Ordinal), "Releases page".Length);
                link.LinkClicked += (s, e) =>
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Libraries.ReleasesUrl) { UseShellExecute = true });
                extras.Controls.Add(link);
            }

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, MinimumSize = new Size(88, 28), AutoSize = true };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, MinimumSize = new Size(88, 28), AutoSize = true };
            ok.Click += (s, e) => Commit();
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 8, 0, 0),
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);

            var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
            root.Controls.Add(general.Parent);
            root.Controls.Add(extras.Parent);
            root.Controls.Add(buttons);
            Controls.Add(root);

            AcceptButton = ok;
            CancelButton = cancel;
            LoadValues(_result);
        }

        private void LoadValues(Settings s)
        {
            _theme.SelectedIndex = (int)s.Theme;
            _toc.Checked = s.ShowToc;
            _sync.Checked = s.SyncScroll;
            _nonMd.Checked = s.RenderNonMarkdown;
            _extensions.Text = s.Extensions;
            _delay.Value = Math.Max(_delay.Minimum, Math.Min(_delay.Maximum, s.UpdateDelayMs));
            _css.Text = s.CustomCssPath;
            // Shown unticked while not installed; the stored preference itself is kept.
            _highlight.Checked = s.SyntaxHighlighting && Libraries.Highlight;
            _mermaid.Checked = s.Mermaid && Libraries.Mermaid;
            _math.Checked = s.Math && Libraries.Math;
            _emoji.Checked = s.Emoji;
            _html.Checked = s.AllowHtml;
        }

        private void Commit()
        {
            _result.Theme = (ThemeMode)Math.Max(0, _theme.SelectedIndex);
            _result.ShowToc = _toc.Checked;
            _result.SyncScroll = _sync.Checked;
            _result.RenderNonMarkdown = _nonMd.Checked;
            _result.Extensions = string.IsNullOrWhiteSpace(_extensions.Text) ? new Settings().Extensions : _extensions.Text.Trim();
            _result.UpdateDelayMs = (int)_delay.Value;
            _result.CustomCssPath = _css.Text.Trim();
            // Disabled boxes keep the stored preference, so a reinstall restores it.
            if (_highlight.Enabled) _result.SyntaxHighlighting = _highlight.Checked;
            if (_mermaid.Enabled) _result.Mermaid = _mermaid.Checked;
            if (_math.Enabled) _result.Math = _math.Checked;
            _result.Emoji = _emoji.Checked;
            _result.AllowHtml = _html.Checked;
        }

        private static CheckBox Check(string text) => new CheckBox { Text = text, AutoSize = true, Margin = new Padding(0, 3, 0, 3) };

        /// <summary>Creates a group box and returns the inner flow panel to add controls to.</summary>
        private static FlowLayoutPanel Group(string title)
        {
            var box = new GroupBox
            {
                Text = title,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 6, 10, 8),
                Margin = new Padding(0, 0, 0, 8),
                MinimumSize = new Size(440, 0),
            };
            var flow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Dock = DockStyle.Fill,
            };
            box.Controls.Add(flow);
            return flow;
        }

        private static FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
            row.Controls.AddRange(controls);
            return row;
        }
    }
}
