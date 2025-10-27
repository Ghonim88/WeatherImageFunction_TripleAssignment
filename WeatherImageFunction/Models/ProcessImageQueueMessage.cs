using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WeatherImageFunction.Models
{
    public class ProcessImageQueueMessage
    {
        public string JobId { get; set; } = string.Empty;
        public int StationId { get; set; }
        public string StationName { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double? Temperature { get; set; }
        public string? Region { get; set; }
        public string SearchKeyword { get; set; } = string.Empty;
    }
}
