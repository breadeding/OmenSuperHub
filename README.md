<div align="center">
  <img src="./Resources/1.png" width="150" alt="OmenSuperHub Logo">
  <h1 align="center">OmenSuperHub</h1>
</div>

<div align="center">
  <a href="https://github.com/breadeding/OmenSuperHub/releases/latest"><img src="https://img.shields.io/github/release/breadeding/OmenSuperHub?label=latest&logo=github&style=for-the-badge" alt="GitHub Releases"></a>
  <a href="https://github.com/breadeding/OmenSuperHub/stargazers"><img src="https://img.shields.io/github/stars/breadeding/OmenSuperHub.svg?logo=github&style=for-the-badge" alt="GitHub stars"></a>
  <a href="https://github.com/breadeding/OmenSuperHub/releases"><img src="https://img.shields.io/github/downloads/breadeding/OmenSuperHub/total.svg?logo=github&style=for-the-badge" alt="GitHub Releases"></a>
  <div><br></div>

  **惠普暗影精灵笔记本第三方控制台**

  简体中文 | [繁體中文](README.zh-TW.md) | [English](README.EN.md)
</div>

关于OmenSuperHub
=
本程序主要功能包括伪装 OGH (Omen Gaming Hub)、风扇控制、功率控制、DB 版本自动切换，Omen 键自定义以及温度/功率监控

OmenSuperHub 实现了惠普暗夜精灵（HP OMEN）系列的控制软件 OGH 的大多数有用功能，但不连接网络，且没有广告、壁纸等无用功能

* 使用前需确保已安装随附的 PawnIO 驱动，否则无法获取 CPU 数据

* 程序设计主要基于暗影精灵 9 Intel 笔记本（i9-13900HX + 4060），不保证能在所有平台正常运行

* 主要支持暗影精灵7及以后的机型，目前最新机型为 HyperX 暗影精灵 Max (2026)，不支持暗影精灵6及以前的机型

* 在不支持的机型上使用可能出现无法读取数据、蓝屏或其他后果

* 为了避免功能冲突，启动前应关闭`OmenCommandCenterBackground`进程或卸载 OGH

* 在任务栏可查看信息或右键菜单切换模式，不会因退出OGH而锁功耗

* 要长时间使用本程序替代OGH，请关闭OGH自启动并开启OSH自启

* 在右键菜单“关于OSH”中可查看更多说明

* 运行`OmenSuperHub清理脚本.bat`可清空OmenSuperHub的所有配置

链接
=
* **@GeographicCone**的 [OmenMon](https://github.com/OmenMon/OmenMon)

* **@GeographicCone**的 [OmenHwCtl](https://github.com/GeographicCone/OmenHwCtl)

这两个项目是本项目的主要灵感来源，作者不仅给出了交互命令，还给出了探索OGH交互的方法，可惜的是缺少对较新机型的支持且已经停止更新，可能无法脱离OGH运行。

本项目通过 [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) 获取CPU和GPU数据。

本项目通过 [TaskScheduler](https://github.com/dahall/TaskScheduler) 创建任务计划程序实现自启功能。

免责声明
=
本程序不属于HP或Omen，品牌名称仅供参考，本程序与硬件交互，可能具有潜在危险或破坏性，使用者自行承担使用本程序的所有后果。

<div><br></div>

<div align="center">

[![Star History Chart](https://api.star-history.com/svg?repos=breadeding/OmenSuperHub&type=Date)](https://www.star-history.com/#breadeding/OmenSuperHub&Date)

</div>
