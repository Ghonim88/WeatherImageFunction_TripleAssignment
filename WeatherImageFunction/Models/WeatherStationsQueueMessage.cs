using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WeatherImageFunction.Models
{
    public class WeatherStationsQueueMessage
    {
        public string JobId { get; set; } = string.Empty;
        public string SearchKeyword { get; set; } = string.Empty;
        public int MaxStations { get; set; }
        public string City { get; set; } = string.Empty;
    }
}
