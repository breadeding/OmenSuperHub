using System;
using System.Diagnostics; // 用于打开浏览器
using System.Drawing;
using System.Reflection;
using System.Windows.Documents;
using System.Windows.Forms;

namespace OmenSuperHub {
  public partial class HelpForm : Form {
    private static HelpForm _instance;
    public HelpForm() {
      this.TopMost = true;
      Text = Strings.HelpWindowTitle;

      // 获取屏幕的大小
      Rectangle screenBounds = Screen.PrimaryScreen.Bounds;

      // 计算窗体的大小为屏幕大小的一半
      Size formSize = new Size(screenBounds.Width / 2, screenBounds.Height / 2);

      // 设置窗体的大小
      this.Size = formSize;

      // 计算窗体的位置使其位于屏幕的中央
      Point formLocation = new Point(
          (screenBounds.Width - formSize.Width) / 2,
          (screenBounds.Height - formSize.Height) / 2);

      // 设置窗体的位置
      this.StartPosition = FormStartPosition.Manual;
      this.Location = formLocation;

      Icon = Properties.Resources.fan;

      var panel = new Panel() {
        Dock = DockStyle.Fill,
        Padding = new Padding(12),  // 设置 Panel 的内边距
        AutoScroll = true,
        BackColor = SystemColors.Control
      };

      Assembly assembly = Assembly.GetExecutingAssembly();
      Version version = assembly.GetName().Version;

      var richTextBox = new RichTextBox() {
        Dock = DockStyle.Fill,
        Text = GetHelpText(version),
        BorderStyle = BorderStyle.None,  // 隐藏边框
        Font = new Font("Microsoft YaHei UI", 12, FontStyle.Regular),
        ReadOnly = true,  // 设置为只读模式
        BackColor = SystemColors.Control,  // 设置背景颜色与 Label 一致
        ScrollBars = RichTextBoxScrollBars.Both
      };

      // 启用自动检测 URL
      richTextBox.DetectUrls = true;

      // 添加 LinkClicked 事件处理
      richTextBox.LinkClicked += (sender, e) => {
        Process.Start(new ProcessStartInfo(e.LinkText) { UseShellExecute = true });
      };

      panel.Controls.Add(richTextBox);
      this.Controls.Add(panel);
    }

    public static HelpForm Instance {
      get {
        if (_instance == null || _instance.IsDisposed) {
          _instance = new HelpForm();
        }
        return _instance;
      }
    }

    private void HelpForm_FormClosed(object sender, FormClosedEventArgs e) {
      _instance = null;
    }

    // ── 帮助正文（按语言返回不同文本）──────────────────────────────────────
    private static string GetHelpText(Version version) {
      switch (Strings.Current) {
        case AppLanguage.TraditionalChinese:
          return GetHelpText_TW(version);
        case AppLanguage.English:
          return GetHelpText_EN(version);
        default:
          return GetHelpText_ZH(version);
      }
    }

    private static string GetHelpText_ZH(Version version) {
      return "版本号：" + version +
        "\n更新说明：\n" +
        "（1）新增：优化DB解锁时的版本不支持提示并附加当前驱动版本信息；\n" +
        "（2）修复：修复切换预设会重新应用解锁DB设置的问题。\n\n" +
        "本项目已开源至Github：https://github.com/breadeding/OmenSuperHub\n\n" +
        "一.   “风扇配置”菜单说明：\n" +
        "（1）本程序可设置两种不同的温度-转速对应配置，安静模式加载\"silent.txt\"，降温模式加载\"cool.txt\"，程序会自动进行线性插值，精度为1℃，修改后需要重新点击对应的模式才能生效；\n" +
        "（2）读取到温度变化后程序将立即设置对应的转速，“实时”，“高”，“中”和“低”分别能以从无到高的强度对温度进行平滑处理。\n\n" +
        "二.   “风扇控制”菜单说明：\n" +
        "（1）选择“自动”则程序会根据风扇配置和当前温度自动设定风扇转速；\n" +
        "（2）“最大风扇”为BIOS控制，不一定是最大转速；\n" +
        "（3）选择固定转速时，OSH将在CPU温度高于（平台最高温度-2）℃时弹出通知警告并转为自动风扇模式；\n" +
        "（4）暗9笔记本转速范围0-6400，但只有BIOS设置中关闭了风扇始终启动才能低于2000转。\n\n" +
        "三.   “性能控制”菜单说明：\n" +
        "（1）图形模式等同于BIOS设置中的冷切换，热切换即NVIDIA Advanced Optimus；\n" +
        "（2）PPab条件（Tpp）增加在CPU和GPU之间分配的总功率，为NVIDIA® Dynamic Boost提供更大的功率容量；\n" +
        "（3）IccMax是CPU的最大电流限值；\n" +
        "（4）AC Load Line通过调整CPU电压补偿负载下的电压降；\n" +
        "（5）显卡功耗=BTGP+CTGP+DB/Ppab，开启CTGP和DB才能获得最大GPU性能；\n" +
        "（6）DB版本指设备管理器-NVIDIA Platform Controllers and Framework的驱动版本，解锁版本使用31.0.15.3730；\n" +
        "（7）点击“解锁版本”，程序会删除解锁版本之外的DB驱动并自动启用再禁用驱动完成解锁；\n" +
        "（8）系统重启后解锁会失效，使用解锁功能最好打开OSH开机自启；\n" +
        "（9）如果出现提示GPU功耗异常无法解锁，请尝试重新解锁；\n" +
        "（10）修改CPU功率会同时修改PL1与PL2；\n" +
        "（11）修改GPU频率限制能实现限制不同级别的功耗，注意该功能不是超频功能。\n\n" +
        "四.   “硬件监控”菜单说明：\n" +
        "（1）可选择开启或关闭对应的监控信息，注意如果使用混合模式应关闭GPU监控。\n\n" +
        "五.   “浮窗显示”菜单说明：\n" +
        "（1）开启后，屏幕上方将覆盖硬件监控信息，1秒刷新一次。\n\n" +
        "六.   “Omen键”菜单说明：\n" +
        "（1）若选择“默认”，Omen键绑定的事件为任务计划程序的“Omen Key”任务；\n" +
        "（2）若选择“切换浮窗显示”，Omen键绑定的事件为切换浮窗显示；\n" +
        "（3）注意，Omen键功能可能与某些hp服务有关；\n" +
        "（4）若选择“取消绑定”，Omen键将无效。\n\n" +
        "七.   “其他设置”菜单说明：\n" +
        "（1）“原版”图标为程序自带图标；\n" +
        "（2）“自定义图标”需要在程序所在文件夹存放custom.ico图标文件；\n" +
        "（3）“动态图标”会以当前CPU温度（若无CPU温度则为GPU温度）作为图标，1秒刷新一次；\n" +
        "（4）“数据本地化”开启后会将CPU、GPU温度数据及风扇转速以Txt文本保存到本地；\n" +
        "（5）设置开机自启后，程序会自动创建任务计划程序实现开机自启；\n" +
        "（6）关闭开机自启后，程序会清除任务计划程序；\n" +
        "（7）“语言”菜单可切换简体中文、繁体中文和英语，重启后完全生效。\n\n";
    }

    private static string GetHelpText_TW(Version version) {
      return "版本號：" + version +
        "\n更新說明：\n" +
        "（1）新增：優化DB解鎖時的版本不支援提示並附加目前驅動版本資訊；\n" +
        "（2）修復：修復切換預設會重新套用解鎖DB設定的問題。\n\n" +
        "本專案已開源至Github：https://github.com/breadeding/OmenSuperHub\n\n" +
        "一.   「風扇配置」選單說明：\n" +
        "（1）本程式可設定兩種不同的溫度-轉速對應配置，安靜模式載入\"silent.txt\"，降溫模式載入\"cool.txt\"，程式會自動進行線性插值，精度為1℃，修改後需重新點擊對應模式才能生效；\n" +
        "（2）讀取到溫度變化後程式將立即設定對應轉速，「即時」、「高」、「中」、「低」分別以從無到高的強度對溫度進行平滑處理。\n\n" +
        "二.   「風扇控制」選單說明：\n" +
        "（1）選擇「自動」則程式會根據風扇配置和當前溫度自動設定風扇轉速；\n" +
        "（2）「最大風扇」為BIOS控制，不一定是最大轉速；\n" +
        "（3）選擇固定轉速時，OSH將在CPU溫度高於（平台最高溫度-2）℃時彈出通知警告並轉為自動風扇模式；\n" +
        "（4）暗9筆電轉速範圍0-6400，但只有BIOS設定中關閉風扇始終啟動才能低於2000轉。\n\n" +
        "三.   「效能控制」選單說明：\n" +
        "（1）圖形模式等同於BIOS設定中的冷切換，熱切換即NVIDIA Advanced Optimus；\n" +
        "（2）PPab條件（Tpp）增加在CPU和GPU之間分配的總功率，為NVIDIA® Dynamic Boost提供更大的功率容量；\n" +
        "（3）IccMax是CPU的最大電流限值；\n" +
        "（4）AC Load Line透過調整CPU電壓補償負載下的電壓降；\n" +
        "（5）顯示卡功耗=BTGP+CTGP+DB/Ppab，開啟CTGP和DB才能獲得最大GPU效能；\n" +
        "（6）DB版本指設備管理員-NVIDIA Platform Controllers and Framework的驅動版本，解鎖版本使用31.0.15.3730；\n" +
        "（7）點擊「解鎖版本」，程式會刪除解鎖版本之外的DB驅動並自動啟用再禁用驅動完成解鎖；\n" +
        "（8）系統重啟後解鎖會失效，使用解鎖功能最好開啟OSH開機自啟；\n" +
        "（9）若出現GPU功耗異常無法解鎖，請嘗試重新解鎖；\n" +
        "（10）修改CPU功率會同時修改PL1與PL2；\n" +
        "（11）修改GPU頻率限制能限制不同級別的功耗，注意該功能不是超頻功能。\n\n" +
        "四.   「硬體監控」選單說明：\n" +
        "（1）可選擇開啟或關閉對應的監控資訊，使用混合模式應關閉GPU監控。\n\n" +
        "五.   「浮窗顯示」選單說明：\n" +
        "（1）開啟後，螢幕上方將覆蓋硬體監控資訊，1秒更新一次。\n\n" +
        "六.   「Omen鍵」選單說明：\n" +
        "（1）若選擇「預設」，Omen鍵綁定事件為任務排程的「Omen Key」任務；\n" +
        "（2）若選擇「切換浮窗顯示」，Omen鍵綁定事件為切換浮窗顯示；\n" +
        "（3）注意，Omen鍵功能可能與某些hp服務有關；\n" +
        "（4）若選擇「取消綁定」，Omen鍵將無效。\n\n" +
        "七.   「其他設定」選單說明：\n" +
        "（1）「原版」圖示為程式自帶圖示；\n" +
        "（2）「自訂圖示」需要在程式所在資料夾存放custom.ico圖示檔案；\n" +
        "（3）「動態圖示」以當前CPU溫度（若無CPU溫度則為GPU溫度）作為圖示，1秒更新一次；\n" +
        "（4）「資料本地化」開啟後會將CPU、GPU溫度及風扇轉速以Txt文字儲存到本地；\n" +
        "（5）設定開機自啟後，程式會自動建立任務排程實現開機自啟；\n" +
        "（6）關閉開機自啟後，程式會清除任務排程；\n" +
        "（7）「語言」選單可切換簡體中文、繁體中文和英文，重啟後完全生效。\n\n";
    }

    private static string GetHelpText_EN(Version version) {
      return "Version: " + version +
        "\nChangelog:\n" +
        "(1) Added: Optimized the prompt for version support during DB unlocking and appended the current driver version information;\n" +
        "(2) Fixed: Fixed the issue where switching presets would reapply the DB unlocking settings.\n\n" +
        "This project is open-source on Github: https://github.com/breadeding/OmenSuperHub\n\n" +
        "1.   Fan Config menu:\n" +
        "(1) Two fan profiles are supported. Silent mode loads \"silent.txt\" (conservative), Cool mode loads \"cool.txt\" (aggressive). Linear interpolation at 1°C precision. Changes take effect only after re-selecting the profile;\n" +
        "(2) Realtime / High / Medium / Low response modes apply increasing temperature smoothing.\n\n" +
        "2.   Fan Control menu:\n" +
        "(1) Auto: OSH sets fan speed based on the fan profile and current temperature (higher of CPU/GPU lookup);\n" +
        "(2) Max Fan: BIOS-controlled; not necessarily the highest RPM;\n" +
        "(3) Fixed RPM: if CPU temperature exceeds (platform max − 2)°C, OSH shows a balloon warning and switches to Auto;\n" +
        "(4) OMEN 16 2023 range: 0–6400 RPM. Below 2000 RPM requires 'Always on' fan option disabled in BIOS.\n\n" +
        "3.   Performance menu:\n" +
        "(1) Graphics mode is equivalent to cold switching in BIOS settings, while hot switching is NVIDIA Advanced Optimus;\n" +
        "(2) PPab (Tpp): increases total power budget between CPU and GPU for NVIDIA® Dynamic Boost;\n" +
        "(3) IccMax: maximum CPU current limit for the voltage regulator;\n" +
        "(4) AC Load Line: load-line calibration adjusts CPU voltage to compensate voltage droop;\n" +
        "(5) GPU TDP = BTGP + CTGP + DB/Ppab. Enable CTGP and DB for maximum GPU performance;\n" +
        "(6) DB version refers to the driver under Device Manager → Software Devices → NVIDIA Platform Controllers. Unlocked version uses 31.0.15.3730;\n" +
        "(7) Clicking Unlocked removes other DB drivers and toggles enable/disable to lock the current power state (40-series or older only);\n" +
        "(8) The unlock resets on reboot — enable autostart if you use this feature;\n" +
        "(9) If the GPU power limit anomaly message appears, retry the unlock;\n" +
        "(10) CPU power changes set both PL1 and PL2;\n" +
        "(11) GPU clock limit reduces max GPU frequency (not overclocking).\n\n" +
        "4.   HW Monitor menu:\n" +
        "(1) Toggle individual monitors. If using Hybrid mode, disable GPU monitoring to avoid high CPU usage from frequent GPU wake/sleep.\n\n" +
        "5.   Overlay menu:\n" +
        "(1) Shows hardware info at the top of the screen, refreshed every second.\n\n" +
        "6.   Omen Key menu:\n" +
        "(1) Default: binds the Omen Key to the Task Scheduler 'Omen Key' task;\n" +
        "(2) Toggle Overlay: pressing the Omen Key toggles the overlay;\n" +
        "(3) Note: Omen Key may depend on certain HP services;\n" +
        "(4) Unbound: the Omen Key has no action.\n\n" +
        "7.   Settings menu:\n" +
        "(1) Default icon: built-in icon;\n" +
        "(2) Custom icon: place custom.ico in the program folder;\n" +
        "(3) Dynamic icon: shows current CPU (or GPU) temperature, refreshed every second;\n" +
        "(4) Data Localize: saves CPU/GPU temp and fan RPM to local .txt files (useful for Macro Deck);\n" +
        "(5) Autostart: creates a Task Scheduler entry to launch OSH at boot;\n" +
        "(6) Disable autostart: removes the Task Scheduler entry;\n" +
        "(7) Language: switch between Simplified Chinese, Traditional Chinese, and English. Restart to fully apply.\n\n";
    }
  }
}
