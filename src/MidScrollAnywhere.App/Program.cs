using MidScrollAnywhere.Core.Autoscroll;
using MidScrollAnywhere.Core.Config;
using MidScrollAnywhere.Core.Logging;

namespace MidScrollAnywhere.App;

internal static class Program
{
    private const string MutexName = "MidScrollAnywhere.SingleInstance";

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Environment.ExitCode = SelfTest.Run();
            return;
        }

        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Mid Scroll Anywhere 已经在运行（看系统托盘）。",
                "Mid Scroll Anywhere",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // 控制器要在钩子线程之外创建浮层窗口，先装好 WinForms 的同步上下文。
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        var store = new ConfigStore();
        var config = store.Load();
        var log = new AppLog(store.LogPath, config.Logging.Enabled, config.Logging.MaxMb);

        Application.ThreadException += (_, e) => log.Error("UI 线程异常", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Error("未处理异常", e.ExceptionObject as Exception);

        try
        {
            using var tray = new TrayContext(store, log);
            Application.Run(tray);
        }
        catch (Exception ex)
        {
            log.Error("启动失败", ex);
            MessageBox.Show(
                "启动失败：" + ex.Message + Environment.NewLine + store.LogPath,
                "Mid Scroll Anywhere",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}

internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MidScrollAnywhere";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string s && s.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null)
            {
                return;
            }

            if (enabled)
            {
                string exe = Environment.ProcessPath ?? Application.ExecutablePath;
                key.SetValue(ValueName, $"\"{exe}\" --tray");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // 注册表不可写时静默失败。
        }
    }
}
