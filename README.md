# PreMonitor

基于Edge Monitor改进而来，一个小巧的进程资源看护工具：你可以选择一个或多个应用设为“受监控”，当其资源占用超过你设定的阈值并持续一定时间时，PreMonitor 会自动终止目标进程，以避免后台/前台异常占用影响系统体验。

此工具仅用于自动终止异常进程，无法从根本上修复应用自身的自动启动或资源泄漏等问题（治标不治本）。

![alt text](image.png)

## 条件

当“受监控应用”的资源占用满足以下判定时，PreMonitor 将自动终止匹配的进程：

1. 满足以下任一阈值（可配置，支持全局或每条规则覆写）：
   - CPU 使用率 ≥ 指定百分比（例如 30%）
   - 内存占用 ≥ 指定 MB（例如 2048MB）
   - 磁盘吞吐 ≥ 指定 B/s（按每秒 I/O 读写字节近似）
   - GPU 使用率 ≥ 指定百分比（基于 GPU Engine 计数器近似）
   - 网络吞吐 ≥ 指定 B/s（按每秒 IO Other Bytes 近似）
2. 持续秒数 ≥ 设定值后执行终止（0 表示立即关闭）。

说明：
- 阈值为 0 时表示不启用该项判定（例如不监控 GPU 则将 GPU 阈值置 0）。
- “全局阈值”适用于所有规则；也可为某条规则单独覆写阈值。

## 技术栈

- .NET 7
- WPF (Windows Presentation Foundation)
- Microsoft Extensions (DI, Logging, Configuration, Hosting)
- MVVM 模式

## 许可证

本项目采用 CC BY-NC 4.0（Creative Commons Attribution-NonCommercial 4.0 International）许可证。

**阁下可以：**
- ✅ 分享 — 在任何媒介以任何形式复制、发行本作品
- ✅ 演绎 — 修改、转换或以本作品为基础进行创作

**惟须遵守下列条件：**
- 📝 **署名** — 阁下必须给出适当的署名，提供指向本许可协议的链接，同时标明是否（对原始作品）作了修改
- 🚫 **非商业性使用** — 阁下不得将本作品用于商业目的

**原作者：** Prelina Montelli

详细许可条款请参见：https://creativecommons.org/licenses/by-nc/4.0/
