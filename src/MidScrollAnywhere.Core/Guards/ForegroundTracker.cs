using System.Text;
using MidScrollAnywhere.Core.Native;

namespace MidScrollAnywhere.Core.Guards;

/// <summary>
/// 记住"用户最近一次真正在前台的外部程序"。
///
/// 为什么需要它：本工具自己的托盘菜单和设置窗口就是前台窗口，所以用户点
/// "把当前前台程序加入忽略列表"的那一瞬间，<see cref="ProcessClassifier.GetForegroundProcessName"/>
/// 拿到的是 midscrollanywhere.exe —— 忽略列表永远只加到自己。这里用低频轮询记下最后一个
/// "不是我们、也不是任务栏/桌面这类外壳窗口"的前台程序，点按钮时用它。
/// </summary>
public static class ForegroundTracker
{
    private const int PollIntervalMs = 400;

    /// <summary>任务栏、桌面、Alt-Tab、开始菜单等外壳窗口的类名：它们不算"用户正在用的程序"。</summary>
    private static readonly HashSet<string> ShellWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland",
        "TaskListThumbnailWnd",
        "TaskSwitcherWnd",
        "MultitaskingViewFrame",
        "ForegroundStaging",
        "XamlExplorerHostIslandWindow",
        "Windows.UI.Core.CoreWindow",
        "Progman",
        "WorkerW",
    };

    private static readonly object Gate = new();
    private static System.Threading.Timer? _timer;
    private static string? _lastProcessName;

    /// <summary>最近一次外部前台程序的 exe 名（小写）。还没记录到时为 null。</summary>
    public static string? LastProcessName
    {
        get
        {
            lock (Gate)
            {
                return _lastProcessName;
            }
        }
    }

    public static void Start()
    {
        lock (Gate)
        {
            _timer ??= new System.Threading.Timer(_ => Poll(), null, 0, PollIntervalMs);
        }
    }

    public static void Stop()
    {
        lock (Gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    /// <summary>采样一次前台窗口；本次采样不合适就保留之前记录的值。</summary>
    public static void Poll()
    {
        try
        {
            IntPtr window = Win32.GetForegroundWindow();
            if (window == IntPtr.Zero)
            {
                return;
            }

            Win32.GetWindowThreadProcessId(window, out uint processId);
            if (!ShouldTrack(processId, GetWindowClass(window), Environment.ProcessId))
            {
                return;
            }

            string? exe = ProcessClassifier.GetProcessName(window);
            if (string.IsNullOrEmpty(exe))
            {
                return;
            }

            lock (Gate)
            {
                _lastProcessName = exe;
            }
        }
        catch
        {
            // 采样失败就保留上一次记录，不影响主流程。
        }
    }

    /// <summary>
    /// 点"加入忽略列表"时该针对哪个程序：优先用记录下来的最近一次外部前台程序；
    /// 还没有记录（比如跟踪还没启动）时才看当前前台，并且同样过滤外壳窗口和自己。
    /// </summary>
    public static string? ResolveTarget()
    {
        if (LastProcessName is { Length: > 0 } last)
        {
            return last;
        }

        IntPtr window = Win32.GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return null;
        }

        Win32.GetWindowThreadProcessId(window, out uint processId);
        if (!ShouldTrack(processId, GetWindowClass(window), Environment.ProcessId))
        {
            return null;
        }

        string? exe = ProcessClassifier.GetProcessName(window);
        return string.Equals(exe, ProcessClassifier.SelfProcessName, StringComparison.OrdinalIgnoreCase) ? null : exe;
    }

    /// <summary>这个前台窗口值不值得记成"用户正在用的程序"（纯判断，便于单元测试）。</summary>
    public static bool ShouldTrack(uint windowProcessId, string? windowClass, int ownProcessId)
    {
        if (windowProcessId == 0 || windowProcessId == (uint)ownProcessId)
        {
            return false;
        }

        return string.IsNullOrEmpty(windowClass) || !ShellWindowClasses.Contains(windowClass);
    }

    private static string? GetWindowClass(IntPtr window)
    {
        var buffer = new StringBuilder(256);
        return Win32.GetClassNameW(window, buffer, buffer.Capacity) > 0 ? buffer.ToString() : null;
    }
}
