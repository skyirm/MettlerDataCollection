using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MettlerDataCollection.Device
{
    public class S470K : IDevice
    {
        public string Name => "S470-K";
        public string Description => "S470-K is a device that measures pH and Conductivity.";
        public event Action<MeasureData>? OnDataProduced;
        private PartialData? _data1 = null;
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
