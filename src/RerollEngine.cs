using System.Drawing.Imaging;

namespace RoxReroller;

internal sealed class AttentionException : Exception
{
    public AttentionException(string message) : base(message) { }
}

/// <summary>
/// The reroll loop. Clicks only happen in ClickAsync; anything unexpected throws AttentionException, which pauses the run.
/// </summary>
internal sealed class RerollEngine
{
    private readonly AppSettings _c;
    private readonly ScreenReader _reader;
    private readonly string _target;
    private readonly int _max;
    private readonly bool _dry;

    private volatile bool _paused;
    private CancellationToken _ct;
    private int _attempts;
    private Point? _expectCursor;
    private IntPtr _hwnd;
    private string _lastSummary = "";

    public event Action<string>? Logged;
    public event Action<string>? StatusChanged;
    public event Action<int, int>? AttemptsChanged;
    public event Action<string>? LastReadChanged;
    public event Action<string>? AttentionRaised;
    public event Action<string>? Completed;

    public bool Paused => _paused;
    public void SetPaused(bool p) => _paused = p;

    public RerollEngine(AppSettings cfg, OcrService ocr, string target, int max, bool dryRun)
    {
        _c = cfg; _reader = new ScreenReader(cfg, ocr);
        _target = target.ToUpperInvariant(); _max = max; _dry = dryRun;
    }

    private void Log(string m) => Logged?.Invoke(m);
    private void Status(string m) => StatusChanged?.Invoke(m);

    public async Task RunAsync(CancellationToken ct)
    {
        _ct = ct;
        Status(_dry ? "Running (DRY RUN – nothing is clicked)" : "Running");
        Log($"Started. Target={_target} + orange, max rerolls={_max}, dry-run={_dry}");
        AttemptsChanged?.Invoke(0, _max);
        string outcome;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                await PauseGateAsync();
                try
                {
                    string? done = await IterationAsync();
                    if (done != null) { outcome = done; break; }
                }
                catch (AttentionException ex)
                {
                    if (_dry)
                    {
                        Log("DRY RUN – a real run would PAUSE here: " + ex.Message);
                        await Task.Delay(_c.DryRunIntervalMs, ct);
                        continue;
                    }
                    await HandleAttentionAsync(ex.Message);
                }
            }
        }
        catch (OperationCanceledException) { outcome = "Stopped by user."; }
        catch (Exception ex) { outcome = "Stopped after an unexpected error: " + ex.Message; Log(ex.ToString()); }
        Completed?.Invoke(outcome);
    }

    private async Task PauseGateAsync()
    {
        if (!_paused) return;
        Status("Paused – press Resume (F9) to continue");
        while (_paused) { _ct.ThrowIfCancellationRequested(); await Task.Delay(100, _ct); }
        _expectCursor = null;
        Status(_dry ? "Running (DRY RUN – nothing is clicked)" : "Running");
    }

    private async Task HandleAttentionAsync(string reason)
    {
        _paused = true;
        _expectCursor = null;
        SaveDebug(reason);
        Log("⚠ PAUSED: " + reason);
        Status("NEEDS ATTENTION – " + reason);
        AttentionRaised?.Invoke(reason);
        while (_paused) { _ct.ThrowIfCancellationRequested(); await Task.Delay(100, _ct); }
        Status("Running");
        Log("Resumed by user; re-reading the screen from scratch.");
    }

    // ------------------------------------------------------------------

    private async Task<string?> IterationAsync()
    {
        if (!_dry) await EnsureGameAsync(false);

        var w = await _reader.ReadWarningAsync();
        if (w.Visible)
            throw new AttentionException("The 'Orange quality' warning is already on screen before the app clicked anything. Handle it yourself in the game, then Resume.");

        var (labelOk, labelText) = await _reader.ReadRerollLabelAsync();
        if (!labelOk)
            throw new AttentionException($"Can't see the Reroll button label (OCR read: \"{Trim(labelText)}\"). Wrong screen, a popup is open, or the layout/calibration is off.");

        var row = await ReadStableRowAsync();

        bool expectWarning = false;
        switch (row.Kind)
        {
            case RowKind.Unclear:
                throw new AttentionException("Can't tell what the Rerolled Stat row shows: " + row.Summary);
            case RowKind.Empty:
                Log("No rerolled stat yet (first reroll).");
                break;
            case RowKind.Present:
                if (row.Quality.Kind == QualityKind.Uncertain)
                    throw new AttentionException("Quality colour is ambiguous: " + row.Summary + "  | " + row.Quality.Detail);
                bool orange = row.Quality.Kind == QualityKind.Orange;
                if (row.Stat == null && (orange || _c.StrictStatReading))
                    throw new AttentionException("Can't read the stat name: " + row.Summary);
                if (orange && row.Stat == _target)
                    return await SaveAsync();
                expectWarning = orange;
                Log($"Read {(row.Stat ?? "?")} / {(orange ? "orange" : "not orange")} → " +
                    (row.Stat == _target ? "right stat but not orange" : "not the target") + ", rerolling.");
                break;
        }

        if (_attempts >= _max)
            return $"Reached the maximum of {_max} rerolls without a match. Nothing was saved.";

        if (_dry)
        {
            _attempts++;
            AttemptsChanged?.Invoke(_attempts, _max);
            Log($"DRY RUN: would click Reroll{(expectWarning ? ", then Confirm once the orange warning is verified" : "")}.");
            await Task.Delay(_c.DryRunIntervalMs, _ct);
            return null;
        }

        _attempts++;
        AttemptsChanged?.Invoke(_attempts, _max);
        await ClickAsync(_c.RerollButton, "Reroll");

        if (expectWarning)
        {
            bool seen = await WaitForWarningAsync(_c.WarningWaitMs);
            if (seen)
            {
                var again = await _reader.ReadWarningAsync();       // fresh capture right before Confirm
                if (!again.Visible)
                    throw new AttentionException("The orange warning could not be re-verified just before Confirm; not clicking.");
                await ClickAsync(_c.ConfirmButton, "Confirm");
                await Task.Delay(_c.AfterConfirmWaitMs, _ct);
            }
            else
            {
                Log("Expected the orange warning but it did not appear; continuing.");
                await Task.Delay(_c.AfterRerollWaitMs, _ct);
            }
        }
        else
        {
            int half = Math.Max(100, _c.AfterRerollWaitMs / 2);
            await Task.Delay(half, _ct);
            var w2 = await _reader.ReadWarningAsync();
            if (w2.Visible)
                throw new AttentionException("An 'Orange quality' warning appeared although the previous stat wasn't orange. NOT clicking Confirm.");
            await Task.Delay(Math.Max(0, _c.AfterRerollWaitMs - half), _ct);
        }
        return null;
    }

    private async Task<bool> WaitForWarningAsync(int timeoutMs)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            var w = await _reader.ReadWarningAsync();
            if (w.Visible)
            {
                await Task.Delay(_c.StableReadDelayMs, _ct);
                var w2 = await _reader.ReadWarningAsync();
                if (w2.Visible) return true;
            }
            await Task.Delay(150, _ct);
        }
        return false;
    }

    private async Task<RowReading> ReadStableRowAsync()
    {
        string? prevKey = null;
        RowReading? last = null;
        for (int i = 0; i < _c.StableReadAttempts; i++)
        {
            _ct.ThrowIfCancellationRequested();
            var cur = await _reader.ReadRowAsync();
            if (prevKey != null && cur.Key == prevKey)
            {
                _lastSummary = cur.Summary;
                LastReadChanged?.Invoke(cur.Summary);
                return cur;
            }
            prevKey = cur.Key; last = cur;
            LastReadChanged?.Invoke(cur.Summary + "  (waiting for a repeat reading)");
            await Task.Delay(_c.StableReadDelayMs, _ct);
        }
        throw new AttentionException("Readings kept changing, so none was trusted. Last: " + (last?.Summary ?? "none"));
    }

    private async Task<string> SaveAsync()
    {
        Log($"MATCH: {_target} and orange detected.");
        if (_dry)
            return $"DRY RUN: {_target} + orange detected – a real run would click Save now. Nothing was clicked.";

        var again = await _reader.ReadRowAsync();          // one more independent look before the only irreversible click
        if (again.Kind != RowKind.Present || again.Stat != _target || again.Quality.Kind != QualityKind.Orange)
            throw new AttentionException("Final check before Save disagreed with the earlier reading: " + again.Summary);

        await ClickAsync(_c.SaveButton, "Save");
        await Task.Delay(_c.AfterSaveWaitMs, _ct);
        var after = await _reader.ReadRowAsync();
        string note = after.Kind == RowKind.Present ? " (Save button still visible – please verify in the game.)" : "";
        return $"SAVED {_target} (orange) after {_attempts} reroll(s).{note}";
    }

    // ------------------------------------------------------------------ clicking & guards

    private async Task EnsureGameAsync(bool needForeground)
    {
        if (_hwnd == IntPtr.Zero || !Native.IsWindow(_hwnd))
            _hwnd = Native.FindWindowByTitle(_c.GameWindowTitle);
        if (_hwnd == IntPtr.Zero)
            throw new AttentionException($"Game window \"{_c.GameWindowTitle}\" not found.");
        if (Native.IsHungAppWindow(_hwnd))
            throw new AttentionException("The game window is not responding.");
        if (needForeground && Native.GetForegroundWindow() != _hwnd)
        {
            Native.BringToFront(_hwnd);
            await Task.Delay(300, _ct);
            if (Native.GetForegroundWindow() != _hwnd)
                throw new AttentionException("The game window is not in the foreground, so the click was NOT sent. Click the game once and Resume.");
        }
    }

    private async Task ClickAsync(Spot s, string what)
    {
        _ct.ThrowIfCancellationRequested();
        if (Native.IsKeyDown(Native.VK_F8)) throw new OperationCanceledException();
        await EnsureGameAsync(true);

        if (_c.PauseOnMouseMove && _expectCursor is Point e)
        {
            var cur = Native.GetCursor();
            if (Math.Abs(cur.X - e.X) > _c.MouseMoveTolerancePx || Math.Abs(cur.Y - e.Y) > _c.MouseMoveTolerancePx)
                throw new AttentionException("The mouse was moved since the app's last click (assumed you want control). No click sent.");
        }

        Log($"Click {what} at ({s.X}, {s.Y})");
        try { await Native.ClickAsync(s.X, s.Y, _c.MouseMoveDelayMs, _c.ClickHoldMs, _ct); }
        catch (InvalidOperationException ex) { throw new AttentionException(ex.Message); }
        _expectCursor = Native.GetCursor();
    }

    private void SaveDebug(string reason)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DebugDir);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            using (var full = Imaging.CaptureScreen())
                full.Save(Path.Combine(AppPaths.DebugDir, stamp + "-screen.png"), ImageFormat.Png);
            File.WriteAllText(Path.Combine(AppPaths.DebugDir, stamp + "-reason.txt"), reason + "\r\n\r\nLast reading: " + _lastSummary);
        }
        catch { }
    }

    private static string Trim(string s) => s.Length <= 80 ? s : s[..80] + "…";
}
