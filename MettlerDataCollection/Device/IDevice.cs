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

        void ReceiveData(string dataString);
    }

    public record MeasureData(double Ph, double Conductivity, int Time, double? PhTemp, double? ConductivityTemp);
}
