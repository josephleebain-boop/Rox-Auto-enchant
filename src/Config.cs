using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoxReroller;

[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class Spot
{
    public int X { get; set; }
    public int Y { get; set; }
    public Spot() { }
    public Spot(int x, int y) { X = x; Y = y; }
    public override string ToString() => $"({X}, {Y})";
}

[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class Area
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public Area() { }
    public Area(int x, int y, int w, int h) { X = x; Y = y; Width = w; Height = h; }
    [JsonIgnore, Browsable(false)] public int Right => X + Width;
    [JsonIgnore, Browsable(false)] public int Bottom => Y + Height;
    public override string ToString() => $"x={X}, y={Y}, {Width}×{Height}";
}

public static class AppPaths
{
    private static string? _dir;

    /// <summary>Folder next to the .exe (settings.json, log.txt, debug/). Falls back to %APPDATA% if not writable.</summary>
    public static string Dir
    {
        get
        {
            if (_dir != null) return _dir;
            string? exe = Environment.ProcessPath;
            string baseDir = exe != null && !Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(exe)! : AppContext.BaseDirectory;
            try
            {
                string probe = Path.Combine(baseDir, ".write-test");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                _dir = baseDir;
            }
            catch
            {
                _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RoxStatReroller");
                Directory.CreateDirectory(_dir);
            }
            return _dir;
        }
    }

    public static string SettingsFile => Path.Combine(Dir, "settings.json");
    public static string LogFile => Path.Combine(Dir, "log.txt");
    public static string DebugDir => Path.Combine(Dir, "debug");
}

public sealed class AppSettings
{
    // ---- Main-window choices (not shown in the settings grid) ----
    [Browsable(false)] public List<string> Stats { get; set; } = new() { "STR", "VIT", "AGI", "DEX", "INT", "LUK" };
    [Browsable(false)] public string TargetStat { get; set; } = "STR";
    [Browsable(false)] public int MaxRerolls { get; set; } = 10;
    [Browsable(false)] public bool DryRun { get; set; } = true;

    // ---- Click points ----
    [Category("Positions (use Calibrate...)")] public Spot RerollButton { get; set; } = new(1602, 975);
    [Category("Positions (use Calibrate...)")] public Spot SaveButton { get; set; } = new(1771, 799);
    [Category("Positions (use Calibrate...)")] public Spot ConfirmButton { get; set; } = new(1105, 671);

    // ---- Read areas ----
    [Category("Positions (use Calibrate...)")] public Area StatText { get; set; } = new(1387, 778, 215, 45);
    [Category("Positions (use Calibrate...)")] public Area QualityIcon { get; set; } = new(1343, 779, 47, 43);
    [Category("Positions (use Calibrate...)")] public Area SaveLabel { get; set; } = new(1720, 779, 104, 43);
    [Category("Positions (use Calibrate...)")] public Area WarningText { get; set; } = new(675, 457, 570, 92);
    [Category("Positions (use Calibrate...)"), Description("Area around the word 'Reroll' on the Reroll button. Used to confirm the Enchant Awakening screen is open. (Estimated, please check with Calibrate.)")]
    public Area RerollLabel { get; set; } = new(1480, 958, 100, 34);

    [Category("Positions (use Calibrate...)")] public int CalibratedWidth { get; set; } = 1920;
    [Category("Positions (use Calibrate...)")] public int CalibratedHeight { get; set; } = 1080;

    // ---- Timing (ms) ----
    [Category("Timing (ms)"), Description("Wait after a Reroll (or Confirm) click before reading the result. Raise this if the game lags.")]
    public int AfterRerollWaitMs { get; set; } = 1500;
    [Category("Timing (ms)"), Description("After clicking Reroll on an orange stat, how long to wait for the 'Orange quality' warning.")]
    public int WarningWaitMs { get; set; } = 1500;
    [Category("Timing (ms)")] public int AfterConfirmWaitMs { get; set; } = 1500;
    [Category("Timing (ms)")] public int AfterSaveWaitMs { get; set; } = 1200;
    [Category("Timing (ms)"), Description("Gap between the two identical readings required before the app trusts a read.")]
    public int StableReadDelayMs { get; set; } = 350;
    [Category("Timing (ms)"), Description("Max reads tried to get two identical consecutive readings before pausing.")]
    public int StableReadAttempts { get; set; } = 4;
    [Category("Timing (ms)")] public int MouseMoveDelayMs { get; set; } = 90;
    [Category("Timing (ms)")] public int ClickHoldMs { get; set; } = 70;
    [Category("Timing (ms)")] public int DryRunIntervalMs { get; set; } = 1500;

    // ---- Safety ----
    [Category("Safety"), Description("If true, pause when the stat name cannot be read even for non-orange stats. (Orange stats always require a readable name.)")]
    public bool StrictStatReading { get; set; } = true;
    [Category("Safety"), Description("Pause if the mouse was moved by you between the app's clicks.")]
    public bool PauseOnMouseMove { get; set; } = true;
    [Category("Safety")] public int MouseMoveTolerancePx { get; set; } = 20;
    [Category("Safety"), Description("Window title of the game (exact match preferred).")]
    public string GameWindowTitle { get; set; } = "Ragnarok X";

    // ---- OCR / colour tuning ----
    [Category("Recognition")] public int OcrScale { get; set; } = 4;
    [Category("Recognition")] public int WarningOcrScale { get; set; } = 2;
    [Category("Recognition"), Description("Orange/gold hue range in degrees (0-360).")] public double OrangeHueMin { get; set; } = 15;
    [Category("Recognition")] public double OrangeHueMax { get; set; } = 52;
    [Category("Recognition")] public double MinSaturation { get; set; } = 0.45;
    [Category("Recognition")] public double MinValue { get; set; } = 0.45;
    [Category("Recognition"), Description("Share of the icon that must be orange to count as Orange quality.")]
    public double OrangeMinFraction { get; set; } = 0.20;
    [Category("Recognition"), Description("Orange share at or below this counts as definitely NOT orange. In between = uncertain (app pauses).")]
    public double NonOrangeMaxFraction { get; set; } = 0.03;
    [Category("Recognition"), Description("Green share of the Save label area that counts as 'Save button visible'.")]
    public double SaveGreenMinFraction { get; set; } = 0.45;

    // ---------------------------------------------------------------

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), Json);
                if (s != null) { s.Normalize(); return s; }
            }
        }
        catch
        {
            try { File.Copy(AppPaths.SettingsFile, AppPaths.SettingsFile + ".broken", true); } catch { }
        }
        var d = new AppSettings();
        d.Save();
        return d;
    }

    public void Save()
    {
        try { Normalize(); File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, Json)); } catch { }
    }

    public AppSettings Clone()
    {
        var c = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, Json), Json)!;
        c.Normalize();
        return c;
    }

    public void Normalize()
    {
        Stats = Stats.Select(s => (s ?? "").Trim().ToUpperInvariant()).Where(s => s.Length > 0).Distinct().ToList();
        if (Stats.Count == 0) Stats = new() { "STR", "VIT", "AGI", "DEX", "INT", "LUK" };
        if (!Stats.Contains(TargetStat)) TargetStat = Stats[0];
        if (MaxRerolls < 1) MaxRerolls = 1;
        OcrScale = Math.Clamp(OcrScale, 1, 8);
        WarningOcrScale = Math.Clamp(WarningOcrScale, 1, 6);
        StableReadAttempts = Math.Clamp(StableReadAttempts, 2, 12);
    }

    public void ResetPositionsToDefault()
    {
        var d = new AppSettings();
        RerollButton = d.RerollButton; SaveButton = d.SaveButton; ConfirmButton = d.ConfirmButton;
        StatText = d.StatText; QualityIcon = d.QualityIcon; SaveLabel = d.SaveLabel;
        WarningText = d.WarningText; RerollLabel = d.RerollLabel;
        CalibratedWidth = d.CalibratedWidth; CalibratedHeight = d.CalibratedHeight;
    }

    [JsonIgnore, Browsable(false)]
    public IEnumerable<(string Name, Area Area)> AllAreas => new[]
    {
        ("Rerolled stat text", StatText), ("Quality icon", QualityIcon), ("Save label", SaveLabel),
        ("Warning text", WarningText), ("Reroll label", RerollLabel)
    };

    [JsonIgnore, Browsable(false)]
    public IEnumerable<(string Name, Spot Spot)> AllSpots => new[]
    {
        ("Reroll button", RerollButton), ("Save button", SaveButton), ("Confirm button", ConfirmButton)
    };
}
