using System.IO.Ports;
using System.Management;
using Serilog;

namespace MettlerDataCollection;

public class ComPortWatcher : IDisposable
{
    private bool _disposed;
    // 监听的时间间隔（秒）
    private const int PollingInterval = 1;

    private ManagementEventWatcher _insertWatcher;
    private ManagementEventWatcher _removeWatcher;

    public ComPortWatcher()
    {
        InitializeWatchers();
    }

    // 用于通知外部 COM 列表已更新的事件
    public event Action<List<string>> ComPortsChanged;

    private void InitializeWatchers()
    {
        // WQL (WMI Query Language) 查询
        // __InstanceCreationEvent 用于监听设备插入
        var insertQuery = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2");

        // __InstanceDeletionEvent 用于监听设备移除
        var removeQuery = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 3");

        // --- 监听设备插入 ---
        _insertWatcher = new ManagementEventWatcher(insertQuery);
        _insertWatcher.EventArrived += OnDeviceChange;
        _insertWatcher.Scope = new ManagementScope("root\\CIMV2"); // 设置 WMI 范围

        // --- 监听设备移除 ---
        _removeWatcher = new ManagementEventWatcher(removeQuery);
        _removeWatcher.EventArrived += OnDeviceChange;
        _removeWatcher.Scope = new ManagementScope("root\\CIMV2");
    }

    // 启动监听
    public void Start()
    {
        try
        {
            _insertWatcher.Start();
            _removeWatcher.Start();
            Serilog.Log.Information("COM 端口变化监听已启动...");
        }
        catch (Exception ex)
        {
            Serilog.Log.Error($"启动 WMI 监听失败: {ex.Message}");
        }
    }

    // 停止监听
    public void Stop()
    {
        if (_insertWatcher != null) _insertWatcher.Stop();
        if (_removeWatcher != null) _removeWatcher.Stop();
        Serilog.Log.Information("COM 端口变化监听已停止。");
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _insertWatcher?.Dispose();
                _removeWatcher?.Dispose();
            }
            _disposed = true;
        }
    }

    ~ComPortWatcher()
    {
        Dispose(false);
    }

    // 设备事件到达时触发
    private void OnDeviceChange(object sender, EventArrivedEventArgs e)
    {
        // 任何设备变动事件都触发 COM 端口列表更新
        UpdateComPortList();
    }

    // 更新 COM 端口列表并通知订阅者
    private void UpdateComPortList()
    {
        // SerialPort.GetPortNames() 是获取当前 COM 端口列表的标准方法
        string[] portNames = SerialPort.GetPortNames();
        var comPorts = new List<string>(portNames);

        // 在 UI 线程上触发事件
        ComPortsChanged?.Invoke(comPorts);
    }
}