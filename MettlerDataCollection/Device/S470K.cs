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

        public CollectMode CurrentMode { get; set; } = CollectMode.PH_AND_COND;

        public event Action<MeasureData>? OnDataProduced;
        public event Action<string>? OnLinePreprocessed;
        public event Action<string>? OnParseError;

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

        /// <summary>
        ///     根据 <see cref="CurrentMode" /> 解析 1 行数据。
        /// </summary>
        public void ParseData(string line)
        {
            switch (CurrentMode)
            {
                case CollectMode.PH_AND_COND:
                    ParsePhAndCond(line);
                    break;
                case CollectMode.PH_ONLY:
                    ParsePhOnly(line);
                    break;
                case CollectMode.COND_ONLY:
                    ParseCondOnly(line);
                    break;
                default:
                    OnParseError?.Invoke($"未知的采集模式 {CurrentMode}（line: {line}）");
                    break;
            }
        }

        /// <summary>
        ///     单工 pH 模式解析。
        ///     协议格式：<c>时间s pH值</c>，如 <c>10s 7.42</c>。
        ///     单行就出一条 <see cref="MeasureData" />（Conductivity / PhTemp / ConductivityTemp 留 0/null）。
        /// </summary>
        private void ParsePhOnly(string line)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                OnParseError?.Invoke($"PH_ONLY 行字段少于 2：{line}");
                return;
            }

            var time = int.TryParse(parts[0].Replace("s", ""), out var t) ? t : 0;
            if (!double.TryParse(parts[1], out var pH))
            {
                OnParseError?.Invoke($"PH_ONLY pH 值无法解析（line: {line}）");
                return;
            }

            OnDataProduced?.Invoke(new MeasureData(
                Ph: pH,
                Conductivity: 0,
                Time: time,
                PhTemp: null,
                ConductivityTemp: null));
        }

        /// <summary>
        ///     单工电导率模式解析。
        ///     协议格式：<c>时间s 电导率值</c>，如 <c>10s 1450.0</c>。
        ///     单行就出一条 <see cref="MeasureData" />（Ph / PhTemp / ConductivityTemp 留 0/null）。
        /// </summary>
        private void ParseCondOnly(string line)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                OnParseError?.Invoke($"COND_ONLY 行字段少于 2：{line}");
                return;
            }

            var time = int.TryParse(parts[0].Replace("s", ""), out var t) ? t : 0;
            if (!double.TryParse(parts[1], out var cond))
            {
                OnParseError?.Invoke($"COND_ONLY 电导率值无法解析（line: {line}）");
                return;
            }

            OnDataProduced?.Invoke(new MeasureData(
                Ph: 0,
                Conductivity: cond,
                Time: time,
                PhTemp: null,
                ConductivityTemp: null));
        }

        /// <summary>
        ///     双工模式解析：配对 pH 消息（带时间戳）和电导率消息（不带时间戳，借前一条 pH 的）。
        ///     协议格式：
        ///     <list type="bullet">
        ///         <item>pH 消息：<c>时间s 1 pH值 pH温度</c></item>
        ///         <item>电导率消息：<c>2 电导率值 电导率温度</c></item>
        ///     </list>
        /// </summary>
        private void ParsePhAndCond(string line)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                OnParseError?.Invoke($"行字段少于 2：{line}");
                return;
            }

            // pH 消息：暂存 pH + 时间 + 温度，等配对的电导率消息
            if (parts.Length >= 3 && parts[1] == "1")
            {
                var time = int.TryParse(parts[0].Replace("s", ""), out var t) ? t : 0;
                var pHValue = double.TryParse(parts[2], out var pH) ? pH : 0;
                double? phTemp = null;
                if (parts.Length > 3 && double.TryParse(parts[3], out var temp))
                    phTemp = temp;

                // 如果已有暂存（说明前一条 pH 没配对上），丢弃旧的
                _data1 = new PartialData(time, pHValue, phTemp);
                return;
            }

            // 电导率消息：配对前一条暂存的 pH，合成完整 MeasureData 触发 OnDataProduced
            if (parts[0] == "2" && parts.Length >= 2)
            {
                if (_data1 is null)
                {
                    // 收到电导率但前面没 pH 暂存（可能仪器切换瞬间），丢弃
                    OnParseError?.Invoke($"电导率消息无配对 pH（line: {line}）");
                    return;
                }

                var conductivityValue = double.TryParse(parts[1], out var cond) ? cond : 0;
                double? conductivityTemp = null;
                if (parts.Length > 2 && double.TryParse(parts[2], out var temp))
                    conductivityTemp = temp;

                var data = new MeasureData(
                    Ph: _data1.Ph,
                    Conductivity: conductivityValue,
                    Time: _data1.Time,
                    PhTemp: _data1.PhTemp,
                    ConductivityTemp: conductivityTemp);

                OnDataProduced?.Invoke(data);
                _data1 = null;
                return;
            }

            OnParseError?.Invoke($"行格式不识别：{line}");
        }
    }

    internal record PartialData(int Time, double Ph, double? PhTemp);
}
