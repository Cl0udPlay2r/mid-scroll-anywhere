using System.Text.Json;
using System.Text.Json.Serialization;

namespace MidScrollAnywhere.Core.Config;

public sealed class WheelConfig
{
    /// <summary>一次发出的最小滚轮单位（120 = 一格）。给"能吃小 delta"的程序用 15。</summary>
    public int MinSendUnits { get; set; } = 15;

    /// <summary>只发整格（老式只认 120 的程序）。</summary>
    public bool LegacyWholeNotches { get; set; }

    /// <summary>一行的像素高度，用于把 px/s 折算成滚轮格数。</summary>
    public double LineHeightPx { get; set; } = 20.0;

    /// <summary>覆盖系统"每格滚几行"，null = 读 SPI_GETWHEELSCROLLLINES。</summary>
    public int? LinesPerNotch { get; set; }
}

public sealed class GuardConfig
{
    public bool FullscreenApi { get; set; } = true;

    public bool CursorHidden { get; set; } = true;

    public bool CursorConfined { get; set; } = true;

    public bool BorderlessFullscreen { get; set; } = true;

    public bool LearnedExe { get; set; } = true;
}

public sealed class IgnoreConfig
{
    public List<string> Processes { get; set; } =
    [
        "chrome.exe",
        "msedge.exe",
        "firefox.exe",
        "brave.exe",
        "opera.exe",
        "vivaldi.exe",
    ];

    public List<string> Learned { get; set; } = [];
}

public sealed class LoggingConfig
{
    public bool Enabled { get; set; } = true;

    public string Level { get; set; } = "info";

    public int MaxMb { get; set; } = 5;
}

public sealed class PerAppOverride
{
    public string? Profile { get; set; }

    public double? SpeedMultiplier { get; set; }

    public bool? LegacyWholeNotches { get; set; }

    public double? LineHeightPx { get; set; }

    public int? EmitStep { get; set; }

    public string? AnchorStyle { get; set; }

    public string? WheelTarget { get; set; }
}

public sealed class AnchorDowngradeInfo
{
    public bool ForceOverlay { get; set; }

    public string? Reason { get; set; }
}

public sealed class AppConfig
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    public string Profile { get; set; } = "chromium";

    /// <summary>immediate | nextAnchor</summary>
    public string ProfileSwitchTiming { get; set; } = "immediate";

    public double SpeedMultiplier { get; set; } = 1.0;

    /// <summary>auto | cursor | overlay</summary>
    public string AnchorStyle { get; set; } = "auto";

    public bool ClickToStart { get; set; } = true;

    public bool SwallowKeysWhileActive { get; set; } = true;

    public bool SwallowStopClick { get; set; } = true;

    /// <summary>hybrid | cursorWindow | anchorWindow</summary>
    public string WheelTarget { get; set; } = "hybrid";

    public int TickMs { get; set; } = 8;

    public bool HorizontalScrolling { get; set; } = true;

    public bool FirefoxDpiQuirk { get; set; } = true;

    public WheelConfig Wheel { get; set; } = new();

    public GuardConfig Guards { get; set; } = new();

    public IgnoreConfig Ignore { get; set; } = new();

    private Dictionary<string, AnchorDowngradeInfo> _anchorDowngrade = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, PerAppOverride> _perApp = new(StringComparer.OrdinalIgnoreCase);

    // 反序列化会把比较器冲掉，这里兜住，保证 exe 名大小写不敏感。
    public Dictionary<string, AnchorDowngradeInfo> AnchorDowngrade
    {
        get => _anchorDowngrade;
        set => _anchorDowngrade = value is null
            ? new Dictionary<string, AnchorDowngradeInfo>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, AnchorDowngradeInfo>(value, StringComparer.OrdinalIgnoreCase);
    }

    public Dictionary<string, PerAppOverride> PerApp
    {
        get => _perApp;
        set => _perApp = value is null
            ? new Dictionary<string, PerAppOverride>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, PerAppOverride>(value, StringComparer.OrdinalIgnoreCase);
    }

    public bool Autostart { get; set; }

    public LoggingConfig Logging { get; set; } = new();

    public PerAppOverride? OverrideFor(string? exe)
    {
        if (string.IsNullOrEmpty(exe))
        {
            return null;
        }

        return PerApp.TryGetValue(exe, out var value) ? value : null;
    }
}

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ConfigStore(string? directory = null)
    {
        Directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MidScrollAnywhere");
        ConfigPath = Path.Combine(Directory, "config.json");
        LogPath = Path.Combine(Directory, "log.txt");
    }

    public string Directory { get; }

    public string ConfigPath { get; }

    public string LogPath { get; }

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                var fresh = new AppConfig();
                Save(fresh);
                return fresh;
            }

            string json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions);
            return config ?? new AppConfig();
        }
        catch (Exception)
        {
            // 损坏配置：备份后回退默认值（计划 §9）。
            try
            {
                if (File.Exists(ConfigPath))
                {
                    File.Copy(ConfigPath, Path.Combine(Directory, "config.broken.json"), overwrite: true);
                }
            }
            catch
            {
                // 备份失败不影响回退。
            }

            var fallback = new AppConfig();
            Save(fallback);
            return fallback;
        }
    }

    public void Save(AppConfig config)
    {
        System.IO.Directory.CreateDirectory(Directory);
        string json = JsonSerializer.Serialize(config, SerializerOptions);
        string temp = ConfigPath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, ConfigPath, overwrite: true);
    }
}
