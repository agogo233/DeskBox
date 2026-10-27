# 更新流程与更新日志 UX 优化方案（参考 PowerToys 源码）

- 日期：2026-10-22
- 状态：待 Simon 拍板分批
- 参考源码：microsoft/PowerToys 浅克隆于 `D:\temp\PowerToys`（本方案所有 PowerToys 引用均出自该树）
- DeskBox 侧现状由本仓库代码梳理（关键文件见第二节）

---

## 一、PowerToys 的更新体系（源码实证）

PowerToys 把更新拆成三个角色：**runner 后台进程**（检查/下载/通知）、**设置 UI**（状态机/浮层/入口）、**What's New 窗口**（更新日志展示），通过一个落盘状态文件解耦。

### 1. 后台检查与下载 — `src/runner/UpdateUtils.cpp`

| 机制 | 实现 | 位置 |
|---|---|---|
| 周期检查 | 常驻线程循环，每 **24h** 检查一次；上次检查时间落盘，按差值补齐睡眠 | `PeriodicUpdateWorker` UpdateUtils.cpp:246-307, 常量 :25 |
| 失败退避 | 拿不到版本信息时 **2h 后再试** | :26, :304 |
| 自动下载 | 设置项 `downloadUpdatesAutomatically`；**按流量计费网络（WWAN/漫游/超限/固定计费）不自动下载** | :265, `IsMeteredConnection` :129-153 |
| 旧包清理 | 下载前 `cleanup_updates()` 清掉历史安装包 | :211 |
| 状态落盘 | `UpdateState`（upToDate/readyToDownload/readyToInstall/errorDownloading/networkError + lastCheckedDate + downloadedInstallerFilename）写入文件，与设置 UI 共享 | common/updating/updateState.h |
| 托盘角标 | `set_tray_icon_update_available(true)` 切换托盘图标为带角标版本 | tray_icon.cpp:534 |

### 2. Toast 通知 — UpdateUtils.cpp:73-112

- 正文：**"有新版本可用" + `当前版本 → 新版本`（带箭头）**（`CurrentVersionToNextVersion` :58）。
- 未自动下载时：单按钮「了解详细信息」→ 协议 `powertoys://open_overview/` 打开设置 Overview 页（更新浮层所在页）。
- 已自动下载完时：按钮升级为「立即更新」→ `powertoys://update_now/` 直接启动独立更新器 `PowerToys.Update.exe`。
- 协议激活在 runner 内分发（main.cpp:440-476），`update_now` → `LaunchPowerToysUpdate`（UpdateUtils.cpp:114-127）。
- 防堆积：所有更新 toast 共用一个 tag，发新的先 `remove_toasts_by_tag` 清旧的。
- 用户可关 toast（设置项 `showNewUpdatesToastNotification`），GPO 可禁用或"落后 ≤2 个 minor 版本才提醒"。

### 3. 设置 UI：状态机 + 常驻徽章 + 浮层卡片

**状态机 `UpdateViewModel`**（`src/settings-ui/Settings.UI/ViewModels/UpdateViewModel.cs`）：

- 7 态：`UpToDate / Checking / NetworkError / ReadyToDownload / Downloading / ReadyToInstall / ErrorDownloading`，叠加瞬态操作（Checking/Downloading/Installing），2 分钟检查超时定时器（:100-104）。
- **主按钮文案随状态切换**（:196-209）：检查更新 / 下载并安装 / 立即安装 / 重试——一个按钮承载全部动作。
- 副标题：有新版本时显示版本号，否则显示"上次检查：xx"（友好相对日期，:154-174）。
- **落盘状态文件 + FileSystemWatcher**（:110-113, :437-455）：runner 每次状态变化，设置 UI 实时刷新，跨进程同步。

**浮层 `UpdateActivityControl`**（`Controls/UpdateActivityControl.xaml`）：

- 挂在设置窗 ShellPage **右下角**（ShellPage.xaml:469-476），440px 宽、圆角 8、`AcrylicInAppFillColorDefaultBrush` 面材 + 1px 描边 + ThemeShadow。
- 出现/消失动画：渐入 + 上移 16px（Implicit.ShowAnimations，0.2s EaseOut Cubic）。
- **可见性策略**（UpdateViewModel.cs:233-235）：状态非 UpToDate 时自动出现；用户点 × 后本窗口会话不再自动弹（`Dismissed`），下次开窗重置。
- 内容 `UpdateStatusControl`（`Controls/UpdateStatusControl.xaml`）：
  - 左侧 20px **状态徽章** + 标题（BodyStrong）+ 副标题（Caption 次级色）+ 可选 Preview 小徽章；
  - 不确定进度条（仅 Checking/Downloading 显示，占 4px 高）；
  - **拉伸满宽的 Accent 主按钮** + 居中「See what's new」文本链接（打开 What's New 窗口）。

**状态徽章 `UpdateStateBadgeControl`**（`Controls/UpdateStateBadgeControl.xaml` + .cs）：

- 20px 圆形渐变底（绿 #6FB538→#397A24 / 琥珀 #FFC328→#FC9A03，浅深色同值、高对比回退系统色）+ Segoe MDL2 图标；
- 5 个 VisualState 映射（.cs:49-59）：最新=绿勾（E73E）、检查中=主题色时钟（E895）、**有更新可装=琥珀叹号（EDB1）**、下载中=主题色下载（E896）、错误=红叉（E711）。

**GeneralPage 版本区**（`Views/GeneralPage.xaml:23-141`）：

- 版本号 + （预览版则）Accent 小徽章 + 状态徽章 + **Accent「检查更新」按钮——点击不是刷新，而是打开右下浮层**（GeneralPage.xaml.cs:102-105 → `OpenUpdateActivity`）。
- 描述行直接是「发行说明」超链接（打开 What's New 窗口）。
- 展开区三个开关：自动下载并安装 / 显示新版本 toast / **更新后显示 What's New**（`ShowWhatsNewAfterUpdates`，默认开）。

### 4. What's New（Scoobe）窗口 — 更新完成后自动弹

**触发**（`src/runner/main.cpp:575-582`）：

- 启动时 `openScoobe = product_version != last_version_run`（落盘记录上次运行版本）；首启 OOBE 优先。
- 设置/GPO 关掉则不弹（:618-625）。
- 顺带：同判据触发"需要重启电脑"toast（:234-243）。

**窗口 `ScoobeWindow.xaml`**：

- 1100×700（Min 480×480）Mica 窗、自绘 TitleBar、NavigationView；
- **左栏 = 版本组列表**：每组显示「December 2025」+「0.96.0」（`ScoobeReleaseGroupViewModel`，按 major.minor 分组）；
- PaneFooter = 「打开设置」入口；
- 内容区：加载 ProgressRing / 失败 InfoBar+重试按钮 / `ScoobeReleaseNotesPage`。

**内容页 `ScoobeReleaseNotesPage.xaml(.cs)`**：

- CommunityToolkit `MarkdownTextBlock` 渲染（表格/任务列表/自动链接全开，字号体系 H1=28 等）；
- 头部 **186px Hero 大图**（从 markdown alt 含 "Hero" 的图片抽出，其余图片留在正文）；
- 数据源：GitHub API `releases?per_page=100` 实时拉（ScoobeWindow.xaml.cs:151），**多版本用 `---` 分隔拼接**、每组标题行带日期 +「View on GitHub」链接；
- 正则清洗：删 Installer Hashes 节、`#PR号` 转超链接（:51-160）；
- 深浅色切换时 Markdown 画笔重刷 workaround（:186-230）。

### 5. 小结：PowerToys 的交互时序

```
后台 24h 检查 ──有新版──→ 自动下载(可选,非计费网) ──→ toast(立即更新|了解详情) + 托盘图标角标
     │                                                        │
     └─ 落盘 UpdateState ──FileSystemWatcher──→ 设置页徽章/浮层实时刷新 ──→ 主按钮按状态驱动
用户点更新 ──→ PowerToys.Update.exe 独立进程安装并重启
新版首启 ──版本变了──→ 自动弹 What's New 窗（左栏版本组 + Markdown + Hero 图）
```

---

## 二、DeskBox 现状（关键事实）

| 环节 | 现状 | 位置 |
|---|---|---|
| 检查触发 | **仅启动一次**（启动模式 45s / 热启 12s 延迟）+ 手动；无周期轮询 | App.xaml.cs:2306-2341 |
| 清单 | 官网 stable.json 优先，GitHub releases 兜底；含 per-locale summary/releaseNotes | AppUpdateService.cs:15-16, AppUpdateManifest.cs |
| 提示 | 系统 Toast（按钮=「去设置页」）+ 托盘菜单项；**设置窗可见时不弹、每进程只弹一次** | App.xaml.cs:3114-3179, App.Tray.cs:1047 |
| 设置页 | About 区 SettingsExpander 三卡：自动检查开关 / 一键更新+View updates / 状态+摘要+下载进度条+官网兜底 | SettingsWindow.xaml:3301-3418 |
| 执行 | 一键=检查→下载(进度+SHA256)→确认 ContentDialog→预更新快照→退出→Updater 静默装→注册表路径校验→Shell 降权重启 | AppUpdateService.cs:145-324, Updater/Program.cs |
| 失败回告 | 取消/失败/路径不符→带参重启弹 ContentDialog；**成功路径零提示** | App.xaml.cs:969-1050 |
| 更新日志 | 独立 ReleaseNotesWindow（820×640 Mica + 自研 Markdown 渲染），**仅手动打开**；数据三级（manifest 内联/磁盘缓存/在线） | ReleaseNotesWindow.xaml(.cs), ReleaseNotesService.cs:44-107 |

DeskBox 基础设施相当扎实（哈希校验、预更新备份、路径一致性校验、Shell 拉回、12 语言安装器参数均强于 PowerToys），**差距集中在"可见性与节奏"，不在可靠性**。

---

## 三、差距分析（体验视角）

1. **长挂进程错过更新**：DeskBox 桌面常驻数天不重启就永远不再检查；PowerToys 24h 周期 + 2h 退避。
2. **更新动作埋得深**：Toast/托盘点击 → 开设置页 → 用户自己找到第三张卡里的按钮；PowerToys toast 按钮=直接动作，设置页按钮=打开浮层即刻可操作。
3. **状态无常驻可见性**：DeskBox 不开设置窗就看不到任何更新状态；PowerToys 托盘角标 + 版本区徽章 + 浮层三处持续在线。
4. **装完零感知（最大缺口）**：DeskBox 成功安装静默回桌面；PowerToys 新版首启自动弹 What's New。DeskBox 已有 ReleaseNotesWindow + 数据链，**只差触发**。
5. **Toast 按钮语义弱**：「Go to update」是导航不是动作。
6. **更新日志单版本视图**：跨多版本累积更新后（用户落后几个版本），没有版本组导航和"这轮更新一共带来了什么"的聚合视角。

---

## 四、方案（分批，待拍板）

### 批 1（P0）：更新后自动弹「更新日志」窗口

- 新增设置字段：`ShowReleaseNotesAfterUpdate`（默认开）+ `LastRunVersion`（CoreSettingsSlice.cs，走 schema 迁移惯例）。
- 启动序列加一步 `RunOptionalStartupStep("whats-new", ...)`：`LastRunVersion != 当前版本` 且开关开 → 打开 ReleaseNotesWindow，然后回写 `LastRunVersion`。
- 更新链路正好是「退出→装→Shell 拉回重启」→ 重启即新版本首启，钩子天然成立；手动装新版同样受益。
- 数据兜底：ReleaseNotesService 三级链已有磁盘缓存；**全新装首启且离线**时显示重试态（参考 PowerToys InfoBar+Retry），不阻塞启动。
- Store 版：同样按版本对比触发（Store 更新不走 Updater，但重启后版本同样变化）。
- 涉及：App.xaml.cs、CoreSettingsSlice.cs、ReleaseNotesWindow（加载失败 UI）、12 语言资源键。

### 批 2（P0）：Toast 从"导航"升级为"动作"

- Toast 双按钮：「立即更新」+「查看更新内容」。
  - 立即更新：走现有 activation 通道打开设置页 About 区，并**自动触发 OneClickUpdate**（检查→下载，完成后弹现有安装确认 ContentDialog）——复用全部现有安全链，只是省掉找按钮那一步。
  - 查看更新内容：直接开 ReleaseNotesWindow（manifest 在手即秒开）。
- 每进程只弹一次的判据从布尔改为**按版本号**记（`_updateNotificationShownVersion`），配合批 3 的周期检查不漏报也不重复。
- 涉及：NativeAppNotificationService、App.xaml.cs:2521-2530/3114-3179、SettingsViewModel.OneClickUpdateActionAsync、资源键。

### 批 3（P1）：24h 周期检查 + 退避

- `ScheduleBackgroundUpdateCheck` 从一次性改为循环：成功后 24h 再查，失败 2h 后重试（照抄 PowerToys 节奏）。
- 尊重现有 `AutoCheckForUpdates` 开关与 `LastUpdateCheckAt` 字段（时间锚点复用）。
- 可选增强（若将来加"自动下载"设置项再做）：PowerToys 式 `IsMeteredConnection`（WWAN/漫游/超限）检测——DeskBox 当前无自动下载，此条挂账不实施。
- 涉及：App.xaml.cs:2306-2341、可能的 System.Threading.Timer 生命周期管理（挂到现有后台步骤框架）。

### 批 4（P1）：状态常驻可见（徽章 + 直达定位）

- 复刻 `UpdateStateBadgeControl`（20px 渐变圆徽 + 5 态 VisualState）放 About 区版本行/状态卡行；状态映射对齐 DeskBox 现有 `Settings.Update.Status.*` 状态集。
- 托盘/Toast 直达时自动展开更新 Expander 并滚动定位到状态卡（确认 `OpenAboutSettingsFromTray` 现有行为，缺定位补定位）。
- 主按钮文案随状态切换：现有 OneClick.UpdateTo/Install/Retry 已基本齐，核对 Checking/Downloading 态禁用逻辑对齐 `CanStartAction` 语义。
- 涉及：SettingsWindow.xaml:3301-3418、新控件文件、主题资源。

### 批 5（P2）：What's New 窗口升级为版本组导航

- ReleaseNotesWindow 左侧加版本列表（PowerToys ScoobeWindow 式：日期+版本号），数据=枚举磁盘缓存目录（`updates\release-notes\*.md`）+ 当前在线 manifest。
- 多版本正文 `---` 分隔拼接，头部显示最新版本 Hero 区（manifest 若将来加图片字段则启用，不强求）。
- 涉及：ReleaseNotesWindow.xaml(.cs)、ReleaseNotesService。

### 不跟的部分（明确记录）

- **GPO/企业策略**（禁用通知/暂停 N 版、预览通道）：DeskBox 无企业场景。
- **独立更新器 UI**：DeskBox.Updater 静默链已更稳（哈希+备份+路径校验+降权重启），无需照搬 PowerToys.Update.exe。
- **设置页右下角浮层卡片**：PowerToys 用它是因为状态由另一进程驱动需要浮层承接；DeskBox 单进程、更新卡就在设置页内，浮层收益低、噪光大，不引入。
- **托盘图标本体换角标**：DeskBox 托盘是自绘菜单体系，换图标成本高于收益，靠托盘菜单项「Update x.y.z available」承担同等职责（已有）。

### 实施注意

- 新 XAML 控件/绑定过 AOT 清单惯例（镜像清单 + 冒烟清单同步）。
- 新设置字段走 slice 迁移；设置代码读 slice 非门面（既有坑）。
- 12 语言资源键全量；日期显示用相对/本地化格式与现有 `Detail.CurrentVersion` 一致。
- 批 1 与批 2 可并行开发；批 3 建议在批 2 的"按版本去重"落地后再上，避免重复打扰。

---

## 六、三渠道兼容性矩阵（Win10 直发 / Win11 直发 / 商店）

### 新发现的前置缺陷：Store 版更新日志窗拿不到内容（现状 bug）

- `StoreAppUpdateService.CheckForUpdatesAsync`（StoreAppUpdateService.cs:29-77）在商店有待更新时**合成**一个 manifest：`DownloadUrl = "ms-windowsstore://downloadsandupdates"`、`Summary = []`、无 releaseNotes/Url；无待更新时直接返回 UpToDate（无 manifest）。
- `FetchLatestManifestForReleaseNotesAsync`（AboutAndUpdates.cs:215-242）过滤条件 `AppUpdateService.IsManifestUsable`（AppUpdateService.cs:481-491）要求 DownloadUrl 为 **deskbox.fun / GitHub releases 的 https 安装包直链**——商店协议 URI 恒 false。
- 结论：**Store 版无论有无待更新，View updates 打开的 ReleaseNotesWindow 都解析不到 manifest**，三级数据链（manifest 内联→磁盘缓存→在线 Url）三级全空（商店版也从未产生过 Direct 下载缓存）。
- 修法（批 1/5 的前置项，单独成小批）：给 ReleaseNotesService 增加独立于安装判定的**内容型 manifest 拉取**——直接读官网 `stable.json`，只取 `version/summary/releaseNotes/releaseNotesUrl` 展示字段，不过 `IsManifestUsable` 安装门（该门的职责是钉死安装包源头，不该管纯文本展示；Direct/Store 通用，安全面零扩大）。或备选：Store 合成 manifest 时补 `ReleaseNotesUrl` 走第三级在线链（改面更小但 Store 依赖网络）。

### 逐批 × 三渠道矩阵

| 批 | Win10 直发 | Win11 直发 | 商店版 |
|---|---|---|---|
| 批 1 自动弹日志窗 | ✅ 触发钩子=更新器 Shell 拉回重启后版本变化；失败重启版本未变→不弹，天然正确 | ✅ 同左 | ⚠️ **依赖前置修复**（上节）。触发本身成立：MSIX 商店更新在下次启动生效，版本变化可检测。首装（LastRunVersion 空）/onboarding 期间必须跳过——DeskBox onboarding 承担首启引导，对应 PowerToys「OOBE 优先于 Scoobe」规则 |
| 批 2 Toast 动作化 | ✅ AppNotification 按钮激活链已在 Win10 生产验证（现有 Go to update 按钮）；焦点助手/全屏吞 toast → 托盘菜单项兜底已存在 | ✅ | ➖ 天然不涉及：商店版后台检查入口直接 return（App.xaml.cs:2308），无 toast 可改；商店更新交给 Store UI 是既定产品决策，不破例 |
| 批 3 24h 周期检查 | ✅ Direct-only。注意睡眠/恢复：定时器在睡眠期不计时，恢复后应按 `LastUpdateCheckAt` 锚点**重算剩余等待**（PowerToys 循环醒来先读落盘时间补差，UpdateUtils.cpp:250-262），不做盲 24h 延时。DNS 失败（deskbox.fun 不通是既有实况）走 2h 退避，周期重试反而改善现状 | ✅ 同左 | ➖ 同批 2，商店版不进检查循环 |
| 批 4 徽章+直达定位 | ✅ 纯 XAML：渐变画笔/VisualStateManager/Segoe MDL2（E73E/E895/E896/EDB1/E711 全部为 Win10 时代标准字形）；ScrollIntoView+Expander 展开无平台差异 | ✅ | ⚠️ 状态集不同：商店版只有 Ready/Checking/UpToDate/Store 失败态（`Settings.Update.Status.Store*`），无下载三态——徽章映射需为商店版单列（或商店版仅 UpToDate/Checking 两态显示），随批实现 |
| 批 5 版本组导航 | ✅ 枚举磁盘缓存目录+在线 manifest | ✅ | ⚠️ 同批 1 前置：内容源修复后，磁盘缓存级在商店版恒空（无下载历史），只剩 manifest 内联+在线两级，可接受 |

### 渠道专项注意

**Win10**：
- 提权运行机器（EnableLUA=0 全提权族）：启动自动开日志窗、toast 激活、周期检查均无 UIPI 障碍（toast 激活由系统完成）；更新安装链在提权下反而更顺（本就要提权装），Shell 拉回已有降权处理。
- 批 1 弹窗时序：挂 `RunOptionalStartupStep` 框架带延迟，不与 onboarding、update-install-result 失败弹窗（`QueueUpdateInstallResultDialog`）抢前台——失败场景版本未变天然互斥，但需与 onboarding 判定显式互斥（先判 onboarding 再判 What's New）。
- NativeAOT：批 4 新控件为纯 VSM+静态资源，反射面为零，AOT 风险最低；仍按惯例过镜像清单。

**Win11**：无渠道特有风险。亚克力采黑族（#521/#730，26200）与 Mica 相关组件无关（日志窗/设置页均为既有窗口）；不动 backdrop。

**商店版（MSIX）**：
- 文件虚拟化：release-notes 缓存目录 `%LOCALAPPDATA%\DeskBox\updates\` 在商店版写入会被虚拟化到包 LocalCache——**自写自读透明**（同进程同路径视图一致），批 5 枚举同路径同样一致；本方案不新增注册表写入（注册表虚拟化是既往坑，不触雷）。
- 商店更新节奏不受我们控制（Store 客户端策略），批 1 在商店版的弹窗时机=「商店装完后的下一次启动」，语义正确。
- `LastRunVersion` 存设置文件：Direct 在 %LOCALAPPDATA%、商店在包 LocalState，各自一致，无跨渠道串味。

