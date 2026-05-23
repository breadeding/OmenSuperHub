<div align="center">
  <img src="./Resources/1.png" width="150" alt="OmenSuperHub Logo">
  <h1 align="center">OmenSuperHub</h1>
</div>

<div align="center">
  <a href="https://github.com/breadeding/OmenSuperHub/releases/latest"><img src="https://img.shields.io/github/release/breadeding/OmenSuperHub?label=latest&logo=github&style=for-the-badge" alt="GitHub Releases"></a>
  <a href="https://github.com/breadeding/OmenSuperHub/stargazers"><img src="https://img.shields.io/github/stars/breadeding/OmenSuperHub.svg?logo=github&style=for-the-badge" alt="GitHub stars"></a>
  <a href="https://github.com/breadeding/OmenSuperHub/releases"><img src="https://img.shields.io/github/downloads/breadeding/OmenSuperHub/total.svg?logo=github&style=for-the-badge" alt="GitHub Releases"></a>
  <div><br></div>

  **惠普暗影精靈筆記本第三方控制台**

  [简体中文](README.md) | 繁體中文 | [English](README.EN.md)
</div>

關於 OmenSuperHub
=
本程式主要功能包括偽裝 OGH (Omen Gaming Hub)、風扇控制、功率控制、DB 版本自動切換、Omen 鍵自訂以及溫度/功率監控

OmenSuperHub 實作了惠普暗影精靈（HP OMEN）系列控制軟體 OGH 的大多數實用功能，但不連接網路，且沒有廣告、桌布等無用功能

* 程式設計主要基於暗影精靈 9 Intel 筆記型電腦（i9-13900HX + 4060），不保證能在所有平台上正常運作

* 主要支援暗影精靈7及以後的機型，目前最新機型為 HyperX 暗影精靈 Max (2026)，不支援暗影精靈6及以前的機型

* 在不支援的機型上使用可能會出現無法讀取資料、藍屏或其他後果

* 為避免功能衝突，啟動前應關閉 `OmenCommandCenterBackground` 程序，或直接解除安裝 OGH

* 在系統匣可查看資訊或透過右鍵選單切換模式，不會因退出 OGH 而鎖住功耗

* 若要長期使用本程式取代 OGH，請關閉 OGH 自動啟動，並開啟 OSH 自動啟動

* 在右鍵選單的「關於 OSH」中可查看更多說明

* 執行`OmenSuperHub清理腳本.bat`可清空 OmenSuperHub 的所有設定

相關連結
=
* **@GeographicCone** 的 [OmenMon](https://github.com/OmenMon/OmenMon)

* **@GeographicCone** 的 [OmenHwCtl](https://github.com/GeographicCone/OmenHwCtl)

這兩個專案是本專案的主要靈感來源。作者不僅提供了互動指令，也展示了探索 OGH 互動的方法。可惜的是，它們缺乏對較新機型的支援且已停止更新，可能無法在沒有 OGH 的環境下執行。

* [OpenHardwareMonitorLib](https://openhardwaremonitor.org)
* [hexagon-oss](https://github.com/hexagon-oss/openhardwaremonitor) 對 OpenHardwareMonitor 的硬體庫進行了更新。
* [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)

本專案取得 CPU 和 GPU 溫度的方法來自上述幾個專案。

## 免責聲明

本程式不屬於 HP 或 Omen，品牌名稱僅供參考。本程式會與硬體直接互動，可能具有潛在危險或破壞性，使用者須自行承擔使用本程式的所有後果。

<div><br></div>

<div align="center">

[![Star History Chart](https://api.star-history.com/svg?repos=breadeding/OmenSuperHub&type=Date)](https://www.star-history.com/#breadeding/OmenSuperHub&Date)

</div>
