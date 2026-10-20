# 开发交接：习惯日视图与拖动显示

日期：2026年10月21日

## 日期居中

日期输入框统一水平、垂直居中，沿用各页面原有紧凑尺寸。`DatePickerNavigation`加载时应用居中，以覆盖系统日期模板的纵向拉伸设置。日历月表头、周视图及课表日期表头同样居中。

Release编译及独立后台桌面专项检查通过，覆盖习惯、心情、日期编辑窗口、周视图与课表，浅深主题。结果：`artifacts/qa/isolated-20261021-042704/results.trx`。已覆盖`artifacts/windows-x64/FocusBloom.exe`；只读备份：`artifacts/backups/focusbloom-before-date-alignment-20261021-042741.db`。

## 习惯日视图与交互

- 目标与习惯增加“日”视图。可用前一天、后一天、今天按钮和日期选择器翻日，点击过去计划执行日的卡片直接补打卡，撤销前确认。
- 卡片使用30圆角、稳定配色和明显完成色块，展示截至所选日期的累计打卡天数及连续完成计划日。顶部汇总当天完成数、比例和鼓励文字；完成后有轻量缩放反馈。宽窗口双列、窄窗口单列，排序、卡片大小和滚动位置保留。
- 目标按所选日期显示进度，补打卡同步更新目标、复盘和统计。未来、开始前和计划外日期不能新增打卡；查看历史时跨午夜保持日期，今天页跨午夜跟随新一天。
- 新配置默认日视图，已有`goals.view`偏好保持；卡片、周、月、年继续可用。新增`GoalsViewModel.Daily`，沿用现有习惯与打卡数据，没有数据库结构变更。

## 拖动与滚动条

- `ReorderPreview`读取系统鼠标屏幕坐标后转换到窗口内容区，卡片和分组共用；绘制限制在内容区域，结束后清理预览与动画。
- 细滚动条可见线约3像素，鼠标移入或拖动期间显示，移开隐藏。透明操作区保留，避免隐藏时布局变化；`ScrollBarChrome`去除横竖交汇处的系统白角。
- 底部默认的“选择一个项目，开始这一刻。”已移除，其他实际状态提示保留。

## 验证与发布

Release编译通过。日视图补打卡、撤销确认、累计与连续更新、目标关联、休息日和未来限制、跨午夜、排序、浅深主题及紧凑窗口通过专项验证。原卡片及周月年检查通过。

最终日视图界面：`artifacts/qa/isolated-20261021-041459/results.trx`；细滚动条与透明交汇角：`artifacts/qa/isolated-20261021-041713/results.trx`。窗口移动后的拖动预览位置、预览清理、跨组拖动与分组排序专项通过。所有界面验证使用独立后台桌面，活动桌面保持`Default`。

预览：`habits-day-*`、`drag-preview-*`、`scrollbars-thin-hover-*`。

已覆盖`artifacts/windows-x64/FocusBloom.exe`，未启动应用。发布前只读备份为`artifacts/backups/focusbloom-before-habit-day-drag-fix-20261021-042009.db`。没有改写真实用户数据，未提交或推送。

## 完成内容

- 日期分组标题增加拖动手柄，支持左右排序并保存。默认组也可排序；范围选择和事项编辑中的分组列表使用同一顺序。
- 日期事项增加“混合查看”，可勾选参与显示的分组，按置顶和日期排列，卡片显示所属分组。混合模式、范围及顺序均保存。
- 心情保留六种：兴奋、愉快、平静、疲惫、低落、烦躁；图标采用统一的彩色矢量表情，选择器、月历、当天详情和桌面组件一致。记录编号继续使用原值，复盘文字保留。
- 心情与复盘可切换月视图和每日记录。每日以日期、心情、文字及图片为主，空段落隐藏，投入摘要可展开。月历按窗口高度调整，并标记图片数量。
- 每日记录支持本地图片导入及Ctrl+V粘贴，自动保存到当前账户数据目录的`reflection-images`。原始文件保留，可查看大图和打开本地目录；取消编辑清理新增草稿副本，移除图片需确认，尚未保存的编辑不会改动记录。支持仅图片记录。
- 本地备份包含日记图片，恢复后改写照片路径。旧JSON没有照片字段时按无照片读取。

## 入口与数据

`DateEventsViewModel`保存`dates.groupOrder`、`dates.mixedView`和`dates.mixedGroups`。日期分组排序复用`ReorderButton`，支持横向边缘滚动。

`ReflectionRules.Moods`提供六种名称、颜色与趋势区间；`MoodIcon`绘制矢量表情。日记视图偏好为`reflection.view`。

`DailyReflection.Photos`为可选的本地路径数组，不改变SQLite表结构。`ReflectionImages`保存上传副本和粘贴PNG，缩略图异步读取；`PhotoViewerWindow`展示大图。备份服务打包并改写`daily_reflections`中的照片路径。

界面说明见[工作界面](workspace-ui.md)。

## 前次验证

Release编译通过。针对性检查覆盖分组拖动、混合筛选、偏好恢复、心情名称及图标、旧记录保留、照片本地副本、上传和粘贴保存管线、仅图片记录、草稿取消、移除确认、备份恢复、日／月导航和既有复盘流程。

日期布局结果为`artifacts/qa/isolated-20261021-030640/results.trx`；最终图片界面及既有复盘流程为`artifacts/qa/isolated-20261021-033423/results.trx`；照片备份恢复专项通过。测试在独立后台桌面运行，活动桌面保持`Default`，没有改动用户剪贴板。

预览：`date-groups-*`、`mood-six-*`、`reflection-diary-photos-*`、`reflection-month-photos.png`。窗口截图按实际内容区域输出。

## 前次发布

已覆盖`artifacts/windows-x64/FocusBloom.exe`，未启动应用。发布前只读备份为`artifacts/backups/focusbloom-before-reflection-photos-20261021-033605.db`。没有改写真实用户数据，未提交或推送。

## 后续

邮箱账号及桌面／独立插件基础同步按CloudBase方案推进；Outlook连接仍待应用注册信息。
