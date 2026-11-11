namespace MidScrollAnywhere.Core.Curves;

public readonly record struct AxisVelocity(double X, double Y)
{
    public static readonly AxisVelocity Zero = new(0, 0);

    public bool IsZero => X == 0 && Y == 0;
}

/// <summary>
/// 一条"指针偏移 → 滚动速度"的曲线。速度单位统一为屏幕像素/秒；
/// 输入是设备像素偏移（屏幕坐标差），DPI 换算由实现自己负责（各家规则不同）。
/// </summary>
public interface IScrollCurve
{
    /// <summary>配置里的稳定标识。</summary>
    string Id { get; }

    /// <summary>界面显示名。</summary>
    string DisplayName { get; }

    /// <summary>该原型是否使用自绘浮层做锚点（Firefox 是，Chromium 换系统光标）。</summary>
    bool UsesOverlayAnchor { get; }

    AxisVelocity VelocityPxPerSecond(int dxDevice, int dyDevice, double dpiScale);
}

/// <summary>
/// Chromium（Chrome / Edge / Brave / Opera / Vivaldi）的自动滚动曲线。
/// 源码：blink::AutoscrollController + FixedVelocityCurve。
///   v = 8e-6 * |d_dip|^2.2  px/ms，逐轴矩形死区 15 DIP，无速度上限；
///   单帧位移 &lt; 0.1px 被丢弃（按 60fps 名义帧换算，有效起步约 21 DIP）。
/// </summary>
public sealed class ChromiumCurve : IScrollCurve
{
    public const double DeadZoneDip = 15.0;
    public const double Exponent = 2.2;
    public const double MultiplierPxPerMs = 8e-6;
    public const double MinDeltaPerNominalFramePx = 0.1;
    public const double NominalFrameMs = 1000.0 / 60.0;

    public string Id => "chromium";

    public string DisplayName => "Chromium (Chrome / Edge)";

    public bool UsesOverlayAnchor => false;

    public AxisVelocity VelocityPxPerSecond(int dxDevice, int dyDevice, double dpiScale)
    {
        if (dpiScale <= 0)
        {
            dpiScale = 1.0;
        }

        return new AxisVelocity(VelocityAxis(dxDevice, dpiScale), VelocityAxis(dyDevice, dpiScale));
    }

    public static double VelocityAxis(int deltaDevice, double dpiScale)
    {
        // Chromium 把屏幕位移除以 DevicePixelRatio 得到 CSS/DIP 位移。
        double d = Math.Abs(deltaDevice) / dpiScale;
        if (d <= DeadZoneDip)
        {
            return 0;
        }

        double pxPerSecond = MultiplierPxPerMs * Math.Pow(d, Exponent) * 1000.0;

        // 丢弃单帧 < 0.1px 的位移（Chromium 的 kMinInertialScrollDelta）。
        double perFrame = pxPerSecond * NominalFrameMs / 1000.0;
        if (perFrame < MinDeltaPerNominalFramePx)
        {
            return 0;
        }

        return Math.Sign(deltaDevice) * pxPerSecond;
    }
}

/// <summary>
/// Firefox 的自动滚动曲线（toolkit/actors/AutoScrollChild.sys.mjs 的 accelerate()）。
///   val = d / 12；|val| &lt;= 1 时为 0；否则 val*sqrt(|val|) ∓ 1，单位 px / 20ms。
///   死区按设备像素算（Firefox 的 DPI 怪癖），可用 firefoxDpiQuirk 关掉。
/// </summary>
public sealed class FirefoxCurve : IScrollCurve
{
    public const double DeadZonePx = 12.0;
    public const double UnitDivisor = 12.0;
    public const double UnitPeriodMs = 20.0;

    private readonly bool _dpiQuirk;

    public FirefoxCurve(bool dpiQuirk = true)
    {
        _dpiQuirk = dpiQuirk;
    }

    public string Id => "firefox";

    public string DisplayName => "Firefox";

    public bool UsesOverlayAnchor => true;

    public bool DpiQuirk => _dpiQuirk;

    public AxisVelocity VelocityPxPerSecond(int dxDevice, int dyDevice, double dpiScale)
    {
        if (dpiScale <= 0)
        {
            dpiScale = 1.0;
        }

        return new AxisVelocity(VelocityAxis(dxDevice, dpiScale, _dpiQuirk), VelocityAxis(dyDevice, dpiScale, _dpiQuirk));
    }

    /// <summary>原封不动移植 accelerate()：入参为设备像素偏移，返回 px / 20ms。</summary>
    public static double Accelerate(double deltaPx)
    {
        double val = deltaPx / UnitDivisor;
        if (val > 1.0)
        {
            return (val * Math.Sqrt(val)) - 1.0;
        }

        if (val < -1.0)
        {
            return (val * Math.Sqrt(-val)) + 1.0;
        }

        return 0.0;
    }

    public static double VelocityAxis(int deltaDevice, double dpiScale, bool dpiQuirk)
    {
        double d = dpiQuirk ? deltaDevice : deltaDevice / dpiScale;
        if (Math.Abs(d) <= DeadZonePx)
        {
            return 0;
        }

        return Accelerate(d) * (1000.0 / UnitPeriodMs);
    }
}

public static class CurveProfiles
{
    public const string Chromium = "chromium";
    public const string Firefox = "firefox";

    public static readonly string[] Ids = [Chromium, Firefox];

    public static IScrollCurve Create(string? id, bool firefoxDpiQuirk = true) => id switch
    {
        Firefox => new FirefoxCurve(firefoxDpiQuirk),
        _ => new ChromiumCurve(),
    };

    public static string Normalize(string? id) => string.Equals(id, Firefox, StringComparison.OrdinalIgnoreCase) ? Firefox : Chromium;
}
