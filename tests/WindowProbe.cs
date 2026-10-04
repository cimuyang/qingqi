using System;
using System.Runtime.InteropServices;
using System.Text;

// Inspect only the test process supplied by verify.ps1. Shell icon workers can own
// auxiliary IME windows, so Process.MainWindowHandle is not a reliable selector.
class WindowProbe
{
    delegate bool Callback(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumWindows(Callback callback, IntPtr data);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    static int Main(string[] args)
    {
        if (args.Length != 2) return 2;
        uint pid; if (!UInt32.TryParse(args[1], out pid)) return 2;
        IntPtr target = IntPtr.Zero;
        EnumWindows(delegate(IntPtr window, IntPtr data)
        {
            uint owner; GetWindowThreadProcessId(window, out owner); if (owner != pid) return true;
            var text = new StringBuilder(256); GetWindowText(window, text, text.Capacity);
            if (text.ToString() == "轻启 · 一键启动") { target = window; return false; } return true;
        }, IntPtr.Zero);
        if (target == IntPtr.Zero) return 1;
        if (args[0] == "--close") return PostMessage(target, 0x0010, IntPtr.Zero, IntPtr.Zero) ? 0 : 1;
        if (args[0] == "--visible") return IsWindowVisible(target) ? 0 : 1;
        return 2;
    }
}
