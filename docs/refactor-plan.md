# MettlerDataCollection 重构计划

> 状态：**待评审 / 待实施**
> 范围：**阶段 1 ~ 阶段 3**（协议层 / 持久化 / 串口服务）
> 暂不做：阶段 4（导出）、阶段 5（ViewModel + CommunityToolkit.Mvvm）、阶段 6（清理）、阶段 7（多设备 + 单测）

---

## 1. 背景

`MainWindow.xaml.cs` 当前 ~660 行，**同时**承担 9 件事：
1. 串口管理（Open/Close/参数）
2. 串口数据接收 + 行切分
3. 三种采集模式（pH+电导率 / pH / 电导率）的协议解析
4. 协议状态机（`_partialDataRecord` 配对两条消息）
5. ScottPlot 实时绘图配置
6. 防断电落盘（`FileStream.Flush(true)`）
7. 数据导出为 txt
8. UI 状态机（`_isCollecting`、`_dataCount`）
9. 9 个按钮的事件处理

更要命的是：第 2-4 项的解析逻辑在 `RecoverData.xaml.cs` 第 116-167 行**几乎原样 copy 了一份**，后续维护会分裂。

好消息：仓库里 `Device/IDevice.cs` + `Device/S470K.cs` 已埋好抽象骨架（事件 + `MeasureData` record），但 `MainWindow` 没接上，是给重构留的接口。

---

## 2. 目标

把 `MainWindow.xaml.cs` 拆成"接收 + 转发"的薄壳，业务逻辑下沉到 `Services/` 和 `Device/`，代码按职责分层。

**目标架构**：

```
Views/          ← UI 绑定 + 事件转发（暂时保留 code-behind 写法）
Services/       ← 串口、落盘（本次新增）
Device/         ← 仪器协议抽象（已有，补完整）
Infrastructure/ ← ComPortWatcher、PowerManagement（已有，不动）
```

---

## 3. 目标目录结构（仅本次改动部分）

```
MettlerDataCollection/
├─ App.xaml / App.xaml.cs           （不动）
├─ MainWindow.xaml / .cs            （瘦身，移除解析/落盘/串口配置）
├─ RecoverData.xaml / .cs           （瘦身，复用新 services）
├─ TestConnection.xaml / .cs        （不动）
├─ SerialportSetting.xaml / .cs     （不动）
├─ CollectionSettingWindow.xaml.cs  （不动）
├─ FluentMessageBox.xaml / .cs      （不动）
├─ ...
├─ Services/                        ★ 新建
│  ├─ ISerialPortService.cs / SerialPortService.cs
│  ├─ IDataPersistenceService.cs / DataPersistenceService.cs
│  └─ IDataAcquisitionService.cs / DataAcquisitionService.cs  （协调 parser + 持久化 + 通知 UI）
└─ Device/                          （已有，补完 S470K）
   ├─ IDevice.cs                    （不动）
   ├─ S470K.cs                      （补完 ReceiveData 实现，作为唯一协议入口）
   └─ Parsers/
      └─ S470RecordParser.cs        ★ 新建（纯函数，可单测）
```

`MeasureData` record 复用 `Device/IDevice.cs` 里已有的定义，不新建。

---

## 4. 阶段 0：准备

| 任务 | 产出 |
|------|------|
| 跑一次现有功能（手动 / 虚拟串口工具），确认能采数据、能导出、能恢复 | 基线截图或笔记 |
| 确认 S470 协议格式文档（如有），核对解析分支 | 协议格式 checklist |
| 在仓库新建 `docs/refactor-plan.md`（本文件） | 本文件 |

**不引入任何依赖、不改任何业务代码。**

---

## 5. 阶段 1：抽协议层

### 目标
让 `MainWindow` 不再硬解析串口数据。`IDevice.ReceiveData` 真正派上用场，状态机从 UI 迁出。

### 涉及改动
- `Device/Parsers/S470RecordParser.cs`（**新建**）
  - 纯函数 `Parse(string line, CollectMode mode) → ParseResult`
  - `ParseResult` = `Success(MeasureData)` / `Incomplete` / `Error(string)`
  - **不抛异常**，错误用 `Error` 变体返回（解析失败不应让 UI 崩）
  - **不持有任何状态**（状态机由调用方管）
- `Device/S470K.cs`（**改**）
  - 实现 `ReceiveData(string)`：调 `S470RecordParser`，根据 `ParseResult`：
    - `Success` → 触发 `OnDataProduced?.Invoke(data)`
    - `Incomplete` → 暂存到内部 `PartialData?`
    - `Error` → 触发新事件 `OnParseError?.Invoke(message)`
  - 持有内部 `_data1`（已有的 `PartialData`）作为配对状态
- `Device/IDevice.cs`（**小改**）
  - 新增 `event Action<string> OnParseError`
- `MainWindow.xaml.cs`（**改**）
  - 删 `ProcessCondRecord` / `ProcessPhRecord` / `ProcessPhAndCondRecord` 三个方法（约 80 行）
  - 删 `_partialDataRecord` 字段
  - 注入一个 `IDevice _device = new S470K(...)`
  - 订阅 `_device.OnDataProduced` 和 `OnParseError`
  - `SerialPort_DataReceived` 收到完整行后，调用 `_device.ReceiveData(line)`，**解析由 device 负责**
- `RecoverData.xaml.cs`（**改**）
  - 删除 `ProcessRecord` 里重复的解析代码（约 50 行）
  - 改用 `S470K` 解析，仅保留把 `MeasureData` 推进 plot 的逻辑

### 关键设计点
- `S470RecordParser` 必须是**纯函数**，不持有 `PartialData` 状态，状态归 `S470K` 管
- 错误处理用 `OnParseError` 事件，不要在 `ReceiveData` 里抛异常
- `CollectMode` 枚举从 `MainWindow.xaml.cs` 末尾搬到 `Device/IDevice.cs` 附近（或保留在 device namespace）

### 验证
- 三种采集模式（pH+电导率 / pH / 电导率）下，实时曲线、状态栏、导出 txt **完全一致**
- 故意喂错格式的数据，UI 不崩，只在 log 里写错

---

## 6. 阶段 2：抽持久化层

### 目标
把防断电写盘逻辑从 `MainWindow` 抽出来。

### 涉及改动
- `Services/IDataPersistenceService.cs`（**新建**）
  ```csharp
  public interface IDataPersistenceService
  {
      string CurrentFilePath { get; }                  // 给 RecoverData 用
      void StartNewFile(string sampleNo, string dataPath);
      void WriteRecord(string rawRecord);              // 包含时间戳
      void Stop();
  }
  ```
- `Services/DataPersistenceService.cs`（**新建**）
  - 把 `MainWindow.WriteDataToFile` 的逻辑搬过来（**保持每条 `Flush(true)` 行为**）
  - 持有一个 `FileStream` / `StreamWriter`，按样本号生成文件名 `yyyyMMdd_HHmmss-{sampleNo}.txt`
  - 加 `Stop()` 用于窗口关闭/开始新采集时关闭旧文件
- `MainWindow.xaml.cs`（**改**）
  - 删 `WriteDataToFile` 方法（约 30 行）
  - 删 `LogFilePath` 字段及其 lock
  - 注入 `IDataPersistenceService`，在 `Button_StartCollect` 调 `StartNewFile`、在 `SerialPort_DataReceived` 调 `WriteRecord`
- `App.xaml.cs`（**小改**）
  - 在 `OnStartup` 里把 `DataPersistenceService` 实例化一次（通过简单服务定位器或直接 new），注入到 `MainWindow`

### 关键设计点
- **绝对不能改成批量写**。每条数据都 `Flush(true)` 是实验防断电的关键
- 文件名规则保留：开始采集时间 + 样品编号
- `RecoverData` 当前直接读文件系统，**不依赖** `CurrentFilePath`，但保留该属性供以后扩展

### 验证
- 开始采集 → 边采边手动拔电源 → 重启后查看文件，最后一条记录完整
- 多次开始/停止采集，文件名按时间戳区分不冲突
- 串口接收路径上不持文件锁不影响 `RecoverData` 读取

---

## 7. 阶段 3：抽串口服务层

### 目标
让 `MainWindow` 不再直接持有 `System.IO.Ports.SerialPort`。

### 涉及改动
- `Services/ISerialPortService.cs`（**新建**）
  ```csharp
  public interface ISerialPortService : IDisposable
  {
      bool IsOpen { get; }
      string? PortName { get; }
      int BaudRate { get; set; }
      int DataBits { get; set; }
      Parity Parity { get; set; }
      StopBits StopBits { get; set; }
      Handshake Handshake { get; set; }

      event EventHandler<string>? LineReceived;    // 完整一行（\r\n 已切分）
      event EventHandler<Exception>? ErrorOccurred;

      void Open(string portName);
      void Close();
  }
  ```
- `Services/SerialPortService.cs`（**新建**）
  - 包一个 `SerialPort`，把现有的 9600/8N1/XOnXOff 默认值搬过来
  - 在 `DataReceived` 里做**行切分**（`MainWindow` 现有的 `_receiveBuffer` + `RecordDelimiter` 逻辑搬过来）
  - 切完发 `LineReceived` 事件，**不再发原始字节**
  - 内部用 `lock` 保护 `_receiveBuffer`
- `Services/IDataAcquisitionService.cs`（**新建**）
  - 协调 `IDevice`（解析）+ `IDataPersistenceService`（落盘）+ UI 数据点
  - 暴露 `event Action<MeasureData> OnDataProduced`、`event Action<string> OnParseError`
  - `MainWindow` 订阅这俩事件，更新 plot 和状态栏
  - 这样 `MainWindow` 完全不知道串口、不知道文件 IO，只知道"有条新数据来了"
- `MainWindow.xaml.cs`（**改**）
  - 删 `SerialPort _serialPort` 字段、`InitSerialPort` 方法（约 20 行）
  - 删 `SerialPort_DataReceived` 方法（约 40 行），改为订阅 `_acqService.OnDataProduced`
  - 删 `_receiveBuffer`、`_bufferLock`、`RecordDelimiter` 常量
  - `Button_OpenPort` / `Button_ClosePort` 改用 `_serialService.Open/Close`
  - `Button_StartCollect` 里"清理串口残留数据"的逻辑搬到 `IDataAcquisitionService.StartAsync` 里
- `SerialportSetting.xaml.cs`（**小改**）
  - 接收 `ISerialPortService` 而不是 `SerialPort`，设置项作用到 service 上
- `TestConnection.xaml.cs`（**小改**）
  - 同样用 `ISerialPortService`（或保留直接 `SerialPort`，因为它就是一个调试工具；**建议**也改一致，节省认知负担）

### 关键设计点
- `ISerialPortService` 的 `LineReceived` 事件发的是**已切分的完整行**，`MainWindow` 不需要再管 buffer
- `IDataAcquisitionService` 是**本阶段的"集成点"**，它把 device + persistence + 数据点通知捏在一起，让 UI 完全无感
- `Button_StartCollect` 里"清空残留数据"这段敏感逻辑，搬进 `DataAcquisitionService.Start`，**保留对用户行为的一致性**（"新实验前点停止"提示仍然必要）

### 验证
- 连接 / 断开串口，UI 状态完全一致
- 修改串口参数（波特率、校验、流控），下次 Open 生效
- 拔插 USB，ComportCombox 自动刷新（已有逻辑，搬到 service 里要保证事件链不断）
- 开始采集前串口残留数据仍能正确保存到上一份文件
- 三种模式曲线、导出、统计完全一致

---

## 8. 风险清单（每阶段都盯紧）

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| 串口 IO 线程 → UI 线程切换 | 崩溃、UI 卡顿 | 保持 `_bufferLock`/`_buffer` 在 service 内部；UI 刷新走 `Dispatcher`（保留 `MainWindow` 里的 `Dispatcher.BeginInvoke`） |
| 持久化从"每条 Flush"被改成"批量" | 实验断电丢数据 | Code Review 重点项；每条 `WriteRecord` 必须 `Flush(true)` |
| 解析异常 → UI 崩溃 | 用户操作中断 | parser 用 `ParseResult` 不抛异常；device 用 `OnParseError` 事件通知 |
| `RecoverData` 那份 copy 漏改 | 后续维护分裂 | 阶段 1、3 显式列出 RecoverData 改动，并对比最终代码确认一致 |
| 串口参数修改时机错误 | 串口打开中改参数抛异常 | `SerialportSetting.Save_Click` 已检查 `IsOpen`，保持；service 同样拒绝 Open 中改参数 |
| 重组时漏掉 `Dispose` | 串口句柄泄漏 | `ISerialPortService : IDisposable`；`DataAcquisitionService` 持 service，组合根处 `Dispose` |

---

## 9. 验收标准

每个阶段必须满足：
1. 编译通过、零 warning
2. 三种采集模式跑通（pH+电导率 / pH / 电导率）
3. 实时曲线、状态栏、导出 txt 与重构前**逐字节一致**
4. 串口连接 / 断开 / 参数修改 / 拔插 USB 行为不变
5. 防断电写盘：边采边拔电源 → 重启能恢复到最后一条
6. 日志（`log/app_log.txt`）无未捕获异常

---

## 10. 时间预估

| 阶段 | 预估 |
|------|------|
| 0 准备 | 0.5 h |
| 1 抽协议层 | 2-3 h |
| 2 抽持久化 | 1 h |
| 3 抽串口服务 | 2-3 h |
| **小计** | **6-7.5 h** |

每阶段一个 commit，方便 review 和回退。

---

## 11. 后续（暂不做，标记为 backlog）

- **阶段 4**：抽导出服务（合并 `MainWindow.Button_ExportData` 和 `RecoverData.ExportData`）
- **阶段 5**：引入 CommunityToolkit.Mvvm，做 `MainViewModel` / `RecoverDataViewModel`
- **阶段 6**：清理 `Create_data` 死代码、搬走残留字段、`Dispose` 放组合根
- **阶段 7**：`IDevice` 多型号支持 + parser 单元测试
