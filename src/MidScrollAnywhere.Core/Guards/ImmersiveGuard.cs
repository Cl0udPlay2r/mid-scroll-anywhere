using System.Diagnostics;
using MidScrollAnywhere.Core.Native;

namespace MidScrollAnywhere.Core.Guards;

public static class ProcessClassifier
{
    /// <summary>取窗口所属进程的 exe 文件名（小写，例如 "notepad.exe"）。</summary>
    public static string? GetProcessName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0)
        {
            return null;
        }

        IntPtr handle = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new System.Text.StringBuilder(1024);
            int size = buffer.Capacity;
            if (!Win32.QueryFullProcessImageNameW(handle, 0, buffer, ref size))
            {
                return null;
            }

            string full = buffer.ToString(0, size);
            return Path.GetFileName(full).ToLowerInvariant();
        }
        finally
        {
            Win32.CloseHandle(handle);
        }
    }

    public static string? GetForegroundProcessName() => GetProcessName(Win32.GetForegroundWindow());

    public static string? GetFullPath(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        IntPtr handle = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new System.Text.StringBuilder(1024);
            int size = buffer.Capacity;
            return Win32.QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
        }
        finally
        {
            Win32.CloseHandle(handle);
        }
    }
}

public interface IGuardProbes
{
    /// <summary>SHQueryUserNotificationState 的返回值（拿不到就返回 QUNS_ACCEPTS_NOTIFICATIONS）。</summary>
    int QueryNotificationState();

    bool IsCursorVisible();

    bool IsCursorConfined();

    bool IsForegroundBorderlessFullscreen(out IntPtr foreground);

    IntPtr GetForegroundWindow();

    string? GetForegroundProcessName();

    void Trace(string message);
}

public readonly record struct GuardVerdict(bool Blocked, string? Reason, string? LearnProcess)
{
    public static readonly GuardVerdict Allow = new(false, null, null);
}

/// <summary>
/// 沉浸式场景守卫：命中任何一条就"完全不介入"（不换光标、不启动滚动、不注入）。
/// 顺序 = 计划 §8 的否决链。
/// </summary>
public sealed class ImmersiveGuard
{
    private readonly IGuardProbes _probes;
    private readonly Config.GuardConfig _config;

    public ImmersiveGuard(IGuardProbes probes, Config.GuardConfig config)
    {
        _probes = probes;
        _config = config;
    }

    public GuardVerdict Check()
    {
        if (_config.FullscreenApi)
        {
            int state = _probes.QueryNotificationState();
            switch (state)
            {
                case Win32.QUNS_RUNNING_D3D_FULL_SCREEN:
                    _probes.Trace("guard: D3D 全屏");
                    return new GuardVerdict(true, "d3d-fullscreen", _probes.GetForegroundProcessName());
                case Win32.QUNS_APP:
                    _probes.Trace("guard: 全屏应用 (QUNS_APP)");
                    return new GuardVerdict(true, "fullscreen-app", _probes.GetForegroundProcessName());
                case Win32.QUNS_PRESENTATION_MODE:
                    _probes.Trace("guard: 演示模式");
                    return new GuardVerdict(true, "presentation", null);
                case Win32.QUNS_BUSY:
                    _probes.Trace("guard: 系统忙/锁屏");
                    return new GuardVerdict(true, "busy", null);
            }
        }

        if (_config.CursorHidden && !_probes.IsCursorVisible())
        {
            _probes.Trace("guard: 光标被隐藏");
            return new GuardVerdict(true, "cursor-hidden", _probes.GetForegroundProcessName());
        }

        if (_config.CursorConfined && _probes.IsCursorConfined())
        {
            _probes.Trace("guard: 光标被锁定");
            return new GuardVerdict(true, "cursor-confined", _probes.GetForegroundProcessName());
        }

        if (_config.BorderlessFullscreen && _probes.IsForegroundBorderlessFullscreen(out IntPtr foreground))
        {
            _probes.Trace("guard: 无边框全屏");
            return new GuardVerdict(true, "borderless-fullscreen", ProcessClassifier.GetProcessName(foreground));
        }

        return GuardVerdict.Allow;
    }
}

public sealed class SystemGuardProbes : IGuardProbes
{
    private readonly Action<string>? _trace;

    public SystemGuardProbes(Action<string>? trace = null) => _trace = trace;

    public void Trace(string message) => _trace?.Invoke(message);

    public int QueryNotificationState()
    {
        try
        {
            return Win32.SHQueryUserNotificationState(out int state) == 0 ? state : Win32.QUNS_ACCEPTS_NOTIFICATIONS;
        }
        catch
        {
            return Win32.QUNS_ACCEPTS_NOTIFICATIONS;
        }
    }

    public bool IsCursorVisible()
    {
        var info = new CURSORINFO { CbSize = System.Runtime.InteropServices.Marshal.SizeOf<CURSORINFO>() };
        return !Win32.GetCursorInfo(ref info) || (info.Flags & Win32.CURSOR_SHOWING) != 0;
    }

    public bool IsCursorConfined()
    {
        if (!Win32.GetClipCursor(out RECT clip))
        {
            return false;
        }

        int vx = Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN);
        int vy = Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN);
        int vw = Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN);
        int vh = Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN);

        bool isWholeDesktop =
            clip.Left <= vx && clip.Top <= vy &&
            clip.Right >= vx + vw && clip.Bottom >= vy + vh;

        return !isWholeDesktop;
    }

    public bool IsForegroundBorderlessFullscreen(out IntPtr foreground)
    {
        foreground = Win32.GetForegroundWindow();
        if (foreground == IntPtr.Zero || !Win32.IsWindowVisible(foreground))
        {
            return false;
        }

        long style = (long)Win32.GetWindowLongPtrW(foreground, Win32.GWL_STYLE);
        bool isPopup = (style & Win32.WS_POPUP) != 0;
        bool hasCaption = (style & Win32.WS_CAPTION) != 0;
        if (!isPopup || hasCaption)
        {
            return false;
        }

        if (!Win32.GetWindowRect(foreground, out RECT windowRect))
        {
            return false;
        }

        IntPtr monitor = Win32.MonitorFromWindow(foreground, Win32.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { CbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!Win32.GetMonitorInfoW(monitor, ref info))
        {
            return false;
        }

        return windowRect.Left <= info.RcMonitor.Left
            && windowRect.Top <= info.RcMonitor.Top
            && windowRect.Right >= info.RcMonitor.Right
            && windowRect.Bottom >= info.RcMonitor.Bottom;
    }

    public IntPtr GetForegroundWindow() => Win32.GetForegroundWindow();

    public string? GetForegroundProcessName() => ProcessClassifier.GetForegroundProcessName();
}

public static class IgnoreList
{
    public static bool IsIgnored(Config.AppConfig config, string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe))
        {
            return false;
        }

        if (config.Ignore.Processes.Any(p => string.Equals(p, exe, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return config.Guards.LearnedExe
            && config.Ignore.Learned.Any(p => string.Equals(p, exe, StringComparison.OrdinalIgnoreCase));
    }

    public static bool Learn(Config.AppConfig config, string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe))
        {
            return false;
        }

        if (config.Ignore.Learned.Any(p => string.Equals(p, exe, StringComparison.OrdinalIgnoreCase))
            || config.Ignore.Processes.Any(p => string.Equals(p, exe, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        config.Ignore.Learned.Add(exe);
        return true;
    }

    public static bool AddProcess(Config.AppConfig config, string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe))
        {
            return false;
        }

        if (config.Ignore.Processes.Any(p => string.Equals(p, exe, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        config.Ignore.Processes.Add(exe);
        return true;
    }

    public static bool RemoveProcess(Config.AppConfig config, string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe))
        {
            return false;
        }

        int removed = config.Ignore.Processes.RemoveAll(p => string.Equals(p, exe, StringComparison.OrdinalIgnoreCase));
        return removed > 0;
    }
}
