namespace RoxReroller;

internal sealed class ResultForm : Form
{
    public ResultForm(string report, List<(string Name, Bitmap Image)> images)
    {
        Text = "Test read – what the app sees";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 640);
        TopMost = true;

        var txt = new TextBox
        {
            Multiline = true, ReadOnly = true, Dock = DockStyle.Top, Height = 280, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9f), Text = report
        };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, Padding = new Padding(6) };
        foreach (var (name, img) in images)
        {
            var panel = new Panel { Width = 230, Height = 130, Margin = new Padding(4), BorderStyle = BorderStyle.FixedSingle };
            var cap = new Label { Text = name, Dock = DockStyle.Top, Height = 18, Font = new Font("Segoe UI", 8f) };
            var pic = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = (Bitmap)img.Clone(), BackColor = Color.Gray };
            panel.Controls.Add(pic);
            panel.Controls.Add(cap);
            flow.Controls.Add(panel);
        }
        var close = new Button { Text = "Close", Dock = DockStyle.Bottom, Height = 32, DialogResult = DialogResult.OK };
        Controls.Add(flow);
        Controls.Add(txt);
        Controls.Add(close);
        AcceptButton = close;
    }
}
