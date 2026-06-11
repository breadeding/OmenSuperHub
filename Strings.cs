namespace OmenSuperHub {
  /// <summary>
  /// 语言枚举：简体中文 / 繁体中文 / 英文
  /// </summary>
  public enum AppLanguage {
    SimplifiedChinese,
    TraditionalChinese,
    English
  }

  /// <summary>
  /// 所有 UI 字符串的静态访问入口。
  /// 修改 Current 后立即对后续调用生效。
  /// </summary>
  public static class Strings {
    public static AppLanguage Current = AppLanguage.SimplifiedChinese;

    public static string ProductUnsupported => T(
        "您的设备不是 HP 产品，本程序可能无法正常工作。是否继续？",
        "您的設備不是 HP 產品，本程式可能無法正常工作。是否繼續？",
        "Your device is not an HP product. This program may not function correctly. Continue anyway?"
    );

    // 不支持的 HP 机型（非游戏类 HP 产品）
    public static string ProductUnsupportedHP => T(
        "您的 HP 机型非惠普游戏笔记本，可能无法正常使用。是否继续？",
        "您的 HP 機型非惠普遊戲筆記本，可能無法正常使用。是否繼續？",
        "Your HP model is not an HP gaming laptop. Some features may not work properly. Continue anyway?"
    );

    // 旧款 OMEN 产品提示
    public static string ProductOldOmen => T(
        "您的设备属于旧款 OMEN 产品，部分功能可能无法使用。是否继续运行程序？",
        "您的設備屬於舊款 OMEN 產品，部分功能可能無法使用。是否繼續執行程式？",
        "Your device is an older OMEN product. Some features may not be available. Do you want to continue?"
    );

    // ─────────────────────────────────────────────────────────────────────────
    // 顶层菜单 / 分组标题
    // ─────────────────────────────────────────────────────────────────────────
    public static string SysInfo => T("本机信息", "本機資訊", "System Info");
    public static string FanConfig => T("风扇配置", "風扇配置", "Fan Config");
    public static string FanControl => T("风扇控制", "風扇控制", "Fan Control");
    public static string PerfControl => T("性能控制", "效能控制", "Performance");
    public static string HwMonitor => T("硬件监控", "硬體監控", "HW Monitor");
    public static string FloatingBar => T("浮窗显示", "浮窗顯示", "Overlay");
    public static string OmenKeyMenu => T("Omen键", "Omen鍵", "Omen Key");
    public static string OtherSettings => T("其他设置", "其他設定", "Settings");
    public static string Help => T("帮助", "說明", "Help");
    public static string Exit => T("退出", "結束", "Exit");
    public static string OK => T("确定", "確定", "OK");
    public static string Cancel => T("取消", "取消", "Cancel");
    public static string Clear => T("清除", "清除", "Clear");

    // ─────────────────────────────────────────────────────────────────────────
    // 预设菜单
    // ─────────────────────────────────────────────────────────────────────────
    public static string PresetsMenu => T("预设", "預設", "Presets");
    public static string PresetExtreme => T("极致性能", "極致性能", "Extreme Performance");
    public static string PresetGpuPriority => T("GPU优先", "GPU優先", "GPU Priority");
    public static string PresetLightUse => T("轻度使用", "輕度使用", "Light Use");
    public static string PresetCustom1 => T("自定义预设1", "自定義預設1", "Custom 1");
    public static string PresetCustom2 => T("自定义预设2", "自定義預設2", "Custom 2");
    public static string PresetCustom3 => T("自定义预设3", "自定義預設3", "Custom 3");
    public static string ActivePreset => T("当前预设", "目前預設", "Active Preset");
    public static string RenamePreset => T("重命名", "重新命名", "Rename");
    public static string RenamePresetTitle => T("重命名预设", "重新命名預設", "Rename Preset");
    public static string RenamePresetPrompt => T("请输入新的预设名称：", "請輸入新的預設名稱：", "Please enter new preset name:");
    public static string RenamePresetError => T("预设名称不能为空，且不能与其他预设同名。", "預設名稱不能為空，且不能與其他預設同名。", "Preset name cannot be empty and must be unique.");

    // ─────────────────────────────────────────────────────────────────────────
    // 预设菜单 — 内置预设提示
    // ─────────────────────────────────────────────────────────────────────────
    public static string PresetNote => T(
        "💡预设包括除DB版本之外的风扇配置、风扇控制、性能控制选项。",
        "💡預設包含DB版本以外的風扇配置、風扇控制、效能控制選項。",
        "💡 Presets include fan config, fan control, and performance control options—excluding the DB version."
    );
    public static string PresetInternalNote => T(
        "💡只有自定义预设能永久保存设置并额外包括硬件监控配置，内置预设的改动会在下一次切换预设时丢失！",
        "💡只有自訂預設能永久儲存設定並額外包含硬體監控配置，內建預設的變更會在下次切換預設時遺失！",
        "💡 Only custom presets permanently save settings and include hardware monitoring configurations; changes made to built-in presets will be lost the next time you switch presets."
    );
    public static string PresetExtremeTooltip => T(
        "完全释放性能，甚至可以尝试继续调高CPU功率。",
        "完全釋放效能，甚至可以嘗試繼續調高CPU功率。",
        "Unleash full performance—you can even try further increasing the CPU power."
    );
    public static string PresetGpuPriorityTooltip => T(
        "散热不足的情况下优先保证GPU性能，适当降低CPU功耗。",
        "散熱不足的情況下優先確保GPU效能，適當降低CPU功耗。",
        "In scenarios with insufficient cooling, priority is given to maintaining GPU performance while appropriately reducing CPU power consumption."
    );
    public static string PresetLightUseTooltip => T(
        "降低整体功耗，适合需要安静的场景。",
        "降低整體功耗，適合需要安靜的場景。",
        "Reduces overall power consumption, making it suitable for environments requiring quiet operation."
    );

    // ─────────────────────────────────────────────────────────────────────────
    // 语言菜单
    // ─────────────────────────────────────────────────────────────────────────
    public static string LanguageMenu => T("语言", "語言", "Language");
    public static string LangSimplified => T("简体中文", "简体中文", "简体中文");
    public static string LangTraditional => T("繁體中文", "繁體中文", "繁體中文");
    public static string LangEnglish => T("English", "English", "English");

    // ─────────────────────────────────────────────────────────────────────────
    // 风扇配置
    // ─────────────────────────────────────────────────────────────────────────
    public static string FanCurveNote => T("💡 右键打开编辑窗口", "💡 右鍵開啟編輯視窗", "💡 Right-click to open the editor");
    public static string FanSilentMode => T("安静模式", "安靜模式", "Silent Mode");
    public static string FanCoolMode => T("降温模式", "降溫模式", "Cool Mode");
    public static string FanCustomMode => T("自定义", "自訂", "Custom");
    public static string FanSilentTooltip => T(
        "左键应用安静模式；右键编辑 silent.txt 风扇曲线。",
        "左鍵套用安靜模式；右鍵編輯 silent.txt 風扇曲線。",
        "Left-click to apply Silent mode; right-click to edit silent.txt fan curve.");
    public static string FanCoolTooltip => T(
        "左键应用降温模式；右键编辑 cool.txt 风扇曲线。",
        "左鍵套用降溫模式；右鍵編輯 cool.txt 風扇曲線。",
        "Left-click to apply Cool mode; right-click to edit cool.txt fan curve.");
    public static string FanCustomTooltip => T(
        "左键应用 custom.txt；右键编辑 custom.txt 风扇曲线。",
        "左鍵套用 custom.txt；右鍵編輯 custom.txt 風扇曲線。",
        "Left-click to apply custom.txt; right-click to edit custom.txt fan curve.");
    public static string FanCurveEditorTitle => T("风扇曲线", "風扇曲線", " Fan Curves");
    public static string FanCurveTemperatureAxis => T("温度 (℃)", "溫度 (℃)", "Temperature (℃)");
    public static string FanCurveFanSpeedAxis => T("风扇转速 (RPM)", "風扇轉速 (RPM)", "Fan Speed (RPM)");
    public static string FanCurveInstructions => T(
        "左键空白处增加节点；拖动节点调整；右键节点删除（至少保留两个节点）。",
        "左鍵空白處新增節點；拖曳節點調整；右鍵節點刪除（至少保留兩個節點）。",
        "Left-click empty space to add; drag to adjust; right-click to delete (keep at least two points).");
    public static string FanCurveSave => T("保存", "儲存", "Save");
    public static string FanCurveSaveAndApply => T("保存并应用", "儲存並套用", "Save & Apply");
    public static string FanCurveCancel => T("取消", "取消", "Cancel");
    public static string FanCurveLoad => T("加载", "載入", "Load");
    public static string FanCurveFileFilter => T(
        "风扇配置文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
        "風扇設定檔 (*.txt)|*.txt|所有檔案 (*.*)|*.*",
        "Fan profile (*.txt)|*.txt|All files (*.*)|*.*");
    public static string FanCurveInvalidFile => T(
        "风扇配置文件格式无效。",
        "風扇設定檔格式無效。",
        "The fan profile format is invalid.");
    public static string FanCurveOutOfRange => T(
        "曲线必须至少包含两个节点，转速不能低于0，且温度不能重复。",
        "曲線必須至少包含兩個節點，轉速不能低於0，溫度無法重複。",
        "The curve must contain at least two nodes, the rotational speed cannot be lower than zero, and the temperatures must not be repeated.");
    public static string FanCurveLoadFailed => T("加载风扇配置失败。", "載入風扇設定失敗。", "Failed to load the fan profile.");
    public static string FanCurveSaveFailed => T("保存风扇配置失败。", "儲存風扇設定失敗。", "Failed to save the fan profile.");
    public static string FanResponseSpeed => T("风扇响应速度", "風扇響應速度", "Fan Response Speed");
    public static string FanRespRealtime => T("实时", "即時", "Realtime");
    public static string FanRespHigh => T("高", "高", "High");
    public static string FanRespMedium => T("中", "中", "Medium");
    public static string FanRespLow => T("低", "低", "Low");

    // 高温自动保护开关
    public static string FanAutoProtect => T("高温自动保护", "高溫自動保護", "High-Temp Auto-Protect");
    public static string FanAutoProtectNote => T(
        "💡开启后，若CPU温度过高且风扇处于固定转速且低于80%最大转速，OSH将自动切换为降温模式和自动风扇控制。",
        "💡開啟後，若CPU溫度過高且風扇處於固定轉速且低於80%最大轉速，OSH將自動切換為降溫模式和自動風扇控制。",
        "💡 When enabled, OSH automatically switches to Cool mode and Auto fan control if CPU temperature is too high and fans are running at a fixed low speed."
    );
    public static string FanAutoProtectOn => T("开启保护", "開啟保護", "Enable Protection");
    public static string FanAutoProtectOff => T("关闭保护", "關閉保護", "Disable Protection");

    // ─────────────────────────────────────────────────────────────────────────
    // 风扇控制
    // ─────────────────────────────────────────────────────────────────────────
    public static string FanAuto => T("自动", "自動", "Auto");
    public static string FanMax => T("最大风扇", "最大風扇", "Max Fan");

    // ─────────────────────────────────────────────────────────────────────────
    // 风扇除尘（Clean Creek）
    // ─────────────────────────────────────────────────────────────────────────
    public static string CleanCreekMenuItem => T("反转除尘", "反轉除塵", "Clean Creek");
    public static string CleanCreekLegacyMenuItem => T("反转除尘（旧版）", "反轉除塵（舊版）", "Clean Creek (Legacy)");
    public static string CleanCreekTitle => T("风扇除尘", "風扇除塵", "Fan Dust Removal");
    public static string CleanCreekConfirmMessage => T(
        "即将开始反转除尘。点击确定开始，要停止除尘请选择“取消”。",
        "即將開始反轉除塵。點擊確定開始，要停止除塵請選擇「取消」。",
        "Reverse dust removal will start soon. Click OK to start, or Cancel to stop.");
    public static string CleanCreekProgressMessageTemplate => T(
        "清洁进行中，剩余 {0} 秒...",
        "清潔進行中，剩餘 {0} 秒...",
        "Cleaning in progress, {0} seconds remaining...");
    public static string CleanCreekStopButton => T("停止", "停止", "Stop");

    // ─────────────────────────────────────────────────────────────────────────
    // 性能控制 — 图形模式
    // ─────────────────────────────────────────────────────────────────────────
    public static string HotSwitch => T("热切换", "熱切換", "Hot Switch");
    public static string GraphicsMode => T("图形模式", "圖形模式", "Graphics Mode");
    public static string GfxOnlyInternal => T("💡仅部分机型支持在此修改图形模式（需重启），若不支持可在BIOS设置修改。",
        "💡僅部分機型支援在此修改圖形模式（需重啟），若不支援可在BIOS設定修改。",
        "💡 Only some models support switching graphics mode here (requires reboot). Use BIOS otherwise.");
    public static string GfxDiscreteMode => T("独显直连", "獨顯直連", "Discrete GPU");
    public static string GfxHybridMode => T("混合模式", "混合模式", "Hybrid Mode");
    public static string GfxUMAConfirm => T("仅集成显卡启用，屏蔽独显，该模式下 HDMI 输出将无法工作。确定切换吗?",
        "僅整合顯示啟用，遮蔽獨顯，此模式下 HDMI 輸出將無法運作。確定切換嗎？",
        "Only iGPU will be active. HDMI output will not work in UMA mode. Confirm switch?");
    public static string GfxUMATitle => T("切换到UMA模式", "切換至UMA模式", "Switch to UMA Mode");
    public static string GfxSwitchedTo(string mode) => T(
        $"已切换到{mode}模式，重启生效。", $"已切換至{mode}模式，重啟生效。", $"Switched to {mode} mode. Reboot to apply.");
    public static string GfxUnsupported => T("该机器不支持系统内冷切！",
        "此機型不支援系統內冷切！", "This model does not support in-OS graphics switching!");

    // ─────────────────────────────────────────────────────────────────────────
    // 性能控制 — GPU 相关
    // ─────────────────────────────────────────────────────────────────────────
    public static string GpuAppsMenu => T("占用GPU的程序", "佔用GPU的程式", "GPU Processes");
    public static string GpuAppsNone => T("无", "無", "None");
    public static string GpuRestartMenu => T("重启显卡", "重啟顯示卡", "Restart GPU");
    public static string GpuRestartTooltip => T("通过重启独立 GPU 减少不必要的占用 GPU 情况。",
        "透過重啟獨立 GPU 減少不必要的 GPU 佔用情況。",
        "Restart the discrete GPU to reduce unnecessary usage.");
    public static string GpuRestartConfirm => T(
        "可能会导致应用崩溃，请尽可能通过手动关闭占用进程来解除独立显卡占用，建议只在混合模式下操作。确定重启显卡吗?",
        "可能導致應用程式崩潰，建議先手動關閉佔用程式，且只在混合模式下操作。確定重啟顯示卡嗎？",
        "This may crash running applications. Close GPU processes manually if possible. Proceed?");
    public static string GpuRestartTitle => T("重启显卡", "重啟顯示卡", "Restart GPU");
    public static string GpuCloseConfirm(string name) => T(
        $"是否关闭进程 {name}?", $"是否關閉程序 {name}？", $"Close process {name}?");
    public static string GpuCloseTitle => T("关闭确认", "關閉確認", "Confirm Close");
    public static string GpuCloseError(string msg) => T(
        $"关闭进程失败: {msg}", $"關閉程序失敗: {msg}", $"Failed to close process: {msg}");

    // ─────────────────────────────────────────────────────────────────────────
    // 性能控制 — 功耗
    // ─────────────────────────────────────────────────────────────────────────
    public static string NotSet => T("不设置", "不設定", "Not Set");
    public static string Maximum => T("最大", "最大", "Maximum");
    public static string Enable => T("开启", "開啟", "Enable");
    public static string Disable => T("关闭", "關閉", "Disable");
    public static string Normal => T("正常", "正常", "Normal");
    public static string LowPower => T("低功耗", "低功耗", "Low Power");
    public static string Unlimited => T("无限制", "無限制", "Unlimited");

    public static string IccMaxMenu => T("IccMax", "IccMax", "IccMax");
    public static string AcLoadLineMenu => T("AC Load Line", "AC Load Line", "AC Load Line");
    public static string CpuPowerMenu => T("CPU功率", "CPU功率", "CPU Power");
    public static string SetCpuPowerSlider => T("拖动滑块设置功率 (W)", "拖動滑桿設定功率 (W)", "Drag slider to set power (W)");
    public static string CurrentSliderValueTemp => T("滑块值：{0}", "滑桿值：{0}", "Slider Value: {0}");
    public static string SetFanSpeedSlider => T("拖动滑块设置转速 (RPM)", "拖動滑桿設定轉速 (RPM)", "Drag slider to set speed (RPM)");
    public static string SetTppSlider => T("拖动滑块设置功率 (W)", "拖動滑桿設定功率 (W)", "Drag slider to set power (W)");
    public static string SetGpuClockSlider => T("拖动滑块设置频率 (MHz)", "拖動滑桿設定頻率 (MHz)", "Drag slider to set clock (MHz)");
    public static string SetMaxFrameRateSlider => T("拖动滑块设置最大帧率 (FPS)", "拖動滑桿設定最大幀率 (FPS)", "Drag slider to set max frame rate (FPS)");
    public static string PpabPowerMenu => T("PPab条件(Tpp)", "PPab條件(Tpp)", "PPab (Tpp)");
    public static string DStateSubMenu => T("dState", "dState", "dState");
    public static string DbVersionMenu => T("DB版本", "DB版本", "DB Version");
    public static string DbNormal => T("普通版本", "普通版本", "Normal");
    public static string DbUnlocked => T("解锁版本", "解鎖版本", "Unlocked");
    public static string GpuClockMenu => T("GPU频率限制", "GPU頻率限制", "GPU Clock Limit");
    public static string MaxFrameRateMenu => T("最大帧率", "最大幀率", "Max Frame Rate");

    // DB 解锁相关
    public static string DbUnlockCpuHighWarning => T("请在CPU低负载下解锁",
        "請在CPU低負載下解鎖", "Please unlock under low CPU load.");
    public static string DbUnlockFailed(float w) => T(
        $"功耗异常，解锁失败，请重新尝试！\n当前显卡功耗限制为：{w:F2} W ！",
        $"功耗異常，解鎖失敗，請重新嘗試！\n當前顯示卡功耗限制為：{w:F2} W！",
        $"Power limit anomaly. Unlock failed. Current GPU power limit: {w:F2} W. Please retry.");
    public static string DbUnlockSuccessNoAutoStart => T(
        "解锁成功！但当前未设置开机自启，解锁后若重启电脑会导致功耗异常，需要重新解锁！",
        "解鎖成功！但目前未設定開機自啟，重啟電腦後功耗將恢復限制，需重新解鎖！",
        "Unlock successful! However, autostart is not enabled. Rebooting will reset the power limit and require re-unlocking.");
    public static string Hint => T("提示", "提示", "Info");
    public static string Warning => T("警告", "警告", "Warning");
    public static string Error => T("错误", "錯誤", "Error");

    // 高温警告（气泡）
    public static string HighTempBalloonTitle => T("温度过高警告", "溫度過高警告", "High Temperature Warning");
    public static string HighTempBalloonText(int limit, float temp) => T(
        $"检测到CPU温度高于{limit - 5}℃ ({temp:F1}℃)，且风扇处于固定转速状态，OSH已自动切换为降温模式并将风扇控制切换为自动模式。",
        $"偵測到CPU溫度高於{limit - 5}℃ ({temp:F1}℃)，且風扇處於固定轉速狀態，OSH已自動切換至降溫模式並將風扇控制改為自動。",
        $"CPU temperature exceeded {limit - 5}°C ({temp:F1}°C) with a fixed fan speed. OSH has switched to Cool mode and Auto fan control.");

    // ─────────────────────────────────────────────────────────────────────────
    // 性能控制 — 提示文本
    // ─────────────────────────────────────────────────────────────────────────
    public static string PerfCpuPowerTip => T("💡同时控制PL1与PL2，选择最大或过高的数值不一定生效，建议优先选择合适的数值而不是无脑最大。",
        "💡同時控制PL1與PL2，選擇最大或過高的數值不一定生效，建議優先選擇合適的數值而不是無腦最大。",
        "💡 Controls both PL1 and PL2. Setting maximum or very high values may not take effect. Choose an appropriate value.");

    public static string PerfTgpTip => T("💡关闭可降低GPU最大功耗。",
        "💡關閉可降低GPU最大功耗。",
        "💡 Disable to reduce GPU max power.");

    public static string PerfPpabTip => T("💡关闭可降低GPU最大功耗。",
        "💡關閉可降低GPU最大功耗。",
        "💡 Disable to reduce GPU max power.");

    public static string PerfTppTip => T("💡改变Ppab/DB增益点，即 GPU 功率在 CPU 功率低于多少时获得额外的Ppab/DB功耗。",
        "💡改變Ppab/DB增益點，即 GPU 功率在 CPU 功率低於多少時獲得額外的Ppab/DB功耗。",
        "💡 Adjusts the Ppab/DB gain point: the CPU power threshold below which GPU gets additional Ppab/DB power.");

    public static string PerfDStateTip => T("💡选择低功耗将把GPU功率限制在一个较低水平。",
        "💡選擇低功耗將把GPU功率限制在一個較低水平。",
        "💡 Low power mode restricts GPU power to a lower level.");

    public static string PerfMaxFrameRateTip => T("💡设置GPU将渲染的最大3D游戏或应用程序帧速率的最大帧速率，0即无限制",
        "💡設置GPU將渲染的最大3D遊戲或應用程序幀速率的最大幀速率，0即無限制",
        "💡 Sets the maximum frame rate for GPU rendering of 3D games or applications, 0 means no limit.");

    public static string PerfDbTip => T("💡你的设备支持Ppab条件更改，请优先选择增大Ppab条件中的功率而不是更改DB版本，两者效果相同。",
        "💡你的設備支援Ppab條件更改，請優先選擇增大Ppab條件中的功率而不是更改DB版本，兩者效果相同。",
        "💡 Your device supports Ppab condition adjustment. Prefer increasing Ppab condition power over changing DB version — same effect.");

    public static string PerfDbUnlockWarning => T("\n警告：一旦解锁DB，只能通过安装一次显卡驱动恢复到原始状态，确认继续吗？",
        "\n警告：一旦解鎖DB，只能透過安裝一次顯示卡驅動恢復到原始狀態，確認繼續嗎？",
        "\nWarning: Once DB is unlocked, you can only restore to original state by reinstalling graphics driver once. Continue?");

    public static string PerfDbUnlockTooltip => T("解锁DB可以在CPU功率较高时避免GPU功率降低。",
        "解鎖DB可以在CPU功率較高時避免GPU功率降低。",
        "Unlocking DB prevents GPU power reduction when CPU power is high.");

    public static string PerfDbNormalTooltip => T("该选项可以重新恢复系统分配功耗的状态，但若在不支持的显卡（例如50系）上解锁过则必须通过安装一次显卡驱动来完全恢复。",
        "該選項可以重新恢復系統分配功耗的狀態，但若在不支援的顯示卡（例如50系）上解鎖過則必須透過安裝一次顯示卡驅動來完全恢復。",
        "This option restores the system's default power allocation. If you previously unlocked on an unsupported GPU (e.g., 50 series), a driver reinstall is required for full recovery.");

    // ─────────────────────────────────────────────────────────────────────────
    // DB 解锁相关对话框
    // ─────────────────────────────────────────────────────────────────────────
    public static string DbUnlockTitle => T("解锁DB", "解鎖DB", "Unlock DB");
    public static string PleaseConnectAC => T("请连接交流电源", "請連接交流電源", "Please connect AC power");

    // ─────────────────────────────────────────────────────────────────────────
    // 浮窗字体大小
    // ─────────────────────────────────────────────────────────────────────────
    public static string SetTextSizeSlider => T("拖动滑块设置字号", "拖動滑塊設置字號", "Drag the slider to set font size");

    // ─────────────────────────────────────────────────────────────────────────
    // 灯光控制 — UI 文本
    // ─────────────────────────────────────────────────────────────────────────
    public static string LightingWmiProtocol => T("WMI 协议", "WMI 協議", "WMI Protocol");
    public static string LightingWmiTip => T("💡 仅当灯光控制无效时，可尝试更改此设置。",
        "💡 僅當燈光控制無效時，可嘗試更改此設定。",
        "💡 Try changing this if lighting control is not working.");
    public static string LightingProtocolBasic => T("四分区", "四分割區", "Basic 4-Zone");
    public static string LightingProtocolDojo => T("Dojo四分区", "Dojo四分割區", "Dojo 4-Zone");
    public static string LightingBrightness => T("亮度", "亮度", "Brightness");
    public static string LightingStaticColor => T("静态颜色", "靜態顏色", "Static Color");
    public static string LightingAllZones => T("全局颜色", "全局顏色", "All Zones");
    public static string LightingZone => T("分区", "分割區", "Zone");
    public static string LightingCustom => T("自定义...", "自訂...", "Custom...");
    public static string LightingAnimation => T("动画效果", "動畫效果", "Animation");
    public static string LightingEffect => T("效果", "效果", "Effect");
    public static string LightingSpeed => T("速度", "速度", "Speed");
    public static string LightingDirection => T("方向", "方向", "Direction");
    public static string LightingTheme => T("主题", "主題", "Theme");
    public static string LightingColorRed => T("红色", "紅色", "Red");
    public static string LightingColorGreen => T("绿色", "綠色", "Green");
    public static string LightingColorBlue => T("蓝色", "藍色", "Blue");
    public static string LightingColorWhite => T("白色", "白色", "White");
    public static string LightingColorCyan => T("冰蓝", "冰藍", "Cyan");
    public static string LightingColorMagenta => T("粉色", "粉色", "Pink");
    public static string LightingColorYellow => T("黄色", "黃色", "Yellow");
    // 动画效果名称
    public static string LightingAnimColorCycle => T("色彩循环", "色彩循環", "Color Cycle");
    public static string LightingAnimStarlight => T("星光", "星光", "Starlight");
    public static string LightingAnimBreathing => T("呼吸", "呼吸", "Breathing");
    public static string LightingAnimWave => T("波浪", "波浪", "Wave");
    public static string LightingAnimRaindrop => T("雨滴", "雨滴", "Raindrop");
    public static string LightingAnimAudioPulse => T("音频脉冲", "音頻脈衝", "Audio Pulse");
    public static string LightingAnimConfetti => T("五彩纸屑", "五彩紙屑", "Confetti");
    public static string LightingAnimSun => T("太阳", "太陽", "Sun");
    public static string LightingAnimSwipe => T("划过", "劃過", "Swipe");
    // Dojo 专用
    public static string LightingDirLeft => T("左/逆时针", "左/逆時針", "Left/Counterclockwise");
    public static string LightingDirRight => T("右/顺时针", "右/順時針", "Right/Clockwise");
    public static string LightingThemeGalaxy => T("银河", "銀河", "Galaxy");
    public static string LightingThemeVolcano => T("火山", "火山", "Volcano");
    public static string LightingThemeJungle => T("丛林", "叢林", "Jungle");
    public static string LightingThemeOcean => T("海洋", "海洋", "Ocean");
    public static string LightingThemeCustom => T("自定义", "自訂", "Custom");
    // 速度
    public static string LightingSpeedSlow => T("慢", "慢", "Slow");
    public static string LightingSpeedMedium => T("中", "中", "Medium");
    public static string LightingSpeedFast => T("快", "快", "Fast");

    // ─────────────────────────────────────────────────────────────────────────
    // 灯光控制 — 状态显示
    // ─────────────────────────────────────────────────────────────────────────
    public static string LightingBrightnessStatus => T("亮度", "亮度", "Brightness");
    public static string LightingAnimationStatus => T("动画效果", "動畫效果", "Animation");
    public static string LightingAnimationNone => T("无", "無", "None");

    public static string LightingZoneTag => T("分区", "分區", "Zone");

    public static string LightingSingleZoneColor => T("颜色", "顏色", "Color");

    // ─────────────────────────────────────────────────────────────────────────
    // 灯光控制 — 开关菜单项
    // ─────────────────────────────────────────────────────────────────────────
    public static string LightingOn => T("开", "開", "On");
    public static string LightingOff => T("关", "關", "Off");

    // ─────────────────────────────────────────────────────────────────────────
    // 灯光控制 — 单键 RGB
    // ─────────────────────────────────────────────────────────────────────────
    public static string LightingPerKeyTitle => T("单键 RGB（测试功能）", "單鍵 RGB（測試功能）", "Per-Key RGB (Experimental)");
    public static string LightingPerKeyStaticColor => T("静态颜色", "靜態顏色", "Static Color");
    public static string LightingPerKeyAnimation => T("动画效果", "動畫效果", "Animation");
    public static string LightingPerKeyBrightness => T("亮度", "亮度", "Brightness");

    // ─────────────────────────────────────────────────────────────────────────
    // 灯光控制 — 四分区/灯条标题
    // ─────────────────────────────────────────────────────────────────────────
    public static string LightingFourZoneKeyboard => T("四分区/单分区键盘", "四分割區/單分割區鍵盤", "4-Zone/1-Zone Keyboard");
    public static string LightingLightBar => T("灯条（测试功能）", "燈條（測試功能）", "Light Bar (Experimental)");
    public static string LightingBrightnessRangeTip => T("💡 亮度范围可能为0~100，也可能为100关228开",
        "💡 亮度範圍可能為0~100，也可能為100關228開",
        "💡 Brightness range may be 0-100, or 100=off, 228=on");

    // ─────────────────────────────────────────────────────────────────────────
    // 系统信息中的 NVIDIA 功率限制动态文本
    // ─────────────────────────────────────────────────────────────────────────
    public static string SysNvidiaPowerLimitText(string limitsText) => T(
        $"NVIDIA 功率限制: {limitsText}",
        $"NVIDIA 功率限制: {limitsText}",
        $"NVIDIA Power Limit: {limitsText}");

    // ─────────────────────────────────────────────────────────────────────────
    // 键盘灯光类型名称
    // ─────────────────────────────────────────────────────────────────────────
    public static string KbTypeNormal => T("普通", "普通", "Normal");
    public static string KbTypeFourZoneWithNumpad => T("四分区带小键盘", "四分割區帶數字鍵", "4-Zone with Numpad");
    public static string KbTypeFourZoneWithoutNumpad => T("四分区无小键盘", "四分割區無數字鍵", "4-Zone without Numpad");
    public static string KbTypeRgbPerKey => T("单键 RGB", "單鍵 RGB", "Per-Key RGB");
    public static string KbTypeOneZoneWithNumpad => T("单分区带小键盘", "單分割區帶數字鍵", "1-Zone with Numpad");
    public static string KbTypeOneZoneWithoutNumpad => T("单分区无小键盘", "單分割區無數字鍵", "1-Zone without Numpad");
    public static string KbTypeUnknown => T("未知或不支持", "未知或不支援", "Unknown/Unsupported");

    // ─────────────────────────────────────────────────────────────────────────
    // 硬件监控
    // ─────────────────────────────────────────────────────────────────────────
    public static string MonitorCpuOn => T("开启CPU监控", "開啟CPU監控", "Enable CPU Monitor");
    public static string MonitorCpuOff => T("关闭CPU监控", "關閉CPU監控", "Disable CPU Monitor");
    public static string MonitorGpuOn => T("开启GPU监控", "開啟GPU監控", "Enable GPU Monitor");
    public static string MonitorGpuOff => T("关闭GPU监控", "關閉GPU監控", "Disable GPU Monitor");
    public static string MonitorFanOn => T("开启风扇监控", "開啟風扇監控", "Enable Fan Monitor");
    public static string MonitorFanOff => T("关闭风扇监控", "關閉風扇監控", "Disable Fan Monitor");
    public static string MonitorRefresh => T("刷新频率", "更新頻率", "Refresh Rate");
    public static string MonitorRefreshHigh => T("高", "高", "High");
    public static string MonitorRefreshLow => T("低", "低", "Low");
    public static string TempDisplay => T("温度显示", "溫度顯示", "Temp Display");
    public static string TempSmoothed => T("平滑值", "平滑值", "Smoothed");
    public static string TempRaw => T("原始值", "原始值", "Raw");
    public static string MonitorClosed => T("监控已关闭", "監控已關閉", "Monitor Disabled");

    // ─────────────────────────────────────────────────────────────────────────
    // 浮窗
    // ─────────────────────────────────────────────────────────────────────────
    public static string FloatingShow => T("显示浮窗", "顯示浮窗", "Show Overlay");
    public static string FloatingHide => T("关闭浮窗", "關閉浮窗", "Hide Overlay");
    public static string FloatingLocLeft => T("左上角", "左上角", "Top Left");
    public static string FloatingLocRight => T("右上角", "右上角", "Top Right");
    public static string FloatingScreen => T("显示器选择", "顯示器選擇", "Display");
    public static string FloatingScreenPrimary => T("主屏幕", "主螢幕", "Primary");

    // ─────────────────────────────────────────────────────────────────────────
    // Omen Key
    // ─────────────────────────────────────────────────────────────────────────
    public static string OmenKeyDefault => T("默认", "預設", "Default");
    public static string OmenKeyToggle => T("切换浮窗显示", "切換浮窗顯示", "Toggle Overlay");
    public static string OmenKeySwitchPreset => T("切换预设", "切換預設", "Switch Preset");
    public static string OmenKeyPresetBalloonTitle => T("已切换预设", "已切換預設", "Preset Switched");
    public static string OmenKeyPresetBalloonText(string name) => T(
        $"当前预设：{name}",
        $"目前預設：{name}",
        $"Current preset: {name}");
    public static string OmenKeyLaunchApp => T("打开应用", "開啟應用程式", "Open App");
    public static string OmenKeyShortcut => T("模拟按键", "模擬按鍵", "Simulate Shortcut");
    public static string OmenKeySelectApp => T("选择应用", "選擇應用程式", "Select App");
    public static string OmenKeySelectDesktopApp => T("选择桌面应用", "選擇桌面應用程式", "Select Desktop App");
    public static string OmenKeySelectUwpApp => T("选择UWP应用", "選擇UWP應用程式", "Select UWP App");
    public static string OmenKeyClearApp => T("清除应用绑定", "清除應用程式綁定", "Clear App Binding");
    public static string OmenKeyCurrentApp => T("当前应用", "目前應用程式", "Current App");
    public static string OmenKeyCurrentShortcut => T("当前模拟按键", "目前模擬按鍵", "Current Shortcut");
    public static string OmenKeyNoAppSelected => T("未选择", "未選擇", "Not Selected");
    public static string OmenKeyNoShortcutSelected => T("未设置", "未設定", "Not Set");
    public static string OmenKeyAppFilter => T(
        "应用程序和快捷方式|*.exe;*.lnk;*.bat;*.cmd|所有文件|*.*",
        "應用程式和捷徑|*.exe;*.lnk;*.bat;*.cmd|所有檔案|*.*",
        "Applications and shortcuts|*.exe;*.lnk;*.bat;*.cmd|All files|*.*");
    public static string OmenKeyAppNotFound => T(
        "未找到已绑定的应用，请重新选择。",
        "找不到已綁定的應用程式，請重新選擇。",
        "The bound app was not found. Please select it again.");
    public static string OmenKeyAppLaunchFailed(string msg) => T(
        $"打开应用失败：{msg}",
        $"開啟應用程式失敗：{msg}",
        $"Failed to open app: {msg}");
    public static string OmenKeyUwpAppName => T("应用名称", "應用程式名稱", "App Name");
    public static string OmenKeyUwpNoApps => T(
        "没有找到可启动的 UWP 应用。",
        "找不到可啟動的 UWP 應用程式。",
        "No launchable UWP apps were found.");
    public static string OmenKeyUwpLoadFailed(string msg) => T(
        $"读取 UWP 应用列表失败：{msg}",
        $"讀取 UWP 應用程式清單失敗：{msg}",
        $"Failed to load UWP app list: {msg}");
    public static string OmenKeySetShortcut => T("设置模拟按键", "設定模擬按鍵", "Set Simulated Shortcut");
    public static string OmenKeyShortcutCapturePrompt => T(
        "请按下要模拟的按键或组合键。",
        "請按下要模擬的按鍵或組合鍵。",
        "Press the key or shortcut to simulate.");
    public static string OmenKeyClearShortcut => T("清除模拟按键", "清除模擬按鍵", "Clear Simulated Shortcut");
    public static string OmenKeyShortcutNotSet => T(
        "未设置要模拟的按键。",
        "未設定要模擬的按鍵。",
        "No simulated shortcut has been set.");
    public static string OmenKeyShortcutSendFailed(int error) => T(
        $"模拟按键发送失败，错误码：{error}",
        $"模擬按鍵傳送失敗，錯誤碼：{error}",
        $"Failed to send simulated shortcut. Error code: {error}");
    public static string OmenKeyNone => T("取消绑定", "取消綁定", "Unbound");

    // ─────────────────────────────────────────────────────────────────────────
    // 其他设置
    // ─────────────────────────────────────────────────────────────────────────
    public static string IconMenu => T("图标", "圖示", "Icon");
    public static string IconOriginal => T("原版", "原版", "Default");
    public static string IconCustom => T("自定义图标", "自訂圖示", "Custom Icon");
    public static string IconDynamic => T("动态图标", "動態圖示", "Dynamic Icon");
    public static string DataLocalize => T("数据本地化", "資料本地化", "Data Localize");
    public static string AutoStart => T("开机自启", "開機自啟", "Autostart");

    // ─────────────────────────────────────────────────────────────────────────
    // 系统信息标签
    // ─────────────────────────────────────────────────────────────────────────
    public static string SysModelName => T("机型名称", "機型名稱", "Product Name");
    public static string SysModelValidation => T("机型支持情况", "機型支持情況", "Product Validation");
    public static string ValidationGamingProduct => T("完全支持", "完全支持", "Fully supported");
    public static string ValidationOldOmenProduct => T("旧 OMEN 機型", "舊 OMEN 機型", "Old Omen Product");
    public static string ValidationUnsupportedHPProduct => T("不支持的 HP 机型", "不支援的 HP 機型", "Unsupported HP Product");
    public static string ValidationUnsupported => T("不支持的机型", "不支援的 HP 機型", "Unsupported HP Product");
    public static string SysBoardProduct => T("主板产品号", "主機板型號", "Board Product");
    public static string SysBiosVersion => T("BIOS 版本", "BIOS 版本", "BIOS Version");
    public static string SysPawnIOState => T("PawnIO 状态", "PawnIO 狀態", "PawnIO State");
    public static string SysPawnIONormol => T("正常", "正常", "Normal");
    public static string SysPawnIONotInstalled => T("未安装", "未安裝", "Not Installed");
    public static string SysCpu => T("CPU", "CPU", "CPU");
    public static string SysCpuTjMax => T("CPU温度墙", "CPU溫度上限", "CPU Tjmax");
    public static string SysGpu => T("显卡", "顯示卡", "GPU");
    public static string SysGpuUnknown => T("显卡: 未知", "顯示卡: 未知", "GPU: Unknown");
    public static string SysGpuList => T("显卡:", "顯示卡:", "GPU:");
    public static string SysNvidiaTjMax => T("NVIDIA 温度墙", "NVIDIA 溫度上限", "NVIDIA Tjmax");
    public static string SysNvidiaPower => T("NVIDIA 功率限制", "NVIDIA 功率限制", "NVIDIA Power Limit");
    public static string SysIRSensor => T("IR传感器", "IR感測器", "IR Sensor");
    public static string SysAmbient => T("环境传感器", "環境感測器", "Ambient Sensor");
    public static string SysPCH => T("PCH传感器", "PCH感測器", "PCH Sensor");
    public static string SysVR => T("VR传感器", "VR感測器", "VR Sensor");
    public static string SysKbType => T("键盘灯光类型", "鍵盤燈光類型", "KB Light Type");
    public static string SysAdapterPower => T("原装适配器功率", "原裝充電器功率", "Adapter Wattage");

    // ─────────────────────────────────────────────────────────────────────────
    // 错误/警告对话框
    // ─────────────────────────────────────────────────────────────────────────
    // ─────────────────────────────────────────────────────────────────────────
    // 性能控制 — GPU 功率控制菜单
    // ─────────────────────────────────────────────────────────────────────────
    public static string GpuPowerControlMenu => T("GPU功率控制", "GPU功率控制", "GPU Power Control");

    // ─────────────────────────────────────────────────────────────────────────
    // DB 解锁 — 50 系不支持提示
    // ─────────────────────────────────────────────────────────────────────────
    public static string DbNo50Series => T("不支持英伟达50系及以后的显卡解锁DB！",
        "不支援 NVIDIA 50 系列及以後的顯示卡解鎖 DB！",
        "Unlocking DB is not supported for NVIDIA 50 series and later GPUs!");

    // ─────────────────────────────────────────────────────────────────────────
    // 灯光控制菜单
    // ─────────────────────────────────────────────────────────────────────────
    public static string LightingControl => T("灯光控制", "燈光控制", "Lighting Control");

    // ─────────────────────────────────────────────────────────────────────────
    // 硬件监控 — 子菜单标签
    // ─────────────────────────────────────────────────────────────────────────
    public static string MonitorCpuLabel => T("CPU", "CPU", "CPU");
    public static string MonitorGpuLabel => T("GPU", "GPU", "GPU");
    public static string MonitorFanLabel => T("风扇", "風扇", "Fan");

    // ─────────────────────────────────────────────────────────────────────────
    // 硬件监控 — 自动转速模式下无法关闭监控的警告
    // ─────────────────────────────────────────────────────────────────────────
    public static string MonitorAutoFanWarning => T("当前为自动转速模式，若要关闭监控需切换为其他转速控制模式。",
        "目前為自動轉速模式，若要關閉監控需切換為其他轉速控制模式。",
        "Fan is in auto mode. Switch to another fan control mode before disabling monitoring.");

    // ─────────────────────────────────────────────────────────────────────────
    // GPU 监控自动启停气泡提示
    // ─────────────────────────────────────────────────────────────────────────
    public static string GpuAutoStopTitle => T("状态更改提示", "狀態更改提示", "Status Change");

    public static string GpuAutoStopText => T(
        "检测到显卡进入低功耗状态，OSH已停止监控GPU以节约能源。\n手动打开GPU监控后，本次将不再自动停止监控GPU。",
        "偵測到顯示卡進入低功耗狀態，OSH已停止監控GPU以節約能源。\n手動開啟GPU監控後，本次將不再自動停止監控GPU。",
        "GPU entered low‑power state. OSH has stopped monitoring GPU to save energy.\nIf you manually enable GPU monitoring, it will not be automatically stopped again.");

    public static string GpuAutoStartText => T(
        "检测到显卡连接到显示器，OSH已开始监控GPU。\n手动关闭GPU监控后，本次将不再自动开始监控GPU。",
        "偵測到顯示卡連接到顯示器，OSH已開始監控GPU。\n手動關閉GPU監控後，本次將不再自動開始監控GPU。",
        "GPU is now connected to a display. OSH has started monitoring GPU.\nIf you manually disable GPU monitoring, it will not be automatically started again.");

    // ─────────────────────────────────────────────────────────────────────────
    // AC Load Line 气泡提示
    // ─────────────────────────────────────────────────────────────────────────
    public static string AcLoadLineBalloonTitle => T("AC Load Line 提示", "AC Load Line 提示", "AC Load Line Hint");
    public static string AcLoadLineBalloonText(int maxSupported, int current) => T(
        $"当前设备支持的最大 AC Load Line 为 {180 - 10 * maxSupported}，将尝试设置 {180 - 10 * current}。",
        $"目前裝置支援的最大 AC Load Line 為 {180 - 10 * maxSupported}，將嘗試設定 {180 - 10 * current}。",
        $"The maximum supported AC Load Line for this device is {180 - 10 * maxSupported}. Attempting to set {180 - 10 * current}.");
    public static string NoCustomIcon => T("不存在自定义图标custom.ico",
        "找不到自訂圖示 custom.ico", "Custom icon file custom.ico not found.");
    public static string KeyboardConnectFail => T("键盘连接失败！", "鍵盤連線失敗！", "Keyboard connection failed!");
    public static string CrashMessage => T(
        $"OSH出现意外错误，详细信息请查看{Logger.logFileName}，报告问题时请附带此日志。",
        $"OSH發生意外錯誤，詳細資訊請查看 {Logger.logFileName}，回報問題時請附上此日誌。",
        $"OSH encountered an unexpected error. See {Logger.logFileName} for details. Please attach this log when reporting.");
    public static string DdsInitFail => T(
        "无法初始化 Advanced Optimus 小程序。请确保所有NVIDIA驱动程序均为最新版本，并将BIOS设置菜单中的“图形模式”设置为\"Nvidia Advanced Optimus\"。",
        "無法初始化 Advanced Optimus 小程式。請確認所有 NVIDIA 驅動程式均為最新版本，並在 BIOS 設定中將「圖形模式」設為 \"Nvidia Advanced Optimus\"。",
        "Failed to initialize the Advanced Optimus applet. Make sure all NVIDIA drivers are up to date and set the Graphics Mode to \"Nvidia Advanced Optimus\" in BIOS.");

    // ─────────────────────────────────────────────────────────────────────────
    // HelpForm
    // ─────────────────────────────────────────────────────────────────────────
    public static string HelpWindowTitle => T(" 帮助", " 說明", " Help");

    // ─────────────────────────────────────────────────────────────────────────
    // 内部辅助
    // ─────────────────────────────────────────────────────────────────────────
    private static string T(string zh, string tw, string en) {
      switch (Current) {
        case AppLanguage.TraditionalChinese: return tw;
        case AppLanguage.English: return en;
        default: return zh;  // SimplifiedChinese
      }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GpuAppManager 相关
    // ─────────────────────────────────────────────────────────────────────────
    public static string CheckDriverFailed => T("查询显卡驱动失败", "查詢顯示卡驅動失敗", "Failed to query graphics driver");
    public static string DriverNotFound => T("无法找到 NVIDIA 显卡驱动版本", "無法找到 NVIDIA 顯示卡驅動版本", "Cannot find NVIDIA graphics driver version");
    public static string DriverNotAllow => T("NVIDIA 显卡驱动版本≥537.42且<610.47才支持解锁DB！当前版本：", "NVIDIA 顯示卡驅動版本≥537.42且<610.47才支援解鎖DB！目前版本：", "Only NVIDIA graphics card driver versions ≥ 537.42 and < 610.47 support DB unlocking! Current version:");
    public static string DeviceNotFound => T("未找到描述包含 NVIDIA 的显示适配器！", "未找到描述包含 NVIDIA 的顯示卡！", "Display adapter containing 'NVIDIA' not found!");
    public static string RestartGPUSuccess => T("重启显卡成功！", "重啟顯示卡成功！", "Restart GPU successful!");
    public static string RestartGPUFailed => T("重启显卡失败！", "重啟顯示卡失敗！", "Failed to restart GPU!");
  }
}
