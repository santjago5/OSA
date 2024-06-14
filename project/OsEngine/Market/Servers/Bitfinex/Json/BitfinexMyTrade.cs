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
        public string id;                   //402088407, //ID

        [JsonProperty("SYMBOL")]
        public string symbol;               //tETHUST", //SYMBOL

        [JsonProperty("MTS")]
        public string time;                  //1574963975602, //MTS

        [JsonProperty("ORDER_ID")]
        public string orderId;             //34938060782, //ORDER_ID

        [JsonProperty("EXEC_AMOUNT")] 
        public string execAmount;          //-0.2, //EXEC_AMOUNT
        
        [JsonProperty("EXEC_PRICE")] 
        public string execPrice;           //153.57, //EXEC_PRICE

        [JsonProperty("ORDER_TYPE")] 
        public string orderType;           //MARKET, //ORDER_TYPE

        [JsonProperty("ORDER_PRICE")]
        public string orderPrice;          //0, //ORDER_PRICE
       
        [JsonProperty("MAKER")] 
        public string maker;                //-1, //MAKER
       
        [JsonProperty("FEE")] 
        public string fee;                  //-0.061668, //FEE
       
        [JsonProperty("FEE_CURRENCY")] 
        public string feeCurrency;         //USD, //FEE_CURRENCY
       
        [JsonProperty("CID")] 
        public string cid;                  //1234 //CID
    }
}











