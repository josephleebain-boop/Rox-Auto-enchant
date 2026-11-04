using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RoxReroller;

internal enum GrayMode { MaxChannel, Luma }

internal static class Imaging
{
    public static Bitmap Capture(Area a)
    {
        var bmp = new Bitmap(a.Width, a.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(a.X, a.Y, 0, 0, new Size(a.Width, a.Height), CopyPixelOperation.SourceCopy);
        return bmp;
    }

    public static Bitmap CaptureScreen()
    {
        var vs = SystemInformation.VirtualScreen;
        var bmp = new Bitmap(vs.Width, vs.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(vs.Left, vs.Top, 0, 0, vs.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    public static byte[] GetBgra(Bitmap b, out int stride)
    {
        var bd = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            stride = bd.Stride;
            var arr = new byte[Math.Abs(bd.Stride) * b.Height];
            Marshal.Copy(bd.Scan0, arr, 0, arr.Length);
            return arr;
        }
        finally { b.UnlockBits(bd); }
    }

    /// <summary>Upscale, convert to grey, stretch contrast, make text dark-on-light, add white margin (what OCR engines like best).</summary>
    public static Bitmap PrepareForOcr(Bitmap src, int scale, GrayMode mode, bool? forceInvert = null)
    {
        scale = Math.Clamp(scale, 1, 8);
        int W = src.Width * scale, H = src.Height * scale;
        using var up = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(up))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(src, new Rectangle(0, 0, W, H), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel);
        }
        var px = GetBgra(up, out int stride);
        var gray = new byte[W * H];
        var hist = new int[256];
        double sum = 0;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int o = y * stride + x * 4;
                int b = px[o], gr = px[o + 1], r = px[o + 2];
                int v = mode == GrayMode.MaxChannel ? Math.Max(r, Math.Max(gr, b)) : (int)(0.299 * r + 0.587 * gr + 0.114 * b);
                gray[y * W + x] = (byte)v;
                hist[v]++;
                sum += v;
            }
        int total = W * H;
        int lo = Percentile(hist, total, 0.02), hi = Percentile(hist, total, 0.98);
        if (hi - lo < 8) hi = Math.Min(255, lo + 8);
        bool invert = forceInvert ?? (sum / total < 128);

        const int pad = 24;
        int OW = W + 2 * pad, OH = H + 2 * pad;
        var outPx = new byte[OW * OH * 4];
        Array.Fill(outPx, (byte)255);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int v = (gray[y * W + x] - lo) * 255 / (hi - lo);
                v = Math.Clamp(v, 0, 255);
                if (invert) v = 255 - v;
                int o = ((y + pad) * OW + x + pad) * 4;
                outPx[o] = outPx[o + 1] = outPx[o + 2] = (byte)v;
                outPx[o + 3] = 255;
            }
        var result = new Bitmap(OW, OH, PixelFormat.Format32bppArgb);
        var bd = result.LockBits(new Rectangle(0, 0, OW, OH), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < OH; y++)
                Marshal.Copy(outPx, y * OW * 4, bd.Scan0 + y * bd.Stride, OW * 4);
        }
        finally { result.UnlockBits(bd); }
        return result;
    }

    private static int Percentile(int[] hist, int total, double p)
    {
        long target = (long)(total * p), cum = 0;
        for (int i = 0; i < 256; i++) { cum += hist[i]; if (cum >= target) return i; }
        return 255;
    }
}
