# 🎨 现代窗口界面集成指南

## ✅ 已创建的文件

已在您的仓库中创建了 `ModernMainForm.cs`，包含一个现代化的、具有以下特性的完整窗口界面：

### 📊 功能特性

#### 1️⃣ **实时监控选项卡**
- 三个彩色监控卡片：CPU、GPU、风扇
- 实时显示温度、功率、转速数据
- 温度趋势图表（支持 CPU/GPU 双线显示）
- 自动更新机制

#### 2️⃣ **风扇控制选项卡**
- 风扇模式下拉菜单（自动/性能/平衡/安静/自定义）
- 风扇速度滑块（0-100%）
- 三个快捷预设按钮（性能/平衡/安静）

#### 3️⃣ **设置选项卡**
- CPU 功耗限制滑块（10-100W）
- GPU 功耗限制滑块（10-150W）
- 监控选项复选框（CPU/GPU/风扇）

#### 4️⃣ **高级选项卡**
- 为未来功能预留

---

## 🔧 集成步骤

### 第一步：修改 Program.cs

在 `Program.cs` 的 `Main` 方法中，将托盘模式改为窗口模式：

**原代码（第 102-268 行）**
```csharp
static void Main(string[] args) {
  // ... 初始化代码 ...
  
  InitTrayIcon();  // ← 删除这一行
  
  // ... 其他代码 ...
}
```

**修改后的代码**
```csharp
static void Main(string[] args) {
  // ... 初始化代码保持不变 ...
  
  // 替换 InitTrayIcon() 为显示现代窗口
  Application.Run(ModernMainForm.Instance);
}
```

### 第二步：更新数据绑定

在 `Program.cs` 中找到 `UpdateTooltip()` 方法（第 854-955 行），添加对窗口的数据更新：

```csharp
static void UpdateTooltip() {
  try {
    QueryHardware();
  } catch (Exception ex) {
    Logger.Error($"[UpdateTooltip] QueryHardware 异常: {ex.Message}");
  }

  // ✅ 添加这行：更新现代窗口的数据
  if (ModernMainForm.Instance != null && !ModernMainForm.Instance.IsDisposed) {
    ModernMainForm.Instance.UpdateMonitoringData(
      CPUTemp, CPUPower, GPUTemp, GPUPower,
      (fanSpeedNow[0] + fanSpeedNow[1]) * 50
    );
  }

  // ... 其他代码保持不变 ...
}
```

### 第三步：禁用托盘初始化

注释掉或删除以下代码：

**在 Program.cs 中**
```csharp
// InitTrayIcon();  // ← 注释这行
```

**在 Program.Menu.cs 中**
如果不需要托盘菜单，可以将整个菜单初始化代码注释掉。

### 第四步：更新项目文件

在 `OmenSuperHub.csproj` 中，在 `<ItemGroup>` 中添加新窗体引用（已自动完成）。

---

## 🎯 快速集成清单

- [ ] 创建 `ModernMainForm.cs` ✅ **已完成**
- [ ] 修改 `Program.cs` 的 Main 方法
- [ ] 在 `UpdateTooltip()` 中添加数据更新调用
- [ ] 注释掉 `InitTrayIcon()`
- [ ] 编译并测试

---

## 🔄 保持托盘+窗口模式（可选）

如果您想同时保留托盘和窗口模式，可以：

1. 在 `InitTrayIcon()` 中添加菜单项用于切换显示/隐藏窗口
2. 在 `Program.cs` 中同时启动托盘和窗口：

```csharp
// 同时显示窗口和托盘
InitTrayIcon();
ModernMainForm.Instance.Show();
Application.Run();
```

---

## 📝 窗口界面特性

✨ **现代设计**
- Microsoft YaHei 字体
- 彩色卡片设计
- Tab 控制组织

⚡ **性能优化**
- 1 秒刷新频率
- 图表数据缓冲（最近 60 秒）
- 防止内存泄漏

🔗 **数据绑定**
- `UpdateMonitoringData()` 方法供 Program.cs 调用
- 自动线程安全处理
- 支持跨线程更新

---

## 🐛 常见问题

**Q: 窗口启动后出现编译错误？**
A: 确保已删除 `InitTrayIcon()` 调用，或在条件编译中禁用它。

**Q: 数据不更新？**
A: 检查是否在 `UpdateTooltip()` 中调用了 `ModernMainForm.Instance.UpdateMonitoringData()`。

**Q: 想保留托盘图标？**
A: 在 Main 方法中不删除 `InitTrayIcon()`，同时显示窗口（见"保持托盘+窗口模式"部分）。

---

## 📞 支持

如有任何问题，请检查：
1. 是否正确注释了托盘初始化代码
2. 是否正确连接了数据更新方法
3. Program.cs 中的 `Application.Run()` 是否指向新窗口

