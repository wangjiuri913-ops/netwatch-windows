# NetWatch · Windows 网络监测平台

Windows 本地运行的网络设备监控平台，支持 SNMP v2c 和 ICMP。用于监测交换机、路由器、防火墙的状态、接口流量、错误计数和常见运行指标。

## 下载运行

下载 [NetWatch-Windows-v1.0.0-portable.zip](NetWatch-Windows-v1.0.0-portable.zip)，完整解压到可写目录（例如 `D:\NetWatch`），双击 `Start-Portable.cmd`。这是免安装运行包。文件页点击 **Download raw file** 下载 ZIP。

浏览器打开 **http://127.0.0.1:18765/**。填写设备管理 IP 和 SNMP v2c 只读社区字符串即可开始接入。首次成功采集显示接口信息，第二次采集开始计算流量。

- `Start-Portable.cmd`：真实监测入口，数据保存在旁边的 `Data` 文件夹。
- `Start-Demo.cmd`：独立演示入口，使用模拟设备、`DemoData` 目录和 18767 端口。
- `NetWatch.exe`：后台主程序；直接启动时数据在 `%LOCALAPPDATA%\NetWatch`。

**EXE 没有独立主窗口。** 它启动浏览器并在 Windows 任务栏通知区域运行；关闭浏览器不会停止采集。退出时展开时间旁边的隐藏图标，找到提示文字为“NetWatch 网络监测平台”的图标，右键选择“退出并停止监测”。

## 功能

- 设备新增、编辑、暂停、移除、筛选和手动采集。
- SNMP 在线状态、每轮 3 次 ICMP 平均延迟及无响应率。
- 系统名称、描述、运行时间。
- 端口名称、描述、管理/运行状态、速率。
- 64 位优先的进出流量、利用率、累计错误包和丢弃包。
- 可配置返回百分比的 CPU / 内存 OID。
- 最近 24 小时设备接口合计曲线与 CSV 导出。
- 连续采集失败、恢复、端口状态变化、计数增长和利用率阈值告警。
- Windows DPAPI 凭据加密、本机数据存储、清晰标注的演示模式。

## 环境与范围

- Windows 10 / 11，.NET Framework 4.7.2+，新版 Edge 或 Chrome。
- 正常运行无需 Node.js、Python、Docker、数据库或管理员权限。
- 仅监听本机 `127.0.0.1`，没有多人远程登录。
- SNMP v2c 适合受控管理网络；不支持 v3、Trap/Syslog 或邮件/短信通知。
- 上限为 100 台设备、每台 512 个接口、4 台并发轮询。未进行真实设备满负载验证。
- 历史是设备全部接口的合计，可能重复统计设备内部转发；不等于出口互联网带宽。每个端口独立的历史曲线尚未保存。
- CPU / 内存需要按设备厂商填写 OID。尚未验证所有厂商兼容性。

## 源码与编译

本仓库根目录即源代码目录。运行包另附 `source` 源码快照，结构略有不同，各自编译脚本已经匹配各自依赖位置。

```powershell
.\Build.ps1
```

生成 `bin\NetWatch.exe`、`bin\SharpSnmpLib.dll` 和运行配置。前端页面、样式、脚本嵌入 EXE，修改后需要重新编译。构建使用 Windows .NET Framework 自带编译器，无需下载依赖或安装 .NET SDK。

| 文件 | 作用 |
| --- | --- |
| `Program.cs` | 程序启动、托盘菜单、退出与单实例管理 |
| `Server.cs` | 本机 HTTP 页面与 API |
| `Engine.cs` | 设备管理、轮询调度、告警和历史 |
| `Collector.cs` | SNMP / ICMP 采集、流量计算、演示设备 |
| `Core.cs` | 数据模型、JSON 保存、DPAPI 和计数器算法 |
| `index.html` / `style.css` / `app.js` | 前端结构、外观和交互 |
| `SharpSnmpLib.dll` | 官方 NuGet 包的 SNMP 库，版本 12.5.7 |
| `Build.ps1` | 构建脚本 |
| `Tests.cs` / `Test.ps1` | 核心和 UDP SNMP 模拟设备测试 |
| `http-test.js` | HTTP 集成测试 |

## 测试

```powershell
.\Test.ps1
# 以下测试仅开发时需要 Node.js 18+，正常运行程序不需要：
node .\http-test.js
```

已通过 28 项核心/SNMP 检查和 19 项 HTTP 检查，以及浏览器新增、编辑、详情和图表检查。测试仅连接回环地址模拟设备；尚未连接仓库所有者的真实网络。

## 数据与文档

真实设备配置、历史、日志、测试目录、社区字符串和个人运行数据均不随仓库发布。备份与恢复请看 [使用说明](使用说明.md)，测试范围见 [验证记录](验证记录.md)。第三方库许可见 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)。
