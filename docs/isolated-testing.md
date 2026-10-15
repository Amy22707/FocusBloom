# 独立桌面测试

日期：2026年10月15日

界面测试使用独立Win32桌面，不切换用户正在使用的桌面。测试宿主确认当前线程属于本次创建的测试桌面，并且与输入桌面不同，再创建WPF应用和窗口。测试结束关闭所属窗口与桌面句柄。

```powershell
./scripts/dev.ps1 test -Configuration Release -Filter 'FullyQualifiedName~GoalTests|FullyQualifiedName~GoalsUiTests'
```

需要先编译后直接运行时：

```powershell
./scripts/test-isolated.ps1 -Configuration Release -Filter 'FullyQualifiedName~GoalsUiTests'
```

`test-isolated.ps1`将子进程分配到`WinSta0\FocusBloomTests-*`桌面，启动隐藏控制台进程，输出保存在`artifacts/qa/isolated-*/output.log`与`results.trx`。不调用桌面切换API，不发送全局键盘或鼠标输入，测试前后记录输入桌面名称。WPF测试宿主拒绝在普通工作桌面启动。

涉及真实鼠标／前台应用拦截／系统通知投递的用例标记`DesktopMode=Interactive`，常规隔离入口排除这些用例，需要独立交互会话另行安排。独立桌面不用于隔离系统通知、声音或其他全局设备，本次目标界面流程使用临时数据库、普通计时且不创建原生通知服务。

实测测试窗口位于`FocusBloomTests-*`，WPF窗口可创建并渲染，输入桌面前后均为`Default`。机制验证记录在`artifacts/qa/desktop-isolation.txt`；每次运行结果目录记录本次桌面与输入桌面状态。

API依据：[Microsoft CreateDesktop](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-createdesktopw)、[线程与桌面连接](https://learn.microsoft.com/en-us/windows/win32/winstation/thread-connection-to-a-desktop)。
