using MidScrollAnywhere.Core.Guards;
using Xunit;

namespace MidScrollAnywhere.Tests;

public class ForegroundTrackerTests
{
    [Fact]
    public void Never_tracks_our_own_windows()
    {
        // 点"加入忽略列表"时前台窗口就是设置窗口本身：它必须是"不记录"的那个。
        Assert.False(ForegroundTracker.ShouldTrack(4321, "Notepad", 4321));
        Assert.False(ForegroundTracker.ShouldTrack(4321, "HwndWrapper[MidScrollAnywhere]", 4321));
    }

    [Fact]
    public void Never_tracks_shell_windows()
    {
        // 点托盘图标会把前台窗口让给任务栏/桌面外壳，它们不是"用户正在用的程序"。
        Assert.False(ForegroundTracker.ShouldTrack(999, "Shell_TrayWnd", 1));
        Assert.False(ForegroundTracker.ShouldTrack(999, "Shell_SecondaryTrayWnd", 1));
        Assert.False(ForegroundTracker.ShouldTrack(999, "NotifyIconOverflowWindow", 1));
        Assert.False(ForegroundTracker.ShouldTrack(999, "Progman", 1));
        Assert.False(ForegroundTracker.ShouldTrack(999, "WorkerW", 1));
        Assert.False(ForegroundTracker.ShouldTrack(999, "XamlExplorerHostIslandWindow", 1));
        Assert.False(ForegroundTracker.ShouldTrack(999, "MultitaskingViewFrame", 1));
    }

    [Fact]
    public void Tracks_normal_app_windows()
    {
        Assert.True(ForegroundTracker.ShouldTrack(999, "Notepad", 1));
        Assert.True(ForegroundTracker.ShouldTrack(999, "Chrome_WidgetWin_1", 1));
        Assert.True(ForegroundTracker.ShouldTrack(999, "CASCADIA_HOSTING_WINDOW_CLASS", 1));

        // 拿不到类名时，只要不是自己就当作正常程序。
        Assert.True(ForegroundTracker.ShouldTrack(999, null, 1));
        Assert.True(ForegroundTracker.ShouldTrack(999, string.Empty, 1));
    }

    [Fact]
    public void Ignores_zero_process_id()
    {
        Assert.False(ForegroundTracker.ShouldTrack(0, "Notepad", 1));
    }
}
