# Microsoft Store 提交检查清单

## 打包前

- [ ] Microsoft Store接受包含WPF在内的桌面应用程序包。此流程提交编译后的应用文件和商店资料，不要求公开源代码，也不要求修改项目许可证。参见[微软桌面应用打包指南](https://learn.microsoft.com/zh-cn/windows/msix/desktop/desktop-to-uwp-packaging-dot-net)。
- [ ] 在Microsoft Partner Center中预留最终产品名称和程序包身份。打包参数中的`IdentityName`必须与预留身份完全一致。
- [ ] 从Partner Center复制准确的`Publisher`值；它与面向用户显示的发布者名称不是同一个字段。
- [ ] 确认最终产品名称和发布者显示名称。最终品牌确定后，将其传入打包脚本。
- [ ] 在Partner Center中核对商店文案、隐私问卷、销售地区、年龄分级和支持网址。只有真实公开网址可用后再填写支持网址。
- [ ] 构建Release独立版客户端，确认`ClientDirectory`包含`YingView.exe`、`LICENSE`、`NOTICE`、`THIRD-PARTY-NOTICES.md`和完整的发布运行文件。
- [ ] 选择以`.0`结尾的四段版本号，例如`0.2.0.0`，并与应用版本保持一致。

## 创建并检查程序包

在项目根目录打开PowerShell，把占位文字替换成Partner Center中的真实值：

先运行`./scripts/build.ps1 -ClientLanguage auto`构建商店通用客户端，确认`client-language.json`中的语言为`auto`。商店脚本不接受固定语言的下载版；中英文名称和图标会进入同一个PRI资源索引。详见[本地化说明](localization.md)。

```powershell
& .\scripts\package-store.ps1 `
  -IdentityName '<已预留的程序包身份名称>' `
  -Publisher '<Partner Center中的准确Publisher字符串>' `
  -PublisherDisplayName '<公开显示的发布者名称>' `
  -Version '0.2.0.0' `
  -ClientDirectory '.\dist\YingView-auto' `
  -OutputDirectory '.\output\store'
```

脚本会生成未签名的x64`.msix`，随后调用MakeAppx解包，并核对程序包身份、图片文件关联和图标文件。脚本不会签名、安装、上传或提交程序包。Microsoft会在认证通过后重新签署商店发行包；制作程序包时仍须使用Partner Center中的真实身份。

当前脚本生成的是`.msix`文件。Microsoft建议商店提交使用`.msixupload`；如果Partner Center要求该格式，可使用Visual Studio打包向导或其他受支持的流程生成上传文件及符号文件。

## 本地检查

- [ ] 如果找不到`MakeAppx.exe`，安装Windows SDK中的Appx打包工具。
- [ ] 如本机具备Windows应用认证工具包（WACK），可对Release程序包运行测试：从Windows启动**Windows App Certification Kit**，选择**Validate an app package**，再选中MSIX并运行检查。WACK已弃用且为可选检查；提交后Partner Center还会执行自己的程序包认证。
- [ ] 需要本地安装时，仅在测试账户或虚拟机中安装正确签名的测试包；检查应用启动、从资源管理器打开图片、路径含空格时的文件激活，以及用户自行选择默认应用的流程。此准备流程不会要求在日常使用的电脑上安装测试证书。
- [ ] 在相应编解码支持可用的系统上检查所有声明的格式，特别是HEIC和AVIF。
- [ ] 检查从旧程序包升级后的行为，并确认卸载应用不会删除用户图片。
- [ ] 在提交前一并核对实际程序包和商店页面；MakeAppx打包成功或WACK通过都不代表微软已批准上架。

## 最终提交

- [ ] 通过Partner Center上传程序包，并处理其程序包验证结果。
- [ ] 在提交表单中核对所有商店资料和隐私问卷。
- [ ] 只有最终身份、版本、程序包和页面资料互相一致后才提交认证。
- [ ] 微软发布后打开公开商店页并确认应用可安装。
