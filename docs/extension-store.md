# FocusBloom 扩展商店提交说明

## 提交包

运行 `scripts/publish-store-extension.ps1`。输出 `FocusBloom-chromium.zip` 用于 Chrome 和 Edge，`FocusBloom-firefox.zip` 用于 Firefox 桌面版。Firefox 使用模块化后台脚本和固定扩展 ID，最低版本为 Firefox 140。商店包使用本机功能，开发用的 CloudBase 配置及账号联调文件留在开发包。

## 审核说明

扩展用于专注计时和专注期间的网站限制。可以无账号验证正计时、倒计时、番茄循环、暂停和恢复、记录、黑名单与白名单以及外观设置。

| 权限 | 用途 |
| --- | --- |
| `alarms` | 浏览器挂起后台时仍能按实际期限恢复计时状态 |
| `storage`、`unlimitedStorage` | 本机保存设置与长期专注记录 |
| `tabs` | 检查已打开的标签页、将受限页面切换到拦截页、解除限制后恢复原网址 |
| `declarativeNetRequest` | 在浏览器中执行主页面导航规则 |
| HTTP 与 HTTPS 站点访问 | 网站名单由用户决定，规则需要覆盖任意站点。拦截只作用于主页面导航 |

全部 JavaScript 随包分发。只有 `blocked.html` 可从受限网站跳转。商店版无账号、分析或远程数据传输，Firefox 清单声明 `none`。商店隐私链接使用公开的中英文说明，并按各平台的口径申报本机网址处理。[FocusBloom 扩展隐私说明](https://github.com/Amy22707/FocusBloom/blob/main/docs/extension-privacy.md)

0.3.7 已在独立配置中实测 Chrome 156、Edge 155 和 Firefox 159，覆盖加载、语言和外观切换、开始和结束计时、拦截本地测试网页以及解除后恢复网页。三家商店分别审核。

Chrome 和 Edge 使用各自的开发者后台提交 ZIP，填写用途、权限说明、数据声明和宣传截图。Firefox 提交独立 ZIP 供 AMO 验证、签名和审核。图标署名为 @芋鱼_yu，提交前确认商店分发授权。

商店短介绍可用「用专注计时记录投入，并在专注期间按自定义规则限制网站。」英文可用 “Time your focus sessions and limit access to distracting websites with your own rules.”

- [Chrome Web Store 开发者政策](https://developer.chrome.com/docs/webstore/program-policies/policies/)，[Manifest V3 额外要求](https://developer.chrome.com/docs/webstore/program-policies/mv3-requirements)，[数据披露](https://developer.chrome.com/docs/webstore/program-policies/disclosure-requirements)
- [Edge Add-ons 发布说明](https://learn.microsoft.com/en-us/microsoft-edge/extensions/publish/publish-extension)
- [Firefox 附加组件政策](https://extensionworkshop.com/documentation/publish/add-on-policies/)，[数据传输声明](https://extensionworkshop.com/documentation/develop/firefox-builtin-data-consent/)，[提交与签名](https://extensionworkshop.com/documentation/publish/submitting-an-add-on/)
