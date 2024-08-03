
using Newtonsoft.Json;
using OsEngine.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;

namespace OsEngine.Market.Servers.Bitfinex.Json
{
  

    public class BitfinexCandle
    {
        // GET https://api-pub.bitfinex.com/v2/candles/{candle}/{section}

        // Response with section="last" OR  Response with section="hist"

        [JsonProperty("MTS")]
        public string Time { get; set; }           //1678465320000 //MTS 

        [JsonProperty("OPEN")]
        public string Open { get; set; }        //20097 //OPEN

        [JsonProperty("CLOSE")]
        public string Close { get; set; }         //20094 //CLOSE

        [JsonProperty("HIGH")]
        public string High { get; set; }            //20097 //HIGH

        [JsonProperty("LOW")]
        public string Low { get; set; }           //20094 //LOW

        [JsonProperty("VOLUME")]
        public string Volume { get; set; }          //0.07870586 //VOLUME

    }

    // Определение класса для структуры данных снимка свечей
    public class CandleSnapshot
    {
        public int ChannelId { get; set; }       // Идентификационный номер канала
        public List<BitfinexCandle> Candles { get; set; } // Список свечей
    }



}
//1m: one minute
//5m : five minutes
//15m : 15 minutes
//30m : 30 minutes
//1h : one hour
//3h : 3 hours
//6h : 6 hours
//12h : 12 hours
//1D : one day
//1W : one week
//14D : two weeks
//1M : one month