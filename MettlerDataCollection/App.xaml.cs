using System.IO;
using System.Windows;
using System.Windows.Threading;
using MettlerDataCollection.Device;
using MettlerDataCollection.Properties;
using MettlerDataCollection.Services;
using MettlerDataCollection.Views;
using MettlerDataCollection.Views.Startup;
using Serilog;

namespace MettlerDataCollection;

/// <summary>
///     应用启动流程编排：日志初始化 + 全局异常处理 + 防睡眠
///     + 弹启动设置窗口 + 创建主窗口。
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 日志：每天一个新文件，写到 ./log/app_log-YYYYMMDD.txt
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File("log/app_log.txt",
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("应用程序已启动。");
        PowerManagement.PreventSleepAndDisplayTurnOff();
        base.OnStartup(e);

        // 关键：默认 ShutdownMode=OnLastWindowClose，但 StartupSettingsWindow.ShowDialog 关闭后
        // Application.Windows 集合是空的（MainWindow 还没 Show），WPF 内部在某些环境下会判定
        // "应该 Shutdown" → MainWindow.Show() 立即被 WPF 内部关掉，Loaded 都不触发。
        // 解决：OnStartup 早期设 OnExplicitShutdown 绕开这个空窗期自动 Shutdown；
        //       MainWindow Show() 之后再设回 OnMainWindowClose，关窗时正常 Shutdown。
        Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 三层全局异常处理（任一崩溃都不会让进程静默退出）
        Current.DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        // 优先尝试从 Settings 加载上次保存的启动配置：路径 + 设备类型都齐 + 路径存在 + 设备类型能找到
        // 才直接走，避免每次启动都让用户重选。
        string? dataPath;
        IDevice? device;
        if (!TryLoadSavedStartupSettings(out dataPath, out device))
        {
            // 加载失败（首次启动 / 之前没保存 / 目录被删 / 设备类型被移除）→ 弹窗让用户重选
            var startupWindow = new StartupSettingsWindow();
            if (startupWindow.ShowDialog() != true || startupWindow.SelectedDevice is null)
            {
                Log.Information("用户在启动设置窗口取消，程序退出。");
                Shutdown();
                return;
            }
            dataPath = startupWindow.DataPath;
            device = startupWindow.SelectedDevice;
        }

        try
        {
            var mainWindow = new MainWindow(
                new DataPersistenceService(dataPath),
                device);

            mainWindow.Show();

            // MainWindow 已 Show，恢复 ShutdownMode 到 OnMainWindowClose：
            // MainWindow 关闭时正常 Shutdown，但不会被"空 Application.Windows 集合"提前触发。
            Current.ShutdownMode = ShutdownMode.OnMainWindowClose;

            // 先以 Normal 状态让窗口正常加载（避免 WindowState=Maximized + 缺位置
            // 触发 WPF 内部"不显示+立即关闭"），再在 Loaded 之后切到 Maximized。
            // 用 Loaded 事件确保窗口已布局完成再最大化，不会触发 WPF 异常路径。
            mainWindow.Loaded += (_, _) => mainWindow.WindowState = WindowState.Maximized;
        }
        catch (Exception ex)
        {
            Log.Error($"主窗口创建/显示失败: {ex}");
            MessageBox.Show(
                "主窗口创建/显示失败。\n\n" + ex.Message,
                "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    /// <summary>
    ///     从 <see cref="Settings.Default" /> 加载上次保存的启动配置。
    ///     成功条件：DataPath 非空 + 目录存在 + SelectedDeviceTypeName 非空 + 能在 DeviceCatalog 里找到。
    ///     任意一条不满足返回 false（让 StartupSettingsWindow 让用户重选）。
    /// </summary>
    private bool TryLoadSavedStartupSettings(out string? dataPath, out IDevice? device)
    {
        dataPath = null;
        device = null;

        var savedPath = Settings.Default.DataPath;
        var savedTypeName = Settings.Default.SelectedDeviceTypeName;

        if (string.IsNullOrWhiteSpace(savedPath) || string.IsNullOrWhiteSpace(savedTypeName))
            return false;

        if (!Directory.Exists(savedPath))
        {
            Log.Warning($"启动设置：上次保存的数据目录不存在: {savedPath}");
            return false;
        }

        var deviceType = DeviceCatalog.DiscoverDeviceTypes()
            .FirstOrDefault(t => t.FullName == savedTypeName);
        if (deviceType is null)
        {
            Log.Warning($"启动设置：上次保存的设备类型未找到: {savedTypeName}");
            return false;
        }

        try
        {
            device = DeviceCatalog.CreateDevice(deviceType);
        }
        catch (Exception ex)
        {
            Log.Error($"创建设备失败: {ex}");
            return false;
        }

        dataPath = savedPath;
        Log.Information($"启动设置从保存值恢复：路径={dataPath}, 设备={device.Name}");
        return true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("应用程序即将退出。");
        Log.CloseAndFlush();
        PowerManagement.AllowSleepAndDisplayTurnOff();
        base.OnExit(e);
    }

    /// <summary>UI 线程未捕获异常（Dispatcher 抛到主线程的）。</summary>
    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Log.Error(e.Exception.Message);
            MessageBox.Show("应用程序发生异常: " + e.Exception.Message,
                "应用程序错误", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true; // 标记已处理，避免 WPF 终止进程
        }
        catch (Exception ex)
        {
            MessageBox.Show("异常处理发生错误: " + ex.Message, "致命错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>非 UI 线程未捕获异常（如 Thread、ThreadPool 抛的）。</summary>
    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            var ex = e.ExceptionObject as Exception;
            if (ex != null) Log.Error(ex.Message);
            MessageBox.Show("应用程序发生非UI线程异常: " + ex?.Message,
                "应用程序错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show("异常处理发生错误: " + ex.Message, "致命错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Task 线程未观察到的异常（async/await 路径漏 try/catch 的）。</summary>
    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            Exception ex = e.Exception;
            Log.Error(ex.Message);
            MessageBox.Show("应用程序发生Task线程异常: " + ex.Message,
                "应用程序错误", MessageBoxButton.OK, MessageBoxImage.Error);
            e.SetObserved(); // 标记已观察，避免 .NET 终止进程
        }
        catch (Exception ex)
        {
            MessageBox.Show("异常处理发生错误: " + ex.Message, "致命错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
