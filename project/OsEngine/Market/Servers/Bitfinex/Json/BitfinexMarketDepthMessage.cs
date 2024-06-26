using Newtonsoft.Json;
using OsEngine.Logging;
using OsEngine.Market.Servers.Alor.Json;
using OsEngine.Market.Servers.Bitfinex.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OsEngine.Market.Servers.Bitfinex.Json
{
    public class BitfinexMarketDepth //Total amount available at that price level (if AMOUNT > 0 then bid else ask)
    {

        public decimal Price { get; set; }
        public decimal Count { get; set; }
        public decimal Amount { get; set; }


        public List<List<string>> asks;
        public List<List<string>> bids;

    }
  
}

//public class ResponseWebSocketDepthItem
//{
//    public string symbol { get; set; }
//    public long time { get; set; }
//    public List<List<string>> asks { get; set; }
//    public List<List<string>> bids { get; set; }
//}
//public class BitfinexMarketDepth //Total amount available at that price level (if AMOUNT > 0 then bid else ask)
//{
//    [JsonProperty("SYMBOL")]
//    public string symbol { get; set; }

//    [JsonProperty("BID")]
//    public string bid { get; set; }

//    [JsonProperty("ASK")]
//    public string ask { get; set; }

//    [JsonProperty("MTS")]
//    public string time { get; set; }

//    public List<string[]> bids;
//    public List<string[]> asks;

//}