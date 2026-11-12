---
name: win-harness
description: Windows 桌面自动化 CLI（单文件 exe，零依赖）。基于 Windows UI Automation（元素树）+ MSAA 回退通道 + SendInput（真实输入）+ GDI（截图）。
---

## 快速开始

```powershell
$W = ".\scripts\win-harness.exe"

& $W windows -Filter "Notepad"                    # 1. 找窗口
& $W tree -Window "Notepad" -Depth 2              # 2. 看元素树
& $W click -Window "Notepad" -AutomationId "..."  # 3. 精确定位并点击
& $W read -Window "Notepad" -Type Document        # 4. 读值验证
```

输出统一为 UTF-8 JSON：`{"ok":true,"data":...}` 或 `{"ok":false,"error":"..."}`。中文转义为 `\uXXXX`（JSON 解析后无损）。

## 命令速查

| 命令 | 用途 |
|---|---|
| `windows [-Filter 标题] [-Pid n]` | 枚举顶层窗口（hwnd/title/pid/rect） |
| `tree -Window <定位> [-Depth 3] [-Filter 文本] [-Backend uia\|msaa\|auto]` | 元素树（扁平 JSON） |
| `find -Window <定位> [-Name <>] [-Type <>] [-AutomationId <>] [-Exact]` | 查找元素（最多 20 个，含坐标/可点击点/值） |
| `click -Window <定位> (-AutomationId <>\|-Name <>\|-X n -Y n) [-Index 0] [-Double] [-Right] [-Scroll] [-Via auto\|click\|select\|toggle\|invoke\|expand] [-ForceClick]` | 点击（默认按控件语义选通道，失败回退鼠标） |
| `type -Window <定位> (-Text <>\|-TextB64 <>) [-AutoFocus] [-Focus <>] [-NoAutoFocus]` | 输入文本（剪贴板法，支持中文）|
| `setvalue -Window <定位> [-Name <>] [-Type Edit] (-Text <>)` | **直接设值**（UIA 原生，最可靠） |
| `key -Window <定位> -Keys "ENTER" [-AutoFocus] [-Focus <>]` | 按键（如 `CTRL+S` / `ALT+F4`） |
| `screenshot [-Window <定位>] [-Monitor n] [-Capture auto\|screen\|window] [-Raise] [-Out <路径>]` | 截图；带 `-Window` 时返回遮挡比例 |
| `wait -Window <定位> -Name <> [-Timeout 10] [-Gone]` | 等待元素出现/消失 |
| `focus -Window <定位>` | 激活窗口 |
| `setwindow -Window <定位> [-X -Y -Width -Height] [-State ...] [-MoveToMonitor n] [-Center]` | 窗口状态/位置/尺寸/跨屏/居中 |
| `launch -Path <exe> [-Args <>] [-Workdir <>] [-WaitWindow <>] [-Timeout 15] [-Max]` | 启动程序（可等窗口） |
| `kill (-Window \| -Pid \| -Path) [-Force]` | 结束进程 |
| `hover [-Window <定位>] -X -Y` | 移动光标 + 报告光标下元素 |
| `drag [-Window <定位>] (-FromX -FromY -ToX -ToY \| -From x,y -To y,y) [-Duration 500] [-Button left\|right]` | 拖拽（分步插值，非瞬移） |
| `scroll [-Window <定位>] -X -Y [-Delta -120] [-Times 1]` | 真滚轮（非 UIA 滚动） |
| `clipboard [-Get] \| [-Set -Text <>]` | 读/写剪贴板 |
| `read -Window <定位> [-Name <>\|-AutomationId <>] [-Index n]` | 读元素文本（Value→Text→Name） |
| `getprop -Window <定位> [-Name <>\|-AutomationId <>] [-Index n]` | 读属性与支持的 Pattern |
| `pixel -X -Y` | 取屏幕像素颜色 |
| `ocr [-Window <定位> \| -X -Y -Width -Height] [-Upscale auto\|n] [-Lang <>] [-Capture auto\|screen\|window] [-Raise] [-NoWords] [-Out <路径>]` | OCR 识别文字，返回**屏幕绝对坐标**（无 UIA 的自绘界面首选） |
| `surfaces` | 列出 shell 表面：taskbar / taskbar2 / desktop / start / overflow / notifications |
| `open -Surface <名字> [-Timeout 1500]` | 打开 shell 表面并返回 hwnd（之后用 `-Window shell:<名字>` 操作） |
| `trace [-Tail 20] [-Failed] [-Since 2h] [-Stats] [-Dir <目录>]` | 回读调用轨迹（需 `-Trace` 才记录） |
| `wndat -X -Y` | 诊断：报告该点命中的窗口（hwnd/根窗口/进程/类名） |
| `clicktext (-Window <定位> \| -X -Y -Width -Height) -Text <文字> [-Index 0] [-DryRun] [-NoWords]` | **按屏幕上的文字点击**（不依赖 UIA），自绘界面/XAML 弹层/任务栏空树时的兜底 |
| `do -Ops <子命令1 ; 子命令2 ; ...>` | 批处理，一次进程执行多步 |
| `b64 -Text <>` | 文本转 base64（备用） |

**窗口定位**：标题（模糊包含）｜`pid:1234`｜`hwnd:12345`

## 核心工作流

1. **找窗口** → `windows -Filter "关键词"`（拿 hwnd / title / pid）
2. **看结构** → `tree -Window <定位> -Depth 3`（了解元素、名称、automationId）
3. **精确定位** → 优先用 `-AutomationId`（最稳定），其次 `-Name -Exact`，最后 `-Name` + `-Index`
4. **操作** → `click` / `setvalue` / `type` / `key`
5. **验证** → `read` / `find` / `screenshot` / `pixel`

## 元素定位策略（四级降级）

1. **`-AutomationId`**（首选）：稳定，不受语言/版本影响。用 `tree`/`find` 先发现它
2. **`-Name -Exact`**：名称精确匹配
3. **`-Name` + `-Type` + `-Class`**：组合缩小范围
4. **`-X -Y` 坐标**：最后手段。配合 `hover` 先探测该点是什么元素

> ⚠ **歧义即报错**：匹配到多个元素且未显式给 `-Index` 时，命令会报错并列出候选，**绝不静默取第一个**。
> 同理，同标题多窗口需 `-WindowIndex <n>`，否则报错列出候选。

## 点击通道（-Via）—— 语义远比"能点就行"重要

`click` 的默认通道 `auto` **按控件类型对齐鼠标点击的真实语义**：

| 控件类型 | 鼠标点击的含义 | 使用的通道 |
|---|---|---|
| ListItem / TreeItem / TabItem / DataItem | **选中** | `SelectionItemPattern.Select()` |
| CheckBox / ToggleButton | **切换** | `TogglePattern.Toggle()` |
| Button / Hyperlink / MenuItem | **激活默认动作** | `InvokePattern.Invoke()` |
| 其他 | — | 真实鼠标点击 |

> ⚠ **`Invoke` 被刻意排在最后**。对列表项而言 `InvokePattern.Invoke()` 的语义是「执行默认动作」
> （≈ **双击 → 打开**），而不是「点击 → 选中」。若把它排在前面，一次本意是"选中文件"的 `click`
> 会**把文件打开** —— 破坏性副作用，而响应里只显示 `via=invoke`，用户无从察觉。

需要显式激活/打开时，写明 `-Via invoke`：

```powershell
& $W click -Window $hw -Type ListItem -NameB64 <b64> -Exact -Via select   # 只选中
& $W click -Window $hw -Type ListItem -NameB64 <b64> -Exact -Via invoke   # 打开
& $W click -Window $hw -AutomationId btnOk -Via click                     # 强制真实鼠标点击
```

`-ForceClick` 等价于 `-Via click`（保留兼容）。响应中的 `via` 字段取值：
`select` / `toggle` / `expand` / `invoke` / `click`（显式要求鼠标）/ `click-fallback`（auto 下无 Pattern 可用）/ `point`（坐标模式）。

## 键盘焦点（-AutoFocus）

`type` 与 `key` **默认不干预键盘焦点** —— 直接向当前前台窗口发送输入。

需要置焦点时显式加 `-AutoFocus`（或用 `-Focus <名称>` 指定目标）。其实现走 **UIA `SetFocus()`**：

> ⚠ 历史实现是「找到编辑区 → **合成点击一下**」来抢焦点。这一下点击除置焦点外，还会
> **清空列表等其他控件的选中状态**。现已改为 UIA 原生 `SetFocus()`（不产生鼠标事件）。
> `SetFocus` 失败时**不会退回点击**，而是明确报错 —— 静默的副作用比报错代价更高。

响应中会附带 `focus` 对象（`via=uia-setfocus`、`element`、`hasKeyboardFocus`，未真正获得焦点时给出 `warning`）。
多候选时同样遵守歧义规则（报错列候选），需用 `-FocusIndex <n>` 或 `-Focus <名称>` 收敛。

## 坐标系与可靠性（重要）

- 进程启动时自动声明 DPI 感知（优先 **per-monitor-v2**），统一 UIA 与 GDI 的坐标空间
- **负数坐标完全支持**。左/上副屏位于负坐标区域（如 `-X -1281 -Y 277`），可直接使用。
  解析器能区分「选项名」与「负数值」—— `-1281` 会作为 `-X` 的值，不会被当成开关。
  - 若某个值本身以 `-` 开头且不是数字（如 `-Args -Dfoo`），用等号形式：`-Args=-Dfoo`
- **坐标无效会直接报「坐标无效」**，不会退化成「匹配到 N 个元素（无过滤条件）」的元素歧义错误
- `screenshot` 返回实际捕获矩形 `{x,y,w,h}` —— 用它把图片坐标换算回屏幕坐标
- 若 UIA 与 GetWindowRect 的窗口矩形读数不一致，`screenshot` 会附带 `warning` 字段
- **`hover` / `drag` / `scroll` 是纯坐标操作**，作用于屏幕最顶层窗口。被遮挡时请传 `-Window <定位>` 先把目标置前台（否则会操作到错误的窗口上且无报错）
- `setwindow` 的响应字段（**不再自相矛盾**）：
  - `applied` —— 已执行的动作名列表（如 `["rect","moveToMonitor"]`），**不含几何结果**
  - `requested_rect` —— 请求的几何（仅当请求了几何时出现）
  - `rect` —— **权威结果**，执行后实际几何，一切以它为准
  - `geometry_note` —— 请求与实得不一致时的解释（应用最小尺寸 / 跨 DPI 换算）
  - `monitor_index` —— 窗口当前所在显示器序号
  - ⚠ **窗口移动或改尺寸后，之前记录的屏幕坐标全部失效**，需重新读 `rect` 或重新截图

## MSAA 回退通道

Chromium 系应用（Edge / Electron / WebView2 / Tauri）的 UIA 树极浅，因为 Chromium 的无障碍树是惰性启用的。
`-Backend auto`（默认）在 UIA 元素数 < 15 时自动改走 MSAA 通道，取元素更多者，并标注 `"backend":"msaa-fallback"`。

实测效果（depth=4）：Edge 7 → 17，VS Code 6 → 9。
**但要注意**：MSAA 只多挖出**浏览器外壳**（窗口按钮、标签栏），**不触及网页正文**。网页内部操作请走 CDP。

## OCR（自绘 / 无 UIA 界面）

用 WinRT `Windows.Media.Ocr` 识别窗口或指定矩形，返回 `lines[] + words[]`，**坐标已换算为屏幕绝对坐标**，可直接拿来点击。

```powershell
& $W ocr -Window "微信"                        # 默认 zh-Hans，上采样按 DPI 自动选择
& $W ocr -X 0 -Y 0 -Width 1920 -Height 1080    # 指定屏幕区域
& $W ocr -Window "CAD" -Lang en-US -Out "$env:TEMP\cad.png"
```

典型流程：`ocr -Window X` → 在 `lines` 里找目标文字 → `click -X (x+4) -Y (y+4)`（点左上角，避开字缝）。

**返回值里有几个字段必须看：**

| 字段 | 含义 |
|---|---|
| `engine` | **实际**使用的语言引擎（如 `zh-Hans-CN`）。`-Lang` 只是请求 |
| `engine_fallback` | 出现即表示**请求语言不可用、已退回用户语言**；同时给 `warning_lang`。此时 `-Lang` 没生效 |
| `upscale` / `upscale_mode` | 实际倍数与来源（`auto(dpi=144)` / `explicit`） |
| `code: "EMPTY_OCR"` | **没识别到任何文字**。别当成"这里没字"，见下 |

**上采样（`-Upscale`）的正确用法**

默认 `auto`：DPI ≥ 120（125% 以上缩放）取 1，否则取 2。原因：高 DPI 屏系统已按缩放比渲染文字（150% 屏的 12px 字实际 18px），再放大只会糊掉小字并使耗时线性增长。

实测（150% 屏，2902×1870 窗口）：

| Upscale | 耗时 | 行数 | 已知标签命中 |
|---|---|---|---|
| 1 | 1.07s | 156 | 11/11 |
| 2（旧默认） | 1.89s | 145 | 9/11 |
| 3 | 3.10s | 148 | 小字误识（`会话` → `刎舌`） |

某些小字区域在 `-Upscale 2` 下会**直接返回 0 行**，降到 1 反而完全正确。所以：**`EMPTY_OCR` 的第一反应是把 upscale 降到 1 重试，而不是判定"没文字"**。只有 100% DPI 屏才需要 2。

**四个必须知道的限制**

1. **抓的是屏幕像素**（`CopyFromScreen`），不是窗口自己的内容。**被其他窗口遮挡时会读到别人的文字并当成目标的**，且不会报错。操作前先 `focus` 置前台，或先 `screenshot` 核对。
2. **最小化/隐藏窗口直接报错**（`窗口已最小化…`），不会返回空。先把窗口 `focus` 还原。
3. **中文按单字拆分**（`会 话 列 表`），做文本匹配前先去掉空格：`-replace '\s',''`。
4. **只认文字**。图标、图形按钮、无字区域它看不见——那才轮到 `pixel` + `hover` 探测。

**响应瘦身**：只要行级文本时加 `-NoWords`，省掉 word 级坐标数组——实测响应从 61.8KB 降到 13.8KB（-78%）。需要点具体某个词时才保留 words。

## 遮挡、抓取模式与 `-Raise`

**这是最容易出错的一环**：`screenshot` / `ocr` 默认抓的是**屏幕像素**（`CopyFromScreen`），目标一旦被别的窗口盖住，就会**静默读到遮挡者的内容**，且不报错。

带 `-Window` 时，响应里会出现：

| 字段 | 含义 |
|---|---|
| `occluded` | 被**其他应用**窗口覆盖的面积比例（0–1，网格采样 24×24） |
| `covered_samples` | `被覆盖点数/总点数`，用来看采样有效性 |
| `covered_by` | 覆盖者列表（hwnd / proc / title / rect / ratio），最多 3 个 |
| `capture` | 实际用的抓取通道：`screen` / `window` / `screen(fallback)` |
| `raised` | 本次调用是否执行过置前 |

> 同一应用的多窗口不算遮挡（UWP 的 `ApplicationFrameHost` 框架 + `CoreWindow`、XAML 弹出层同 PID/同标题、父子窗口），否则会把应用自己的部件误报成遮挡者。

**`-Capture` 三种取值：**

| 值 | 行为 |
|---|---|
| `auto`（默认） | `occluded > 0.05` 时优先 `PrintWindow` 抓窗口自身内容，否则抓屏幕 |
| `screen` | 强制屏幕像素，与旧行为一致 |
| `window` | 强制 `PrintWindow(PW_RENDERFULLCONTENT)`，失败回退屏幕并标 `screen(fallback)` |

**实测（Navicat 窗口被完全遮挡时）：**

| 模式 | 结果 | 耗时 |
|---|---|---|
| `screen` | **168 行**——全是遮挡者（OC Manager）的文字 | 1174ms |
| `window` | **27 行**——Navicat 自己的界面文字 | 521ms |

**`PrintWindow` 支持度（实测）：**

| 目标 | 结果 |
|---|---|
| Win32 / WinForms / WPF | ✅ |
| WinUI / UWP（计算器） | ✅ 遮挡状态下仍抓到正确内容 |
| 自绘（微信 4.0） | ✅ |
| Qt（Navicat） | ✅ |
| DirectX 独占 / 硬件叠加层 | ❌ 返回全黑 → 自动回退 `screen` 并标 `screen(fallback)` |

**`-Raise`**：检测到遮挡就置前再抓（内部走 `Uia.FocusWindow` 的 ALT 技巧 + `AttachThreadInput`，裸 `SetForegroundWindow` 在后台进程里基本无效）。

```powershell
& $W ocr -Window "微信" -Capture window          # 不看是否被遮挡，直接取窗口内容
& $W ocr -Window "Navicat" -Raise                # 先置前再抓（会抢焦点）
```

## 支持程度分级（实测）


| 类型 | 等级 | 说明 |
|---|---|---|
| Win32 经典控件（记事本旧版/资源管理器/对话框） | ★★★★★ | 纯元素驱动，`AutomationId` 稳定，零截图 |
| WinForms / WPF | ★★★★★ | 同上；WPF 虚拟化列表需 `-Scroll` |
| UWP / WinUI（计算器/设置） | ★★★★★ | 元素暴露好（计算器实测 51 个按钮，`num7Button` 等 aid 稳定，`Invoke` 通道可用）；但**组合键可能不响应**，改用 `setvalue` |
| Qt（如 Navicat） | ★★★☆☆ | 结构可读但**几乎无名称**（Navicat 实测 34 个 Pane，aid 是数字）→ 定位靠 OCR 文字 + 坐标实现 |
| 自绘 UI（微信 4.0、CAD、国产软件） | ★★★☆☆ | UIA 几乎为空（微信实测仅 3 个元素）→ **先走 `ocr`** 拿到文本+屏幕坐标直点；OCR 认不出（图标、纯图形）才退回截图 + `pixel` + `hover` 探测 |
| Chromium 系（Edge/Electron/WebView2） | ★☆☆☆☆ | UIA 树 6-13 个元素，MSAA 仅补外壳 → **请用 CDP** |

## Shell 表面（任务栏 / 托盘 / 开始菜单 / 通知中心）

这些窗口**不在 `windows` 的返回里**（实测 `windows` 只列 7 个普通窗口，`Shell_TrayWnd` 不在其中），所以提供了别名定位：

```powershell
& $W surfaces                                  # 先看有什么、hwnd、rect、类名
& $W tree -Window shell:taskbar -Depth 3       # 任务栏（托盘图标都是带名字的 Button）
& $W open -Surface start                       # 打开开始菜单，返回 hwnd
& $W tree -Window shell:overflow -Depth 3      # 托盘"隐藏图标"弹层
& $W click -Window shell:taskbar -Name "显示隐藏的图标"
```

`shell:` 支持：`taskbar` / `taskbar2`（副屏）/ `desktop` / `start` / `overflow` / `notifications`。

**实测（Win11 26H2 / build 26300）：**

| 表面 | 状态 |
|---|---|
| `taskbar` | ✅ 27–28 个元素，托盘按钮带完整名称（`显示隐藏的图标` / `蓝牙设备需要...` / `电源 电池状态: 已充满 100%`） |
| `taskbar2` | ✅（副屏 `Shell_SecondaryTrayWnd`） |
| `desktop` | ✅ `Progman` |
| `overflow` | ✅ `TopLevelWindowForOverflowXamlIsland`，标题「系统托盘溢出窗口。」 |
| `start` | ⚠️ 见下 |
| `notifications` | ⚠️ 未实测通过 |

**两个已知坑：**

1. **任务栏 UIA 树会间歇性返回 0 个元素**（同一 hwnd 先 27 个、稍后 0 个，按一次 `ESC` 后又恢复——通常是开始菜单/弹出层残留把树压制了）。`tree -Window shell:*` 已内置 3 次重试；仍为空时 `backend` 会标 `uia(empty-after-retry)`，此时改用 `screenshot` + `ocr` 兜底。
2. **开始菜单在 Win11 是 `Wnd_StartFeed`**，不再是老的 `Windows.UI.Core.CoreWindow`（同标题的 CoreWindow 实测是「设置」）。另外本机 **`SendInput` 发 `VK_LWIN` 不弹开始菜单**，所以 `open -Surface start` 改为先点任务栏的开始按钮，再退回 WIN 键。若两条都失败会明确报错，不会静默。

## 按文字点击（`clicktext`）

当**元素树里根本没有目标**时用这个：自绘界面、XAML 弹层、任务栏 UIA 抽风、以及那些只有截图才有意义的界面。它把 OCR 结果直接变成可点坐标，不经过 UIA。

```powershell
& $W clicktext -Window "Navicat" -Text "我的连接" -DryRun     # 先看命中哪几处
& $W clicktext -Window "Navicat" -Text "我的连接" -Index 0     # 再真点
& $W clicktext -X 440 -Y 260 -Width 1100 -Height 1520 -Text "计算器"   # 只在该区域找（弹层/开始菜单）
```

返回 `matches[]`（全部命中，含坐标）与 `picked` / `point` / `clicked`。**先用 `-DryRun` 确认再点**，因为 OCR 是模糊匹配：

- 匹配规则：归一化去空白后「OCR 行包含查询」或「查询包含 OCR 行（行需 ≥2 字）」
- 命中多处时用 `-Index` 选；不指定默认第 0 个
- 区域模式（`-X -Y -Width -Height`）不需要 `-Window`，弹层 OCR 比全窗快一个量级

**实测**：Win11 开始菜单的 UIA 窗口在本机无法定位（点击能打开，但 `Wnd_StartFeed` 始终不可见、`WindowFromPoint` 也命中不到它），改用 `clicktext -X ... -Text "计算器"` → 命中 `(1342,801) 计 算 器` 并成功点击、拉起了计算器。这就是"缺口用另一条通道绕过去"的典型做法。

代价：OCR 有误识（小字经常错 1–2 个汉字），所以**关键操作要配 `-DryRun` + 结果校验**；不要用它在聊天窗口里点"发送"这类不可逆按钮。

## 轨迹与错误码（长流程排障）


加 `-Trace`（或环境变量 `WIN_HARNESS_TRACE=1`）后，每次调用写一行 NDJSON：

```
%LOCALAPPDATA%\win-harness\trace\20261112.ndjson
```

```json
{"ts":"2026-11-12T21:09:16.370","seq":2,"trace":"848d33c0","cmd":"find","step":2,"of":3,
 "args":{"Window":"nosuch"},"ok":false,"ms":19,"code":"NOT_FOUND","retry":"reprobe","error":"..."}
```

- **`do` 批处理共用一个 `trace` id**，每步一条，带 `step` / `of`，失败处直接可见前几步干了什么
- **脱敏**：`-Text` / `-TextB64` / `-Args` / `clipboard -Set` 记为 `<redacted:N>`；定位用的 `-Name` / `-AutomationId` 保留
- 结果里的关键字段（`occluded` / `capture` / `count` / `upscale`）会提到行顶层，方便 grep
- 按天分文件，自动清理 7 天前；`-Trace` 关闭时零开销

回读：

```powershell
& $W trace -Tail 20            # 最近 20 条（原始行，不解析）
& $W trace -Failed             # 只失败
& $W trace -Since 30m -Stats   # 聚合：总数/失败/平均耗时/按 cmd/按 code
```

**错误码与重试建议**（响应和 trace 里都有）：

| code | 含义 | retry |
|---|---|---|
| `NOT_FOUND` | 窗口/元素不存在 | `reprobe`（重查树） |
| `AMBIGUOUS` | 多匹配 | `never`（要人来收敛条件） |
| `PERM_DENIED` | 完整性级别不足（UIPI） | `never`（需提权终端） |
| `EMPTY_OCR` | OCR 无结果 | `safe`（降 upscale 重试） |
| `OCR_MINIMIZED` | 窗口最小化/隐藏 | `safe`（focus 还原后） |
| `BLOCKED_SHORTCUT` | 被拦的组合键 | `never` |
| `BAD_ARGS` | 参数问题 | `never` |
| `TIMEOUT` | 等待超时 | `safe` |

## 性能与提速

**每次调用都是一次新进程，启动约 40–60ms**，多步操作慢的主因在这里；但在大窗口（Electron/浏览器，2000+ 元素）上，**元素查找**才是大头。实测（OC Manager，Wails/Chromium 窗口）：

| 操作 | 优化前 | 优化后 |
|---|---|---|
| `find -AutomationId` | ~2000ms | **~400ms**（UIA 原生 `PropertyCondition` 过滤） |
| `find -Name <x> -Exact` | ~2000ms | **~390ms**（同上） |
| `find -Type Button` | ~2100ms | **~650ms**（`ControlType` 条件下推） |
| `find -Name <x>`（模糊包含） | ~2000ms | ~1600ms（UIA 无法表达 contains，只能枚举 + 属性缓存） |
| `pixel`（最小命令） | 67ms | 67ms |
| `windows` | 197ms | 197ms |
| 单次 UIA `click`（`do` 内） | 19–38ms | 19–38ms |
| 5 次 `pixel` 分开调 | 330ms | 330ms |
| 同样 5 次放进一个 `do` | — | **130ms** |
| OCR 全窗 150 行（含 words） | 61.8KB 响应 | `-NoWords` 后 **13.8KB（-78%）** |

**规则：**

1. **多步操作写成一条 `do -Ops`** —— 5 步实测 330ms → 130ms；7 步点击只起一次进程。
2. **优先用 `-AutomationId` 或 `-Name ... -Exact`**，两者走 UIA 原生过滤；**模糊 `-Name` 在 Electron/浏览器窗口上要 1.6 秒**，只在真不知道精确名称时用。
3. **别默认 `tree -Depth 5`** —— WinUI/浏览器上深度 5–6 会拉 200–500 个元素。先 `find` 定位，需要看结构再 `-Depth 2/3`。
4. **OCR 只给区域**（`-X -Y -Width -Height`），比全窗快一个量级；只要行级文本时加 `-NoWords`。
5. **`-Trace` 平时关掉**，排障时才开。

## 工具路由（三堵墙，别硬啃）


| 场景 | 用什么 | 为什么 |
|---|---|---|
| 浏览器页面内操作（登录态、JS 渲染、DOM） | **browser-harness（CDP）** | UIA/MSAA 都摸不到网页正文 |
| Office 文档正文（Word/Excel 内容） | **COM / VBA** | UIA 只能摸到 Ribbon 和对话框，正文需要 Office 对象模型 |
| DirectX 全屏游戏 | **不支持** | 独占全屏下 GDI 截图为黑屏，需 DXGI Desktop Duplication；且 SendInput 会被 raw input 忽略 |
| 桌面窗口/控件/对话框/启动程序 | **win-harness** | 本位面 |

## 已知局限与对策

| 场景 | 现象 | 对策 |
|---|---|---|
| WinUI 应用（Win11 记事本）组合键 | `CTRL+A`/`CTRL+C`/`CTRL+V` 无效 | 用 `setvalue` 直接设值；`read` 直接读文档 |
| 组合键在部分应用失效 | 无反应 | 改用分步单键，或 `setvalue` |
| 目标被其他窗口遮挡 | 纯坐标操作打错窗口且无报错 | `hover`/`drag`/`scroll` 传 `-Window` 先置前台 |
| 剪贴板被其他程序占用 | `type` 报错 | 内置 5 次重试；仍失败稍后重试 |
| 最小化窗口 | UIA 树不可用 | `focus` 自动还原（SW_RESTORE）后再操作 |
| 元素名称含空格/特殊字符 | `-Name` 不匹配 | 用 `tree` 查看精确名称；改用 `-AutomationId` |
| 多显示器 + 缩放 | 截图截错区域（历史故障） | 已修：显式 DPI 声明 + 虚拟桌面截图；留意 `warning` 字段 |
| OCR 返回 `EMPTY_OCR` / 0 行 | 可能是 `-Upscale` 过大（高 DPI 屏） | 降到 `-Upscale 1` 重试；仍空则 `screenshot` 人工核对 |
| OCR 读到的文字不属于目标窗口 | 目标被遮挡，OCR 抓的是屏幕像素 | 先 `focus -Window <定位>` 置前台，或按固定区域 OCR |
| `-Lang` 传了却没换引擎 | 系统未安装该语言 OCR 包，静默退回用户语言 | 看返回的 `engine_fallback` / `engine` 字段，别假设 `-Lang` 生效 |
| **目标以管理员权限运行** | 能读不能驱动；`click` 报 `PERM_DENIED` / `E_ACCESSDENIED`（UIPI 拦截输入） | 从**同级别（提权）终端**启动 win-harness；普通会话永远驱动不了提权窗口 |
| 提权与非提权混用 | 两者不能共用临时/状态目录 | 提权运行时单独指定工作目录 |
| 任务栏 UIA 突然变空 | 开始菜单/弹出层残留把树压制住，`tree` 返回 0 | 发一次 `ESC` 再试（`tree -Window shell:*` 已内置重试）；仍空改用 `ocr` |
| 开始菜单识别不到 | Win11 是 `Wnd_StartFeed`，不是老 `CoreWindow` | 用 `open -Surface start`；失败时先 `surfaces` 看 `start.present` |
| 合成 `VK_LWIN` 不弹开始菜单 | 本机 `SendInput` 发 WIN 键 shell 不响应 | `open -Surface start` 已改为优先点任务栏开始按钮 |

### 已修复的高危问题（避免回归）

| 问题 | 曾经的后果 | 现在的行为 |
|---|---|---|
| 列表项点击走 `Invoke` | 本意"选中文件"变成"**打开文件**"（破坏性且无提示） | `Select` 排前、`Invoke` 排最后；`via` 字段如实汇报通道 |
| autofocus 用合成点击 | `type`/`key` 抢焦点时**清空列表选中状态** | 改用 UIA `SetFocus()`；`type`/`key` 默认不再干预焦点 |
| `launch -WaitWindow` 取首个同标题窗口 | 已有同名窗口时返回**错误的 hwnd/pid** | 优先等本次启动进程自己的窗口；退回时附 `warning` |
| `kill -Path` 取首个同名进程 | 可能杀掉**错误的进程** | 多进程时报错列候选，要求 `-Pid` |
| `ocr` 默认 `-Upscale 2` | 高 DPI 屏上更慢更差，局部小字区域**返回 0 行**而 `ok:true` | 改 `auto`：DPI≥120 取 1；返回 `upscale_mode` 如实汇报 |
| `ocr` 空结果静默 `ok:true` | 被误判为"这里没文字" | 返回 `code:"EMPTY_OCR"` + `warning`，列出遮挡/放大/无文字三种可能 |
| `ocr -Lang` 静默回退 | 传 `en-US` 实际仍用 `zh-Hans-CN`，无从察觉 | 返回 `engine_fallback` + `warning_lang`，并给实际 `engine` |
| `ocr` 最小化/隐藏窗口 | 返回空数组，看起来像工具坏了 | 显式报错"窗口已最小化，请先 focus 还原" |
| 元素/窗口多匹配静默取首个 | 操作到错误元素/窗口且无报错 | 一律报错列候选，要求 `-Index` / `-WindowIndex` |
| 负数坐标解析成"无值开关" | 左/上副屏上 `click`/`hover`/`drag`/`scroll`/`pixel` **全部不可用** | 解析器区分选项与负数；`click` 不再用 `>= 0` 判断是否走坐标 |
| 坐标丢失伪装成元素歧义 | 报「匹配到 N 个元素（无过滤条件）」，排障方向被带偏 | 坐标无效/缺失时直接报坐标错误；无任何条件时明确要求给坐标或元素条件 |
| `setwindow` 的 `applied` 与 `rect` 自相矛盾 | 同一响应两个矛盾矩形（跨 DPI 移动后 1.5x 差异），易误读 | `applied` 只列动作；新增 `requested_rect` + `geometry_note` + `monitor_index` |
| 截图/OCR 只抓屏幕像素 | 目标被遮挡时**静默读到遮挡者的内容**（Navicat 实测：screen 168 行全是别人的字，window 27 行才对） | 新增遮挡检测 `occluded`/`covered_by`；`-Capture window` 走 `PrintWindow` 取窗口自身内容 |
| `-Raise` 用裸 `SetForegroundWindow` | 后台进程调用基本无效，却仍报 `raised:true` | 改用 `Uia.FocusWindow`（ALT 技巧 + AttachThreadInput），实测遮挡 1 → 0 |
| 遮挡把 UWP 自身部件算成遮挡者 | 计算器被自己的框架窗口报 7% 遮挡，误触发窗口抓取 | 同 PID / 同标题 / 父子窗口不计入遮挡 |
| 任务栏等 shell 窗口不在 `windows` 里 | 调用方拿不到 hwnd，只能硬编码，版本一变即失效 | 新增 `shell:` 别名 + `surfaces` + `open`；实测任务栏 27–28 个元素 |
| shell UIA 偶发空树 | 同一 hwnd 先 27 个元素、稍后 0 个，被误判为"没有控件" | `tree -Window shell:*` 内置 3 次重试，仍空则标 `uia(empty-after-retry)` |

## 排障

- **未找到窗口**：`windows` 不带过滤列出全部；注意标题关键词（部分匹配即可）
- **窗口歧义报错**：按提示用 `-WindowIndex n`，或直接用 `hwnd:`
- **元素歧义报错**：按候选列表用 `-AutomationId` / `-Exact` / `-Index` 收敛
- **点击无效**：`getprop` 看 `isOffscreen`/`isEnabled`；屏外元素加 `-Scroll`；或 `-ForceClick` 强制走鼠标
- **重新编译**：修改 `scripts/src/*.cs` 后运行 `scripts/build.ps1`（用 .NET Framework 自带 csc.exe，无需安装工具链）

## 实现说明（维护参考）

- `scripts/win-harness.exe` — 编译产物（单文件，运行时不依赖 PowerShell）
- `scripts/src/Native.cs` — P/Invoke：窗口/鼠标键盘/DPl/剪贴板/MSAA
- `scripts/src/Uia.cs` — UIA 封装 + `FindSpec`/`ResolveOne`（歧义断言）+ MSAA 遍历
- `scripts/src/InputSim.cs` — SendInput 鼠标键盘/剪贴板/截图（含 `PrintWindow` 窗口抓取）
- `scripts/src/Occlusion.cs` — 遮挡检测（网格采样 + 同应用排除）与 `-Raise`
- `scripts/src/ShellSurface.cs` — shell 表面定位（taskbar/desktop/start/overflow/notifications）
- `scripts/src/Codes.cs` — 错误码 / retry 语义 / NDJSON 轨迹日志
- `scripts/src/Ocr.cs` — WinRT OCR 封装（自适应上采样 / 语言回退上报）
- `scripts/src/WinHarness.cs` — CLI 主程序 + 命令分发 + 批处理 + MiniJson
- `scripts/build.ps1` — 编译脚本（UTF-8 BOM 转换 + 退出码检查）
- `scripts/win-harness.exe.bak` — 升级前基线备份
- **为何用 exe 而非 PowerShell 脚本**：脚本内嵌 C# P/Invoke 会触发 AMSI 启发式误拦（ScriptContainedMaliciousContent）；编译为 exe 后二进制不经脚本扫描，稳定可靠
- **为何 JSON 输出 \uXXXX 转义**：规避控制台/管道编码链（GBK/UTF-8 混杂）造成的中文损坏，任何链路下无损
