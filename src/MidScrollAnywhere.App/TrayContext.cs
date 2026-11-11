using MidScrollAnywhere.Core.Anchors;
using MidScrollAnywhere.Core.Autoscroll;
using MidScrollAnywhere.Core.Config;
using MidScrollAnywhere.Core.Curves;
using MidScrollAnywhere.Core.Guards;
using MidScrollAnywhere.Core.Logging;

namespace MidScrollAnywhere.App;

internal sealed class TrayContext : ApplicationContext
{
    private readonly ConfigStore _store;
    private readonly AppLog _log;
    private readonly AutoscrollController _controller;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _chromiumItem;
    private readonly ToolStripMenuItem _firefoxItem;
    private readonly ToolStripMenuItem _statusItem;
    private SettingsForm? _settings;
    private Icon? _trayIcon;

    public TrayContext(ConfigStore store, AppLog log)
    {
        _store = store;
        _log = log;
        _controller = new AutoscrollController(store, log);
        _controller.StateChanged += OnStateChanged;
        _controller.Start();

        // 记录"用户最近一次真正在用的程序"，否则点"加入忽略列表"时前台窗口是本工具自己。
        ForegroundTracker.Start();

        _enabledItem = new ToolStripMenuItem("启用", null, (_, _) => ToggleEnabled()) { CheckOnClick = false };
        _chromiumItem = new ToolStripMenuItem("Chromium 曲线（Chrome / Edge）", null, (_, _) => SetProfile(CurveProfiles.Chromium));
        _firefoxItem = new ToolStripMenuItem("Firefox 曲线", null, (_, _) => SetProfile(CurveProfiles.Firefox));
        _statusItem = new ToolStripMenuItem("状态：…") { Enabled = false };

        var curveMenu = new ToolStripMenuItem("滚动曲线");
        curveMenu.DropDownItems.Add(_chromiumItem);
        curveMenu.DropDownItems.Add(_firefoxItem);

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_enabledItem);
        menu.Items.Add(curveMenu);
        menu.Items.Add(new ToolStripMenuItem("把当前前台程序加入忽略列表", null, (_, _) => IgnoreForeground()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("设置…", null, (_, _) => ShowSettings()));
        menu.Items.Add(new ToolStripMenuItem("打开配置目录", null, (_, _) => OpenFolder(_store.Directory)));
        menu.Items.Add(new ToolStripMenuItem("打开日志", null, (_, _) => OpenFile(_store.LogPath)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => ExitApp()));
        menu.Opening += (_, _) => UpdateMenu();

        _trayIcon = IconFactory.CreateTrayIcon();
        _icon = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = "Mid Scroll Anywhere",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => ShowSettings();

        UpdateMenu();

        if (!_controller.HooksInstalled)
        {
            _icon.ShowBalloonTip(5000, "Mid Scroll Anywhere", "低级鼠标钩子安装失败，功能不可用。", ToolTipIcon.Error);
        }
    }

    private void OnStateChanged(AutoscrollState state)
    {
        if (_icon is null)
        {
            return;
        }

        _icon.Text = state == AutoscrollState.Active ? "Mid Scroll Anywhere（滚动中）" : "Mid Scroll Anywhere";
        UpdateMenu();
    }

    private void UpdateMenu()
    {
        AppConfig config = _controller.Config;
        _enabledItem.Checked = config.Enabled;
        _statusItem.Text = $"状态：{(_controller.HooksInstalled ? "已就绪" : "钩子未安装")} · 曲线 {config.Profile} · {(_controller.State == AutoscrollState.Active ? "滚动中" : "空闲")}"
            + (_controller.LastSkipReason is { } reason ? $" · 上次跳过：{reason}" : string.Empty);
        _chromiumItem.Checked = CurveProfiles.Normalize(config.Profile) == CurveProfiles.Chromium;
        _firefoxItem.Checked = CurveProfiles.Normalize(config.Profile) == CurveProfiles.Firefox;
    }

    private void ToggleEnabled()
    {
        _controller.SetEnabled(!_controller.Config.Enabled);
        UpdateMenu();
    }

    private void SetProfile(string profile)
    {
        _controller.Config.Profile = profile;
        _controller.SaveConfig();
        _controller.RefreshLive();
        UpdateMenu();
        _log.Info($"切换曲线：{profile}");
    }

    private void IgnoreForeground()
    {
        string? exe = _controller.ResolveIgnoreTarget();
        if (string.IsNullOrEmpty(exe))
        {
            _icon.ShowBalloonTip(
                3000,
                "Mid Scroll Anywhere",
                "没拿到要忽略的程序：先切到目标程序，再点这里。",
                ToolTipIcon.Warning);
            return;
        }

        bool added = _controller.AddToIgnore(exe);
        _icon.ShowBalloonTip(
            3000,
            "Mid Scroll Anywhere",
            added ? $"已加入忽略列表：{exe}" : $"{exe} 已在忽略列表里。",
            ToolTipIcon.Info);
        UpdateMenu();
    }

    private void ShowSettings()
    {
        if (_settings is { IsDisposed: false })
        {
            _settings.Activate();
            return;
        }

        _settings = new SettingsForm(_controller);
        _settings.FormClosed += (_, _) =>
        {
            _settings = null;
            UpdateMenu();
        };
        _settings.Show();
        _settings.Activate();
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch
        {
            // 打开目录失败不影响主流程。
        }
    }

    private static void OpenFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                });
            }
        }
        catch
        {
            // 忽略。
        }
    }

    private void ExitApp()
    {
        _icon.Visible = false;
        ForegroundTracker.Stop();
        _controller.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ForegroundTracker.Stop();
            _icon.Visible = false;
            _icon.Dispose();
            _trayIcon?.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal static class IconFactory
{
    public static Icon CreateTrayIcon()
    {
        using Bitmap bitmap = AnchorArt.CreatePanIcon(32, PanDirection.None);
        IntPtr handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }
}
