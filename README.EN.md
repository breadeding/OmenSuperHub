<div align="center">
  <img src="./Resources/1.png" width="150" alt="OmenSuperHub Logo">
  <h1 align="center">OmenSuperHub</h1>
</div>

<div align="center">
  <a href="https://github.com/breadeding/OmenSuperHub/releases/latest"><img src="https://img.shields.io/github/release/breadeding/OmenSuperHub?label=latest&logo=github&style=for-the-badge" alt="GitHub Releases"></a>
  <a href="https://github.com/breadeding/OmenSuperHub/stargazers"><img src="https://img.shields.io/github/stars/breadeding/OmenSuperHub.svg?logo=github&style=for-the-badge" alt="GitHub stars"></a>
  <a href="https://github.com/breadeding/OmenSuperHub/releases"><img src="https://img.shields.io/github/downloads/breadeding/OmenSuperHub/total.svg?logo=github&style=for-the-badge" alt="GitHub Releases"></a>
  <div><br></div>

  **HP Omen/HyperX Laptop Third-Party Control Console**

  [简体中文](README.md) | [繁體中文](README.zh-TW.md) | English
</div>

About OmenSuperHub
=
OmenSuperHub is a utility that emulates OGH (Omen Gaming Hub) functionalities, including fan control, power management, automatic DB version switching, Omen key customization, and temperature/power monitoring.

It implements most of the useful features of HP OMEN's official OGH software, **without** network connection, advertisements, wallpapers, or other unnecessary bloatware.

- Ensure the included PawnIO driver is installed before use; otherwise, CPU data cannot be retrieved.
- The program is primarily designed and tested on **HP OMEN 16 2023 Intel** (i9‑13900HX + RTX 4060). It may not work on all platforms.
- Primarily supports the OMEN 16 (2021) and subsequent models; the latest model currently supported is the HyperX OMEN Max (2026). It does not support the OMEN 15 (2020) or earlier models.
- Using the program on unsupported models may lead to reading failures, blue screens, or other consequences.
- To avoid conflicts, please close the `OmenCommandCenterBackground` process or uninstall OGH before launching OmenSuperHub.
- Information can be viewed via the tray icon – right‑click the icon to change modes. Power will not be locked after exiting OGH.
- If you intend to use OmenSuperHub as a permanent OGH replacement, disable OGH autostart and enable OmenSuperHub autostart.
- More detailed instructions are available in the "About OSH" right‑click menu.
- Run `OmenSuperHub清理脚本.bat` to clear all OmenSuperHub configuration.

Links
=
- [OmenMon](https://github.com/OmenMon/OmenMon) by **@GeographicCone**
- [OmenHwCtl](https://github.com/GeographicCone/OmenHwCtl) by **@GeographicCone**

These two projects were the primary inspiration for this work. The authors not only provided interactive commands but also explored methods to interact with OGH. Unfortunately, support for newer models is missing and development has ceased; they may not work without OGH.

This project retrieves CPU and GPU data using [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor).

This project implements the auto-start function by creating a scheduled task using [TaskScheduler](https://github.com/dahall/TaskScheduler).

## Disclaimer

This program is **not** affiliated with or endorsed by HP or OMEN. The brand name is used for reference only. This program interacts directly with hardware and may be potentially dangerous or destructive. The user assumes all responsibility and risk for using this software.

<div><br></div>

<div align="center">

[![Star History Chart](https://api.star-history.com/svg?repos=breadeding/OmenSuperHub&type=Date)](https://www.star-history.com/#breadeding/OmenSuperHub&Date)

</div>
