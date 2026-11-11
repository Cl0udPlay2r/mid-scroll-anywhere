using MidScrollAnywhere.Core.Native;

namespace MidScrollAnywhere.Core.Input;

/// <summary>
/// 全局低级鼠标/键盘钩子，跑在一条独立的带消息循环的线程上。
/// 回调里只做判决（快），绝不做重活。
/// </summary>
public sealed class LowLevelHooks : IDisposable
{
    private readonly LowLevelMouseProc _mouseProc;
    private readonly LowLevelKeyboardProc _keyboardProc;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private volatile bool _stopping;

    public LowLevelHooks()
    {
        // 委托必须保活，否则 GC 后回调会崩。
        _mouseProc = MouseCallback;
        _keyboardProc = KeyboardCallback;
    }

    /// <summary>鼠标事件（消息号，屏幕坐标，mouseData）。返回 true 表示吞掉。</summary>
    public Func<int, POINT, int, bool>? MouseFilter { get; set; }

    /// <summary>键盘按下事件。返回 true 表示吞掉。</summary>
    public Func<int, bool>? KeyboardFilter { get; set; }

    public Action<Exception>? OnError { get; set; }

    public bool IsInstalled => _mouseHook != IntPtr.Zero && _keyboardHook != IntPtr.Zero;

    public bool Start()
    {
        if (_thread is not null)
        {
            return true;
        }

        using var ready = new ManualResetEventSlim(false);
        _thread = new Thread(() => ThreadBody(ready))
        {
            IsBackground = true,
            Name = "msw-hook-thread",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait(2000);
        return IsInstalled;
    }

    private void ThreadBody(ManualResetEventSlim ready)
    {
        _threadId = Win32.GetCurrentThreadId();
        IntPtr module = Win32.GetModuleHandleW(null);

        _mouseHook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _mouseProc, module, 0);
        _keyboardHook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _keyboardProc, module, 0);
        ready.Set();

        if (_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero)
        {
            OnError?.Invoke(new InvalidOperationException(
                $"SetWindowsHookEx 失败 (mouse={_mouseHook}, keyboard={_keyboardHook})"));
        }

        while (!_stopping && Win32.GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }

        if (_mouseHook != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        if (_keyboardHook != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
    }

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int message = wParam.ToInt32();
            var info = System.Runtime.InteropServices.Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            try
            {
                if (MouseFilter?.Invoke(message, info.Pt, info.MouseData) == true)
                {
                    return 1;
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex);
            }
        }

        return Win32.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int message = wParam.ToInt32();
            var info = System.Runtime.InteropServices.Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            try
            {
                if (KeyboardFilter?.Invoke(info.VkCode) == true)
                {
                    // 只吞"按下"，up 让程序自己收尾，避免按键状态卡住。
                    if (message is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN)
                    {
                        return 1;
                    }
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex);
            }
        }

        return Win32.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        _stopping = true;
        if (_threadId != 0)
        {
            Win32.PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
        }

        _thread?.Join(1000);
        _thread = null;
    }
}
