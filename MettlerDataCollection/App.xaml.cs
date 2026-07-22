using System.Windows;
using System.Windows.Threading;
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

        // 三层全局异常处理（任一崩溃都不会让进程静默退出）
        Current.DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        // 启动设置窗口：用户选数据目录 + 设备；取消则退出
        var startupWindow = new StartupSettingsWindow();
        if (startupWindow.ShowDialog() != true || startupWindow.SelectedDevice is null)
        {
            Log.Information("用户在启动设置窗口取消，程序退出。");
            Shutdown();
            return;
        }

        var mainWindow = new MainWindow(
            new DataPersistenceService(startupWindow.DataPath),
            startupWindow.SelectedDevice);
        mainWindow.Show();
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
            Log.Error(ex.Message);
            MessageBox.Show("应用程序发生非UI线程异常: " + ex.Message,
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
