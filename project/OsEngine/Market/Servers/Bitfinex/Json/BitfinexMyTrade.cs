using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OsEngine.Market.Servers.Bitfinex.Json
{
    public class BitfinexMyTrade
    {
        // POST https://api.bitfinex.com/v2/auth/r/trades/hist
        [JsonProperty("ID")]
        public string TradeId;                   //402088407, //ID

        [JsonProperty("SYMBOL")]
        public string Symbol;               //tETHUST", //SYMBOL

        [JsonProperty("MTS")]
        public string Time;                  //1574963975602, //MTS

        [JsonProperty("ORDER_ID")]
        public string OrderId;             //34938060782, //ORDER_ID

        [JsonProperty("EXEC_AMOUNT")] 
        public string Amount;          //-0.2, //EXEC_AMOUNT
        
        [JsonProperty("EXEC_PRICE")] 
        public string Price;           //153.57, //EXEC_PRICE

        [JsonProperty("ORDER_TYPE")] 
        public string OrderType;           //MARKET, //ORDER_TYPE

        [JsonProperty("ORDER_PRICE")]
        public string OrderPrice;          //0, //ORDER_PRICE
       
        [JsonProperty("MAKER")] 
        public string Maker;                //-1, //MAKER
       
        [JsonProperty("FEE")] 
        public string Fee;                  //-0.061668, //FEE
       
        [JsonProperty("FEE_CURRENCY")] 
        public string FeeCurrency;         //USD, //FEE_CURRENCY
       
        [JsonProperty("CID")] 
        public string ClientOrderId;                  //1234 //CID
    }
}











