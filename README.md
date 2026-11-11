# Mid Scroll Anywhere

浏览器里"中键点一下就能滚页面"的自动滚动，在 Windows 上所有程序里都能用。

浏览器（Chromium / Firefox）是**在页面内部**实现这个功能的，所以其它程序天生没有。这个小工具把它们的行为做成系统级的：
**中键单击 → 锚点出现 → 把指针拉开就持续滚，拉得越远越快 → 再点一下停**。

手感不是拍脑袋调的：速度公式、死区、取消规则全部来自浏览器源码（见下文"保真度"）。

---

## 快速开始

1. 双击 `dist\MidScrollAnywhere.exe`，托盘出现一个圆形四箭头图标（无主窗口）。
2. 在任意能滚动的程序里（记事本、资源管理器、Word、VS Code…）**中键单击**：
   - Chromium 曲线：鼠标指针变成四箭头平移光标；Firefox 曲线：按下点出现 40px 圆环。
   - 指针离按下点越远，滚得越快；回到按下点附近就停住（但仍是激活状态）。
3. **再按下任意鼠标键**（或按任意键盘键）停止。停止用的那次点击会被吞掉，不会顺带选中文字或按到按钮。
4. 托盘菜单：启用/禁用、切换滚动曲线、把当前前台程序加入忽略列表、打开设置。

默认**不**在浏览器里生效（Chrome/Edge/Firefox/Brave/Opera/Vivaldi），也不会在全屏游戏、演示模式里介入——它们本来就会/不该由这个工具来滚。

---

## 保真度：哪些是逐条对齐的

| 项目 | 来源 | 说明 |
|---|---|---|
| 速度公式 | Chromium `blink::AutoscrollController` | `v = 8×10⁻⁶ × \|d\|^2.2` px/ms，无上限 |
| 死区 | 同上 | **每轴 15 DIP 的矩形**（不是圆）；超出才滚 |
| 起步门槛 | `kMinInertialScrollDelta` | 单帧位移 < 0.1px 丢弃 → 实际约 21 DIP 才动 |
| 取消规则 | 同上 | 任意鼠标键按下 / 任意按键 / 前台窗口变化；**移出窗口、滚轮不取消** |
| 点击语义 | 同上 | 只做 Toggled：点一下持续滚，下一次点击停；松手位置不参与判断 |
| 中键转发 | 同上 | 中键本身照常发给程序（开链接、终端粘贴、CAD 平移都保留） |
| 锚点视觉 | `win_cursor_factory.cc` | 浏览器是把指针换成系统平移光标，本工具自绘同款四箭头光标 |
| Firefox 曲线 | `AutoScrollChild.sys.mjs accelerate()` | `val=d/12`，`val×√\|val\|∓1`，单位 px/20ms；死区 12 像素 |

采样对照（本工具自检输出）：

```
Chromium: 50px→44px/s, 100px→201, 200px→923, 300px→2253, 500px→6931, 800px→19493
Firefox : 50px→375px/s, 100px→1153, 200px→3352, 300px→6200, 500px→13398, 800px→27167
```

## 保真度：哪些**做不到**，以及为什么

滚轮消息的量化粒度是硬墙：`WHEEL_DELTA = 120`，一格滚多少**像素**由目标程序自己决定（Windows 默认 3 行 × 行高）。
浏览器内部是连续像素滚动，浏览器之外只能"喂滚轮"。所以：

- 能吃小 delta 的程序（Chromium/Electron、部分 WPF/UWP）：接近平滑。
- 只认整 120 的老程序：会一格一格跳。设置里勾"只发整格"可让它稳定工作，但更顿。
- 这是数学限制，不是实现偷懒。设置里的"最小发送单位 / 行高 / 每格行数 / 速度倍率"就是给这些差异留的旋钮。

完整差异清单：

1. **链接命中**：Chrome 里中键点在链接上不会滚动（只开新标签）。任意程序里做不了链接命中测试，只能靠忽略列表近似。
2. **提权窗口**：UIPI 会静默拒绝注入。本工具以普通权限运行（`asInvoker`），管理员窗口里不生效——不用管理员权限去碰别的进程，是刻意的选择。
3. **只认整格的程序**：见上。
4. **无边框全屏**的判断是启发式（`WS_POPUP` + 无标题栏 + 铺满显示器），全屏看图/全屏播放器可能被误判为"沉浸式"而不介入；可在设置里关掉这条信号。
5. **横向滚动**依赖目标程序处理 `WM_MOUSEHWHEEL`，很多程序不处理。
6. **反作弊**：全屏游戏里零介入（不注入、不换光标），但全局低级钩子本身常驻；遇到游戏/反作弊报警直接退出本工具。

---

## 设置

托盘 → 设置…。所有配置写在 `%APPDATA%\MidScrollAnywhere\config.json`（改动即时生效，无需重启）。

| 字段 | 含义 |
|---|---|
| `profile` | `chromium` / `firefox`，两套曲线，可切换（切换只换参数，瞬时生效） |
| `profileSwitchTiming` | `immediate` 立即生效 / `nextAnchor` 下次锚点出现时生效 |
| `speedMultiplier` | 全局速度倍率 0.2–3.0 |
| `anchorStyle` | `auto`（跟随曲线）/ `cursor`（系统平移光标）/ `overlay`（自绘浮层） |
| `tickMs` | 节拍，默认 8ms（`timeBeginPeriod(1)` + 实测 dt 校正） |
| `wheel.minSendUnits` | 一次发出的最小滚轮单位，越小越平滑，默认 15 |
| `wheel.legacyWholeNotches` | 只发整格 120，给老式程序 |
| `wheel.lineHeightPx` / `wheel.linesPerNotch` | 一格滚轮 = 行高 × 行数，用于把 px/s 折算成格数 |
| `wheelTarget` | `hybrid`（光标在锚点窗口内走真实滚轮，漂出去锁定原窗口投递）/ `cursorWindow` / `anchorWindow` |
| `swallowKeysWhileActive` | 滚动期间吞按键（Chrome 行为，会占用快捷键） |
| `swallowStopClick` | 吞掉用于停止的那次点击 |
| `guards.*` | 五条"不介入"信号：系统全屏判定、光标隐藏、光标锁定、无边框全屏、自动学习名单 |
| `ignore.processes` / `ignore.learned` | 手动忽略 / 自动学习到的游戏程序 |
| `perApp` | 按程序覆盖曲线、倍率、整格模式、行高等 |
| `anchorDowngrade` | 某些程序会抢回光标，自动降级到浮层后记录在这里 |

---

## 自检

```
MidScrollAnywhere.exe --selftest
```

无界面、不动你的光标，检查：配置读写、曲线采样、守卫探针、钩子安装/卸载、以及 `WM_MOUSEWHEEL` 是否真能让一个可滚动窗口滚动。

## 构建

```powershell
dotnet build                      # 编译
dotnet test                       # 51 个单元测试（曲线采样点、累积器、守卫真值表、配置往返）
dotnet publish src\MidScrollAnywhere.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

需要 .NET 8 SDK。发布产物是单个 `MidScrollAnywhere.exe`（约 140 MB，自带运行时，拷走即用）。

## 目录

```
src/MidScrollAnywhere.Core     纯逻辑 + Win32 互操作
  Native/         Win32 声明、结构体
  Curves/         Chromium / Firefox 曲线
  Scroll/         8ms 引擎、滚轮累积器、注入目标
  Anchors/        锚点美术、系统光标渲染、自绘浮层
  Guards/         沉浸式守卫、进程识别、忽略列表
  Input/          低级鼠标/键盘钩子
  Autoscroll/     状态机与取消规则
src/MidScrollAnywhere.App      WinForms 托盘 + 设置界面
tests/MidScrollAnywhere.Tests  xUnit
```

## 验收清单（对着 Chrome 并排比）

- [ ] 记事本 / 资源管理器 / Word / VS Code：中键单击出现锚点并持续滚动
- [ ] 指针拉开 → 变快；回到按下点 → 停住但保持激活；再点一下 → 停止
- [ ] 停止用的点击不产生选中/按钮触发
- [ ] 滚动中按任意键 → 停止（且按键没传给程序）
- [ ] 在 Chrome/Edge/Firefox 里 → 不发生双重滚动
- [ ] 在独占全屏游戏里 → 完全没反应
- [ ] 150% 缩放屏上死区仍是 15 DIP（把指针慢慢拉开，约 15 个逻辑像素后才开始滚）
- [ ] 强杀进程后：无残留光标、无残留滚动
