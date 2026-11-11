namespace MidScrollAnywhere.Core.Logging;

/// <summary>极简日志：单文件、按大小轮换、绝不因为写日志抛异常。</summary>
public sealed class AppLog
{
    private readonly object _gate = new();
    private string _path;
    private long _maxBytes;

    public AppLog(string path, bool enabled = true, int maxMb = 5)
    {
        _path = path;
        Enabled = enabled;
        _maxBytes = Math.Max(1, maxMb) * 1024L * 1024L;
    }

    public bool Enabled { get; set; }

    public void Configure(string path, bool enabled, int maxMb)
    {
        lock (_gate)
        {
            _path = path;
            Enabled = enabled;
            _maxBytes = Math.Max(1, maxMb) * 1024L * 1024L;
        }
    }

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    private void Write(string level, string message)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                string? dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var info = new FileInfo(_path);
                if (info.Exists && info.Length > _maxBytes)
                {
                    File.Move(_path, _path + ".1", overwrite: true);
                }

                File.AppendAllText(
                    _path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志失败不影响主流程。
        }
    }
}
