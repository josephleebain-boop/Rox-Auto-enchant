using System.Diagnostics;

namespace RoxReroller;

internal sealed class SettingsForm : Form
{
    private readonly AppSettings _work;
    private readonly TextBox _stats = new() { Multiline = true, Height = 90, Dock = DockStyle.Top, ScrollBars = ScrollBars.Vertical };
    private readonly PropertyGrid _grid = new() { Dock = DockStyle.Fill, HelpVisible = true, ToolbarVisible = false, PropertySort = PropertySort.Categorized };
    public AppSettings? Result { get; private set; }

    public SettingsForm(AppSettings cfg)
    {
        _work = cfg.Clone();
        Text = "Settings";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 700);
        TopMost = true;

        _stats.Text = string.Join("\r\n", _work.Stats);
        _grid.SelectedObject = _work;

        var lbl = new Label
        {
            Text = "Stat names – one per line (or comma-separated). This list feeds BOTH the dropdown and the OCR matching.\r\nWrite them as they appear in the game, e.g. STR, VIT, P.ATK.",
            Dock = DockStyle.Top, Height = 44, Padding = new Padding(4)
        };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var ok = new Button { Text = "Save", Width = 90, Height = 30 };
        var cancel = new Button { Text = "Cancel", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
        var folder = new Button { Text = "Open settings folder", Width = 150, Height = 30 };
        buttons.Controls.AddRange(new Control[] { ok, cancel, folder });

        ok.Click += (_, _) => OnSave();
        folder.Click += (_, _) => Process.Start(new ProcessStartInfo { FileName = AppPaths.Dir, UseShellExecute = true });

        Controls.Add(_grid);
        Controls.Add(_stats);
        Controls.Add(lbl);
        Controls.Add(buttons);
        CancelButton = cancel;
    }

    private void OnSave()
    {
        var names = _stats.Text.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0).Distinct().ToList();
        if (names.Count == 0) { MessageBox.Show(this, "Add at least one stat name."); return; }
        _work.Stats = names;
        _work.Normalize();
        Result = _work;
        DialogResult = DialogResult.OK;
    }
}
