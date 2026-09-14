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
        public event Action<CollectMode>? OnCollectModeDetected;
        public event Action<string>? OnParseWarning;
        public event Action<string>? OnParseError;

        private PendingMeasurement? _pendingPh;
        private PendingMeasurement? _pendingCond;
        private bool _headerHasPh;
        private bool _headerHasCond;
        private bool _headerInProgress;
        private string? _phMessageType = "1";
        private string? _condMessageType = "2";

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
            ProcessProtocolHeader(line);

            // S470-K 打印输出在实际数据前会带一段协议说明/页眉，例如：
            //   ESC CS3
            //   Measurement1
            //   Meas.type1 pH      ?C
            //   ------------------------
            // 这些行不是测量数据，不应被当成解析错误。这里统一过滤，
            // 实时采集和历史恢复（RecoverData）都经过同一个入口，因此两条路径行为一致。
            if (IsIgnorableProtocolLine(line))
                return;

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
        ///     从 S470-K 打印页眉识别本次输出包含的通道。页眉以 Measurement 开始、
        ///     分隔线结束；双通道按 Meas.type1/2 映射识别，单通道按 pH / uS/cm 页眉识别。
        /// </summary>
        private void ProcessProtocolHeader(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            var trimmed = line.Trim();
            if (trimmed.StartsWith("Measurement", StringComparison.OrdinalIgnoreCase))
            {
                _headerHasPh = false;
                _headerHasCond = false;
                _headerInProgress = true;
                // 默认协议仍是 1=pH、2=电导率；双通道页眉若发现反向定义，
                // 后续 type 行会覆盖这两个映射。这样单通道页眉暂不识别模式时，
                // 也不会把已有的默认消息编号清空。
                _phMessageType = "1";
                _condMessageType = "2";
                _pendingPh = null;
                _pendingCond = null;
                return;
            }

            if (!_headerInProgress)
                return;

            string? messageType = null;
            if (trimmed.StartsWith("Meas.type1", StringComparison.OrdinalIgnoreCase))
                messageType = "1";
            else if (trimmed.StartsWith("Meas.type2", StringComparison.OrdinalIgnoreCase))
                messageType = "2";

            if (messageType is not null)
            {
                var normalized = trimmed.ToLowerInvariant();
                if (normalized.Contains("ph"))
                {
                    _headerHasPh = true;
                    _phMessageType = messageType;
                }
                else if (normalized.Contains("s/cm") || normalized.Contains("conduct"))
                {
                    _headerHasCond = true;
                    _condMessageType = messageType;
                }
            }
            else if (trimmed.StartsWith("pH", StringComparison.OrdinalIgnoreCase))
            {
                _headerHasPh = true;
                _phMessageType = null;
            }
            else if (trimmed.StartsWith("uS/cm", StringComparison.OrdinalIgnoreCase)
                     || trimmed.StartsWith("µS/cm", StringComparison.OrdinalIgnoreCase))
            {
                _headerHasCond = true;
                _condMessageType = null;
            }
            else if (trimmed.All(c => c is '-' or '=' or '_'))
            {
                _headerInProgress = false;
                if (_headerHasPh && !_headerHasCond && _phMessageType is null)
                {
                    CurrentMode = CollectMode.PH_ONLY;
                    OnCollectModeDetected?.Invoke(CollectMode.PH_ONLY);
                    return;
                }

                if (_headerHasCond && !_headerHasPh && _condMessageType is null)
                {
                    CurrentMode = CollectMode.COND_ONLY;
                    OnCollectModeDetected?.Invoke(CollectMode.COND_ONLY);
                    return;
                }

                if (_headerHasPh && _headerHasCond
                    && _phMessageType is not null && _condMessageType is not null)
                {
                    CurrentMode = CollectMode.PH_AND_COND;
                    OnCollectModeDetected?.Invoke(CollectMode.PH_AND_COND);
                }
            }
        }

        /// <summary>
        ///     判断一行是否为仪器打印的页眉/说明行，而非测量数据。
        ///     只忽略协议中可明确识别的固定形式；疑似数据但格式错误的行仍交给
        ///     各模式解析器报告错误，避免吞掉真正的采集异常。
        /// </summary>
        private static bool IsIgnorableProtocolLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return true;

            var trimmed = line.Trim();

            // 仪器启动/打印设置时会输出 ESC 控制码（示例："ESC CS3"）。
            if (trimmed[0] == '\u001b')
                return true;

            if (trimmed.Equals("CS3", StringComparison.OrdinalIgnoreCase))
                return true;

            if (trimmed.StartsWith("pH", StringComparison.OrdinalIgnoreCase))
                return true;

            if (trimmed.StartsWith("uS/cm", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("µS/cm", StringComparison.OrdinalIgnoreCase))
                return true;

            // 页眉中的横线分隔符（长度可能随打印格式变化）。
            if (trimmed.All(c => c is '-' or '=' or '_'))
                return true;

            // 测量类型说明行（如 Measurement1、Meas.type1 pH ?C）。
            return trimmed.StartsWith("Measurement", StringComparison.OrdinalIgnoreCase)
                   || trimmed.StartsWith("Meas.type", StringComparison.OrdinalIgnoreCase);
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

            double? phTemp = null;
            if (parts.Length > 2 && double.TryParse(parts[2], out var temp))
                phTemp = temp;

            OnDataProduced?.Invoke(new MeasureData(
                Ph: pH,
                Conductivity: 0,
                Time: time,
                PhTemp: phTemp,
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

            double? conductivityTemp = null;
            if (parts.Length > 2 && double.TryParse(parts[2], out var temp))
                conductivityTemp = temp;

            OnDataProduced?.Invoke(new MeasureData(
                Ph: 0,
                Conductivity: cond,
                Time: time,
                PhTemp: null,
                ConductivityTemp: conductivityTemp));
        }

        /// <summary>
        ///     双工模式解析：配对 pH 消息（带时间戳）和电导率消息（不带时间戳，借前一条 pH 的）。
        ///     协议格式：
        ///     <list type="bullet">
        ///         <item>pH 消息：<c>时间s 消息编号 pH值 pH温度</c></item>
        ///         <item>电导率消息：<c>消息编号 电导率值 电导率温度</c></item>
        ///         <item>消息编号由双通道页眉中的 Meas.type1/2 实际类型决定。</item>
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

            // 带时间的行形如“5s 1 value temp”，不带时间的行形如“2 value temp”。
            // 哪个编号对应哪种结构由仪器实际输出顺序决定，因此两种通道都按编号解析。
            var hasTimestamp = parts.Length >= 3 && parts[0].EndsWith("s", StringComparison.OrdinalIgnoreCase);
            var messageType = hasTimestamp ? parts[1] : parts[0];
            var valueIndex = hasTimestamp ? 2 : 1;

            if (messageType != _phMessageType && messageType != _condMessageType)
            {
                OnParseError?.Invoke($"行格式不识别：{line}");
                return;
            }

            if (!double.TryParse(parts[valueIndex], out var value))
            {
                OnParseError?.Invoke($"双工模式数值无法解析（line: {line}）");
                return;
            }

            double? temperature = null;
            if (parts.Length > valueIndex + 1 && double.TryParse(parts[valueIndex + 1], out var temp))
                temperature = temp;

            int? time = null;
            if (hasTimestamp && int.TryParse(parts[0][..^1], out var parsedTime))
                time = parsedTime;

            var pending = new PendingMeasurement(value, temperature, time);
            if (messageType == _phMessageType)
            {
                if (_pendingPh is not null)
                    OnParseWarning?.Invoke($"收到新的 pH 消息前，上一条 pH 尚未与电导率配对，已丢弃上一条（line: {line}）");
                _pendingPh = pending;
            }
            else
            {
                // 兼容原有协议约定：默认 2=电导率时，孤立的电导率消息仍提示未配对。
                // 若页眉声明了反向编号，则允许电导率先到，等待后续 pH 消息再合并。
                if (_condMessageType == "2" && _pendingPh is null)
                    OnParseError?.Invoke($"电导率消息无配对 pH（line: {line}）");
                if (_pendingCond is not null)
                    OnParseWarning?.Invoke($"收到新的电导率消息前，上一条电导率尚未与 pH 配对，已丢弃上一条（line: {line}）");
                _pendingCond = pending;
            }

            TryEmitCombinedData();
        }

        private void TryEmitCombinedData()
        {
            if (_pendingPh is null || _pendingCond is null)
                return;

            var data = new MeasureData(
                Ph: _pendingPh.Value.Value,
                Conductivity: _pendingCond.Value.Value,
                Time: _pendingPh.Value.Time ?? _pendingCond.Value.Time ?? 0,
                PhTemp: _pendingPh.Value.Temperature,
                ConductivityTemp: _pendingCond.Value.Temperature);

            OnDataProduced?.Invoke(data);
            _pendingPh = null;
            _pendingCond = null;
        }

        private readonly record struct PendingMeasurement(double Value, double? Temperature, int? Time);
    }
}
