using MidScrollAnywhere.Core.Anchors;
using MidScrollAnywhere.Core.Autoscroll;
using MidScrollAnywhere.Core.Config;
using MidScrollAnywhere.Core.Curves;
using MidScrollAnywhere.Core.Guards;
using MidScrollAnywhere.Core.Scroll;

namespace MidScrollAnywhere.App;

internal sealed class SettingsForm : Form
{
    private readonly AutoscrollController _controller;
    private readonly AppConfig _draft;

    private readonly CheckBox _enabled = new() { Text = "启用中键自动滚动", AutoSize = true };
    private readonly ComboBox _profile = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
    private readonly ComboBox _switchTiming = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
    private readonly NumericUpDown _speed = new() { DecimalPlaces = 2, Increment = 0.05m, Minimum = 0.2m, Maximum = 3m, Width = 90 };
    private readonly CheckBox _horizontal = new() { Text = "允许横向滚动", AutoSize = true };
    private readonly CheckBox _autostart = new() { Text = "开机启动", AutoSize = true };

    private readonly NumericUpDown _tick = new() { Minimum = 4, Maximum = 30, Increment = 1, Width = 90 };
    private readonly NumericUpDown _minSend = new() { Minimum = 1, Maximum = 120, Increment = 1, Width = 90 };
    private readonly CheckBox _legacy = new() { Text = "只发整格（120）—— 老式程序的兼容档", AutoSize = true };
    private readonly NumericUpDown _lineHeight = new() { DecimalPlaces = 1, Minimum = 6, Maximum = 80, Increment = 1, Width = 90 };
    private readonly CheckBox _overrideLines = new() { Text = "覆盖系统每格行数", AutoSize = true };
    private readonly NumericUpDown _lines = new() { Minimum = 1, Maximum = 20, Increment = 1, Width = 90 };
    private readonly ComboBox _wheelTarget = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
    private readonly ComboBox _anchorStyle = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };

    private readonly CheckBox _swallowKeys = new() { Text = "滚动期间吞掉按键（Chrome 行为；会占用你的快捷键）", AutoSize = true };
    private readonly CheckBox _swallowStopClick = new() { Text = "吞掉用于停止的那次点击", AutoSize = true };

    private readonly CheckBox _guardFullscreen = new() { Text = "系统全屏/演示判定（独占全屏游戏）", AutoSize = true };
    private readonly CheckBox _guardCursorHidden = new() { Text = "光标被隐藏", AutoSize = true };
    private readonly CheckBox _guardCursorConfined = new() { Text = "光标被锁定在窗口内", AutoSize = true };
    private readonly CheckBox _guardBorderless = new() { Text = "无边框全屏窗口", AutoSize = true };
    private readonly CheckBox _guardLearned = new() { Text = "启用自动学习名单", AutoSize = true };

    private readonly ListBox _ignoreList = new() { Width = 300, Height = 120, IntegralHeight = false };
    private readonly Label _hint = new() { AutoSize = true, MaximumSize = new Size(560, 0), ForeColor = SystemColors.GrayText };

    public SettingsForm(AutoscrollController controller)
    {
        _controller = controller;
        _draft = Clone(controller.Config);

        Text = "Mid Scroll Anywhere · 设置";
        StartPosition = FormStartPosition.CenterScreen;

        // 内容固定按 620px 宽排版（见 MakeGroup），窗口初始尺寸按屏幕工作区收一下，
        // 免得在 1366×768 这类小屏上底部按钮被任务栏盖住。
        Rectangle work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        ClientSize = new Size(Math.Min(660, work.Width - 40), Math.Min(780, work.Height - 60));
        MinimumSize = new Size(GroupWidth + 56, 320);
        ShowInTaskbar = true;
        MaximizeBox = false;

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(8),
        };

        flow.Controls.Add(BuildGeneralGroup());
        flow.Controls.Add(BuildFeelGroup());
        flow.Controls.Add(BuildBehaviorGroup());
        flow.Controls.Add(BuildGuardGroup());
        flow.Controls.Add(BuildIgnoreGroup());

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 48,
            Padding = new Padding(8),
        };
        // 按钮按文字自适应宽度：字体是"点"、会随系统缩放变大，写死像素宽度在 150% 下会把字截断。
        var save = new AutoSizeButton("保存") { DialogResult = DialogResult.None };
        var cancel = new AutoSizeButton("取消");
        var openFolder = new AutoSizeButton("打开配置目录");
        save.Click += (_, _) => SaveAndClose();
        cancel.Click += (_, _) => Close();
        openFolder.Click += (_, _) => OpenConfigFolder();
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(openFolder);

        Controls.Add(flow);
        Controls.Add(buttons);

        LoadFromDraft();
    }

    private static AppConfig Clone(AppConfig source)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(source);
        return System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
    }

    private GroupBox BuildGeneralGroup()
    {
        var group = MakeGroup("通用");
        var grid = MakeGrid();

        _profile.Items.AddRange(["Chromium（Chrome / Edge 手感）", "Firefox 手感"]);
        _switchTiming.Items.AddRange(["立即生效", "下次锚点出现时生效"]);
        _anchorStyle.Items.AddRange(["自动（跟随曲线原型）", "总是换系统光标", "总是自绘浮层"]);

        AddRow(grid, string.Empty, _enabled);
        AddRow(grid, "滚动曲线", _profile);
        AddRow(grid, "切换时机", _switchTiming);
        AddRow(grid, "速度倍率", _speed);
        AddRow(grid, "锚点样式", _anchorStyle);
        AddRow(grid, string.Empty, _horizontal);
        AddRow(grid, string.Empty, _autostart);

        return FinishGroup(group, grid);
    }

    private GroupBox BuildFeelGroup()
    {
        var group = MakeGroup("手感与注入");
        var grid = MakeGrid();

        _wheelTarget.Items.AddRange(["混合（推荐）", "只发真实滚轮", "只投递给锚点窗口"]);
        AddRow(grid, "节拍（毫秒）", _tick);
        AddRow(grid, "最小发送单位", _minSend);
        AddRow(grid, string.Empty, _legacy);
        AddRow(grid, "行高（像素）", _lineHeight);
        AddRow(grid, string.Empty, _overrideLines);
        AddRow(grid, "每格行数", _lines);
        AddRow(grid, "滚轮目标", _wheelTarget);
        AddRow(grid, string.Empty, _hint);

        _hint.Text = "浏览器之外无法做到 100% 相同的连续像素滚动：滚轮 Delta 以 120 为一格，"
            + "一格滚多少像素由目标程序自己决定。这里给出的是最接近的近似。"
            + Environment.NewLine + Environment.NewLine
            + "· 最小发送单位越小越平滑（15 起），只认整格的老程序请勾\"只发整格\"。"
            + Environment.NewLine
            + "· 行高 × 每格行数 = 一格的像素数，用于把像素速度折算成滚轮格数。";

        return FinishGroup(group, grid);
    }

    private GroupBox BuildBehaviorGroup()
    {
        var group = MakeGroup("行为");
        var grid = MakeGrid();
        AddRow(grid, string.Empty, _swallowKeys);
        AddRow(grid, string.Empty, _swallowStopClick);
        return FinishGroup(group, grid);
    }

    private GroupBox BuildGuardGroup()
    {
        var group = MakeGroup("不介入的场景（游戏与沉浸式）");
        var grid = MakeGrid();
        AddRow(grid, string.Empty, _guardFullscreen);
        AddRow(grid, string.Empty, _guardCursorHidden);
        AddRow(grid, string.Empty, _guardCursorConfined);
        AddRow(grid, string.Empty, _guardBorderless);
        AddRow(grid, string.Empty, _guardLearned);
        return FinishGroup(group, grid);
    }

    private GroupBox BuildIgnoreGroup()
    {
        var group = MakeGroup("忽略列表");
        var grid = MakeGrid();

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var add = new AutoSizeButton("添加当前前台程序");
        var remove = new AutoSizeButton("从列表移除选中项");
        var clearLearned = new AutoSizeButton("清空自动学习名单");
        add.Click += (_, _) => AddForeground();
        remove.Click += (_, _) => RemoveSelected();
        clearLearned.Click += (_, _) => ClearLearned();
        buttons.Controls.Add(add);
        buttons.Controls.Add(remove);
        buttons.Controls.Add(clearLearned);
        EqualizeWidths(add, remove, clearLearned);

        // 列表宽度按"分组内宽 - 标签列 - 按钮列"算，按钮宽度是随 DPI 变的，不能写死。
        int buttonWidth = Math.Max(add.PreferredSize.Width, Math.Max(remove.PreferredSize.Width, clearLearned.PreferredSize.Width));
        _ignoreList.Width = Math.Max(180, GroupInnerWidth - LabelColumnWidth - buttonWidth - 24);

        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        panel.Controls.Add(_ignoreList);
        panel.Controls.Add(buttons);

        AddRow(grid, "不生效的程序", panel);
        return FinishGroup(group, grid);
    }

    private const int GroupWidth = 620;
    private const int LabelColumnWidth = 130;

    // 分组内可用宽度（GroupBox 四周各 8px 内边距）。
    private const int GroupInnerWidth = GroupWidth - 16;

    // 注意：分组刻意**不**用 AutoSize。
    // 分组里装的是 Dock=Top 的 TableLayoutPanel，它的宽度取自父分组；而 AutoSize 分组的
    // 宽度又取自内容。两边互等，WinForms 把这个循环解成 0 → 分组被压成 16px 宽、
    // 每个字占一行（看起来像竖排）。所以宽度写死，高度在 FinishGroup 里按布局结果算。
    private static GroupBox MakeGroup(string title) => new()
    {
        Text = title,
        AutoSize = false,
        Width = GroupWidth,
        Padding = new Padding(8, 4, 8, 8),
        Margin = new Padding(0, 0, 0, 10),
    };

    private static TableLayoutPanel MakeGrid()
    {
        var grid = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 0,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
        };

        // 标签列固定宽度：各组之间控件起始位置才对得齐。
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelColumnWidth));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return grid;
    }

    private static GroupBox FinishGroup(GroupBox group, TableLayoutPanel grid)
    {
        group.Controls.Add(grid);

        // 宽度已定，先跑一次布局让 TableLayoutPanel 算出自己的高度，再据此定分组高度。
        group.PerformLayout();
        grid.PerformLayout();
        group.Height = grid.Bottom + group.Padding.Bottom;

        return group;
    }

    private static void AddRow(TableLayoutPanel grid, string label, Control control)
    {
        int row = grid.RowCount;
        grid.RowCount = row + 1;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Margin = new Padding(0, 4, 0, 4);

        if (string.IsNullOrEmpty(label))
        {
            // 没有标签的行（勾选框、提示文字）横跨两列，从最左边开始，别跟着标签列的缩进走。
            grid.Controls.Add(control, 0, row);
            grid.SetColumnSpan(control, 2);
            return;
        }

        grid.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 12, 4),
        }, 0, row);
        grid.Controls.Add(control, 1, row);
    }

    private void LoadFromDraft()
    {
        _enabled.Checked = _draft.Enabled;
        _profile.SelectedIndex = CurveProfiles.Normalize(_draft.Profile) == CurveProfiles.Firefox ? 1 : 0;
        _switchTiming.SelectedIndex = string.Equals(_draft.ProfileSwitchTiming, "nextAnchor", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _speed.Value = Clamp((decimal)_draft.SpeedMultiplier, _speed.Minimum, _speed.Maximum);
        _horizontal.Checked = _draft.HorizontalScrolling;
        _autostart.Checked = _draft.Autostart;

        _anchorStyle.SelectedIndex = _draft.AnchorStyle switch
        {
            "cursor" => 1,
            "overlay" => 2,
            _ => 0,
        };

        _tick.Value = Math.Clamp(_draft.TickMs, 4, 30);
        _minSend.Value = Math.Clamp(_draft.Wheel.MinSendUnits, 1, 120);
        _legacy.Checked = _draft.Wheel.LegacyWholeNotches;
        _lineHeight.Value = Clamp((decimal)_draft.Wheel.LineHeightPx, _lineHeight.Minimum, _lineHeight.Maximum);

        _overrideLines.Checked = _draft.Wheel.LinesPerNotch.HasValue;
        _lines.Value = Math.Clamp(_draft.Wheel.LinesPerNotch ?? 3, 1, 20);
        _lines.Enabled = _overrideLines.Checked;
        _overrideLines.CheckedChanged += (_, _) => _lines.Enabled = _overrideLines.Checked;

        _wheelTarget.SelectedIndex = _draft.WheelTarget switch
        {
            WheelTargetFactory.CursorWindow => 1,
            WheelTargetFactory.AnchorWindow => 2,
            _ => 0,
        };

        _swallowKeys.Checked = _draft.SwallowKeysWhileActive;
        _swallowStopClick.Checked = _draft.SwallowStopClick;

        _guardFullscreen.Checked = _draft.Guards.FullscreenApi;
        _guardCursorHidden.Checked = _draft.Guards.CursorHidden;
        _guardCursorConfined.Checked = _draft.Guards.CursorConfined;
        _guardBorderless.Checked = _draft.Guards.BorderlessFullscreen;
        _guardLearned.Checked = _draft.Guards.LearnedExe;

        RefreshIgnoreList();
    }

    private void RefreshIgnoreList()
    {
        _ignoreList.Items.Clear();
        foreach (string item in _draft.Ignore.Processes)
        {
            _ignoreList.Items.Add(item);
        }

        foreach (string item in _draft.Ignore.Learned)
        {
            _ignoreList.Items.Add(item + "  (自动学习)");
        }
    }

    private void AddForeground()
    {
        string? exe = ProcessClassifier.GetForegroundProcessName();
        if (string.IsNullOrEmpty(exe))
        {
            MessageBox.Show(this, "拿不到前台程序名。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!_draft.Ignore.Processes.Contains(exe, StringComparer.OrdinalIgnoreCase))
        {
            _draft.Ignore.Processes.Add(exe);
            RefreshIgnoreList();
        }
    }

    private void RemoveSelected()
    {
        if (_ignoreList.SelectedItem is not string selected)
        {
            return;
        }

        bool learned = selected.EndsWith("(自动学习)", StringComparison.Ordinal);
        string name = learned ? selected.Replace("  (自动学习)", string.Empty) : selected;
        if (learned)
        {
            _draft.Ignore.Learned.RemoveAll(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            _draft.Ignore.Processes.RemoveAll(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
        }

        RefreshIgnoreList();
    }

    private void ClearLearned()
    {
        _draft.Ignore.Learned.Clear();
        RefreshIgnoreList();
    }

    private void SaveAndClose()
    {
        var config = _controller.Config;

        config.Enabled = _enabled.Checked;
        config.Profile = _profile.SelectedIndex == 1 ? CurveProfiles.Firefox : CurveProfiles.Chromium;
        config.ProfileSwitchTiming = _switchTiming.SelectedIndex == 1 ? "nextAnchor" : "immediate";
        config.SpeedMultiplier = (double)_speed.Value;
        config.HorizontalScrolling = _horizontal.Checked;
        config.AnchorStyle = _anchorStyle.SelectedIndex switch
        {
            1 => "cursor",
            2 => "overlay",
            _ => "auto",
        };
        config.TickMs = (int)_tick.Value;
        config.Wheel.MinSendUnits = (int)_minSend.Value;
        config.Wheel.LegacyWholeNotches = _legacy.Checked;
        config.Wheel.LineHeightPx = (double)_lineHeight.Value;
        config.Wheel.LinesPerNotch = _overrideLines.Checked ? (int)_lines.Value : null;
        config.WheelTarget = _wheelTarget.SelectedIndex switch
        {
            1 => WheelTargetFactory.CursorWindow,
            2 => WheelTargetFactory.AnchorWindow,
            _ => WheelTargetFactory.Hybrid,
        };
        config.SwallowKeysWhileActive = _swallowKeys.Checked;
        config.SwallowStopClick = _swallowStopClick.Checked;
        config.Guards.FullscreenApi = _guardFullscreen.Checked;
        config.Guards.CursorHidden = _guardCursorHidden.Checked;
        config.Guards.CursorConfined = _guardCursorConfined.Checked;
        config.Guards.BorderlessFullscreen = _guardBorderless.Checked;
        config.Guards.LearnedExe = _guardLearned.Checked;
        config.Ignore.Processes = [.. _draft.Ignore.Processes];
        config.Ignore.Learned = [.. _draft.Ignore.Learned];

        if (config.Autostart != _autostart.Checked)
        {
            config.Autostart = _autostart.Checked;
            Autostart.Set(config.Autostart);
        }

        _controller.SaveConfig();
        _controller.RefreshLive();
        Close();
    }

    private void OpenConfigFolder()
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MidScrollAnywhere");
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch
        {
            // 忽略。
        }
    }

    private static decimal Clamp(decimal value, decimal min, decimal max) => Math.Min(Math.Max(value, min), max);

    private static void EqualizeWidths(params Button[] buttons)
    {
        int width = 0;
        foreach (Button button in buttons)
        {
            width = Math.Max(width, button.PreferredSize.Width);
        }

        foreach (Button button in buttons)
        {
            button.MinimumSize = new Size(width, 0);
        }
    }

    /// <summary>
    /// 宽度跟着文字走的按钮。字体以"点"为单位、会随系统缩放变大，而控件尺寸是像素，
    /// 所以写死宽度会在 150% 缩放下把按钮文字截断。
    /// </summary>
    private sealed class AutoSizeButton : Button
    {
        public AutoSizeButton(string text)
        {
            Text = text;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12, 2, 12, 2);
            MinimumSize = new Size(80, 0);
        }
    }
}
