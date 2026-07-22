using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MettlerDataCollection.Device
{
    public class S470K : IDevice
    {
        private const string RecordDelimiter = "\r\n";
        private readonly object _bufferLock = new();
        private readonly StringBuilder _receiveBuffer = new();

        public string Name => "S470-K";
        public string Description => "S470-K is a device that measures pH and Conductivity.";
        public event Action<MeasureData>? OnDataProduced;
        public event Action<string>? OnLinePreprocessed;
        private PartialData? _data1 = null;

        /// <summary>
        ///     把串口原始 chunk 喂进来，每切出一行触发 <see cref="OnLinePreprocessed" />。
        ///     半行（没遇到 \r\n）保留在内部 buffer，下次 PreprocessData 时继续拼。
        /// </summary>
        /// <remarks>
        ///     event 在锁外触发，避免 handler 慢（写盘 IO 等）阻塞后续串口接收。
        /// </remarks>
        public void PreprocessData(string chunk)
        {
            List<string>? lines = null;

            lock (_bufferLock)
            {
                _receiveBuffer.Append(chunk);

                var bufferStr = _receiveBuffer.ToString();
                int delimiterIndex;

                while ((delimiterIndex = bufferStr.IndexOf(RecordDelimiter)) >= 0)
                {
                    var completeLine = bufferStr[..delimiterIndex].Trim();
                    bufferStr = bufferStr[(delimiterIndex + RecordDelimiter.Length)..];

                    if (!string.IsNullOrEmpty(completeLine))
                    {
                        lines ??= new List<string>();
                        lines.Add(completeLine);
                    }
                }

                // 剩下不完整的数据保留
                _receiveBuffer.Clear();
                _receiveBuffer.Append(bufferStr);
            }

            // 锁外触发 event。订阅者在自己的线程里处理，handler 慢不阻塞后续 PreprocessData。
            if (lines != null)
            {
                foreach (var line in lines)
                    OnLinePreprocessed?.Invoke(line);
            }
        }

        public void ReceiveData(string dataString)
        {
            var parts = dataString.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) throw new Exception("Invalid data format");
            if (parts.Length >= 3 && parts[1] == "1")
            {
                var time = int.TryParse(parts[0].Replace("s", ""), out var t) ? t : 0;
                var pHValue = double.TryParse(parts[2], out var pH) ? pH : 0;
                var phTemp = double.TryParse(parts[3], out var temp) ? temp : 0;
                if (_data1 is null) _data1 = new PartialData(time, pHValue, phTemp);
            }
            else if (parts.Length >= 3 && parts[0] == "2" && _data1 is not null)
            {
                var conductivityValue = double.TryParse(parts[2], out var conductivity) ? conductivity : 0;
                var conductivityTemp = double.TryParse(parts[3], out var temp) ? temp : 0;
                var completeData = new MeasureData(
                    Ph: _data1.Ph,
                    Conductivity: conductivityValue,
                    Time: _data1.Time,
                    PhTemp: _data1.PhTemp,
                    ConductivityTemp: conductivityTemp
                );
                OnDataProduced?.Invoke(completeData);
                _data1 = null;
            }
            else
            {
                throw new Exception("Invalid data format");
            }

        }
    }

    internal record PartialData(int Time, double Ph, double? PhTemp);
}
