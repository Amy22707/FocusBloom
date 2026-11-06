# FocusBloom for macOS

一刻专注 macOS 版是一款离线专注和计划工具，支持 macOS 14 Sonoma 或更新版本。下载包自带运行时。

## 下载与安装

1. 在 [GitHub Releases](https://github.com/Amy22707/FocusBloom/releases/tag/v1.0.1) 下载对应的 DMG。Apple 芯片选择 `FocusBloom-macOS-arm64.dmg`，Intel 选择 `FocusBloom-macOS-x64.dmg`。可以在「苹果菜单 → 关于本机」查看芯片或处理器。
2. 双击 DMG，把 `FocusBloom.app` 拖入「应用程序」，然后从「应用程序」打开。
3. 此下载使用本地签名，没有 Apple Developer ID 签名或公证。若 macOS 阻止首次打开，先尝试启动一次，再到「系统设置 → 隐私与安全性」点「仍要打开」。参见 [Apple 的打开说明](https://support.apple.com/zh-cn/102445)。

## 功能与资料

- 正计时、倒计时、番茄循环、暂停与休息，支持中断恢复、全屏、截图和自定义专注素材。少于 10 秒的专注不计入历史，正计时达到 24 小时后自动结束。
- 项目、待办和日／周／月计划本。从事项开始专注时，专注记录保留其关联名称。
- 月历、日期事项、学期课表、CSV 课程导入和 ICS 导出。
- 习惯打卡、目标、心情月历、图文复盘、专注历史与统计。
- 中英文、跟随系统明暗模式、手动和自动备份。

资料目录为 `~/Library/Application Support/FocusBloom`。在 Windows 版创建 `.fbbak`，复制到 Mac 后可从「偏好设置 → 数据备份与恢复」导入。恢复前会保存当前资料。备份包含自定义背景、声音、句子和复盘照片。

Mac 版的专注计时可搭配 Chrome、Edge 或 Firefox 的独立扩展限制网站。Windows 的应用限制、内置学习浏览器和桌面组件目前属于 Windows 版功能。Mac 版支持图片背景，视频背景和系统提醒目前只保留备份数据。

## 构建与验证

在 macOS 上安装 .NET 10 SDK，按芯片传入 `arm64` 或 `x64`：

```sh
bash scripts/macos/package.sh arm64 artifacts/macos
bash scripts/macos/verify.sh artifacts/macos/FocusBloom-macOS-arm64.dmg
```

`macOS packages` 工作流在两种芯片的 macOS 运行器上各自打包。验证会挂载 DMG，把 `.app` 复制到带空格的目录后启动，并测试计时门槛、事项身份和 Windows 备份恢复。两种架构均通过后才把生成的 DMG 发布到 GitHub Release。
