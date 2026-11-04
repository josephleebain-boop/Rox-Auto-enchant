// Pure logic (no Windows / System.Drawing dependencies) so it can be unit-tested anywhere.
namespace RoxReroller;

public enum QualityKind { Orange, Other, Uncertain }

public sealed record QualityResult(QualityKind Kind, string Dominant, double OrangeFraction, double OtherFraction, int MaskPixels, string Detail)
{
    public static QualityResult Unknown(string why) => new(QualityKind.Uncertain, "unknown", 0, 0, 0, why);
}

public sealed record StatParse(string? Stat, string Clean, string Reason);

public static class ColorClassifier
{
    private static readonly string[] Names = { "orange", "red", "yellow", "green", "blue", "purple" };

    public static void ToHsv(byte r, byte g, byte b, out double hue, out double sat, out double val)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf)), min = Math.Min(rf, Math.Min(gf, bf)), d = max - min;
        val = max;
        sat = max <= 0 ? 0 : d / max;
        if (d <= 1e-9) { hue = 0; return; }
        if (max == rf) hue = 60 * (((gf - bf) / d) % 6);
        else if (max == gf) hue = 60 * (((bf - rf) / d) + 2);
        else hue = 60 * (((rf - gf) / d) + 4);
        if (hue < 0) hue += 360;
    }

    private static int Bucket(double hue, AppSettings c)
    {
        if (hue >= c.OrangeHueMin && hue <= c.OrangeHueMax) return 0;
        if (hue < c.OrangeHueMin || hue >= 345) return 1;
        if (hue < 70) return 2;
        if (hue < 170) return 3;
        if (hue < 262) return 4;
        return 5;
    }

    /// <summary>Classifies the quality icon from BGRA pixels. Only an inscribed ellipse is inspected (ignores corners/background).</summary>
    public static QualityResult Classify(byte[] bgra, int width, int height, int stride, AppSettings c)
    {
        if (width < 4 || height < 4 || bgra.Length < stride * height) return QualityResult.Unknown("icon area too small / invalid");
        var counts = new int[Names.Length];
        int total = 0;
        double cx = (width - 1) / 2.0, cy = (height - 1) / 2.0;
        double rx = Math.Max(1, width * 0.42), ry = Math.Max(1, height * 0.42);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double dx = (x - cx) / rx, dy = (y - cy) / ry;
                if (dx * dx + dy * dy > 1) continue;
                total++;
                int o = y * stride + x * 4;
                ToHsv(bgra[o + 2], bgra[o + 1], bgra[o], out double hue, out double sat, out double val);
                if (sat < c.MinSaturation || val < c.MinValue) continue;
                counts[Bucket(hue, c)]++;
            }
        }
        if (total == 0) return QualityResult.Unknown("no pixels sampled");

        double orange = counts[0] / (double)total;
        double other = 0; int otherIdx = -1;
        for (int i = 1; i < counts.Length; i++)
        {
            double f = counts[i] / (double)total;
            if (f > other) { other = f; otherIdx = i; }
        }
        string detail = string.Join(" ", Names.Select((n, i) => $"{n}={counts[i] / (double)total:P0}"));
        string dominant = orange >= other ? (orange > 0.02 ? "orange" : "white/grey") : Names[otherIdx];
        if (other <= 0.02 && orange <= 0.02) dominant = "white/grey";

        QualityKind kind;
        if (orange >= c.OrangeMinFraction && orange >= 2 * other) kind = QualityKind.Orange;
        else if (orange <= c.NonOrangeMaxFraction) kind = QualityKind.Other;
        else kind = QualityKind.Uncertain;
        return new QualityResult(kind, dominant, orange, other, total, detail);
    }

    /// <summary>Share of pixels that are clearly green (the Save button).</summary>
    public static double GreenFraction(byte[] bgra, int width, int height, int stride)
    {
        if (width < 1 || height < 1 || bgra.Length < stride * height) return 0;
        int green = 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int o = y * stride + x * 4;
                ToHsv(bgra[o + 2], bgra[o + 1], bgra[o], out double hue, out double sat, out double val);
                if (sat >= 0.35 && val >= 0.5 && hue >= 70 && hue <= 170) green++;
            }
        return green / (double)(width * height);
    }
}

public static class TextLogic
{
    public static string Clean(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char ch in s.ToUpperInvariant()) if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        return sb.ToString();
    }

    public static string LettersLower(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char ch in s) if (char.IsLetter(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    // Characters OCR commonly confuses are treated as equal when comparing stat names.
    private static char Fold(char c) => c switch
    {
        'L' or 'I' or '1' => 'I',
        'O' or '0' => 'O',
        'S' or '5' => 'S',
        'B' or '8' => 'B',
        _ => c
    };

    /// <summary>
    /// Finds exactly one configured stat name in the OCR text. The name must stand alone (not part of a longer word)
    /// and a digit must follow it (the stat value). Zero or several different matches => Stat = null.
    /// </summary>
    public static StatParse ParseStat(string raw, IEnumerable<string> stats)
    {
        string t = Clean(raw);
        if (t.Length == 0) return new StatParse(null, t, "no text");
        var found = new List<string>();
        foreach (var s in stats)
        {
            string name = Clean(s);
            if (name.Length == 0) continue;
            for (int i = 0; i + name.Length <= t.Length; i++)
            {
                bool ok = true;
                for (int k = 0; k < name.Length; k++)
                    if (Fold(t[i + k]) != Fold(name[k])) { ok = false; break; }
                if (!ok) continue;
                bool before = i == 0 || !char.IsLetter(t[i - 1]);
                int e = i + name.Length;
                bool after = e < t.Length && !char.IsLetter(t[e]);
                bool digitAfter = false;
                for (int k = e; k < t.Length; k++) if (char.IsDigit(t[k])) { digitAfter = true; break; }
                if (before && after && digitAfter) { if (!found.Contains(s.ToUpperInvariant())) found.Add(s.ToUpperInvariant()); break; }
            }
        }
        if (found.Count == 1) return new StatParse(found[0], t, "ok");
        if (found.Count == 0) return new StatParse(null, t, "no configured stat name found");
        return new StatParse(null, t, "ambiguous: " + string.Join("/", found));
    }

    /// <summary>Smallest edit distance between needle and any substring of hay (Sellers algorithm).</summary>
    public static int ApproxDistance(string hay, string needle)
    {
        int m = needle.Length;
        var prev = new int[m + 1];
        var cur = new int[m + 1];
        for (int j = 0; j <= m; j++) prev[j] = j;
        int best = m;
        foreach (char c in hay)
        {
            cur[0] = 0;
            for (int j = 1; j <= m; j++)
            {
                int cost = needle[j - 1] == c ? 0 : 1;
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
            }
            best = Math.Min(best, cur[m]);
            (prev, cur) = (cur, prev);
        }
        return best;
    }

    /// <summary>True only for "The current stat is Orange quality. Do you want to reroll?" (tolerates small OCR errors).</summary>
    public static bool IsOrangeWarning(string ocr)
    {
        string l = LettersLower(ocr);
        if (l.Length < 20) return false;
        return ApproxDistance(l, "currentstatisorangequality") <= 3
            && ApproxDistance(l, "doyouwanttoreroll") <= 3
            && ApproxDistance(l, "orange") <= 1;
    }

    public static bool IsRerollLabel(string ocr)
    {
        string l = LettersLower(ocr);
        return l.Length >= 4 && ApproxDistance(l, "reroll") <= 1;
    }

    public static bool IsSaveLabel(string ocr)
    {
        string l = LettersLower(ocr);
        return l.Length >= 3 && l.Length <= 8 && ApproxDistance(l, "save") <= 1;
    }
}
