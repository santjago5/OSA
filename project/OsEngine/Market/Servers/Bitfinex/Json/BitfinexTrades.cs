using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OsEngine.Market.Servers.Bitfinex.Json
{
  

    public class BitfinexTrades
    {
        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }

        [JsonPropertyName("tradeId")]
        public long TradeId { get; set; }

        [JsonPropertyName("amount")]
        public double Amount { get; set; }

        [JsonPropertyName("price")]
        public double Price { get; set; }
    }
}
