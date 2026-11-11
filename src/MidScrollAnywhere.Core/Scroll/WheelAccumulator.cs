namespace MidScrollAnywhere.Core.Scroll;

/// <summary>
/// 把连续的"滚轮单位/秒"累积成离散的滚轮 delta。
/// 关键点：余量不丢、方向翻转时不会卡住、emitStep 变化时余量守恒。
/// </summary>
public sealed class WheelAccumulator
{
    private double _accX;
    private double _accY;

    public double AccumulatedX => _accX;

    public double AccumulatedY => _accY;

    public (int X, int Y) Accumulate(double unitsX, double unitsY, int emitStep)
    {
        if (emitStep < 1)
        {
            emitStep = 1;
        }

        _accX += unitsX;
        _accY += unitsY;

        int ex = Take(ref _accX, emitStep);
        int ey = Take(ref _accY, emitStep);
        return (ex, ey);
    }

    public void Reset()
    {
        _accX = 0;
        _accY = 0;
    }

    private static int Take(ref double acc, int emitStep)
    {
        if (Math.Abs(acc) < emitStep)
        {
            return 0;
        }

        // 向零截断：只发出 emitStep 的整数倍，余量留给下一 tick。
        double steps = Math.Truncate(acc / emitStep);
        double emitted = steps * emitStep;
        acc -= emitted;
        return (int)emitted;
    }

    /// <summary>px/秒 → 滚轮单位/秒。notchPx = 一格滚轮在该程序里滚动的像素数。</summary>
    public static double UnitsFromPixels(double pxPerSecond, double notchPx)
    {
        if (notchPx <= 0.0)
        {
            notchPx = 60.0;
        }

        return pxPerSecond / notchPx * 120.0;
    }
}
