//+------------------------------------------------------------------+
//|                                          HVTradingBotBridge.mq5  |
//|  Connects MetaTrader 5 to HVTradingBot running on this computer. |
//|                                                                  |
//|  Every second it reports the account, open positions and recent  |
//|  deals of HVTradingBot's trades to HVTradingBot, and places or   |
//|  closes the trades HVTradingBot asks for. Demo accounts only.    |
//|                                                                  |
//|  Set up: Tools > Options > Expert Advisors > tick "Allow         |
//|  WebRequest for listed URL" and add http://127.0.0.1:5080 .       |
//|  Then attach this Expert Advisor to any one chart, paste the     |
//|  bridge key from HVTradingBot (Settings > Broker account) and    |
//|  make sure "Algo Trading" is on.                                 |
//+------------------------------------------------------------------+
#property copyright   "HVTradingBot"
#property version     "1.00"
#property description "Bridge between HVTradingBot and this MT5 demo account."

#include <Trade\Trade.mqh>

input string BridgeUrl   = "http://127.0.0.1:5080/api/bridge/mt5"; // HVTradingBot bridge address
input string BridgeKey   = "";                                      // Bridge key (HVTradingBot Settings > Broker account)
input long   MagicNumber = 770077;                                  // Marks HVTradingBot's trades
input int    PollMs      = 1000;                                    // How often to check in (milliseconds)
input int    HistoryDays = 7;                                       // Days of closed trades to report

#define EA_VERSION "1.0"

CTrade   g_trade;
string   g_results[];     // results not yet delivered to HVTradingBot
long     g_done[];        // command ids already executed: never execute one twice
datetime g_lastWarning = 0;

//+------------------------------------------------------------------+
int OnInit()
  {
   if(StringLen(BridgeKey) == 0)
     {
      Alert("HVTradingBot: paste the bridge key from HVTradingBot (Settings > Broker account) into the Expert Advisor inputs.");
      return(INIT_PARAMETERS_INCORRECT);
     }
   if(AccountInfoInteger(ACCOUNT_TRADE_MODE) == ACCOUNT_TRADE_MODE_REAL)
     {
      Alert("HVTradingBot: this is a real-money account. The bridge only runs on demo accounts.");
      return(INIT_FAILED);
     }
   g_trade.SetExpertMagicNumber((ulong)MagicNumber);
   g_trade.SetDeviationInPoints(20);
   EventSetMillisecondTimer(MathMax(250, PollMs));
   Print("HVTradingBot bridge ", EA_VERSION, " started: ", BridgeUrl);
   return(INIT_SUCCEEDED);
  }

//+------------------------------------------------------------------+
void OnDeinit(const int reason)
  {
   EventKillTimer();
  }

//+------------------------------------------------------------------+
void OnTimer()
  {
   Sync();
  }

//+------------------------------------------------------------------+
//| Report to HVTradingBot and run the commands it answers with.     |
//+------------------------------------------------------------------+
void Sync()
  {
   string body = BuildReport();
   char   data[];
   char   result[];
   string resultHeaders;
   int n = StringToCharArray(body, data, 0, WHOLE_ARRAY, CP_UTF8);
   if(n > 0)
      ArrayResize(data, n - 1); // without the terminating NUL
   string headers = "Content-Type: application/json\r\nX-HV-Request: 1\r\nX-HV-Bridge-Key: " + BridgeKey + "\r\n";

   ResetLastError();
   int status = WebRequest("POST", BridgeUrl, headers, 5000, data, result, resultHeaders);
   if(status == -1)
     {
      int error = GetLastError();
      if(error == 4014)
         Warn("HVTradingBot: allow WebRequest for http://127.0.0.1:5080 in Tools > Options > Expert Advisors.");
      else
         Warn("HVTradingBot: cannot reach " + BridgeUrl + " (error " + IntegerToString(error) + "). Is HVTradingBot running?");
      return; // results are kept and sent next time
     }

   string text = CharArrayToString(result, 0, WHOLE_ARRAY, CP_UTF8);
   if(status != 200)
     {
      Warn("HVTradingBot: " + text);
      return;
     }

   ArrayResize(g_results, 0); // delivered
   string lines[];
   int count = StringSplit(text, '\n', lines);
   for(int i = 0; i < count; i++)
      Execute(lines[i]);
  }

//+------------------------------------------------------------------+
//| OPEN|id|symbol|BUY or SELL|units|stop|target|clientId           |
//| CLOSE|id|position                                               |
//+------------------------------------------------------------------+
void Execute(string line)
  {
   StringTrimLeft(line);
   StringTrimRight(line);
   if(line == "" || line == "OK")
      return;

   string f[];
   int n = StringSplit(line, '|', f);
   if(n < 3)
      return;
   long id = StringToInteger(f[1]);
   if(IsDone(id))
      return;
   MarkDone(id);

   if(f[0] == "OPEN" && n >= 8)
      Open(id, f[2], f[3] == "BUY", StringToDouble(f[4]), StringToDouble(f[5]), StringToDouble(f[6]), f[7]);
   else
      if(f[0] == "CLOSE")
         Close(id, StringToInteger(f[2]));
  }

//+------------------------------------------------------------------+
void Open(long id, string symbol, bool buy, double units, double sl, double tp, string clientId)
  {
   if(AccountInfoInteger(ACCOUNT_TRADE_MODE) == ACCOUNT_TRADE_MODE_REAL)
     {
      AddResult(id, false, 0, "Real-money account: refused.", 0, 0, 0, 0, 0);
      return;
     }
   if(!SymbolSelect(symbol, true))
     {
      AddResult(id, false, 0, "Symbol " + symbol + " is not available on this account (check the symbol suffix in HVTradingBot).", 0, 0, 0, 0, 0);
      return;
     }

   double contract = SymbolInfoDouble(symbol, SYMBOL_TRADE_CONTRACT_SIZE);
   double vmin     = SymbolInfoDouble(symbol, SYMBOL_VOLUME_MIN);
   double vmax     = SymbolInfoDouble(symbol, SYMBOL_VOLUME_MAX);
   double step     = SymbolInfoDouble(symbol, SYMBOL_VOLUME_STEP);
   if(contract <= 0 || step <= 0)
     {
      AddResult(id, false, 0, "No contract size for " + symbol + ".", 0, 0, 0, 0, 0);
      return;
     }

   // Units to lots, rounded down to the lot step: never more risk than HVTradingBot sized.
   double lots = MathMin(MathFloor(units / contract / step + 1e-9) * step, vmax);
   if(lots < vmin - 1e-9)
     {
      AddResult(id, false, 0, StringFormat("%.0f units is below the broker minimum of %g lot (%.0f units); raise the risk per trade or the trading capital.",
                                           units, vmin, vmin * contract), 0, 0, 0, 0, 0);
      return;
     }
   lots = NormalizeDouble(lots, VolumeDigits(step));
   int digits = (int)SymbolInfoInteger(symbol, SYMBOL_DIGITS);
   sl = NormalizeDouble(sl, digits);
   tp = NormalizeDouble(tp, digits);

   g_trade.SetTypeFillingBySymbol(symbol);
   bool ok = buy ? g_trade.Buy(lots, symbol, 0.0, sl, tp, clientId) : g_trade.Sell(lots, symbol, 0.0, sl, tp, clientId);
   uint rc = g_trade.ResultRetcode();
   if(ok && (rc == TRADE_RETCODE_DONE || rc == TRADE_RETCODE_DONE_PARTIAL || rc == TRADE_RETCODE_PLACED))
     {
      ulong  deal       = g_trade.ResultDeal();
      long   position   = 0;
      double commission = 0;
      if(deal > 0 && HistoryDealSelect(deal))
        {
         position   = HistoryDealGetInteger(deal, DEAL_POSITION_ID);
         commission = HistoryDealGetDouble(deal, DEAL_COMMISSION);
        }
      if(position == 0)
         position = (long)g_trade.ResultOrder();
      double volume = g_trade.ResultVolume();
      AddResult(id, true, (int)rc, g_trade.ResultRetcodeDescription(), position, g_trade.ResultPrice(), volume, volume * contract, commission);
     }
   else
      AddResult(id, false, (int)rc, g_trade.ResultRetcodeDescription(), 0, 0, 0, 0, 0);
  }

//+------------------------------------------------------------------+
void Close(long id, long positionId)
  {
   ulong ticket = FindPosition(positionId);
   if(ticket == 0)
     {
      AddResult(id, true, (int)TRADE_RETCODE_DONE, "The position is already closed.", positionId, 0, 0, 0, 0);
      return;
     }
   bool ok = g_trade.PositionClose(ticket);
   uint rc = g_trade.ResultRetcode();
   AddResult(id, ok && (rc == TRADE_RETCODE_DONE || rc == TRADE_RETCODE_DONE_PARTIAL || rc == TRADE_RETCODE_PLACED), (int)rc,
             g_trade.ResultRetcodeDescription(), positionId, g_trade.ResultPrice(), 0, 0, 0);
  }

ulong FindPosition(long identifier)
  {
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket > 0 && PositionGetInteger(POSITION_IDENTIFIER) == identifier)
         return(ticket);
     }
   return(0);
  }

//+------------------------------------------------------------------+
//| The report: account, all open positions, deals of our positions. |
//| Times are sent as UTC seconds.                                   |
//+------------------------------------------------------------------+
string BuildReport()
  {
   long offset = ServerOffset();
   string s = "{\"version\":\"" + EA_VERSION + "\",\"account\":{";
   s += "\"login\":" + IntegerToString(AccountInfoInteger(ACCOUNT_LOGIN));
   s += ",\"server\":" + Str(AccountInfoString(ACCOUNT_SERVER));
   s += ",\"company\":" + Str(AccountInfoString(ACCOUNT_COMPANY));
   s += ",\"currency\":" + Str(AccountInfoString(ACCOUNT_CURRENCY));
   s += ",\"balance\":" + Num(AccountInfoDouble(ACCOUNT_BALANCE));
   s += ",\"equity\":" + Num(AccountInfoDouble(ACCOUNT_EQUITY));
   s += ",\"tradeMode\":" + Str(TradeMode());
   s += "},\"positions\":[";

   bool first = true;
   for(int i = 0; i < PositionsTotal(); i++)
     {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0)
         continue;
      string symbol = PositionGetString(POSITION_SYMBOL);
      if(!first)
         s += ",";
      first = false;
      s += "{\"ticket\":" + IntegerToString(PositionGetInteger(POSITION_IDENTIFIER));
      s += ",\"symbol\":" + Str(symbol);
      s += ",\"type\":" + Str(PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_BUY ? "BUY" : "SELL");
      s += ",\"volume\":" + Num(PositionGetDouble(POSITION_VOLUME));
      s += ",\"priceOpen\":" + Num(PositionGetDouble(POSITION_PRICE_OPEN));
      s += ",\"sl\":" + Num(PositionGetDouble(POSITION_SL));
      s += ",\"tp\":" + Num(PositionGetDouble(POSITION_TP));
      s += ",\"profit\":" + Num(PositionGetDouble(POSITION_PROFIT));
      s += ",\"swap\":" + Num(PositionGetDouble(POSITION_SWAP));
      s += ",\"time\":" + IntegerToString((long)PositionGetInteger(POSITION_TIME) - offset);
      s += ",\"comment\":" + Str(PositionGetString(POSITION_COMMENT));
      s += ",\"magic\":" + IntegerToString(PositionGetInteger(POSITION_MAGIC));
      s += ",\"contractSize\":" + Num(SymbolInfoDouble(symbol, SYMBOL_TRADE_CONTRACT_SIZE));
      s += "}";
     }

   s += "],\"deals\":[" + Deals(offset) + "],\"results\":[";
   for(int i = 0; i < ArraySize(g_results); i++)
      s += (i > 0 ? "," : "") + g_results[i];
   s += "]}";
   return(s);
  }

//| Deals of positions HVTradingBot opened (by magic number), including stop/target closes.
string Deals(long offset)
  {
   datetime now = TimeCurrent();
   if(!HistorySelect(now - HistoryDays * 86400, now + 86400))
      return("");

   long ours[];
   int total = HistoryDealsTotal();
   for(int i = 0; i < total; i++)
     {
      ulong deal = HistoryDealGetTicket(i);
      if(deal > 0 && HistoryDealGetInteger(deal, DEAL_MAGIC) == MagicNumber && HistoryDealGetInteger(deal, DEAL_ENTRY) == DEAL_ENTRY_IN)
        {
         int size = ArraySize(ours);
         ArrayResize(ours, size + 1);
         ours[size] = HistoryDealGetInteger(deal, DEAL_POSITION_ID);
        }
     }

   string s = "";
   for(int i = 0; i < total; i++)
     {
      ulong deal = HistoryDealGetTicket(i);
      if(deal == 0)
         continue;
      long position = HistoryDealGetInteger(deal, DEAL_POSITION_ID);
      if(!Contains(ours, position))
         continue;
      if(s != "")
         s += ",";
      s += "{\"ticket\":" + IntegerToString((long)deal);
      s += ",\"positionId\":" + IntegerToString(position);
      s += ",\"entry\":" + Str(Entry(HistoryDealGetInteger(deal, DEAL_ENTRY)));
      s += ",\"price\":" + Num(HistoryDealGetDouble(deal, DEAL_PRICE));
      s += ",\"profit\":" + Num(HistoryDealGetDouble(deal, DEAL_PROFIT));
      s += ",\"commission\":" + Num(HistoryDealGetDouble(deal, DEAL_COMMISSION));
      s += ",\"swap\":" + Num(HistoryDealGetDouble(deal, DEAL_SWAP));
      s += ",\"time\":" + IntegerToString((long)HistoryDealGetInteger(deal, DEAL_TIME) - offset);
      s += ",\"reason\":" + Str(Reason(HistoryDealGetInteger(deal, DEAL_REASON)));
      s += "}";
     }
   return(s);
  }

//+------------------------------------------------------------------+
void AddResult(long id, bool ok, int retcode, string message, long ticket, double price, double volume, double units, double commission)
  {
   string r = "{\"id\":" + IntegerToString(id) + ",\"ok\":" + (ok ? "true" : "false") + ",\"retcode\":" + IntegerToString(retcode);
   r += ",\"message\":" + Str(message);
   r += ",\"ticket\":" + (ticket == 0 ? "null" : IntegerToString(ticket));
   r += ",\"price\":" + (price == 0 ? "null" : Num(price));
   r += ",\"volume\":" + Num(volume) + ",\"units\":" + Num(units) + ",\"commission\":" + Num(commission) + "}";
   int size = ArraySize(g_results);
   ArrayResize(g_results, size + 1);
   g_results[size] = r;
   Print("HVTradingBot command ", id, ": ", (ok ? "done" : "refused"), " - ", message);
  }

bool IsDone(long id)
  {
   return(Contains(g_done, id));
  }

void MarkDone(long id)
  {
   int size = ArraySize(g_done);
   if(size >= 500)
     {
      ArrayRemove(g_done, 0, 100);
      size = ArraySize(g_done);
     }
   ArrayResize(g_done, size + 1);
   g_done[size] = id;
  }

bool Contains(const long &values[], long value)
  {
   for(int i = 0; i < ArraySize(values); i++)
      if(values[i] == value)
         return(true);
   return(false);
  }

//| Server time minus UTC, in seconds (MT5 reports times in server time).
long ServerOffset()
  {
   long offset = (long)(TimeTradeServer() - TimeGMT());
   return((long)MathRound(offset / 900.0) * 900); // whole quarter hours
  }

int VolumeDigits(double step)
  {
   int d = 0;
   while(d < 8 && MathAbs(step * MathPow(10, d) - MathRound(step * MathPow(10, d))) > 1e-9)
      d++;
   return(d);
  }

string TradeMode()
  {
   long mode = AccountInfoInteger(ACCOUNT_TRADE_MODE);
   if(mode == ACCOUNT_TRADE_MODE_DEMO)
      return("ACCOUNT_TRADE_MODE_DEMO");
   if(mode == ACCOUNT_TRADE_MODE_CONTEST)
      return("ACCOUNT_TRADE_MODE_CONTEST");
   return("ACCOUNT_TRADE_MODE_REAL");
  }

string Entry(long entry)
  {
   if(entry == DEAL_ENTRY_IN)
      return("IN");
   if(entry == DEAL_ENTRY_OUT)
      return("OUT");
   if(entry == DEAL_ENTRY_INOUT)
      return("INOUT");
   return("OUT_BY");
  }

string Reason(long reason)
  {
   if(reason == DEAL_REASON_SL)
      return("SL");
   if(reason == DEAL_REASON_TP)
      return("TP");
   if(reason == DEAL_REASON_SO)
      return("SO");
   if(reason == DEAL_REASON_EXPERT)
      return("EXPERT");
   if(reason == DEAL_REASON_CLIENT || reason == DEAL_REASON_MOBILE || reason == DEAL_REASON_WEB)
      return("CLIENT");
   return("OTHER");
  }

string Num(double value)
  {
   return(DoubleToString(value, 8));
  }

string Str(string value)
  {
   StringReplace(value, "\\", "\\\\");
   StringReplace(value, "\"", "\\\"");
   StringReplace(value, "\r", " ");
   StringReplace(value, "\n", " ");
   StringReplace(value, "\t", " ");
   return("\"" + value + "\"");
  }

//| At most one warning a minute in the Experts log.
void Warn(string message)
  {
   if(TimeLocal() - g_lastWarning < 60)
      return;
   g_lastWarning = TimeLocal();
   Print(message);
  }
//+------------------------------------------------------------------+
