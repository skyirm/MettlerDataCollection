using System.Windows;
using System.Windows.Threading;
using Serilog;

namespace MettlerDataCollection;

/// <summary>
///     Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 配置日志记录器
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug() // 设置最低日志级别
            .WriteTo.File("log/app_log.txt", // 将日志写入文件
                rollingInterval: RollingInterval.Day, // 每天生成一个新文件
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("应用程序已启动。");
        PowerManagement.PreventSleepAndDisplayTurnOff(); // 启动时阻止睡眠和关闭屏幕
        base.OnStartup(e);
        // 在应用程序启动时执行的代码

        // 1. UI线程异常处理
        Current.DispatcherUnhandledException += App_DispatcherUnhandledException;

        // 2. 非UI线程异常处理
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        // 3. Task线程异常处理
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("应用程序即将退出。");
        Log.CloseAndFlush(); // 确保所有日志都已写入
        PowerManagement.AllowSleepAndDisplayTurnOff(); // 退出时恢复电源管理设置
        base.OnExit(e);
        // 在应用程序退出时执行的代码
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            // 可以在这里添加日志记录
            Log.Error(e.Exception.Message);

            // 显示友好错误提示
            MessageBox.Show("应用程序发生异常: " + e.Exception.Message,
                "应用程序错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            // 标记异常已处理，防止应用程序崩溃
            e.Handled = true;
        }
        catch (Exception ex)
        {
            // 处理异常处理过程中的异常
            MessageBox.Show("异常处理发生错误: " + ex.Message,
                "致命错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    ///     非UI线程异常处理
    /// </summary>
    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            var ex = e.ExceptionObject as Exception;
            // 日志记录
            Log.Error(ex.Message);

            // 显示错误信息
            MessageBox.Show("应用程序发生非UI线程异常: " + ex.Message,
                "应用程序错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            // 处理异常处理过程中的异常
            MessageBox.Show("异常处理发生错误: " + ex.Message,
                "致命错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    ///     Task线程异常处理
    /// </summary>
    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            Exception ex = e.Exception;
            // 日志记录
            Log.Error(ex.Message);

            // 显示错误信息
            MessageBox.Show("应用程序发生Task线程异常: " + ex.Message,
                "应用程序错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            // 标记异常已观察，防止进程终止
            e.SetObserved();
        }
        catch (Exception ex)
        {
            // 处理异常处理过程中的异常
            MessageBox.Show("异常处理发生错误: " + ex.Message,
                "致命错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}