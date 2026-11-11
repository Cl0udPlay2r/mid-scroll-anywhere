using MidScrollAnywhere.Core.Native;

namespace MidScrollAnywhere.Core.Anchors;

/// <summary>
/// 穿透式分层窗口：无边框、不激活、不进任务栏、不吃鼠标事件，用 UpdateLayeredWindow 贴一张带 alpha 的位图。
/// </summary>
internal sealed class LayeredAnchorWindow : Form
{
    private Bitmap? _bitmap;

    public LayeredAnchorWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Size = new Size(1, 1);
        BackColor = Color.Black;
        SetStyle(ControlStyles.Opaque, true);
    }

    public Bitmap? CurrentBitmap => _bitmap;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    /// <summary>把位图以"中心对齐 screenPoint"的方式贴到桌面最上层。</summary>
    public void RenderTo(Bitmap bitmap, int screenX, int screenY)
    {
        _bitmap = bitmap;

        if (!IsHandleCreated)
        {
            _ = Handle;
        }

        if (!Visible)
        {
            Show();
        }

        IntPtr screenDc = Win32.GetDC(IntPtr.Zero);
        IntPtr memDc = Win32.CreateCompatibleDC(screenDc);
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;
        try
        {
            hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            previous = Win32.SelectObject(memDc, hBitmap);

            var size = new SIZE(bitmap.Width, bitmap.Height);
            var source = new POINT(0, 0);
            var destination = new POINT(screenX - (bitmap.Width / 2), screenY - (bitmap.Height / 2));
            var blend = new BLENDFUNCTION
            {
                BlendOp = 0,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = Win32.AC_SRC_ALPHA,
            };

            Win32.UpdateLayeredWindow(Handle, screenDc, ref destination, ref size, memDc, ref source, 0, ref blend, Win32.ULW_ALPHA);
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
            {
                Win32.SelectObject(memDc, previous);
                Win32.DeleteObject(hBitmap);
            }

            Win32.DeleteDC(memDc);
            Win32.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public void Conceal()
    {
        if (Visible)
        {
            Hide();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bitmap = null;
        }

        base.Dispose(disposing);
    }
}

/// <summary>Firefox 档（以及 Chromium 档的降级路径）：自绘 40 DIP 的穿透浮层。</summary>
public sealed class OverlayAnchorRenderer : IAnchorRenderer
{
    private const double PanelSizeDip = 40.0;

    private readonly LayeredAnchorWindow _window;
    private Bitmap? _bitmap;
    private POINT _anchor;
    private double _dpiScale = 1.0;
    private bool _visible;

    public OverlayAnchorRenderer()
    {
        _window = new LayeredAnchorWindow();
        _ = _window.Handle;
    }

    public void Show(POINT anchorPoint, double dpiScale)
    {
        _anchor = anchorPoint;
        _dpiScale = dpiScale <= 0 ? 1.0 : dpiScale;
        _visible = true;

        int size = Math.Clamp((int)Math.Round(PanelSizeDip * _dpiScale), 16, 160);
        if (_bitmap is null || _bitmap.Width != size)
        {
            _bitmap?.Dispose();
            _bitmap = AnchorArt.CreatePanIcon(size, PanDirection.None);
        }

        Dispatch(() => _window.RenderTo(_bitmap!, _anchor.X, _anchor.Y));
    }

    public void Update(POINT anchorPoint, POINT cursor, double dpiScale)
    {
        if (!_visible)
        {
            return;
        }

        double scale = dpiScale <= 0 ? 1.0 : dpiScale;
        if (Math.Abs(scale - _dpiScale) > 0.01)
        {
            Show(_anchor, scale);
        }
    }

    public void Hide()
    {
        if (!_visible)
        {
            return;
        }

        _visible = false;
        Dispatch(() => _window.Conceal());
    }

    private void Dispatch(Action action)
    {
        try
        {
            if (_window.IsHandleCreated && _window.InvokeRequired)
            {
                _window.BeginInvoke(action);
            }
            else
            {
                action();
            }
        }
        catch (Exception)
        {
            // 窗口已销毁（退出过程中）。
        }
    }

    public void Dispose()
    {
        try
        {
            if (_window.IsHandleCreated && !_window.IsDisposed)
            {
                _window.BeginInvoke(() =>
                {
                    _bitmap?.Dispose();
                    _bitmap = null;
                    _window.Dispose();
                });
            }
            else
            {
                _bitmap?.Dispose();
                _bitmap = null;
            }
        }
        catch (Exception)
        {
            // 退出路径尽力而为。
        }
    }
}
