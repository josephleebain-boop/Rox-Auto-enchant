using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace RoxReroller;

/// <summary>Plain Win32 calls: synthetic mouse clicks, key polling, window lookup. Nothing touches the game process.</summary>
internal static class Native
{
    public const int VK_F8 = 0x77;
    public const int VK_F9 = 0x78;

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    public static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    public static Point GetCursor() { GetCursorPos(out var p); return new Point(p.X, p.Y); }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public INPUTUNION u; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint n, INPUT[] inputs, int cbSize);

    private const uint MOVE = 0x0001, LEFTDOWN = 0x0002, LEFTUP = 0x0004, VIRTUALDESK = 0x4000, ABSOLUTE = 0x8000;

    private static void SendMouse(uint flags, int dx = 0, int dy = 0)
    {
        var inp = new INPUT { type = 0, u = new INPUTUNION { mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags } } };
        if (SendInput(1, new[] { inp }, Marshal.SizeOf<INPUT>()) != 1)
            throw new InvalidOperationException("Windows rejected the mouse input. If the game runs as Administrator, run this app as Administrator too.");
    }

    public static void MoveTo(int x, int y)
    {
        var vs = SystemInformation.VirtualScreen;
        int ax = (int)Math.Round((x - vs.Left) * 65535.0 / Math.Max(1, vs.Width - 1));
        int ay = (int)Math.Round((y - vs.Top) * 65535.0 / Math.Max(1, vs.Height - 1));
        SendMouse(MOVE | ABSOLUTE | VIRTUALDESK, ax, ay);
    }

    public static async Task ClickAsync(int x, int y, int moveDelayMs, int holdMs, CancellationToken ct)
    {
        MoveTo(x, y);
        await Task.Delay(moveDelayMs, ct);
        var cur = GetCursor();
        if (Math.Abs(cur.X - x) > 4 || Math.Abs(cur.Y - y) > 4)
        {
            MoveTo(x, y);
            await Task.Delay(moveDelayMs, ct);
            cur = GetCursor();
            if (Math.Abs(cur.X - x) > 6 || Math.Abs(cur.Y - y) > 6)
                throw new InvalidOperationException($"Mouse did not reach ({x},{y}); it is at ({cur.X},{cur.Y}).");
        }
        ct.ThrowIfCancellationRequested();
        SendMouse(LEFTDOWN);
        try { await Task.Delay(holdMs); }          // deliberately not cancellable: never leave the button held down
        finally { SendMouse(LEFTUP); }
    }

    // ---- windows ----
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsHungAppWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();

    public static IntPtr FindWindowByTitle(string title)
    {
        IntPtr exact = IntPtr.Zero, prefix = IntPtr.Zero;
        uint me = (uint)Environment.ProcessId;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            int len = GetWindowTextLength(h);
            if (len == 0) return true;
            var sb = new StringBuilder(len + 1);
            GetWindowText(h, sb, sb.Capacity);
            string t = sb.ToString();
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == me) return true;
            if (t.Equals(title, StringComparison.OrdinalIgnoreCase)) { exact = h; return false; }
            if (prefix == IntPtr.Zero && t.StartsWith(title, StringComparison.OrdinalIgnoreCase)
                && !t.Contains("Chrome") && !t.Contains("Edge") && !t.Contains("Firefox"))
                prefix = h;
            return true;
        }, IntPtr.Zero);
        return exact != IntPtr.Zero ? exact : prefix;
    }

    public static Rectangle GetWindowBounds(IntPtr h)
    {
        GetWindowRect(h, out var r);
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    public static void BringToFront(IntPtr h)
    {
        if (IsIconic(h)) ShowWindow(h, 9 /*SW_RESTORE*/);
        SetForegroundWindow(h);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO { public uint cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }
    [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FLASHWINFO f);
    public static void Flash(IntPtr h)
    {
        var f = new FLASHWINFO { cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(), hwnd = h, dwFlags = 3 | 12, uCount = 5, dwTimeout = 0 };
        FlashWindowEx(ref f);
    }
}
