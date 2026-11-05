# FocusBloom 跨平台可行性

| 平台 | 可复用 | 需要验证或重做 |
| --- | --- | --- |
| Safari（macOS、iOS、iPadOS） | 浏览器扩展的计时、记录、规则编辑和界面 | 用 Xcode 将 WebExtension 打包进宿主应用。分别验证后台休眠恢复、导航拦截和用户授权，随后签名并通过 App Store Connect 分发。 |
| macOS 桌面 | .NET 中与系统无关的计时、清单和数据逻辑 | WPF 界面、托盘、通知和 Windows 应用限制要分别移植。先验证无严格模式的离线应用，再用原型确认 macOS 能提供什么限制行为。 |
| iOS | 计时与清单规则、备份格式和术语 | 需要移动界面和后台计时恢复方案。系统级应用限制应单独验证 Screen Time API，发行前需申请 Family Controls 权限。 |
| Android | 计时、清单及本地数据模型 | 需要移动界面、通知与后台恢复。UsageStatsManager 可读取使用情况。若采用 AccessibilityService 实现应用拦截，需要按 Google Play 要求申报、说明用途并取得同意。 |

Safari 扩展是与现有实现最接近的一条线。iOS 和 Android 可以先做计时、周期清单和记录，把应用限制单独验证。macOS 桌面版需要先确定新界面框架和可实现的限制方式。

- [Apple Safari 扩展开发与分发](https://developer.apple.com/safari/extensions/)
- [Apple Family Controls 权限](https://developer.apple.com/documentation/familycontrols/requesting-the-family-controls-entitlement)
- [Microsoft WPF 平台说明](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)
- [Android UsageStatsManager](https://developer.android.com/reference/android/app/usage/UsageStatsManager)
- [Google Play AccessibilityService 政策](https://support.google.com/googleplay/android-developer/answer/10964491)
