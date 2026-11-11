using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using MidScrollAnywhere.Core.Native;

namespace MidScrollAnywhere.Core.Anchors;

public enum PanDirection
{
    None,
    Up,
    Right,
    Down,
    Left,
}

/// <summary>锚点美术：一圈四箭头的实心图标（Chromium 的光标与 Firefox 的面板共用同一套绘制）。</summary>
public static class AnchorArt
{
    public static Bitmap CreatePanIcon(int size, PanDirection highlight = PanDirection.None)
    {
        size = Math.Max(16, size);
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        float cx = size / 2f;
        float cy = size / 2f;
        float radius = size * 0.37f;

        using (var shadow = new SolidBrush(Color.FromArgb(45, 0, 0, 0)))
        {
            g.FillEllipse(shadow, cx - radius - size * 0.03f, cy - radius + size * 0.04f, radius * 2 + size * 0.04f, radius * 2);
        }

        using (var fill = new SolidBrush(Color.FromArgb(235, 252, 252, 252)))
        {
            g.FillEllipse(fill, cx - radius, cy - radius, radius * 2, radius * 2);
        }

        using (var ring = new Pen(Color.FromArgb(210, 70, 70, 70), Math.Max(1f, size / 22f)))
        {
            g.DrawEllipse(ring, cx - radius, cy - radius, radius * 2, radius * 2);
        }

        float tip = radius * 0.88f;
        float baseOffset = radius * 0.32f;
        float halfWidth = radius * 0.30f;

        DrawArrow(g, cx, cy, 0, -1, tip, baseOffset, halfWidth, highlight == PanDirection.Up);
        DrawArrow(g, cx, cy, 0, 1, tip, baseOffset, halfWidth, highlight == PanDirection.Down);
        DrawArrow(g, cx, cy, -1, 0, tip, baseOffset, halfWidth, highlight == PanDirection.Left);
        DrawArrow(g, cx, cy, 1, 0, tip, baseOffset, halfWidth, highlight == PanDirection.Right);

        return bitmap;
    }

    public static PanDirection DirectionOf(int dx, int dy, int deadZone)
    {
        if (Math.Abs(dx) <= deadZone && Math.Abs(dy) <= deadZone)
        {
            return PanDirection.None;
        }

        return Math.Abs(dy) >= Math.Abs(dx)
            ? (dy < 0 ? PanDirection.Up : PanDirection.Down)
            : (dx < 0 ? PanDirection.Left : PanDirection.Right);
    }

    private static void DrawArrow(Graphics g, float cx, float cy, float dx, float dy, float tip, float baseOffset, float halfWidth, bool highlighted)
    {
        float px = -dy;
        float py = dx;
        var tipPoint = new PointF(cx + (dx * tip), cy + (dy * tip));
        var left = new PointF(cx + (dx * baseOffset) + (px * halfWidth), cy + (dy * baseOffset) + (py * halfWidth));
        var right = new PointF(cx + (dx * baseOffset) - (px * halfWidth), cy + (dy * baseOffset) - (py * halfWidth));

        Color color = highlighted ? Color.FromArgb(255, 45, 45, 45) : Color.FromArgb(255, 135, 135, 135);
        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, [tipPoint, left, right]);
    }
}

public interface IAnchorRenderer : IDisposable
{
    void Show(POINT anchorPoint, double dpiScale);

    void Update(POINT anchorPoint, POINT cursor, double dpiScale);

    void Hide();
}

/// <summary>
/// Chromium 档：把指针换成"平移光标"（Chrome 就是这么做的，不是画浮层）。
/// 目标程序收到 WM_SETCURSOR 会抢回光标，所以每个节拍重设一次；被抢的比例用于自动降级。
/// </summary>
public sealed class CursorAnchorRenderer : IAnchorRenderer
{
    private const int CursorSizeFallback = 32;

    private readonly IntPtr[] _cursors = new IntPtr[5];
    private int _mismatch;
    private int _samples;
    private PanDirection _current;
    private bool _visible;

    public CursorAnchorRenderer()
    {
        int size = Win32.GetSystemMetrics(Win32.SM_CXCURSOR);
        if (size < 16)
        {
            size = CursorSizeFallback;
        }

        _cursors[(int)PanDirection.None] = CreateCursor(AnchorArt.CreatePanIcon(size, PanDirection.None));
        _cursors[(int)PanDirection.Up] = CreateCursor(AnchorArt.CreatePanIcon(size, PanDirection.Up));
        _cursors[(int)PanDirection.Right] = CreateCursor(AnchorArt.CreatePanIcon(size, PanDirection.Right));
        _cursors[(int)PanDirection.Down] = CreateCursor(AnchorArt.CreatePanIcon(size, PanDirection.Down));
        _cursors[(int)PanDirection.Left] = CreateCursor(AnchorArt.CreatePanIcon(size, PanDirection.Left));
    }

    /// <summary>被目标程序抢回光标的比例（0..1），用于判断要不要降级到浮层。</summary>
    public double MismatchRatio => _samples == 0 ? 0 : (double)_mismatch / _samples;

    public int Samples => _samples;

    public void Show(POINT anchorPoint, double dpiScale)
    {
        _visible = true;
        _current = PanDirection.None;
        Apply(PanDirection.None);
    }

    public void Update(POINT anchorPoint, POINT cursor, double dpiScale)
    {
        if (!_visible)
        {
            return;
        }

        var direction = AnchorArt.DirectionOf(cursor.X - anchorPoint.X, cursor.Y - anchorPoint.Y, 0);
        _current = direction;
        Apply(direction);
    }

    public void Hide()
    {
        if (!_visible)
        {
            return;
        }

        _visible = false;

        // 让指针下的窗口自己把光标恢复回去（比强行设成箭头更正确）。
        Win32.GetCursorPos(out POINT pt);
        IntPtr hwnd = Win32.WindowFromPoint(pt);
        if (hwnd != IntPtr.Zero)
        {
            Win32.SendMessageTimeoutW(hwnd, Win32.WM_SETCURSOR, hwnd,
                Win32.MakeLParam(Win32.HTCLIENT, Win32.WM_MOUSEMOVE), 0x0002, 120, out _);
        }

        IntPtr arrow = Win32.LoadCursorW(IntPtr.Zero, Win32.IDC_ARROW);
        if (arrow != IntPtr.Zero)
        {
            Win32.SetCursor(arrow);
        }
    }

    private void Apply(PanDirection direction)
    {
        IntPtr handle = _cursors[(int)direction];
        if (handle == IntPtr.Zero)
        {
            return;
        }

        Win32.SetCursor(handle);

        IntPtr actual = Win32.GetCursor();
        _samples++;
        if (actual != handle)
        {
            _mismatch++;
        }
    }

    private static IntPtr CreateCursor(Bitmap bitmap)
    {
        IntPtr color = IntPtr.Zero;
        IntPtr mask = IntPtr.Zero;
        try
        {
            color = bitmap.GetHbitmap(Color.FromArgb(0));
            mask = Win32.CreateBitmap(bitmap.Width, bitmap.Height, 1, 1, IntPtr.Zero);
            var info = new ICONINFO
            {
                FIcon = 0,
                XHotspot = bitmap.Width / 2,
                YHotspot = bitmap.Height / 2,
                HbmMask = mask,
                HbmColor = color,
            };
            return Win32.CreateIconIndirect(ref info);
        }
        finally
        {
            if (color != IntPtr.Zero)
            {
                Win32.DeleteObject(color);
            }

            if (mask != IntPtr.Zero)
            {
                Win32.DeleteObject(mask);
            }
        }
    }

    public void Dispose()
    {
        for (int i = 0; i < _cursors.Length; i++)
        {
            if (_cursors[i] != IntPtr.Zero)
            {
                Win32.DestroyIcon(_cursors[i]);
                _cursors[i] = IntPtr.Zero;
            }
        }
    }
}
