using System.Reflection;

namespace MettlerDataCollection.Device;

/// <summary>
///     反射发现所有 <see cref="IDevice" /> 实现。添加新设备时不用改 catalog——
///     任何新写的实现 <see cref="IDevice" /> 的类会被自动发现。
/// </summary>
public static class DeviceCatalog
{
    /// <summary>
    ///     返回所有非抽象、非接口的 <see cref="IDevice" /> 实现类的 <see cref="Type" />。
    ///     要求实现类有 public 无参构造器（用于 Activator.CreateInstance）。
    /// </summary>
    public static IReadOnlyList<Type> DiscoverDeviceTypes()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => SafeGetTypes(a))
            .Where(t => typeof(IDevice).IsAssignableFrom(t)
                        && t is { IsClass: true, IsAbstract: false, IsInterface: false })
            .OrderBy(t => t.Name)
            .ToList();
    }

    /// <summary>
    ///     为指定 <see cref="Type" /> 创建 <see cref="IDevice" /> 实例。
    ///     失败抛 <see cref="InvalidOperationException" /> 包装详细错误。
    /// </summary>
    public static IDevice CreateDevice(Type type)
    {
        try
        {
            return (IDevice)Activator.CreateInstance(type)!;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"创建设备 {type.FullName} 失败（必须有 public 无参构造器）: {ex.Message}", ex);
        }
    }

    private static Type[] SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // 部分类型加载失败时（如依赖缺失），忽略这些类型
            return ex.Types.Where(t => t != null).ToArray()!;
        }
    }
}
