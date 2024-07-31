using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OsEngine.Market.Servers.Bitfinex.Json
{


    public class SubscriptionResponseTrade
    {
        public string Event { get; set; }
        public string Channel { get; set; }
        public string ChanId { get; set; }
        public string Symbol { get; set; }
        public string Pair { get; set; }
    }
    //Класс для представления трейда:
    public class BitfinexTrades
    {
        public string Id { get; set; }
        public string Mts { get; set; }
        public string Amount { get; set; }
        public string Price { get; set; }
    }
    //Класс для представления снимка и обновлений:
    public class TradeSnapshot
    {
        public string ChannelId { get; set; }
        public List<BitfinexTrades> Trades { get; set; }
    }

    public class TradeUpdate
    {
        public string ChannelId { get; set; }
        public string MsgType { get; set; }
        public BitfinexTrades Trade { get; set; }
    }

}
