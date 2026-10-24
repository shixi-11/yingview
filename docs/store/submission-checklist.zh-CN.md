# Microsoft Store 提交检查清单

## 打包前

- [ ] Microsoft Store接受包含WPF在内的桌面应用程序包。此流程提交编译后的应用文件和商店资料，不要求公开源代码，也不要求修改项目许可证。参见[微软桌面应用打包指南](https://learn.microsoft.com/zh-cn/windows/msix/desktop/desktop-to-uwp-packaging-dot-net)。
- [ ] 在Microsoft Partner Center中预留最终产品名称和程序包身份。打包参数中的`IdentityName`必须与预留身份完全一致。
- [ ] 从Partner Center复制准确的`Publisher`值；它与面向用户显示的发布者名称不是同一个字段。
- [ ] 确认最终产品名称和发布者显示名称。最终品牌确定后，将其传入打包脚本。
- [ ] 发布并验证可访问的HTTPS支持页、应用隐私说明页和客户端EULA页面，再将真实网址填入Partner Center。隐私页须说明映窗及本机数据处理；EULA须对应免费个人/商业使用的客户端许可，不能用PolyForm源码许可证代替。
- [ ] 在Partner Center设置免费定价与销售范围、应用类别、功能声明并完成年龄分级问卷。
- [ ] 准备en-US和zh-CN本地化商店资料与截图。至少需要一张截图；桌面截图须为PNG且至少1366×768，并满足商店标志资源要求。
- [ ] 构建Release独立版客户端，确认`ClientDirectory`包含`YingView.exe`、官方源码`LICENSE`、`NOTICE`、`THIRD-PARTY-NOTICES.md`、`CLIENT-LICENSE.txt`、`CLIENT-LICENSE.zh-CN.txt`、`LICENSING.md`和完整的发布运行文件。
- [ ] 选择与应用版本一致、以`.0`结尾的四段版本号（当前应用版本为`0.3.13`，程序包版本应为`0.3.13.0`）。

## 创建并检查程序包

只有在Partner Center预留应用后才能运行此命令。将所有尖括号占位值替换为Partner Center提供的准确身份与发布者字段；脚本会拒绝占位值，不能猜测或伪造身份：

先运行`./scripts/build.ps1 -ClientLanguage auto`构建商店通用客户端，确认`client-language.json`中的语言为`auto`。商店脚本不接受固定语言的下载版；中英文名称和图标会进入同一个PRI资源索引。详见[本地化说明](localization.md)。

```powershell
& .\scripts\package-store.ps1 `
  -IdentityName '<已预留的程序包身份名称>' `
  -Publisher '<Partner Center中的准确Publisher字符串>' `
  -PublisherDisplayName '<公开显示的发布者名称>' `
  -Version '0.3.13.0' `
  -ClientDirectory '.\dist\YingView-auto' `
  -OutputDirectory '.\output\store'
```

脚本会生成未签名的x64`.msix`，随后调用MakeAppx解包，并核对程序包身份、图片文件关联和图标文件。脚本不会签名、安装、上传或提交程序包。Microsoft会在认证通过后重新签署商店发行包；制作程序包时仍须使用Partner Center中的真实身份。

当前脚本生成的是`.msix`文件。Microsoft建议商店提交使用`.msixupload`；如果Partner Center要求该格式，可使用Visual Studio打包向导或其他受支持的流程生成上传文件及符号文件。

## 本地检查

- [x] 本机已具备Windows SDK打包工具：SDK 10.0.26100.0（x64）中的`MakeAppx.exe`和`MakePri.exe`。
- [ ] 本机具备时，建议对Release程序包运行Windows应用认证工具包（WACK）作为提交前检查：从Windows启动**Windows App Certification Kit**，选择**Validate an app package**，再选中MSIX并运行检查。微软说明WACK已弃用且不再维护，但仍可用于可选的本地检查；提交后Partner Center会执行正式认证。参见[本地程序包验证](https://learn.microsoft.com/en-us/windows/msix/package/packaging-uwp-apps#validate-your-app-package)和[MSIX提交认证](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/create-app-submission?pivots=store-installer-msix)。
- [ ] 需要本地安装时，仅在测试账户或虚拟机中安装正确签名的测试包；检查应用启动、从资源管理器打开图片、路径含空格时的文件激活，以及用户自行选择默认应用的流程。此准备流程不会要求在日常使用的电脑上安装测试证书。
- [ ] 只检查客户端实际支持的格式：PNG、JPEG、GIF、WebP、AVIF、BMP、TIFF和ICO。客户端不声明或注册HEIC/HEIF支持。
- [ ] 检查从旧程序包升级后的行为，并确认卸载应用不会删除用户图片。
- [ ] 在提交前一并核对实际程序包和商店页面；MakeAppx打包成功或WACK通过都不代表微软已批准上架。

## 当前待完成的上架前置事项

目前仍缺Partner Center产品预留及准确程序包身份/Publisher、已验证的发布者账号、已公开可访问的支持/隐私/EULA网址、本地化截图与商店标志素材、最终应用属性与年龄分级，以及实际MSIX构建和程序包验证。尚未提交商店页面，也没有认证结果。

## 最终提交

- [ ] 通过Partner Center上传程序包，并处理其程序包验证结果。
- [ ] 在提交表单中核对所有商店资料和隐私问卷。
- [ ] 只有最终身份、版本、程序包和页面资料互相一致后才提交认证。
- [ ] 微软发布后打开公开商店页并确认应用可安装。
