using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MettlerDataCollection.Device
{
    public interface IDevice
    {
        public string Name { get; }
        public string Description { get; }

        event Action<MeasureData> OnDataProduced;

        /// <summary>
        ///     数据<strong>预处理</strong>：把串口来的原始 chunk 切成 0..N 个完整行。
        ///     不同设备的物理层分隔符可能不同（CRLF / LF / 自定义），所以归设备驱动管。
        ///     实现需自行维护半行缓冲 + 并发安全。
        /// </summary>
        /// <remarks>
        ///     跟 <see cref="ReceiveData" />（解析内容）是两件事——前者管"怎么切"，后者管"怎么解"。
        /// </remarks>
        IEnumerable<string> PreprocessData(string chunk);

        void ReceiveData(string dataString);
    }

    public record MeasureData(double Ph, double Conductivity, int Time, double? PhTemp, double? ConductivityTemp);
}
