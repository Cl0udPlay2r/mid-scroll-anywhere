using MidScrollAnywhere.Core.Anchors;
using MidScrollAnywhere.Core.Config;
using MidScrollAnywhere.Core.Curves;
using MidScrollAnywhere.Core.Guards;
using MidScrollAnywhere.Core.Input;
using MidScrollAnywhere.Core.Logging;
using MidScrollAnywhere.Core.Native;
using MidScrollAnywhere.Core.Scroll;

namespace MidScrollAnywhere.Core.Autoscroll;

public enum AutoscrollState
{
    Idle,
    Active,
}

/// <summary>
/// 中键自动滚动的总控：钩子 → 守卫 → 状态机 → 引擎 → 锚点。
/// 只实现 Toggled 模式（点一下持续滚，下一次点击停），松手位置不参与判断。
/// </summary>
public sealed class AutoscrollController : IDisposable
{
    private readonly ConfigStore _store;
    private readonly AppLog _log;
    private readonly ImmersiveGuard _guard;
    private readonly LowLevelHooks _hooks;
    private readonly ScrollEngine _engine = new();
    private readonly SynchronizationContext? _ui;
    private readonly object _gate = new();

    private AppConfig _config;
    private AutoscrollState _state = AutoscrollState.Idle;
    private POINT _anchor;
    private IntPtr _anchorWindow;
    private IntPtr _foregroundAtStart;
    private RECT _anchorRect;
    private double _dpiScale = 1.0;
    private volatile IAnchorRenderer? _renderer;
    private string? _currentExe;
    private int _swallowUpMessage;
    private bool _stopping;
    private int _downgradeAttempted;

    public AutoscrollController(ConfigStore store, AppLog log, IGuardProbes? probes = null)
    {
        _store = store;
        _log = log;
        _config = store.Load();
        _guard = new ImmersiveGuard(probes ?? new SystemGuardProbes(log.Info), _config.Guards);
        _hooks = new LowLevelHooks
        {
            MouseFilter = OnMouseEvent,
            KeyboardFilter = OnKeyboardEvent,
            OnError = ex => _log.Error("hook", ex),
        };
        _ui = SynchronizationContext.Current;

        _engine.Ticked += OnTicked;
        _engine.Stopped += OnEngineStopped;
    }

    public event Action<AutoscrollState>? StateChanged;

    public event Action<string>? Activity;

    public AppConfig Config => _config;

    public AutoscrollState State => _state;

    public bool HooksInstalled => _hooks.IsInstalled;

    public string? LastSkipReason { get; private set; }

    public string? LastSkipProcess { get; private set; }

    public void Start()
    {
        if (!_hooks.Start())
        {
            _log.Error("低级钩子安装失败，功能不可用");
            Activity?.Invoke("钩子安装失败");
            return;
        }

        _log.Info($"启动：曲线={_config.Profile}，忽略 {_config.Ignore.Processes.Count} 项");
    }

    public void Stop() => Cancel("stop");

    public void ReloadFromDisk()
    {
        _config = _store.Load();
        RefreshLive();
    }

    public void SaveConfig()
    {
        try
        {
            _store.Save(_config);
        }
        catch (Exception ex)
        {
            _log.Error("保存配置失败", ex);
        }
    }

    public void SetEnabled(bool enabled)
    {
        _config.Enabled = enabled;
        if (!enabled)
        {
            Cancel("disabled");
        }

        SaveConfig();
    }

    /// <summary>配置在设置界面被改动后调用：立即生效（曲线/倍率/锚点样式都会重建会话）。</summary>
    public void RefreshLive()
    {
        if (_state != AutoscrollState.Active)
        {
            return;
        }

        if (!string.Equals(_config.ProfileSwitchTiming, "immediate", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        RestartSession();
    }

    /// <summary>
    /// 点"把当前前台程序加入忽略列表"时该针对哪个程序。
    ///
    /// 不能用 <see cref="ProcessClassifier.GetForegroundProcessName"/> 直接取：触发按钮的
    /// 托盘菜单 / 设置窗口本身就是前台窗口，取到的永远是 midscrollanywhere.exe。
    /// 优先用 <see cref="ForegroundTracker"/> 记下的最近一次外部前台程序。
    /// </summary>
    public string? ResolveIgnoreTarget() => ForegroundTracker.ResolveTarget();

    /// <summary>把指定程序加入忽略列表；返回是否真的新增了。</summary>
    public bool AddToIgnore(string? exe)
    {
        if (!IgnoreList.AddProcess(_config, exe))
        {
            return false;
        }

        SaveConfig();
        _log.Info($"加入忽略列表：{exe}");
        return true;
    }

    public bool AddForegroundToIgnore() => AddToIgnore(ResolveIgnoreTarget());

    // ---------------------------------------------------------------- 输入回调

    private bool OnMouseEvent(int message, POINT pt, int mouseData)
    {
        switch (message)
        {
            case Win32.WM_MBUTTONDOWN:
                if (_state == AutoscrollState.Active)
                {
                    // 第二次中键 = 停止；Chrome 会把这次点击吞掉。
                    Cancel("middle-down");
                    if (_config.SwallowStopClick)
                    {
                        _swallowUpMessage = Win32.WM_MBUTTONUP;
                        return true;
                    }

                    return false;
                }

                TryActivate(pt);
                return false; // 中键本身始终转发给程序（Q4）

            case Win32.WM_MBUTTONUP:
                return ConsumeSwallowedUp(message);

            case Win32.WM_LBUTTONDOWN:
            case Win32.WM_RBUTTONDOWN:
            case Win32.WM_XBUTTONDOWN:
                if (_state == AutoscrollState.Active)
                {
                    Cancel("other-button");
                    if (_config.SwallowStopClick)
                    {
                        _swallowUpMessage = message switch
                        {
                            Win32.WM_LBUTTONDOWN => Win32.WM_LBUTTONUP,
                            Win32.WM_RBUTTONDOWN => Win32.WM_RBUTTONUP,
                            _ => Win32.WM_XBUTTONUP,
                        };
                        return true;
                    }
                }

                return false;

            case Win32.WM_LBUTTONUP:
            case Win32.WM_RBUTTONUP:
            case Win32.WM_XBUTTONUP:
                return ConsumeSwallowedUp(message);

            default:
                // 滚轮 / 移动都不取消（对齐 Chrome）。
                return false;
        }
    }

    private bool ConsumeSwallowedUp(int message)
    {
        if (_swallowUpMessage == message)
        {
            _swallowUpMessage = 0;
            return true;
        }

        return false;
    }

    private bool OnKeyboardEvent(int vkCode)
    {
        if (_state != AutoscrollState.Active)
        {
            return false;
        }

        Cancel("key");
        return _config.SwallowKeysWhileActive;
    }

    // ---------------------------------------------------------------- 激活

    private void TryActivate(POINT pt)
    {
        if (!_config.Enabled || !_config.ClickToStart)
        {
            return;
        }

        IntPtr foreground = Win32.GetForegroundWindow();
        string? exe = ProcessClassifier.GetProcessName(foreground);

        if (IgnoreList.IsIgnored(_config, exe))
        {
            LastSkipReason = "ignored";
            LastSkipProcess = exe;
            return;
        }

        var verdict = _guard.Check();
        if (verdict.Blocked)
        {
            LastSkipReason = verdict.Reason;
            LastSkipProcess = exe;
            if (verdict.LearnProcess is { } learn && IgnoreList.Learn(_config, learn))
            {
                SaveConfig();
                _log.Info($"学习到沉浸式程序：{learn}");
            }

            return;
        }

        _anchor = pt;
        _foregroundAtStart = foreground;
        _currentExe = exe;
        _downgradeAttempted = 0;

        IntPtr windowUnderCursor = Win32.WindowFromPoint(pt);
        if (windowUnderCursor == IntPtr.Zero)
        {
            windowUnderCursor = foreground;
        }

        _anchorWindow = windowUnderCursor;
        IntPtr root = Win32.GetAncestor(windowUnderCursor, Win32.GA_ROOT);
        IntPtr rectOwner = root != IntPtr.Zero ? root : windowUnderCursor;
        if (!Win32.GetWindowRect(rectOwner, out _anchorRect))
        {
            _anchorRect = new RECT { Left = 0, Top = 0, Right = 1, Bottom = 1 };
        }

        _dpiScale = DpiScaleFor(foreground);
        _state = AutoscrollState.Active;

        // 钩子回调里不建窗口：浮层渲染器要在 UI 线程上创建（否则会在钩子线程里重入）。
        if (_ui is not null && _ui != SynchronizationContext.Current)
        {
            _ui.Post(_ => StartSessionIfActive(), null);
        }
        else
        {
            StartSessionIfActive();
        }

        _log.Info($"激活：{exe} @ {pt} dpi={_dpiScale:0.##}");
        StateChanged?.Invoke(AutoscrollState.Active);
    }

    private void StartSessionIfActive()
    {
        if (_state != AutoscrollState.Active)
        {
            return;
        }

        StartSession();
    }

    private static double DpiScaleFor(IntPtr hwnd)
    {
        uint dpi = Win32.GetDpiForWindow(hwnd);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    private void StartSession()
    {
        PerAppOverride? app = _config.OverrideFor(_currentExe);
        string profileId = CurveProfiles.Normalize(app?.Profile ?? _config.Profile);
        IScrollCurve curve = CurveProfiles.Create(profileId, _config.FirefoxDpiQuirk);

        bool legacy = app?.LegacyWholeNotches ?? _config.Wheel.LegacyWholeNotches;
        int emitStep = app?.EmitStep ?? (legacy ? 120 : Math.Clamp(_config.Wheel.MinSendUnits, 1, 120));
        double lineHeight = app?.LineHeightPx ?? _config.Wheel.LineHeightPx;
        double multiplier = app?.SpeedMultiplier ?? _config.SpeedMultiplier;
        string anchorStyle = app?.AnchorStyle ?? _config.AnchorStyle;
        string targetMode = app?.WheelTarget ?? _config.WheelTarget;

        IWheelTarget target = WheelTargetFactory.Create(targetMode, _anchorWindow, _anchorRect);

        IAnchorRenderer renderer = CreateRenderer(curve, anchorStyle);
        _renderer = renderer;
        renderer.Show(_anchor, _dpiScale);

        var session = new ScrollSession
        {
            Curve = curve,
            Target = target,
            AnchorPoint = _anchor,
            SpeedMultiplier = multiplier,
            TickMs = _config.TickMs,
            EmitStep = emitStep,
            LineHeightPx = lineHeight,
            LinesPerNotch = _config.Wheel.LinesPerNotch,
            DpiScale = _dpiScale,
            Horizontal = _config.HorizontalScrolling,
            ShouldContinue = ContinueSession,
        };

        if (!_engine.Start(session))
        {
            _log.Error("滚动引擎启动失败");
            Cancel("engine-start-failed");
        }
    }

    private IAnchorRenderer CreateRenderer(IScrollCurve curve, string anchorStyle)
    {
        bool forceOverlay = false;
        if (_currentExe is not null && _config.AnchorDowngrade.TryGetValue(_currentExe, out var downgrade))
        {
            forceOverlay = downgrade.ForceOverlay;
        }

        bool overlay = anchorStyle switch
        {
            "overlay" => true,
            "cursor" => false,
            _ => curve.UsesOverlayAnchor || forceOverlay,
        };

        return overlay ? new OverlayAnchorRenderer() : new CursorAnchorRenderer();
    }

    private bool ContinueSession()
    {
        if (_state != AutoscrollState.Active)
        {
            return false;
        }

        if (!Win32.IsWindow(_anchorWindow))
        {
            return false;
        }

        return Win32.GetForegroundWindow() == _foregroundAtStart;
    }

    private void OnTicked(ScrollTick tick)
    {
        IAnchorRenderer? renderer = _renderer;
        renderer?.Update(_anchor, tick.Cursor, _dpiScale);

        if (renderer is CursorAnchorRenderer cursor && cursor.Samples > 40 && cursor.MismatchRatio > 0.6 && _downgradeAttempted == 0)
        {
            _downgradeAttempted = 1;
            DowngradeToOverlay("目标程序反复抢回光标");
        }
    }

    private void DowngradeToOverlay(string reason)
    {
        _log.Warn($"锚点降级到浮层：{reason} ({_currentExe})");
        if (_currentExe is not null)
        {
            _config.AnchorDowngrade[_currentExe] = new AnchorDowngradeInfo { ForceOverlay = true, Reason = reason };
            SaveConfig();
        }

        void Swap()
        {
            if (_state != AutoscrollState.Active)
            {
                return;
            }

            IAnchorRenderer? old = _renderer;
            var overlay = new OverlayAnchorRenderer();
            overlay.Show(_anchor, _dpiScale);
            _renderer = overlay;
            try
            {
                old?.Hide();
                old?.Dispose();
            }
            catch (Exception ex)
            {
                _log.Error("替换锚点渲染器", ex);
            }
        }

        if (_ui is not null)
        {
            _ui.Post(_ => Swap(), null);
        }
        else
        {
            Swap();
        }
    }

    private void OnEngineStopped(string reason)
    {
        if (_stopping)
        {
            return;
        }

        if (_state == AutoscrollState.Active)
        {
            // 引擎自己停了（窗口没了 / 目标拒绝 / 异常），把状态收干净。
            Cancel("engine:" + reason);
        }
    }

    // ---------------------------------------------------------------- 停止

    private void Cancel(string reason)
    {
        IAnchorRenderer? renderer;
        lock (_gate)
        {
            if (_state == AutoscrollState.Idle)
            {
                return;
            }

            _state = AutoscrollState.Idle;
            renderer = _renderer;
            _renderer = null;
        }

        _stopping = true;
        try
        {
            _engine.Stop();
        }
        finally
        {
            _stopping = false;
        }

        try
        {
            renderer?.Hide();
        }
        catch (Exception ex)
        {
            _log.Error("隐藏锚点", ex);
        }

        try
        {
            renderer?.Dispose();
        }
        catch (Exception ex)
        {
            _log.Error("释放锚点", ex);
        }

        _swallowUpMessage = 0;
        _log.Info($"停止：{reason}");
        StateChanged?.Invoke(AutoscrollState.Idle);
    }

    private void RestartSession()
    {
        IAnchorRenderer? renderer;
        lock (_gate)
        {
            renderer = _renderer;
            _renderer = null;
        }

        _stopping = true;
        try
        {
            _engine.Stop();
        }
        finally
        {
            _stopping = false;
        }

        try
        {
            renderer?.Hide();
            renderer?.Dispose();
        }
        catch (Exception ex)
        {
            _log.Error("重建会话", ex);
        }

        if (_state == AutoscrollState.Active)
        {
            StartSession();
        }
    }

    public void Dispose()
    {
        Cancel("dispose");
        _engine.Ticked -= OnTicked;
        _engine.Stopped -= OnEngineStopped;
        _engine.Dispose();
        _hooks.Dispose();
    }
}
