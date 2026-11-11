using System.Runtime.InteropServices;
using System.Text;

namespace MidScrollAnywhere.Core.Native;

[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    public int X;
    public int Y;

    public POINT(int x, int y)
    {
        X = x;
        Y = y;
    }

    public override readonly string ToString() => $"({X},{Y})";
}

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;

    public readonly bool Contains(POINT p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;

    public override readonly string ToString() => $"[{Left},{Top},{Right},{Bottom}]";
}

[StructLayout(LayoutKind.Sequential)]
public struct SIZE
{
    public int Cx;
    public int Cy;

    public SIZE(int cx, int cy)
    {
        Cx = cx;
        Cy = cy;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct MSG
{
    public IntPtr Hwnd;
    public uint Message;
    public IntPtr WParam;
    public IntPtr LParam;
    public uint Time;
    public POINT Point;
}

[StructLayout(LayoutKind.Sequential)]
public struct MSLLHOOKSTRUCT
{
    public POINT Pt;
    public int MouseData;
    public int Flags;
    public int Time;
    public IntPtr DwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct KBDLLHOOKSTRUCT
{
    public int VkCode;
    public int ScanCode;
    public int Flags;
    public int Time;
    public IntPtr DwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct CURSORINFO
{
    public int CbSize;
    public int Flags;
    public IntPtr HCursor;
    public POINT PtScreenPos;
}

[StructLayout(LayoutKind.Sequential)]
public struct MOUSEINPUT
{
    public int Dx;
    public int Dy;
    public int MouseData;
    public uint DwFlags;
    public int Time;
    public IntPtr DwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct KEYBDINPUT
{
    public ushort WVk;
    public ushort WScan;
    public uint DwFlags;
    public int Time;
    public IntPtr DwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct HARDWAREINPUT
{
    public uint UMsg;
    public ushort WParamL;
    public ushort WParamH;
}

[StructLayout(LayoutKind.Explicit)]
public struct InputUnion
{
    [FieldOffset(0)] public MOUSEINPUT Mi;
    [FieldOffset(0)] public KEYBDINPUT Ki;
    [FieldOffset(0)] public HARDWAREINPUT Hi;
}

[StructLayout(LayoutKind.Sequential)]
public struct INPUT
{
    public uint Type;
    public InputUnion U;
}

[StructLayout(LayoutKind.Sequential)]
public struct MONITORINFO
{
    public int CbSize;
    public RECT RcMonitor;
    public RECT RcWork;
    public uint DwFlags;
}

[StructLayout(LayoutKind.Sequential)]
public struct BLENDFUNCTION
{
    public byte BlendOp;
    public byte BlendFlags;
    public byte SourceConstantAlpha;
    public byte AlphaFormat;
}

[StructLayout(LayoutKind.Sequential)]
public struct ICONINFO
{
    public int FIcon;
    public int XHotspot;
    public int YHotspot;
    public IntPtr HbmMask;
    public IntPtr HbmColor;
}

public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

public static class Win32
{
    // ---- hooks -----------------------------------------------------------------
    public const int WH_KEYBOARD_LL = 13;
    public const int WH_MOUSE_LL = 14;

    // ---- window messages -------------------------------------------------------
    public const int WM_SETCURSOR = 0x0020;
    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_MBUTTONDOWN = 0x0207;
    public const int WM_MBUTTONUP = 0x0208;
    public const int WM_MOUSEWHEEL = 0x020A;
    public const int WM_XBUTTONDOWN = 0x020B;
    public const int WM_XBUTTONUP = 0x020C;
    public const int WM_MOUSEHWHEEL = 0x020E;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;
    public const int WM_APP = 0x8000;
    public const int HTCLIENT = 1;

    // ---- SendInput -------------------------------------------------------------
    public const uint INPUT_MOUSE = 0;
    public const uint MOUSEEVENTF_WHEEL = 0x0800;
    public const uint MOUSEEVENTF_HWHEEL = 0x01000;

    // ---- window styles ---------------------------------------------------------
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_CAPTION = 0x00C00000;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TOPMOST = 0x00000008;

    // ---- cursors / system metrics ---------------------------------------------
    public const int CURSOR_SHOWING = 0x0001;
    public const int IDC_ARROW = 32512;
    public const int IDC_SIZEALL = 32646;
    public const int SM_CXCURSOR = 13;
    public const int SM_CYCURSOR = 14;
    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;

    public const uint SPI_GETWHEELSCROLLLINES = 0x0068;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int MDT_EFFECTIVE_DPI = 0;

    public const uint ULW_ALPHA = 0x00000002;
    public const byte AC_SRC_ALPHA = 0x01;

    // ---- shell notification state ---------------------------------------------
    public const int QUNS_NOT_PRESENT = 1;
    public const int QUNS_BUSY = 2;
    public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    public const int QUNS_PRESENTATION_MODE = 4;
    public const int QUNS_ACCEPTS_NOTIFICATIONS = 5;
    public const int QUNS_QUIET_TIME = 6;
    public const int QUNS_APP = 7;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern IntPtr SetCursor(IntPtr hCursor);

    [DllImport("user32.dll")]
    public static extern IntPtr GetCursor();

    [DllImport("user32.dll")]
    public static extern IntPtr LoadCursorW(IntPtr hInstance, IntPtr lpCursorName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetCursorInfo(ref CURSORINFO pci);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetClipCursor(out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    public const uint GA_ROOT = 2;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeoutW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    public const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfoW(uint uiAction, uint uiParam, out uint pvParam, uint fWinIni);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out int state);

    [DllImport("winmm.dll")]
    public static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll")]
    public static extern uint timeEndPeriod(uint uPeriod);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint nPlanes, uint nBitCount, IntPtr lpBits);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreateIconIndirect(ref ICONINFO piconinfo);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    public static readonly IntPtr HWND_TOPMOST = new(-1);

    public static POINT GetCursorPosition()
    {
        GetCursorPos(out var p);
        return p;
    }

    public static IntPtr MakeLParam(int lo, int hi) => (IntPtr)((hi << 16) | (lo & 0xFFFF));
}
