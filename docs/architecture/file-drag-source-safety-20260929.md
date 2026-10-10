# 文件拖出原件保护

用户报告 DeskBox 1.5.5 文件拖到微信后找不到，回收站也没有。缺少该用户的诊断包、原路径和复现步骤，具体删除或移动原因尚未确认。用户确定的最高要求是文件不能丢失。

本文件原记录 2026-09-29 第一批"全部 Copy-only"止血实现。2026-09-30 起，源端改造为 `StartDragAsync` 发起 + 对外公告的最终形态（方案 B）：允许集与默认偏好解耦，且不依赖 DeskBox 事后核验删除。公告形状按 OS 分档（2026-10-01 二次修复定稿，见文末 Win10 专项清单）：**Win11 公告 `Copy | Move` + 偏好档位**（FollowWindows 无偏好走原生卷规则，同卷移动/跨卷复制、修饰键生效）；**Win10 公告集整体收单比特**（FollowWindows/Move 档=仅 `Move`，Copy 档=仅 `Copy`，即 1.5.5 已验证不弹菜单的形态），preferred 偏好在 Win10 无意义（到达不了 brokered drag）保持 `None`。第三批"复制+核验+回收"实验已整体移除。

## 当前实现

- `FileItemDragPackage` 在 `DragStarting` 决定公告与偏好（两者均按 OS 分档，`ResolveDragOutAllowedOperations` / `ResolveDragOutPreferredOperation`，OS 开关可注入供测试）：**Win11** 公告 `AllowedOperations = Copy | Move`，偏好来自拖出三档设置——`FollowWindows`（默认）解析为 `None` 不给偏好（目标走原生卷规则），`Move`/`Copy` 写单值偏好；**Win10** 公告集收单比特（`FollowWindows`/`Move`→仅 `Move`、`Copy`→仅 `Copy`），偏好恒 `None`——Win10 shell 对**多于一个公告效果**的 brokered 拖放每次落放都弹复制/移动菜单且 preferred 到不了它，单效果公告是唯一静默形态（1.5.5 出货即此形状）。由容器级 `CanDrag`（`UIElement.StartDragAsync`）发起，`ListViewBase.CanDragItems` 已关闭。已知代价：Win10 的 Move/FollowWindows 档会被 copy-only 接收端（VS Code、Chromium、WinForms）拒收——设置页在 Win10 显示说明行（`Settings.DragOutAction.Win10Note`）。
- 原生 Shell 载荷始终经过 `FileDragSourceGuardDataObject`：Preferred DropEffect 只在写入值为单一位（Copy/Move/Link）时由包装层存储并回答（内层对象不接受该格式写入）；`DROPEFFECT_NONE(0)` 与多位掩码是引擎的"无偏好"拼法，包装层保持该格式不存在——与从不写偏好的 Shell 原生源完全一致，规避"格式存在但值为 0"在 Win10/第三方目标上的歧义解释；后续写入不可服务值也会清掉先前的有效偏好。Performed DropEffect、Logical Performed DropEffect、Paste Succeeded、TargetCLSID 等完成回执被消费且只记录日志，永不抵达内层 Shell 对象。正确处理 `fRelease` 的存储介质所有权。
- DeskBox 不根据 `DropResult` 或任何回执删除源文件。目标按公告的 `Move` 自行完成移动（Explorer 的原生移动语义）时，撤销语义是"撤销移动"，文件回到原处。
- DeskBox 内部文件传输使用私有文件载荷确定逻辑意图；跨格、文件夹、叠放导入继续遵循 Ctrl/Shift 和既有移动策略。传输许可和拖放回执分开：内部路由反馈 `Copy`/`Link`，完成永不返回 `Move`。
- 便签和待办附件继续建立关联，对内部文件拖拽使用 `Copy` 回执。
- 外部完成处理只按成功目录枚举和存在性复查刷新列表（`ObserveExternalDragOutAsync`）；`missingOrUnavailable` 不是删除成功证明，也不触发自动补删或恢复。
- 外部拖出完成后登记桌面到达豁免（`SuppressDraggedArrivals`），防止桌面自动整理把刚落桌面的文件收回格子；待决条目寿命 = 当前整理延迟档位 + 60min 落地余量（覆盖 ≥5min 档位下的延迟物化），指纹条目额外给 60min settle 余量；登记时同时记录 `(2)`/`(3)` 等冲突改名候选；待决条目要求"创建于登记后、登记后落盘或源已消失"的到达证据，且证据不足时保留条目重试；同路径跨操作的声明取更强语义（指纹 > 待决）而非盲覆盖；豁免随恢复存储持久化，重启不丢（`LedgerPath`）。

## 明确的体验变化与边界

- 拖到桌面/Explorer：Win11 与资源管理器一致的原生移动——同卷改名、跨卷复制、Ctrl/Shift/冲突对话框/Ctrl+Z 全部系统原生。Win10 因公告集收单比特：Move/FollowWindows 档落放即移动（**跨卷也是移动**=copy+删源，等价资源管理器跨卷移动语义；且 copy-only 接收端会拒收该拖放）；想要复制须把设置档切到"复制"（公告集变 Copy），in-drag Ctrl 在 Move-only 公告下救不回。
- 拖到微信等第三方：看到与从资源管理器拖出相同的允许集；即使对方误报 `Move`，DeskBox 也不删源。残余风险是接收方主动修改或删除原路径——与从资源管理器拖出同等，DeskBox 无法也不应额外防护。
- `Link` 不对外公告（XAML 把 `Copy|Move|Link` 全选当无偏好）；Alt 拖出创建快捷方式与任务栏固定暂不支持，需要时可用 `Microsoft.UI.Input.DragDrop.DragOperation` 自发起补齐。
- 源文件真实路径仍通过 CF_HDROP 交给目标；不把原件设为只读、不复制大文件到 UI 线程、不复活中途接管 DoDragDrop 的旧实验（`NativeFileDragOut` 已删除）。

## 验证

`FileDragSafetyTests` 覆盖：普通文件和快捷方式收到虚假 `Move` 完成、跨父目录 StorageItems 回退、原生完成回执格式不抵达 Shell 对象、`fRelease` 两种所有权、Preferred DropEffect 单值存取、0/多位值永不回答且会清掉先前偏好、CF_HDROP 可读，以及内部移动意图和对外回执分离。`FileItemMultiDragTests` 锁定 `Copy | Move` 公告与"反馈/完成永不 Move"。`DesktopAutoOrganizationSuppressionRegistryTests` 覆盖拖出到达豁免的指纹、待决、过期与到达证据。

## 验收条件

1. 可控 OLE 接收端与临时文件的真实鼠标矩阵：只读后误报 Move、取消、拒收、延迟读取、退出、多选部分完成，覆盖 Win10/Win11 和两种叠放模式。微信只用一次性测试文件，不以成功发送作为唯一验收；原路径与内容哈希必须保持。
2. 桌面/Explorer 拖出按资源管理器语义验收：同卷移动、跨卷复制、修饰键、冲突对话框、Ctrl+Z 撤销移动。
3. 正式包的 Native AOT、ARM64 和 Store 验证独立于 Debug/x64 测试；未经真实拖放验收，不将本改动称为故障已完全解决。

### Win10 专项清单（2026-09-30 双审查 P1-6 → 09-30 真机证伪 → 10-01 二次修复定稿）

**真机结论（2026-09-30，anim2 AOT 包，Win10 19045，诊断包 20260930-234554）**：`FollowWindows` 无偏好形态在 Win10 弹三态菜单（日志三次会话 `requested=None allowed=Copy, Move`，用户菜单选"复制到当前位置"后 `dropResult=Copy`）。同包还暴露 AOT 桥白名单漏 4 个拖出绑定（设置下拉框空白），已修。**修复两版**：第一版（dragfix 包）= Win10 钳单值 `Move` preferred——被真机否定，preferred 到不了 Win10 的 brokered drag，菜单照弹；第二版（dragfix2 包，定稿）= `ResolveDragOutAllowedOperations` 把 Win10 公告集整体收单比特（FollowWindows/Move→仅 `Move`、Copy→仅 `Copy`，即 1.5.5 出货已验证不弹菜单的形状），preferred 恢复全域 `None`。完整排查链见 `docs/articles/win10-drag-out-incident-20260930.md`。以下清单待 dragfix2 复测：

- [ ] Win10 21H2+ `ManagedDragOutAction=FollowWindows` 拖出到桌面/Explorer：不弹操作菜单，同卷执行移动。
- [ ] Win10 `FollowWindows`/`Move` 档**跨卷**拖出：不弹菜单，执行移动（copy+删源）——单比特公告的语义变化点，重点验收；in-drag Ctrl 救不回复制，须切"复制"档。
- [ ] Win10 copy-only 接收端（VS Code / Chromium 浏览器 / WinForms 程序）：Move/FollowWindows 档拖放被**拒收**是已知代价，切"复制"档可达——各验一处即可。
- [ ] Win10 拖出到微信（一次性测试文件）：不弹菜单、文件完整到达、源不受影响。
- [ ] Win10 `ManagedDragOutAction=Copy` 档：公告仅 Copy，落放即复制、不弹菜单。
- [ ] Win10 格子间内部拖放：跨格移动、叠放进出、格内重排正常（公告集单值后的内部路由——反馈单值 Move 临时回退、完成永不 Move）。
- [ ] Win10 设置页"拖出格子时"下拉框显示"跟随 Windows 默认"、展开三项齐全、两个拖出提示开关可用、Win10 说明行显示（AOT 桥修复的验收点，Debug 构建验证不了）。
- [ ] Win11 同矩阵对照一遍（FollowWindows 仍无偏好走卷规则，不弹菜单；显式 Move/Copy 档偏好生效）。
- [ ] 叠放弹层（StackPopover）开着时从 Explorer 拖文件入弹层：确认外部拖入可达（独立 HWND 依赖 WinUI 自注册 OLE，代码无法静态证实）。

## 依据

- [微软 Shell 传输场景](https://learn.microsoft.com/en-us/windows/win32/shell/datascenarios)
- [Shell 文件格式与完成回执](https://learn.microsoft.com/en-us/windows/win32/shell/clipboard)
- [SetStorageItems](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.datatransfer.datapackage.setstorageitems)
- [DataPackage.RequestedOperation](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.datatransfer.datapackage.requestedoperation)
- [UIElement.StartDragAsync](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.startdragasync)
- WinUI 3 源码（`UIElement_Partial_DragDrop.cpp`、`ListViewBase_Partial_Reorder.cpp`）：`StartDragAsync` 传递 `AllowedOperations`，`ListViewBase` 内置拖拽不传递。
