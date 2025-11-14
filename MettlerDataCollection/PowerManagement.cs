using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MettlerDataCollection;

public class PowerManagement
{
    // 定义所需的 Execution State 标志
    [FlagsAttribute]
    public enum EXECUTION_STATE : uint
    {
        // 必须始终设置此标志以保持状态
        ES_CONTINUOUS = 0x80000000,

        // 阻止系统进入睡眠/待机状态
        ES_SYSTEM_REQUIRED = 0x00000001,

        // 阻止显示器关闭 (变黑/进入低功耗模式)
        ES_DISPLAY_REQUIRED = 0x00000002

        // 注意：ES_AWAYMODE_REQUIRED 适用于媒体应用，通常不需要与 ES_DISPLAY_REQUIRED 一起使用
    }

    private static EXECUTION_STATE _previousExecutionState;

    // 引入 kernel32.dll 中的 SetThreadExecutionState 函数
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

    /// <summary>
    ///     阻止系统进入睡眠和关闭屏幕。
    /// </summary>
    public static void PreventSleepAndDisplayTurnOff()
    {
        // 结合 ES_CONTINUOUS 和 ES_DISPLAY_REQUIRED 标志
        // 同时使用 ES_SYSTEM_REQUIRED 阻止系统睡眠
        var newExecutionState =
            EXECUTION_STATE.ES_CONTINUOUS |
            EXECUTION_STATE.ES_SYSTEM_REQUIRED |
            EXECUTION_STATE.ES_DISPLAY_REQUIRED;

        // 调用 Windows API 并保存上一个状态
        _previousExecutionState = SetThreadExecutionState(newExecutionState);

        // 可以检查返回值是否为 0 (NULL) 来判断是否失败
        if (_previousExecutionState == 0)
            // 处理失败情况 (例如记录日志)
            Debug.WriteLine("SetThreadExecutionState 调用失败。");
    }

    /// <summary>
    ///     恢复系统到之前的电源管理状态。
    /// </summary>
    public static void AllowSleepAndDisplayTurnOff()
    {
        // 恢复到之前保存的状态
        if (_previousExecutionState != 0)
        {
            SetThreadExecutionState(_previousExecutionState);
            _previousExecutionState = 0; // 避免重复恢复
            Debug.WriteLine("已恢复系统电源管理状态。");
        }
    }
}