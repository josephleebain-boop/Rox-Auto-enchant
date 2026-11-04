using System.Media;

namespace RoxReroller;

internal sealed class MainForm : Form
{
    private enum RunState { Idle, Running, Paused }

    private AppSettings _cfg;
    private readonly OcrService _ocr = new();
    private CancellationTokenSource? _cts;
    private RerollEngine? _engine;
    private RunState _state = RunState.Idle;
    private bool _f8Prev, _f9Prev;

    private readonly ComboBox _cmbStat = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly NumericUpDown _numMax = new() { Minimum = 1, Maximum = 100000, Width = 80 };
    private readonly CheckBox _chkDry = new() { Text = "Dry-run (read the screen and report, never click)", AutoSize = true };
    private readonly Button _btnStart = new() { Text = "Start", Width = 110, Height = 34 };
    private readonly Button _btnPause = new() { Text = "Pause (F9)", Width = 110, Height = 34, Enabled = false };
    private readonly Button _btnStop = new() { Text = "STOP (F8)", Width = 110, Height = 34, Enabled = false };
    private readonly Button _btnCalibrate = new() { Text = "Calibrate…", Width = 110, Height = 30 };
    private readonly Button _btnSettings = new() { Text = "Settings…", Width = 110, Height = 30 };
    private readonly Button _btnTest = new() { Text = "Test read", Width = 110, Height = 30 };
    private readonly Label _lblStatus = new() { AutoSize = true, MaximumSize = new Size(470, 0), Font = new Font("Segoe UI", 10f, FontStyle.Bold), Text = "Idle" };
    private readonly Label _lblAttempts = new() { AutoSize = true, Text = "Rerolls: 0 / 0" };
    private readonly Label _lblLast = new() { AutoSize = true, MaximumSize = new Size(470, 0), Text = "Last read: –" };
    private readonly TextBox _txtLog = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas", 8.5f) };
    private readonly System.Windows.Forms.Timer _keyTimer = new() { Interval = 25 };
    private readonly NotifyIcon _tray = new() { Icon = SystemIcons.Application, Text = "ROX Stat Reroller" };

    public MainForm()
    {
        _cfg = AppSettings.Load();

        Text = "ROX Stat Reroller";
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(490, 660);

        BuildLayout();
        LoadUiFromConfig();

        _btnStart.Click += async (_, _) => await OnStartAsync();
        _btnPause.Click += (_, _) => TogglePause();
        _btnStop.Click += (_, _) => EmergencyStop("Stop button");
        _btnCalibrate.Click += (_, _) => OnCalibrate();
        _btnSettings.Click += (_, _) => OnSettings();
        _btnTest.Click += async (_, _) => await OnTestAsync();

        _keyTimer.Tick += (_, _) => PollKeys();
        _keyTimer.Start();

        Load += (_, _) => { PositionOutOfTheWay(); AppendLog("Ready. OCR: Windows built-in. Dry-run is ON by default – switch it off only after Test read looks right."); };
        FormClosing += (_, _) => { _cts?.Cancel(); _keyTimer.Stop(); _tray.Visible = false; _tray.Dispose(); SaveUiToConfig(); };
    }

    // ---------------------------------------------------------------- layout

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        FlowLayoutPanel Flow() => new() { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 2) };
        Label Lbl(string t) => new() { Text = t, AutoSize = true, Margin = new Padding(0, 7, 4, 0) };

        var row1 = Flow();
        row1.Controls.AddRange(new Control[] { Lbl("Target stat:"), _cmbStat, Lbl("   Max rerolls:"), _numMax });
        root.Controls.Add(row1);
        root.Controls.Add(_chkDry);

        var row3 = Flow();
        row3.Controls.AddRange(new Control[] { _btnStart, _btnPause, _btnStop });
        root.Controls.Add(row3);

        root.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(470, 0), ForeColor = Color.DimGray,
            Text = "F8 = emergency stop, F9 = pause/resume (work even while the game has focus). Select the equipment in the game first."
        });
        root.Controls.Add(_lblStatus);
        root.Controls.Add(_lblAttempts);
        root.Controls.Add(_lblLast);

        var row8 = Flow();
        row8.Controls.AddRange(new Control[] { _btnCalibrate, _btnSettings, _btnTest });
        root.Controls.Add(row8);

        root.Controls.Add(_txtLog);
        root.RowCount = root.Controls.Count;
        for (int i = 1; i < root.Controls.Count - 1; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);
    }

    private void PositionOutOfTheWay()
    {
        var scr = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(scr.Left, scr.Top + 20);
        if (Height > scr.Height - 30) Height = scr.Height - 30;
    }

    private void LoadUiFromConfig()
    {
        _cmbStat.Items.Clear();
        foreach (var s in _cfg.Stats) _cmbStat.Items.Add(s);
        int idx = _cmbStat.Items.IndexOf(_cfg.TargetStat);
        _cmbStat.SelectedIndex = idx >= 0 ? idx : 0;
        _numMax.Value = Math.Clamp(_cfg.MaxRerolls, 1, 100000);
        _chkDry.Checked = _cfg.DryRun;
        _lblAttempts.Text = $"Rerolls: 0 / {_cfg.MaxRerolls}";
    }

    private void SaveUiToConfig()
    {
        if (_cmbStat.SelectedItem is string s) _cfg.TargetStat = s;
        _cfg.MaxRerolls = (int)_numMax.Value;
        _cfg.DryRun = _chkDry.Checked;
        _cfg.Save();
    }

    // ---------------------------------------------------------------- helpers

    private void UI(Action a)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) BeginInvoke(a); else a();
    }

    private void AppendLog(string msg)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
        UI(() =>
        {
            if (_txtLog.TextLength > 150_000) _txtLog.Clear();
            _txtLog.AppendText(line + "\r\n");
        });
        try { File.AppendAllText(AppPaths.LogFile, line + "\r\n"); } catch { }
    }

    private void SetState(RunState s)
    {
        _state = s;
        bool idle = s == RunState.Idle;
        _btnStart.Enabled = idle;
        _btnPause.Enabled = !idle;
        _btnStop.Enabled = !idle;
        _btnPause.Text = s == RunState.Paused ? "Resume (F9)" : "Pause (F9)";
        _cmbStat.Enabled = _numMax.Enabled = _chkDry.Enabled = idle;
        _btnCalibrate.Enabled = _btnSettings.Enabled = _btnTest.Enabled = idle;
    }

    private bool EnsureOcr()
    {
        if (_ocr.IsReady) return true;
        if (_ocr.TryInit(out string err)) { AppendLog("OCR ready: " + _ocr.Description); return true; }
        MessageBox.Show(this, err, "OCR not available", MessageBoxButtons.OK, MessageBoxIcon.Error);
        return false;
    }

    private void PollKeys()
    {
        bool f8 = Native.IsKeyDown(Native.VK_F8), f9 = Native.IsKeyDown(Native.VK_F9);
        if (f8 && !_f8Prev) EmergencyStop("F8");
        if (f9 && !_f9Prev) TogglePause();
        _f8Prev = f8; _f9Prev = f9;
    }

    private void EmergencyStop(string source)
    {
        if (_state == RunState.Idle) return;
        AppendLog($"EMERGENCY STOP ({source}).");
        _engine?.SetPaused(false);
        _cts?.Cancel();
    }

    private void TogglePause()
    {
        if (_engine == null) return;
        if (_state == RunState.Running) { _engine.SetPaused(true); SetState(RunState.Paused); AppendLog("Paused by user."); }
        else if (_state == RunState.Paused) { _engine.SetPaused(false); SetState(RunState.Running); AppendLog("Resume requested."); _lblStatus.ForeColor = SystemColors.ControlText; }
    }

    // ---------------------------------------------------------------- start

    private (List<string> blockers, List<string> warnings) Preflight()
    {
        var blockers = new List<string>();
        var warnings = new List<string>();
        var scr = Screen.PrimaryScreen!.Bounds;

        if (scr.Width != _cfg.CalibratedWidth || scr.Height != _cfg.CalibratedHeight)
            warnings.Add($"Screen is {scr.Width}×{scr.Height} but the positions were set up for {_cfg.CalibratedWidth}×{_cfg.CalibratedHeight}. Re-run Calibrate.");

        foreach (var (name, a) in _cfg.AllAreas)
            if (a.Width < 4 || a.Height < 4 || a.X < scr.Left || a.Y < scr.Top || a.Right > scr.Right || a.Bottom > scr.Bottom)
                blockers.Add($"'{name}' area ({a}) is outside the screen or too small.");
        foreach (var (name, s) in _cfg.AllSpots)
            if (!scr.Contains(s.X, s.Y)) blockers.Add($"'{name}' click point {s} is outside the screen.");

        var me = Bounds;
        foreach (var (name, a) in _cfg.AllAreas)
            if (me.IntersectsWith(new Rectangle(a.X, a.Y, a.Width, a.Height))) blockers.Add($"This control window covers the '{name}' area. Move it (drag it to the far left).");
        foreach (var (name, s) in _cfg.AllSpots)
            if (me.IntersectsWith(new Rectangle(s.X - 15, s.Y - 15, 30, 30))) blockers.Add($"This control window covers the '{name}' click point. Move it.");

        IntPtr h = Native.FindWindowByTitle(_cfg.GameWindowTitle);
        if (h == IntPtr.Zero) warnings.Add($"Game window \"{_cfg.GameWindowTitle}\" not found.");
        else
        {
            var r = Native.GetWindowBounds(h);
            var wa = Screen.PrimaryScreen!.WorkingArea;
            bool Close(Rectangle b) => Math.Abs(r.Width - b.Width) <= 24 && Math.Abs(r.Height - b.Height) <= 24;
            if (!Close(scr) && !Close(wa))
                warnings.Add($"Game window is {r.Width}×{r.Height}; expected it maximized (about {wa.Width}×{wa.Height}).");
        }
        return (blockers, warnings);
    }

    private async Task OnStartAsync()
    {
        try
        {
            SaveUiToConfig();
            if (!EnsureOcr()) return;
            bool dry = _chkDry.Checked;

            var (blockers, warnings) = Preflight();
            if (blockers.Count > 0)
            {
                MessageBox.Show(this, "Cannot start:\n\n• " + string.Join("\n• ", blockers), "Fix this first", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (warnings.Count > 0)
            {
                foreach (var w in warnings) AppendLog("WARNING: " + w);
                if (!dry)
                {
                    var r = MessageBox.Show(this, "Warnings:\n\n• " + string.Join("\n• ", warnings) + "\n\nStart anyway?",
                        "Layout check", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                    if (r != DialogResult.Yes) return;
                }
            }

            if (!dry)
            {
                var r = MessageBox.Show(this,
                    $"Live mode: the app will click in the game.\n\nTarget: {_cfg.TargetStat} + orange\nMax rerolls: {_cfg.MaxRerolls}\n\n" +
                    "Make sure the equipment is selected on the Enchant Awakening screen.\nPress F8 at any time to stop.",
                    "Start live run?", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (r != DialogResult.OK) return;
            }

            _cts = new CancellationTokenSource();
            _engine = new RerollEngine(_cfg.Clone(), _ocr, _cfg.TargetStat, _cfg.MaxRerolls, dry);
            _engine.Logged += AppendLog;
            _engine.StatusChanged += t => UI(() => { _lblStatus.Text = t; _lblStatus.ForeColor = t.StartsWith("NEEDS") ? Color.Firebrick : SystemColors.ControlText; });
            _engine.AttemptsChanged += (n, m) => UI(() => _lblAttempts.Text = $"Rerolls: {n} / {m}");
            _engine.LastReadChanged += t => UI(() => _lblLast.Text = "Last read: " + t);
            _engine.AttentionRaised += reason => UI(() =>
            {
                SetState(RunState.Paused);
                SystemSounds.Exclamation.Play();
                Native.Flash(Handle);
                _tray.Visible = true;
                _tray.ShowBalloonTip(5000, "ROX Stat Reroller paused", reason, ToolTipIcon.Warning);
            });
            _engine.Completed += outcome => UI(() =>
            {
                AppendLog("FINISHED: " + outcome);
                _lblStatus.Text = outcome;
                _lblStatus.ForeColor = outcome.StartsWith("SAVED") ? Color.ForestGreen : SystemColors.ControlText;
                SetState(RunState.Idle);
                if (outcome.StartsWith("SAVED") || outcome.StartsWith("DRY RUN")) SystemSounds.Asterisk.Play();
                _engine = null;
            });

            SetState(RunState.Running);
            var engine = _engine; var token = _cts.Token;
            if (!dry)
            {
                // give the game focus before the first click
                IntPtr h = Native.FindWindowByTitle(_cfg.GameWindowTitle);
                if (h != IntPtr.Zero) Native.BringToFront(h);
                await Task.Delay(400);
            }
            _ = Task.Run(() => engine.RunAsync(token));
        }
        catch (Exception ex)
        {
            AppendLog("Start failed: " + ex.Message);
            SetState(RunState.Idle);
            MessageBox.Show(this, ex.ToString(), "Start failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ---------------------------------------------------------------- tools

    private void OnCalibrate()
    {
        SaveUiToConfig();
        using var dlg = new CalibrationForm(_cfg);
        dlg.ShowDialog(this);
        _cfg.Save();
        AppendLog("Calibration closed. Positions saved.");
    }

    private void OnSettings()
    {
        SaveUiToConfig();
        using var dlg = new SettingsForm(_cfg);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result != null)
        {
            _cfg = dlg.Result;
            _cfg.Save();
            LoadUiFromConfig();
            AppendLog("Settings saved. Stat list: " + string.Join(", ", _cfg.Stats));
        }
    }

    private async Task OnTestAsync()
    {
        if (!EnsureOcr()) return;
        try
        {
            SetEnabled(false);
            var reader = new ScreenReader(_cfg, _ocr);
            var images = new List<(string, Bitmap)>();
            var row = await reader.ReadRowAsync(images);
            var warn = await reader.ReadWarningAsync(images);
            var label = await reader.ReadRerollLabelAsync(images);
            string report =
                "ROWS / STAT\r\n" + row.Summary + "\r\n\r\n" +
                "Quality detail: " + row.Quality.Detail + "\r\n" +
                "Save label OCR: \"" + row.SaveOcr + "\"\r\n\r\n" +
                $"WARNING AREA\r\nOCR: \"{warn.Text}\"\r\nOrange warning recognised: {warn.Visible}\r\n\r\n" +
                $"REROLL LABEL\r\nOCR: \"{label.Text}\"\r\nRecognised: {label.Ok}\r\n\r\n" +
                "Decision a real run would take on this state:\r\n" + Describe(row, warn.Visible);
            AppendLog("Test read: " + row.Summary);
            using var dlg = new ResultForm(report, images);
            dlg.ShowDialog(this);
            foreach (var (_, b) in images) b.Dispose();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.ToString(), "Test read failed"); }
        finally { SetEnabled(true); }
    }

    private void SetEnabled(bool on) => _btnStart.Enabled = _btnTest.Enabled = _btnCalibrate.Enabled = _btnSettings.Enabled = on;

    private string Describe(RowReading r, bool warning)
    {
        if (warning) return "PAUSE – the orange warning is open and the app never starts with it open.";
        switch (r.Kind)
        {
            case RowKind.Unclear: return "PAUSE – row unclear.";
            case RowKind.Empty: return "Reroll (no rerolled stat yet).";
            default:
                if (r.Quality.Kind == QualityKind.Uncertain) return "PAUSE – quality colour ambiguous.";
                bool orange = r.Quality.Kind == QualityKind.Orange;
                if (r.Stat == null && (orange || _cfg.StrictStatReading)) return "PAUSE – stat name unreadable.";
                if (orange && r.Stat == _cfg.TargetStat) return $"SAVE – {r.Stat} + orange matches the target ({_cfg.TargetStat}).";
                return orange ? "Reroll and Confirm the orange warning (stat is not the target)." : "Reroll (stat is not orange).";
        }
    }
}
