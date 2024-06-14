using Newtonsoft.Json;
using OsEngine.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OsEngine.Market.Servers.Bitfinex.Json
{
    public class BitfinexBook
    {

        [JsonProperty("ORDER_ID")]
        public string orderId { get; set; }

        [JsonProperty("PRICE")]
        public string price { get; set; }

        [JsonProperty("AMOUNT")]
        public string amount { get; set; }
    }
}
