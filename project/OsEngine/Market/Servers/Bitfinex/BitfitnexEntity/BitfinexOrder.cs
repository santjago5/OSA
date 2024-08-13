namespace OsEngine.Market.Servers.Bitfinex.BitfitnexEntity
{


    public class BitfinexResponseOrder
    {
        public string Mts { get; set; } // Временная метка (MTS)
        public string Type { get; set; } // Тип события (TYPE)
        public string MessageId { get; set; } // ID сообщения (MESSAGE_ID)
        public string Data { get; set; } // DATA массив (десериализуется отдельно)
        public string Code { get; set; } // Код ответа (CODE)
        public string Status { get; set; } // Статус (STATUS)
        public string Text { get; set; } // Текст ответа (TEXT)
    }

    class BitfinexOrder
    {
        public string Id { get; set; } // Идентификатор заказа (ID)
        public string Gid { get; set; } // Групповой идентификатор (GID)
        public string Cid { get; set; } // Клиентский идентификатор (CID)
        public string Symbol { get; set; } // Символ (SYMBOL)
        public string MtsCreate { get; set; } // Временная метка создания (MTS_CREATE)
        public string MtsUpdate { get; set; } // Временная метка обновления (MTS_UPDATE)
        public string Amount { get; set; } // Объем (AMOUNT)
        public string AmountOrig { get; set; } // Оригинальный объем (AMOUNT_ORIG)
        public string OrderType { get; set; } // Тип ордера (ORDER_TYPE)
        public string TypePrev { get; set; } // Предыдущий тип (TYPE_PREV)
        public string MtsTif { get; set; } // Временная метка TIF (MTS_TIF)
        public string Flags { get; set; } // Флаги (FLAGS)
        public string Status { get; set; } // Статус ордера (STATUS)
        public string Price { get; set; } // Цена (PRICE)
        public string PriceAvg { get; set; } // Средняя цена (PRICE_AVG)
        public string PriceTrailing { get; set; } // Цена трейлинга (PRICE_TRAILING)
        public string PriceAuxLimit { get; set; } // Вспомогательная лимитная цена (PRICE_AUX_LIMIT)
        public string Notify { get; set; } // Уведомление (NOTIFY)
        public string Hidden { get; set; } // Скрытый ордер (HIDDEN)
        public string PlacedId { get; set; } // ID размещенного ордера (PLACED_ID)
        public string Routing { get; set; } // Маршрутизация (ROUTING)
        public string Meta { get; set; } // Метаданные (META)
    }
}