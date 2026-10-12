using System.Drawing;
using System.Windows.Forms;

namespace MarkLens.UI
{
    /// <summary>
    /// Explains a change that only takes effect after Notepad++ restarts and asks whether to
    /// restart now. Returns DialogResult.Yes for "Restart Now".
    /// </summary>
    internal sealed class RestartDialog : Form
    {
        public RestartDialog(string what, string why)
        {
            Text = Main.PluginName;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Font = SystemFonts.MessageBoxFont;
            Padding = new Padding(16, 14, 16, 12);

            var icon = new PictureBox
            {
                Image = SystemIcons.Information.ToBitmap(),
                SizeMode = PictureBoxSizeMode.AutoSize,
                Margin = new Padding(0, 0, 12, 0),
            };
            var heading = new Label
            {
                Text = what,
                AutoSize = true,
                MaximumSize = new Size(380, 0),
                Font = new Font(Font.FontFamily, Font.Size + 1.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(0x00, 0x33, 0x99),
                Margin = new Padding(0, 0, 0, 8),
            };
            var body = new Label
            {
                Text = why + "\n\nOpen files are kept: Notepad++ asks to save unsaved changes and reopens your session.",
                AutoSize = true,
                MaximumSize = new Size(380, 0),
                Margin = new Padding(0),
            };
            var texts = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            texts.Controls.Add(heading);
            texts.Controls.Add(body);

            var top = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            top.Controls.Add(icon);
            top.Controls.Add(texts);

            var now = new Button { Text = "Restart Now", DialogResult = DialogResult.Yes, MinimumSize = new Size(100, 28), AutoSize = true };
            var later = new Button { Text = "Restart Later", DialogResult = DialogResult.No, MinimumSize = new Size(100, 28), AutoSize = true };
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 14, 0, 0),
            };
            buttons.Controls.Add(later);
            buttons.Controls.Add(now);

            var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
            root.Controls.Add(top);
            root.Controls.Add(buttons);
            Controls.Add(root);

            AcceptButton = now;
            CancelButton = later;
        }
    }
}
