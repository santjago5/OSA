
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
using Candle = OsEngine.Entity.Candle;
using Method = RestSharp.Method;
using SuperSocket.ClientEngine;
using Trade = OsEngine.Entity.Trade;
using MarketDepth = OsEngine.Entity.MarketDepth;
using System.Text.Json;
using System.Globalization;
using Timer = System.Timers.Timer;
using System.Linq;
using Side = OsEngine.Entity.Side;
using WebSocketState = WebSocket4Net.WebSocketState;
using ErrorEventArgs = SuperSocket.ClientEngine.ErrorEventArgs;
using WebSocket = WebSocket4Net.WebSocket;
using JsonSerializer = System.Text.Json.JsonSerializer;
using static Grpc.Core.Metadata;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Tab;
using WebSocketSharp;










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

            Thread threadForPublicMessages = new Thread(PublicMessageReader)
            {
                IsBackground = true,
                Name = "PublicMessageReaderBitfinex"
            };
            threadForPublicMessages.Start();

            Thread threadForPrivateMessages = new Thread(PrivateMessageReader)
            {
                IsBackground = true,
                Name = "PrivateMessageReaderBitfinex"
            };
            threadForPrivateMessages.Start();

        }

        public DateTime ServerTime { get; set; }


        public void Connect()
        {
            try
            {
                _securities.Clear();
                _portfolios.Clear();


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
                string apiPath = "v2/platform/status";

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

        private string _getUrl = "https://api-pub.bitfinex.com";

        private string _postUrl = "https://api.bitfinex.com";

        private HttpClient _httpClient = new HttpClient();

        string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString(); //берет время сервера без учета локального

        #endregion



        /// <summary>
        /// Запрос доступных для подключения бумаг у подключения. 
        /// </summary>
        #region 3 Securities
        RateGate _rateGateGetsecurity = new RateGate(280, TimeSpan.FromMilliseconds(60));

        private List<Security> _securities = new List<Security>();

        public event Action<List<Security>> SecurityEvent;

        public void GetSecurities()
        {
            _rateGateGetsecurity.WaitToProceed();

            try
            {
                RestClient client = new RestClient(_getUrl);
                RestRequest request = new RestRequest("v2/tickers?symbols=ALL");
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

                    //for (int i = 0; i < 3; i++)
                    for (int i = 0; i < securityList.Count; i++)
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

                    newSecurity.Name = securityData.Symbol;
                    newSecurity.NameFull = securityData.Symbol;
                    newSecurity.NameClass = "Spot";
                    newSecurity.NameId = Convert.ToString(securityData.Symbol);
                    newSecurity.Exchange = ServerType.Bitfinex.ToString();

                    newSecurity.Lot = 1;

                    newSecurity.SecurityType = SecurityType.CurrencyPair;

                    newSecurity.State = SecurityStateType.Activ;

                    newSecurity.PriceStep = newSecurity.Decimals.GetValueByDecimals();

                    newSecurity.PriceStepCost = newSecurity.PriceStep;

                    //newSecurity.Decimals = Convert.ToInt32(securityData.LastPrice);////////////////


                    // newSecurity.DecimalsVolume = newSecurity.Decimals;


                    _securities.Add(newSecurity);////////////////////////;




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
                                Balance = (walletData[2]).ToString().ToDecimal(),////??????????????????
                                UnsettledInterest = (walletData[3]).ToString().ToDecimal(),
                                AvailableBalance = (walletData[4]).ToString().ToDecimal(),
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
        //public void GetPortfolios()
        //{
        //    if (string.IsNullOrEmpty(_portfolioType) == false)
        //    {
        //        GetCurrentPortfolio(_portfolioType, "SPOT");
        //    }

        //    ActivatePortfolioSocket();
        //}


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

            if (endTime > DateTime.UtcNow)
            {
                endTime = DateTime.UtcNow;
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



        private List<Candle> CreateQueryCandles(string nameSec, string tf, DateTime timeFrom, DateTime timeTo)//////////TimeSpan interval
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

            //var timeStampStart = timeFrom - yearBegin;
            //var startTimeMilliseconds = timeStampStart.TotalMilliseconds;
            // string startTime = Convert.ToInt64(startTimeMilliseconds).ToString();

            //var timeStampEnd = timeTo - yearBegin;
            //var endTimeMilliseconds = timeStampEnd.TotalMilliseconds;
            //string endTime = Convert.ToInt64(endTimeMilliseconds).ToString();


            string startTime = Convert.ToInt64((timeFrom - yearBegin).TotalMilliseconds).ToString();
            string endTime = Convert.ToInt64((timeTo - yearBegin).TotalMilliseconds).ToString();

            // string section = timeFrom !=DateTime.Today ?"hist":"last";////////////



            RestClient client = new RestClient($"https://api-pub.bitfinex.com/v2/candles/trade:{1m}:{nameSec}/hist?start={startTime}&end={endTime}");//////TF 

            var request = new RestRequest("", Method.GET);//поменяла строки

            //var request = new RestRequest("");

            request.AddHeader("accept", "application/json");

            var response = client.Get(request);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                string jsonResponse = response.Content;

                // List<List<BitfinexCandle>> candles = JsonConvert.DeserializeObject<List<List<BitfinexCandle>>>(jsonResponse);//////////////////////////////
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
            try
            {
                for (int i = 0; i < candleList.Count; i++)
                {
                    var candle = candleList[i];
                    try
                    {

                        if (string.IsNullOrEmpty(candle.Time) || string.IsNullOrEmpty(candle.Open) ||
                            string.IsNullOrEmpty(candle.Close) || string.IsNullOrEmpty(candle.High) ||
                            string.IsNullOrEmpty(candle.Low) || string.IsNullOrEmpty(candle.Volume))
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
                            Open = (candle.Open).ToString().ToDecimal(),
                            Close = (candle.Close).ToString().ToDecimal(),
                            High = (candle.High).ToString().ToDecimal(),
                            Low = (candle.Low).ToString().ToDecimal(),
                            Volume = (candle.Volume).ToString().ToDecimal()
                        };

                        candles.Add(newCandle);

                    }
                    catch (FormatException ex)
                    {
                        SendLogMessage($"Format exception: {ex.Message}", LogMessageType.Error);
                    }

                }
                //  candles.Reverse();
                return candles;
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
                return null;
            }
        }


        #endregion


        /// <summary>
        /// Создание вёбсокет соединения. 
        /// </summary>
        #region  6 WebSocket creation



        private WebSocket _webSocketPublic;
        private WebSocket _webSocketPrivate;



        private readonly string _webSocketPublicUrl = "wss://api-pub.bitfinex.com/ws/2";
        private readonly string _webSocketPrivateUrl = "wss://api.bitfinex.com/ws/2";
        private Timer _pingTimer;
        // Set up a timer to send pings every 15 seconds


        private void CreateWebSocketConnection()
        {
            try
            {




                if (_webSocketPublic != null)
                {
                    return;
                }

                //_socketPublicIsActive = false;////////////
                //  _socketPrivateIsActive = false;////////////////


                _webSocketPublic = new WebSocket(_webSocketPublicUrl)
                {
                    EnableAutoSendPing = true,
                    AutoSendPingInterval = 15
                };

                _webSocketPublic.Opened += WebSocketPublic_Opened;
                _webSocketPublic.Closed += WebSocketPublic_Closed;
                _webSocketPublic.MessageReceived += WebSocketPublic_MessageReceived;
                _webSocketPublic.Error += WebSocketPublic_Error;

                _webSocketPublic.Open();


                _webSocketPrivate = new WebSocket(_webSocketPrivateUrl)
                {
                    EnableAutoSendPing = true,
                    AutoSendPingInterval = 15
                };

                _webSocketPrivate.Opened += WebSocketPrivate_Opened;
                _webSocketPrivate.Closed += WebSocketPrivate_Closed;
                _webSocketPrivate.MessageReceived += WebSocketPrivate_MessageReceived;
                _webSocketPrivate.Error += WebSocketPrivate_Error;

                _webSocketPrivate.Open();


                //_pingTimer = new Timer(25000); // 25 seconds
                //_pingTimer.Elapsed += SendPing;
                //_pingTimer.AutoReset = true;



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

        public event Action<MarketDepth> MarketDepthEvent;



        #endregion

        //private List<MarketDepth> _depths;

        /// <summary>
        /// Обработка входящих сообщений от вёбсокета. И что важно в данном конкретном случае, Closed и Opened методы обязательно должны находиться здесь,
        /// </summary>
        #region  7 WebSocket events

        private bool _socketPublicIsActive;

        private bool _socketPrivateIsActive;

        private void WebSocketPublic_Opened(object sender, EventArgs e)
        {


            _socketPublicIsActive = true;//отвечает за соединение

            CheckActivationSockets();
            //  _pingTimer.Start();////////////////////////
            SendLogMessage("Connection to public data is Open", LogMessageType.System);

        }


        private void WebSocketPublic_Closed(object sender, EventArgs e)
        {
            try
            {
                // _pingTimer.Stop();

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

        private string _currentSymbol;

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



        public long tradeChannelId;
        public long bookChannelId;






        // Снимок(snapshot) : Структура данных содержит массив массивов, где каждый внутренний массив представляет собой запись в стакане(book entry).
        //Обновление(update) : Структура данных содержит только один массив, представляющий одну запись в стакане(book entry).

        MarketDepth marketDepth = new MarketDepth();

        // Инициализируем списки для ask и bid уровней
        List<MarketDepthLevel> asks = new List<MarketDepthLevel>();
        List<MarketDepthLevel> bids = new List<MarketDepthLevel>();


        public void ProcessOrderBookResponse(string jsonResponse, int chanelId, string symbol)
        {
            if (chanelId == tradeChannelId)
            {
                return;
            }

            JsonDocument document = JsonDocument.Parse(jsonResponse);
            JsonElement root = document.RootElement;

            int channelId = root[0].GetInt32();
            JsonElement data = root[1];


            if (chanelId != bookChannelId)/////////////////////////////
            {

                return;
            }



            if (root.ValueKind == JsonValueKind.Object)
            {
                var eventType = root.GetProperty("event").GetString();
                if (eventType == "info" || eventType == "auth" || eventType == "hb")
                {
                    return;
                }
            }

            // Проверяем, является ли data массивом массивов (snapshot)
            if (data.ValueKind == JsonValueKind.Array && data[0].ValueKind == JsonValueKind.Array)
            {
                // Это snapshot
                var snapshot = new BitfinexBookSnapshot
                {
                    ChannelId = channelId.ToString()
                };

                marketDepth.SecurityNameCode = symbol;
                // Инициализируем списки для ask и bid уровней
                List<MarketDepthLevel> asks = new List<MarketDepthLevel>();
                List<MarketDepthLevel> bids = new List<MarketDepthLevel>();

             

                // Очистка старых данных и добавление новых уровней
                bids.Clear();
                asks.Clear();

                // Используем цикл for для итерации по элементам массива
                for (int i = 0; i < data.GetArrayLength(); i++)
                {
                    var entryElement = data[i];
                    var price = entryElement[0].GetDecimal();
                    var count = entryElement[1].GetInt32();
                    var amount = entryElement[2].GetDecimal();

                    if (amount > 0)
                    {
                        // Это бид
                        var bidLevel = new MarketDepthLevel
                        {
                            Price = price,
                            Bid = amount
                        };
                        //marketDepth.Bids.Add(bidLevel);
                        bids.Add(bidLevel);
                    }
                    else
                    {
                        // Это аск
                        var askLevel = new MarketDepthLevel
                        {
                            Price = price,
                            Ask = Math.Abs(amount)
                        };
                        //marketDepth.Asks.Add(askLevel);
                        asks.Add(askLevel);
                    }
                }

                marketDepth.Time = DateTime.UtcNow;

                //// Присваиваем отсортированные списки ask и bid уровней ордербуку
                marketDepth.Asks = asks;
                marketDepth.Bids = bids;
                //marketDepth.Time = TimeManager.GetDateTimeFromTimeStamp(Convert.ToInt64());

            }
            else if (data.ValueKind == JsonValueKind.Array)
            {
                // Это update
                var price = data[0].GetDecimal();
                var count = data[1].GetInt32();
                var amount = data[2].GetDecimal();

                if (count == 0)
                {
                    // Удаление уровня из бидов
                    if (amount > 0)
                    {
                        for (int i = 0; i < marketDepth.Bids.Count; i++)
                       
                        {
                            if (marketDepth.Bids[i].Price == price)
                            {
                                 marketDepth.Bids.RemoveAt(i);
                               
                                break;
                            }
                        }
                    }
                    else// Удаление уровня из асков
                    {
                        for (int i = 0; i < marketDepth.Asks.Count; i++)
                  
                        {
                            
                                if (marketDepth.Asks[i].Price == price)

                                {
                                    marketDepth.Asks.RemoveAt(i);
                              
                                    break;
                                }
                            
                        }
                    }
                }
                else
                {
                    // Обновление или добавление уровня
                    var level = new MarketDepthLevel
                    {
                        Price = price,
                        Bid = amount > 0 ? amount : 0,
                        Ask = amount < 0 ? Math.Abs(amount) : 0
                    };

                    if (amount > 0)
                    {
                        bool updated = false;
                        for (int i = 0; i < marketDepth.Bids.Count; i++)
                        {

                            if (marketDepth.Bids[i].Price == price)
                            {
                                marketDepth.Bids[i] = level; // Обновление уровня

                                updated = true;
                                break;
                            }
                        }
                        if (!updated)
                        {
                             marketDepth.Bids.Add(level); // Добавление нового уровня
                        
                        }
                    }
                    else
                    {
                        bool updated = false;
                        for (int i = 0; i < marketDepth.Asks.Count; i++)
                        {
                             if (marketDepth.Asks[i].Price == price)
                       
                            {
                                 marketDepth.Asks[i] = level; // Обновление уровня

                               
                                updated = true;
                                break;
                            }
                        }
                        if (!updated)
                        {
                             marketDepth.Asks.Add(level); /// Добавление нового уровня
                            
                        }
                    }
                }

                    marketDepth.Time = DateTime.UtcNow;
                // Сортировка ask по возрастанию (сначала наименьшие цены)
                asks.Sort((x, y) => x.Price.CompareTo(y.Price));

                // Сортировка bid по убыванию (сначала наибольшие цены)
                bids.Sort((x, y) => y.Price.CompareTo(x.Price));


                if (marketDepth.Asks.Count == 0 ||
                    marketDepth.Bids.Count == 0)
                {
                    return;
                }

            } 
                  MarketDepthEvent(marketDepth);
        }


        



        //    // Извлечение корневого элемента JSON

        //    int channelId = root[0].GetInt32();  // CHANNEL_ID
        //    var data = root[1];  // Данные: это может быть массив массивов (snapshot) или один массив (update)

        //    // Проверка, является ли data массивом массивов (snapshot)
        //    if (data.ValueKind == JsonValueKind.Array && data[0].ValueKind == JsonValueKind.Array)
        //    {
        //        // Это snapshot
        //        var snapshot = new BitfinexBookSnapshot
        //        {
        //            ChannelId = channelId.ToString(),
        //            BookEntries = new List<BitfinexBookEntry>()
        //        };

        //        // Заполнение данными snapshot
        //        for (int i = 0; i < data.GetArrayLength(); i++)
        //        {
        //            var entry = data[i];
        //            snapshot.BookEntries.Add(new BitfinexBookEntry
        //            {
        //                Price = entry[0].ToString(),
        //                Count = entry[1].ToString(),
        //                Amount = entry[2].ToString()
        //            });
        //        }

        //    //HandleSnapshot(snapshot);

        //     ProcessBitfinexOrderBook(snapshot);


        //}
        //    else if (data.ValueKind == JsonValueKind.Array)
        //    {
        //        // Это update
        //        var update = new BitfinexBookUpdate
        //        {
        //            ChannelId = channelId.ToString(),
        //            BookEntry = new BitfinexBookEntry
        //            {
        //                Price = data[0].ToString(),
        //                Count = data[1].ToString(),
        //                Amount = data[2].ToString()
        //            }
        //        };

        //       // HandleUpdate(update);//
        //    ProcessBitfinexOrderBookUpdate(update);
        //    }
        //}

        //private void HandleSnapshot(BitfinexBookSnapshot snapshot)
        //{
        //    // Обработка данных snapshot
        //    Console.WriteLine($"Snapshot received for channel {snapshot.ChannelId} with {snapshot.BookEntries.Count} entries.");

        //    // Например, здесь можно обновить внутреннее состояние ордербука на основе данных snapshot
        //}

        //private void HandleUpdate(BitfinexBookUpdate update)
        //{
        //    // Обработка данных обновления
        //    var bookEntry = update.BookEntry;

        //    Console.WriteLine($"Update received for channel {update.ChannelId}: Price={bookEntry.Price}, Count={bookEntry.Count}, Amount={bookEntry.Amount}.");

        //    // Например, здесь можно обновить внутреннее состояние ордербука на основе данных обновления
        //}

        //public void ProcessBitfinexOrderBook(BitfinexBookSnapshot snapshot )
        //{
        //    // Создаем объект для хранения данных ордербука
        //    MarketDepth marketDepth = new MarketDepth();

        //    // Инициализируем списки для ask и bid уровней
        //    List<MarketDepthLevel> asks = new List<MarketDepthLevel>();
        //    List<MarketDepthLevel> bids = new List<MarketDepthLevel>();

        //    // Устанавливаем имя инструмента, которое связано с данным каналом
        //    marketDepth.SecurityNameCode = snapshot.ChannelId;  // предполагается, что Symbol был добавлен в класс BitfinexBookSnapshot

        //    // Проходимся по ask уровням и добавляем их в список
        //    for (int i = 0; i < snapshot.BookEntries.Count; i++)
        //    {
        //        var entry = snapshot.BookEntries[i];
        //        if ((entry.Amount).ToDecimal() < 0)
        //        {
        //            // Если Amount < 0, это ask уровень
        //            MarketDepthLevel newMDLevel = new MarketDepthLevel
        //            {
        //                Ask = Math.Abs((entry.Amount).ToDecimal()), // Берем абсолютное значение для ask
        //                Price = (entry.Price).ToDecimal()
        //            };
        //            asks.Add(newMDLevel);
        //        }
        //    }

        //    // Проходимся по bid уровням и добавляем их в список
        //    for (int i = 0; i < snapshot.BookEntries.Count; i++)
        //    {
        //        var entry = snapshot.BookEntries[i];
        //        if ((entry.Amount).ToDecimal() > 0)
        //        {
        //            // Если Amount > 0, это bid уровень
        //            MarketDepthLevel newMDLevel = new MarketDepthLevel
        //            {
        //                Bid = entry.Amount.ToDecimal(),  // Используем прямое значение для bid
        //                Price = (entry.Price).ToDecimal()
        //            };
        //            bids.Add(newMDLevel);
        //        }
        //    }

        //    // Присваиваем отсортированные списки ask и bid уровней ордербуку
        //    marketDepth.Asks = asks;
        //    marketDepth.Bids = bids;
        //    marketDepth.Time = DateTime.UtcNow;  // Здесь может быть установлено текущее время или время из данных

        //    //// Проверка, что оба списка не пусты
        //    //if (marketDepth.Asks.Count == 0 || marketDepth.Bids.Count == 0)
        //    //{
        //    //    return;
        //    //}

        //    // Вызов события для обработки обновленного ордербука
        //    MarketDepthEvent(marketDepth);
        //}

        //public void ProcessBitfinexOrderBookUpdate(BitfinexBookUpdate update)
        //{

        //    // Создаем объект для хранения данных ордербука
        //    MarketDepth marketDepth = new MarketDepth();
        //    // Устанавливаем имя инструмента, которое связано с данным каналом
        //    marketDepth.SecurityNameCode = update.ChannelId;  // предполагается, что Symbol был добавлен в класс BitfinexBookSnapshot

        //    // Инициализируем списки для ask и bid уровней
        //    List<MarketDepthLevel> asks = new List<MarketDepthLevel>();
        //    List<MarketDepthLevel> bids = new List<MarketDepthLevel>();


        //    // Обработка одного обновления(update)
        //    var entry = update.BookEntry;

        //    if (entry.Count == "0")
        //    {
        //        // Если количество ордеров на уровне 0, нужно удалить этот уровень
        //        if (entry.Amount.Contains("-"))
        //        {
        //            depthAsks.RemoveAll(ask => ask.Price == entry.Price);
        //        }
        //        else
        //        {
        //            depthBids.RemoveAll(bid => bid.Price == entry.Price);
        //        }
        //    }


        //    if ((entry.Amount).ToDecimal() < 0)
        //    {
        //        // Это ask уровень
        //        MarketDepthLevel newMDLevel = new MarketDepthLevel
        //        {
        //            Ask = Math.Abs((entry.Amount).ToDecimal()), // Берем абсолютное значение для ask
        //            Price = (entry.Price).ToDecimal()
        //        };
        //        asks.Add(newMDLevel);
        //    }

        //    else if ((entry.Amount).ToDecimal() > 0)
        //    {
        //        // Это bid уровень
        //        MarketDepthLevel newMDLevel = new MarketDepthLevel
        //        {
        //            Bid = entry.Amount.ToDecimal(),  // Используем прямое значение для bid
        //            Price = (entry.Price).ToDecimal()
        //        };
        //        bids.Add(newMDLevel);
        //    }

        //        // Присваиваем отсортированные списки ask и bid уровней ордербуку
        //        marketDepth.Asks = asks;
        //        marketDepth.Bids = bids;

        //        marketDepth.Time = DateTime.UtcNow;  // Здесь может быть установлено текущее время или время из данных

        //        //// Проверка, что оба списка не пусты
        //        //if (marketDepth.Asks.Count == 0 || marketDepth.Bids.Count == 0)
        //        //{
        //        //    return;
        //        //}

        //        // Вызов события для обработки обновленного ордербука
        //        MarketDepthEvent(marketDepth);

        //}







        ////    private void ProcessSnapshot(List<BitfinexBookEntry> bookEntries)
        //    {
        //        // Обновление внутреннего состояния стакана
        //        depthBids.Clear(); // Очистка текущих записей о bid'ах
        //        depthAsks.Clear(); // Очистка текущих записей о ask'ах

        //        // Перебор всех записей в снимке с использованием цикла for
        //        for (int i = 0; i < bookEntries.Count; i++)
        //        {
        //            var entry = bookEntries[i]; // Извлечение текущей записи

        //            // Если количество больше 0, это bid
        //            //
        //            // if (Convert.ToDecimal(entry.Amount) > 0)
        //            if (entry.Amount.Contains("-"))
        //            {
        //                // Добавление новой записи в список ask'ов
        //                depthAsks.Add(new BitfinexBookEntry
        //                {
        //                    Price = entry.Price, // Преобразование цены в строку
        //                    Count = entry.Count, // Преобразование количества в строку
        //                    Amount = entry.Amount // Преобразование объема в строку
        //                });
        //            }
        //            else // В противном случае, это bid
        //            {
        //                // Добавление новой записи в список bid'ов
        //                depthBids.Add(new BitfinexBookEntry
        //                {
        //                    Price = entry.Price, // Преобразование цены 
        //                    Count = entry.Count, // Преобразование количества 
        //                    Amount = entry.Amount // Преобразование объема 
        //                });
        //            }
        //        }

        //        // Вызов обновления стакана
        //        UpdateOrderBook(); // Вызов метода для обновления стакана
        //    }
        //    private void ProcessUpdate(BitfinexBookEntry entry)////не понятно что тут
        //    {
        //        // Логика обновления стакана, аналогичная приведенной выше
        //        var bookEntry = new BitfinexBookEntry
        //        {
        //            Price = entry.Price.ToString(),
        //            Count = entry.Count.ToString(),
        //            Amount = entry.Amount.ToString()
        //        };

        //        // Обновление или удаление записи в стакане
        //        if (Convert.ToUInt32(entry.Count) == 0)
        //        {
        //            // if (Convert.ToDecimal(entry.Amount )> 0)

        //            if (entry.Amount.Contains("-"))
        //            {
        //                depthAsks.RemoveAll(ask => ask.Price == bookEntry.Price);
        //            }
        //            else
        //            {
        //                depthBids.RemoveAll(bid => bid.Price == bookEntry.Price);
        //            }
        //        }
        //        else

        //        {

        //            // if (Convert.ToUInt32(entry.Amount )> 0)
        //            if (entry.Amount.Contains("-"))
        //            {
        //                bool updated = false;
        //                for (int i = 0; i < depthAsks.Count; i++)
        //                {
        //                    if (depthAsks[i].Price == bookEntry.Price)
        //                    {
        //                        depthAsks[i] = bookEntry;
        //                        updated = true;
        //                        break;
        //                    }
        //                }
        //                if (!updated)
        //                {
        //                    depthAsks.Add(bookEntry);
        //                }
        //            }
        //            else
        //            {
        //                bool updated = false;
        //                for (int i = 0; i < depthBids.Count; i++)
        //                {
        //                    if (depthBids[i].Price == bookEntry.Price)
        //                    {
        //                        depthBids[i] = bookEntry;
        //                        updated = true;
        //                        break;
        //                    }
        //                }
        //                if (!updated)
        //                {
        //                    depthBids.Add(bookEntry);
        //                }
        //            }
        //        }

        //        // Вызов обновления стакана
        //        //UpdateOrderBook();
        //        UpdateOrderBook( );
        //    }

        //    //   private void UpdateOrderBook()
        //    private void UpdateOrderBook(/*string message*/)
        //    {
        //        // Десериализация сообщения в список объектов
        //       // var responseDepth = JsonConvert.DeserializeObject<List<object>>(message);
        //        // Создание нового объекта MarketDepth для обновления стакана

        //        MarketDepth newMarketDepth = new MarketDepth
        //        {
        //            SecurityNameCode = _currentSymbol,
        //            Time = DateTime.UtcNow,
        //            Asks = new List<MarketDepthLevel>(),
        //            Bids = new List<MarketDepthLevel>()
        //        };

        //        // Перебор всех bid уровней и добавление их в новый стакан
        //        for (int i = 0; i < depthBids.Count; i++)
        //        {
        //            var bid = depthBids[i];

        //            try
        //            {
        //                decimal bidPrice = Convert.ToDecimal(bid.Price, CultureInfo.InvariantCulture);
        //                decimal bidAmount = Convert.ToDecimal(bid.Amount, CultureInfo.InvariantCulture);

        //                newMarketDepth.Bids.Add(new MarketDepthLevel
        //                {
        //                    Price = bidPrice,
        //                    Bid = bidAmount
        //                });
        //            }
        //            catch (FormatException)
        //            {
        //                // Логирование или обработка ошибки при неверном формате
        //                Console.WriteLine($"Invalid bid format: Price={bid.Price}, Amount={bid.Amount}");
        //            }
        //            catch (Exception ex)
        //            {
        //                // Логирование или обработка других исключений
        //                Console.WriteLine($"Unexpected error: {ex.Message}");
        //            }
        //        }

        //        // Перебор всех ask уровней и добавление их в новый стакан
        //        for (int i = 0; i < depthAsks.Count; i++)
        //        {
        //            var ask = depthAsks[i];
        //            try
        //            {
        //                decimal askPrice = Convert.ToDecimal(ask.Price, CultureInfo.InvariantCulture); // или просто Convert.toDecimal
        //                decimal askAmount = Convert.ToDecimal(ask.Amount, CultureInfo.InvariantCulture); 

        //                newMarketDepth.Asks.Add(new MarketDepthLevel
        //                {
        //                    Price = askPrice,
        //                    // Ask = Math.Abs(askAmount)
        //                    Ask = askAmount
        //                });
        //            }
        //            catch (Exception exception)
        //            {
        //                SendLogMessage(exception.ToString(), LogMessageType.Error);
        //            }
        //        }

        //        // Вызов события обновления стакана
        //        MarketDepthEvent?.Invoke(newMarketDepth);
        //    }



        private void WebSocketPrivate_Opened(object sender, EventArgs e)
        {

            GenerateAuthenticate();
            _socketPrivateIsActive = true;//отвечает за соединение
            CheckActivationSockets();
            SendLogMessage("Connection to private data is Open", LogMessageType.System);

        }

        private void GenerateAuthenticate()
        {
            //string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();

            string payload = $"AUTH{nonce}";
            string signature = ComputeHmacSha384(payload, _secretKey);

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

            _webSocketPrivate.Send(authMessageJson);

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
                    _webSocketPublic.State == WebSocketState.Open && _webSocketPrivate.State == WebSocketState.Open) //добавлена проверка
                {
                    ServerStatus = ServerConnectStatus.Connect;
                    ConnectEvent();
                }

                SendLogMessage("All sockets activated.", LogMessageType.System);

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

        private void WebSocketPrivate_Error(object sender, ErrorEventArgs e)
        {
            try
            {


                if (e.Exception != null)
                {
                    SendLogMessage(e.Exception.ToString(), LogMessageType.Error);
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

                if (e == null || string.IsNullOrEmpty(e.Message))
                {
                    return;
                }

                if (e.Message.Contains("hb"))
                {
                    return;
                }

                if (WebSocketPrivateMessage == null)
                {
                    return;
                }

                WebSocketPrivateMessage.Enqueue(e.Message); // Добавление полученного сообщения в очередь

            }

            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }


        /// <summary>
        /// Проверка вёбсокета на работоспособность путём отправки ему пингов.
        /// </summary>
        //private void SendPing(object sender, EventArgs e)// ElapsedEventArgs e)
        //{
        //    // Проверяем, инициализирован ли _webSocketPublic и открыт ли он
        //    if (_webSocketPublic != null && _webSocketPublic.State == WebSocketState.Open)
        //    {
        //        string Ping = "{\"event\":\"ping\",\"cid\":1234}";

        //        _webSocketPublic.Send(Ping);
        //    }
        //    else
        //    {
        //        Console.WriteLine("WebSocket is not open. Ping not sent.");
        //    }


        //}

        #endregion

        /// <summary>
        /// Подписка на бумагу.С обязательным контролем скорости и кол-ву запросов к методу Subscrible через rateGate.
        /// </summary>
        #region  8 Security subscrible ( или WebSocket security subscrible)

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

        private void CreateSubscribleSecurityMessageWebSocket(Security security)
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
                    {
                        return;
                    }
                }


                _subscribledSecurities.Add(security);

                //Subscribing to account info
                //_webSocketPrivate.Send($"{{\"event\":\"auth\",\"apiKey\":\"{_publicKey}\",\"authSig\":{signature}\",\"authPayload\":\"{ payload}\",\" authNonce\":{authNonce}\",\"calc\": 1\"}}"); 

                ////tiker websocket-event: "subscribe", channel: "ticker",symbol: SYMBOL 
                // _webSocketPublic.Send($"{{\"event\":\"subscribe\",\"channel\":\"ticker\",\"symbol\":{security.Name}\"}}");


                ////candle websocket  //event: "subscribe",//channel: "candles", //key: "trade:1m:tBTCUSD"
                //  _webSocketPublic.Send($"{{\"event\": \"subscribe\", \"channel\": \"candles\", \"key\": \"trade:1m:{security.Name}\"}}");


                _webSocketPublic.Send($"{{\"event\":\"subscribe\",\"channel\":\"book\",\"symbol\":\"{security.Name}\",\"prec\":\"P0\",\"freq\":\"F0\",\"len\":\"25\"}}");//стакан

                _webSocketPublic.Send($"{{\"event\":\"subscribe\",\"channel\":\"trades\",\"symbol\":\"{security.Name}\"}}"); //трейды


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


        /// <summary>
        ///Разбор сообщений от сокета и отправка их наверх
        /// </summary>
        #region  9 WebSocket parsing the messages



        public event Action<Trade> NewTradesEvent;

        public event Action<Order> MyOrderEvent;//новые мои ордера

        public event Action<MyTrade> MyTradeEvent;//новые мои сделки

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


                    WebSocketPublicMessage.TryDequeue(out string message);


                    if (message == null)
                    {
                        continue;
                    }


                    if (message.Contains("info") || message.Contains("hb"))
                    {
                        continue;
                    }
                    if (message.Contains("book"))
                    {


                        BitfinexResponceWebSocketDepth responseDepth = JsonConvert.DeserializeObject<BitfinexResponceWebSocketDepth>(message);
                        _currentSymbol = responseDepth.Symbol;
                        bookChannelId = Convert.ToInt64(responseDepth.ChanId);
                        continue;

                    }

                    if (message.Contains("trade"))
                    {
                        SubscriptionResponseTrade responseTrade = JsonConvert.DeserializeObject<SubscriptionResponseTrade>(message);
                        tradeChannelId = Convert.ToInt64(responseTrade.ChanId);
                        continue;

                    }

                    if (message.StartsWith("["))
                    {
                        var jsonDocument = JsonDocument.Parse(message);
                        var root = jsonDocument.RootElement;
                        int chanelId = root[0].GetInt32();

                        if (root.ValueKind == JsonValueKind.Object)
                        {
                            var eventType = root.GetProperty("event").GetString();
                            if (eventType == "info" || eventType == "auth" || eventType == "hb")
                            {
                                continue;
                            }
                        }
                        if (root[0].ValueKind != JsonValueKind.Number)
                        {
                            SendLogMessage("Неверный формат Channel ID", LogMessageType.Error);
                            return;
                        }
                    

                        if (root[0].ValueKind == JsonValueKind.Number && root[1].ValueKind == JsonValueKind.Array)
                        {


                            //ProcessOrderBookResponse(message, _currentSymbol);
                            ProcessOrderBookResponse(message, chanelId, _currentSymbol);

                           
                        }
                       

                        // Извлечение Channel ID


                        // Извлечение основного содержимого


                        if (root[1].ValueKind == JsonValueKind.String)
                        {
                            string messageTypeString = root[1].GetString();


                            if (messageTypeString == "tu" || messageTypeString == "te")
                            {
                                ProcessTradeResponse(message);

                            }
                        }

                    }
                }

                catch (Exception exception)
                {
                    Thread.Sleep(2000);
                    SendLogMessage(exception.ToString(), LogMessageType.Error);
                }
            }
        }


        private void ProcessTradeResponse(string message)
        {
            // Десериализация JSON-ответа в JsonDocument
            var jsonDocument = JsonDocument.Parse(message);
            var root = jsonDocument.RootElement;

            // Извлечение Channel ID и MSG_TYPE
            int channelId = root[0].GetInt32(); // CHANNEL_ID
            var secondElement = root[1];

            // Проверка, является ли второй элемент массивом или строкой
            if (secondElement.ValueKind == JsonValueKind.String)
            {
                string msgType = secondElement.GetString(); // MSG_TYPE

                // Проверка типа сообщения (msgType должно быть "te" или "tu" для обновления трейдов)
                if (msgType == "te" || msgType == "tu")
                {
                    // Извлечение данных трейда
                    var tradeDataElement = root[2];

                    // Создание объекта BitfinexTradeUpdate из данных
                    var trade = new BitfinexTradeUpdate
                    {
                        Id = tradeDataElement[0].ToString(),
                        Timestamp = tradeDataElement[1].ToString(),
                        Amount = tradeDataElement[2].ToString(),
                        Price = tradeDataElement[3].ToString()
                    };

                    // Обработка обновления трейда
                    UpdateTrade(trade);
                }
                else
                {
                    SendLogMessage("Неизвестный тип сообщения: " + msgType, LogMessageType.Error);
                }
            }
            else if (secondElement.ValueKind == JsonValueKind.Array)
            {
                // Это снимок (snapshot)
                var tradeArray = secondElement.EnumerateArray().ToList();

                // Подсчёт количества элементов в массиве
                int tradeCount = tradeArray.Count;

                // Использование цикла for для итерации по массиву
                List<BitfinexTradeUpdate> tradeList = new List<BitfinexTradeUpdate>();

                for (int i = 0; i < tradeCount; i++)
                {
                    var tradeElement = tradeArray[i];

                    // Создание объекта BitfinexTradeUpdate из данных
                    var trade = new BitfinexTradeUpdate
                    {
                        Id = tradeElement[0].ToString(),
                        Timestamp = tradeElement[1].ToString(),
                        Amount = tradeElement[2].ToString(),
                        Price = tradeElement[3].ToString()
                    };

                    tradeList.Add(trade);
                }

                // Обработка снимка
                ProcessSnapshot(tradeList);
            }
            else
            {
                SendLogMessage("Неизвестный формат сообщения", LogMessageType.Error);
            }
        }


        //private void ProcessTradeResponse(string message)
        //{

        //    // Десериализация JSON-ответа в JsonDocument
        //    var jsonDocument = JsonDocument.Parse(message);
        //    var root = jsonDocument.RootElement;

        //    // Извлечение Channel ID и MSG_TYPE
        //    int channelId = root[0].GetInt32(); // CHANNEL_ID
        //    string msgType = root[1].GetString(); // MSG_TYPE

        //    // Проверка типа сообщения (msgType должно быть "te" для обновления трейдов)
        //    if (msgType == "te" || msgType == "tu")
        //    {
        //        // Извлечение данных трейда
        //        string tradeDataJson = root[2].GetRawText();

        //        // Десериализация данных трейда как списка объектов
        //        var tradeList = JsonConvert.DeserializeObject<List<object>>(tradeDataJson);



        //        // Создание объекта BitfinexTradeUpdate из списка
        //        var trade = new BitfinexTradeUpdate
        //        {
        //            Id = tradeList[0].ToString(),
        //            Timestamp = tradeList[1].ToString(),
        //            Amount = tradeList[2].ToString(),
        //            Price = tradeList[3].ToString()
        //        };

        //        // Обработка обновления трейда
        //        UpdateTrade(trade);
        //    }


        //     else if (root[1].ValueKind == JsonValueKind.Array)
        //    {
        //        // Это снимок (snapshot)
        //        var tradeArray = root[1].EnumerateArray();

        //        // Перебор всех записей в снимке
        //        var tradeList = new List<BitfinexTradeUpdate>();
        //        foreach (var tradeElement in tradeArray)
        //        {
        //            // Десериализация данных трейда как списка объектов
        //            var trade = new BitfinexTradeUpdate
        //            {
        //                Id = tradeElement[0].ToString(),
        //                Timestamp = tradeElement[1].ToString(),
        //                Amount = tradeElement[2].ToString(),
        //                Price = tradeElement[3].ToString()
        //            };

        //            tradeList.Add(trade);
        //        }

        //        // Обработка снимка
        //        ProcessSnapshot(tradeList);
        //    }
        //    else
        //    {
        //        SendLogMessage("Неизвестный формат сообщения", LogMessageType.Error);
        //    }
        //}

        private void ProcessSnapshot(List<BitfinexTradeUpdate> tradeList)
        {

            // Логика обработки снимка трейдов
            for (int i = 0; i < tradeList.Count; i++)
            {
                // Извлечение текущего трейда из списка
                var trade = tradeList[i];

                //trade.Amount = tradeList[i].Amount;
                //trade.Price = tradeList[i].Price;
                //trade.Timestamp = tradeList[i]. Timestamp;
                //trade.Id = tradeList[i].Id;
                // Пример обработки каждого трейда в снимке
                SendLogMessage($"Обработка трейда: ID = {trade.Id}, Timestamp = {trade.Timestamp}, Amount = {trade.Amount}, Price = {trade.Price}", LogMessageType.System);
            }
        }

        private void UpdateTrade(BitfinexTradeUpdate tradeUpdate)
        {
            try
            {
                // Проверка и конвертация поля Price
                decimal price;
                try
                {
                    price = decimal.Parse(tradeUpdate.Price, NumberStyles.Float, CultureInfo.InvariantCulture);
                }
                catch (FormatException)
                {
                    throw new FormatException($"Неверный формат цены: {tradeUpdate.Price}");
                }

                // Проверка и конвертация поля Amount
                decimal amount;
                try
                {
                    amount = decimal.Parse(tradeUpdate.Amount, NumberStyles.Float, CultureInfo.InvariantCulture);//Параметр NumberStyles.Float указывает, что строка может содержать числа в экспоненциальной нотации и/или использовать плавающую точку.
                                                                                                                 //Он позволяет строке содержать символы +, -, E, e, . (точка), а также цифры.
                                                                                                                 // Параметр CultureInfo.InvariantCulture указывает, что при преобразовании строки в число нужно использовать инвариантную культуру.Инвариантная культура основана на английском языке и используется для стандартного форматирования чисел и дат.
                                                                                                                 //  CultureInfo.InvariantCulture гарантирует, что при анализе строки используется стандартное форматирование чисел, независимо от региональных настроек компьютера.
                }
                catch (FormatException)
                {
                    throw new FormatException($"Неверный формат количества: {tradeUpdate.Amount}");
                }

                // Проверка и конвертация поля Timestamp
               
                 long  timestamp = Convert.ToInt64(tradeUpdate.Timestamp);
               

                Trade newTrade = new Trade
                {
                    SecurityNameCode = _currentSymbol,
                    Price = price,
                    Id = tradeUpdate.Id,
                    Time = TimeManager.GetDateTimeFromTimeStamp(timestamp),
                    Volume = Math.Abs(amount),
                    Side = amount > 0 ? Side.Buy : Side.Sell
                };

                ServerTime = newTrade.Time;
                NewTradesEvent?.Invoke(newTrade);
            }
            catch (Exception exception)
            {
                SendLogMessage("$\"Ошибка формата при обновлении трейда:." + exception.Message.ToString(), LogMessageType.Error);
            }
        }


        private void PrivateMessageReader()
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

                    if (message.Contains("pong"))
                    {
                        continue;
                    }

                    if (message.Contains("auth"))
                    {
                        return;
                        //GenerateAuthenticate();////////////////
                        //continue;
                    }
                    if (message.Contains("info"))
                    {
                        return;
                        // continue;
                    }

                    var jsonDocument = JsonDocument.Parse(message);
                    var root = jsonDocument.RootElement;
                    var eventType = root.GetProperty("event").GetString();

                    if (message.Contains("ou"))
                    {
                        UpdateOrder(message);
                        continue;
                    }
                    if (message.Contains("tu"))
                    {
                        UpdateMyTrade(message);
                        continue;
                    }
                    //if (message.Contains("wu"))
                    //{
                    //    UpdatePortfolio(message);
                    //}

                    //if(message.Contains("CHAN_ID = 0") && message.Contains("ou"))
                    //{
                    //    UpdateOrder(message);
                    //continue;
                    //}


                    //if (root.ValueKind == JsonValueKind.Object)

                    //{
                    //  'os'(снимок заказа), 'on'(новый заказ), 'ou'(обновление заказа) и 'oc'(отмена заказа).

                    //if (eventType == "os" || eventType == "on" || eventType == "ou" || eventType == "oc")
                    //{
                    //    UpdateOrder(message);
                    //    continue;
                    //}
                    // }

                    //if (message.Contains(""))
                    //{
                    //    UpdatePortfolio(message);
                    //    continue;
                    //}

                    //jsonDocument = JsonDocument.Parse(message);
                    //root = jsonDocument.RootElement;
                    //var json = JToken.Parse(message);

                    //if (json[1].Value<string>() == "on" || json[1].Value<string>() == "ou")
                    //{
                    //    // Это сообщение о новом ордере или об обновлении ордера
                    //    Console.WriteLine("Order update received: " + json);
                    //}
                    // Проверка, что это массив и имеет хотя бы 2 элемента
                    //if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 1)
                    //{
                    //    // Извлечение второго элемента массива (тип события)
                    //    // string eventType = root[1].GetString();

                    //    // Проверка на тип события "on" или "ou"
                    //    if (eventType == "on" || eventType == "ou")
                    //    {

                    //        UpdateOrder(message);
                    //        continue;

                    //    }
                    //}
                    //if (root[0].ValueKind == JsonValueKind.Array) //&& root[0].ValueKind = JsonValueKind.Number)
                    //{
                    //    int chanelId = root[0].GetInt32();

                    //    if (root.ValueKind == JsonValueKind.Object)
                    //    {
                    //        if (eventType == "tu" || eventType == "te")
                    //        {
                    //            if (chanelId == 0)
                    //            {
                    //                UpdateMyTrade(message);
                    //                continue;
                    //            }
                    //        }
                    //    }
                    //}

                }
                catch (Exception exception)
                {
                    Thread.Sleep(5000);
                    SendLogMessage(exception.ToString(), LogMessageType.Error);

                }
            }
        }

        //дописала округление объема
        private void UpdateMyTrade(string message)
        {
            try
            {

                BitfinexMyTrade response = JsonConvert.DeserializeObject<BitfinexMyTrade>(message);

                if (response == null)
                {
                    return;
                }
                var time = Convert.ToInt64(response.Mts);

                var myTrade = new MyTrade
                {

                    Time = TimeManager.GetDateTimeFromTimeStamp(time),
                    SecurityNameCode = response.Symbol,
                    NumberOrderParent = response.OrderId,
                    Price = (response.OrderPrice).ToDecimal(),
                    NumberTrade = response.Id,
                    //NumberPosition = response.Id,
                    Side = response.Amount.Contains("-") ? Side.Sell : Side.Buy,
                    //   Side = response.Amount > 0 ? Side.Buy : Side.Sell;
                   // Volume = (response.Amount).ToString().ToDecimal(),
                    
                };


                // при покупке комиссия берется с монеты и объем уменьшается и появляются лишние знаки после запятой
                decimal preVolume = myTrade.Side == Side.Sell ? response.Amount.ToDecimal() : response.Amount.ToDecimal() - response.Fee.ToDecimal();

                myTrade.Volume = GetVolumeForMyTrade(response.Symbol, preVolume);


                MyTradeEvent?.Invoke(myTrade);
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }


        private Dictionary<string, int> _decimalVolume = new Dictionary<string, int>();
        // метод для округления знаков после запятой
        private decimal GetVolumeForMyTrade(string symbol, decimal preVolume)
        {
            int forTruncate = 1;

            Dictionary<string, int>.Enumerator enumerator = _decimalVolume.GetEnumerator();

            while (enumerator.MoveNext())
            {
                string key = enumerator.Current.Key;
                int value = enumerator.Current.Value;

                if (key.Equals(symbol))
                {
                    if (value != 0)
                    {
                        for (int i = 0; i < value; i++)
                        {
                            forTruncate *= 10;
                        }
                    }
                    return Math.Truncate(preVolume * forTruncate) / forTruncate; // при округлении может получиться больше доступного объема, поэтому обрезаем
                }
            }
            return preVolume;
        }

        private void UpdateOrder(string message)
        {
            try
            {
                var response = JsonConvert.DeserializeObject<BitfinexOrderData>(message);

                if (response == null)
                {
                    return;
                }
                if (string.IsNullOrEmpty(response.Cid))
                {
                    return;
                }

                OrderStateType stateType = GetOrderState(response.Status, response.Amount);

                if (response.OrderType.Equals("EXCHANGE LIMIT", StringComparison.OrdinalIgnoreCase) && stateType == OrderStateType.Activ)//StringComparison.OrdinalIgnoreCase*что сравнение должно быть нечувствительным к регистру.
                {
                    return;
                }

                var newOrder = new Order
                {
                    SecurityNameCode = response.Symbol,
                    TimeCallBack = TimeManager.GetDateTimeFromTimeStamp(Convert.ToInt64(response.MtsCreate)),
                    NumberUser = Convert.ToInt32(response.Cid),
                    NumberMarket = response.Id,
                    Side = response.Amount.Equals("-") ? Side.Sell : Side.Buy,
                    //State = (OrderStateType)Enum.Parse(typeof(OrderStateType), response.Status),
                    State = stateType,
                    TypeOrder = response.OrderType.Equals("EXCHANGE MARKET", StringComparison.OrdinalIgnoreCase) ? OrderPriceType.Market : OrderPriceType.Limit,
                    //Volume = response.Status.Equals("DONE") ? Convert.ToDecimal(response.AmountOrig) : Convert.ToDecimal(response.AmountOrig),//ACTIVE
                    Volume = stateType == OrderStateType.Activ ? response.Amount.ToDecimal() : response.AmountOrig.ToDecimal(),
                    Price = (response.Price).ToDecimal(),
                    ServerType = ServerType.Bitfinex,
                    PortfolioNumber = "Bitfinex"

                };

                MyOrderEvent?.Invoke(newOrder);
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }
        private OrderStateType GetOrderState(string status, string filledSize)
        {
            OrderStateType stateType;

            if (status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase )/*&& filledSize == "0"*/)//игнорирование регистра
            {
                stateType = OrderStateType.Activ;
            }
            else if (status == "EXECUTED")
            {
                stateType = OrderStateType.Done;
            }
            else if (status == "PARTIALLY FILLED")
            {
                stateType = OrderStateType.Patrial;
            }
            else if (status == "CANCELED" /*&& filledSize == "0"*/)
            {
                stateType = OrderStateType.Cancel;
            }
            else if (status == "REJECTED" /*&& filledSize == "0"*/)
            {
                stateType = OrderStateType.Fail;
            }
            else
            {
                stateType = OrderStateType.None; // Handle unanticipated status values
            }

            return stateType;
        }


        //else if (baseMessage.status == "CANCELED")
        //{
        //    lock (_changePriceOrdersArrayLocker)
        //    {
        //        DateTime now = DateTime.UtcNow;
        //        for (int i = 0; i < _changePriceOrders.Count; i++)
        //        {
        //            if (_changePriceOrders[i].TimeChangePriceOrder.AddSeconds(2) < now)
        //            {
        //                _changePriceOrders.RemoveAt(i);
        //                i--;
        //                continue;
        //            }

        //            //if (_changePriceOrders[i].MarketId == order.NumberMarket)
        //            //{
        //            //    return null;
        //            //}
        //        }
        //    }

        //    if (string.IsNullOrEmpty(baseMessage.amount))
        //    {
        //        order.State = OrderStateType.Cancel;
        //    }
        //    else if (baseMessage.amount == "0")
        //    {
        //        order.State = OrderStateType.Cancel;
        //    }
        //    else
        //    {
        //        try
        //        {
        //            decimal volFilled = baseMessage.amount.ToDecimal();

        //            if (volFilled > 0)
        //            {
        //                order.State = OrderStateType.Done;
        //            }
        //            else
        //            {
        //                order.State = OrderStateType.Cancel;
        //            }
        //        }
        //        catch
        //        {
        //            order.State = OrderStateType.Cancel;
        //        }
        //    }
        //}

        //return order;
        //                }



        #endregion


        /// <summary>
        /// посвящённый торговле. Выставление ордеров, отзыв и т.д
        /// </summary>
        #region  11 Trade

        private RateGate _rateGateSendOrder = new RateGate(1, TimeSpan.FromMilliseconds(300));//уточнить задержку  Таймфрейм в формате отрезка времени. TimeSpan.

        private readonly RateGate _rateGateCancelOrder = new RateGate(1, TimeSpan.FromMilliseconds(300));

        private RateGate _rateGateChangePriceOrder = new RateGate(1, TimeSpan.FromMilliseconds(300));


        public void SendOrder(Order order)
        {
            _rateGateSendOrder.WaitToProceed();


            BitfinexOrderData data = new BitfinexOrderData
            {
                Cid = order.NumberUser.ToString(),
                Symbol = order.SecurityNameCode,
                Amount = order.Volume.ToString().Replace(",", "."),
                OrderType = order.TypeOrder.ToString().ToUpper(),
                Price = order.TypeOrder == OrderPriceType.Market ? null : order.Price.ToString().Replace(",", "."),
                MtsCreate = order.TimeCreate.ToString(),
                Status = order.State.ToString(),
                //AmountOrig = order.Side.ToString(),

            };

            string apiPath = "v2/auth/w/order/submit";
            string typeOrder = ""; // Переменная для хранения типа ордера
                                   //  decimal orderSide = 0;
            string orderSide = "";

            // Условие для определения типа ордера
            if (order.TypeOrder.ToString() == "Limit")
            {
                typeOrder = "EXCHANGE LIMIT";
            }
            else
            {
                typeOrder = "EXCHANGE MARKET";
            }

            decimal amount;

            if (decimal.TryParse(data.Amount, out amount))
            {
                // Изменение знака на отрицательный 

                if (order.Side.ToString() == "Sell")
                {// Преобразование обратно в строку
                    orderSide = (-amount).ToString();
                }

                else
                {
                    orderSide = (data.Amount); // Для остальных случаев используем положительное значение
                }
            }
            // Создание анонимного объекта с использованием переменной `type`
            var body = new
            {
                type = typeOrder,
                symbol = data.Symbol,
                price = data.Price,
                amount = orderSide
            };


            // Сериализуем объект тела в JSON

            //  string bodyJson = JsonConvert.SerializeObject(body);
            string bodyJson = System.Text.Json.JsonSerializer.Serialize(body);

            // Создаем nonce как текущее время в миллисекундах
            string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();

            // Создаем строку для подписи
            string signature = $"/api/{apiPath}{nonce}{bodyJson}";


            // Вычисляем подпись с использованием HMACSHA384
            string sig = ComputeHmacSha384(_secretKey, signature);

            // // Создаем клиента RestSharp
            var client = new RestClient(_postUrl);

            // Создаем запрос типа POST
            var request = new RestRequest(apiPath, Method.POST);

            // Добавляем заголовки
            request.AddHeader("accept", "application/json");
            request.AddHeader("bfx-nonce", nonce);
            request.AddHeader("bfx-apikey", _publicKey);
            request.AddHeader("bfx-signature", sig);

            // Добавляем тело запроса в формате JSON
            request.AddJsonBody(body); //


            try
            {
                // Отправляем запрос и получаем ответ
                var response = client.Execute(request);

                // Выводим тело ответа
                string responseBody = response.Content;

                if (response.StatusCode == HttpStatusCode.OK)
                {


                    //if (responseBody.Contains("on-req"))/* && responseBody.StartsWith("[[")) */ //if (jsonResponse.Trim().StartsWith("["))
                    //{
                    //    // Десериализация ответа как массив объектов

                    // string responseBody = "[1723557977,\"on-req\",null,null,[[167966185075,null,1723557977011,\"tTRXUSD\",1723557977011,1723557977011,22,22,\"EXCHANGE LIMIT\",null,null,null,0,\"ACTIVE\",null,null,0.12948,0,0,0,null,null,null,0,0,null,null,null,\"API>BFX\",null,null,{}]],null,\"SUCCESS\",\"Submitting 1 orders.\"]";

                    // Десериализация верхнего уровня в список объектов
                    var responseArray = JsonConvert.DeserializeObject<List<object>>(responseBody);

                    // Проверка размера массива перед извлечением значений
                    if (responseArray != null)
                    {
                        // Извлечение нужных элементов
                        var dataJson = responseArray[4].ToString();

                        string status = responseArray[6].ToString();
                        string text = responseArray[7].ToString();

                        // Десериализация dataJson в список заказов
                        var ordersArray = JsonConvert.DeserializeObject<List<List<object>>>(dataJson);
                        var orders = ordersArray[0]; // Получаем первый заказ из массива

                        // Создание объекта BitfinexOrderData
                        BitfinexOrderData orderData = new BitfinexOrderData
                        {
                            Cid = Convert.ToString(orders[2]),
                            Symbol = Convert.ToString(orders[3]),
                        };


                        order.State = OrderStateType.Activ;

                        order.NumberMarket = orderData.Cid;//для массива


                        SendLogMessage($"Order num {order.NumberMarket} on exchange.{order.State},{status},{text}", LogMessageType.Trade);

                        MyOrderEvent?.Invoke(order);
                    }
                }
                else
                {

                    SendLogMessage($"Error Status code {response.StatusCode}: {responseBody}", LogMessageType.Error);

                    CreateOrderFail(order);
                    SendLogMessage("Order Fail", LogMessageType.Error);
                }
            }
            catch (Exception exception)
            {
                // Обрабатываем исключения и выводим сообщение
                CreateOrderFail(order);
                SendLogMessage("Order send exception " + exception.ToString(), LogMessageType.Error);
            }

        }


        public string ToolTip(Order order)// при наведении на график появляется подсказка о сделанном ордере(цена,дата,объем,направление)
        {
            return "";
        }


        private void CreateOrderFail(Order order)
        {
            order.State = OrderStateType.Fail;

            MyOrderEvent?.Invoke(order);
        }

        public void CancelAllOrders()
        {

            string apiPath = "v2/auth/w/order/cancel/multi";
            var body = new
            {
                all = 1  // Идентификатор ордера для отмены
            };


            //  string bodyJson = JsonConvert.SerializeObject(body);
            string bodyJson = System.Text.Json.JsonSerializer.Serialize(body);

            // Создаем nonce как текущее время в миллисекундах
            string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();
          
            // Создаем строку для подписи
            string signature = $"/api/{apiPath}{nonce}{bodyJson}";
         
            string sig = ComputeHmacSha384(_secretKey, signature);

            //post https://api.bitfinex.com/v2/auth/w/order/cancel/multi

            var client = new RestClient(_postUrl);
            var request = new RestRequest(apiPath, Method.POST);

            request.AddHeader("accept", "application/json");
            request.AddHeader("bfx-nonce", nonce);
            request.AddHeader("bfx-apikey", _publicKey);
            request.AddHeader("bfx-signature", sig);
            //request.AddJsonBody("{\"all\":1}", false); //all\":1 отменить все ордера
            request.AddJsonBody(body);

            IRestResponse response = client.Execute(request);

            try
            {
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    if (response != null)
                    {
                        SendLogMessage($"Code: {response.StatusCode}", LogMessageType.Error);

                    }

                    else
                    {
                        SendLogMessage($" {response}", LogMessageType.Error);
                    }
                }
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }


        private RateGate rateGateCancelAllOrder = new RateGate(1, TimeSpan.FromMilliseconds(350));

        public void CancelAllOrdersToSecurity(Security security)
        {
           rateGateCancelAllOrder.WaitToProceed();
        ////https://api.bitfinex.com/v2 /auth/w/order/cancel/multi

        //    string apiPath = "v2/auth/w/order/cancel/multi";// 
        //    string signature = $"{_postUrl}{apiPath}{nonce}";
        //    string sig = ComputeHmacSha384(_secretKey, signature);
        //    RestClient client = new RestClient(_postUrl);
        //    RestRequest request = new RestRequest(apiPath, Method.POST);
        //    request.AddHeader("accept", "application/json");
        //    request.AddHeader("bfx-nonce", nonce);
        //    request.AddHeader("bfx-apikey", _publicKey);
        //    request.AddHeader("bfx-signature", sig);
        //    // Добавление тела запроса в формате JSON
        //    request.AddJsonBody("{\"all\":1}");

          
        //    // Отправляем запрос и получаем ответ
        //    var response = client.Execute(request);

        //    // Выводим тело ответа
        //    string responseBody = response.Content;
        //    try
        //    {
        //        if (response.StatusCode == HttpStatusCode.OK)
        //        {
        //            if (response != null)
        //            {
        //                SendLogMessage($"Code: {response.StatusCode}", LogMessageType.Error);
        //            }

        //            else
        //            {
        //                SendLogMessage($" {response}", LogMessageType.Error);
        //            }
        //        }
        //    }
        //    catch (Exception exception)
        //    {
        //        SendLogMessage(exception.ToString(), LogMessageType.Error);
        //    }
        }


        public void CancelOrder(Order order)
        {
            _rateGateCancelOrder.WaitToProceed();

            // Формирование тела запроса с указанием ID ордера
            var body = new
            {
                id = order.NumberMarket  // Идентификатор ордера для отмены
            };
            string apiPath = "v2/auth/w/order/cancel";
            // Сериализуем объект тела в JSON

            //  string bodyJson = JsonConvert.SerializeObject(body);
            string bodyJson = System.Text.Json.JsonSerializer.Serialize(body);

            // Создаем nonce как текущее время в миллисекундах
            string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();

            // Создаем строку для подписи
            string signature = $"/api/{apiPath}{nonce}{bodyJson}";


            // Вычисляем подпись с использованием HMACSHA384
            string sig = ComputeHmacSha384(_secretKey, signature);

            // // Создаем клиента RestSharp
            var client = new RestClient(_postUrl);

            // Создаем запрос типа POST
            var request = new RestRequest(apiPath, Method.POST);

            // Добавляем заголовки
            request.AddHeader("accept", "application/json");
            request.AddHeader("bfx-nonce", nonce);
            request.AddHeader("bfx-apikey", _publicKey);
            request.AddHeader("bfx-signature", sig);

            // Добавляем тело запроса в формате JSON
            request.AddJsonBody(body); //


            // Отправляем запрос и получаем ответ
            var response = client.Execute(request);

            // Выводим тело ответа
            string responseBody = response.Content;
           
            try
            {
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    //var responseArray = JsonConvert.DeserializeObject<List<object>>(responseBody);

                    SendLogMessage("Order canceled successfully. Order ID: " + order.NumberMarket, LogMessageType.System);
                }

                else
                {
                    CreateOrderFail(order);
                    SendLogMessage($"Order cancellation error: code - {response.StatusCode} - {response.Content}, message - {response.ErrorMessage}", LogMessageType.Error);

                    if (response.Content != null)
                    {
                        SendLogMessage("Failure reasons: " + response.Content, LogMessageType.Error);
                    }
                }
             
            }
            catch (Exception exception)
            {
                CreateOrderFail(order);
                SendLogMessage(exception.ToString(), LogMessageType.Error);
                SendLogMessage($"Http State Code: {response.StatusCode} - {response.Content}", LogMessageType.Error);
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

        private RateGate rateGateChangePriceOrder = new RateGate(1, TimeSpan.FromMilliseconds(350));


        public void ChangeOrderPrice(Order order, decimal newPrice)
        {
            // post https://api.bitfinex.com/v2/auth/w/order/update
            try
            {

                rateGateChangePriceOrder.WaitToProceed();

                // Проверка типа ордера
                if (order.TypeOrder == OrderPriceType.Market)
                {
                    SendLogMessage("Can't change price for market order", LogMessageType.Error);
                    return;
                }

                string apiPath = "v2/auth/w/order/update";

                var body = new
                {


                    id = order.NumberMarket,  // Идентификатор ордера
                    price = newPrice.ToString(), // Новая цена
                                                 // amount = order.Volume.ToString()// новый объем
                };

                //  string bodyJson = JsonConvert.SerializeObject(body);
                string bodyJson = System.Text.Json.JsonSerializer.Serialize(body);

                // Создаем nonce как текущее время в миллисекундах
                string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();

                // Создаем строку для подписи
                string signature = $"/api/{apiPath}{nonce}{bodyJson}";

                // Вычисляем подпись с использованием HMACSHA384
                string sig = ComputeHmacSha384(_secretKey, signature);

                // Создаем клиента RestSharp
                var client = new RestClient(_postUrl);

                // Создаем запрос типа POST
                var request = new RestRequest(apiPath, Method.POST);

                // Добавляем заголовки
                request.AddHeader("accept", "application/json");
                request.AddHeader("bfx-nonce", nonce);
                request.AddHeader("bfx-apikey", _publicKey);
                request.AddHeader("bfx-signature", sig);

                // Добавляем тело запроса в формате JSON
                request.AddJsonBody(body); //

                // Количество оставшегося объема ордера
                int qty = Convert.ToInt32(order.Volume - order.VolumeExecute);

                // Проверка, что ордер активен и есть неисполненный объем
                if (qty <= 0 || order.State != OrderStateType.Activ)
                {
                    SendLogMessage("Can't change price for the order. It is not in Active state", LogMessageType.Error);
                    return;
                }

                // Отправляем запрос и получаем ответ
                var response = client.Execute(request);

                // Выводим тело ответа
                string responseBody = response.Content;

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    SendLogMessage("Order change price. New price: " + newPrice
                      + "  " + order.SecurityNameCode, LogMessageType.System);


                    order.Price = newPrice;

                    // Вызов события изменения ордера
                    MyOrderEvent?.Invoke(order);

                }
                else
                {
                    SendLogMessage("Change price order Fail. Status: "
                                + response.StatusCode + "  " + order.SecurityNameCode, LogMessageType.Error);

                    if (response.Content != null)
                    {
                        SendLogMessage("Fail reasons: "
                      + response.Content, LogMessageType.Error);
                    }
                }

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

        private string _portfolioType;
        // post https://api.bitfinex.com/v2/auth/r/orders

        private Order ConvertToOsEngineOrder(BitfinexOrderData baseMessage, string portfolioName)
        {
            Order order = new Order();

            // Установка кода инструмента
            order.SecurityNameCode = baseMessage.Symbol;

            //// Логика преобразования остается прежней, но теперь используется BitfinexOrderData
            //order.SecurityNameCode = baseMessage.Symbol;
            //order.Volume = baseMessage.ExecutedAmount > 0 ? baseMessage.ExecutedAmount : baseMessage.OriginalAmount;
            //order.PortfolioNumber = portfolioName;
            //if (baseMessage.Type == "EXCHANGE LIMIT")
            //{
            //    order.Price = baseMessage.Price;
            //    order.TypeOrder = OrderPriceType.Limit;
            //}
            //else if (baseMessage.Type == " EXCHANGE MARKET")
            //{
            //    order.TypeOrder = OrderPriceType.Market;
            //}

            //try
            //{
            //    order.NumberUser = Convert.ToInt32(baseMessage.ClientOrderId);
            //}
            //catch
            //{
            //    // Игнорируем ошибку
            //}

            //order.NumberMarket = baseMessage.Id;





            // Установка объема ордера
            if (baseMessage.Amount.ToDecimal() > 0)
            {
                order.Volume = baseMessage.Amount.ToDecimal();
            }
            else
            {
                order.Volume = baseMessage.AmountOrig.ToDecimal();
            }

            // Установка номера портфеля
            order.PortfolioNumber = portfolioName;

            // Установка типа ордера (лимитный или рыночный)
            if (baseMessage.OrderType == "EXCHANGE LIMIT")
            {
                order.Price = baseMessage.Price.ToDecimal();
                order.TypeOrder = OrderPriceType.Limit;
            }
            else if (baseMessage.OrderType == "EXCHANGE MARKET")
            {
                order.TypeOrder = OrderPriceType.Market;
            }

            // Установка пользовательского номера ордера (если применимо)
            try
            {
                order.NumberUser = Convert.ToInt32(baseMessage.Cid);
            }
            catch
            {
                // Игнорируем ошибку, если не удается преобразовать номер
            }

            // Установка номера ордера на бирже
            order.NumberMarket = baseMessage.Id;

            // Установка времени обратного вызова (время транзакции)
            order.TimeCallBack = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(baseMessage.MtsUpdate)).UtcDateTime;

            // Установка направления ордера (покупка или продажа)
            if (baseMessage.OrderType == "BUY")
            {
                order.Side = Side.Buy;
            }
            else
            {
                order.Side = Side.Sell;
            }

            // Установка состояния ордера на основе статуса
            // "ACTIVE" - Активен, "EXECUTED" - Исполнен, "CANCELED" - Отменен, "REJECTED" - Отклонен
            switch (baseMessage.Status)
            {
                case "ACTIVE":
                    order.State = OrderStateType.Activ;
                    break;
                case "EXECUTED":
                    order.State = OrderStateType.Done;
                    break;
                case "CANCELED":
                    order.State = OrderStateType.Cancel;
                    break;
                case "REJECTED":
                    order.State = OrderStateType.Fail;
                    break;
                default:
                    order.State = OrderStateType.None;
                    break;
            }

            return order;
        }

        private RateGate rateGateAllOrders = new RateGate(1, TimeSpan.FromMilliseconds(350));


        private List<Order> GetAllOrdersFromExchangeByPortfolio(string portfolio)
        {
            rateGateAllOrders.WaitToProceed();


            // string apiPath = "/v2/auth/r/orders/t" + portfolio + "/hist";

           

            // Сериализуем объект тела в JSON

            //  string bodyJson = JsonConvert.SerializeObject(body);
            //string bodyJson = System.Text.Json.JsonSerializer.Serialize(body);
            // Создаем nonce как текущее время в миллисекундах
            string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();
            
            string apiPath = "/v2/auth/r/orders";

            // Создаем строку для подписи
            //string signature = $"/api/{apiPath}{nonce}{bodyJson}";
            string signature = $"/api/{apiPath}{nonce}";

            // Вычисляем подпись с использованием HMACSHA384
            string sig = ComputeHmacSha384(_secretKey, signature);

            // // Создаем клиента RestSharp
            var client = new RestClient(_postUrl);

            // Создаем запрос типа POST
            var request = new RestRequest(apiPath, Method.POST);

            // Добавляем заголовки
            request.AddHeader("accept", "application/json");
            request.AddHeader("bfx-nonce", nonce);
            request.AddHeader("bfx-apikey", _publicKey);
            request.AddHeader("bfx-signature", sig);

            // Добавляем тело запроса в формате JSON
           // request.AddJsonBody(body); //


            try
            {
                // Отправляем запрос и получаем ответ
                var response = client.Execute(request);

                // Выводим тело ответа
                string responseBody = response.Content;

                if (response.StatusCode == HttpStatusCode.OK)
                {

                    if (string.IsNullOrEmpty(responseBody) /*|| responseBody == "[]"*/)
                    {
                        return null; // Если ответ пустой, возвращаем null
                    }
                    else
                    {
                        // Десериализация ответа
                        List<BitfinexOrderData> bitfinexOrders = JsonSerializer.Deserialize<List<BitfinexOrderData>>(responseBody);

                        List<Order> osEngineOrders = new List<Order>();

                        // Конвертация ордеров из формата Bitfinex в формат OsEngine
                        for (int i = 0; i < bitfinexOrders.Count; i++)
                        {
                            Order newOrd = ConvertToOsEngineOrder(bitfinexOrders[i], portfolio);

                            if (newOrd == null)
                            {
                                continue;
                            }

                            osEngineOrders.Add(newOrd);
                        }

                        return osEngineOrders; // Возвращаем список ордеров
                    }
                }
                else if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null; // Если ордеры не найдены, возвращаем null
                }
                else
                {
                    // Логируем ошибку при запросе
                    SendLogMessage("Get all orders request error. ", LogMessageType.Error);

                    if (!string.IsNullOrEmpty(response.Content))
                    {
                        SendLogMessage("Fail reasons: " + response.Content, LogMessageType.Error);
                    }
                }
            }
            catch (Exception exception)
            {
                // Логируем ошибку выполнения запроса
                SendLogMessage("Get all orders request error." + exception.ToString(), LogMessageType.Error);
            }

            return null;
        }



        private List<Order> GetAllOrdersFromExchange()////////////////////1111111111111
        {
            List<Order> orders = new List<Order>();

            if (string.IsNullOrEmpty(_portfolioType) == false)
            {
                List<Order> newOrders = GetAllOrdersFromExchangeByPortfolio(_portfolioType);

                if (newOrders != null &&
                    newOrders.Count > 0)
                {
                    orders.AddRange(newOrders);
                }
            }
            return orders;
        }



        public void GetAllActivOrders()////////////////////111111111
        {
            // post https://api.bitfinex.com/v2/auth/r/orders

            List<Order> orders = GetAllOrdersFromExchange();

            for (int i = 0; orders != null && i < orders.Count; i++)
            {
                if (orders[i] == null)
                {
                    continue;
                }

                if (orders[i].State != OrderStateType.Activ
                    && orders[i].State != OrderStateType.Patrial
                    && orders[i].State != OrderStateType.Pending)
                {
                    continue;
                }

                orders[i].TimeCreate = orders[i].TimeCallBack;

                if (MyOrderEvent != null)
                {
                    MyOrderEvent(orders[i]);
                }
            }


            // throw new NotImplementedException();
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
}





