using System.Drawing.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace RoxReroller;

/// <summary>Wraps the OCR engine that is built into Windows 10/11 (Windows.Media.Ocr). Nothing to install on most PCs.</summary>
internal sealed class OcrService
{
    private OcrEngine? _engine;
    public bool IsReady => _engine != null;
    public string Description { get; private set; } = "not initialised";

    public bool TryInit(out string error)
    {
        error = "";
        try
        {
            _engine = OcrEngine.TryCreateFromLanguage(new Language("en-US"));
            _engine ??= OcrEngine.TryCreateFromUserProfileLanguages();
            if (_engine == null)
            {
                foreach (var lang in OcrEngine.AvailableRecognizerLanguages)
                {
                    if (lang.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                    {
                        _engine = OcrEngine.TryCreateFromLanguage(lang);
                        if (_engine != null) break;
                    }
                }
            }
            if (_engine == null)
            {
                error = "Windows has no OCR language available.\n\nFix: Settings > Time & language > Language & region > Add a language > " +
                        "English (United States) (make sure 'Text recognition' / 'Basic typing' is ticked), then restart this app.";
                return false;
            }
            Description = "Windows OCR (" + _engine.RecognizerLanguage.DisplayName + ")";
            return true;
        }
        catch (Exception ex)
        {
            error = "Could not start Windows OCR: " + ex.Message;
            return false;
        }
    }

    public async Task<string> ReadAsync(Bitmap bmp)
    {
        if (_engine == null) throw new InvalidOperationException("OCR engine not initialised.");
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        using var ras = ms.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(ras);
        using var sb = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var res = await _engine.RecognizeAsync(sb);
        return res.Text.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}
