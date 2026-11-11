using System.Runtime.InteropServices;
using MidScrollAnywhere.Core.Native;

namespace MidScrollAnywhere.Core.Scroll;

/// <summary>滚轮注入的目标：决定这一次 delta 是"模拟真实滚轮"还是"直接投递给锚点窗口"。</summary>
public interface IWheelTarget
{
    bool ContainsCursor(POINT pt);

    /// <summary>返回 false 表示目标已不可用（窗口销毁 / UIPI 拒绝），引擎应停止。</summary>
    bool Wheel(int deltaX, int deltaY, POINT cursor);
}

public static class InputInjector
{
    /// <summary>真实用户级滚轮注入。正 deltaY = 向上，正 deltaX = 向右。</summary>
    public static bool Wheel(int deltaX, int deltaY)
    {
        int count = (deltaY != 0 ? 1 : 0) + (deltaX != 0 ? 1 : 0);
        if (count == 0)
        {
            return true;
        }

        var inputs = new INPUT[count];
        int i = 0;
        if (deltaY != 0)
        {
            inputs[i] = MakeMouseInput(Win32.MOUSEEVENTF_WHEEL, deltaY);
            i++;
        }

        if (deltaX != 0)
        {
            inputs[i] = MakeMouseInput(Win32.MOUSEEVENTF_HWHEEL, deltaX);
        }

        uint sent = Win32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return sent == inputs.Length;
    }

    private static INPUT MakeMouseInput(uint flags, int data) => new()
    {
        Type = Win32.INPUT_MOUSE,
        U = new InputUnion
        {
            Mi = new MOUSEINPUT
            {
                Dx = 0,
                Dy = 0,
                MouseData = data,
                DwFlags = flags,
                Time = 0,
                DwExtraInfo = IntPtr.Zero,
            },
        },
    };
}

/// <summary>混合目标：光标还在锚点窗口内就走真实滚轮，漂出去就锁定锚点窗口句柄投递。</summary>
public sealed class HybridWheelTarget : IWheelTarget
{
    private readonly IntPtr _hwnd;
    private readonly RECT _windowRect;

    public HybridWheelTarget(IntPtr hwnd, RECT windowRect)
    {
        _hwnd = hwnd;
        _windowRect = windowRect;
    }

    public bool ContainsCursor(POINT pt) => _windowRect.Contains(pt);

    public bool Wheel(int deltaX, int deltaY, POINT cursor)
    {
        if (ContainsCursor(cursor))
        {
            return InputInjector.Wheel(deltaX, deltaY);
        }

        return PostWheel(deltaX, deltaY, cursor);
    }

    internal bool PostWheel(int deltaX, int deltaY, POINT cursor)
    {
        if (!Win32.IsWindow(_hwnd))
        {
            return false;
        }

        bool ok = true;
        if (deltaY != 0)
        {
            ok &= Win32.PostMessageW(_hwnd, Win32.WM_MOUSEWHEEL, PackWheelDelta(deltaY), Win32.MakeLParam(cursor.X, cursor.Y));
        }

        if (deltaX != 0)
        {
            ok &= Win32.PostMessageW(_hwnd, Win32.WM_MOUSEHWHEEL, PackWheelDelta(deltaX), Win32.MakeLParam(cursor.X, cursor.Y));
        }

        return ok;
    }

    private static IntPtr PackWheelDelta(int delta) => (IntPtr)((delta & 0xFFFF) << 16);
}

/// <summary>永远走真实滚轮（光标在谁身上就滚谁）。</summary>
public sealed class CursorWheelTarget : IWheelTarget
{
    public bool ContainsCursor(POINT pt) => true;

    public bool Wheel(int deltaX, int deltaY, POINT cursor) => InputInjector.Wheel(deltaX, deltaY);
}

/// <summary>永远投递给锚点窗口（窗口销毁时停止）。</summary>
public sealed class AnchorWindowWheelTarget : IWheelTarget
{
    private readonly HybridWheelTarget _inner;

    public AnchorWindowWheelTarget(IntPtr hwnd, RECT windowRect) => _inner = new HybridWheelTarget(hwnd, windowRect);

    public bool ContainsCursor(POINT pt) => false;

    public bool Wheel(int deltaX, int deltaY, POINT cursor) => _inner.PostWheel(deltaX, deltaY, cursor);
}

public static class WheelTargetFactory
{
    public const string Hybrid = "hybrid";
    public const string CursorWindow = "cursorWindow";
    public const string AnchorWindow = "anchorWindow";

    public static readonly string[] Ids = [Hybrid, CursorWindow, AnchorWindow];

    public static IWheelTarget Create(string? mode, IntPtr hwnd, RECT windowRect) => mode switch
    {
        CursorWindow => new CursorWheelTarget(),
        AnchorWindow => new AnchorWindowWheelTarget(hwnd, windowRect),
        _ => new HybridWheelTarget(hwnd, windowRect),
    };
}
