# PearlTray

Windows 10 / 11 纯数字托盘价格监控，使用 .NET Framework 4.8。
免安装包：`PearlTray-windows.zip`，解压后双击运行。

## 使用

双击 `PearlTray.exe` 即自动获取 PEARL/USDT 价格，无需填写价格接口 URL 或 API Token，
无需部署中转服务。刷新周期默认为 60 秒，最短 5 秒。
通过 Windows 10 / 11 自带的 `curl.exe` 和浏览器 User-Agent 直接请求 SafeTrade 公开 JSON 接口：
`https://safetrade.com/api/v2/peatio/public/markets/pearlusdt/tickers`，
读取 `ticker.last`，不抓取交易页面 HTML。不会弹出命令行窗口，也不依赖 Python。
旧配置自动忽略原有价格 URL 和 Token，保留汇率、钱包和通知设置；
旧的 1 至 4 秒刷新周期自动改为 5 秒。
设置窗口中的“USDT/CNY 汇率接口 URL”可编辑，已默认填写公开接口。
旧配置升级后也会自动使用默认地址。
“钱包地址”可选，填写后会请求 `https://pearlhash.xyz/api/account/<钱包地址>`。
右键菜单按矿机名称列出显卡名称、算力，支持多台机器。
矿机较多时列表限制高度，可使用鼠标滚轮或上下箭头浏览；底部操作项始终可见。
菜单不显示价格和全网算力，也不再请求全网算力接口。
采用 Codex 风格的圆角、细边框、留白与悬停高亮，跟随 Windows 应用深浅主题。
显卡名称省略 NVIDIA GeForce 前缀，例如 RTX 4070 Ti。
矿机名作为分组标题，显卡型号左对齐，算力右对齐，数值以英伟达绿（#76B900）加粗突出显示。
操作项配有图标，主菜单与单位子菜单使用统一的间距和悬停效果。
右键菜单参考 Tray-Temps，内嵌 JetBrains Mono Regular 9.75 pt，
矿机标题与算力数值使用 ExtraBold；中文使用 Windows 微软雅黑 UI，无需安装字体。
主设置和 Webhook 设置沿用同一套深浅主题、内嵌字体、圆角细边框与绿色强调。
设置按功能分组，输入框提供焦点高亮，Webhook 密码可切换显隐。
Webhook 使用 GET / POST 分段选择；GET 下保留并禁用正文及 Content-Type，切回 POST 可继续编辑。
窗口支持调整大小，内容过长时可滚动，底部保存、取消和错误提示保持可见。
算力从 H/s 换算为 TH/s，保留两位小数。
矿机信息跟随设置的刷新周期，也支持“立即刷新”。钱包留空时关闭矿机查询。
矿机请求无需认证；失败时保留上次数据并显示失败状态，
更换钱包后会清除旧钱包的数据，在线矿机为空时显示“暂无在线矿机”。

设置中可填写“总算力阈值”，单位为 TH/s，默认 `0`（关闭告警）。
填写单个数值，例如 `350`；所有机器的全部显卡总算力低于阈值时通知，
等于阈值不通知。设置中仅显示总算力，不再显示每台机器的算力及阈值顺序。
旧配置中逗号分隔的各机器阈值自动相加为总算力阈值，例如 `250,100` 转为 `350`。
在线机器为空时按总算力 0 判断；接口失败时保留旧数据，不根据旧数据触发告警。

通知方式可同时开启：“Windows 系统通知”默认勾选，“Webhook”默认不勾选。
系统通知通过 Windows 托盘通知显示，是否弹出取决于系统通知设置。
总算力持续低于阈值只通知一次，恢复后再次下降会重新通知；
重启程序后重新检测。Webhook 发送失败会在菜单提示，并在下个成功的矿机刷新周期重试。
点击“Webhook 设置”可配置 URL、GET/POST、Content-Type、JSON 请求头、
正文模板和可选的 Username/Password，参照 Komari 的 Webhook 通知配置。
默认 GET；POST 默认正文为 `{"message":"{{message}}","title":"{{title}}"}`。
GET URL 中的占位符会进行 URL 编码，JSON 正文中的占位符会进行 JSON 转义。
例如 GET URL 可填写 `https://notify.example/send?title={{title}}&body={{message}}`。
Webhook 请求超时为 20 秒，HTTP 2xx 视为成功。
Webhook 密码使用 Windows DPAPI 加密保存；URL 和自定义请求头保存在本地配置中。

行情 GET 请求不携带认证头，读取 JSON 中的 `ticker.last`。
例如 `{"ticker":{"high":"1.79","last":"1.65"}}` 显示 **1.65**。
命令行末尾出现的 `%` 通常是 shell 提示符，不应是接口响应的一部分。

- 左键数字：默认浏览器打开 https://safetrade.com/exchange/PEARL-USDT 。
- 右键数字：立即刷新、设置刷新周期 / 汇率 / 矿机 / 通知、切换显示单位，或退出。
- “显示单位”中选择 **USDT** 或 **人民币 (CNY)**，选择会保存，重启后沿用。
- “显示单位”中也可选择托盘数字保留 **0、1、2 位小数**，默认 2 位，四舍五入并补齐末尾的零；立即生效，重启后沿用。长数值缩写为 K/M/B 时也使用所选位数。
- 悬停数字：查看完整价格、刷新状态、最后成功更新时间。
- 未获取到价格时显示 `--`；失败且无历史价格时显示黄色 `!`。
- 刷新失败时保留上次价格并变黄，下一周期重试。
- 数字颜色跟随 Windows 任务栏深浅主题。

人民币价格 = 接口 `ticker.last` × USDT/CNY 参考汇率，托盘在换算后按所选小数位数四舍五入。
小于 0.01 元的价格在悬停提示中保留更多小数，托盘仍遵循所选位数，可能显示为零。
汇率来自 https://github.com/fawazahmed0/exchange-api 的日更 USDT/CNY 数据，
默认通过 jsDelivr 获取，失败时尝试该项目的 Cloudflare 镜像。
更换为自定义汇率 URL 后，只请求你填写的地址；保存后立即丢弃旧汇率并重新获取。
自定义接口需返回相同结构，例如：

```json
{"date":"2026-10-26","usdt":{"cny":6.66}}
```

程序每小时检查一次汇率；失败后每分钟重试。
汇率请求无需认证。
悬停和“显示单位”菜单可查看汇率及数据日期，人民币价格仅为换算参考。
汇率刷新失败时用上次成功汇率并变黄；尚未获取过汇率时显示 `!`，
不会把原始 USDT 数值标成人民币。切回 USDT 不需要汇率。

Windows 单个托盘图标的宽度固定。
数字完整恢复 1.1 的显示方式，使用 Windows 自带的 Arial 粗体、原始字间距和抗锯齿绘制，不加描边。
数字高度固定为图标的 74%，宽度最多占图标宽度减 1 像素，超宽时只做横向压缩。
价格按所选位数显示，大于等于 1000 的数值使用 K/M/B 缩写，悬停可查看完整值。
Windows 可能把新图标放进
任务栏的上箭头菜单，可以把它拖到常驻区域。

配置保存在 `PearlTray.exe` 所在目录下的 `settings.json`，不受启动时工作目录影响。
保存设置时自动创建配置文件；移动程序时请将配置文件一起移动。
旧版配置可从 `%LOCALAPPDATA%\PearlTray\settings.json` 复制到 exe 所在目录继续使用。
Webhook 密码通过 Windows DPAPI 按当前 Windows 用户加密。
价格接口不再存储 URL 或 Token。
刷新周期范围为 5 至 86400 秒，请求超时为 20 秒，同一配置不并发刷新。
HTTP 403 显示访问被拒绝，HTTP 429 提示增大刷新周期；失败后保留上次价格并在下个周期重试。

## 构建与检查

不需要 NuGet 包或 Node.js：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Test
```

输出位于 `dist\`，同时生成 `PearlTray-windows.zip`。
托盘数字使用 Windows 自带 Arial，菜单字体已内嵌，无需安装额外字体。
分发时保留 `PearlTray.exe`、`PearlTray.exe.config`。
支持通过 `PearlTray.exe --config <配置文件绝对路径>` 使用隔离测试配置。
