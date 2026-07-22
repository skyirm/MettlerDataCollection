using System.IO.Ports;
using System.Management;
using Serilog;

namespace MettlerDataCollection;

/// <summary>
///     通过 WMI 监听 USB/COM 设备插拔，通知订阅者刷新串口列表。
/// </summary>
public class ComPortWatcher : IDisposable
{
    private const int PollingInterval = 1; // 秒
    private bool _disposed;
    private ManagementEventWatcher _insertWatcher;
    private ManagementEventWatcher _removeWatcher;

    public ComPortWatcher()
    {
        InitializeWatchers();
    }

    /// <summary>COM 端口列表发生变化时触发（设备插拔都触发）。</summary>
    public event Action<List<string>> ComPortsChanged;

    private void InitializeWatchers()
    {
        // WQL (WMI Query Language) 查询：
        // EventType=2 表示设备插入，=3 表示设备移除
        var insertQuery = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 2");
        var removeQuery = new WqlEventQuery("SELECT * FROM Win32_DeviceChangeEvent WHERE EventType = 3");

        _insertWatcher = new ManagementEventWatcher(insertQuery);
        _insertWatcher.EventArrived += OnDeviceChange;
        _insertWatcher.Scope = new ManagementScope("root\\CIMV2");

        _removeWatcher = new ManagementEventWatcher(removeQuery);
        _removeWatcher.EventArrived += OnDeviceChange;
        _removeWatcher.Scope = new ManagementScope("root\\CIMV2");
    }

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

    // 设备插拔都会触发此事件，统一走"重新拉取 COM 列表"逻辑
    private void OnDeviceChange(object sender, EventArrivedEventArgs e)
    {
        UpdateComPortList();
    }

    private void UpdateComPortList()
    {
        // 注意：事件在 WMI 线程触发，订阅者需自己用 Dispatcher 转发到 UI 线程
        string[] portNames = SerialPort.GetPortNames();
        var comPorts = new List<string>(portNames);
        ComPortsChanged?.Invoke(comPorts);
    }
}
