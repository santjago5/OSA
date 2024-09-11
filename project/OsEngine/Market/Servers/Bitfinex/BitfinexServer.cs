
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
using OsEngine.Market.Servers.BitStamp.BitStampEntity;
using System.Net.WebSockets;
using Com.Lmax.Api.Internal;
using OsEngine.Market.Servers.Transaq.TransaqEntity;
using OsEngine.Market.Servers.BitMax;
using Newtonsoft.Json.Linq;
using System.Windows.Documents;
using System.Diagnostics;
using static Kraken.WebSockets.KrakenApi;










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
                apiPath = "v2/platform/status";

                RestClient client = new RestClient(_getUrl);
                RestRequest request = new RestRequest(apiPath, Method.GET);
                request.AddHeader("accept", "application/json");

                IRestResponse response = client.Execute(request);

                if (response.StatusCode == HttpStatusCode.OK)
                {
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
                _portfolios.Clear();/////////

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
        private string body;
        private string apiPath;

        string _baseUrl = "https://api.bitfinex.com";

        private string _getUrl = "https://api-pub.bitfinex.com";

        private string _postUrl = "https://api.bitfinex.com";

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

                    //List<List< BitfinexSecurity>> securityList = JsonConvert.DeserializeObject<List<List<BitfinexSecurity>>>(jsonResponse);

                    if (securityList == null)
                    {
                        SendLogMessage("Deserialization resulted in null", LogMessageType.Error);
                        return;
                    }

                    if (securityList.Count > 0)

                    {
                        SendLogMessage("Securities loaded. Count: " + securityList.Count, LogMessageType.System);
                        SecurityEvent?.Invoke(_securities);

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

                    UpdateSecurity(jsonResponse);////////

                }

                //else
                //{
                //    //SendLogMessage($"Result: LogMessageType.Error\n"
                //    //    + $"Message: {stateResponse.message}", LogMessageType.Error);
                //}


                else
                {
                    SendLogMessage("Securities /*request exception.*/ Status: " + response.StatusCode, LogMessageType.Error);

                }

            }
            catch (Exception exception)
            {
                SendLogMessage("/*Securities request */exception" + exception.ToString(), LogMessageType.Error);

            }
        }

        ////////private void UpdateSecurity(List<BitfinexSecurity> security)
        ////////{
        ////////    // если на обновление приходит json



        ////////    try
        ////////    {
        ////////        if (security == null || security.Count == 0)
        ////////        {
        ////////            return;
        ////////        }

        ////////        for (int i = 0; i < security.Count; i++)
        ////////        {
        ////////            var securityData = security[i];

        ////////            Security newSecurity = new Security();
        ////////            newSecurity.Exchange = ServerType.Bitfinex.ToString();
        ////////            newSecurity.Name = securityData.Symbol;
        ////////            newSecurity.NameFull = securityData.Symbol;
        ////////            newSecurity.NameClass = securityData.Symbol.StartsWith("f") ? "Futures" : "CurrencyPair";
        ////////            newSecurity.NameId = Convert.ToString(securityData.Symbol);
        ////////            newSecurity.Lot = 1;
        ////////            newSecurity.SecurityType = securityData.Symbol.StartsWith("f") ? SecurityType.Futures : SecurityType.CurrencyPair;
        ////////            newSecurity.State = SecurityStateType.Activ;
        ////////            newSecurity.PriceStep = newSecurity.Decimals.GetValueByDecimals();
        ////////            newSecurity.PriceStepCost = newSecurity.PriceStep;
        ////////            newSecurity.DecimalsVolume = newSecurity.Decimals;


        ////////            _securities.Add(newSecurity);////////////////////////;

        ////////        }

        ////////        SecurityEvent?.Invoke(_securities);

        ////////    }
        ////////    catch (Exception exception)
        ////////    {
        ////////        SendLogMessage(exception.ToString(), LogMessageType.Error);
        ////////    }

        ////////}
        /// <summary>
        /// 
        /// 
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>

        private void UpdateSecurity(string json)
        {
            // Десериализация ответа от Bitfinex API
            var response = JsonConvert.DeserializeObject<List<List<object>>>(json);

            List<Security> securities = new List<Security>();

            // Проходим по каждому элементу в ответе
            for (int i = 0; i < response.Count; i++)
            {
                var item = response[i];

                // Символ актива (например, "tBTCUSD")
                string symbol = item[0]?.ToString();

                // Проверяем активность актива (здесь нет прямого поля is_active, допустим активные активы присутствуют в списке)
                //bool isActive = true;

                // Определяем тип актива (например, если символ начинается с 't', это может быть торговая пара)
                SecurityType securityType = GetSecurityType(symbol);

                // Пропускаем активы, если не удается определить их тип
                if (securityType == SecurityType.None)
                {
                    continue;
                }

                // Создаем объект Security
                Security newSecurity = new Security
                {
                    Exchange = ServerType.Bitfinex.ToString(),  // Указываем биржу Bitfinex
                    Name = symbol,          // Название актива
                    NameFull = symbol,      // Полное название актива
                    NameClass = symbol.StartsWith("f") ? "Futures" : "CurrencyPair",   // Класс актива, напримерSpot"
                    NameId = symbol,        // Идентификатор актива
                    SecurityType = securityType, // Тип актива
                    Lot = 1,      // Предполагаем, что это контрактный размер или лот
                    State =  SecurityStateType.Activ//: SecurityStateType.Close, // Состояние актива
                   
                };
                newSecurity.PriceStep = newSecurity.Decimals.GetValueByDecimals();
                newSecurity.PriceStepCost = newSecurity.PriceStep;
                newSecurity.DecimalsVolume = newSecurity.Decimals;
              
                // Добавляем актив в список
                securities.Add(newSecurity);
            }

            // Вызываем событие обновления списка активов
            SecurityEvent(securities);
        }
       
       

        private SecurityType GetSecurityType(string type)
        {
            // Инициализируем переменную с значением по умолчанию
            SecurityType _securityType = SecurityType.None;

            // Проверяем, начинается ли строка с символа 't' и назначаем соответствующий тип
            if (type.StartsWith("t"))
            {
                _securityType = SecurityType.CurrencyPair;
            }
            else
            {
                _securityType = SecurityType.Futures;
            }

            // Возвращаем определённый тип
            return _securityType;
        }



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
        // Метод для создания запроса на получение портфеля
        // private void CreateQueryPortfolio(bool IsUpdateValueBegin, string currency)
        private void CreateQueryPortfolio()
        {
            // Ожидаем перед выполнением запроса для соблюдения лимитов запросов
            _rateGatePortfolio.WaitToProceed();

            // Добавляем канал портфеля в массив для отслеживания обновлений через WebSocket
            //  _arrayChannelsAccount.Add($"user.portfolio.{currency.ToLower()}");
            // Формируем строку запроса на получение данных о портфеле для заданной валюты

            apiPath = "v2/auth/r/wallets";////////////У

            try
            {
                // Отправляем запрос на сервер Bitfinex и получаем ответ
                var response = ExecuteRequest(apiPath, body);

                // Выводим тело ответа
                string responseBody = response.Content;

                // Если ответ успешный, обновляем портфель и запрашиваем позиции
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    UpdatePortfolio(responseBody); // Обновляем портфель
                    CreateQueryPosition(); // Обновляем позиции
                }
                else
                {
                    // Логируем ошибку, если запрос не удался
                    SendLogMessage($"CreateQueryPortfolio: {response.StatusCode}{responseBody}", LogMessageType.Error);
                }
            }
            catch (Exception exception)
            {
                // Логируем исключение в случае ошибки
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }

        // Метод для обновления портфеля на основе полученных данных
        // private void UpdatePortfolio(string json, bool IsUpdateValueBegin)
        private void UpdatePortfolio(string json)
        {
            // Десериализуем ответ от Bitfinex в объект портфеля
            List<List<object>> response = JsonConvert.DeserializeObject<List<List<object>>>(json);

            Portfolio portfolio = new Portfolio();
            portfolio.Number = "BitfinexPortfolio"; // Устанавливаем идентификатор портфеля
            portfolio.ValueBegin = response[0][2].ToString().ToDecimal();
            portfolio.ValueCurrent = response[0][4].ToString().ToDecimal();

            // Проходимся по всем полученным данным о портфеле
            for (int i = 0; i < response.Count; i++)
            {
                List<object> wallet = response[i];

                if (wallet[0].ToString() == "exchange")
                {

                    // Создаем позицию на основе данных из ответа
                    PositionOnBoard pos = new PositionOnBoard()
                    {
                        PortfolioName = "BitfinexPortfolio",
                        SecurityNameCode = wallet[1].ToString(), // Код валюты (например, "USD", "BTC")
                        ValueBegin = wallet[2].ToString().ToDecimal(), // Начальное значение
                        ValueCurrent = (wallet[4]).ToString().ToDecimal(), // Текущая стоимость
                        ValueBlocked = (wallet[2]).ToString().ToDecimal() - (wallet[4]).ToString().ToDecimal() // Заблокированное значение
                    };

                    // Если необходимо обновить начальное значение портфеля
                    //if (IsUpdateValueBegin)
                    //{
                    //    pos.ValueBegin = Convert.ToDecimal(wallet[2]); // Устанавливаем начальное значение
                    //}


                    // Добавляем новую позицию в портфель
                    portfolio.SetNewPosition(pos);

                }
            }

            // Вызываем событие обновления портфеля с новыми данными
            PortfolioEvent(new List<Portfolio> { portfolio });
        }

        // Метод для создания запроса на получение позиций
        private void CreateQueryPosition()
        {
            apiPath = "v2/auth/r/positions";// ПОЛУЧЕНИЕ АКТИВНЫХ ПОЗИЦИЙ
                                            // Ожидаем перед выполнением запроса для соблюдения лимитов запросов
                                            //  _rateGatePositions.WaitToProceed();

            try
            {
                // Формируем строку запроса на получение позиций


                // Отправляем запрос на сервер Bitfinex и получаем ответ
                var response = ExecuteRequest(apiPath, body);
                // Выводим тело ответа
                string responseBody = response.Content;


                // Если ответ успешный, обновляем позиции
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    UpdatePosition(responseBody); // Обновляем позиции

                }
                else
                {
                    // Логируем ошибку, если запрос не удался
                    SendLogMessage($"Create Query Position: {response.StatusCode}, {responseBody}", LogMessageType.Error);
                }
            }
            catch (Exception exception)
            {
                // Логируем исключение в случае ошибки
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }

        // Метод для обновления позиций на основе полученных данных
        private void UpdatePosition(string json)
        {
            // Десериализуем ответ от Bitfinex в объект списка позиций
            List<List<object>> response = JsonConvert.DeserializeObject<List<List<object>>>(json);

            Portfolio portfolio = new Portfolio();
            portfolio.Number = "BitfinexPortfolio"; // Устанавливаем идентификатор портфеля
            portfolio.ValueBegin = 1;
            portfolio.ValueCurrent = 1;

            // Проходимся по всем полученным данным о позициях
            for (int i = 0; i < response.Count; i++)
            {
                List<object> position = response[i];

                // Создаем позицию на основе данных из ответа
                PositionOnBoard pos = new PositionOnBoard()
                {
                    PortfolioName = "BitfinexPortfolio",
                    SecurityNameCode = position[0].ToString(), // Код инструмента (например, "tBTCUSD")
                    ValueBlocked = 0,
                    ValueCurrent = (position[4]).ToString().ToDecimal()// Размер позиции
                };

                // Если необходимо обновить начальное значение позиции
                //if (IsUpdateValueBegin)
                //{
                //    pos.ValueBegin = Convert.ToDecimal(position[2]); // Устанавливаем начальное значение
                //}

                // Добавляем новую позицию в портфель
                portfolio.SetNewPosition(pos);
            }

            // Вызываем событие обновления портфеля с новыми данными
            PortfolioEvent(new List<Portfolio> { portfolio });
        }

        //private void CreateQueryPortfolio()
        //{
        //    _rateGatePortfolio.WaitToProceed();

        //    try
        //    {

        //        string apiPath = "v2/auth/r/wallets";

        //        string signature = $"/api/{apiPath}{nonce}";

        //        string sig = ComputeHmacSha384(_secretKey, signature);


        //         var client = new RestClient(_postUrl);

        //        var request = new RestRequest(apiPath, Method.POST);

        //        request.AddHeader("accept", "application/json");
        //        request.AddHeader("bfx-nonce", nonce);
        //        request.AddHeader("bfx-apikey", _publicKey);
        //        request.AddHeader("bfx-signature", sig);

        //        try
        //        {

        //            IRestResponse response = client.Execute(request);


        //            if (response.StatusCode == HttpStatusCode.OK)
        //            {
        //                string jsonResponse = response.Content;

        //                //"[[\"exchange\",\"TRX\",34.780002,0,34.780002,\"Trading fees for 22.0 TRX (TRXUSD) @ 0.1564 on BFX (0.2%)\",null],[\"exchange\",\"USD\",5.78503574,0,5.78503574,\"Exchange 22.0 TRX for USD @ 0.15644\",{\"reason\":\"TRADE\",\"order_id\":171007965952,\"order_id_oppo\":171011721474,\"trade_price\":\"0.15644\",\"trade_amount\":\"22.0\",\"order_cid\":1725249812080,\"order_gid\":null}]]"
        //                // List<List<object>> walletList = JsonConvert.DeserializeObject<List<List<object>>>(jsonResponse);

        //                List<JArray> walletList = JsonConvert.DeserializeObject<List<JArray>>(jsonResponse);

        //                if (walletList == null)
        //                {
        //                    SendLogMessage("walletList is null", LogMessageType.Error);
        //                    return;
        //                }


        //                List<BitfinexPortfolioRest> wallets = new List<BitfinexPortfolioRest>();


        //                for (int i = 0; i < walletList.Count; i++)
        //                {
        //                    var walletData = walletList[i];

        //                    BitfinexPortfolioRest wallet = new BitfinexPortfolioRest
        //                    {
        //                        Type = walletData[0].ToString(),
        //                        Currency = walletData[1].ToString(),
        //                        Balance = Convert.ToDecimal(walletData[2]),
        //                        UnsettledInterest = Convert.ToDecimal(walletData[3]),
        //                        AvailableBalance = Convert.ToDecimal(walletData[4]),
        //                        LastChange = walletData[5].ToString(),


        //                    };
        //                    wallets.Add(wallet);
        //                }


        //            }
        //            else
        //            {
        //                SendLogMessage("Securities request exception. Status: " + response.StatusCode, LogMessageType.Error);
        //            }

        //        }
        //        catch (Exception exception)
        //        {
        //            SendLogMessage(exception.ToString(), LogMessageType.Error);
        //        }
        //    }
        //    catch (Exception exception)
        //    {
        //        SendLogMessage(exception.ToString(), LogMessageType.Error);
        //    }
        //}

        // private void UpdatePortfolio(List<BitfinexPortfolioRest> portfolios)
        //message	"[[\"exchange\",\"TRX\",34.780002,0,34.780002,\"Trading fees for 22.0 TRX (TRXUSD) @ 0.1564 on BFX (0.2%)\",null],[\"exchange\",\"USD\",5.78503574,0,3.35843574,\"Exchange 22.0 TRX for USD @ 0.15644\",{\"reason\":\"TRADE\",\"order_id\":171007965952,\"order_id_oppo\":171011721474,\"trade_price\":\"0.15644\",\"trade_amount\":\"22.0\",\"order_cid\":1725249812080,\"order_gid\":null}]]"	string

        //private void UpdatePortfolio(string message/*bool isUpdateValueBeginList<BitfinexPortfolioRest> portfolios*/)
        //{
        //    try
        //    {
        //        var jsonDoc = JsonDocument.Parse(message);
        //        var root = jsonDoc.RootElement;

        //        List<BitfinexPortfolioRest> response = JsonConvert.DeserializeObject<List<BitfinexPortfolioRest>>(message);

        //         if (root.ValueKind == JsonValueKind.Array)
        //        {
        //            // Handle data messages
        //            int channelId = root[0].GetInt32();
        //            string msgType = root[1].GetString();

        //            if (channelId == 0)
        //            {
        //                // Wallet messages
        //                switch (msgType)
        //                {
        //                    case "ws":
        //                        SendLogMessage("Received wallet snapshot", LogMessageType.System);
        //                       // HandleWalletSnapshot(root[2]);
        //                        break;
        //                    case "wu":
        //                        SendLogMessage("Received wallet update", LogMessageType.System);
        //                       // HandleWalletUpdate(root[2], response);
        //                        break;
        //                }
        //            }
        //        }
        //// Десериализация JSON в список объектов Wallet
        //    List<BitfinexPortfolioSocket> wallets = JsonConvert.DeserializeObject<List<BitfinexPortfolioSocket>>(message);

        //    Portfolio portfolio = new Portfolio();
        //    portfolio.Number = "BitfinexPortfolio";
        //    portfolio.ValueBegin = 1;
        //    portfolio.ValueCurrent = 1;

        //    // Используем цикл for для итерации по списку кошельков
        //    for (int i = 0; i < wallets.Count; i++)
        //    {
        //    BitfinexPortfolioSocket wallet = wallets[i];

        //        PositionOnBoard pos = new PositionOnBoard()
        //        {
        //            PortfolioName = "BitfinexPortfolio", // Используем название портфеля как имя инструмента
        //            SecurityNameCode = wallet.Currency,   // Используем название валюты как код инструмента
        //            ValueBlocked = 0,  // На Bitfinex нет прямого аналога заблокированных средств в этой секции, можно оставить как 0
        //            ValueCurrent = wallet.Balance.ToDecimal(),       // Текущий баланс для данной валюты
        //        };

        //        // Если указано, обновляем начальное значение баланса
        //        //if (isUpdateValueBegin)
        //        //{
        //            pos.ValueBegin = wallet.Balance.ToDecimal();
        //        //}

        //        // Добавляем новую позицию в портфель
        //        portfolio.SetNewPosition(pos);
        //    }

        //    // Генерация события с обновленным портфелем
        //    PortfolioEvent(new List<Portfolio> { portfolio });



        /////////////////////////////////
        //Portfolio portfolio = new Portfolio
        //{
        //    Number = "Bitfinex",
        //    ValueBegin = 1,
        //    ValueCurrent = 1
        //    // ValueBegin = portfolios[0].Currency == "USD" ? portfolios[0].Balance : 0,
        //    //  ValueCurrent = portfolios[0].Currency == "USD" ? portfolios[0].AvailableBalance : 0,
        //};


        //if (portfolios == null || portfolios.Count == 0)
        //{
        //    return;
        //}


        //for (int i = 0; i < portfolios.Count; i++)
        //{
        //    if (decimal.TryParse(portfolios[i].AvailableBalance.ToString(), out decimal availableBalance) &&
        //        decimal.TryParse(portfolios[i].UnsettledInterest.ToString(), out decimal unsettledInterest))
        //    {


        //        PositionOnBoard position = new PositionOnBoard
        //        {
        //            PortfolioName = portfolios[i].Type,
        //            ValueBegin = availableBalance,
        //            ValueCurrent = availableBalance,
        //            ValueBlocked = unsettledInterest,
        //            SecurityNameCode = portfolios[i].Currency
        //        };


        //        portfolio.SetNewPosition(position);
        //    }
        //    else
        //    {
        //        SendLogMessage($"Failed to parse balance or interest for portfolio {portfolios[i].Currency}", LogMessageType.Error);
        //    }
        //}

        // PortfolioEvent?.Invoke(new List<Portfolio> { portfolio });

        //    }
        //    catch (Exception exception)
        //    {
        //        SendLogMessage($"{exception.Message}", LogMessageType.Error);
        //    }
        //}

        //private void UpdatePorfolio(string json)
        //{
        //    ResponseMessagePortfolios response = JsonConvert.DeserializeObject<ResponseMessagePortfolios>(json);
        //    Portfolio portfolio = new Portfolio
        //    {
        //        Number = "Bitfinex",
        //        ValueBegin = 1,
        //        ValueCurrent = 1

        //    };


        //    if (portfolios == null || portfolios.Count == 0)
        //    {
        //        return;
        //    }

        //    for (int i = 0; i < portfolios.Count; i++)
        //    {
        //        if (decimal.TryParse(portfolios[i].AvailableBalance.ToString(), out decimal availableBalance) &&
        //            decimal.TryParse(portfolios[i].UnsettledInterest.ToString(), out decimal unsettledInterest))
        //        {


        //            PositionOnBoard position = new PositionOnBoard
        //            {
        //                PortfolioName = portfolios[i].Type,
        //                ValueBegin = availableBalance,
        //                ValueCurrent = availableBalance,
        //                ValueBlocked = unsettledInterest,
        //                SecurityNameCode = portfolios[i].Currency
        //            };


        //            portfolio.SetNewPosition(position);
        //        }
        //        else
        //        {
        //            SendLogMessage($"Failed to parse balance or interest for portfolio {portfolios[i].Currency}", LogMessageType.Error);
        //        }
        //    }
        //    SendLogMessage($" Update Wallet Type: {walletUpdate[0]}, Currency: {walletUpdate[1]}, Balance: {walletUpdate[2]}", LogMessageType.System);
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

        public List<Candle> GetCandleHistory(string nameSec, TimeSpan tf, int CountToLoad, DateTime endTime)
        {

            int needToLoadCandles = CountToLoad;

            List<Candle> candles = new List<Candle>();
            DateTime fromTime = endTime - TimeSpan.FromMinutes(tf.TotalMinutes * CountToLoad);//перепрыгивает на сутки назад


            do
            {

                // ограничение Bitfinex: For each query, the system would return at most 1500 pieces of data. To obtain more data, please page the data by time.
                int maxCandleCountToLoad = 10000;
                int limit = Math.Min(needToLoadCandles, maxCandleCountToLoad);

                List<Candle> rangeCandles; //= new List<Candle>(); //////не нужен новый список 

                rangeCandles = CreateQueryCandles(nameSec, GetStringInterval(tf), fromTime, endTime);

                if (rangeCandles == null)
                    return null; // нет данных

                // rangeCandles.Reverse();

                candles.InsertRange(0, rangeCandles);

                if (candles.Count != 0)
                {
                    endTime = candles[0].TimeStart;
                }

                needToLoadCandles -= limit;

            }

            while (needToLoadCandles > 0);

            return candles;
        }



        public List<Candle> GetCandleDataToSecurity(Security security, TimeFrameBuilder timeFrameBuilder, DateTime startTime, DateTime endTime, DateTime actualTime)/////string
        {

            //if (startTime != actualTime)
            //{
            //    startTime = actualTime;
            //}
            if (actualTime < startTime)
            {
                startTime = actualTime;
            }

            //if (actualTime > endTime)
            //{
            //    return null;
            //}

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
            // return tf.Minutes != 0 ? $"{tf.Minutes}m" : $"{tf.Hours}h";
            if (tf.Minutes != 0)
            {
                return $"{tf.Minutes}m";
            }
            else
            {
                return $"{tf.Hours}h";
            }
        }



        // private List<Candle> CreateQueryCandles(string nameSec, string tf, DateTime timeFrom, DateTime timeTo)//////////TimeSpan interval

        private List<Candle> CreateQueryCandles(string nameSec, string tf, DateTime timeFrom, DateTime timeTo)
        {


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





            //RestClient client = new RestClient($"https://api-pub.bitfinex.com/v2/candles/trade:{1m}:{nameSec}/hist?start={startTime}&end={endTime}");//////TF 

            //var request = new RestRequest("", Method.GET);//поменяла строки

            ////var request = new RestRequest("");
            ///
            //string section = timeFrom !=DateTime.Today ?"hist":"last";////////////

            //string section = startTime != DateTime.Today ? "hist" : "last";



            //string candle = $"trade:30m:{nameSec}";

            string candle = $"trade:{tf}:{nameSec}";


            apiPath = $"/v2/candles/{candle}/hist";//?start={startTime}&end={endTime}";

            // // Создаем клиента RestSharp
            var client = new RestClient(_getUrl);

            // Создаем запрос типа POST
            var request = new RestRequest(apiPath, Method.GET);

            // Добавляем заголовки
            request.AddHeader("accept", "application/json");


            try
            {
                // Отправляем запрос и получаем ответ
                IRestResponse response = client.Execute(request);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    // Выводим тело ответа
                    string jsonResponse = response.Content;

                    // List<List<BitfinexCandle>> candles = JsonConvert.DeserializeObject<List<List<BitfinexCandle>>>(jsonResponse);//////////////////////////////
                    //List<object> candles = JsonConvert.DeserializeObject<List<object>>(jsonResponse);

                    List<List<object>> candles = JsonConvert.DeserializeObject<List<List<object>>>(jsonResponse);

                    if (candles == null || candles.Count == 0)
                    {
                        return null;
                    }

                    List<BitfinexCandle> candleList = new List<BitfinexCandle>();

                    for (int i = 0; i < candles.Count; i++) ////////////////////////
                                                            //for (int i = 0; i < 2; i++)
                    {
                        var candleData = candles[i];// Получаем данные по текущей свече.

                        // var candleData = (List<object>)candles[i];

                        if (candles[i] == null || candles.Count < 6)
                        {
                            SendLogMessage("Candle data is incomplete", LogMessageType.Error);
                            continue;
                        }

                        if (candleData == null)
                        //if (candleData[0] == null || candleData[1] == null ||
                        //    candleData[2] == null ||  candleData[3] == null ||
                        //    candleData[4] == null || candleData[5] == null)

                        {
                            SendLogMessage("Candle data contains null values", LogMessageType.Error);

                            continue;
                        }
                        //// .ToString().ToDecimal()
                        //if (Convert.ToDecimal(candleData[1]) == 0 ||
                        //    Convert.ToDecimal(candleData[2]) == 0 ||
                        //    Convert.ToDecimal(candleData[3]) == 0 ||
                        //    Convert.ToDecimal(candleData[4]) == 0 ||
                        //    Convert.ToDecimal(candleData[5]) == 0)
                        //{
                        //    SendLogMessage("Candle data contains zero values", LogMessageType.Error);

                        //    continue;
                        //}

                        // Создание объекта класса BitfinexCandle и присвоение значений полям

                        BitfinexCandle newCandle = new BitfinexCandle

                        {
                            Mts = candleData[0].ToString(),
                            Open = candleData[1].ToString(),
                            Close = candleData[2].ToString(),
                            High = candleData[3].ToString(),
                            Low = candleData[4].ToString(),
                            Volume = candleData[5].ToString()


                            //// Преобразование данных в нужные типы
                            //Mts = (candleData[0]),
                            //Open = Convert.ToDecimal(candleData[1]).ToString(),
                            //Close = Convert.ToDecimal(candleData[2]).ToString(),
                            //High = Convert.ToDecimal(candleData[3]).ToString(),
                            //Low = Convert.ToDecimal(candleData[4]).ToString(),
                            //Volume = Convert.ToDecimal(candleData[5]).ToString()

                        };


                        candleList.Add(newCandle); // Добавляем объект свечи в список.
                    }

                    return ConvertToCandles(candleList); // Преобразуем список BitfinexCandle в список Candle.
                }

            }
            catch
            {
                { SendLogMessage("Http request error", LogMessageType.Error); }/////////
            }
            // Возвращаем пустой список в случае ошибки.
            return null;
        }

        private List<Candle> ConvertToCandles(List<BitfinexCandle> candleList)
        {
            List<Candle> candles = new List<Candle>();// Список для хранения окончательных свечей.

            try
            {
                for (int i = 0; i < candleList.Count; i++)
                {
                    var candle = candleList[i];// Получаем текущую свечу из списка.

                    try
                    {

                        if (string.IsNullOrEmpty(candle.Mts) || string.IsNullOrEmpty(candle.Open) ||
                            string.IsNullOrEmpty(candle.Close) || string.IsNullOrEmpty(candle.High) ||
                            string.IsNullOrEmpty(candle.Low) || string.IsNullOrEmpty(candle.Volume))
                        {
                            SendLogMessage("Candle data contains null or empty values", LogMessageType.Error);
                            continue;
                        }

                        if (Convert.ToDecimal(candle.Open) == 0 || Convert.ToDecimal(candle.Close) == 0 ||
                            Convert.ToDecimal(candle.High) == 0 || Convert.ToDecimal(candle.Low) == 0 ||
                            Convert.ToDecimal(candle.Volume) == 0)
                        {
                            SendLogMessage("Candle data contains zero values", LogMessageType.Error);
                            continue;
                        }

                        Candle newCandle = new Candle
                        {
                            State = CandleState.Finished,
                            TimeStart = TimeManager.GetDateTimeFromTimeStamp(Convert.ToInt64(candle.Mts)),
                            Open = (candle.Open).ToString().ToDecimal(),
                            Close = (candle.Close).ToString().ToDecimal(),
                            High = (candle.High).ToString().ToDecimal(),
                            Low = (candle.Low).ToString().ToDecimal(),
                            Volume = (candle.Volume).ToString().ToDecimal()


                        };

                        candles.Add(newCandle);
                    }
                    catch (FormatException exception)
                    {
                        SendLogMessage($"Format exception: {exception.Message}", LogMessageType.Error);
                    }
                }
                candles.Reverse();//////
                return candles;// Возвращаем окончательный список свечей.
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
                return null;
            }
        }

        #endregion

        /// <summary>
        /// Создание вебсокет соединения. 
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

        public event Action<MarketDepth> MarketDepthEvent;/////////где



        #endregion


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
            SendLogMessage("Websocket public Bitfinex Opened", LogMessageType.System);

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
                SendLogMessage("WebSocket Public сlosed by Bitfinex.", LogMessageType.Error);


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


                // marketDepth.Time = DateTime.UtcNow;

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

                //// Присваиваем отсортированные списки ask и bid уровней ордербуку
                marketDepth.Asks = asks;
                marketDepth.Bids = bids;

                // Сортировка bid по убыванию (сначала наибольшие цены)
                marketDepth.Bids.Sort((x, y) => y.Price.CompareTo(x.Price));

                // Сортировка ask по возрастанию (сначала наименьшие цены)
                marketDepth.Asks.Sort((x, y) => x.Price.CompareTo(y.Price));

                //marketDepth.Time = TimeManager.GetDateTimeFromTimeStamp(Convert.ToInt64());

                marketDepth.Time = DateTime.UtcNow;

                if (marketDepth.Asks.Count == 0 ||
                    marketDepth.Bids.Count == 0)
                {
                    return;
                }

            }
            MarketDepthEvent(marketDepth);
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
                SendLogMessage("Connection Closed by Bitfinex. WebSocket Private сlosed ", LogMessageType.Error);

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


                            if (messageTypeString == "te")
                            {
                                ProcessTradeResponse(message);

                            }
                            if (messageTypeString == "tu")
                            {
                                UpdateTrade(message);
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
                    //UpdateTrade(trade);
                    UpdateTrade(message);

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


        private void ProcessSnapshot(List<BitfinexTradeUpdate> tradeList)////////
        {
            //////дописать логику обработки снимков трейдов
            // Логика обработки снимка трейдов
            for (int i = 0; i < tradeList.Count; i++)
            {
                // Извлечение текущего трейда из списка
                var trade = tradeList[i];

                trade.Amount = tradeList[i].Amount;
                trade.Price = tradeList[i].Price;
                trade.Timestamp = tradeList[i].Timestamp;
                trade.Id = tradeList[i].Id;
                // Пример обработки каждого трейда в снимке
                SendLogMessage($"Обработка трейда: ID = {trade.Id}, Timestamp = {trade.Timestamp}, Amount = {trade.Amount}, Price = {trade.Price}", LogMessageType.System);
            }
        }
        private void UpdateTrade(string jsonMessage)
        {
            // Логика обновления трейда

            try
            {
                // Парсим полученные данные из JSON
                var document = JsonDocument.Parse(jsonMessage);
                var root = document.RootElement;

                // Проверка на валидность формата сообщения
                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 2)
                {
                    return;
                }

                // Получение ID канала и данных
                var channelId = root[0].GetInt32();
                var data = root[1];

                // Если пришло сообщение об обновлении трейдов
                if (data.ValueKind == JsonValueKind.Array && data.GetArrayLength() >= 4)
                {

                    var tradeId = data[0].GetInt64(); // ID сделки
                    var timestamp = data[1].GetInt64(); // Временная метка сделки
                    var amount = data[2].GetDecimal(); // Объем сделки
                    var price = data[3].GetDecimal(); // Цена сделки


                    // Создание объекта для хранения информации о сделке
                    Trade newTrade = new Trade
                    {
                        SecurityNameCode = _currentSymbol,
                        Id = tradeId.ToString(),
                        Price = price,
                        Volume = Math.Abs(amount), // Объем может быть отрицательным для продаж
                        Side = amount > 0 ? Side.Buy : Side.Sell, // Определение стороны сделки
                        Time = TimeManager.GetDateTimeFromTimeStamp(timestamp)// дата и время

                    };

                    ServerTime = newTrade.Time;

                    NewTradesEvent?.Invoke(newTrade);
                }
            }
            catch (Exception ex)
            {
                SendLogMessage("Error in trade update: " + ex.Message, LogMessageType.Error);
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

                    //if(message.Contains("CHAN_ID = 0") chanelId == 0)

                    if (message.Contains("ou"))
                    {
                        UpdateOrder(message);
                        // continue;
                    }
                    if (message.Contains("tu"))
                    {
                        UpdateMyTrade(message);
                        //continue;
                    }
                    if (message.Contains("ws"))
                    {
                        UpdatePortfolio(message);
                    }

                }
                catch (Exception exception)
                {

                    SendLogMessage(exception.ToString(), LogMessageType.Error);

                }
            }
        }

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
                    NumberOrderParent = response.Cid,//что тут должно быть
                    Price = (response.OrderPrice).ToDecimal(),
                    NumberTrade = response.OrderId,//что тут должно быт
                    Side = response.Amount.Contains("-") ? Side.Sell : Side.Buy,
                    // Side = response.Amount > 0 ? Side.Buy : Side.Sell;
                    // Volume = (response.Amount).ToString().ToDecimal(),

                };


                // при покупке комиссия берется с монеты и объем уменьшается и появляются лишние знаки после запятой
                decimal preVolume = myTrade.Side == Side.Sell ? response.Amount.ToDecimal() : response.Amount.ToDecimal() - response.Fee.ToDecimal();

                myTrade.Volume = GetVolumeForMyTrade(response.Symbol, preVolume);


                MyTradeEvent?.Invoke(myTrade);

                SendLogMessage(myTrade.ToString(), LogMessageType.Trade);////надо или нет

            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }

        //округление объемом
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
                // Десериализация сообщения в объект BitfinexOrderData
                var response = JsonConvert.DeserializeObject<List<BitfinexOrderData>>(message);

                if (response == null || response.Count == 0)
                {
                    return;
                }

                // Перебор всех ордеров в ответе
                for (int i = 0; i < response.Count; i++)
                {
                    var orderData = response[i];

                    if (string.IsNullOrEmpty(orderData.Cid))
                    {
                        continue; // Пропускаем ордера без Cid ИЛИ RETURN?
                    }

                    // Определение состояния ордера
                    OrderStateType stateType = GetOrderState(orderData.Status);

                    // Игнорируем ордера типа "EXCHANGE MARKET" и активные
                    if (orderData.OrderType.Equals("EXCHANGE MARKET", StringComparison.OrdinalIgnoreCase) && stateType == OrderStateType.Activ)
                    {
                        continue;
                    }

                    // Создаем новый объект ордера
                    var updateOrder = new Order
                    {
                        SecurityNameCode = orderData.Symbol,
                        TimeCallBack = TimeManager.GetDateTimeFromTimeStamp(Convert.ToInt64(orderData.MtsCreate)),
                        NumberUser = Convert.ToInt32(orderData.Cid),
                        NumberMarket = orderData.Id,
                        Side = orderData.Amount.Equals("-") ? Side.Sell : Side.Buy, // Продаем, если количество отрицательное
                        State = stateType,
                        TypeOrder = orderData.OrderType.Equals("EXCHANGE MARKET", StringComparison.OrdinalIgnoreCase) ? OrderPriceType.Market : OrderPriceType.Limit,
                        Volume = Math.Abs(orderData.Amount.ToDecimal()), // Абсолютное значение объема
                        Price = orderData.Price.ToDecimal(),
                        ServerType = ServerType.Bitfinex,
                        PortfolioNumber = "BitfinexPortfolio"
                    };

                    // Если ордер исполнен или частично исполнен, обновляем сделку
                    if (stateType == OrderStateType.Done || stateType == OrderStateType.Patrial)// Partial  
                    {
                        UpdateMyTrade(message);
                    }

                    // Вызываем событие для обновленного ордера
                    MyOrderEvent?.Invoke(updateOrder);
                }
            }
            catch (Exception exception)
            {
                // Логируем ошибку
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }
        }
        private OrderStateType GetOrderState(string orderStateResponse)
        {
            OrderStateType stateType;

            switch (orderStateResponse)
            {
                case ("ACTIVE"):
                    stateType = OrderStateType.Activ;
                    break;
                case ("EXECUTED"):
                    stateType = OrderStateType.Done;
                    break;
                case ("REJECTED"):
                    stateType = OrderStateType.Fail;
                    break;
                case ("CANCELED"):
                    stateType = OrderStateType.Cancel;
                    break;
                case "PARTIALLY FILLED":
                    stateType = OrderStateType.Patrial;
                    break;
                default:
                    stateType = OrderStateType.None;
                    break;
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

            apiPath = "v2/auth/w/order/submit";
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
            string bodyJson = JsonSerializer.Serialize(body);

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

                    // Десериализация ответа как массив объектов

                    // string responseBody = "[1723557977,\"on-req\",null,null,[[167966185075,null,1723557977011,\"tTRXUSD\",1723557977011,1723557977011,22,22,\"EXCHANGE LIMIT\",null,null,null,0,\"ACTIVE\",null,null,0.12948,0,0,0,null,null,null,0,0,null,null,null,\"API>BFX\",null,null,{}]],null,\"SUCCESS\",\"Submitting 1 orders.\"]";

                    // Десериализация верхнего уровня в список объектов
                    var responseArray = JsonConvert.DeserializeObject<List<object>>(responseBody);

                    // Проверка размера массива перед извлечением значений
                    if (responseArray == null)
                    {
                        return;
                    }
                    //

                    // Парсим JSON-строку в JArray (массив JSON)
                    JArray jsonArray = JArray.Parse(responseBody);

                    //// Путь к статусу "ACTIVE" в JSON структуре
                    //string status = (string)jsonArray[4][0][13];

                    if (responseArray.Contains("on-req"))
                    {
                        // Извлечение нужных элементов
                        var dataJson = responseArray[4].ToString();
                        string status = (string)jsonArray[4][0][13];

                        //string status = responseArray[6].ToString();
                        string text = responseArray[7].ToString();

                        // Десериализация dataJson в список заказов
                        var ordersArray = JsonConvert.DeserializeObject<List<List<object>>>(dataJson);
                        var orders = ordersArray[0]; // Получаем первый заказ из массива

                        // Создание объекта BitfinexOrderData
                        BitfinexOrderData orderData = new BitfinexOrderData
                        {
                            //Cid = Convert.ToString(orders[2]),
                            Id = Convert.ToString(orders[0]),
                            Symbol = Convert.ToString(orders[3]),
                        };


                        ////order.State = OrderStateType.Active///////////надо или нет
                        ////order.NumberMarket = orderData.Id;  //надо или нет

                        //Order order = ConvertToOsEngineOrder(orderData, PortfolioName);

                        SendLogMessage($"Order num {order.NumberMarket} on exchange.{order.State},{text}", LogMessageType.Trade);

                    }
                }
                else
                {
                    CreateOrderFail(order);
                    SendLogMessage($"Error Status code {response.StatusCode}: {responseBody}", LogMessageType.Error);

                }

                MyOrderEvent?.Invoke(order);

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
            rateGateCancelAllOrder.WaitToProceed();

            apiPath = "v2/auth/w/order/cancel/multi";
            var body = new
            {
                all = 1  //1 отменить все ордера Идентификатор ордера для отмены
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

            request.AddJsonBody(body);

            IRestResponse response = client.Execute(request);

            try
            {
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    if (response == null) { return; }

                    // Десериализация верхнего уровня в список объектов
                    var responseJson = JsonConvert.DeserializeObject<List<object>>(response.Content);

                    if (responseJson.Contains("oc_multi-req"))
                    {

                        SendLogMessage($"All active orders canceled: {response.StatusCode}", LogMessageType.Trade);//LogMessageType.Error

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




        public void CancelOrder(Order order)
        {
            _rateGateCancelOrder.WaitToProceed();

            if (OrderStateType.Cancel == order.State)//если ордер активный можно снять
            {
                return;
            }

            long orderId = Convert.ToInt64(order.NumberMarket);

            // Формирование тела запроса с указанием ID ордера
            var body = new
            {
                //id = order.NumberMarket  // Идентификатор ордера для отмены
                id = order.NumberMarket////надо или нет
                //id = order
            };
            apiPath = "v2/auth/w/order/cancel";
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
            // [0,"n",[1575291219660,"oc-req",null,null,[1185815100,null,1575289350475,"tETHUSD",1575289351944,1575289447644,-3,-3,"LIMIT","LIMIT",null,null,0,"ACTIVE",null,null,240,0,0,0,null,null,null,0,0,null,null,null,"API>BFX",null,null,null],null,"SUCCESS","Submitted for cancellation; waiting for confirmation (ID: 1185815100)."]]

            //[0,"oc",[1185815100,null,1575289350475,"tETHUSD",1575289351944,1575291219663,-3,-3,"LIMIT","LIMIT",null,null,0,"CANCELED",null,null,240,0,0,0,null,null,null,0,0,null,null,null,"API>BFX",null,null,null]]
            {
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var responseJson = JsonConvert.DeserializeObject<List<object>>(responseBody);


                    if (responseJson.Contains("oc-req"))
                    //if (responseJson.Contains("CANCELED"))
                    {

                        SendLogMessage("Order canceled successfully. Order ID: " + order.NumberMarket, LogMessageType.Trade);
                        order.State = OrderStateType.Cancel;// надо или нет
                        MyOrderEvent(order);////// надо или нет
                    }

                }

                else
                {
                    //order.State = OrderStateType.Cancel;
                    CreateOrderFail(order);
                    SendLogMessage($"Order cancellation error: code - {response.StatusCode} - {response.Content}, {response.ErrorMessage}", LogMessageType.Error);
                }

                MyOrderEvent(order);


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

                apiPath = "v2/auth/w/order/update";

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
                    order.Price = newPrice;

                    SendLogMessage("Order change price. New price: " + newPrice
                      + "  " + order.SecurityNameCode, LogMessageType.Trade);//LogMessageType.System

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
                // Вызов события изменения ордера
                MyOrderEvent?.Invoke(order);
            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);

            }
        }


        #endregion

        /// <summary>
        /// Место расположение HTTP запросов.
        /// </summary>_httpPublicClient
        #region  12 Queries

        //private string _portfolioType;
        //// post https://api.bitfinex.com/v2/auth/r/orders

        //private Order ConvertToOsEngineOrder(BitfinexOrderData baseMessage, string portfolioName)
        //{
        //    Order order = new Order();

        //    // Установка кода инструмента
        //    order.SecurityNameCode = baseMessage.Symbol;

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





        //    // Установка объема ордера
        //    if (baseMessage.Amount.ToDecimal() > 0)
        //    {
        //        order.Volume = baseMessage.Amount.ToDecimal();
        //    }
        //    else
        //    {
        //        order.Volume = baseMessage.AmountOrig.ToDecimal();
        //    }

        //    // Установка номера портфеля
        //    order.PortfolioNumber = portfolioName;

        //    // Установка типа ордера (лимитный или рыночный)
        //    if (baseMessage.OrderType == "EXCHANGE LIMIT")
        //    {
        //        order.Price = baseMessage.Price.ToDecimal();
        //        order.TypeOrder = OrderPriceType.Limit;
        //    }
        //    else if (baseMessage.OrderType == "EXCHANGE MARKET")
        //    {
        //        order.TypeOrder = OrderPriceType.Market;
        //    }

        //    // Установка пользовательского номера ордера (если применимо)
        //    try
        //    {
        //        order.NumberUser = Convert.ToInt32(baseMessage.Cid);
        //    }
        //    catch
        //    {
        //        // Игнорируем ошибку, если не удается преобразовать номер
        //    }

        //    // Установка номера ордера на бирже
        //    order.NumberMarket = baseMessage.Id;

        //    // Установка времени обратного вызова (время транзакции)
        //    order.TimeCallBack = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(baseMessage.MtsUpdate)).UtcDateTime;

        //    // Установка направления ордера (покупка или продажа)
        //    if (baseMessage.OrderType == "BUY")
        //    {
        //        order.Side = Side.Buy;
        //    }
        //    else
        //    {
        //        order.Side = Side.Sell;
        //    }

        //    // Установка состояния ордера на основе статуса
        //    // "ACTIVE" - Активен, "EXECUTED" - Исполнен, "CANCELED" - Отменен, "REJECTED" - Отклонен
        //    switch (baseMessage.Status)
        //    {
        //        case "ACTIVE":
        //            order.State = OrderStateType.Activ;
        //            break;
        //        case "EXECUTED":
        //            order.State = OrderStateType.Done;
        //            break;
        //        case "CANCELED":
        //            order.State = OrderStateType.Cancel;
        //            break;
        //        case "REJECTED":
        //            order.State = OrderStateType.Fail;
        //            break;
        //        default:
        //            order.State = OrderStateType.None;
        //            break;
        //    }

        //    return order;
        //}

        //private RateGate rateGateAllOrders = new RateGate(1, TimeSpan.FromMilliseconds(350));


        //private List<Order> GetAllOrdersFromExchangeByPortfolio(string portfolio)
        //{
        //    rateGateAllOrders.WaitToProceed();


        //    // string apiPath = "/v2/auth/r/orders/t" + portfolio + "/hist";



        //    // Сериализуем объект тела в JSON

        //    //  string bodyJson = JsonConvert.SerializeObject(body);
        //    //string bodyJson = System.Text.Json.JsonSerializer.Serialize(body);
        //    // Создаем nonce как текущее время в миллисекундах
        //    string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();

        //    string apiPath = "/v2/auth/r/orders";

        //    // Создаем строку для подписи
        //    //string signature = $"/api/{apiPath}{nonce}{bodyJson}";
        //    string signature = $"/api/{apiPath}{nonce}";

        //    // Вычисляем подпись с использованием HMACSHA384
        //    string sig = ComputeHmacSha384(_secretKey, signature);

        //    // // Создаем клиента RestSharp
        //    var client = new RestClient(_postUrl);

        //    // Создаем запрос типа POST
        //    var request = new RestRequest(apiPath, Method.POST);

        //    // Добавляем заголовки
        //    request.AddHeader("accept", "application/json");
        //    request.AddHeader("bfx-nonce", nonce);
        //    request.AddHeader("bfx-apikey", _publicKey);
        //    request.AddHeader("bfx-signature", sig);

        //    // Добавляем тело запроса в формате JSON
        //   // request.AddJsonBody(body); //


        //    try
        //    {
        //        // Отправляем запрос и получаем ответ
        //        var response = client.Execute(request);

        //        // Выводим тело ответа
        //        string responseBody = response.Content;

        //        if (response.StatusCode == HttpStatusCode.OK)
        //        {

        //            if (string.IsNullOrEmpty(responseBody) /*|| responseBody == "[]"*/)
        //            {
        //                return null; // Если ответ пустой, возвращаем null
        //            }
        //            else
        //            {
        //                // Десериализация ответа
        //                List<BitfinexOrderData> activeOrders = JsonSerializer.Deserialize<List<BitfinexOrderData>>(responseBody);

        //                List<Order> osEngineOrders = new List<Order>();

        //                // Конвертация ордеров из формата Bitfinex в формат OsEngine
        //                for (int i = 0; i < activeOrders.Count; i++)
        //                {
        //                    Order newOrd = ConvertToOsEngineOrder(activeOrders[i], portfolio);

        //                    if (newOrd == null)
        //                    {
        //                        continue;
        //                    }

        //                    osEngineOrders.Add(newOrd);
        //                }

        //                return osEngineOrders; // Возвращаем список ордеров
        //            }
        //        }
        //        else if (response.StatusCode == HttpStatusCode.NotFound)
        //        {
        //            return null; // Если ордеры не найдены, возвращаем null
        //        }
        //        else
        //        {
        //            // Логируем ошибку при запросе
        //            SendLogMessage("Get all orders request error. ", LogMessageType.Error);

        //            if (!string.IsNullOrEmpty(response.Content))
        //            {
        //                SendLogMessage("Fail reasons: " + response.Content, LogMessageType.Error);
        //            }
        //        }
        //    }
        //    catch (Exception exception)
        //    {
        //        // Логируем ошибку выполнения запроса
        //        SendLogMessage("Get all orders request error." + exception.ToString(), LogMessageType.Error);
        //    }

        //    return null;
        //}



        //private List<Order> GetAllOrdersFromExchange()////////////////////1111111111111
        //{
        //    List<Order> orders = new List<Order>();

        //    if (string.IsNullOrEmpty(_portfolioType) == false)
        //    {
        //        List<Order> newOrders = GetAllOrdersFromExchangeByPortfolio(_portfolioType);

        //        if (newOrders != null &&
        //            newOrders.Count > 0)
        //        {
        //            orders.AddRange(newOrders);
        //        }
        //    }
        //    return orders;
        //}

        public void CancelAllOrdersToSecurity(Security security)
        {
            throw new NotImplementedException();
        }





        public List<Order> GetAllOrdersFromExchange()
        {
            // post https://api.bitfinex.com/v2/auth/r/orders

            List<Order> orders = new List<Order>();


            // Логика получения данных и заполнения orderFromExchange


            apiPath = "v2/auth/r/orders";


            // Создаем nonce как текущее время в миллисекундах
            string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();

            // Создаем строку для подписи
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

            try
            { // Отправляем запрос и получаем ответ
                var response = client.Execute(request);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    // Выводим тело ответа
                    string responseBody = response.Content;
                    // Десериализация полученных данных
                    //var listOrders = JsonConvert.DeserializeObject<List<List<BitfinexOrderData>>>(response.Content);
                    var listOrders = JsonConvert.DeserializeObject<List<List<object>>>(response.Content);
                    //  доступ к первому элементу


                    var activeOrders = new List<BitfinexOrderData>();

                    if (orders != null && orders.Count > 0)
                    {
                        for (int i = 0; i < orders.Count; i++)
                        {
                            Order newOrder = new Order();

                            newOrder.TimeCallBack = TimeManager.GetDateTimeFromTimeStamp(long.Parse(activeOrders[i].MtsUpdate));
                            newOrder.TimeCreate = TimeManager.GetDateTimeFromTimeStamp(long.Parse(activeOrders[i].MtsCreate));
                            newOrder.ServerType = ServerType.Bitfinex;
                            newOrder.SecurityNameCode = activeOrders[i].Symbol;

                            newOrder.NumberUser = Convert.ToInt32(activeOrders[i].Cid);/////

                            newOrder.NumberMarket = activeOrders[i].Id;
                            newOrder.Side = activeOrders[i].Amount.Equals("-") ? Side.Sell : Side.Buy;
                            newOrder.State = GetOrderState(activeOrders[i].Status);
                            newOrder.Volume = activeOrders[i].Amount.ToDecimal();
                            newOrder.Price = activeOrders[i].Price.ToDecimal();
                            newOrder.PortfolioNumber = "BitfinexPortfolio";

                            orders.Add(newOrder);

                            //allOrders[i].TimeCreate = allOrders[i].TimeCallBack;

                            // MyOrderEvent?.Invoke(allOrders[i]);


                        }

                    }

                }
                else
                {

                    SendLogMessage($" Can't get all orders. Http State Code: {response.Content}", LogMessageType.Error);
                }

            }
            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }

            return orders;  // Возвращаем найденный ордер

        }

        //public void GetOrderStatus(Order order)
        //{
        //    throw new NotImplementedException();
        //}
        public void GetOrderStatus(Order order)
        {
            // Получаем ордер с биржи по рыночному номеру ордера
            Order orderFromExchange = GetOrderFromExchange(order.NumberMarket);

            // Если ордер не найден, выходим из метода
            if (orderFromExchange == null)
            {
                return;
            }

            // Объявляем переменную для хранения ордера на рынке
            Order orderOnMarket = null;

            // Если пользовательский номер ордера (NumberUser) и номер ордера с биржи (NumberUser) совпадают, сохраняем ордер с биржи
            if (order.NumberUser != 0 && orderFromExchange.NumberUser != 0 && orderFromExchange.NumberUser == order.NumberUser)
            {
                orderOnMarket = orderFromExchange;
            }

            // Если рыночный номер ордера (NumberMarket) совпадает, также сохраняем ордер с биржи
            if (!string.IsNullOrEmpty(order.NumberMarket) && order.NumberMarket == orderFromExchange.NumberMarket)
            {
                orderOnMarket = orderFromExchange;
            }

            // Если ордер на рынке не найден, выходим из метода
            if (orderOnMarket == null)
            {
                return;
            }

            // Если ордер на рынке найден и существует обработчик события, вызываем событие MyOrderEvent
            if (orderOnMarket != null && MyOrderEvent != null)
            {
                MyOrderEvent(orderOnMarket);
            }

            // Проверяем состояние ордера: если ордер выполнен (Done) или частично выполнен (Patrial)
            if (orderOnMarket.State == OrderStateType.Done || orderOnMarket.State == OrderStateType.Patrial)
            {
                // Получаем список сделок по номеру ордера
                List<MyTrade> tradesBySecurity = GetMyTradesBySecurity(order.SecurityNameCode, order.NumberMarket);

                // Если сделки не найдены, выходим из метода
                if (tradesBySecurity == null)
                {
                    return;
                }

                // Объявляем список для хранения сделок, связанных с данным ордером
                List<MyTrade> tradesByMyOrder = new List<MyTrade>();

                // Используем цикл for для перебора всех сделок в списке tradesBySecurity
                for (int i = 0; i < tradesBySecurity.Count; i++)
                {
                    // Если сделка связана с данным ордером (по совпадению родительского номера ордера), добавляем её в список
                    if (tradesBySecurity[i].NumberOrderParent == orderOnMarket.NumberMarket)
                    {
                        tradesByMyOrder.Add(tradesBySecurity[i]);
                    }
                }

                // Используем цикл for для обработки всех найденных сделок по ордеру
                for (int i = 0; i < tradesByMyOrder.Count; i++)
                {
                    // Если существует обработчик события MyTradeEvent, вызываем его для каждой сделки
                    if (MyTradeEvent != null)
                    {
                        MyTradeEvent(tradesByMyOrder[i]);
                    }
                }
            }
        }

        private Order GetOrderFromExchange(string numberMarket)
        {
            Order newOrder = new Order();

            apiPath = "v2/auth/r/orders";////////////
            var body = new
            {
                id = numberMarket

            };


            // Сериализуем объект тела в JSON

            string bodyJson = JsonConvert.SerializeObject(body);

            // Отправляем запрос на сервер Bitfinex и получаем ответ
            var response = ExecuteRequest(apiPath, bodyJson);

            // Выводим тело ответа
            string responseBody = response.Content;

            // Десериализуем ответ
            var response1 = JsonConvert.DeserializeObject<List<List<object>>>(responseBody);
            // Если ответ успешный, обновляем портфель и запрашиваем позиции
            List<BitfinexOrderData> listOrder = new List<BitfinexOrderData>();
            try
            {
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    for (int i = 0; i < listOrder.Count; i++)
                    {
                        var order = listOrder[i];


                        // Заполняем данные ордера
                        newOrder.TimeCallBack = TimeManager.GetDateTimeFromTimeStamp(Convert.ToInt64(order.MtsUpdate));
                        newOrder.TimeCreate = TimeManager.GetDateTimeFromTimeStamp(Convert.ToInt64(order.MtsCreate));
                        newOrder.ServerType = ServerType.Bitfinex;
                        newOrder.SecurityNameCode = order.Symbol;
                        newOrder.NumberMarket = numberMarket;
                        newOrder.Side = (order.Amount).Equals("-") ? Side.Sell : Side.Buy;////// или 
                        newOrder.State = OrderStateType.Activ; // Статус ордера (например, активный)
                        newOrder.Volume = (order.Amount).ToDecimal();
                        newOrder.Price = (order.Price).ToDecimal();
                        newOrder.PortfolioNumber = "BitfinexPortfolio";
                    }
                }
                else
                {
                    SendLogMessage($"GetOrderState. Http State Code: {response.Content}", LogMessageType.Error);
                }

            }

            catch (Exception exception)
            {
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }

            return newOrder;

        }




        private List<MyTrade> GetMyTradesBySecurity(string security, string orderId)
        {


       // https://api.bitfinex.com/v2/auth/r/trades/{symbol}/hist

            apiPath = "v2/auth/r/trades/{symbol}";////////////
            var body = new
            {
                id = orderId,
                symbol = security
            };


            // Сериализуем объект тела в JSON

            string bodyJson = JsonConvert.SerializeObject(body);

            try
            {
                // Отправляем запрос на сервер Bitfinex и получаем ответ
                var response = ExecuteRequest(apiPath, bodyJson);

                // Выводим тело ответа
                string responseBody = response.Content;

                // Если ответ успешный, обновляем портфель и запрашиваем позиции
                if (response.StatusCode == HttpStatusCode.OK)
                {

                    CreateQueryPosition(); // Обновляем позиции
                }
                else
                {
                    // Логируем ошибку, если запрос не удался
                    SendLogMessage($" {response.StatusCode}{responseBody}", LogMessageType.Error);
                }
                return new List<MyTrade>();   // Возвращаем пустой список (пример
            }
            catch (Exception exception)
            {
                // Логируем исключение в случае ошибки
                SendLogMessage(exception.ToString(), LogMessageType.Error);
            }


            return null;
        }

        public List<Candle> GetCandleDataToSecurity(string security, TimeFrameBuilder timeFrameBuilder, DateTime startTime, DateTime endTime, DateTime actualTime)
        {
            throw new NotImplementedException();
        }




        // Метод для выполнения запросов GET или POST в зависимости от параметра
        public IRestResponse ExecuteRequest(string apiPath, string body = null)
        {
            string method;
            // Генерация уникального идентификатора запроса (nonce)
            string nonce = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000).ToString();

            // Если apiPath содержит слово "auth", то устанавливаем метод как POST
            if (apiPath.ToLower().Contains("auth"))
            {
                method = "POST";
            }
            else
            {
                // Иначе метод будет GET
                method = "GET";
            }

            //if (body != null)
            //{
            //    body = JsonSerializer.Serialize(body);////это правильно или нет
            //}
            // Формирование строки для подписи в зависимости от типа запроса
            string signature = method.Equals("GET", StringComparison.OrdinalIgnoreCase) ?
                $"/api/{apiPath}{nonce}" :
                $"/api/{apiPath}{nonce}{body}";


            // Генерация подписи HMAC-SHA384
            string sig = ComputeHmacSha384(_secretKey, signature);


            // Создаем клиента RestSharp
            var client = new RestClient(_baseUrl);

            // Выбираем тип запроса
            RestRequest request = new RestRequest(apiPath, method.Equals("GET", StringComparison.OrdinalIgnoreCase) ? Method.GET : Method.POST);

            // Добавляем заголовки
            request.AddHeader("accept", "application/json");
            request.AddHeader("bfx-nonce", nonce);
            request.AddHeader("bfx-apikey", _publicKey);
            request.AddHeader("bfx-signature", sig);

            // Если это POST запрос, добавляем тело запроса
            if (method.Equals("POST", StringComparison.OrdinalIgnoreCase) && body != null)
            {
                request.AddJsonBody(body);
            }

            // Выполняем запрос и возвращаем ответ
            return client.Execute(request);
        }

        #endregion
        /// <summary>
        /// Логирование.
        /// </summary>
        #region 13 Log

        public event Action<string, LogMessageType> LogMessageEvent;


        private void SendLogMessage(string message, LogMessageType messageType)
        {
            LogMessageEvent(message, messageType);
        }



        public void GetAllActivOrders()
        {

            List<Order> orders = GetAllOrdersFromExchange();

            for (int i = 0; orders != null && i < orders.Count; i++)
            {
                if (orders[i] == null)
                {
                    continue;
                }

                //if (orders[i].State != OrderStateType.Activ
                //    && orders[i].State != OrderStateType.Patrial
                //    && orders[i].State != OrderStateType.Pending)
                //{
                //    continue;
                //}

                orders[i].TimeCreate = orders[i].TimeCallBack;

                if (MyOrderEvent != null)
                {
                    MyOrderEvent(orders[i]);
                }
            }
        }


        //public void UnsubscribeFromChannel(int channelId)//отписаться от канала
        //{
        //    var unsubscribeMessage = new
        //    {
        //        @event = "unsubscribe",
        //        chanId = channelId
        //    };

        //    string message = JsonConvert.SerializeObject(unsubscribeMessage);

        //    // Отправка сообщения через WebSocket
        //    webSocket.Send(message);
        //}


        #endregion


    }
}


#endregion


