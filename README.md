# PearlTray

轻量的 Windows 托盘监控工具，在任务栏直接显示 PEARL 价格，支持人民币换算、矿机算力查询、钱包余额和阈值通知。

## 功能

- **实时价格**：读取 Kryptex 的 PRL 价格，取其价格页面默认月线的最后一个数据点，无需 API Token 或中转服务。
- **显示设置**：支持 USDT / 人民币切换，以及 0、1、2 位小数。
- **矿机监控**：填写钱包地址后，可查看在线矿机、显卡型号及算力。
- **钱包余额**：右键菜单顶部显示 Pearl Wallet 余额，单位 PRL，与显卡算力按列对齐。
- **算力告警**：总算力低于设定阈值或恢复时，发送 Windows 通知或 Webhook。
- **余额通知**：钱包余额从阈值以下达到阈值时通知，支持失败重试。
- **登录自启动**：随包提供开启和关闭自启动的 BAT 脚本，仅作用于当前 Windows 用户。
- **主题适配**：菜单和设置窗口跟随 Windows 应用深浅主题。
- **免安装运行**：配置文件与 exe 放在同一目录。

## 下载与运行

从仓库的 **Releases** 页面下载 `PearlTray-windows.zip`，解压后运行 `PearlTray.exe`。

需要 Windows 10 / 11 和 .NET Framework 4.8。

首次运行会自动获取价格。若图标出现在任务栏折叠菜单中，可将其拖到常驻区域。

压缩包包含 `PearlTray.exe`、`PearlTray.exe.config`、两个自启动 BAT、README 和字体许可文件。发布包不包含个人 `settings.json`、钱包地址、Webhook 配置、日志或测试输出。

### 登录自启动

将 `Enable-Startup.bat`、`Disable-Startup.bat` 与 `PearlTray.exe` 放在同一目录：

- 双击 `Enable-Startup.bat`，在当前用户的 Windows 启动文件夹创建 `PearlTray.lnk`，下次登录时自动运行，无需管理员权限。
- 双击 `Disable-Startup.bat`，删除该快捷方式，关闭自启动。
- 移动程序目录后，重新运行 `Enable-Startup.bat` 更新快捷方式。脚本运行完毕后按任意键关闭窗口。

## 使用方式

| 操作 | 功能 |
| --- | --- |
| 左键点击托盘数字 | 在默认浏览器中打开 SafeTrade 交易页面 |
| 右键点击托盘数字 | 查看矿机信息、刷新数据、修改设置或退出 |
| 鼠标悬停 | 查看完整价格、刷新状态及最后成功更新时间 |

默认每 **60 秒**刷新一次，最低支持 **5 秒**。刷新失败时保留上次价格，并以黄色提示；尚未获取到价格时显示 `--` 或 `!`。

价格接口为 `https://pool.kryptex.com/api/v1/coin/prl/price/chart?time_range=month`，价格源采用美元报价。人民币显示沿用 USDT/CNY 参考汇率换算，支持自定义汇率接口。左键仍打开 SafeTrade 交易页面。

## 矿机与告警

在设置中填写钱包地址，即可查询 PearlHash 在线矿机信息。钱包留空时关闭查询。

同一个钱包地址用于查询 Blockbook 已确认余额，接口为 `https://blockbook.pearlresearch.ai/api/v2/address/钱包地址?details=basic`。余额按 8 位小数换算为 PRL，显示时四舍五入并固定保留两位小数，数字使用金黄色，随刷新周期更新；失败时保留上次余额并提示，尚未获取时显示 `-- PRL`。

“总算力阈值”的单位为 **TH/s**，默认 `0`，表示关闭告警。例如填写 `350`，全部在线矿机的显卡总算力低于 350 TH/s 时触发告警；回升至大于或等于阈值时视为恢复。

持续低于阈值只通知一次；算力恢复时通知一次，持续正常不重复通知，之后再次下降会重新告警。首次查询算力正常时不发送恢复通知。Windows 系统通知和 Webhook 均支持恢复通知，发送失败时在下个周期重试。接口请求失败时，不根据旧数据触发通知。

“钱包阈值”的单位为 **PRL**，默认 `0`，表示关闭通知。只有上一次成功查询的余额低于阈值、本次余额大于或等于阈值时，才通知一次；持续高于阈值不重复通知，降至阈值以下后再次达到阈值可再次通知。首次查询不通知，更换钱包或阈值后重新记录起点。比较使用未四舍五入的余额，查询失败不更新比较状态。沿用下方通知方式，发送失败时在下个周期重试。

支持同时开启：

- **Windows 系统通知**：通过托盘通知显示。
- **Webhook**：支持 GET / POST、自定义请求头、正文模板和可选的用户名、密码。

Webhook 支持 `{{title}}` 和 `{{message}}` 占位符。POST 默认正文：

```json
{"message":"{{message}}","title":"{{title}}"}
```

## 配置文件

保存设置后，程序会在 `PearlTray.exe` 所在目录创建 `settings.json`。移动程序时，请将配置文件一起移动。

旧版配置可从 `%LOCALAPPDATA%\PearlTray\settings.json` 复制到 exe 所在目录继续使用。

Webhook 密码通过 Windows DPAPI 按当前用户加密，换电脑或 Windows 用户后可能需要重新填写。配置中的 URL、请求头等信息请妥善保管。

## 从源码构建

在项目根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

无需安装 NuGet 包或 Node.js。构建结果：

- 程序：`dist/PearlTray.exe`
- 免安装包：`PearlTray-windows.zip`

打包采用明确的文件清单，即使输出目录中已有个人配置或测试文件，也不会将它们收入 ZIP。两个 BAT 会复制到输出目录并打入免安装包。

## 源码结构

仓库以当前 Windows 托盘版为准，无需部署服务端。

| 路径 | 内容 |
| --- | --- |
| 根目录 `*.cs` | 托盘程序、设置窗口、行情、矿机与钱包查询、通知 |
| `tests/` | 自动化测试 |
| `assets/` | 内嵌字体和字体许可 |
| `tools/` | 构建时使用的图标生成工具 |
| `build.ps1` | 编译、测试和打包脚本 |
| `Enable-Startup.bat` / `Disable-Startup.bat` | 开启 / 关闭当前用户登录自启动 |

## v1.4.0 更新

- 价格源改为 Kryptex PRL 月线最后一个数据点。
- 新增钱包余额展示、余额阈值通知及独立通知渠道重试。
- 新增算力恢复通知，持续正常时不重复通知。
- 钱包余额与显卡算力的数值、单位按列对齐。
- 免安装包包含开启和关闭登录自启动的两个 BAT 文件。

## 致谢

菜单内嵌 JetBrains Mono 字体，相关许可与第三方声明见 `THIRD-PARTY-NOTICES.txt`。
