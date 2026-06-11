using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace OmenSuperHub {
  public partial class HelpForm : Form {
    private static HelpForm _instance;
    private WebBrowser webBrowser;

    public HelpForm() {
      TopMost = true;
      Text = Strings.HelpWindowTitle;
      Icon = Properties.Resources.fan;
      this.FormClosed += HelpForm_FormClosed;

      // 窗体大小和位置（居中）
      Rectangle screenBounds = Screen.PrimaryScreen.Bounds;
      Size = new Size(screenBounds.Width * 3 / 5, screenBounds.Height * 3 / 5);
      StartPosition = FormStartPosition.CenterScreen;

      // 创建 WebBrowser 控件
      webBrowser = new WebBrowser {
        Dock = DockStyle.Fill,
        IsWebBrowserContextMenuEnabled = false,
        WebBrowserShortcutsEnabled = true,
        ScriptErrorsSuppressed = true
      };
      webBrowser.Navigating += WebBrowser_Navigating;
      Controls.Add(webBrowser);

      LoadHelpContent();
    }

    private void WebBrowser_Navigating(object sender, WebBrowserNavigatingEventArgs e) {
      if (e.Url.Scheme == Uri.UriSchemeHttp || e.Url.Scheme == Uri.UriSchemeHttps) {
        e.Cancel = true;
        Process.Start(new ProcessStartInfo(e.Url.ToString()) { UseShellExecute = true });
      }
    }

    private void LoadHelpContent() {
      Assembly assembly = Assembly.GetExecutingAssembly();
      Version version = assembly.GetName().Version;
      string html = GetHelpHtml(version);
      webBrowser.DocumentText = html;
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
      try {
        webBrowser?.Stop();
      } catch { }

      webBrowser = null;
      _instance = null;
    }

    // ─────────────────────────────────────────────────────────────────
    // 入口方法：根据当前语言返回完整 HTML
    // ─────────────────────────────────────────────────────────────────
    private static string GetHelpHtml(Version version) {
      string content;
      switch (Strings.Current) {
        case AppLanguage.TraditionalChinese:
          content = GetHelpHtml_TW(version);
          break;
        case AppLanguage.English:
          content = GetHelpHtml_EN(version);
          break;
        default:
          content = GetHelpHtml_ZH(version);
          break;
      }

      return $@"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'>
<style>
    body {{
        font-family: 'Segoe UI', 'Segoe UI Emoji', 'Microsoft YaHei UI', sans-serif;
        font-size: 12pt;
        line-height: 1.5;
        margin: 16px;
        color: #1e1e1e;
        background-color: #fefefe;
    }}
    h1 {{
        font-size: 1.6em;
        margin-top: 0;
        color: #2c3e50;
    }}
    h2 {{
        font-size: 1.3em;
        margin-top: 0.2em;
        margin-bottom: 0.4em;
        color: #2980b9;
        border-left: 5px solid #2980b9;
        padding-left: 12px;
    }}
    .update-list {{
        padding: 0 16px;
    }}
    .update-list p {{
        font-size: 1.1em;
        margin-top: 0.4em;
        margin-bottom: 0.2em;
    }}
    .update-list li {{
        list-style: none;
        line-height: 1.8;
        padding-left: 10px;
    }}
    a {{
        color: #3498db;
        text-decoration: none;
    }}
    a:hover {{
        text-decoration: underline;
    }}
    hr {{
        margin: 20px 0;
        border: none;
        border-top: 1px solid #ddd;
    }}
</style>
</head>
<body>
{content}
</body>
</html>";
    }

    // 简体中文 HTML 内容（注意内部双引号已转义为两个双引号）
    private static string GetHelpHtml_ZH(Version version) {
      return $@"
<h2>📦 OmenSuperHub v{Assembly.GetExecutingAssembly().GetName().Version} 更新说明</h2>
<div class='update-list'>
<p><strong>新增：</strong></p>
<li>✨ 点击OSH程序图标可以快速切换浮窗显示</a></li>
<p><strong>优化：</strong></p>
<li>⚡ 更紧凑的Omen键菜单结构</li>
<li>⚡ 打开设置窗口后支持继续操作菜单</li>
<p><strong>修复：</strong></p>
<li>🐛 任务计划程序无UserId限制导致多次启动、登录前无法自动控制转速</li>
<li>🐛 切换语言后未关闭已打开的窗口、菜单</li>
<li>🐛 Omen键已绑定目标应用时将Omen键设定为“打开应用”模式无效</li>
<li>🐛 模拟按键设置界面的“取消”点击无效</li>
<li>🐛 Omen键已绑定模拟按键时将Omen键设定为“模拟按键”模式依然打开设置界面</li>
<li>🐛 Omen键已绑定模拟按键时将Omen键设定为“模拟按键”模式无效</li>
</div>

<p>本项目已开源至 Github：<a href='https://github.com/breadeding/OmenSuperHub'>https://github.com/breadeding/OmenSuperHub</a></p>

<hr/>

<h2>一. “风扇配置”菜单说明</h2>
<p>(1) 本程序支持安静、降温和自定义三种温度-转速配置。安静模式加载 ""silent.txt""，降温模式加载 ""cool.txt""；左键点击自定义会应用 ""custom.txt""，右键点击则打开 CPU/GPU 曲线编辑器；若应用时文件不存在，会先复制 ""cool.txt""；</p>
<p>(2) 自定义曲线中左键增加节点、拖动调整节点、右键删除节点；“保存”只写入文件，“保存并应用”会写入并立即切换到自定义曲线，“加载”可将其他 txt 配置载入编辑器；程序会自动进行线性插值，精度为1℃；</p>
<p>(3) 读取到温度变化后程序将立即设置对应的转速，“实时”，“高”，“中”和“低”分别能以从无到高的强度对温度进行平滑处理。</p>

<h2>二. “风扇控制”菜单说明</h2>
<p>(1) 选择“自动”则程序会根据风扇配置和当前温度自动设定风扇转速；</p>
<p>(2) “最大风扇”为BIOS控制，不一定是最大转速；</p>
<p>(3) 选择固定转速时，OSH将在CPU温度高于（平台最高温度-2）℃时弹出通知警告并转为自动风扇模式；</p>
<p>(4) 暗9笔记本转速范围0-6400，但只有BIOS设置中关闭了风扇始终启动才能低于2000转。</p>

<h2>三. “性能控制”菜单说明</h2>
<p>(1) 图形模式等同于BIOS设置中的冷切换，热切换即NVIDIA Advanced Optimus；</p>
<p>(2) PPab条件（Tpp）增加在CPU和GPU之间分配的总功率，为NVIDIA® Dynamic Boost提供更大的功率容量；</p>
<p>(3) IccMax是CPU的最大电流限值；</p>
<p>(4) AC Load Line通过调整CPU电压补偿负载下的电压降；</p>
<p>(5) 显卡功耗=BTGP+CTGP+DB/Ppab，开启CTGP和DB才能获得最大GPU性能；</p>
<p>(6) DB版本指设备管理器-NVIDIA Platform Controllers and Framework的驱动版本，解锁版本使用31.0.15.3730；</p>
<p>(7) 点击“解锁版本”，程序会删除解锁版本之外的DB驱动并自动启用再禁用驱动完成解锁；</p>
<p>(8) 系统重启后解锁会失效，使用解锁功能最好打开OSH开机自启；</p>
<p>(9) 如果出现提示GPU功耗异常无法解锁，请尝试重新解锁；</p>
<p>(10) 修改CPU功率会同时修改PL1与PL2；</p>
<p>(11) 修改GPU频率限制能实现限制不同级别的功耗，注意该功能不是超频功能。</p>

<h2>四. “硬件监控”菜单说明</h2>
<p>(1) 可选择开启或关闭对应的监控信息，注意如果使用混合模式，在不使用GPU时应关闭GPU监控，否则可能会导致功耗增加。</p>

<h2>五. “浮窗显示”菜单说明</h2>
<p>(1) 开启后，屏幕上方将覆盖硬件监控信息，1秒刷新一次。</p>

<h2>六. “Omen键”菜单说明</h2>
<p>(1) 若选择“默认”，Omen键绑定的事件为任务计划程序的“Omen Key”任务；</p>
<p>(2) 若选择“切换浮窗显示”，Omen键绑定的事件为切换浮窗显示；</p>
<p>(3) 若选择“切换预设”，Omen键绑定的事件为在候选预设之间循环切换；</p>
<p>(4) 若选择“打开应用”，Omen键绑定的事件为打开选择的应用；</p>
<p>(5) 注意，Omen键功能可能与某些hp服务有关；</p>
<p>(6) 若选择“取消绑定”，Omen键将无效。</p>

<h2>七. “其他设置”菜单说明</h2>
<p>(1) “原版”图标为程序自带图标；</p>
<p>(2) “自定义图标”需要在程序所在文件夹存放custom.ico图标文件；</p>
<p>(3) “动态图标”会以当前CPU温度（若无CPU温度则为GPU温度）作为图标，1秒刷新一次；</p>
<p>(4) “数据本地化”开启后会将CPU、GPU温度数据及风扇转速以Txt文本保存到本地；</p>
<p>(5) 设置开机自启后，程序会自动创建任务计划程序实现开机自启；</p>
<p>(6) 关闭开机自启后，程序会清除任务计划程序；</p>
<p>(7) “语言”菜单可切换简体中文、繁体中文和英语，重启后完全生效。</p>
";
    }

    // 繁体中文 HTML 内容
    private static string GetHelpHtml_TW(Version version) {
      return $@"
<h2>📦 OmenSuperHub v{Assembly.GetExecutingAssembly().GetName().Version} 更新說明</h2>
<div class='update-list'>
<p><strong>新增：</strong></p>
<li>✨ 點選OSH程式圖示可快速切換浮窗顯示</a></li>
<p><strong>優化：</strong></p>
<li>⚡ 更緊湊的Omen鍵選單結構</li>
<li>⚡ 開啟設定視窗後支援繼續操作選單</li>
<p><strong>修復：</strong></p>
<li>🐛 任務規劃程式無UserId限制導致多次啟動、登入前無法自動控制轉速</li>
<li>🐛 切換語言後未關閉已開啟的視窗、選單</li>
<li>🐛 Omen鍵已綁定目標應用時將Omen鍵設定為「開啟應用程式」模式無效</li>
<li>🐛 模擬按鍵設定介面的「取消」點選無效</li>
<li>🐛 Omen鍵已綁定模擬按鍵時將Omen鍵設定為「模擬按鍵」模式仍開啟設定介面</li>
<li>🐛 Omen鍵已綁定模擬按鍵時將Omen鍵設定為「模擬按鍵」模式無效</li>
</div>

<p>本專案已開源至 Github：<a href='https://github.com/breadeding/OmenSuperHub'>https://github.com/breadeding/OmenSuperHub</a></p>

<hr/>

<h2>一. 「風扇配置」選單說明</h2>
<p>(1) 本程式支援安靜、降溫和自訂三種溫度-轉速設定。安靜模式載入 ""silent.txt""，降溫模式載入 ""cool.txt""；左鍵點擊自訂會套用 ""custom.txt""，右鍵點擊則開啟 CPU/GPU 曲線編輯器；若套用時檔案不存在，會先複製 ""cool.txt""；</p>
<p>(2) 自訂曲線中左鍵新增節點、拖曳調整節點、右鍵刪除節點；「儲存」只寫入檔案，「儲存並套用」會寫入並立即切換至自訂曲線，「載入」可將其他 txt 設定載入編輯器；程式會自動進行線性插值，精度為1℃；</p>
<p>(3) 讀取到溫度變化後程式將立即設定對應轉速，「即時」、「高」、「中」、「低」分別以從無到高的強度對溫度進行平滑處理。</p>

<h2>二. 「風扇控制」選單說明</h2>
<p>(1) 選擇「自動」則程式會根據風扇配置和當前溫度自動設定風扇轉速；</p>
<p>(2) 「最大風扇」為BIOS控制，不一定是最大轉速；</p>
<p>(3) 選擇固定轉速時，OSH將在CPU溫度高於（平台最高溫度-2）℃時彈出通知警告並轉為自動風扇模式；</p>
<p>(4) 暗9筆電轉速範圍0-6400，但只有BIOS設定中關閉風扇始終啟動才能低於2000轉。</p>

<h2>三. 「效能控制」選單說明</h2>
<p>(1) 圖形模式等同於BIOS設定中的冷切換，熱切換即NVIDIA Advanced Optimus；</p>
<p>(2) PPab條件（Tpp）增加在CPU和GPU之間分配的總功率，為NVIDIA® Dynamic Boost提供更大的功率容量；</p>
<p>(3) IccMax是CPU的最大電流限值；</p>
<p>(4) AC Load Line透過調整CPU電壓補償負載下的電壓降；</p>
<p>(5) 顯示卡功耗=BTGP+CTGP+DB/Ppab，開啟CTGP和DB才能獲得最大GPU效能；</p>
<p>(6) DB版本指設備管理員-NVIDIA Platform Controllers and Framework的驅動版本，解鎖版本使用31.0.15.3730；</p>
<p>(7) 點擊「解鎖版本」，程式會刪除解鎖版本之外的DB驅動並自動啟用再禁用驅動完成解鎖；</p>
<p>(8) 系統重啟後解鎖會失效，使用解鎖功能最好開啟OSH開機自啟；</p>
<p>(9) 若出現GPU功耗異常無法解鎖，請嘗試重新解鎖；</p>
<p>(10) 修改CPU功率會同時修改PL1與PL2；</p>
<p>(11) 修改GPU頻率限制能限制不同級別的功耗，注意該功能不是超頻功能。</p>

<h2>四. 「硬體監控」選單說明</h2>
<p>(1) 可選擇開啟或關閉對應的監控訊息，注意如果使用混合模式，在不使用GPU時應關閉GPU監控，否則可能會導致功耗增加。</p>

<h2>五. 「浮窗顯示」選單說明</h2>
<p>(1) 開啟後，螢幕上方將覆蓋硬體監控資訊，1秒更新一次。</p>

<h2>六. 「Omen鍵」選單說明</h2>
<p>(1) 若選擇「預設」，Omen鍵綁定事件為任務排程的「Omen Key」任務；</p>
<p>(2) 若選擇「切換浮窗顯示」，Omen鍵綁定事件為切換浮窗顯示；</p>
<p>(3) 若選擇“切換預設”，Omen鍵綁定的事件為在候選預設之間循環切換；</p>
<p>(4) 若選擇“開啟應用”，Omen鍵綁定的事件為開啟選擇的應用；</p>
<p>(5) 注意，Omen鍵功能可能與某些hp服務有關；</p>
<p>(6) 若選擇「取消綁定」，Omen鍵將無效。</p>

<h2>七. 「其他設定」選單說明</h2>
<p>(1) 「原版」圖示為程式自帶圖示；</p>
<p>(2) 「自訂圖示」需要在程式所在資料夾存放custom.ico圖示檔案；</p>
<p>(3) 「動態圖示」以當前CPU溫度（若無CPU溫度則為GPU溫度）作為圖示，1秒更新一次；</p>
<p>(4) 「資料本地化」開啟後會將CPU、GPU溫度及風扇轉速以Txt文字儲存到本地；</p>
<p>(5) 設定開機自啟後，程式會自動建立任務排程實現開機自啟；</p>
<p>(6) 關閉開機自啟後，程式會清除任務排程；</p>
<p>(7) 「語言」選單可切換簡體中文、繁體中文和英文，重啟後完全生效。</p>
";
    }

    // 英文 HTML 内容
    private static string GetHelpHtml_EN(Version version) {
      return $@"
<h2>📦 OmenSuperHub v{Assembly.GetExecutingAssembly().GetName().Version} Changelog</h2>
<div class='update-list'>
<p><strong>New Features:</strong></p>
<li>✨ Click the OSH program icon to quickly toggle the floating window display.</a></li>
<p><strong>Improvements:</strong></p>
<li>⚡ More compact Omen key menu structure</li>
<li>⚡ Menu remains interactive after opening the settings window</li>
<p><strong>Fixes:</strong></p>
<li>🐛 Task scheduler lacked a UserID restriction, causing multiple launches; fan speed could not be automatically controlled before login</li>
<li>🐛 Open windows and menus were not closed after switching languages</li>
<li>🐛 Setting the Omen key to ""Open Application"" mode had no effect when it was already bound to a target application</li>
<li>🐛 Clicking ""Cancel"" in the simulated key settings interface did not work</li>
<li>🐛 Setting the Omen key to ""Simulated Key"" mode still opened the settings interface when it was already bound to a simulated key</li>
<li>🐛 Setting the Omen key to ""Simulated Key"" mode had no effect when it was already bound to a simulated key</li>
</div>

<p>This project is open-source on Github: <a href='https://github.com/breadeding/OmenSuperHub'>https://github.com/breadeding/OmenSuperHub</a></p>

<hr/>

<h2>1. Fan Config menu</h2>
<p>(1) Three fan profiles are supported. Silent loads ""silent.txt"" and Cool loads ""cool.txt"". Left-click Custom to apply ""custom.txt""; right-click it to open the CPU/GPU curve editor. If custom.txt is missing when applied, cool.txt is copied first;</p>
<p>(2) In the custom editor, left-click to add a point, drag to adjust it, and right-click to delete it. Save only writes the file; Save & Apply writes it and switches to the custom curve immediately. Load imports another txt profile. Curves use linear interpolation at 1°C precision;</p>
<p>(3) Realtime / High / Medium / Low response modes apply increasing temperature smoothing.</p>

<h2>2. Fan Control menu</h2>
<p>(1) Auto: OSH sets fan speed based on the fan profile and current temperature (higher of CPU/GPU lookup);</p>
<p>(2) Max Fan: BIOS-controlled; not necessarily the highest RPM;</p>
<p>(3) Fixed RPM: if CPU temperature exceeds (platform max − 2)°C, OSH shows a balloon warning and switches to Auto;</p>
<p>(4) OMEN 16 2023 range: 0–6400 RPM. Below 2000 RPM requires 'Always on' fan option disabled in BIOS.</p>

<h2>3. Performance menu</h2>
<p>(1) Graphics mode is equivalent to cold switching in BIOS settings, while hot switching is NVIDIA Advanced Optimus;</p>
<p>(2) PPab (Tpp): increases total power budget between CPU and GPU for NVIDIA® Dynamic Boost;</p>
<p>(3) IccMax: maximum CPU current limit for the voltage regulator;</p>
<p>(4) AC Load Line: load-line calibration adjusts CPU voltage to compensate voltage droop;</p>
<p>(5) GPU TDP = BTGP + CTGP + DB/Ppab. Enable CTGP and DB for maximum GPU performance;</p>
<p>(6) DB version refers to the driver under Device Manager → Software Devices → NVIDIA Platform Controllers. Unlocked version uses 31.0.15.3730;</p>
<p>(7) Clicking Unlocked removes other DB drivers and toggles enable/disable to lock the current power state (40-series or older only);</p>
<p>(8) The unlock resets on reboot — enable autostart if you use this feature;</p>
<p>(9) If the GPU power limit anomaly message appears, retry the unlock;</p>
<p>(10) CPU power changes set both PL1 and PL2;</p>
<p>(11) GPU clock limit reduces max GPU frequency (not overclocking).</p>

<h2>4. HW Monitor menu</h2>
<p>(1) You can choose to enable or disable the corresponding monitoring information. Note that if you are using hybrid mode, you should disable GPU monitoring when the GPU is not in use; otherwise, power consumption may increase.</p>

<h2>5. Overlay menu</h2>
<p>(1) Shows hardware info at the top of the screen, refreshed every second.</p>

<h2>6. Omen Key menu</h2>
<p>(1) Default: binds the Omen Key to the Task Scheduler 'Omen Key' task;</p>
<p>(2) Toggle Overlay: pressing the Omen Key toggles the overlay;</p>
<p>(3) Switch Preset: pressing the Omen Key cycles through the preset options;</p>
<p>(4) Open App: pressing the Omen Key opens the selected application;</p>
<p>(5) Note: Omen Key may depend on certain HP services;</p>
<p>(6) Unbound: the Omen Key has no action.</p>

<h2>7. Settings menu</h2>
<p>(1) Default icon: built-in icon;</p>
<p>(2) Custom icon: place custom.ico in the program folder;</p>
<p>(3) Dynamic icon: shows current CPU (or GPU) temperature, refreshed every second;</p>
<p>(4) Data Localize: saves CPU/GPU temp and fan RPM to local .txt files (useful for Macro Deck);</p>
<p>(5) Autostart: creates a Task Scheduler entry to launch OSH at boot;</p>
<p>(6) Disable autostart: removes the Task Scheduler entry;</p>
<p>(7) Language: switch between Simplified Chinese, Traditional Chinese, and English. Restart to fully apply.</p>
";
    }
  }
}
