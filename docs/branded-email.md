# FocusBloom 验证码邮件

发件人显示名称使用`FocusBloom`，标题使用`【FocusBloom】邮箱验证码`。正文使用[验证码邮件模板](../cloud/email/verification.html)。

在CloudBase控制台进入“身份认证 → 登录方式 → 邮箱验证码”，配置自定义SMTP发件邮箱，并在邮件模板中设置标题和HTML正文。发件地址使用实际拥有的邮箱；使用品牌域名邮箱时，按邮件服务商要求配置域名验证、SPF和DKIM。

SMTP主机、端口、账号和授权码只保存在云端控制台，不填写到桌面或扩展配置，也不提交到源码仓库。配置成功后，桌面与扩展继续使用现有验证码接口。

正文中的`{{.VerificationCode}}`和`{{.ExpireMinutes}}`分别由CloudBase填入验证码和有效分钟数。配置后通过控制台测试邮件核对发件名称、标题、验证码和有效期。

官方说明：[邮箱登录与自定义SMTP](https://docs.cloudbase.net/authentication-v2/method/email-login)、[验证码接口与模板变量](https://docs.cloudbase.net/http-api/auth/auth-send-verification)。
