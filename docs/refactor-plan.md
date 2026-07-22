# MettlerDataCollection 重构计划 (v2)

> 状态：**待评审 / 待实施**
> 制定日期：2026-07-22
> 覆盖：架构层重构 + 一次性 MVVM 化

---

## 1. 背景与目标

### 1.1 现状问题

`MainWindow.xaml.cs` 660 行同时承担 9 件事（串口、解析、绘图、落盘、状态机、UI 事件…）。
更关键的是：协议解析代码在 `MainWindow` 和 `RecoverData` 里**几乎原样 copy 了一份**，后续维护会分裂。

### 1.2 已完成的预备工作

| 阶段 | 状态 | commit |
|------|------|--------|
| 抽 `IDataPersistenceService` | ✅ 已完成 | `e890998` |
| 删 `DiscardOutBuffer` 死调用 + 时序注释 | ✅ 已完成 | `35b0095` |

### 1.3 重构目标（用户原话）

> "界面和逻辑分离" + "数据进来后按行分割" + "每行数据两条路径（落盘 + Device 解析）" + "Device 返回统一格式"

按此设计出 3 个职责清晰的层次（**串口直接由 MainWindow 持有，不抽 ISerialPortService；两条路径在 MainWindow 里直接 fan-out**）：

```
┌──────────┐ bytes  ┌─────────────────────────────────────────────┐
│ 串口设备 │ ─────→ │ MainWindow（直接持有 SerialPort）            │
└──────────┘        │  chunk = _serialPort.ReadExisting()          │
                    │  foreach line in _s470K.PreprocessData(chunk) │
                    │     ├→ _persistence.WriteRecord(line)         │  ← 路径 1：raw 存盘
                    │     └→ _s470K.ParseData(line)                 │  ← 路径 2：解析
                    │             └─ OnDataProduced(MeasureData)    │
                    └──────────┬──────────────────────────────────┘
                               ↓ OnDataProduced
                         ┌──────────┐
                         │  界面层  │  XAML 绑定
                         └──────────┘
```

### 1.4 关键架构决策（已与用户对齐）

| # | 决策 | 选择 |
|---|------|------|
| 1 | Device 抽象形态 | **仅 S470**，IDevice 抽象保留但不过度设计，不做多设备 UI |
| 2 | 行切分（预处理）放哪 | **绑到 `IDevice.PreprocessData` 上**——每种设备的物理层分隔符可能不同（CRLF/LF/其他），所以归设备驱动管，不独立成 service |
| 3 | MVVM 时机 | **本轮一起做**（一次性到位，不返工） |
| 4 | MVVM 框架 | **CommunityToolkit.Mvvm**（NuGet） |
| 5 | 防断电写盘 | **每条 `Flush(true)` 必须保留**（用户重点强调） |

---

## 2. 目标架构

### 2.1 层次划分

| 层 | 职责 | 类型 |
|----|------|------|
| **Infrastructure** | 串口硬件监听、电源管理 | 已有，不动 |
| **Device** | 仪器协议解析（S470K） | 已有，补完 |
| **Services** | 业务逻辑（行切分、串口、落盘、采集协调） | 新建 |
| **ViewModels** | UI 状态、命令 | 新建（MVVM） |
| **Views** | XAML 绑定、code-behind 只剩 plot/关闭 | 改造 |

### 2.2 目标目录结构

```
MettlerDataCollection/
├─ App.xaml / App.xaml.cs                 （App.xaml.cs 初始化 services）
├─ Views/                                 （改造）
│  ├─ MainWindow.xaml / .cs               （瘦身：只剩绑定 + plot 初始化）
│  ├─ RecoverData.xaml / .cs              （瘦身：用统一 MeasureData）
│  ├─ TestConnection.xaml / .cs           （不动：调试窗口，保留自己的 SerialPort）
│  ├─ SerialportSetting.xaml / .cs        （不动：继续从 MainWindow 传 _serialPort 引用）
│  ├─ CollectionSettingWindow.xaml.cs     （不动）
│  ├─ InputSampleNo.xaml / .cs            （不动）
│  ├─ HelpWindow.xaml / .cs               （不动）
│  ├─ About.xaml / .cs                    （不动）
│  └─ FluentMessageBox.xaml / .cs         （不动）
├─ ViewModels/                            ★ 新建
│  ├─ MainViewModel.cs
│  └─ RecoverDataViewModel.cs
├─ Services/                              ★ 新建（部分）
│  ├─ ~~ISerialPortService.cs / SerialPortService.cs~~  ✗ 取消
│  ├─ IDataPersistenceService.cs / DataPersistenceService.cs  (已有)
│  └─ ~~IDataAcquisitionService.cs / DataAcquisitionService.cs~~  ✗ 取消
└─ Device/
   ├─ IDevice.cs                          （已有，小改：加 PreprocessData + ParseData）
   └─ S470K.cs                            （已有，补完 PreprocessData + ParseData 实现）
```

---

## 3. 关键接口设计

### 3.1 `IDevice`（轻量版，仅 S470）

```csharp
public interface IDevice
{
    string Name { get; }
    string Description { get; }

    /// <summary>
    /// 数据**预处理**：把串口来的原始 chunk 切成 0..N 个完整行。
    /// 不同设备的物理层分隔符可能不同（CRLF / LF / 自定义），所以归设备驱动管。
    /// 实现需自行维护半行缓冲 + 并发安全。
    /// </summary>
    IEnumerable<string> PreprocessData(string chunk);

    /// <summary>
    /// 数据**解析**：把 1 行解析成 0..1 MeasureData。
    /// 跟 PreprocessData 是两件事——PreprocessData 切行，ParseData 解内容。
    /// </summary>
    void ParseData(string line);

    /// <summary>解析出完整数据点时触发</summary>
    event Action<MeasureData>? OnDataProduced;

    /// <summary>解析失败时触发（不要在 ParseData 里抛异常）</summary>
    event Action<string>? OnParseError;
}
```

> **不预留多设备**：IDevice 接口保持最小，不引入 `IDeviceFactory`、不做设备选择 UI。
> **PreprocessData 与 ParseData 分离**：前者管"怎么切"，后者管"怎么解"。未来加新设备时，只动 IDevice 实现类（如 `S220K.cs`），不用动 MainViewModel 的 fan-out 逻辑。

### 3.2 ~~`ILineSplitter`~~ — **取消**

> 行切分逻辑已经并入 `IDevice.PreprocessData`，不需要独立的 splitter service。
> 取消理由：未来加新设备时，预处理是设备驱动的一部分（不同设备分隔符可能不同），
> 拆出独立 service 会让"加新设备"变复杂（要同时改 Splitter + Device）。

### 3.3 ~~`ISerialPortService`~~ — **取消**

> 用户的决定：串口读取功能**就放在 MainWindow 里**，不抽 service。
> `SerialPort` 由 MainWindow 直接持有，硬件 FIFO 事件 `DataReceived` 在 MainWindow 里订阅。
> 取消理由：本项目只有一个串口、一个主窗口，service 抽象的复用价值不明确；
> 等真有多串口或多窗口需求时再抽也不晚。

### 3.4 ~~`IDataAcquisitionService`（协调者）~~ — **取消**

> 用户的决定：两条路径（落盘 + Device 解析）**不**通过显式的协调者 service 串起来，
> 而是在 `MainViewModel`（MVVM 化后）或 `MainWindow` code-behind（MVVM 前）里直接 fan-out。
>
> 好处：少一层胶水代码，调用链更短。坏处：分发逻辑散在 UI 层，但反正 MainViewModel 已经是 thin 的，问题不大。

**MainViewModel 里的 fan-out 长这样**（伪代码）：
```csharp
private void OnSerialBytesReceived(string chunk)
{
    foreach (var line in _lineSplitter.Feed(chunk))
    {
        _persistenceService.WriteRecord(line);   // 路径 1：raw 落盘
        _s470K.ReceiveData(line);                 // 路径 2：协议解析
    }
}
```

---

## 4. 实施阶段

按**风险从低到高**、**每阶段可独立验证**划分。每阶段一个 commit。

### 阶段 1：行切分（预处理）搬入 S470K（~1.5h）

**目标**：把 `MainWindow` 里的 `_bufferLock` / `_receiveBuffer` / `RecordDelimiter` 抽到 `S470K`，作为 `IDevice.PreprocessData` 的实现

**改动文件**：
- `Device/IDevice.cs`（改：加 `PreprocessData` 方法，删除原 `ReceiveData`，新增 `ParseData`）
- `Device/S470K.cs`（改：实现 `PreprocessData`，把 StringBuilder 半行缓冲 + lock 搬进来；实现 `ParseData` 把现有 `ReceiveData` 改名）
- `MainWindow.xaml.cs`（删 3 个字段/常量：`RecordDelimiter` / `_receiveBuffer` / `_bufferLock`；`SerialPort_DataReceived` 改为调 `_s470K.PreprocessData(chunk)` 然后 foreach；删除原 `ProcessXxxRecord`/`_partialDataRecord` 暂留阶段 2 处理）

**关键设计点**：
- `S470K.PreprocessData` 内部用 StringBuilder + lock
- 返回 `IEnumerable<string>`，调用方立即 foreach（避免在 S470K 里保留迭代器状态）
- 一次 PreprocessData 可能产出 0..N 行（视 \r\n 位置）
- **`MeasureData.PhTemp` / `MeasureData.ConductivityTemp` 在 PreprocessData 之后**由 `ParseData` 填充（不影响阶段 1，阶段 2 一起处理）

**验证**：
- 连续 feed "abc\r\ndef\r\nxyz" 产出 3 行
- feed "abc"（无 \r\n）产出 0 行
- feed "abc\r" 产出 0 行（半截 \r 不算 \r\n）
- feed "abc\r\nde" 产出 1 行（"abc"），内部剩 "de"

---

### 阶段 2：Device 解析层接入（~3h）

**目标**：让 `S470K.ParseData` 真正工作，删 `MainWindow` 和 `RecoverData` 里的解析代码

**改动文件**：
- `Device/S470K.cs`（改：实现 `ParseData` —— 即原 `ReceiveData` 重命名；持内部 `_data1` 状态机）
- `MainWindow.xaml.cs`（删 3 个 `ProcessXxxRecord` 方法 + `_partialDataRecord` 字段；改为订阅 `_s470K.OnDataProduced`）
- `RecoverData.xaml.cs`（删重复的 `ProcessRecord` 解析分支，改用 S470K）

**关键设计点**：
- `S470K.ParseData` 持有内部 `_data1` (PartialData) 状态，配对 pH + 电导率两条消息
- **不抛异常**，错误通过 `OnParseError` 事件通知
- 完整数据点通过 `OnDataProduced(MeasureData)` 通知
- 协议格式：`<时间>s 1 <pH> <pH温度>` 和 `2 <电导率> <电导率温度>`（参考原 `S470K.cs` 已有的雏形）

**验证**：
- 三种模式（pH+电导率 / pH / 电导率）下，曲线、状态、导出 txt **与重构前逐字节一致**
- 喂错格式数据，UI 不崩，log 写错

---

### 阶段 3：MVVM 化（~4-5h）

**目标**：引入 `CommunityToolkit.Mvvm`，`MainWindow` 改为 XAML 绑定，code-behind 只剩 plot 初始化

**改动文件**：
- `MettlerDataCollection.csproj`（加 NuGet 包）
- `ViewModels/MainViewModel.cs`（新建）
- `ViewModels/RecoverDataViewModel.cs`（新建）
- `MainWindow.xaml`（改：所有 UI 控件加 `x:Name`、改 `{Binding ...}`）
- `MainWindow.xaml.cs`（删所有业务字段、改为 `DataContext = new MainViewModel(...)`）
- `RecoverData.xaml / .cs`（同样 MVVM 化）

**关键设计点**：
- `MainViewModel` 暴露：
  - `[ObservableProperty] string SampleNo;`
  - `[ObservableProperty] bool IsCollecting;`
  - `[ObservableProperty] bool IsConnected;`
  - `[ObservableProperty] string? SelectedComPort;`
  - `[ObservableProperty] int CollectMode;`（0/1/2）
  - `[ObservableProperty] int DataCount;`
  - `[ObservableProperty] string CurrentTime;`
  - `[ObservableProperty] string CurrentPh;`
  - `[ObservableProperty] string CurrentCond;`
  - `[ObservableProperty] ObservableCollection<string> ComPorts;`
- 命令：
  - `[RelayCommand] Connect()`、`Disconnect()`
  - `[RelayCommand] StartCollect()`、`StopCollect()`
  - `[RelayCommand] ExportData()`
  - `[RelayCommand] ModifySampleNo()`
  - `[RelayCommand] OpenSerialPortSetting()`、`OpenCollectionSetting()`、`OpenTestConnection()`、`OpenRecoverData()`、`OpenHelp()`、`OpenAbout()`
- **两条路径的 fan-out 在 MainViewModel 里**（直接调 `_persistence.WriteRecord` + `_s470K.ParseData`；行切分通过 `_s470K.PreprocessData` 完成）
- **ScottPlot 绑定的坑**：`DataLogger.Add/Clear` 是命令式 API，不适合绑定。
  ViewModel 持有 `ObservableCollection<MeasureData> PhSeries / CondSeries` 作为"模型数据"；
  View 订阅 `OnDataProduced`，收到新数据时主动调 `_phLogger.Add(...)` / `MainPlot.Refresh()`。
  这样 View 和 ViewModel 都干净。

**验证**：
- 所有按钮、ComboBox、RadioBox 行为完全一致
- 实时曲线刷新正确
- `StopCollect` 后状态正确（`IsCollecting = false`、按钮互斥）

---

### 阶段 4：清理（~0.5h）

**改动**：
- 删 `MainWindow.Create_data` 死代码
- `MainWindow.Dispose` 现状保留（释放 `SerialPort` / `ComPortWatcher` / `_persistenceService`）
- 跑一遍 manual smoke test

---

## 5. 风险清单（每阶段都盯紧）

| # | 风险 | 缓解措施 |
|---|------|----------|
| 1 | 持久化从"每条 Flush"被改成"批量" | **Code Review 重点**，每条 `WriteRecord` 必须 `Flush(true)` |
| 2 | 解析异常 → UI 崩溃 | parser 不抛异常，用 `OnParseError` 事件通知 |
| 3 | `RecoverData` 重复代码漏改 | 阶段 2 显式列出 RecoverData 改动清单 |
| 4 | 串口 IO 线程 → UI 线程切换 | MainWindow 在 `OnDataProduced` 回调里 `Dispatcher.Invoke` 转发；`MeasureData` 跨线程安全 |
| 5 | `Button_StartCollect` 残留数据抢救时序错乱 | 保留在 MainWindow；MVVM 化时搬到 MainViewModel，**保持"先抢救再 StartNewFile"** |
| 6 | MVVM binding 漏掉导致 UI 不刷新 | 阶段 4 用一遍手动 smoke test；不引入新功能，只搬代码 |
| 7 | ScottPlot 绑定失败 | 用 `OnDataProduced` 事件驱动 plot 更新，不强行 XAML 绑定 |
| 8 | `S470K.PreprocessData` 半行缓冲的并发安全 | 内部用 lock；调用方拿到 IEnumerable 后**立即** foreach，不要延迟迭代（LINQ 链式会延迟到枚举时才访问） |

---

## 6. 验收标准（每阶段都满足）

1. `dotnet build` 0 错误，warning 数 ≤ baseline
2. 三种采集模式跑通（pH+电导率 / pH / 电导率）
3. 实时曲线、状态栏、导出 txt 与重构前**逐字节一致**
4. 串口连接/断开/参数修改/拔插 USB 行为不变
5. 防断电写盘：边采边拔电源 → 重启能恢复到最后一条
6. 日志（`log/app_log.txt`）无未捕获异常

---

## 7. 时间预估

| 阶段 | 预估 |
|------|------|
| 1 行切分（预处理）搬入 S470K | 1.5 h |
| 2 Device 解析层接入 | 3 h |
| 3 MVVM 化 | 4-5 h |
| 4 清理 | 0.5 h |
| **小计** | **9-10 h** |

每阶段一个 commit。

---

## 8. 暂不做的（backlog）

- **多设备支持**（UI 加设备选择下拉、`IDeviceFactory`）—— 用户明确：仅 S470
- **抽 `ISerialPortService`** —— 用户决定：串口直接放 MainWindow。等真有多串口/多窗口需求时再抽。
- 阶段 6/7 的导出合并、单测、PLOT 性能优化等
