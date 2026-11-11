using MidScrollAnywhere.Core.Curves;
using Xunit;

namespace MidScrollAnywhere.Tests;

public class ChromiumCurveTests
{
    private readonly ChromiumCurve _curve = new();

    // 计划 §4 的采样点：d=50/100/200/300/500/800 → 43.7/201/923/2253/6931/19493 px/s
    [Theory]
    [InlineData(50, 43.7)]
    [InlineData(100, 201.0)]
    [InlineData(200, 923.0)]
    [InlineData(300, 2253.0)]
    [InlineData(500, 6931.0)]
    [InlineData(800, 19493.0)]
    public void Downward_offset_matches_chromium_speed_table(int distance, double expectedPxPerSecond)
    {
        var velocity = _curve.VelocityPxPerSecond(0, distance, 1.0);

        Assert.Equal(0, velocity.X);
        Assert.InRange(velocity.Y, expectedPxPerSecond * 0.99, expectedPxPerSecond * 1.01);
    }

    [Fact]
    public void Dead_zone_is_rectangular_per_axis()
    {
        // 每轴独立：dy 在死区内，dx 超出 → 只有 X 滚。
        var velocity = _curve.VelocityPxPerSecond(100, 14, 1.0);

        Assert.NotEqual(0, velocity.X);
        Assert.Equal(0, velocity.Y);
    }

    [Fact]
    public void Both_axes_inside_dead_zone_yield_zero()
    {
        var velocity = _curve.VelocityPxPerSecond(14, -14, 1.0);

        Assert.True(velocity.IsZero);
    }

    [Fact]
    public void Sub_frame_floor_drops_tiny_deflections()
    {
        // 0.1px / 16.667ms 的门槛 → 有效起步约 21 DIP。
        Assert.Equal(0, ChromiumCurve.VelocityAxis(20, 1.0));
        Assert.True(ChromiumCurve.VelocityAxis(21, 1.0) > 0);
    }

    [Fact]
    public void Dead_zone_scales_with_dpi()
    {
        // 200% 屏：30 设备像素 = 15 DIP → 恰好等于死区边界，不滚。
        Assert.Equal(0, ChromiumCurve.VelocityAxis(30, 2.0));
        // 同样的设备像素在 100% 屏上远超死区。
        Assert.True(ChromiumCurve.VelocityAxis(30, 1.0) > 0);
    }

    [Fact]
    public void Direction_sign_is_preserved()
    {
        Assert.True(ChromiumCurve.VelocityAxis(-200, 1.0) < 0);
        Assert.True(ChromiumCurve.VelocityAxis(200, 1.0) > 0);
    }

    [Fact]
    public void Chromium_uses_cursor_anchor()
    {
        Assert.False(_curve.UsesOverlayAnchor);
        Assert.Equal("chromium", _curve.Id);
    }
}

public class FirefoxCurveTests
{
    private readonly FirefoxCurve _curve = new(dpiQuirk: true);

    [Fact]
    public void Matches_autoscroll_child_accelerate_formula()
    {
        // d=20 → 57 px/s；d=500 → 13400 px/s（研究结论里的两个参考点）
        Assert.InRange(FirefoxCurve.VelocityAxis(20, 1.0, true), 57.0, 58.5);
        Assert.InRange(FirefoxCurve.VelocityAxis(500, 1.0, true), 13300, 13450);
    }

    [Fact]
    public void Dead_zone_is_twelve_pixels()
    {
        Assert.Equal(0, FirefoxCurve.VelocityAxis(12, 1.0, true));
        Assert.True(FirefoxCurve.VelocityAxis(13, 1.0, true) > 0);
    }

    [Fact]
    public void Dpi_quirk_uses_device_pixels()
    {
        // 怪癖开：12 设备像素就是死区边界，与 DPI 无关。
        Assert.Equal(0, FirefoxCurve.VelocityAxis(12, 2.0, true));
        // 怪癖关：按 DIP 判定，24 设备像素（=12 DIP @200%）才到边界。
        Assert.Equal(0, FirefoxCurve.VelocityAxis(24, 2.0, false));
        Assert.True(FirefoxCurve.VelocityAxis(30, 2.0, false) > 0);
    }

    [Fact]
    public void Accelerate_matches_reference_implementation()
    {
        Assert.Equal(0, FirefoxCurve.Accelerate(12), 6);
        Assert.Equal(0, FirefoxCurve.Accelerate(-12), 6);
        Assert.True(FirefoxCurve.Accelerate(24) > FirefoxCurve.Accelerate(12));
        Assert.True(FirefoxCurve.Accelerate(-24) < 0);
    }

    [Fact]
    public void Firefox_uses_overlay_anchor()
    {
        Assert.True(_curve.UsesOverlayAnchor);
    }
}

public class CurveProfileTests
{
    [Fact]
    public void Normalize_falls_back_to_chromium()
    {
        Assert.Equal("chromium", CurveProfiles.Normalize(null));
        Assert.Equal("chromium", CurveProfiles.Normalize("nonsense"));
        Assert.Equal("firefox", CurveProfiles.Normalize("FireFox"));
    }

    [Fact]
    public void Create_returns_requested_profile()
    {
        Assert.IsType<ChromiumCurve>(CurveProfiles.Create("chromium"));
        Assert.IsType<FirefoxCurve>(CurveProfiles.Create("firefox"));
    }
}
