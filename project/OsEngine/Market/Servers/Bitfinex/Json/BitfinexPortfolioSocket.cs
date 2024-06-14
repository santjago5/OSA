using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tinkoff.InvestApi.V1;

namespace OsEngine.Market.Servers.Bitfinex.Json
{
    public class BitfinexPortfolioSocket
    {
        [JsonProperty("CHAN_ID")]
        public string chanId { get;set;}
        
        [JsonProperty("TYPE")]
        public string  type {get;set;}
        
        [JsonProperty("WALLET_TYPE")]
        public string walletType{ get; set; }

        [JsonProperty("CURRENCY")]
        public string currency { get; set; }

        [JsonProperty("BALANCE")]
        public string balance { get; set; }

        [JsonProperty("UNSETTLED_INTEREST")]
        public string unsettled_interest{ get; set; }

        [JsonProperty("BALANCE_AVAILABLE")]

        public string balanceAvailable{get; set; }
 
        [JsonProperty("DESCRIPTION")]
        public string description { get; set; }

        [JsonProperty("META")]
        public string meta{ get; set; }



        //[0,"wu",["exchange","BTC",1.61169184,0,null,"Exchange 0.01 BTC for USD @ 7804.6",{"reason":"TRADE","order_id":34988418651,"order_id_oppo":34990541044,"trade_price":"7804.6","trade_amount":"0.01"}]]
    }
}
