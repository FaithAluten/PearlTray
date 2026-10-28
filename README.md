# PearlTray

轻量的 Windows 托盘监控工具，在任务栏直接显示 PEARL 价格，支持人民币换算、矿机算力查询和低算力告警。

## 功能

- **实时价格**：直接读取 SafeTrade 的 PEARL/USDT 公开行情，无需 API Token 或中转服务。
- **显示设置**：支持 USDT / 人民币切换，以及 0、1、2 位小数。
- **矿机监控**：填写钱包地址后，可查看在线矿机、显卡型号及算力。
- **算力告警**：总算力低于设定阈值时，发送 Windows 通知或 Webhook。
- **主题适配**：菜单和设置窗口跟随 Windows 应用深浅主题。
- **免安装运行**：配置文件与 exe 放在同一目录。

## 下载与运行

从仓库的 **Releases** 页面下载 `PearlTray-windows.zip`，解压后运行 `PearlTray.exe`。

需要 Windows 10 / 11 和 .NET Framework 4.8。

首次运行会自动获取价格。若图标出现在任务栏折叠菜单中，可将其拖到常驻区域。

## 使用方式

| 操作 | 功能 |
| --- | --- |
| 左键点击托盘数字 | 在默认浏览器中打开 SafeTrade 交易页面 |
| 右键点击托盘数字 | 查看矿机信息、刷新数据、修改设置或退出 |
| 鼠标悬停 | 查看完整价格、刷新状态及最后成功更新时间 |

默认每 **60 秒**刷新一次，最低支持 **5 秒**。刷新失败时保留上次价格，并以黄色提示；尚未获取到价格时显示 `--` 或 `!`。

人民币价格根据 PEARL/USDT 行情和 USDT/CNY 参考汇率换算，支持自定义汇率接口。

## 矿机与告警

在设置中填写钱包地址，即可查询 PearlHash 在线矿机信息。钱包留空时关闭查询。

“总算力阈值”的单位为 **TH/s**，默认 `0`，表示关闭告警。例如填写 `350`，全部在线矿机的显卡总算力低于 350 TH/s 时触发通知，等于阈值时不通知。

持续低于阈值只通知一次；算力恢复后再次下降，会重新通知。接口请求失败时，不根据旧数据触发告警。

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
powershell -ExecutionPolicy Bypass -File .\windows-tray\build.ps1 -Test
```

无需安装 NuGet 包或 Node.js。构建结果：

- 程序：`windows-tray/dist/PearlTray.exe`
- 免安装包：`windows-tray/PearlTray-windows.zip`

## 源码结构

仓库以当前 Windows 托盘版为准，无需部署服务端。

| 路径 | 内容 |
| --- | --- |
| `windows-tray/` | 托盘程序、设置窗口、行情与矿机查询、通知 |
| `windows-tray/tests/` | 自动化测试 |
| `windows-tray/assets/` | 内嵌字体和字体许可 |
| `windows-tray/tools/` | 构建时使用的图标生成工具 |
| `windows-tray/build.ps1` | 编译、测试和打包脚本 |

## 致谢

菜单内嵌 JetBrains Mono 字体，相关许可与第三方声明见 `windows-tray/THIRD-PARTY-NOTICES.txt`。
