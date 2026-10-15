# 开发交接：历史补打卡与完成日历

日期：2026年10月15日

## 完成内容

- 习惯历史月历支持点击过去的计划执行日补打卡，撤销前确认；未来日期不开放打卡。补打卡即时更新连续天数、本月完成率、关联目标及统计。
- 已打卡日期保留实心主题色，去掉数字旁的勾号，数字居中；补打卡后停留在原月份。
- 目标与习惯顶部可选卡片、周、月、年视图，视图选择保存。完成日历按整体或某一条目标／习惯筛选，支持前后翻页、回到今天及年月日期选择。
- 周视图显示七天，月视图按窗口高度安排日期格，年视图用热力图展示全年。色块深浅按完成比例变化，悬停查看进度，点击日期查看明细；单条习惯可直接进入对应月份补打卡。
- 整体完成度取当天各目标与计划内习惯进度的平均值。目标按截至当天的周期进度计算并封顶100%，习惯按打卡与否取0或100%；未来日期和没有计划的日期不计入期间平均完成度。

## 实现位置

`Core/Goals/GoalCompletion.cs`按日汇总完成进度，与`GoalRules.Period`共享周期边界。`GoalsViewModel.Completion`管理日期、筛选和数据，`GoalCompletionView`绘制完成日历，`CompletionDayWindow`显示明细，`HabitHistoryWindow`处理历史打卡与撤销。数据继续使用现有`habit_checkins`，未更改表结构。

规则与说明已同步到[目标与习惯](goals-and-habits.md)。

## 验证与发布

Release编译通过。10项相关检查覆盖规则、存储、统计关联、日完成比例、跨周／跨月周期、补打卡及撤销确认、筛选和三种完成视图，结果位于`artifacts/qa/isolated-20261015-163634/results.trx`。完成日历最终界面检查通过，结果位于`artifacts/qa/isolated-20261015-164217/results.trx`。测试均在独立后台桌面运行，活动桌面保持`Default`。

预览位于`artifacts/qa/goal-completion-*`与`habit-history-*`。未提交或推送。

Release版本已覆盖`artifacts/windows-x64/FocusBloom.exe`。发布前数据备份为`artifacts/backups/focusbloom-before-backfill-calendar-20261015-164332.db`。

## 后续

项目里程碑、成果趋势和统计／目标桌面组件可继续推进；账号／插件同步及Outlook接入按后续模块实施。历史完成度按当前目标与习惯定义计算，修改执行计划会改变计划日期及比例，已有打卡记录保留。
