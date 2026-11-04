using RoxReroller;

int fails = 0, passes = 0;
void Check(string name, bool ok) { if (ok) passes++; else { fails++; Console.WriteLine("FAIL: " + name); } }

var stats = new AppSettings().Stats;

// ---- stat parsing
string? P(string s) => TextLogic.ParseStat(s, stats).Stat;
Check("VIT plain", P("Lv.2VIT +3") == "VIT");
Check("DEX decimal", P("Lv.2DEX +1.9") == "DEX");
Check("LUK", P("Lv.1LUK +1.3") == "LUK");
Check("AGI", P("Lv.1AGI +1") == "AGI");
Check("spaces", P("Lv. 2 VIT + 3") == "VIT");
Check("OCR 1 for I", P("Lv.2V1T +3") == "VIT");
Check("OCR l for I", P("Lv.2VlT +3") == "VIT");
Check("INT", P("Lv.2INT +3") == "INT");
Check("STR", P("Lv.1STR +2") == "STR");
Check("garbage -> null", P("@@ ~~") == null);
Check("empty -> null", P("") == null);
Check("two stats -> null (ambiguous)", P("Lv.1STR 2 Lv.1VIT 3") == null);
Check("no value digit -> null", P("Lv.VIT") == null);
Check("stat inside word -> null", P("POINT 5") == null);

// custom stat added later
var more = new List<string>(stats) { "P.ATK" };
Check("custom stat", TextLogic.ParseStat("Lv.1P.ATK +61.0", more).Stat == "P.ATK");

// ---- warning detection
Check("exact warning", TextLogic.IsOrangeWarning("The current stat is Orange quality. Do you want to reroll?"));
Check("warning w/ OCR noise", TextLogic.IsOrangeWarning("The current stat is 0range quality. Do you want to rerol1?"));
Check("purple warning rejected", !TextLogic.IsOrangeWarning("The current stat is Purple quality. Do you want to reroll?"));
Check("scenery text rejected", !TextLogic.IsOrangeWarning("Off-hand Lv.70"));
Check("empty rejected", !TextLogic.IsOrangeWarning(""));
Check("other popup rejected", !TextLogic.IsOrangeWarning("Insufficient materials. Please obtain more stones."));

// ---- labels
Check("reroll label", TextLogic.IsRerollLabel("Reroll"));
Check("reroll label noisy", TextLogic.IsRerollLabel("Rerol1 1000"));
Check("reroll label wrong", !TextLogic.IsRerollLabel("Cancel"));
Check("save label", TextLogic.IsSaveLabel("Save"));
Check("save label noisy", TextLogic.IsSaveLabel("Sav"));
Check("save label wrong", !TextLogic.IsSaveLabel("Confirm"));

// ---- colour classification on synthetic 47x43 icons
byte[] Icon(int w, int h, Func<int, int, (byte r, byte g, byte b)> f, out int stride)
{
    stride = w * 4; var a = new byte[stride * h];
    for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
    { var (r, g, b) = f(x, y); int o = y * stride + x * 4; a[o] = b; a[o + 1] = g; a[o + 2] = r; a[o + 3] = 255; }
    return a;
}
bool InCircle(int x, int y) { double dx = x - 23, dy = y - 21; return dx * dx + dy * dy < 18 * 18; }
var cfg = new AppSettings();
(byte, byte, byte) Bg() => (20, 20, 40);

var orange = Icon(47, 43, (x, y) => !InCircle(x, y) ? Bg() : (x + y) % 5 == 0 ? ((byte)255, (byte)255, (byte)255) : ((byte)240, (byte)150, (byte)40), out int s1);
var q = ColorClassifier.Classify(orange, 47, 43, s1, cfg);
Check("orange icon -> Orange (" + q.Detail + ")", q.Kind == QualityKind.Orange);

var gold = Icon(47, 43, (x, y) => !InCircle(x, y) ? Bg() : ((byte)235, (byte)185, (byte)50), out int s1b);
Check("gold icon -> Orange", ColorClassifier.Classify(gold, 47, 43, s1b, cfg).Kind == QualityKind.Orange);

var blue = Icon(47, 43, (x, y) => !InCircle(x, y) ? Bg() : (x + y) % 5 == 0 ? ((byte)255, (byte)255, (byte)255) : ((byte)70, (byte)130, (byte)230), out int s2);
Check("blue icon -> Other", ColorClassifier.Classify(blue, 47, 43, s2, cfg).Kind == QualityKind.Other);

var purple = Icon(47, 43, (x, y) => !InCircle(x, y) ? Bg() : ((byte)160, (byte)80, (byte)220), out int s2b);
Check("purple icon -> Other", ColorClassifier.Classify(purple, 47, 43, s2b, cfg).Kind == QualityKind.Other);

var grey = Icon(47, 43, (x, y) => !InCircle(x, y) ? Bg() : ((byte)210, (byte)210, (byte)215), out int s3);
Check("grey icon -> Other", ColorClassifier.Classify(grey, 47, 43, s3, cfg).Kind == QualityKind.Other);

var empty = Icon(47, 43, (x, y) => ((byte)22, (byte)22, (byte)45), out int s4);
Check("empty panel -> Other", ColorClassifier.Classify(empty, 47, 43, s4, cfg).Kind == QualityKind.Other);

var mixed = Icon(47, 43, (x, y) => !InCircle(x, y) ? Bg() : x < 23 ? ((byte)240, (byte)150, (byte)40) : ((byte)70, (byte)130, (byte)230), out int s5);
Check("half orange/half blue -> Uncertain", ColorClassifier.Classify(mixed, 47, 43, s5, cfg).Kind == QualityKind.Uncertain);

var green = Icon(104, 43, (x, y) => ((byte)150, (byte)225, (byte)40), out int s6);
Check("green save button", ColorClassifier.GreenFraction(green, 104, 43, s6) > 0.9);
Check("dark panel is not green", ColorClassifier.GreenFraction(empty, 47, 43, s4) < 0.01);

// ---- settings round trip
var c2 = cfg.Clone(); c2.Stats.Add("matk"); c2.Normalize();
Check("clone keeps positions", c2.RerollButton.X == 1602 && c2.WarningText.Width == 570);
Check("normalize uppercases/dedupes", c2.Stats.Contains("MATK") && c2.Stats.Count == 7);

Console.WriteLine($"{passes} passed, {fails} failed");
return fails == 0 ? 0 : 1;
