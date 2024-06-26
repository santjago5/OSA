using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OsEngine.Market.Servers.Bitfinex.Json
{
    //POST https://api.bitfinex.com/v2/auth/r/trades/hist
   public class BitfinexTrades
    {
        [JsonProperty("ID")]
        public string id;       //402088407

        [JsonProperty("SYMBOL")]
        public string symbol;	//"tETHUST"
  	

        [JsonProperty("MTS")]
        public string time;      //1574963975602

        [JsonProperty("ORDER_ID")]
        public string orderId;    //34938060782

        [JsonProperty("EXEC_AMOUNT")]
        public string execAmount; //-0.2

        [JsonProperty("EXEC_PRICE")]
        public string execPrice; // 153.57

        [JsonProperty("ORDER_TYPE")]
        public string order_type;//	"MARKET"
  
           
        [JsonProperty("ORDER_PRICE")]
        public string orderPrice;//0
            
        [JsonProperty("MAKER")]
        public string maker;//	-1

        [JsonProperty("FEE")]
        public string fee;//-0.061668, 

        [JsonProperty("FEE_CURRENCY")]
        public string feeCurrency;	//"USD", 

        [JsonProperty("CID")]
        public string cid;//1234 //	Client Order ID


    }

    
  	
   

  	
  
  	
  
  	
}
