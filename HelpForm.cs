using System;
using System.Diagnostics; // 用于打开浏览器
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace OmenSuperHub {
  public partial class HelpForm : Form {
    private static HelpForm _instance;
    public HelpForm() {
      this.TopMost = true;
      Text = "OmenSuperHub";

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
        Text = "版本号：" + version +
         "\n更新说明：\n" +
         "（1）新增功能：根据机型自动启用大师模式、Tpp、PL4、IccMax和AC Load Line设置；\n" +
         "（2）新增功能：为性能控制菜单中的Tpp、PL4、IccMax、AC Load Line和CPU功率选项添加“不设置”选项并作为默认值；\n" +
         "（3）修复：更改转速配置文件格式并使用新的默认配置方法，以支持更多机型；\n" +
         "（4）修复：修改CTGP/DB配置名称为最大GPU功率、中等GPU功率和最小GPU功率，以避免错误使用；\n" +
         "（5）修复：启动时直接调度风扇，而不会延迟一段时间；\n\n" +

         "本项目已开源至Github：https://github.com/breadeding/OmenSuperHub\n\n" +
         "一.   “风扇配置”菜单说明：\n" +
         "（1）本程序可设置两种不同的温度-转速对应配置，安静模式加载\"silent.txt\"，可以设置较为保守的风扇调度, 降温模式加载\"cool.txt\"，可以设置较为激进的风扇调度，打开silent和cool文件可自行修改风扇配置，注意使用英文逗号，格式为“60,2000,2300,50,2000,2300”，不能有空缺（可以重复设置来占位），第一列和第四列分别为CPU温度和GPU温度，后面两列为两个风扇的转速，行数不限，程序会自动进行线性插值，精度为1℃。例如，如果设置了50,3000,3400和52,3200,3600两个相邻点，程序会在51℃时设置3200和3400的转速。注意，修改后需要重新点击对应的模式才能生效；\n" +
         "（2）读取到温度变化后程序将立即设置对应的转速，为了转速变化更加平缓，“实时”，“高”，“中”和“低”分别能以从无到高的强度对温度进行平滑处理。\n\n" +

         "二.   “风扇控制”菜单说明：\n" +
         "（1）选择“自动”则程序会根据风扇配置和当前温度自动设定风扇转速，转速将被设定为cpu package温度和gpu温度对应转速的较大值；\n" +
         "（2）“最大风扇”为BIOS控制，不一定是最大转速，手动或自动有可能可以设置更高的转速；\n" +
         "（3）选择固定转速时，可能出现转速过低又忘记调整而导致温度过高的情况，因此OSH将在CPU温度高于90摄氏度时弹出通知警告并转为自动风扇模式；\n" +
         "（4）暗9笔记本转速范围0-6400，但是只有BIOS设置中关闭了风扇始终启动，才能低于2000转，请根据自己的机型设置合理的转速。\n\n" +

         "三.   “性能控制”菜单说明：\n" +
         "（1）“大师模式”、“狂暴模式”和“平衡模式”在不同的机型上作用可能不同。对于暗9，平衡模式会限制CPU PL1为55W（底层PL1，软件无法读取和修改），同时限制GPU（功耗不固定，70-110W均有可能），同时切换狂暴/平衡均会重置CPU功率设定。注意，两种模式仅影响最大性能，对省电几乎没有影响，要省电应开启混合模式避免使用独显，关闭所有监控GPU状态的程序；\n" +
         "（2）Tpp选项的官方说明（来自OGH）：增加在 CPU 和 GPU 之间分配的总功率，为 NVIDIA® Dynamic Boost 提供更大的功率容量，以提升 GPU 性能。在打开后可以调整最大功率增益。\r\n如果系统温度过高，可实现的功率增益可能会受到限制；\n" +
         "（3）IccMax选项的官方说明（来自OGH）：IccMax 是 CPU 的最大电流限值，用于指定处理器电压调节器可提供的最大电流；\n" +
         "（4）AC Load Line选项的官方说明（来自OGH）：通过调整为 CPU 提供的电压、补偿在负载条件下的电压降以及确保一致的性能和超频潜力，负载线校准可帮助保持电压稳定性。低电压水平会使 CPU 以最低电压运行，从而优化能效，但会增加系统崩溃或不稳定的风险；\n" +
         "（5）显卡功耗=BTGP（基础功耗）+CTGP（可配置功耗）+DB（动态提升功耗），DB的含义是在CPU功率较低时额外提升GPU功耗，以暗9-i9-4060为例，BTGP=80W，BTGP+CTGP=115W，默认整机功耗为170W，显卡最大功耗140W，因此CPU功耗由60W降至30W时（具体阈值取决于Tpp），显卡总功耗将由110W升至140W，即DB功耗由-5W升至25W。在默认状态下，开启CTGP和DB才能获得最大GPU性能；\n" +
         "（6）DB版本指的是设备管理器-软件设备-NVIDIA Platform Controllers and Framework的驱动版本，解锁版本使用31.0.15.3730（来自537.42显卡驱动），注意旧显卡驱动不支持更新的DB驱动，否则会导致显卡锁定基础功耗，请使用最新显卡驱动；\n" +
         "（7）点击“解锁版本”，程序会删除解锁版本之外的DB驱动，只使用537.42对应DB驱动，然后自动启用再禁用驱动完成解锁，这会让显卡锁定当前功耗状态（必须为40系及以下显卡），即BTGP、CTGP和DB，利用这一点可以绕开CPU功率较高时DB功率降低的限制。点击“普通版本”会重新启用上述驱动，GPU功耗将取消锁定。注意更新NVIDIA显卡驱动后会更新DB驱动，需要重新解锁；\n" +
         "（8）由于其锁定功耗的特性，OSH会短暂地设置狂暴+CTGP开+DB开进行自动解锁。同样地，请注意不要在CPU高负载时切换解锁版本，否则也会导致解锁后只有CTGP开+DB关的功耗。系统重启后解锁会失效造成功耗限制为基础功耗，需要由软件自动完成启用再禁用驱动的操作恢复解锁状态，因此使用解锁功能最好打开OSH开机自启；\n" +
         "（9）如果出现提示GPU功耗异常无法解锁，则是因为当前GPU功耗被异常限制，请尝试重新解锁；\n" +
         "（10）修改PL4会影响CPU最大功耗和整机功耗，且不一定完全等同于功耗；\n" +
         "（11）修改CPU功率会同时修改PL1与PL2，点击一次只设定一次，因此同时使用ThrottleStop控制会导致设置被覆盖；\n" +
         "（12）修改GPU频率限制能实现限制不同级别的功耗，效果相当于小飞机中拉平曲线，注意该功能不是超频功能。\n\n" +

         "四.   “硬件监控”菜单说明：\n" +
         "（1）可选择开启或关闭对应的监控信息，注意如果使用混合模式，应关闭GPU监控，否则可能会导致因频繁开启/关闭GPU造成CPU占用高。\n\n" +

         "五.   “浮窗显示”菜单说明：\n" +
         "（1）开启“浮窗显示”后，屏幕上方将覆盖硬件监控信息，1秒刷新一次。\n\n" +

         "六.   “Omen键”菜单说明：\n" +
         "（1）若选择“默认”，Omen键绑定的事件为任务计划程序的“Omen Key”任务，修改其启动程序可以自定义想要启动的程序；\n" +
         "（2）若选择“切换浮窗显示”，Omen键绑定的事件为启动OSH，通过传递特定参数使OSH获得指令并切换浮窗显示；\n" +
         "（3）注意，Omen键功能可能与某些hp服务有关，禁用某些hp服务可能导致无法使用Omen键；\n" +
         "（4）若选择“取消绑定”，Omen键将无效。\n\n" +

         "七.   “其他设置”菜单说明：\n" +
         "（1）“原版”图标为程序自带图标；\n" +
         "（2）“自定义图标”需要在程序所在文件夹存放custom.ico图标文件才能使用；\n" +
         "（3）“动态图标”会以当前CPU温度作为图标，1秒刷新一次；\n" +
         "（4）“数据本地化”开启后会将CPU、GPU温度数据及风扇转速以Txt文本保存到本地，与程序本地位于相同位置，可用于Macro Deck实现在其他设备上查看电脑状态；\n" +
         "（5）设置开机自启后，程序会自动创建任务计划程序实现开机自启，如果有自己设定过则应删除。同时，旧版OSH使用的自启方式也会在这一步被自动清除；\n" +
         "（6）关闭开机自启后，程序会清除任务计划程序。\n\n",


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
  }
}
