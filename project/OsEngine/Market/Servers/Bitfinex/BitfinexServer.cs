
using Newtonsoft.Json;
using OsEngine.Entity;
using OsEngine.Language;
using OsEngine.Logging;
using OsEngine.Market.Servers.Bitfinex.Json;
using OsEngine.Market.Servers.Entity;
using RestSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using WebSocket4Net;
using Order = OsEngine.Entity.Order;
using Security = OsEngine.Entity.Security;
using BitfinexSecurity = OsEngine.Market.Servers.Bitfinex.Json.BitfinexSecurity;
using BitfinexMarketDepth = OsEngine.Market.Servers.Bitfinex.Json.BitfinexMarketDepth;
using BitfinexOrder = OsEngine.Market.Servers.Bitfinex.Json.BitfinexOrder;
using Candle = OsEngine.Entity.Candle;
using Method = RestSharp.Method;
using SuperSocket.ClientEngine;
using Trade = OsEngine.Entity.Trade;
using OsEngine.Market.Servers.Pionex.Entity;
using OsEngine.Market.Servers.BitMax;
using System.Windows.Documents;
using static System.Net.WebRequestMethods;
using Google.Protobuf.WellKnownTypes;
using OsEngine.Market.Servers.Bybit;
using OsEngine.Market.Servers.Transaq.TransaqEntity;
using OsEngine.Market.Servers.OKX.Entity;
using System.Linq;
using MarketDepth = OsEngine.Entity.MarketDepth;








namespace OsEngine.Market.Servers.Bitfinex
{
    public class BitfinexServer : AServer//Класс с конечной логикой коннектора
    {
        public BitfinexServer()
        {
            BitfinexServerRealization realization = new BitfinexServerRealization();// Место создания реализации IServerRealization.
            ServerRealization = realization;

            CreateParameterString(OsLocalization.Market.ServerParamPublicKey, "");
            CreateParameterPassword(OsLocalization.Market.ServerParamSecretKey, "");

        }

        public List<Candle> GetCandleHistory(string nameSec, TimeSpan tf)
        {
            return ((BitfinexServer)ServerRealization).GetCandleHistory(nameSec, tf);
        }
    }

    public class BitfinexServerRealization : IServerRealization //Чтобы AServer мог спокойно через единый интерфейс обращаться к различным вариантам IServerRealization.
    {
        #region 1 Constructor, Status, Connection

        public BitfinexServerRealization()
        {
            ServerStatus = ServerConnectStatus.Disconnect;

            Thread threadForPublicMessages = new Thread(PublicMessageReader);
            threadForPublicMessages.IsBackground = true;
            threadForPublicMessages.Name = "PublicMessageReaderBitfinex";
            threadForPublicMessages.Start();

            Thread threadForPrivateMessages = new Thread(PrivateMessageReader);
            threadForPrivateMessages.IsBackground = true;
            threadForPrivateMessages.Name = "PrivateMessageReaderBitfinex";
            threadForPrivateMessages.Start();

        }

        public DateTime ServerTime { get; set; }


        public void Connect()
        {
            try
            {
                _securities.Clear();
                _portfolios.Clear();
                // _subscribledSecurities.Clear();

                SendLogMessage("Start Bitfinex Connection", LogMessageType.System);

                _publicKey = ((ServerParameterString)ServerParameters[0]).Value;
                _secretKey = ((ServerParameterPassword)ServerParameters[1]).Value;

                if (string.IsNullOrEmpty(_publicKey) || string.IsNullOrEmpty(_secretKey))

                {
                    SendLogMessage("Connection failed. Authorization exception", LogMessageType.Error);
                    ServerStatus = ServerConnectStatus.Disconnect;
                    DisconnectEvent();
                    return;
                }
                string apiPath = "/platform/status";

                RestClient client = new RestClient(_getUrl);
                RestRequest request = new RestRequest(apiPath, Method.GET);
                request.AddHeader("accept", "application/json");

                IRestResponse response = client.Execute(request);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    //WebSocketPublicMessage = new ConcurrentQueue<string>();
                    //WebSocketPrivateMessage = new ConcurrentQueue<string>();
                    CreateWebSocketConnection();
                }
            }

            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
                SendLogMessage("Connection cannot be open. Bitfinex. Error request", LogMessageType.Error);
                ServerStatus = ServerConnectStatus.Disconnect;
                DisconnectEvent();
            }

        }

        public void Dispose()
        {
            try
            {
                _securities.Clear();
                _portfolios.Clear();

                DeleteWebSocketConnection();
            }
            catch (Exception exception)
            {
                SendLogMessage("Connection closed by Bitfinex. WebSocket Data Closed Event" + exception.ToString(), LogMessageType.System);
            }

            //WebSocketPublicMessage = new ConcurrentQueue<string>();//?????
            //WebSocketPrivateMessage = new ConcurrentQueue<string>();//?????

            if (ServerStatus != ServerConnectStatus.Disconnect)
            {
                ServerStatus = ServerConnectStatus.Disconnect;
                DisconnectEvent();
            }
        }

        public ServerType ServerType
        {
            get { return ServerType.Bitfinex; }
        }

        public event Action ConnectEvent;

        public event Action DisconnectEvent;



        #endregion


        /// <summary>
        /// настройки коннектора
        /// </summary>
        #region 2 Properties 
        public List<IServerParameter> ServerParameters { get; set; }
        public ServerConnectStatus ServerStatus { get; set; }

        private string _publicKey = "";

        private string _secretKey = "";

        private string _getUrl = "https://api-pub.bitfinex.com/v2";
        private string _postUrl = "https://api.bitfinex.com/v2";
        private HttpClient _httpClient = new HttpClient();
        string nonce = (DateTimeOffset.Now.ToUnixTimeMilliseconds() * 1000).ToString();


        #endregion



        /// <summary>
        /// Запрос доступных для подключения бумаг у подключения. 
        /// </summary>
        #region 3 Securities
        RateGate _rateGateGetsecurity = new RateGate(2, TimeSpan.FromMilliseconds(1000));
        private List<Security> _securities = new List<Security>();

        public event Action<List<Security>> SecurityEvent;

        public void GetSecurities()
        {
            _rateGateGetsecurity.WaitToProceed();

            try
            {


                RestClient client = new RestClient(_getUrl);
                RestRequest request = new RestRequest("/tickers?symbols=ALL");
                request.AddHeader("accept", "application/json");
                IRestResponse response = client.Get(request);


                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string jsonResponse = response.Content;


                    List<List<object>> securityList = JsonConvert.DeserializeObject<List<List<object>>>(jsonResponse);

                    if (securityList == null)
                    {
                        SendLogMessage("Deserialization resulted in null", LogMessageType.Error);
                        return;
                    }

                    List<BitfinexSecurity> security = new List<BitfinexSecurity>();

                    for (int i = 0; i < 3; i++)
                    //for (int i = 0; i < securityList.Count; i++)
                    {
                        var item = securityList[i];

                        BitfinexSecurity ticker = new BitfinexSecurity

                        {

                            Symbol = item[0].ToString(),
                            Bid = (item[1]).ToString(),
                            BidSize = (item[2]).ToString(),
                            Ask = (item[3]).ToString(),
                            AskSize = (item[4]).ToString(),
                            DailyChange = (item[5]).ToString(),
                            DailyChangeRelative = (item[6]).ToString(),
                            LastPrice = (item[7]).ToString(),
                            Volume = (item[8]).ToString(),
                            High = (item[9]).ToString(),
                            Low = (item[10]).ToString()
                        };

                        security.Add(ticker);

                    }



                    UpdateSecurity(security);

                }

                //else
                //{
                //    //SendLogMessage($"Result: LogMessageType.Error\n"
                //    //    + $"Message: {stateResponse.message}", LogMessageType.Error);
                //}


                else
                {
                    SendLogMessage("Securities request exception. Status: " + response.StatusCode, LogMessageType.Error);

                }

                if (_securities.Count > 0)

                {
                    SendLogMessage("Securities loaded. Count: " + _securities.Count, LogMessageType.System);

                    SecurityEvent?.Invoke(_securities);
                }
            }
            catch (Exception exception)
            {
                SendLogMessage("Securities request exception" + exception.ToString(), LogMessageType.Error);

            }
        }



        private void UpdateSecurity(List<BitfinexSecurity> security)
        {
            //List<Security> securities = new List<Security>();

            try
            {
                if (security == null || security.Count == 0)
                {
                    return;
                }

                for (int i = 0; i < security.Count; i++)
                {
                    var securityData = security[i];


                    Security newSecurity = new Security();
                    {
                        newSecurity.Name = securityData.Symbol.ToString();
                        newSecurity.NameFull = securityData.Symbol.ToString();
                        newSecurity.NameClass = "Spot";
                        newSecurity.NameId = Convert.ToString(securityData.Symbol);/////
                        newSecurity.Exchange = ServerType.Bitfinex.ToString();

                        newSecurity.Lot = 1;

                        newSecurity.SecurityType = SecurityType.CurrencyPair;
                        //newSecurity.SecurityType GetSecurityType(newSecurity);
                        newSecurity.State = SecurityStateType.Activ;
                        //newSecurity.Decimals = Convert.ToInt32(securityData[11].ToString());
                        newSecurity.PriceStep = newSecurity.Decimals.GetValueByDecimals();
                        newSecurity.DecimalsVolume = newSecurity.Decimals;
                        //  newSecurity.SecurityType = GetSecurityType(.type);



                        //PriceStep = array[6].ToDecimal();
                        //Lot = array[7].ToDecimal();
                        //PriceStepCost = array[8].ToDecimal();


                        _securities.Add(newSecurity);////////////////////////;


                    };


                    SecurityEvent(_securities);
                }


            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }

            SecurityEvent?.Invoke(_securities);
        }

        private SecurityType GetSecurityType(string type)
        {
            SecurityType _securityType = SecurityType.None;

            switch (type)
            {
                case "f":
                    _securityType = SecurityType.Futures;
                    break;
                case "t":
                    _securityType = SecurityType.CurrencyPair;
                    break;
            }
            return _securityType;
        }
        #endregion


        /// <summary>
        /// Запрос доступных портфелей у подключения. 
        /// </summary>
        #region 4 Portfolios

        private List<Portfolio> _portfolios = new List<Portfolio>();

        public event Action<List<Portfolio>> PortfolioEvent;

        private RateGate _rateGatePortfolio = new RateGate(1, TimeSpan.FromMilliseconds(200));

        public void GetPortfolios()///////////////////////////
        {
            if (_portfolios.Count != 0)
            {
                PortfolioEvent?.Invoke(_portfolios);
            }

            CreateQueryPortfolio();
        }




        private void CreateQueryPortfolio()
        {
            _rateGatePortfolio.WaitToProceed();

            try
            {

                string apiPath = "v2/auth/r/wallets";
                string signature = $"/api/{apiPath}{nonce}";

                string sig = ComputeHmacSha384(_secretKey, signature);
                string body = "";
                var client = new RestClient("https://api.bitfinex.com/v2/auth/r/wallets");

                var request = new RestRequest("", Method.POST);
                request.AddHeader("bfx-nonce", nonce);
                request.AddHeader("bfx-apikey", _publicKey);
                request.AddHeader("bfx-signature", sig);

                request.AddJsonBody(body);

                request.AddParameter("accept", "application/json");

                try
                {

                    IRestResponse response = client.Execute(request);


                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        string jsonResponse = response.Content;


                        List<List<object>> walletList = JsonConvert.DeserializeObject<List<List<object>>>(jsonResponse);

                        if (walletList == null)
                        {
                            SendLogMessage("walletList is null", LogMessageType.Error);
                            return;
                        }

                        List<BitfinexPortfolioRest> wallets = new List<BitfinexPortfolioRest>();


                        for (int i = 0; i < walletList.Count; i++)
                        {
                            var walletData = walletList[i];

                            BitfinexPortfolioRest wallet = new BitfinexPortfolioRest
                            {
                                PortfolioName = walletData[0]?.ToString(),
                                Currency = walletData[1]?.ToString(),
                                Balance = Convert.ToDecimal(walletData[2]),
                                UnsettledInterest = Convert.ToDecimal(walletData[3]),
                                AvailableBalance = Convert.ToDecimal(walletData[4]),
                                LastChange = walletData[5]?.ToString()


                            };
                            wallets.Add(wallet);
                        }
                        UpdatePortfolio(wallets);

                    }
                    else
                    {
                        Console.WriteLine($"Error Status code {response.StatusCode}: {response.Content}");
                    }

                }
                catch (Exception exception)
                {
                    SendLogMessage(exception.ToString(), LogMessageType.Error);
                }
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }

        private void UpdatePortfolio(List<BitfinexPortfolioRest> assets)
        {
            try
            {
                Portfolio portfolio = new Portfolio
                {
                    Number = "Bitfinex",
                    ValueBegin = 1,
                    ValueCurrent = 1
                };


                if (assets == null || assets.Count == 0)
                {
                    return;
                }


                for (int i = 0; i < assets.Count; i++)
                {
                    if (decimal.TryParse(assets[i].AvailableBalance.ToString(), out decimal availableBalance) &&
                        decimal.TryParse(assets[i].UnsettledInterest.ToString(), out decimal unsettledInterest))
                    {


                        PositionOnBoard position = new PositionOnBoard
                        {
                            PortfolioName = assets[i].PortfolioName,
                            ValueBegin = availableBalance,
                            ValueCurrent = availableBalance,
                            ValueBlocked = unsettledInterest,
                            SecurityNameCode = assets[i].Currency
                        };


                        portfolio.SetNewPosition(position);
                    }
                    else
                    {
                        SendLogMessage($"Failed to parse balance or interest for asset {assets[i].Currency}", LogMessageType.Error);
                    }
                }
                    PortfolioEvent?.Invoke(new List<Portfolio> { portfolio });
                
            }
            catch (Exception exception)
            {
                SendLogMessage($"{exception.Message} {exception.StackTrace}", LogMessageType.Error);
            }
        }



        #endregion


        /// <summary>
        /// Запросы данных по свечкам и трейдам. 
        /// </summary>
        #region 5 Data 

        private readonly RateGate _rateGateCandleHistory = new RateGate(700, TimeSpan.FromSeconds(30));

        public List<Trade> GetTickDataToSecurity(Security security, DateTime startTime, DateTime endTime, DateTime actualTime)
        {
            return null;
        }
        public List<Candle> GetLastCandleHistory(Security security, TimeFrameBuilder timeFrameBuilder, int candleCount)
        {
            DateTime startTime = DateTime.UtcNow - TimeSpan.FromMinutes(timeFrameBuilder.TimeFrameTimeSpan.Minutes * candleCount);
            DateTime endTime = DateTime.UtcNow.AddMilliseconds(-10);
            DateTime actualTime = startTime;


            return GetCandleDataToSecurity(security, timeFrameBuilder, startTime, endTime, actualTime);////////////

        }


        public List<Candle> GetCandleHistory(string nameSec, TimeSpan tf, int CountToLoad, DateTime timeEnd)
        {

            int needToLoadCandles = CountToLoad;


            List<Candle> candles = new List<Candle>();
            DateTime fromTime = timeEnd - TimeSpan.FromMinutes(tf.TotalMinutes * CountToLoad);//перепрыгивает на сутки назад


            do
            {

                // ограничение Bitfinex: For each query, the system would return at most 1500 pieces of data. To obtain more data, please page the data by time.
                int maxCandleCountToLoad = 10000;
                int limit = Math.Min(needToLoadCandles, maxCandleCountToLoad);

                List<Candle> rangeCandles; //= new List<Candle>(); //////не нужен новый список 

                rangeCandles = CreateQueryCandles(nameSec, GetStringInterval(tf), fromTime, timeEnd);

                if (rangeCandles == null)
                    return null; // нет данных

                rangeCandles.Reverse();

                candles.InsertRange(0, rangeCandles);

                if (candles.Count != 0)
                {
                    timeEnd = candles[0].TimeStart;
                }

                needToLoadCandles -= limit;

            }

            while (needToLoadCandles > 0);

            return candles;
        }



        public List<Candle> GetCandleDataToSecurity(Security security, TimeFrameBuilder timeFrameBuilder, DateTime startTime, DateTime endTime, DateTime actualTime)/////string
        {

            if (startTime != actualTime)
            {
                startTime = actualTime;
            }

            if (actualTime > endTime)
            {
                return null;
            }

            if (startTime > endTime)
            {
                return null;
            }

            if (endTime > DateTime.Now)
            {
                endTime = DateTime.Now;
            }


            int countNeedToLoad = GetCountCandlesFromSliceTime(startTime, endTime, timeFrameBuilder.TimeFrameTimeSpan);

            return GetCandleHistory(security.NameFull, timeFrameBuilder.TimeFrameTimeSpan, countNeedToLoad, endTime);
        }



        private int GetCountCandlesFromSliceTime(DateTime startTime, DateTime endTime, TimeSpan tf)
        {
            TimeSpan TimeSlice = endTime - startTime;
            if (tf.Hours != 0)
            {


                return Convert.ToInt32(TimeSlice.TotalHours / tf.TotalHours);
            }
            else
            {

                return Convert.ToInt32(TimeSlice.TotalMinutes / tf.Minutes);
            }
        }

        private string GetStringInterval(TimeSpan tf)
        {
            // Type of candlestick patterns: 1min, 3min, 5min, 15min, 30min, 1hour, 3hour, 6hour,  12hour, 1day, 1week, 14 days,1 month
            // return tf.Minutes != 0 ? $"{tf.Minutes}min" : $"{tf.Hours}hour";
            if (tf.Minutes != 0)
            {
                return $"{tf.Minutes}min";
            }
            else
            {
                return $"{tf.Hours}hour";
            }
        }



        private List<Candle> CreateQueryCandles(string nameSec, string interval, DateTime timeFrom, DateTime timeTo)
        {


            //string exchangeType =  BitfinexExchangeType.SpotExchange.ToString();
            //if (nameSec.StartsWith("t"))
            //{
            //    exchangeType = BitfinexExchangeType.SpotExchange.ToString();
            //}
            //if (nameSec.StartsWith("f"))
            //{
            //    exchangeType = BitfinexExchangeType.FuturesExchange.ToString();
            //}
            //else
            //{
            //    exchangeType = BitfinexExchangeType.MarginExchange.ToString();
            //}



            _rateGateCandleHistory.WaitToProceed(100);



            DateTime yearBegin = new DateTime(1970, 1, 1);

            var timeStampStart = timeFrom - yearBegin;
            //var startTimeMilliseconds = timeStampStart.TotalMilliseconds;
            // string startTime = Convert.ToInt64(startTimeMilliseconds).ToString();

            //var timeStampEnd = timeTo - yearBegin;
            //var endTimeMilliseconds = timeStampEnd.TotalMilliseconds;
            //string endTime = Convert.ToInt64(endTimeMilliseconds).ToString();


            string startTime = Convert.ToInt64((timeFrom - yearBegin).TotalMilliseconds).ToString();
            string endTime = Convert.ToInt64((timeTo - yearBegin).TotalMilliseconds).ToString();

            // string section = timeFrom !=DateTime.Today ?"hist":"last";////////////

            // var client = new RestClient($"https://api-pub.bitfinex.com/v2/candles/trade:{interval}:{nameSec}/hist?start={startTime}&end={endTime}");


            //RestClient client = new RestClient($"https://api-pub.bitfinex.com/v2/candles/trade:{interval}:{nameSec}/hist?start={startTimeMilliseconds}&end={endTimeMilliseconds}");

            // RestClient client = new RestClient($"https://api-pub.bitfinex.com/v2/candles/trade:1m:tNEOUSD/hist");

            RestClient client = new RestClient($"https://api-pub.bitfinex.com/v2/candles/trade:1m:{nameSec}/hist?start={startTime}&end={endTime}");

            // var request = new RestRequest("", Method.GET);

            var request = new RestRequest("");

            request.AddHeader("accept", "application/json");

            var response = client.Get(request);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                string jsonResponse = response.Content;

                List<List<object>> candles = JsonConvert.DeserializeObject<List<List<object>>>(jsonResponse);



                if (candles == null || candles.Count == 0)
                {
                    return null;
                }

                List<BitfinexCandle> candleList = new List<BitfinexCandle>();

                //for (int i = 0; i < candles.Count; i++) ////////////////////////
                  for (int i = 0; i < 2; i++)                                       
                {
                    var candleData = candles[i];

                    if (candleData[0] == null ||
                        candleData[1] == null ||
                        candleData[2] == null ||
                        candleData[3] == null ||
                        candleData[4] == null ||
                        candleData[5] == null)
                    {
                        SendLogMessage("Candle data contains null values", LogMessageType.Error);

                        continue;
                    }

                    if (Convert.ToDecimal(candleData[1]) == 0 ||
                        Convert.ToDecimal(candleData[2]) == 0 ||
                        Convert.ToDecimal(candleData[3]) == 0 ||
                        Convert.ToDecimal(candleData[4]) == 0 ||
                        Convert.ToDecimal(candleData[5]) == 0)
                    {
                        SendLogMessage("Candle data contains zero values", LogMessageType.Error);

                        continue;
                    }



                    BitfinexCandle newCandle = new BitfinexCandle

                    {
                        Time = candleData[0].ToString(),
                        Open = candleData[1].ToString(),
                        Close = candleData[2].ToString(),
                        High = candleData[3].ToString(),
                        Low = candleData[4].ToString(),
                        Volume = candleData[5].ToString()
                    };

                    candleList.Add(newCandle);
                }

                return ConvertToCandles(candleList);
            }

            else
            {
                SendLogMessage($"Http State Code: {response.StatusCode}", LogMessageType.Error);
            }
            return null;
        }

        private List<Candle> ConvertToCandles(List<BitfinexCandle> candleList)
        {
            List<Candle> candles = new List<Candle>();

            for (int i = 0; i < candleList.Count; i++)
            {
                var candle = candleList[i];
                try
                {

                    if (string.IsNullOrEmpty(candle.Time) || string.IsNullOrEmpty(candle.Open) || string.IsNullOrEmpty(candle.Close) || string.IsNullOrEmpty(candle.High) || string.IsNullOrEmpty(candle.Low) || string.IsNullOrEmpty(candle.Volume))
                    {
                        SendLogMessage("Candle data contains null or empty values", LogMessageType.Error);
                        continue;
                    }

                    //if (Convert.ToDecimal(candle.Open) == 0 || Convert.ToDecimal(candle.Close) == 0 || Convert.ToDecimal(candle.High) == 0 || Convert.ToDecimal(candle.Low) == 0 || Convert.ToDecimal(candle.Volume) == 0)
                    //{
                    //    SendLogMessage("Candle data contains zero values", LogMessageType.Error);
                    //    continue;
                    //}

                    Candle newCandle = new Candle
                    {
                        State = CandleState.Finished,
                        TimeStart = TimeManager.GetDateTimeFromTimeStamp(Convert.ToInt64(candle.Time)),
                        Open = Convert.ToDecimal(candle.Open),
                        Close = Convert.ToDecimal(candle.Close),
                        High = Convert.ToDecimal(candle.High),
                        Low = Convert.ToDecimal(candle.Low),
                        Volume = Convert.ToDecimal(candle.Volume)
                    };

                    candles.Add(newCandle);
                }
                catch (FormatException ex)
                {
                    SendLogMessage($"Format exception: {ex.Message}", LogMessageType.Error);
                }

            }
            return candles;
        }


        #endregion


        /// <summary>
        /// Создание вёбсокет соединения. 
        /// </summary>
        #region  6 WebSocket creation



        private WebSocket _webSocketPublic;// wss://api-pub.bitfinex.com/ws/2
        private WebSocket _webSocketPrivate; // wss://api.bitfinex.com/ws/2



        private readonly string _webSocketPublicUrl = "wss://api-pub.bitfinex.com/ws/2";
        private readonly string _webSocketPrivateUrl = "wss://api.bitfinex.com/ws/2";

        private void CreateWebSocketConnection()
        {
            try
            {
                //_subscriptionsPublic.Clear();
                //_subscriptionsPrivate.Clear();

                if (_webSocketPublic != null)
                {
                    return;
                }

                //_socketPublicIsActive = false;////////////
              //  _socketPrivateIsActive = false;////////////////


                _webSocketPublic = new WebSocket(_webSocketPublicUrl);
                _webSocketPublic.EnableAutoSendPing = true;
                _webSocketPublic.AutoSendPingInterval = 15;

                _webSocketPublic.Opened += WebSocketPublic_Opened;
                _webSocketPublic.Closed += WebSocketPublic_Closed;
                _webSocketPublic.MessageReceived += WebSocketPublic_MessageReceived;
                _webSocketPublic.Error += WebSocketPublic_Error;

                _webSocketPublic.Open();


                _webSocketPrivate = new WebSocket(_webSocketPrivateUrl);
                _webSocketPrivate.EnableAutoSendPing = true;
                _webSocketPrivate.AutoSendPingInterval = 15;

                _webSocketPrivate.Opened += WebSocketPrivate_Opened;
                _webSocketPrivate.Closed += WebSocketPrivate_Closed;
                _webSocketPrivate.MessageReceived += WebSocketPrivate_MessageReceived;
                _webSocketPrivate.Error += WebSocketPrivate_Error;

                _webSocketPrivate.Open();

            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }


        private void DeleteWebSocketConnection()
        {
            try
            {
                if (_webSocketPublic != null)
                {
                    try
                    {
                        _webSocketPublic.Close();
                    }
                    catch (Exception exception)
                    {
                        SendLogMessage(exception.ToString(), LogMessageType.System);
                    }

                    _webSocketPublic.Opened -= WebSocketPublic_Opened;
                    _webSocketPublic.Closed -= WebSocketPublic_Closed;
                    _webSocketPublic.MessageReceived -= WebSocketPublic_MessageReceived;
                    _webSocketPublic.Error -= WebSocketPublic_Error;
                    _webSocketPublic = null;

                }
                if (_webSocketPrivate != null)
                {

                    try
                    {
                        _webSocketPrivate.Close();
                    }
                    catch (Exception exception)
                    {
                        SendLogMessage(exception.ToString(), LogMessageType.System);
                    }


                    _webSocketPrivate.Opened -= WebSocketPrivate_Opened;
                    _webSocketPrivate.Closed -= WebSocketPrivate_Closed;
                    _webSocketPrivate.MessageReceived -= WebSocketPrivate_MessageReceived;
                    _webSocketPrivate.Error -= WebSocketPrivate_Error;
                    _webSocketPrivate = null;

                }

            }

            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.System);
            }

        }


        private bool _socketPublicIsActive;

        private bool _socketPrivateIsActive;

        private bool IsDispose;


        #endregion



        /// <summary>
        /// Обработка входящих сообщений от вёбсокета. И что важно в данном конкретном случае, Closed и Opened методы обязательно должны находиться здесь,
        /// </summary>
        #region  7 WebSocket events
        private void WebSocketPublic_Opened(object sender, EventArgs e)
        {

            _socketPublicIsActive = true;//отвечает за соединение
            CheckActivationSockets();
           SendLogMessage("Connection to public data is Open", LogMessageType.System);
           

        }

        private void WebSocketPublic_Closed(object sender, EventArgs e)
        {
            try
            {
               
                if (ServerStatus != ServerConnectStatus.Disconnect)
                {

                    ServerStatus = ServerConnectStatus.Disconnect;
                    DisconnectEvent();
                }
                 SendLogMessage("Connection Closed by Bitfinex.WebSocket Public сlosed Event ", LogMessageType.Error);


            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }

        private void WebSocketPublic_Error(object sender, ErrorEventArgs e)
        {
            try
            {
                ErrorEventArgs error = e;

                if (error.Exception != null)
                {
                    SendLogMessage(error.Exception.ToString(), LogMessageType.Error);
                }
                if (e == null)
                {
                    return;
                }

                if (string.IsNullOrEmpty(e.ToString()))
                {
                    return;
                }

                if (WebSocketPublicMessage == null)
                {
                    return;
                }


            }
            catch (Exception exception)
            {
                SendLogMessage("Data socket exception" + exception.ToString(), LogMessageType.Error);
            }
        }

        private void WebSocketPublic_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            try
            {
                if (e == null)
                {
                    return;
                }
                if (string.IsNullOrEmpty(e.Message))
                {
                    return;
                }
                //if (e.Message.Length == 4)
                //{ // pong message
                //    return;
                //}

                if (e.Message.Contains("ping"))
                {
                    string pong = $"{{\"event\": \"ping\", \"cid\": 1234}}";
                    _webSocketPublic.Send(pong);
                    return;
                }

                //if (e.Message.Contains("event") && e.Message.Contains("subscribed"))
                //{

                //    var subscribeRequest = new
                //    {
                //        @event = "subscribe",
                //        channel = "book",
                //        symbol = symbol
                //    };

                //    string subscribeMessage = JsonConvert.SerializeObject(subscribeRequest);
                //    _webSocketPublic.Send(subscribeMessage);
                //}
                //else if (e.Message.StartsWith("["))
                //{
                //    UpdateDepth(e.Message);
                //}


                if (WebSocketPublicMessage == null)
                {
                    return;
                }

                if (ServerStatus == ServerConnectStatus.Disconnect)
                {
                    return;
                }


                WebSocketPublicMessage.Enqueue(e.Message);
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }

        private void WebSocketPrivate_Opened(object sender, EventArgs e)
        {

            GenerateAuthenticate();
            _socketPrivateIsActive = true;//отвечает за соединение
            CheckActivationSockets();
            SendLogMessage("Connection to private data is Open", LogMessageType.System);

        }

        private void GenerateAuthenticate()
        {


            string payload = $"AUTH{nonce}";
            string signature = ComputeHmacSha384(payload, _secretKey);

            //string signature;

            //using (var hmac = new HMACSHA384(Encoding.UTF8.GetBytes(_apiSecret)))
            //{
            //    signature = BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).Replace("-", "").ToLower();
            //}
            // Create the payload
            var authMessage = new
            {
                @event = "auth",
                apiKey = _publicKey,
                authSig = signature,
                authPayload = payload,
                authNonce = nonce

            };
            string authMessageJson = JsonConvert.SerializeObject(authMessage);
            SendMessage(authMessageJson);
        }

        private void SendMessage(string message)
        {
            _webSocketPrivate.Send(message);
        }



        private void CheckActivationSockets()
        {
            if (_socketPublicIsActive == false)
            {
                return;
            }

            if (_socketPrivateIsActive == false)
            {
                return;
            }

            try
            {
                if (ServerStatus != ServerConnectStatus.Connect &&
                    _webSocketPublic != null && _webSocketPrivate != null &&
                    _webSocketPublic.State == WebSocketState.Open && _webSocketPrivate.State == WebSocketState.Open)

                {
                    ServerStatus = ServerConnectStatus.Connect;
                    ConnectEvent();
                }
                SendLogMessage("All sockets activated. Connect State", LogMessageType.System);  //добавлена проверка

            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }

        }

        private void WebSocketPrivate_Closed(object sender, EventArgs e)
        {
            try
            {
                SendLogMessage("Connection Closed by Bitfinex. WebSocket Private сlosed Event", LogMessageType.Error);

                if (ServerStatus != ServerConnectStatus.Disconnect)
                {
                    ServerStatus = ServerConnectStatus.Disconnect;
                    DisconnectEvent();
                }
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }

        private void WebSocketPrivate_Error(object sender, SuperSocket.ClientEngine.ErrorEventArgs e)
        {
            try
            {
                var error = e;

                if (error.Exception != null)
                {
                    SendLogMessage(error.Exception.ToString(), LogMessageType.Error);
                }
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }

        private void WebSocketPrivate_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            try
            {
                if (ServerStatus == ServerConnectStatus.Disconnect)
                {
                    return;
                }

                if (e == null)
                {
                    return;
                }

                if (string.IsNullOrEmpty(e.Message))
                {
                    return;
                }
                //if (e.Message.Length == 4)
                //{ // pong message
                //    return;
                //}

                if (e.Message.Contains("ping"))
                {
                    string pong = $"{{\"event\": \"ping\", \"cid\": 1234}}";
                    _webSocketPrivate.Send(pong);
                    return;

                }


                if (WebSocketPrivateMessage == null)
                {
                    return;
                }

                WebSocketPrivateMessage.Enqueue(e.Message);
                ProcessMessage(e.Message);

            }




            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }

        private void ProcessMessage(string message)
        {
            //var jsonDoc = JsonConvert.DeserializeObject<dynamic>(message);
            dynamic jsonDoc = JsonConvert.DeserializeObject(message);

            if (jsonDoc["event"] != null)
            {
                // Handle event messages
                string eventType = jsonDoc["event"];
                switch (eventType)
                {
                    case "auth":
                        if (jsonDoc["status"] != null && jsonDoc["status"] == "OK")
                        {
                            Console.WriteLine("Authenticated successfully");
                        }
                        else
                        {
                            Console.WriteLine("Authentication failed");
                        }
                        break;
                    default:
                        Console.WriteLine($"Unknown event type: {eventType}");
                        break;
                }
            }
            else if (jsonDoc is Newtonsoft.Json.Linq.JArray)
            {
                // Handle data messages
                int channelId = jsonDoc[0];
                string msgType = jsonDoc[1];

                if (channelId == 0)
                {
                    // Wallet messages
                    switch (msgType)
                    {
                        case "ws":

                            HandleWalletSnapshot(jsonDoc[2]);
                            break;
                        case "wu":

                            HandleWalletUpdate(jsonDoc[2]);
                            break;

                    }
                }
            }
        }

        private void HandleWalletSnapshot(dynamic walletSnapshot)
        {
            for (int i = 0; i < walletSnapshot.Count; i++)
            {
                var wallet = walletSnapshot[i];
               
            }
        }

        private void HandleWalletUpdate(dynamic walletUpdate)
        {

        }

       // else if (mes.StartsWith("{\"event\"\book"))

        #endregion

        /// <summary>
        /// Проверка вёбсокета на работоспособность путём отправки ему пингов.
        /// </summary>
        #region  8 WebSocket check alive


        #endregion



        /// <summary>
        /// Подписка на бумагу.С обязательным контролем скорости и кол-ву запросов к методу Subscrible через rateGate.
        /// </summary>
        #region  9 Security subscrible ( или WebSocket security subscrible)

        private RateGate _rateGateSecurity = new RateGate(1, TimeSpan.FromMilliseconds(250));

        List<Security> _subscribledSecurities = new List<Security>();

        public void Subscrible(Security security)
        {
            try
            {

                CreateSubscribleSecurityMessageWebSocket(security);

                Thread.Sleep(200);
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }

        }


       

        private void CreateSubscribleSecurityMessageWebSocket(Security security)//SubscribleTradesAndDepths
        {

            try
            {
                _rateGateSecurity.WaitToProceed();


                if (ServerStatus == ServerConnectStatus.Disconnect)
                {
                    return;
                }

                for (int i = 0; i < _subscribledSecurities.Count; i++)
                {
                    if (_subscribledSecurities[i].Name == security.Name &&
                        _subscribledSecurities[i].NameClass == security.NameClass)
                    // if (_subscribledSecurities[i].Equals(security.Name))
                    {
                        return;
                    }
                }


                GenerateAuthenticate();

                //Subscribing to account info
                //_webSocketPublic.Send($"{{\"event\":\"auth\",\"apiKey\":\"{_publicKey}\",\"authSig\":{signature}\",\"authPayload\":\"{ payload}\",\" authNonce\":{authNonce}\",\"calc\": 1\"}}"); 

                ////tiker websocket-event: "subscribe", channel: "ticker",symbol: SYMBOL 
                _webSocketPublic.Send($"{{\"event\":\"subscribe\",\"channel\":\"ticker\",\"symbol\":{security.Name}\"}}");///????????????

                ////trade websocket//event: "subscribe", channel: "trades", symbol: SYMBOL
                _webSocketPublic.Send($"{{\"event\":\"subscribe\",\"channel\":\"trades\",\"symbol\":{security.Name}\"}}");

                ////candle websocket  //event: "subscribe",//channel: "candles", //key: "trade:1m:tBTCUSD"
                _webSocketPublic.Send($"{{\"event\": \"subscribe\", \"channel\": \"candles\", \"key\": \"trade:1m:{security.Name}\"}}");

                // book websocket(стаканы ?)
                 _webSocketPublic.Send($"{{\"event\":\"subscribe\",\"channel\":\"book\",\"symbol\":\"{security.Name}\",\"prec\":\"P0\",\"freq\":\"F0\"}}");
                //  _webSocketPublic.Send($"{{\"event\":\"subscribe\",\"channel\":\"book\",\"symbol\":\"{security.Name}\",\"prec\":\"P0\",\"freq\":\"F0\",\"len\":\"25\",\"subId\": 123\"}}");


               _subscribledSecurities.Add(security);
         

    }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }

        


        //private void UnsubscribeFromAllWebSockets()
        //{
        //    if (_webSocketPublic == null || _webSocketPrivate == null)
        //    { return; }

        //    for (int i = 0; i < _subscribedSecurities.Count; i++)
        //    {
        //        string securityName = _subscribedSecurities[i];

        //        _webSocketPublic.Send($"{{\"event\":\"unsubscribe\", \"chanId\": \"{chanId}\"}}"); 

        //        _webSocketPrivate.Send($"{{\"event\":\"unsubscribe\", \"chanId\": \"{chanId}\"}}");
        //    }
        //}



        #endregion
        // OFFER_STATUS string Offer Status: ACTIVE, EXECUTED, PARTIALLY FILLED, CANCELED

        /// <summary>
        ///Разбор сообщений от сокета и отправка их наверх
        /// </summary>
        #region  10 WebSocket parsing the messages

        public event Action<MarketDepth> MarketDepthEvent;

        public event Action<Trade> NewTradesEvent;

        public event Action<Order> MyOrderEvent;//новые мои ордера

        public event Action<MyTrade> MyTradeEvent;//новые мои сделки

        //private List<BitfinexSocketSubscription> _subscriptionsPublic = new List<BitfinexSocketSubscription>();

        //private List<BitfinexSocketSubscription> _subscriptionsPrivate = new List<BitfinexSocketSubscription>();

        private ConcurrentQueue<string> WebSocketPublicMessage = new ConcurrentQueue<string>();

        private ConcurrentQueue<string> WebSocketPrivateMessage = new ConcurrentQueue<string>();

  
        private void PublicMessageReader()
        {
            Thread.Sleep(1000);

            while (true)
            {
                try
                {
                    if (ServerStatus != ServerConnectStatus.Connect)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }
                 
                    if (WebSocketPublicMessage.IsEmpty)
                    {
                        Thread.Sleep(1);
                        continue;
                    }

                    string message;

                    WebSocketPublicMessage.TryDequeue(out message);

                    if (message == null)
                    {
                        continue;
                    }

                    if (message.Contains("ping"))
                    {
                        string pong = $"{{\"event\": \"ping\", \"cid\": 1234}}";
                        _webSocketPublic.Send(pong);
                        continue;
                    }


                    //////////////////////////////////////////////////////////////////////////
                   //if( WebSocketPublicMessage.Contains("book"))
                   //  {
                   //     UpdateDepth(message);
                   //     continue;
                   // }
                    //var stream = JsonConvert.DeserializeObject<BitfinexResponseWebSocketMessage<object>>(message);
                    //if (stream.topic != null && stream.data != null)
                    //{
                    //    if (message.Contains("book"))
                    //    {
                    //        UpdateDepth(message);
                    //        continue;
                    //    }
                    //    if (message.Contains("trades"))
                    //    {
                    //        UpdateTrade(message);
                    //        continue;
                    //    }
                    //}
                    ///////////////////////////////////////////
                }
                catch (Exception exception)
                {
                    Thread.Sleep(5000);
                    SendLogMessage(exception.ToString(), LogMessageType.Error);
                }
            }
        }



        private void PrivateMessageReader()//object sender, MessageReceivedEventArgs e
        {
            Thread.Sleep(1000);

            while (true)
            {
                try
                {
                    if (WebSocketPrivateMessage.IsEmpty)
                    {
                        Thread.Sleep(1);
                        continue;
                    }

                    if (ServerStatus != ServerConnectStatus.Connect)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }

                    WebSocketPrivateMessage.TryDequeue(out string message);

                    if (message == null)
                    {
                        continue;
                    }

                    if (message.Equals("pong"))
                    {
                        continue;
                    }

                    var stream = JsonConvert.DeserializeObject<BitfinexResponseWebSocketMessage<object>>(message);
                    if (stream.topic != null && stream.data != null)
                    {
                        //if (stream.topic.Equals("order"))////////////////
                        //{
                        //    UpdateOrder(message);
                        //    continue;
                        //}

                        //if (stream.topic.Equals("trade"))////////////////////////
                        //{
                        //    UpdateMyTrade(message);
                        //    continue;
                        //}
                        //if (stream.topic.Equals("book"))
                        //{
                        //    UpdateDepth(message);
                        //    continue;
                        //}

                        //if (stream.topic.Equals("BALANCE"))
                        //{
                        //    UpdatePortfolio(message);
                        //    continue;
                        //}
                    }

                }
                catch (Exception exception)
                {
                    Thread.Sleep(5000);
                    SendLogMessage(exception.ToString(), LogMessageType.Error);

                }
            }
        }


        // подписки 25 каналов одновременно
        //Ticker
        //Trades
        //Books
        //Raw Books

        //     //Candles
        //     event: "subscribe",
        //channel: "candles",
        //key: "trade:1m:tBTCUSD"

        private void UpdateTrade(string message)
        {
            var responseTrade = JsonConvert.DeserializeObject<BitfinexResponseWebSocketMessage<List<BitfinexMyTrade>>>(message);

            if (responseTrade?.data == null)
            {
                return;
            }

            var element = responseTrade.data[responseTrade.data.Count - 1];

            var trade = new Trade
            {
                SecurityNameCode = element.symbol,
                Price = Convert.ToDecimal(element.execPrice),
                Id = element.id,
                Time = TimeManager.GetDateTimeFromTimeStamp(Convert.ToInt64(element.time)),
                Volume = Convert.ToDecimal(element.execAmount),
                Side = element.execAmount.Contains("-") ? Side.Sell : Side.Buy

            };

            NewTradesEvent?.Invoke(trade);
        }



        //private void UpdateDepth(string message)
        //{
        //    var responseDepth = JsonConvert.DeserializeObject<BitfinexResponseWebSocketMessage<BitfinexMarketDepth>>(message);

        //    if (responseDepth?.data == null)
        //    {
        //        return;
        //    }

        //    var marketDepth = new MarketDepth
        //    {
        //        SecurityNameCode = responseDepth.data.symbol,
        //        Time = TimeManager.GetDateTimeFromTimeStamp(responseDepth.data.time),
        //        Asks = new List<MarketDepthLevel>(),
        //        Bids = new List<MarketDepthLevel>()
        //    };

        //    foreach (var ask in responseDepth.data.ask)
        //    {
        //        marketDepth.Asks.Add(new MarketDepthLevel
        //        {
        //            Ask = Convert.ToDecimal(ask[1]),
        //            Price = Convert.ToDecimal(ask[0])
        //        });
        //    }

        //    foreach (var bid in responseDepth.data.bids)
        //    {
        //        marketDepth.Bids.Add(new MarketDepthLevel
        //        {
        //            Bid = Convert.ToDecimal(bid[1]),
        //            Price = Convert.ToDecimal(bid[0])
        //        });
        //    }

        //    MarketDepthEvent?.Invoke(marketDepth);
        //}



        public List<BitfinexMarketDepth> GetDepth(string symbol, string precision = "P0")
        {
            try
            {
                var client = new RestClient($"{_getUrl}/book/{symbol}/{precision}");
                var request = new RestRequest("", Method.GET);
                request.AddHeader("accept", "application/json");

                var response = client.Execute(request);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string jsonResponse = response.Content;
                    var marketDepthList = JsonConvert.DeserializeObject<List<List<object>>>(jsonResponse);

                    if (marketDepthList == null)
                    {

                        return null;
                    }
                    List<BitfinexMarketDepth> marketDepths = new List<BitfinexMarketDepth>();

                    for (int i = 0; i < marketDepthList.Count; i++)
                    {
                        var marketDepth = marketDepthList[i];

                        if (marketDepths?.Count >= 3)
                        {
                            BitfinexMarketDepth newMarketDepth = new BitfinexMarketDepth
                            {
                                Price = (marketDepths[0].ToString()),
                                Count = (marketDepths[1].ToString()),
                                Amount = (marketDepths[2].ToString())
                            };

                            marketDepths.Add(newMarketDepth);
                        }
                    }

                    return marketDepths;
                }
                else
                {
                   
                    return null;
                }
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
                return null;
            }

        }
        //void UpdateDepth(List<BitfinexMarketDepth> message)
        //{


        //    //// BitfinexResponseWebSocketMessage<BitfinexMarketDepth> responseDepth = JsonConvert.DeserializeObject<BitfinexResponseWebSocketMessage<BitfinexMarketDepth>>(message);
        //    // var marketDepthList = JsonConvert.DeserializeObject<List<object>>(message);
        //    // if (marketDepthList == null || marketDepthList.Count < 2 || !(marketDepthList[1] is List<object> depthData))
        //    // {
        //    //     return;
        //    // }

        //    var marketDepth = new MarketDepth
        //    {
        //        SecurityNameCode = marketDepthList[0].ToString(),
        //        Asks = new List<MarketDepthLevel>(),
        //        Bids = new List<MarketDepthLevel>()
        //    };

        //    for (int i = 0; i < depthData.Count; i++)
        //    {
        //        if (depthData[i] is List<object> depthItem && depthItem.Count >= 3)
        //        {
        //            var price = Convert.ToDecimal(depthItem[0]);
        //            var count = Convert.ToDecimal(depthItem[1]);
        //            var amount = Convert.ToDecimal(depthItem[2]);

        //            if (amount > 0)
        //            {
        //                var bid = new MarketDepthLevel { Price = price, Bid = count };
        //                marketDepth.Bids.Add(bid);
        //            }
        //            else
        //            {
        //                var ask = new MarketDepthLevel { Price = price, Ask = Math.Abs(amount) };
        //                marketDepth.Asks.Add(ask);
        //            }
        //        }
        //    }

        //    marketDepth.Time = DateTime.UtcNow;

        //    if (marketDepth.Asks.Count == 0 || marketDepth.Bids.Count == 0)
        //    {
        //        return;
        //    }

        //    MarketDepthEvent?.Invoke(marketDepth);


        //}







        private void UpdateMyTrade(string message)
        {
            var response = JsonConvert.DeserializeObject<BitfinexResponseWebSocketMessage<BitfinexMyTrade>>(message);

            if (response?.data == null)
            {
                return;
            }

            var item = response.data;
            var time = Convert.ToInt64(item.time);

            var newTrade = new MyTrade
            {
                Time = TimeManager.GetDateTimeFromTimeStamp(time),
                SecurityNameCode = item.symbol,
                NumberOrderParent = item.orderId,
                Price = Convert.ToDecimal(item.execPrice),
                NumberTrade = item.id,
                NumberPosition = item.orderId,//////////
                Side = item.execAmount.Contains("-") ? Side.Sell : Side.Buy,
                Volume = Convert.ToDecimal(item.execAmount)
            };

            MyTradeEvent?.Invoke(newTrade);
        }
        private void UpdateOrder(string message)
        {
            var response = JsonConvert.DeserializeObject<BitfinexResponseWebSocketMessage<BitfinexOrder>>(message);

            if (response?.data == null)
            {
                return;
            }

            var item = response.data;
            var stateType = response.status;

            if (item.type.Equals("EXCHANGE MARKET")/* &&  item.status == OrderStateType.Activ*/)
            {
                return;
            }

            var newOrder = new Order
            {
                SecurityNameCode = item.symbol,
                TimeCallBack = TimeManager.GetDateTimeFromTimeStamp(Convert.ToInt64(item.time)),
                NumberUser = Convert.ToInt32(item.cid),
                NumberMarket = item.id,
                Side = item.amount.Equals("-") ? Side.Sell : Side.Buy,
                //State =item.status,
                //State = (OrderStateType)Enum.Parse(typeof(OrderStateType), item.status),
                TypeOrder = item.type.Equals("EXCHANGE MARKET") ? OrderPriceType.Market : OrderPriceType.Limit,
                Volume = item.status.Equals("OPEN") ? Convert.ToDecimal(item.amount) : Convert.ToDecimal(item.amountOrig),
                Price = Convert.ToDecimal(item.price),
                ServerType = ServerType.Bitfinex,
                PortfolioNumber = "Bitfinex"
            };

            MyOrderEvent?.Invoke(newOrder);
        }


        //ACTIVE - На исполнении
        //EXECUTED - Исполнена
        //CANCELED - Отменена


        //    if (baseMessage.status == "ACTIVE")
        //    {
        //        order.State = OrderStateType.Activ;
        //    }
        //    else if (baseMessage.status == "EXECUTED")
        //    {
        //        order.State = OrderStateType.Done;
        //    }
        //    else if (baseMessage.status == "PARTIALLY FILLED")
        //    {
        //        order.State = OrderStateType.Patrial;
        //    }

        //    else if (baseMessage.status == "CANCELED")
        //    {
        //        lock (_changePriceOrdersArrayLocker)
        //        {
        //            DateTime now = DateTime.Now;
        //            for (int i = 0; i < _changePriceOrders.Count; i++)
        //            {
        //                if (_changePriceOrders[i].TimeChangePriceOrder.AddSeconds(2) < now)
        //                {
        //                    _changePriceOrders.RemoveAt(i);
        //                    i--;
        //                    continue;
        //                }

        //                //if (_changePriceOrders[i].MarketId == order.NumberMarket)
        //                //{
        //                //    return null;
        //                //}
        //            }
        //        }

        //        if (string.IsNullOrEmpty(baseMessage.amount))
        //        {
        //            order.State = OrderStateType.Cancel;
        //        }
        //        else if (baseMessage.amount == "0")
        //        {
        //            order.State = OrderStateType.Cancel;
        //        }
        //        else
        //        {
        //            try
        //            {
        //                decimal volFilled = baseMessage.amount.ToDecimal();

        //                if (volFilled > 0)
        //                {
        //                    order.State = OrderStateType.Done;
        //                }
        //                else
        //                {
        //                    order.State = OrderStateType.Cancel;
        //                }
        //            }
        //            catch
        //            {
        //                order.State = OrderStateType.Cancel;
        //            }
        //        }
        //    }

        //    return order;
        //}



        #endregion


        /// <summary>
        /// посвящённый торговле. Выставление ордеров, отзыв и т.д
        /// </summary>
        #region  11 Trade

        private RateGate _rateGateSendOrder = new RateGate(1, TimeSpan.FromMilliseconds(300));//уточнить задержку  Таймфрейм в формате отрезка времени. TimeSpan.

        private readonly RateGate _rateGateCancelOrder = new RateGate(1, TimeSpan.FromMilliseconds(300));

        private RateGate _rateGateChangePriceOrder = new RateGate(1, TimeSpan.FromMilliseconds(300));

        //  private RateGate _rateGateSubscrible = new RateGate(1, TimeSpan.FromMilliseconds(50));

        public void SendOrder(Order order)//Submit
        {
            _rateGateSendOrder.WaitToProceed();


            SendNewOrder data = new SendNewOrder();
            data.clientOrderId = order.NumberUser.ToString();
            data.symbol = order.SecurityNameCode;
            data.side = order.Side.ToString().ToUpper();
            data.type = order.TypeOrder.ToString().ToUpper();
            data.price = order.TypeOrder == OrderPriceType.Market ? null : order.Price.ToString().Replace(",", ".");
            data.size = order.Volume.ToString().Replace(",", ".");
            data.amount = order.TypeOrder == OrderPriceType.Market && order.Side == Side.Buy ? (order.Volume * order.Price).ToString().Replace(",", ".") : null; // для BUY MARKET ORDER указывается размер в USDT не меньше 10//////////////////


            string apiPath = "/auth/w/order/submit";
            RestClient client = new RestClient(_postUrl);
            RestRequest request = new RestRequest(apiPath, Method.POST);
            request.AddHeader("accept", "application/json");
            request.AddJsonBody($"{{\"type\":\"{order.TypeOrder}\",\"symbol\":\"{order.SecurityNameCode}\",\"amount\":\"{order.Volume}\",\"price\":\"{order.Price}\"}}");

            string signature = $"{_postUrl}{apiPath}{nonce}";
            string sig = ComputeHmacSha384(_secretKey, signature);
            request.AddHeader("accept", "application/json");
            request.AddHeader("bfx-nonce", nonce);
            request.AddHeader("bfx-apikey", _publicKey);
            request.AddHeader("bfx-signature", sig);

            IRestResponse response = client.Execute(request);

            try

            {

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var jsonResponse = response.Content;
                    BitfinexResponseWebSocketMessage<BitfinexOrder> stateResponse = JsonConvert.DeserializeObject<BitfinexResponseWebSocketMessage<BitfinexOrder>>(jsonResponse);



                    SendLogMessage($"Order num {order.NumberUser} on exchange.", LogMessageType.Trade);

                    order.State = OrderStateType.Activ;
                    order.NumberMarket = stateResponse.data.cid;

                    if (MyOrderEvent != null)
                    {
                        MyOrderEvent(order);
                    }

                }

                else
                {
                    order.State = OrderStateType.Fail;

                    SendLogMessage("Order Fail. Status: "
                        + response.StatusCode + "  " + order.SecurityNameCode, LogMessageType.Error);

                    //if (response.Content != null)
                    //{
                    //    SendLogMessage("Fail reasons: "
                    //  + response.Content, LogMessageType.Error);
                    //}


                }
            }
            catch (Exception exception)
            {
                CreateOrderFail(order);
                SendLogMessage("Order send exception " + exception.ToString(), LogMessageType.Error);
            }
        }



        private void CreateOrderFail(Order order)
        {
            order.State = OrderStateType.Fail;

            if (MyOrderEvent != null)
            {
                MyOrderEvent(order);
            }
        }

        public void CancelAllOrders()
        {

            string apiPath = "auth/w/order/cancel/multi";
            string signature = $"{_postUrl}{apiPath}{nonce}";// {bodyJson}";
            string sig = ComputeHmacSha384(_secretKey, signature);

            //post https://api.bitfinex.com/v2/auth/w/order/cancel/multi
            var client = new RestClient(_postUrl);
            var request = new RestRequest(apiPath);

            request.AddHeader("accept", "application/json");
            request.AddHeader("bfx-nonce", nonce);
            request.AddHeader("bfx-apikey", _publicKey);
            request.AddHeader("bfx-signature", sig);
            //request.AddJsonBody("{\"all\":1}", false); //all\":1 отменить все ордера
            var response = client.Post(request);


        }


        public void CancelAllOrdersToSecurity(Security security)
        {



        }


        public void CancelOrder(Order order)
        {
            _rateGateCancelOrder.WaitToProceed();

            string apiPath = "/auth/w/order/cancel";
            string signature = $"/api/{apiPath}{nonce}";//{body};
            string sig = ComputeHmacSha384(_secretKey, signature);
            var client = new RestClient(_postUrl);
            var request = new RestRequest(apiPath, Method.POST);

            request.AddHeader("accept", "application/json");
            request.AddHeader("bfx-nonce", nonce);
            request.AddHeader("bfx-apikey", _publicKey);
            request.AddHeader("bfx-signature", sig);
            //request.AddJsonBody($"{{\"id\":{order.id}\"}}", false);




            IRestResponse response = client.Execute(request);
            try
            {


                if (response.StatusCode == HttpStatusCode.OK)
                {



                    if (response != null)
                    {
                        SendLogMessage($"Code: {response.StatusCode}"
                            , LogMessageType.Error);
                    }

                    else
                    {
                        CreateOrderFail(order);
                        SendLogMessage($" {response}", LogMessageType.Error);
                    }
                }
            }
            catch (Exception exception)
            {
                CreateOrderFail(order);

                SendLogMessage(exception.ToString(), LogMessageType.Error);


            }

        }

        private string ComputeHmacSha384(string apiSecret, string signature)
        {

            using (var hmac = new HMACSHA384(Encoding.UTF8.GetBytes(apiSecret)))
            {
                byte[] output = hmac.ComputeHash(Encoding.UTF8.GetBytes(signature));
                return BitConverter.ToString(output).Replace("-", "").ToLower();
            }


        }

        public void ChangeOrderPrice(Order order, decimal newPrice)
        {
            try
            {

            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
                SendLogMessage("Conection cannot be open.Bitfinex.Error request", LogMessageType.Error);
                //ServerStatus = ServerConnectStatus.Disconnect;
                //DisconnectEvent();
            }
        }


        #endregion

        /// <summary>
        /// Место расположение HTTP запросов.
        /// </summary>_httpPublicClient
        #region  12 Queries

        public void GetAllActivOrders()
        {
            throw new NotImplementedException();
        }

        public void GetOrderStatus(Order order)
        {
            throw new NotImplementedException();
        }

        public List<Candle> GetCandleDataToSecurity(string security, TimeFrameBuilder timeFrameBuilder, DateTime startTime, DateTime endTime, DateTime actualTime)
        {
            throw new NotImplementedException();
        }

        #endregion
        /// <summary>
        /// Логирование.
        /// </summary>
        #region 13 Log

        public event Action<string, LogMessageType> LogMessageEvent;
        private void SendLogMessage(string message, LogMessageType messageType)
        {
            LogMessageEvent?.Invoke(message, messageType);
        }

        #endregion

    }



    public enum BitfinexSubType
    {
        Trades,
        MarketDepth,
        Porfolio,
        Positions,
        Orders,
        MyTrades
    }
}



//internal class Program
//{
//    private readonly HttpClient client = new HttpClient();

//    private  Main(string[] args)
//    {
//        const string baseUrl = "https://api.bitfinex.com/v2";
//        const string apiKey = "";
//        const string apiSecret = "";

//        string apiPath = "auth/w/order/submit";

//        var body = new
//        {
//            type = "LIMIT",
//            symbol = "tBTCUSD",
//            price = "15",
//            amount = "0.1"
//        };
//        // string bodyJson = JsonSerializer.Serialize(body);
//        //string signature = $"/api/{apiPath}{nonce}{bodyJson}";
//        string nonce = (DateTimeOffset.Now.ToUnixTimeMilliseconds() * 1000).ToString();

//        string bodyJson = JsonConvert.SerializeObject(body);

//        string signature = $"{_postUrl}{apiPath}{nonce}{bodyJson}";
//        string sig = ComputeHmacSha384(_secretKey, signature);


//        var request = new HttpRequestMessage(HttpMethod.Post, $"{_postUrl}/{apiPath}");
//        request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
//        request.Headers.Add("bfx-nonce", nonce);
//        request.Headers.Add("bfx-apikey", _publicKey);
//        request.Headers.Add("bfx-signature", sig);

//        try
//        {
//            HttpResponseMessage response =  client.SendAsync(request);

//            string responseBody = response.Content.ReadAsStringAsync();
//            if (response.IsSuccessStatusCode)
//            {
//                Console.WriteLine(responseBody);
//            }
//            else
//            {
//                Console.WriteLine($"Error Status code {response.StatusCode}: {responseBody}");
//            }
//        }
//        catch (HttpRequestException e)
//        {
//            Console.WriteLine("\nException Caught!");
//            Console.WriteLine("Message :{0} ", e.Message);
//        }
//    }

//    private string ComputeHmacSha384(string apiSecret, string signature)
//    {
//        //string sig;
//        using (var hmac = new HMACSHA384(Encoding.UTF8.GetBytes(apiSecret)))
//        {
//            byte[] output = hmac.ComputeHash(Encoding.UTF8.GetBytes(signature));
//            return  BitConverter.ToString(output).Replace("-", "").ToLower();
//        }

//        //return sig;
//    }
//}


// }





//Чтобы использовать библиотеку RestSharp для отправки REST-запроса, нужно сначала установить ее через NuGet Package Manager. Выполните следующие шаги:

//Откройте консоль диспетчера пакетов NuGet в Visual Studio.
//Выполните команду:
//bash
//Copy code
//Install-Package RestSharp
//Теперь перепишем ваш код, используя RestSharp:

//csharp
//Copy code
//using System;
//using System.Security.Cryptography;
//using System.Text;
//using RestSharp;

//namespace BitfinexCSdotharp
//{
//    internal class Program
//    {
//        static readonly RestClient client = new RestClient("https://api.bitfinex.com");

//        static void Main(string[] args)
//        {
//            const string apiKey = "";
//            const string apiSecret = "";
//            const string apiPath = "v2/auth/w/order/submit";

//            var body = new
//            {
//                type = "LIMIT",
//                symbol = "tBTCUSD",
//                price = "15",
//                amount = "0.1"
//            };
//            string bodyJson = Newtonsoft.Json.JsonConvert.SerializeObject(body);

//            string nonce = (DateTimeOffset.Now.ToUnixTimeMilliseconds() * 1000).ToString();
//            string signature = $"/api/{apiPath}{nonce}{bodyJson}";
//            string sig;
//            using (var hmac = new HMACSHA384(Encoding.UTF8.GetBytes(apiSecret)))
//            {
//                byte[] output = hmac.ComputeHash(Encoding.UTF8.GetBytes(signature));
//                sig = BitConverter.ToString(output).Replace("-", "").ToLower();
//            }

//            var request = new RestRequest(apiPath, Method.POST);
//            request.AddHeader("bfx-nonce", nonce);
//            request.AddHeader("bfx-apikey", apiKey);
//            request.AddHeader("bfx-signature", sig);
//            request.AddJsonBody(body);

//            try
//            {
//                IRestResponse response = client.Execute(request);

//                if (response.StatusCode == HttpStatusCode.OK)
//                {
//                    Console.WriteLine(response.Content);
//                }
//                else
//                {
//                    Console.WriteLine($"Error Status code {response.StatusCode}: {response.Content}");
//                }
//            }
//            catch (Exception exception)
//            {
//                Console.WriteLine("\nException Caught!");
//                Console.WriteLine("Message :{0} ", exception.Message);
//            }
//        }
//    }
//}
//Здесь использована библиотека RestSharp для создания и отправки REST-запроса.




//using System;
//using System.Net.Http;
//using System.Text;
//using System.Text.Json;
//using System.Threading.Tasks;
//using System.Security.Cryptography;

//namespace BitfinexCSdotharp // Note: actual namespace depends on the project name.
//{
//    internal class Program
//    {
//        readonly HttpClient client = new HttpClient();

//        public Main(string[] args)
//        {
//            const string baseUrl = "https://api.bitfinex.com/v2";
//            const string apiKey = "";
//            const string apiSecret = "";

//            string apiPath = "auth/w/order/submit";

//            var body = new
//            {
//                type = "LIMIT",
//                symbol = "tBTCUSD",
//                price = "15",
//                amount = "0.1"
//            };
//            // string bodyJson = JsonSerializer.Serialize(body);
//            //string signature = $"/api/{apiPath}{nonce}{bodyJson}";
//            string nonce = (DateTimeOffset.Now.ToUnixTimeMilliseconds() * 1000).ToString();

//            string bodyJson = JsonConvert.SerializeObject(body);

//            string signature = $"{_postUrl}{apiPath}{nonce}{bodyJson}";

//            string sig = ComputeHmacSha384(apiSecret, signature);

//            var request = new HttpRequestMessage(HttpMethod.Post, $"{_postUrl}/{apiPath}");
//            request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
//            request.Headers.Add("bfx-nonce", nonce);
//            request.Headers.Add("bfx-apikey", apiKey);
//            request.Headers.Add("bfx-signature", sig);

//            try
//            {
//                HttpResponseMessage response = client.SendAsync(request);

//                string responseBody =  response.Content.ReadAsStringAsync();
//                if (response.IsSuccessStatusCode)
//                {
//                    Console.WriteLine(responseBody);
//                }
//                else
//                {
//                    Console.WriteLine($"Error Status code {response.StatusCode}: {responseBody}");
//                }
//            }
//            catch (HttpRequestException exception)
//            {
//                Console.WriteLine("\nException Caught!");
//                Console.WriteLine("Message :{0} ", exception.Message);
//            }
//        }

//        private static string ComputeHmacSha384(string apiSecret, string signature)
//        {
//            string sig;
//            using (var hmac = new HMACSHA384(Encoding.UTF8.GetBytes(apiSecret)))
//            {
//                byte[] output = hmac.ComputeHash(Encoding.UTF8.GetBytes(signature));
//                sig = BitConverter.ToString(output).Replace("-", "").ToLower();
//            }

//            return sig;
//        }
//    }
//}



//const crypto = require('crypto') 
//const WebSocket = require('ws') 

//const apiKey = '' // const apiKey = 'paste key here'
//const apiSecret = '' // const apiSecret = 'paste secret here'

//const nonce = (Date.now() * 1000).toString()
//const authPayload = 'AUTH' + nonce 
//const authSig = crypto.createHmac('sha384', apiSecret).update(authPayload).digest('hex') 

//const payload = {
//  apiKey, //API key
//  authSig, //Authentication Sig
//  nonce,
//  authPayload,
//  event: 'auth'
//}

//const wss = new WebSocket('wss://api.bitfinex.com/ws/2') // Create new Websocket

//wss.on('open', () => wss.send(JSON.stringify(payload)))

//wss.on('message', (msg) => { 
//  console.log(msg)
//})




//Вкладка предоставляет набор событий, через которые торговые роботы могут получать данные. Вы можете подробнее ознакомиться с описанием, нажав на имя интересующего события.

//MyTradeEvent – получена новая сделка по своему счету.
//NewTickEvent – очередная обезличенная сделка.
//ServerTimeChangeEvent – изменилось время сервера.
//CandleFinishedEvent – завершилась свеча.
//CandleUpdateEvent – обновление последней свечки.
//MarketDepthUpdateEvent - обновился стакан заявок.
//BestBidAskChangeEvent – изменилась цена лучшего спроса - предложения.
//PositionClosingSuccesEvent – была успешно закрыта позиция.
//PositionOpeningSuccesEvent – успешное открытие позиции.
//PositionNetVolumeChangeEvent – изменился открытый объем позиции.
//PositionOpeningFailEvent – не удалось открыть позицию.
//PositionClosingFailEvent – не удалось закрыть позицию.
//PositionStopActivateEvent – сработала условная заявка, которая использовалась в качестве стоп лосса.
//PositionProfitActivateEvent - сработала условная заявка, которая использовалась в качестве тейк профита.
//PositionBuyAtStopActivateEvent – сработала условная заявка на покупку.
//PositionSellAtStopActivateEvent – сработала условная заявка на продажу.
//DeleteBotEvent – был запрос на удаление робота, соответственно и вкладки.
//OrderUpdateEvent - обновилось состояние ордера.
//IndicatorUpdateEvent – изменились параметры индикатора.
//SecuritySubscribeEvent – изменился инструмент вкладки.
//FirstTickToDayEvent – первый трейд в 10:00 на московской бирже, не рекомендуется к использованию.

//Connector – публичное свойство. Возвращает ссылку на объект ConnectorCandles. Данный объект инкапсулирует всю логику взаимодействия с биржей.
//TimeFrameBuilder – публичное свойство. Возвращает объект, который хранит настройки способа создания свечных серий данных.
//public event Action<string, TimeFrame, TimeSpan, string, ServerType> ConnectorStartedReconnectEvent;
//используемый таймфрейм в виде перечисления TimeFrame;
//используемый таймфрейм в виде структуры TimeSpan;




// private static readonly HttpClient _httpClient = new HttpClient();



//    string apiPath = "v2/auth/w/order/submit";

//    var body = new
//    {
//        type = "LIMIT",
//        symbol = "tBTCUSD",
//        price = "15",
//        amount = "0.1"
//    };
//    string bodyJson = JsonSerializer.Serialize(body);

//    string nonce = (DateTimeOffset.Now.ToUnixTimeMilliseconds() * 1000).ToString();
//    string signature = $"/api/{apiPath}{nonce}{bodyJson}";



//HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/{apiPath}");
//request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
//request.Headers.Add("bfx-nonce", nonce);
//request.Headers.Add("bfx-apikey", _publicKey);
//request.Headers.Add("bfx-signature", sig);

//try
//{
//    HttpResponseMessage response = _httpClient.SendAsync(request).Result;

//    string responseBody = response.Content.ReadAsStringAsync().Result;

//    if (response.IsSuccessStatusCode)
//    {
//        Console.WriteLine(responseBody);
//    }
//    else
//    {
//        Console.WriteLine($"Error Status code {response.StatusCode}: {responseBody}");
//    }
//}
//catch (Exception exception)
//{
//    SendLogMessage(exception.ToString(), LogMessageType.Error);
//}





//using System;
//using WebSocket4Net;

//class Program
//{
//    static void Main()
//    {
//        // Bitfinex WebSocket API endpoint
//        string endpoint = "wss://api-pub.bitfinex.com/ws/2";

//        // Create a WebSocket client
//        var webSocket = new WebSocket(endpoint);

//        // Event handler for when the connection is opened
//        webSocket.Opened += (sender, e) =>
//        {
//            Console.WriteLine("WebSocket connection opened.");

//            // Create a message to subscribe to the ticker channel for 'tBTCUSD'
//            var subscribeMessage = new
//            {
//                @event = "subscribe",
//                channel = "ticker",
//                symbol = "tBTCUSD"
//            };

//            // Convert the message to JSON and send it
//            webSocket.Send(JsonConvert.SerializeObject(subscribeMessage));

//            Console.WriteLine("Subscribed to the ticker channel for tBTCUSD.");
//        };

//        // Event handler for incoming messages
//        webSocket.MessageReceived += (sender, e) =>
//        {
//            Console.WriteLine($"Received message: {e.Message}");
//        };

//        // Event handler for when the connection is closed
//        webSocket.Closed += (sender, e) =>
//        {
//            Console.WriteLine("WebSocket connection closed.");
//        };

//        // Connect to the WebSocket
//        webSocket.Open();

//        Console.WriteLine("Press any key to exit.");
//        Console.ReadKey();

//        // Close the WebSocket connection when done
//        webSocket.Close();
//    }
//}
//using System;
//using WebSocketSharp;

//class Program
//{
//    static void Main()
//    {
//        using (var ws = new WebSocket("wss://api-pub.bitfinex.com/ws/2"))
//        {
//            // Event handler for incoming messages
//            ws.OnMessage += (sender, e) =>
//            {
//                Console.WriteLine($"Received message: {e.Data}");
//            };

//            // Connect to the WebSocket
//            ws.Connect();

//            // Check if the connection is alive
//            if (ws.IsAlive)
//            {
//                // Create a message to subscribe to the ticker channel for 'tBTCUSD'
//                var subscribeMessage = new
//                {
//                    @event = "subscribe",
//                    channel = "ticker",
//                    symbol = "tBTCUSD"
//                };

//                // Send the subscription message
//                ws.Send(Newtonsoft.Json.JsonConvert.SerializeObject(subscribeMessage));

//                Console.WriteLine("Subscribed to the ticker channel for tBTCUSD. Press any key to exit.");

//                // Wait for user input before exiting
//                Console.ReadKey(true);
//            }
//        }
//    }
//}





//RestClientOptions options = new RestClientOptions("https://api-pub.bitfinex.com/v2");
//IRestClient client = new RestClient(options);
//RestRequest request = new RestRequest("platform/status");
//request.AddHeader("accept", "application/json");
//RestResponse response = client.GetAsync(request);
//response.IsSuccessful
//response.StatusCode


// IRestResponse response = client.Execute(requestRest);


//            //private readonly HttpClient client = new HttpClient();
//            private string _baseUrl = "https://api.bitfinex.com";
//private string apiKey = "";
//private string _secretKey = "";

//private string apiPath = "v2/auth/w/order/submit";

//static void Main(string[] args)
//{
//    Thread mainThread = new Thread(ExecuteBitfinexRequest);
//  ;

//    //Thread mainThread = new Thread(() =>
//    //{
//    //    ExecuteBitfinexRequest();
//    //});

//    mainThread.Start();
//    mainThread.Join();
//}

//static void ExecuteBitfinexRequest()
//{
//    Thread requestThread = new Thread(() =>
//    {
//        const string _baseUrl = "https://api.bitfinex.com";
//        const string apiKey = "";
//        const string _secretKey = "";

//        string apiPath = "v2/auth/w/order/submit";

//        var body = new
//        {
//            type = "LIMIT",
//            symbol = "tBTCUSD",
//            price = "15",
//            amount = "0.1"
//        };
//        string bodyJson = JsonSerializer.Serialize(body);

//        string nonce = (DateTimeOffset.Now.ToUnixTimeMilliseconds() * 1000).ToString();
//        string signature = $"/api/{apiPath}{nonce}{bodyJson}";
//        string sig;
//        using (var hmac = new HMACSHA384(Encoding.UTF8.GetBytes(_secretKey)))
//        {
//            byte[] output = hmac.ComputeHash(Encoding.UTF8.GetBytes(signature));
//            sig = BitConverter.ToString(output).Replace("-", "").ToLower();
//        }

//        var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/{apiPath}");
//        request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
//        request.Headers.Add("bfx-nonce", nonce);
//        request.Headers.Add("bfx-apikey", apiKey);
//        request.Headers.Add("bfx-signature", sig);

//        try
//        {
//            HttpResponseMessage response = client.SendAsync(request).Result;

//            string responseBody = response.Content.ReadAsStringAsync().Result;
//            if (response.IsSuccessStatusCode)
//            {
//                Console.WriteLine(responseBody);
//            }
//            else
//            {
//                Console.WriteLine($"Error Status code {response.StatusCode}: {responseBody}");
//            }
//        }
//        catch (HttpRequestException exception)
//        {
//            Console.WriteLine("\nException Caught!");
//            Console.WriteLine("Message :{0} ", exception.Message);
//        }
//    });

//    requestThread.Start();
//    requestThread.Join();
//}



//using System;
//using System.Security.Cryptography;
//using System.Text;
//using WebSocket4Net;

//class Program
//{
//    static void Main()
//    {
//        string apiKey = ""; // Replace with your API key
//        string apiSecret = ""; // Replace with your API secret

//        long nonce = (DateTimeOffset.Now.ToUnixTimeMilliseconds() * 1000);
//        string authPayload = "AUTH" + nonce;

//        using (var hmac = new HMACSHA384(Encoding.UTF8.GetBytes(apiSecret)))
//        {
//            byte[] hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(authPayload));
//            string authSig = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();

//            var payload = new
//            {
//                apiKey,
//                authSig,
//                nonce,
//                authPayload,
//                @event = "auth"
//            };

//            using (var ws = new WebSocket("wss://api.bitfinex.com/ws/2"))
//            {
//                ws.Opened += (sender, e) => ws.Send(Newtonsoft.Json.JsonConvert.SerializeObject(payload));
//                ws.MessageReceived += (sender, e) => Console.WriteLine(e.Message);

//                ws.Open();

//                Console.ReadKey(true);
//            }
//        }
//    }
//}




//if (end == DateTime.MaxValue)
//{
//    param.Add("", "t" + security + "/hist" + "?"
//                  + "limit=" + count);
//}
//else
//{
//    param.Add("", "t" + security + "/hist" + "?"
//                  + "limit=" + count
//                  + "&end=" + (end - new DateTime(1970, 1, 1)).TotalMilliseconds);
//} //public List<Candle> GetCandleDataToSecurity(Security security, TimeFrameBuilder timeFrameBuilder, DateTime startTime, DateTime endTime, DateTime actualTime)//взять историю свечей за период
//{
//    if (!CheckTime(startTime, endTime, actualTime))
//    {
//        return null;
//    }

//    int tfTotalMinutes = (int)timeFrameBuilder.TimeFrameTimeSpan.TotalMinutes;

//    if (!CheckTf(tfTotalMinutes))
//    {
//        return null;
//    }

//    List<Candle> allCandles = new List<Candle>();

//    string interval = GetInterval(timeFrameBuilder.TimeFrameTimeSpan);

//    int timeRange = 0;
//    if (interval == "1m")
//    {
//        timeRange = tfTotalMinutes * 10000; // для 1m ограничение 10000 свечек
//    }


//    DateTime maxStartTime = DateTime.Now.AddMinutes(-timeRange);
//    DateTime startTimeData = startTime;

//    if (maxStartTime > startTime)
//    {
//        SendLogMessage($"The maximum number of candles has been exceeded.{interval}", LogMessageType.Error);
//        return null;
//    }

//    DateTime partEndTime = startTimeData.AddMinutes(timeFrameBuilder.TimeFrameTimeSpan.TotalMinutes * 720);

//    do
//    {
//        List<Candle> candles = new List<Candle>();

//        long from = TimeManager.GetTimeStampMilliSecondsToDateTime(startTimeData);
//        long to = TimeManager.GetTimeStampMilliSecondsToDateTime(partEndTime);

//        candles = GetCandleHistory(security.Name, interval, 720, from, to);

//        if (candles == null || candles.Count == 0)
//        {
//            break;
//        }

//        Candle last = candles.Last();

//        if (last.TimeStart >= endTime)
//        {
//            for (int i = 0; i < candles.Count; i++)
//            {
//                if (candles[i].TimeStart <= endTime)
//                {
//                    allCandles.Add(candles[i]);
//                }
//            }
//            break;
//        }

//        allCandles.AddRange(candles);

//        startTimeData = partEndTime;
//        partEndTime = startTimeData.AddMinutes(timeFrameBuilder.TimeFrameTimeSpan.TotalMinutes * 720);

//        if (startTimeData >= DateTime.Now)
//        {
//            break;
//        }

//        if (partEndTime > DateTime.Now)
//        {
//            partEndTime = DateTime.Now;
//        }
//    }
//    while (true);

//    return allCandles;
//}

//private bool CheckTime(DateTime startTime, DateTime endTime, DateTime actualTime)
//{
//    if (endTime > DateTime.Now ||
//        startTime >= endTime ||
//        startTime >= DateTime.Now ||
//        actualTime > endTime ||
//        actualTime > DateTime.Now)
//    {
//        return false;
//    }

//    return true;
//}

//private bool CheckTf(int timeFrameMinutes)
//{
//    if (timeFrameMinutes == 1 ||
//        timeFrameMinutes == 5 ||
//        timeFrameMinutes == 15 ||
//        timeFrameMinutes == 30 ||
//        timeFrameMinutes == 60 ||
//        timeFrameMinutes == 180 ||
//        timeFrameMinutes == 360)

//    {
//        return true;
//    }

//    return false;
//}


//кусок кода из алора


//public List<Candle> GetCandleDataToSecurity(Security security, TimeFrameBuilder timeFrameBuilder, DateTime startTime, DateTime endTime,
//   DateTime actualTime)
//{
//    if (startTime != actualTime)
//    {
//        startTime = actualTime;
//    }

//    List<Candle> candles = new List<Candle>();

//    TimeSpan additionTime = TimeSpan.FromMinutes(timeFrameBuilder.TimeFrameTimeSpan.TotalMinutes * 2500);

//    DateTime endTimeReal = startTime.Add(additionTime);

//    while (startTime < endTime)
//    {
//        BitfinexCandlesHistory history = GetHistoryCandle(security, timeFrameBuilder, startTime, endTimeReal);

//        List<Candle> newCandles = ConvertToOsEngineCandles(history);

//        if (newCandles != null &&
//            newCandles.Count > 0)
//        {
//            candles.AddRange(newCandles);
//        }

//        if (string.IsNullOrEmpty(history.hist)
//            && string.IsNullOrEmpty(history.last))
//        {// на случай если указаны очень старые данные, и их там нет
//            startTime = startTime.Add(additionTime);
//            endTimeReal = startTime.Add(additionTime);
//            continue;
//        }

//        if (string.IsNullOrEmpty(history.last))
//        {
//            break;
//        }

//        DateTime realStart = ConvertToDateTimeFromUnixFromSeconds(history.last);

//        startTime = realStart;
//        endTimeReal = realStart.Add(additionTime);
//    }

//    while (candles != null &&
//        candles.Count != 0 &&
//        candles[candles.Count - 1].TimeStart > endTime)
//    {
//        candles.RemoveAt(candles.Count - 1);
//    }

//    return candles;
//}

//private BitfinexCandlesHistory GetHistoryCandle(Security security, TimeFrameBuilder timeFrameBuilder, DateTime startTime, DateTime endTime)
//{

//    try
//    {
//        RestClient client = new RestClient(_getUrl);
//        RestRequest request = new RestRequest("/candles/trade%3A1m%3AtBTCUSD/hist");
//        request.AddHeader("accept", "application/json");
//        IRestResponse response = client.Get(request);

//        if (response.StatusCode == HttpStatusCode.OK)
//        {
//            string content = response.Content;
//            BitfinexCandlesHistory candles = JsonConvert.DeserializeAnonymousType(content, new BitfinexCandlesHistory());
//            return candles;
//        }
//        else
//        {
//            SendLogMessage("Candles request exception. Status: " + response.StatusCode, LogMessageType.Error);
//        }
//    }
//    catch (Exception exception)
//    {
//        SendLogMessage("Candles request exception" + exception.ToString(), LogMessageType.Error);
//    }
//    return null;
//}
//public List<Trade> GetTickDataToSecurity(Security security, DateTime startTime, DateTime endTime, DateTime actualTime)
//{
//    return null;
//}


//public List<Candle> GetLastCandleHistory(Security security, TimeFrameBuilder timeFrameBuilder, int candleCount)//Интерфейс для получения последний свечек по инструменту.Используется для активации серий свечей в боевых торгах
//{
//    string tf = GetInterval(timeFrameBuilder.TimeFrameTimeSpan);
//    return GetCandleHistory(security.Name, tf);
//}

//long from = TimeManager.GetTimeStampMilliSecondsToDateTime(startTimeData);
//        long to = TimeManager.GetTimeStampMilliSecondsToDateTime(partEndTime);


//        candles = GetCandleHistory(security.Name, interval, 720, from, to);

//private string GetInterval(TimeSpan timeFrame)
//{
//    if (timeFrame.Minutes != 0)
//    {
//        return $"{timeFrame.Minutes}m";
//    }
//    else if (timeFrame.Hours != 0)
//    {
//        return $"{timeFrame.Hours}h";
//    }
//    else
//    {
//        return $"{timeFrame.Days}d";
//    }
//}

//private List<Candle> GetCandleHistory(DateTime fromTimeStamp, DateTime toTimeStamp, string nameSec, string tameFrame/*,string candle, string section = "hist", long limit = 10000*/)
//{
//    _rateGate.WaitToProceed();

//    try
//    {//https://api-pub.bitfinex.com/v2/candles/trade:1m:tBTCUSD/hist
//        //get  https://api-pub.bitfinex.com/v2/candles/{candle}/{section} по умолчанию предоставляет последние 100 свечей

//        string apiPath = "/candles/{candle}/{section}";
//        string timeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();



//        if (fromTimeStamp < toTimeStamp )
//        {

//            candle= "trade:" + tameFrame + ":t" + nameSec + "/hist" + "?"
//                                     + "limit=" + limit
//                                     //+ "&start=" + (start - new DateTime(1970, 1, 1)).TotalMilliseconds);
//                                     + "&end=" +  (toTimeStamp - new DateTime(1970, 1, 1)).TotalMilliseconds;
//        }


//        else
//        {
//            candle = "trade:" + tameFrame + ":t" + nameSec + "/hist";
//        }


//        RestClient client = new RestClient(_getUrl);
//        RestRequest request = new RestRequest(apiPath);
//        request.AddHeader("accept", "application/json");
//        IRestResponse response = client.Get(request);

//        if (response.StatusCode == HttpStatusCode.OK)
//        {
//            string content = response.Content;
//            BitfinexCandlesHistory candles = JsonConvert.DeserializeAnonymousType(content, new BitfinexCandlesHistory());
//            return candles;

//        }

//        else
//        {
//            SendLogMessage("Candles request exception. Status: " + response.StatusCode, LogMessageType.Error);
//        }
//    }
//    catch (Exception exception)
//    {
//        SendLogMessage("Candles request exception" + exception.ToString(), LogMessageType.Error);
//    }

//    return null;
//}


//private List<Candle> ConvertToOsEngineCandles(BitfinexCandlesHistory candles)
//{
//    try
//    {


//        List<Candle> result = new List<Candle>();

//        for (int i = 0; i < candles.history.Count; i++)
//        {
//            BitfinexCandle curCandle = candles.history[i];//&&&&&&&&&&&&&&&&

//            Candle newCandle = new Candle();
//            newCandle.Open = curCandle.open.ToDecimal();
//            newCandle.High = curCandle.high.ToDecimal();
//            newCandle.Low = curCandle.low.ToDecimal();
//            newCandle.Close = curCandle.close.ToDecimal();
//            newCandle.Volume = curCandle.volume.ToDecimal();
//            newCandle.TimeStart = ConvertToDateTimeFromUnixFromSeconds(curCandle.time);

//            result.Add(newCandle);
//        }

//        // result.Reverse();

//        return result;
//    }
//    catch (Exception exception)
//    { 
//     SendLogMessage(exception.ToString(), LogMessageType.Error); 
//    }  
//}


//using Newtonsoft.Json;
//using RestSharp;

//// Ваш код для отправки запроса

//var options = new RestClientOptions("https://api-pub.bitfinex.com/v2/tickers?symbols=ALL");
//var client = new RestClient(options);
//var request = new RestRequest("");
//request.AddHeader("accept", "application/json");
//var response = await client.GetAsync(request);

//// Десериализация JSON в C# объект
//var jsonResponse = response.Content;
//var result = JsonConvert.DeserializeObject<YourObjectType>(jsonResponse);

//// Вывод результатов
//Console.WriteLine("{0}", jsonResponse);
//Console.WriteLine("Deserialized Object: {0}", result);