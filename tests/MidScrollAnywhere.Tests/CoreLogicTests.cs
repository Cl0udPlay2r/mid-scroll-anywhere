using MidScrollAnywhere.Core.Anchors;
using MidScrollAnywhere.Core.Config;
using MidScrollAnywhere.Core.Guards;
using MidScrollAnywhere.Core.Native;
using MidScrollAnywhere.Core.Scroll;
using Xunit;

namespace MidScrollAnywhere.Tests;

public class WheelAccumulatorTests
{
    [Fact]
    public void Emits_nothing_until_threshold_and_keeps_remainder()
    {
        var accumulator = new WheelAccumulator();

        Assert.Equal((0, 0), accumulator.Accumulate(7, 0, 15));
        Assert.Equal((0, 0), accumulator.Accumulate(7, 0, 15));
        Assert.Equal((15, 0), accumulator.Accumulate(7, 0, 15));
        Assert.Equal(6, accumulator.AccumulatedX, 6);
    }

    [Fact]
    public void Emitted_units_are_multiples_of_emit_step()
    {
        var accumulator = new WheelAccumulator();
        var (x, y) = accumulator.Accumulate(0, -100, 30);

        Assert.Equal(0, x);
        Assert.True(y % 30 == 0);
        Assert.Equal(-90, y);
    }

    [Fact]
    public void Direction_flip_does_not_stick()
    {
        var accumulator = new WheelAccumulator();
        accumulator.Accumulate(0, 100, 120);
        Assert.Equal(100, accumulator.AccumulatedY, 6);

        var down = accumulator.Accumulate(0, -260, 120);
        Assert.Equal(-120, down.Y);
        Assert.Equal(-40, accumulator.AccumulatedY, 6);
    }

    [Fact]
    public void Full_notch_mode_emits_120_only()
    {
        var accumulator = new WheelAccumulator();
        var result = accumulator.Accumulate(0, 130, 120);

        Assert.Equal(120, result.Y);
        Assert.Equal(10, accumulator.AccumulatedY, 6);
    }

    [Fact]
    public void Units_from_pixels_uses_notch_size()
    {
        // 60px 一格：60px/s = 120 units/s = 每秒一格。
        Assert.Equal(120, WheelAccumulator.UnitsFromPixels(60, 60), 6);
        Assert.Equal(240, WheelAccumulator.UnitsFromPixels(120, 60), 6);
    }
}

public class WheelCalibrationTests
{
    [Fact]
    public void Notch_pixels_uses_override_when_present()
    {
        Assert.Equal(40, WheelCalibration.NotchPixels(2, 20));
        Assert.Equal(80, WheelCalibration.NotchPixels(4, 20));
    }

    [Fact]
    public void Notch_pixels_falls_back_to_system_lines()
    {
        int lines = WheelCalibration.SystemLinesPerNotch();
        Assert.InRange(lines, 1, 100);
        Assert.Equal(lines * 20.0, WheelCalibration.NotchPixels(null, 20));
    }
}

internal sealed class FakeProbes : IGuardProbes
{
    public int NotificationState { get; set; } = Win32.QUNS_ACCEPTS_NOTIFICATIONS;

    public bool CursorVisible { get; set; } = true;

    public bool CursorConfined { get; set; }

    public bool BorderlessFullscreen { get; set; }

    public string? Process { get; set; } = "notepad.exe";

    public List<string> Traces { get; } = [];

    public int QueryNotificationState() => NotificationState;

    public bool IsCursorVisible() => CursorVisible;

    public bool IsCursorConfined() => CursorConfined;

    public bool IsForegroundBorderlessFullscreen(out IntPtr foreground)
    {
        foreground = new IntPtr(1234);
        return BorderlessFullscreen;
    }

    public IntPtr GetForegroundWindow() => new(1234);

    public string? GetForegroundProcessName() => Process;

    public void Trace(string message) => Traces.Add(message);
}

public class ImmersiveGuardTests
{
    private static ImmersiveGuard Make(FakeProbes probes, GuardConfig? config = null) =>
        new(probes, config ?? new GuardConfig());

    [Fact]
    public void Allows_normal_desktop_apps()
    {
        var probes = new FakeProbes();
        Assert.False(Make(probes).Check().Blocked);
    }

    [Theory]
    [InlineData(Win32.QUNS_RUNNING_D3D_FULL_SCREEN, "d3d-fullscreen")]
    [InlineData(Win32.QUNS_APP, "fullscreen-app")]
    [InlineData(Win32.QUNS_PRESENTATION_MODE, "presentation")]
    [InlineData(Win32.QUNS_BUSY, "busy")]
    public void Blocks_fullscreen_notification_states(int state, string expectedReason)
    {
        var probes = new FakeProbes { NotificationState = state };
        var verdict = Make(probes).Check();

        Assert.True(verdict.Blocked);
        Assert.Equal(expectedReason, verdict.Reason);
    }

    [Fact]
    public void Quiet_time_is_not_a_blocker()
    {
        var probes = new FakeProbes { NotificationState = Win32.QUNS_QUIET_TIME };
        Assert.False(Make(probes).Check().Blocked);
    }

    [Fact]
    public void Blocks_hidden_cursor_and_learns_process()
    {
        var probes = new FakeProbes { CursorVisible = false, Process = "game.exe" };
        var verdict = Make(probes).Check();

        Assert.True(verdict.Blocked);
        Assert.Equal("cursor-hidden", verdict.Reason);
        Assert.Equal("game.exe", verdict.LearnProcess);
    }

    [Fact]
    public void Blocks_confined_cursor()
    {
        var probes = new FakeProbes { CursorConfined = true };
        Assert.Equal("cursor-confined", Make(probes).Check().Reason);
    }

    [Fact]
    public void Blocks_borderless_fullscreen()
    {
        var probes = new FakeProbes { BorderlessFullscreen = true };
        Assert.Equal("borderless-fullscreen", Make(probes).Check().Reason);
    }

    [Fact]
    public void Each_signal_can_be_disabled()
    {
        var probes = new FakeProbes { CursorVisible = false, CursorConfined = true, BorderlessFullscreen = true };
        var config = new GuardConfig
        {
            CursorHidden = false,
            CursorConfined = false,
            BorderlessFullscreen = false,
        };

        Assert.False(Make(probes, config).Check().Blocked);
    }
}

public class IgnoreListTests
{
    [Fact]
    public void Browsers_are_ignored_by_default()
    {
        var config = new AppConfig();
        Assert.True(IgnoreList.IsIgnored(config, "chrome.exe"));
        Assert.True(IgnoreList.IsIgnored(config, "MSEDGE.EXE"));
        Assert.False(IgnoreList.IsIgnored(config, "notepad.exe"));
    }

    [Fact]
    public void Learned_processes_are_ignored_only_when_learning_enabled()
    {
        var config = new AppConfig();
        Assert.True(IgnoreList.Learn(config, "somegame.exe"));
        Assert.True(IgnoreList.IsIgnored(config, "somegame.exe"));

        config.Guards.LearnedExe = false;
        Assert.False(IgnoreList.IsIgnored(config, "somegame.exe"));
    }

    [Fact]
    public void Learn_does_not_duplicate()
    {
        var config = new AppConfig();
        Assert.True(IgnoreList.Learn(config, "game.exe"));
        Assert.False(IgnoreList.Learn(config, "GAME.EXE"));
        Assert.Single(config.Ignore.Learned);
    }

    [Fact]
    public void Add_and_remove_work_case_insensitively()
    {
        var config = new AppConfig();
        Assert.True(IgnoreList.AddProcess(config, "Notepad.exe"));
        Assert.True(IgnoreList.IsIgnored(config, "notepad.exe"));
        Assert.True(IgnoreList.RemoveProcess(config, "notepad.EXE"));
        Assert.False(IgnoreList.IsIgnored(config, "notepad.exe"));
    }
}

public class AnchorArtTests
{
    [Theory]
    [InlineData(0, 0, 15, PanDirection.None)]
    [InlineData(0, -40, 15, PanDirection.Up)]
    [InlineData(0, 40, 15, PanDirection.Down)]
    [InlineData(-40, 0, 15, PanDirection.Left)]
    [InlineData(40, 0, 15, PanDirection.Right)]
    [InlineData(10, -40, 15, PanDirection.Up)]
    [InlineData(60, 40, 15, PanDirection.Right)]
    public void Direction_is_dominant_axis(int dx, int dy, int deadZone, PanDirection expected)
    {
        Assert.Equal(expected, AnchorArt.DirectionOf(dx, dy, deadZone));
    }

    [Fact]
    public void Pan_icon_has_alpha_and_expected_size()
    {
        using Bitmap bitmap = AnchorArt.CreatePanIcon(40);

        Assert.Equal(40, bitmap.Width);
        Assert.Equal(40, bitmap.Height);
        Assert.Equal(System.Drawing.Imaging.PixelFormat.Format32bppArgb, bitmap.PixelFormat);

        // 圆环上一定有不透明像素，四角一定是透明的。
        Assert.Equal(0, bitmap.GetPixel(0, 0).A);
        Assert.True(bitmap.GetPixel(20, 20).A > 0);
    }
}

public class ConfigStoreTests
{
    [Fact]
    public void Round_trip_keeps_values()
    {
        string dir = Path.Combine(Path.GetTempPath(), "msw-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConfigStore(dir);
            var config = store.Load();
            config.Profile = "firefox";
            config.SpeedMultiplier = 1.25;
            config.Wheel.MinSendUnits = 30;
            config.Wheel.LinesPerNotch = 4;
            config.PerApp["notepad.exe"] = new PerAppOverride { SpeedMultiplier = 2.0, LegacyWholeNotches = true };
            store.Save(config);

            var reloaded = store.Load();

            Assert.Equal("firefox", reloaded.Profile);
            Assert.Equal(1.25, reloaded.SpeedMultiplier, 6);
            Assert.Equal(30, reloaded.Wheel.MinSendUnits);
            Assert.Equal(4, reloaded.Wheel.LinesPerNotch);
            Assert.NotNull(reloaded.OverrideFor("NOTEPAD.exe"));
            Assert.Equal(2.0, reloaded.OverrideFor("notepad.exe")!.SpeedMultiplier!.Value, 6);
            Assert.True(IgnoreList.IsIgnored(reloaded, "chrome.exe"));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Broken_file_falls_back_to_defaults_and_backs_up()
    {
        string dir = Path.Combine(Path.GetTempPath(), "msw-broken-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "config.json"), "{ not json");

            var store = new ConfigStore(dir);
            var config = store.Load();

            Assert.Equal("chromium", config.Profile);
            Assert.True(File.Exists(Path.Combine(dir, "config.broken.json")));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}

public class SessionMathTests
{
    // 计划里的换算：速度 → 单位 → 离散 delta，跑 1 秒应当落在合理区间。
    [Fact]
    public void One_second_of_scrolling_matches_pixel_budget()
    {
        var curve = new MidScrollAnywhere.Core.Curves.ChromiumCurve();
        double notchPx = WheelCalibration.NotchPixels(3, 20); // 60px
        double velocity = curve.VelocityPxPerSecond(0, 300, 1.0).Y; // ~2253 px/s
        double unitsPerSecond = WheelAccumulator.UnitsFromPixels(velocity, notchPx);

        // 每 8ms 一 tick，累计发出多少像素？
        var accumulator = new WheelAccumulator();
        double emittedUnits = 0;
        for (int i = 0; i < 125; i++)
        {
            double units = -unitsPerSecond * 0.008;
            var (_, y) = accumulator.Accumulate(0, units, 15);
            emittedUnits += Math.Abs(y);
        }

        double emittedPixels = emittedUnits / 120.0 * notchPx;
        Assert.InRange(emittedPixels, velocity * 0.85, velocity * 1.01);
    }
}
