using System.Drawing.Imaging;

namespace RoxReroller;

internal enum RowKind { Present, Empty, Unclear }

internal sealed class RowReading
{
    public RowKind Kind = RowKind.Unclear;
    public string? Stat;
    public string StatRaw = "";
    public QualityResult Quality = QualityResult.Unknown("not read");
    public bool SaveVisible;
    public double SaveGreen;
    public string SaveOcr = "";
    public string Why = "";

    public string Key => $"{Kind}|{Stat}|{Quality.Kind}";
    public string Summary =>
        $"row={Kind}  stat={(Stat ?? "?")}  text=\"{StatRaw}\"  quality={Quality.Kind} ({Quality.Dominant}, orange {Quality.OrangeFraction:P0}, other {Quality.OtherFraction:P0})  " +
        $"save={(SaveVisible ? "visible" : "not visible")} (green {SaveGreen:P0}){(Why.Length > 0 ? "  [" + Why + "]" : "")}";
}

internal sealed class WarningReading
{
    public bool Visible;
    public string Text = "";
}

/// <summary>Captures the configured screen areas and turns them into structured readings.</summary>
internal sealed class ScreenReader
{
    private readonly AppSettings _c;
    private readonly OcrService _ocr;

    private static readonly (GrayMode, bool?)[] StatPreps = { (GrayMode.MaxChannel, null), (GrayMode.Luma, null) };
    private static readonly (GrayMode, bool?)[] LabelPreps =
        { (GrayMode.Luma, null), (GrayMode.Luma, true), (GrayMode.MaxChannel, null), (GrayMode.Luma, false) };

    public ScreenReader(AppSettings c, OcrService ocr) { _c = c; _ocr = ocr; }

    private static void Keep(List<(string, Bitmap)>? sink, string name, Bitmap b) => sink?.Add((name, (Bitmap)b.Clone()));

    public async Task<RowReading> ReadRowAsync(List<(string, Bitmap)>? images = null)
    {
        var r = new RowReading();

        // quality colour
        using (var icon = Imaging.Capture(_c.QualityIcon))
        {
            Keep(images, "quality icon", icon);
            var px = Imaging.GetBgra(icon, out int stride);
            r.Quality = ColorClassifier.Classify(px, icon.Width, icon.Height, stride, _c);
        }

        // stat text
        using (var text = Imaging.Capture(_c.StatText))
        {
            Keep(images, "stat text (raw)", text);
            bool first = true;
            foreach (var (mode, inv) in StatPreps)
            {
                using var prep = Imaging.PrepareForOcr(text, _c.OcrScale, mode, inv);
                if (first) { Keep(images, "stat text (prepared for OCR)", prep); first = false; }
                string t = await _ocr.ReadAsync(prep);
                var p = TextLogic.ParseStat(t, _c.Stats);
                if (r.StatRaw.Length == 0 || p.Stat != null) r.StatRaw = t;
                if (p.Stat != null) { r.Stat = p.Stat; r.Why = ""; break; }
                r.Why = p.Reason;
            }
        }

        // Save button present?
        using (var save = Imaging.Capture(_c.SaveLabel))
        {
            Keep(images, "save label", save);
            var px = Imaging.GetBgra(save, out int stride);
            r.SaveGreen = ColorClassifier.GreenFraction(px, save.Width, save.Height, stride);
            r.SaveVisible = r.SaveGreen >= _c.SaveGreenMinFraction;
            if (!r.SaveVisible)
            {
                foreach (var (mode, inv) in LabelPreps)
                {
                    using var prep = Imaging.PrepareForOcr(save, 3, mode, inv);
                    string t = await _ocr.ReadAsync(prep);
                    r.SaveOcr = t;
                    if (TextLogic.IsSaveLabel(t)) { r.SaveVisible = true; break; }
                }
            }
        }

        if (r.SaveVisible) r.Kind = RowKind.Present;
        else if (r.Stat == null && TextLogic.Clean(r.StatRaw).Length <= 2 && r.Quality.Kind == QualityKind.Other)
        { r.Kind = RowKind.Empty; r.Why = ""; }
        else
        {
            r.Kind = RowKind.Unclear;
            if (r.Why.Length == 0) r.Why = "no Save button but something is in the stat area";
        }
        return r;
    }

    public async Task<WarningReading> ReadWarningAsync(List<(string, Bitmap)>? images = null)
    {
        using var bmp = Imaging.Capture(_c.WarningText);
        Keep(images, "warning area (raw)", bmp);
        using var prep = Imaging.PrepareForOcr(bmp, _c.WarningOcrScale, GrayMode.Luma);
        Keep(images, "warning area (prepared for OCR)", prep);
        string t = await _ocr.ReadAsync(prep);
        return new WarningReading { Text = t, Visible = TextLogic.IsOrangeWarning(t) };
    }

    public async Task<(bool Ok, string Text)> ReadRerollLabelAsync(List<(string, Bitmap)>? images = null)
    {
        using var bmp = Imaging.Capture(_c.RerollLabel);
        Keep(images, "reroll label", bmp);
        string all = "";
        foreach (var (mode, inv) in LabelPreps)
        {
            using var prep = Imaging.PrepareForOcr(bmp, 3, mode, inv);
            string t = await _ocr.ReadAsync(prep);
            all += (all.Length > 0 ? " | " : "") + t;
            if (TextLogic.IsRerollLabel(t)) return (true, t);
        }
        return (false, all);
    }
}
