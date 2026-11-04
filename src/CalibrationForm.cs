namespace RoxReroller;

internal sealed class CalibrationForm : Form
{
    private sealed record Item(string Key, string Title, bool IsPoint, string Hint);

    private static readonly Item[] Items =
    {
        new("RerollButton",  "Reroll button – click point", true,  "Click the CENTRE of the Reroll button."),
        new("SaveButton",    "Save button – click point", true,    "The 'Rerolled Stat' row must be visible. Click the CENTRE of the green Save button."),
        new("ConfirmButton", "Confirm button (orange warning) – click point", true, "The 'current stat is Orange quality' popup must be visible. Click the CENTRE of Confirm."),
        new("StatText",      "Rerolled stat text – area", false,   "Drag a box around the text in the Rerolled Stat row (e.g. 'Lv.2VIT +3'). Leave the icon and Save button out."),
        new("QualityIcon",   "Quality icon – area", false,         "Drag a box tightly around the round stat icon at the left of the Rerolled Stat row."),
        new("SaveLabel",     "Save button label – area", false,    "Drag a box inside the green Save button, around the word 'Save'."),
        new("WarningText",   "Warning text – area", false,         "The popup must be visible. Drag a box around the sentence 'The current stat is Orange quality. Do you want to reroll?'"),
        new("RerollLabel",   "Reroll button label – area", false,  "Drag a box around the word 'Reroll' on the Reroll button (not the price)."),
    };

    private readonly AppSettings _cfg;
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly NumericUpDown _delay = new() { Minimum = 0, Maximum = 30, Value = 3, Width = 60 };

    public CalibrationForm(AppSettings cfg)
    {
        _cfg = cfg;
        Text = "Calibration";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 420);
        TopMost = true;

        var info = new Label
        {
            Dock = DockStyle.Top, Height = 78, Padding = new Padding(4),
            Text = "Open the Enchant Awakening screen in the game first. Pick an item and press 'Set selected'. This window hides, the screen is " +
                   "captured after the delay below, and you click/drag on the frozen picture. Esc cancels.\r\n" +
                   "For the Save button/text you need a Rerolled Stat row on screen; for the warning you need the orange popup open " +
                   "(use a longer delay to set that up)."
        };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 76, Padding = new Padding(4), WrapContents = true };
        var set = new Button { Text = "Set selected…", Width = 120, Height = 30 };
        var show = new Button { Text = "Show all on screen", Width = 130, Height = 30 };
        var reset = new Button { Text = "Reset to 1920×1080 defaults", Width = 190, Height = 30 };
        var close = new Button { Text = "Close", Width = 80, Height = 30, DialogResult = DialogResult.OK };
        buttons.Controls.AddRange(new Control[] { new Label { Text = "Capture delay (s):", AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, _delay, set, show, reset, close });

        set.Click += async (_, _) => await PickSelectedAsync();
        _list.DoubleClick += async (_, _) => await PickSelectedAsync();
        show.Click += async (_, _) => await ShowAllAsync();
        reset.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Reset all positions to the default 1920×1080 values?", "Reset", MessageBoxButtons.YesNo) == DialogResult.Yes)
            { _cfg.ResetPositionsToDefault(); Refill(); }
        };

        Controls.Add(_list);
        Controls.Add(info);
        Controls.Add(buttons);
        AcceptButton = close;
        Refill();
    }

    private string CurrentText(Item it) => it.IsPoint ? GetSpot(it.Key).ToString() : GetArea(it.Key).ToString();

    private void Refill()
    {
        int sel = _list.SelectedIndex;
        _list.Items.Clear();
        foreach (var it in Items) _list.Items.Add($"{it.Title}   →   {CurrentText(it)}");
        _list.SelectedIndex = sel >= 0 ? sel : 0;
    }

    private Spot GetSpot(string k) => k switch { "RerollButton" => _cfg.RerollButton, "SaveButton" => _cfg.SaveButton, _ => _cfg.ConfirmButton };
    private Area GetArea(string k) => k switch
    {
        "StatText" => _cfg.StatText, "QualityIcon" => _cfg.QualityIcon, "SaveLabel" => _cfg.SaveLabel,
        "WarningText" => _cfg.WarningText, _ => _cfg.RerollLabel
    };

    private List<(string, Rectangle)> Context()
    {
        var l = new List<(string, Rectangle)>();
        foreach (var (n, a) in _cfg.AllAreas) l.Add((n, new Rectangle(a.X, a.Y, a.Width, a.Height)));
        foreach (var (n, s) in _cfg.AllSpots) l.Add((n, new Rectangle(s.X - 10, s.Y - 10, 20, 20)));
        return l;
    }

    private async Task<Bitmap> HideAndCaptureAsync()
    {
        var owner = Owner;
        double ownerOpacity = owner?.Opacity ?? 1;
        Opacity = 0;
        if (owner != null) owner.Opacity = 0;
        await Task.Delay(300 + (int)_delay.Value * 1000);
        return Imaging.CaptureScreen();
    }

    private void Restore()
    {
        Opacity = 1;
        if (Owner != null) Owner.Opacity = 1;
        Activate();
    }

    private async Task PickSelectedAsync()
    {
        int i = _list.SelectedIndex;
        if (i < 0) return;
        var it = Items[i];
        Bitmap shot = await HideAndCaptureAsync();
        try
        {
            var vs = SystemInformation.VirtualScreen;
            using var pf = new PickerForm(shot, vs, it.Title + "\r\n" + it.Hint, it.IsPoint ? PickerMode.Point : PickerMode.Area, Context());
            if (pf.ShowDialog() == DialogResult.OK)
            {
                if (it.IsPoint)
                {
                    var s = GetSpot(it.Key); s.X = pf.ResultPoint.X; s.Y = pf.ResultPoint.Y;
                }
                else
                {
                    var a = GetArea(it.Key);
                    a.X = pf.ResultArea.X; a.Y = pf.ResultArea.Y; a.Width = pf.ResultArea.Width; a.Height = pf.ResultArea.Height;
                }
                var scr = Screen.PrimaryScreen!.Bounds;
                _cfg.CalibratedWidth = scr.Width; _cfg.CalibratedHeight = scr.Height;
                _cfg.Save();
            }
        }
        finally { shot.Dispose(); Restore(); Refill(); }
    }

    private async Task ShowAllAsync()
    {
        Bitmap shot = await HideAndCaptureAsync();
        try
        {
            var vs = SystemInformation.VirtualScreen;
            using var pf = new PickerForm(shot, vs, "Current positions (green). Click anywhere or press Esc to close.", PickerMode.View, Context());
            pf.ShowDialog();
        }
        finally { shot.Dispose(); Restore(); }
    }
}

internal enum PickerMode { Point, Area, View }

/// <summary>Full-screen frozen screenshot on which the user clicks a point or drags a rectangle.</summary>
internal sealed class PickerForm : Form
{
    private readonly Bitmap _shot;
    private readonly Bitmap _dim;
    private readonly Rectangle _vs;
    private readonly PickerMode _mode;
    private readonly string _prompt;
    private readonly List<(string Label, Rectangle Rect)> _context;
    private Point _start, _cur;
    private bool _drag;

    public Point ResultPoint { get; private set; }
    public Rectangle ResultArea { get; private set; }

    public PickerForm(Bitmap shot, Rectangle virtualScreen, string prompt, PickerMode mode, List<(string, Rectangle)> context)
    {
        _shot = shot; _vs = virtualScreen; _mode = mode; _prompt = prompt; _context = context;
        _dim = new Bitmap(shot);
        using (var g = Graphics.FromImage(_dim))
        using (var br = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
            g.FillRectangle(br, 0, 0, _dim.Width, _dim.Height);

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = virtualScreen;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        KeyPreview = true;
        Cursor = mode == PickerMode.View ? Cursors.Default : Cursors.Cross;
    }

    protected override void Dispose(bool disposing) { if (disposing) _dim.Dispose(); base.Dispose(disposing); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        base.OnKeyDown(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (_mode == PickerMode.View) { DialogResult = DialogResult.Cancel; Close(); return; }
        if (_mode == PickerMode.Point)
        {
            ResultPoint = new Point(e.X + _vs.Left, e.Y + _vs.Top);
            DialogResult = DialogResult.OK; Close(); return;
        }
        _start = _cur = e.Location; _drag = true;
    }

    protected override void OnMouseMove(MouseEventArgs e) { _cur = e.Location; Invalidate(); base.OnMouseMove(e); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_mode != PickerMode.Area || !_drag) return;
        _drag = false;
        var r = Norm(_start, e.Location);
        if (r.Width < 4 || r.Height < 4) { Invalidate(); return; }
        ResultArea = new Rectangle(r.X + _vs.Left, r.Y + _vs.Top, r.Width, r.Height);
        DialogResult = DialogResult.OK; Close();
    }

    private static Rectangle Norm(Point a, Point b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.DrawImageUnscaled(_dim, 0, 0);

        using var ctxPen = new Pen(Color.Lime, 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        using var font = new Font("Segoe UI", 8f);
        foreach (var (label, rect) in _context)
        {
            var r = new Rectangle(rect.X - _vs.Left, rect.Y - _vs.Top, rect.Width, rect.Height);
            if (_mode == PickerMode.View) g.DrawImage(_shot, r, rect.X - _vs.Left, rect.Y - _vs.Top, rect.Width, rect.Height, GraphicsUnit.Pixel);
            g.DrawRectangle(ctxPen, r);
            g.DrawString(label, font, Brushes.Lime, r.X, Math.Max(0, r.Y - 14));
        }

        if (_mode == PickerMode.Area && _drag)
        {
            var r = Norm(_start, _cur);
            if (r.Width > 0 && r.Height > 0) g.DrawImage(_shot, r, r.X, r.Y, r.Width, r.Height, GraphicsUnit.Pixel);
            g.DrawRectangle(Pens.Red, r);
            g.DrawString($"{r.X + _vs.Left},{r.Y + _vs.Top}  {r.Width}×{r.Height}", font, Brushes.Yellow, r.X, r.Bottom + 4);
        }
        else if (_mode != PickerMode.View)
        {
            g.DrawLine(Pens.Red, _cur.X, 0, _cur.X, Height);
            g.DrawLine(Pens.Red, 0, _cur.Y, Width, _cur.Y);
            g.DrawString($"{_cur.X + _vs.Left}, {_cur.Y + _vs.Top}", font, Brushes.Yellow, _cur.X + 10, _cur.Y + 10);
        }

        using var big = new Font("Segoe UI", 12f, FontStyle.Bold);
        var size = g.MeasureString(_prompt, big, 700);
        g.FillRectangle(Brushes.Black, 12, 12, size.Width + 12, size.Height + 8);
        g.DrawString(_prompt, big, Brushes.White, new RectangleF(18, 16, 700, size.Height));
    }
}
