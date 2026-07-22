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
        ///     每切出一个完整行就触发一次。订阅者按行处理（写盘 / 解析等）。
        /// </summary>
        event Action<string> OnLinePreprocessed;

        /// <summary>
        ///     数据<strong>预处理</strong>：把串口来的原始 chunk 喂进来，每切出一行触发
        ///     <see cref="OnLinePreprocessed" />。半行保留在内部 buffer 下次继续拼。
        ///     不同设备的物理层分隔符可能不同（CRLF / LF / 自定义），所以归设备驱动管。
        /// </summary>
        /// <remarks>
        ///     跟 <see cref="ReceiveData" />（解析内容）是两件事——前者管"怎么切"，后者管"怎么解"。
        /// </remarks>
        void PreprocessData(string chunk);

        void ReceiveData(string dataString);
    }

    public record MeasureData(double Ph, double Conductivity, int Time, double? PhTemp, double? ConductivityTemp);
}
