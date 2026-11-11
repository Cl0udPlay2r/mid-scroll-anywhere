using MidScrollAnywhere.Core.Anchors;
using MidScrollAnywhere.Core.Config;
using MidScrollAnywhere.Core.Curves;
using MidScrollAnywhere.Core.Guards;
using MidScrollAnywhere.Core.Input;
using MidScrollAnywhere.Core.Native;
using MidScrollAnywhere.Core.Scroll;

namespace MidScrollAnywhere.App;

/// <summary>
/// 无界面自检：`MidScrollAnywhere.exe --selftest`
/// 覆盖配置读写、曲线采样、守卫探针、钩子安装/卸载、以及"滚轮消息能否真的让一个可滚动窗口滚动"。
/// 不模拟键盘鼠标，不会动你的光标。
/// </summary>
internal static class SelfTest
{
    public static int Run()
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch
        {
            // 某些宿主不允许改编码。
        }

        var failures = new List<string>();
        var lines = new List<string>();

        void Check(string name, bool ok, string? detail = null)
        {
            lines.Add($"[{(ok ? " OK " : "FAIL")}] {name}{(detail is null ? string.Empty : " — " + detail)}");
            if (!ok)
            {
                failures.Add(name);
            }
        }

        // 1. 配置读写
        string dir = Path.Combine(Path.GetTempPath(), "msw-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConfigStore(dir);
            var config = store.Load();
            config.Profile = "firefox";
            config.Wheel.MinSendUnits = 30;
            store.Save(config);
            var reloaded = store.Load();
            Check("配置读写往返", reloaded.Profile == "firefox" && reloaded.Wheel.MinSendUnits == 30);
        }
        finally
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                // 清理失败无所谓。
            }
        }

        // 2. 曲线采样
        var chromium = new ChromiumCurve();
        var firefox = new FirefoxCurve();
        var samples = new[] { 50, 100, 200, 300, 500, 800 };
        lines.Add("  Chromium: " + string.Join(", ", samples.Select(d => $"{d}px→{chromium.VelocityPxPerSecond(0, d, 1.0).Y:0}px/s")));
        lines.Add("  Firefox : " + string.Join(", ", samples.Select(d => $"{d}px→{firefox.VelocityPxPerSecond(0, d, 1.0).Y:0}px/s")));
        Check("Chromium 曲线在 300px 处约 2253px/s", Math.Abs(chromium.VelocityPxPerSecond(0, 300, 1.0).Y - 2253) < 30);
        Check("Firefox 曲线在 20px 处约 57px/s", Math.Abs(firefox.VelocityPxPerSecond(0, 20, 1.0).Y - 57.6) < 1.5);
        Check("死区逐轴矩形（dx=100,dy=14 只滚 X）",
            chromium.VelocityPxPerSecond(100, 14, 1.0).X != 0 && chromium.VelocityPxPerSecond(100, 14, 1.0).Y == 0);

        // 3. 守卫探针
        var probes = new SystemGuardProbes(_ => { });
        int state = probes.QueryNotificationState();
        lines.Add($"  前台通知状态 = {state}（5 = 正常可交互）");
        lines.Add($"  光标可见 = {probes.IsCursorVisible()}，光标被锁定 = {probes.IsCursorConfined()}，无边框全屏 = {probes.IsForegroundBorderlessFullscreen(out _)}");
        lines.Add($"  前台进程 = {probes.GetForegroundProcessName() ?? "(未知)"}");
        Check("守卫探针可读", state is >= 1 and <= 7);

        // 4. 钩子安装 / 卸载
        var hooks = new LowLevelHooks();
        bool installed = hooks.Start();
        Check("低级鼠标+键盘钩子安装", installed);
        hooks.Dispose();
        Check("钩子卸载干净", !hooks.IsInstalled);

        // 5. 滚轮消息投递（真让一个可滚动窗口滚动）
        Check("SendMessage(WM_MOUSEWHEEL) 能让可滚动窗口滚动", WheelDeliveryWorks(out string detail), detail);

        // 6. 设置界面能构建，且布局不塌陷（回归：分组曾被压成 16px 宽、每个字占一行）
        string uiDir = Path.Combine(Path.GetTempPath(), "msw-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            var uiStore = new ConfigStore(uiDir);
            using var controller = new Core.Autoscroll.AutoscrollController(uiStore, new Core.Logging.AppLog(Path.Combine(uiDir, "log.txt"), false));
            using var form = new SettingsForm(controller);
            form.CreateControl();
            form.PerformLayout();

            Check("设置窗口可构建", form.Controls.Count > 0, $"{form.Controls.Count} 个顶层控件");

            var groups = form.Controls.OfType<FlowLayoutPanel>()
                .SelectMany(p => p.Controls.OfType<GroupBox>())
                .ToList();

            Check(
                "设置窗口分组宽度未塌陷",
                groups.Count == 5 && groups.All(g => g.Width >= 500),
                string.Join(", ", groups.Select(g => $"{g.Text}={g.Width}px")));

            var overflowing = new List<string>();
            foreach (GroupBox group in groups)
            {
                foreach (Control row in group.Controls.Cast<Control>().SelectMany(c => c.Controls.Cast<Control>()))
                {
                    if (row.Right > group.ClientSize.Width)
                    {
                        overflowing.Add($"{group.Text}/{row.GetType().Name}");
                    }
                }
            }

            Check("分组内控件不越界", overflowing.Count == 0, string.Join(", ", overflowing));
        }
        catch (Exception ex)
        {
            Check("设置窗口可构建", false, ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            try
            {
                if (Directory.Exists(uiDir))
                {
                    Directory.Delete(uiDir, recursive: true);
                }
            }
            catch
            {
                // 忽略。
            }
        }

        foreach (string line in lines)
        {
            Console.WriteLine(line);
        }

        Console.WriteLine();
        Console.WriteLine(failures.Count == 0 ? "SELFTEST PASS" : $"SELFTEST FAIL ({failures.Count})");
        return failures.Count == 0 ? 0 : 1;
    }

    private static bool WheelDeliveryWorks(out string detail)
    {
        using var form = new Form
        {
            Width = 220,
            Height = 200,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-4000, -4000),
            AutoScroll = true,
        };

        form.Controls.Add(new Panel { Width = 180, Height = 2000, Left = 0, Top = 0, BackColor = Color.Gray });
        _ = form.Handle;
        form.PerformLayout();

        int before = form.AutoScrollPosition.Y;
        Win32.SendMessageW(form.Handle, Win32.WM_MOUSEWHEEL, (IntPtr)((-120 & 0xFFFF) << 16), Win32.MakeLParam(0, 0));
        Application.DoEvents();
        int after = form.AutoScrollPosition.Y;

        detail = $"AutoScrollPosition.Y {before} → {after}";
        return after < before;
    }
}
