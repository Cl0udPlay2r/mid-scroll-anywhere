using System.Diagnostics;
using MidScrollAnywhere.Core.Curves;
using MidScrollAnywhere.Core.Native;

namespace MidScrollAnywhere.Core.Scroll;

public static class WheelCalibration
{
    /// <summary>系统"每格滚轮滚几行"（SPI_GETWHEELSCROLLLINES），拿不到就用 3。</summary>
    public static int SystemLinesPerNotch()
    {
        if (Win32.SystemParametersInfoW(Win32.SPI_GETWHEELSCROLLLINES, 0, out uint value, 0))
        {
            // 0xFFFFFFFF 表示"整页"，无法折算成像素，退回默认。
            if (value is >= 1 and <= 100)
            {
                return (int)value;
            }
        }

        return 3;
    }

    public static double NotchPixels(int? linesOverride, double lineHeightPx)
    {
        int lines = linesOverride ?? SystemLinesPerNotch();
        return Math.Max(1.0, lines * lineHeightPx);
    }
}

public sealed class ScrollSession
{
    public required IScrollCurve Curve { get; init; }

    public required IWheelTarget Target { get; init; }

    public POINT AnchorPoint { get; init; }

    public double SpeedMultiplier { get; init; } = 1.0;

    public int TickMs { get; init; } = 8;

    public int EmitStep { get; init; } = 15;

    public double LineHeightPx { get; init; } = 20.0;

    public int? LinesPerNotch { get; init; }

    public double DpiScale { get; init; } = 1.0;

    public bool Horizontal { get; init; } = true;

    public int MaxDtMs { get; init; } = 50;

    public Func<bool>? ShouldContinue { get; init; }
}

public readonly record struct ScrollTick(POINT Cursor, int DeltaX, int DeltaY, long Index);

/// <summary>
/// 8ms 节拍的滚动引擎：曲线 → 单位换算 → 累积 → 注入。
/// 节拍精度靠 timeBeginPeriod(1) + 混合 sleep/spin，dt 用 Stopwatch 实测并截断。
/// </summary>
public sealed class ScrollEngine : IDisposable
{
    private readonly object _gate = new();
    private Thread? _thread;
    private volatile bool _stop;
    private volatile bool _running;

    public bool IsRunning => _running;

    public event Action<ScrollTick>? Ticked;

    public event Action<string>? Stopped;

    public bool Start(ScrollSession session)
    {
        lock (_gate)
        {
            if (_running)
            {
                // 上一轮还没退干净（例如热切换配置），先让它退出再重启。
                _stop = true;
                _thread?.Join(300);
                if (_running)
                {
                    return false;
                }
            }

            _stop = false;
            _running = true;
            _thread = new Thread(() => Run(session))
            {
                IsBackground = true,
                Name = "msw-scroll-engine",
                Priority = ThreadPriority.AboveNormal,
            };
            _thread.Start();
            return true;
        }
    }

    public void Stop()
    {
        _stop = true;
    }

    public void Dispose()
    {
        Stop();
        Thread? thread;
        lock (_gate)
        {
            thread = _thread;
        }

        if (thread is not null && thread.IsAlive && thread != Thread.CurrentThread)
        {
            thread.Join(200);
        }
    }

    private void Run(ScrollSession session)
    {
        string? reason = null;
        Win32.timeBeginPeriod(1);
        try
        {
            var accumulator = new WheelAccumulator();
            double notchPx = WheelCalibration.NotchPixels(session.LinesPerNotch, session.LineHeightPx);
            int tickMs = Math.Max(4, session.TickMs);
            var clock = Stopwatch.StartNew();
            double lastMs = 0.0;
            long index = 0;

            while (!_stop)
            {
                if (session.ShouldContinue is { } keepGoing && !keepGoing())
                {
                    reason = "cancel";
                    break;
                }

                if (!Win32.GetCursorPos(out POINT cursor))
                {
                    reason = "cursor";
                    break;
                }

                double nowMs = clock.Elapsed.TotalMilliseconds;
                double dtMs = Math.Clamp(nowMs - lastMs, 0.1, session.MaxDtMs);
                lastMs = nowMs;
                double dtSeconds = dtMs / 1000.0;

                AxisVelocity velocity = session.Curve.VelocityPxPerSecond(
                    cursor.X - session.AnchorPoint.X,
                    cursor.Y - session.AnchorPoint.Y,
                    session.DpiScale);

                double unitsPerPx = 120.0 / notchPx * session.SpeedMultiplier;

                // 屏幕向下 = 滚轮负 delta；屏幕向右 = HWHEEL 正 delta。
                double unitsY = -velocity.Y * dtSeconds * unitsPerPx;
                double unitsX = session.Horizontal ? velocity.X * dtSeconds * unitsPerPx : 0.0;

                var (deltaX, deltaY) = accumulator.Accumulate(unitsX, unitsY, session.EmitStep);

                if (deltaX != 0 || deltaY != 0)
                {
                    if (!session.Target.Wheel(deltaX, deltaY, cursor))
                    {
                        reason = "target";
                        break;
                    }
                }

                index++;
                Ticked?.Invoke(new ScrollTick(cursor, deltaX, deltaY, index));

                WaitForNextTick(clock, lastMs, tickMs);
            }
        }
        catch (Exception ex)
        {
            reason = "exception: " + ex.Message;
        }
        finally
        {
            Win32.timeEndPeriod(1);
            _running = false;
            Stopped?.Invoke(reason ?? "stopped");
        }
    }

    private void WaitForNextTick(Stopwatch clock, double lastMs, int tickMs)
    {
        double target = lastMs + tickMs;
        while (!_stop)
        {
            double remaining = target - clock.Elapsed.TotalMilliseconds;
            if (remaining <= 0.0)
            {
                return;
            }

            if (remaining > 2.0)
            {
                Thread.Sleep(1);
            }
            else
            {
                Thread.SpinWait(120);
            }
        }
    }
}
