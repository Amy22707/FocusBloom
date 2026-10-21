# 开发交接：桌面组件视图同步

日期：2026年10月21日

## 完成内容

- 习惯打卡组件新增日、周、月、年视图，原列表和平铺保留。新组件默认日视图，原保存的视图偏好保持。
- 日视图按所选日期展示圆角彩色卡片、累计打卡、连续计划日和完成汇总，可直接翻到过去日期补打卡。列表和平铺同样支持日期切换。撤销前确认，未来与计划外日期不能新增打卡。
- 周视图按习惯和七天展示打卡格，标记居中；月及年热力图展示所选习惯的完成比例。单习惯选择时点击日期直接打卡，多习惯时进入该日列表。主界面及组件双向刷新。
- 习惯组件打开主界面时带入日期和日／周／月／年视图，组件浏览期间主界面日期保持独立。
- 日期事项组件新增分组分列显示，卡片和列表混合显示所选分组，并标明归属；原封面、大字和月历保留。组件设置支持全部或勾选多个分组，与原指定事项选择共同生效。
- 日期分组顺序沿用主界面保存顺序，分组列可滚动、可在该组添加。主界面改名、移动和删除分组后刷新组件，选择范围随改名保存，删除后跟随事项归入“未分组”。

## 实现与数据

`DesktopWidgetWindow.Habits.cs`管理习惯各视图，复用`HabitDayRow`、`GoalRules`和`GoalCompletion`。组件管理器缓存原始打卡数据，业务变化时更新，各次绘图使用缓存。

`DesktopWidgetPreferences.DateGroups`为可选分组名称数组，null显示全部，空数组不显示任何组。`DateEventsViewModel.GroupRemapped`同步组件中的改名／删除选择，分组排序发出数据变更刷新组件。

详情见[桌面组件](desktop-widgets.md)。没有数据库结构变更，原组件位置、尺寸、背景、置顶及筛选偏好保持。

## 验证

Debug 与 Release 构建通过，两套全量测试各 124 项、0 失败。组件视图同步 3 项专项测试通过：习惯组件历史补打卡、撤销、所有视图、单项选择、主题及自定义背景；日期组件分组筛选、改名、移动、删除、顺序及添加入口；原有组件偏好恢复。

最终验证结果为`artifacts/qa/isolated-20261021-122901/results.trx`（Debug）和`artifacts/qa/isolated-20261021-123301/results.trx`（Release）。预览为`widget-habits-*-synced.png`、`widget-habits-day-custom-background.png`、`widget-dates-groups-synced.png`和`widget-dates-mixed-dark.png`。

测试运行于独立后台桌面，活动桌面保持`Default`，没有启动真实用户应用。

## 发布

已覆盖`artifacts/windows-x64/FocusBloom.exe`。发布前只读备份为`artifacts/backups/focusbloom-before-widget-view-sync-20261021-122358.db`。未改写真实业务数据，未提交或推送。

## 后续

邮箱账号及桌面／独立插件基础同步按CloudBase方案推进；Outlook连接仍待应用注册信息。
