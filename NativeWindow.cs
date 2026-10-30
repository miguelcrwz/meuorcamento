using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MeuOrcamento;

/// <summary>Tweaks only Windows (DWM) can make: dark frame, corners and border color.</summary>
static class NativeWindow
{
    const int DarkMode = 20, CornerPreference = 33, BorderColor = 34, RoundCorners = 2;
    const int Border = 0x00242222; // #222224; COLORREF is 0x00BBGGRR

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    const int Restore = 9;

    /// <summary>Shows the Meu Orçamento window that is already open (restoring it if minimized).</summary>
    public static void BringOtherInstanceToFront()
    {
        var current = System.Diagnostics.Process.GetCurrentProcess();
        foreach (var other in System.Diagnostics.Process.GetProcessesByName(current.ProcessName))
        {
            if (other.Id == current.Id || other.MainWindowHandle == IntPtr.Zero) continue;
            if (IsIconic(other.MainWindowHandle)) ShowWindow(other.MainWindowHandle, Restore);
            SetForegroundWindow(other.MainWindowHandle);
            return;
        }
    }

    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        int mode = 1, corner = RoundCorners, border = Border;
        DwmSetWindowAttribute(handle, DarkMode, ref mode, sizeof(int));
        DwmSetWindowAttribute(handle, CornerPreference, ref corner, sizeof(int)); // Windows 11; ignored on 10
        DwmSetWindowAttribute(handle, BorderColor, ref border, sizeof(int));
    }
}
