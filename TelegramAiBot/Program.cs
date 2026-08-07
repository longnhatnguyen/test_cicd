using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var config = BotConfig.LoadFromEnvironment();
using var httpClient = new HttpClient();
var accessStore = new InMemoryAccessStore();

var telegram = new TelegramClient(httpClient, config);
var ai = new OpenAiClient(httpClient, config);
var accessControl = new AccessControl(config, accessStore);
var marketData = new MarketDataClient(httpClient, config);
var chartWatcher = new ChartAnalysisWatcher(config, telegram, marketData, ai);

if (config.ChartAnalysisEnabled)
{
    _ = chartWatcher.RunAsync(CancellationToken.None);
}

Console.WriteLine($"[{DateTimeOffset.UtcNow:u}] Telegram AI bot started.");
Console.WriteLine($"Polling chat updates with model '{config.OpenAiModel}'.");

var offset = 0L;

while (true)
{
    try
    {
        var updates = await telegram.GetUpdatesAsync(offset, CancellationToken.None);

        foreach (var update in updates)
        {
            offset = update.UpdateId + 1;

            if (update.Message?.Chat?.Id is not long chatId || string.IsNullOrWhiteSpace(update.Message.Text))
            {
                continue;
            }

            var messageText = update.Message.Text.Trim();

            if (messageText.Equals("/start", StringComparison.OrdinalIgnoreCase) ||
                messageText.Equals("/help", StringComparison.OrdinalIgnoreCase))
            {
                await telegram.SendMessageAsync(
                    chatId,
                    await accessControl.GetWelcomeMessageAsync(chatId, CancellationToken.None),
                    CancellationToken.None);
                continue;
            }

            if (messageText.Equals("/logout", StringComparison.OrdinalIgnoreCase))
            {
                await accessControl.LogoutAsync(chatId, CancellationToken.None);
                await telegram.SendMessageAsync(
                    chatId,
                    "Da dang xuat. Gui lai mat khau de tiep tuc dung bot.",
                    CancellationToken.None);
                continue;
            }

            if (messageText.Equals("/chatid", StringComparison.OrdinalIgnoreCase))
            {
                await telegram.SendMessageAsync(chatId, $"Chat id: {chatId}", CancellationToken.None);
                continue;
            }

            if (!await accessControl.IsAuthorizedAsync(chatId, CancellationToken.None))
            {
                var accessReply = await accessControl.TryAuthorizeAsync(chatId, messageText, CancellationToken.None);
                await telegram.SendMessageAsync(chatId, accessReply, CancellationToken.None);
                continue;
            }

            if (ChartCommand.TryParse(messageText, out var chartRequest))
            {
                await telegram.SendChatActionAsync(chatId, "typing", CancellationToken.None);

                try
                {
                    var snapshot = await marketData.GetSnapshotAsync(chartRequest.Interval, CancellationToken.None);
                    var snapshotReply = MarketSignalEngine.FormatSnapshot(snapshot);
                    await telegram.SendMessageAsync(chatId, snapshotReply, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    await telegram.SendMessageAsync(
                        chatId,
                        $"Không lấy được dữ liệu chart: {ex.Message}",
                        CancellationToken.None);
                }

                continue;
            }

            string reply;

            if (string.IsNullOrWhiteSpace(config.OpenAiApiKey))
            {
                reply = "Bot da nhan tin nhan, nhung chua co OPENAI_API_KEY de goi AI.";
            }
            else
            {
                await telegram.SendChatActionAsync(chatId, "typing", CancellationToken.None);
                reply = await ai.GenerateReplyAsync(new ConversationTurn("user", messageText), CancellationToken.None);
            }

            await telegram.SendMessageAsync(chatId, reply, CancellationToken.None);
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[{DateTimeOffset.UtcNow:u}] Loop error: {ex.Message}");
        await Task.Delay(TimeSpan.FromSeconds(5));
    }
}

internal sealed record BotConfig(
    string TelegramBotToken,
    string TelegramApiBase,
    string OpenAiApiKey,
    string OpenAiApiBase,
    string OpenAiModel,
    string SystemPrompt,
    string AccessPassword,
    bool ChartAnalysisEnabled,
    long? ChartAnalysisChatId,
    string ChartAnalysisUrl,
    string ChartAnalysisSymbol,
    string ChartAnalysisDataSymbol,
    IReadOnlyList<string> ChartAnalysisIntervals,
    int ChartAnalysisPeriodMinutes,
    bool ChartAnalysisSendNoTrade,
    bool ChartAnalysisUseAi,
    decimal ChartAnalysisMaxRiskPrice,
    decimal ChartAnalysisMaxRewardPrice)
{
    public static BotConfig LoadFromEnvironment()
    {
        var telegramBotToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(telegramBotToken))
        {
            throw new InvalidOperationException("Missing TELEGRAM_BOT_TOKEN environment variable.");
        }

        return new BotConfig(
            TelegramBotToken: telegramBotToken,
            TelegramApiBase: (Environment.GetEnvironmentVariable("TELEGRAM_API_BASE")?.Trim() ?? "https://api.telegram.org").TrimEnd('/'),
            OpenAiApiKey: Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim() ?? string.Empty,
            OpenAiApiBase: (Environment.GetEnvironmentVariable("OPENAI_API_BASE")?.Trim() ?? "https://api.openai.com/v1").TrimEnd('/'),
            OpenAiModel: Environment.GetEnvironmentVariable("OPENAI_MODEL")?.Trim() ?? "gpt-4.1-mini",
            SystemPrompt: Environment.GetEnvironmentVariable("OPENAI_SYSTEM_PROMPT")?.Trim()
                ?? "Ban la tro ly Telegram gon gang, lich su va huu ich. Tra loi bang tieng Viet neu nguoi dung viet tieng Viet.",
            AccessPassword: Environment.GetEnvironmentVariable("TELEGRAM_ACCESS_PASSWORD")?.Trim()
                ?? throw new InvalidOperationException("Missing TELEGRAM_ACCESS_PASSWORD environment variable."),
            ChartAnalysisEnabled: ReadBool("CHART_ANALYSIS_ENABLED", false),
            ChartAnalysisChatId: ReadLong("CHART_ANALYSIS_CHAT_ID"),
            ChartAnalysisUrl: Environment.GetEnvironmentVariable("CHART_ANALYSIS_URL")?.Trim() ?? string.Empty,
            ChartAnalysisSymbol: TradingViewSymbolResolver.Resolve(
                Environment.GetEnvironmentVariable("CHART_ANALYSIS_URL")?.Trim(),
                Environment.GetEnvironmentVariable("CHART_ANALYSIS_SYMBOL")?.Trim() ?? "OANDA:XAUUSD"),
            ChartAnalysisDataSymbol: Environment.GetEnvironmentVariable("CHART_ANALYSIS_DATA_SYMBOL")?.Trim() ?? "BINANCE:XAUTUSDT",
            ChartAnalysisIntervals: ReadIntervals(),
            ChartAnalysisPeriodMinutes: ReadInt("CHART_ANALYSIS_PERIOD_MINUTES", 5),
            ChartAnalysisSendNoTrade: ReadBool("CHART_ANALYSIS_SEND_NO_TRADE", false),
            ChartAnalysisUseAi: ReadBool("CHART_ANALYSIS_USE_AI", false),
            ChartAnalysisMaxRiskPrice: ReadDecimal("CHART_ANALYSIS_MAX_RISK_PRICE", 10m),
            ChartAnalysisMaxRewardPrice: ReadDecimal("CHART_ANALYSIS_MAX_REWARD_PRICE", 10m));
    }

    private static int ReadInt(string variableName, int fallbackValue)
    {
        var raw = Environment.GetEnvironmentVariable(variableName)?.Trim();
        return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : fallbackValue;
    }

    private static bool ReadBool(string variableName, bool fallbackValue)
    {
        var raw = Environment.GetEnvironmentVariable(variableName)?.Trim();
        return bool.TryParse(raw, out var parsed) ? parsed : fallbackValue;
    }

    private static long? ReadLong(string variableName)
    {
        var raw = Environment.GetEnvironmentVariable(variableName)?.Trim();
        return long.TryParse(raw, out var parsed) ? parsed : null;
    }

    private static decimal ReadDecimal(string variableName, decimal fallbackValue)
    {
        var raw = Environment.GetEnvironmentVariable(variableName)?.Trim();
        return decimal.TryParse(raw, out var parsed) && parsed > 0 ? parsed : fallbackValue;
    }

    private static IReadOnlyList<string> ReadIntervals()
    {
        var raw = Environment.GetEnvironmentVariable("CHART_ANALYSIS_INTERVALS")?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = Environment.GetEnvironmentVariable("CHART_ANALYSIS_INTERVAL")?.Trim() ?? "M5,M15,H1,H4";
        }

        var intervals = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ChartCommand.NormalizeInterval)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return intervals.Length > 0 ? intervals : ["5", "15", "60", "240"];
    }
}

internal sealed class AccessControl(BotConfig config, InMemoryAccessStore store)
{
    public Task<bool> IsAuthorizedAsync(long chatId, CancellationToken cancellationToken)
        => store.IsAuthorizedAsync(chatId, cancellationToken);

    public async Task<string> GetWelcomeMessageAsync(long chatId, CancellationToken cancellationToken)
    {
        if (await IsAuthorizedAsync(chatId, cancellationToken))
        {
            return "Ban da dang nhap. Cu hoi thoai mai nhe.";
        }

        return "Chao ban. Hay nhap mat khau de bat dau tro chuyen voi bot.";
    }

    public async Task<string> TryAuthorizeAsync(long chatId, string candidatePassword, CancellationToken cancellationToken)
    {
        if (MatchesPassword(candidatePassword, config.AccessPassword))
        {
            await store.SetAuthorizedAsync(chatId, true, cancellationToken);
            return "Xac thuc thanh cong. Bay gio ban co the tro chuyen voi bot. Dung /logout neu muon khoa lai.";
        }

        return "Sai mat khau. Hay thu lai.";
    }

    public Task LogoutAsync(long chatId, CancellationToken cancellationToken)
        => store.SetAuthorizedAsync(chatId, false, cancellationToken);

    private static bool MatchesPassword(string candidatePassword, string expectedPassword)
    {
        var candidateBytes = Encoding.UTF8.GetBytes(candidatePassword);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedPassword);
        return CryptographicOperations.FixedTimeEquals(candidateBytes, expectedBytes);
    }
}

internal sealed class InMemoryAccessStore
{
    private readonly object _lock = new();
    private readonly HashSet<long> _authorizedChats = [];

    public Task<bool> IsAuthorizedAsync(long chatId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_authorizedChats.Contains(chatId));
        }
    }

    public Task SetAuthorizedAsync(long chatId, bool isAuthorized, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (isAuthorized)
            {
                _authorizedChats.Add(chatId);
            }
            else
            {
                _authorizedChats.Remove(chatId);
            }
        }

        return Task.CompletedTask;
    }
}

internal sealed record ConversationTurn(string Role, string Content);

internal sealed record ChartRequest(string Symbol, string Interval, bool Analyze = false);

internal static class ChartCommand
{
    public static bool TryParse(string messageText, out ChartRequest request)
    {
        request = new ChartRequest("OANDA:XAUUSD", "60");

        if (!messageText.StartsWith("/chart", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = messageText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var analyze = parts.Any(IsAnalyzeToken);
        var values = parts.Skip(1).Where(part => !IsAnalyzeToken(part)).ToArray();
        var symbol = values.Length >= 1 ? TradingViewSymbolResolver.Resolve(values[0], values[0]) : request.Symbol;
        var interval = values.Length >= 2 ? NormalizeInterval(values[1]) : request.Interval;

        request = new ChartRequest(symbol, interval, analyze);
        return true;
    }

    public static string NormalizeInterval(string input)
    {
        var value = input.Trim().ToUpperInvariant();
        return value switch
        {
            "M1" => "1",
            "M5" => "5",
            "M15" => "15",
            "M30" => "30",
            "H1" => "60",
            "H4" => "240",
            "D1" => "D",
            "W1" => "W",
            _ => value
        };
    }

    private static bool IsAnalyzeToken(string part)
        => part.Equals("analyze", StringComparison.OrdinalIgnoreCase) ||
           part.Equals("phan-tich", StringComparison.OrdinalIgnoreCase) ||
           part.Equals("pt", StringComparison.OrdinalIgnoreCase);
}

internal static class TradingViewSymbolResolver
{
    public static string Resolve(string? tradingViewUrlOrSymbol, string fallbackSymbol)
    {
        if (string.IsNullOrWhiteSpace(tradingViewUrlOrSymbol))
        {
            return fallbackSymbol;
        }

        var trimmed = tradingViewUrlOrSymbol.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed;
        }

        var query = uri.Query.TrimStart('?');
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2 || !parts[0].Equals("symbol", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var symbol = Uri.UnescapeDataString(parts[1]).Trim();
            return string.IsNullOrWhiteSpace(symbol) ? fallbackSymbol : symbol;
        }

        return fallbackSymbol;
    }
}

internal sealed class MarketDataClient(HttpClient httpClient, BotConfig config)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, CachedMarketResponse> _responseCache = [];

    public async Task<MarketSnapshot> GetSnapshotAsync(string interval, CancellationToken cancellationToken)
    {
        if (TryGetBinanceSymbol(config.ChartAnalysisDataSymbol, out var binanceSymbol))
        {
            return await GetBinanceSnapshotAsync(binanceSymbol, interval, cancellationToken);
        }

        return await GetYahooSnapshotAsync(interval, cancellationToken);
    }

    private async Task<MarketSnapshot> GetYahooSnapshotAsync(string interval, CancellationToken cancellationToken)
    {
        var normalizedInterval = ChartCommand.NormalizeInterval(interval);
        var yahooInterval = normalizedInterval == "240" ? "60m" : ToYahooInterval(normalizedInterval);
        var range = yahooInterval is "5m" or "15m" ? "5d" : "1mo";
        var url =
            $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(config.ChartAnalysisDataSymbol)}" +
            $"?range={range}&interval={yahooInterval}";

        var raw = await GetRawChartAsync(url, cancellationToken);
        var candles = ParseCandles(raw);
        if (normalizedInterval == "240")
        {
            candles = AggregateCandles(candles, groupSize: 4);
        }

        if (candles.Count < 60)
        {
            throw new InvalidOperationException($"Không đủ dữ liệu {config.ChartAnalysisDataSymbol} {normalizedInterval}.");
        }

        return MarketSnapshot.Create(config.ChartAnalysisSymbol, config.ChartAnalysisDataSymbol, normalizedInterval, candles);
    }

    private async Task<MarketSnapshot> GetBinanceSnapshotAsync(
        string binanceSymbol,
        string interval,
        CancellationToken cancellationToken)
    {
        var normalizedInterval = ChartCommand.NormalizeInterval(interval);
        var binanceInterval = ToBinanceInterval(normalizedInterval);
        var url =
            $"https://api.binance.com/api/v3/klines?symbol={Uri.EscapeDataString(binanceSymbol)}" +
            $"&interval={binanceInterval}&limit=500";

        var raw = await GetRawChartAsync(url, cancellationToken);
        var candles = ParseBinanceCandles(raw);

        if (candles.Count < 60)
        {
            throw new InvalidOperationException($"Không đủ dữ liệu BINANCE:{binanceSymbol} {normalizedInterval}.");
        }

        return MarketSnapshot.Create(config.ChartAnalysisSymbol, $"BINANCE:{binanceSymbol}", normalizedInterval, candles);
    }

    private async Task<string> GetRawChartAsync(string url, CancellationToken cancellationToken)
    {
        lock (_cacheLock)
        {
            if (_responseCache.TryGetValue(url, out var cached) &&
                DateTimeOffset.UtcNow - cached.FetchedAt < CacheDuration)
            {
                return cached.RawJson;
            }
        }

        var raw = await FetchRawChartWithRetryAsync(url, cancellationToken);

        lock (_cacheLock)
        {
            _responseCache[url] = new CachedMarketResponse(DateTimeOffset.UtcNow, raw);
        }

        return raw;
    }

    private async Task<string> FetchRawChartWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        string? lastError = null;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 Chrome/126.0 Safari/537.36");
            request.Headers.Accept.ParseAdd("application/json,text/plain,*/*");
            request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
            request.Headers.Referrer = new Uri("https://finance.yahoo.com/");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return raw;
            }

            lastError = $"Yahoo data error {(int)response.StatusCode}: {raw}";
            var shouldRetry = (int)response.StatusCode == 429 || (int)response.StatusCode >= 500;
            if (!shouldRetry || attempt == 3)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(attempt * 5), cancellationToken);
        }

        throw new InvalidOperationException(lastError ?? "Yahoo data error.");
    }

    private static string ToYahooInterval(string interval)
        => interval switch
        {
            "1" => "1m",
            "5" => "5m",
            "15" => "15m",
            "30" => "30m",
            "60" => "60m",
            "D" => "1d",
            "W" => "1wk",
            _ => "15m"
        };

    private static string ToBinanceInterval(string interval)
        => interval switch
        {
            "1" => "1m",
            "5" => "5m",
            "15" => "15m",
            "30" => "30m",
            "60" => "1h",
            "240" => "4h",
            "D" => "1d",
            "W" => "1w",
            _ => "15m"
        };

    private static bool TryGetBinanceSymbol(string dataSymbol, out string binanceSymbol)
    {
        const string prefix = "BINANCE:";

        if (dataSymbol.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            binanceSymbol = dataSymbol[prefix.Length..].Trim().ToUpperInvariant();
            return !string.IsNullOrWhiteSpace(binanceSymbol);
        }

        binanceSymbol = string.Empty;
        return false;
    }

    private static IReadOnlyList<Candle> ParseCandles(string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        var result = document.RootElement
            .GetProperty("chart")
            .GetProperty("result")[0];

        var timestamps = result.GetProperty("timestamp").EnumerateArray().Select(item => item.GetInt64()).ToArray();
        var quote = result.GetProperty("indicators").GetProperty("quote")[0];
        var opens = quote.GetProperty("open").EnumerateArray().ToArray();
        var highs = quote.GetProperty("high").EnumerateArray().ToArray();
        var lows = quote.GetProperty("low").EnumerateArray().ToArray();
        var closes = quote.GetProperty("close").EnumerateArray().ToArray();
        var volumes = quote.GetProperty("volume").EnumerateArray().ToArray();

        var candles = new List<Candle>(timestamps.Length);
        for (var index = 0; index < timestamps.Length; index++)
        {
            if (opens[index].ValueKind == JsonValueKind.Null ||
                highs[index].ValueKind == JsonValueKind.Null ||
                lows[index].ValueKind == JsonValueKind.Null ||
                closes[index].ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            candles.Add(new Candle(
                Time: DateTimeOffset.FromUnixTimeSeconds(timestamps[index]),
                Open: opens[index].GetDecimal(),
                High: highs[index].GetDecimal(),
                Low: lows[index].GetDecimal(),
                Close: closes[index].GetDecimal(),
                Volume: volumes[index].ValueKind == JsonValueKind.Null ? 0 : volumes[index].GetDecimal()));
        }

        return candles;
    }

    private static IReadOnlyList<Candle> ParseBinanceCandles(string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        var candles = new List<Candle>();

        foreach (var item in document.RootElement.EnumerateArray())
        {
            candles.Add(new Candle(
                Time: DateTimeOffset.FromUnixTimeMilliseconds(item[0].GetInt64()),
                Open: ParseDecimal(item[1]),
                High: ParseDecimal(item[2]),
                Low: ParseDecimal(item[3]),
                Close: ParseDecimal(item[4]),
                Volume: ParseDecimal(item[5])));
        }

        return candles;
    }

    private static decimal ParseDecimal(JsonElement element)
        => decimal.Parse(element.GetString() ?? "0", CultureInfo.InvariantCulture);

    private static IReadOnlyList<Candle> AggregateCandles(IReadOnlyList<Candle> candles, int groupSize)
    {
        var aggregated = new List<Candle>();

        for (var index = 0; index + groupSize <= candles.Count; index += groupSize)
        {
            var group = candles.Skip(index).Take(groupSize).ToArray();
            aggregated.Add(new Candle(
                Time: group[0].Time,
                Open: group[0].Open,
                High: group.Max(candle => candle.High),
                Low: group.Min(candle => candle.Low),
                Close: group[^1].Close,
                Volume: group.Sum(candle => candle.Volume)));
        }

        return aggregated;
    }
}

internal sealed record CachedMarketResponse(DateTimeOffset FetchedAt, string RawJson);

internal sealed record Candle(
    DateTimeOffset Time,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume);

internal sealed record MarketSnapshot(
    string Symbol,
    string DataSymbol,
    string Interval,
    IReadOnlyList<Candle> Candles,
    decimal LastPrice,
    decimal Ema20,
    decimal Ema50,
    decimal Rsi14,
    decimal Atr14,
    decimal PreviousRangeHigh,
    decimal PreviousRangeLow)
{
    public Candle Current => Candles[^1];
    public Candle Previous => Candles[^2];

    public static MarketSnapshot Create(
        string symbol,
        string dataSymbol,
        string interval,
        IReadOnlyList<Candle> candles)
    {
        var previousRange = candles.TakeLast(21).SkipLast(1).ToArray();

        return new MarketSnapshot(
            Symbol: symbol,
            DataSymbol: dataSymbol,
            Interval: interval,
            Candles: candles,
            LastPrice: candles[^1].Close,
            Ema20: IndicatorMath.Ema(candles.Select(candle => candle.Close), 20),
            Ema50: IndicatorMath.Ema(candles.Select(candle => candle.Close), 50),
            Rsi14: IndicatorMath.Rsi(candles.Select(candle => candle.Close).ToArray(), 14),
            Atr14: IndicatorMath.Atr(candles, 14),
            PreviousRangeHigh: previousRange.Max(candle => candle.High),
            PreviousRangeLow: previousRange.Min(candle => candle.Low));
    }
}

internal sealed record TradeSignal(
    string Direction,
    string Symbol,
    string EntryInterval,
    decimal Entry,
    decimal StopLoss,
    decimal TakeProfit,
    decimal Risk,
    decimal Reward,
    string Reason);

internal static class MarketSignalEngine
{
    public static TradeSignal? TryBuildSignal(IReadOnlyList<MarketSnapshot> snapshots, BotConfig config)
    {
        var h1 = FindSnapshot(snapshots, "60");
        var h4 = FindSnapshot(snapshots, "240") ?? h1;

        if (h1 is null || h4 is null)
        {
            return null;
        }

        var bullishBias = IsBullish(h1) && IsBullishOrNeutral(h4);
        var bearishBias = IsBearish(h1) && IsBearishOrNeutral(h4);
        var entryFrames = new[] { FindSnapshot(snapshots, "5"), FindSnapshot(snapshots, "15") }
            .Where(snapshot => snapshot is not null)
            .Cast<MarketSnapshot>()
            .ToArray();

        if (entryFrames.Length == 0)
        {
            entryFrames = [snapshots[0]];
        }

        foreach (var entryFrame in entryFrames)
        {
            if (bullishBias && HasLongTrigger(entryFrame))
            {
                var signal = BuildLongSignal(entryFrame, config);
                if (signal is not null)
                {
                    return signal;
                }
            }

            if (bearishBias && HasShortTrigger(entryFrame))
            {
                var signal = BuildShortSignal(entryFrame, config);
                if (signal is not null)
                {
                    return signal;
                }
            }
        }

        return null;
    }

    public static string FormatSnapshot(MarketSnapshot snapshot)
        => $"""
        {snapshot.Symbol} ({snapshot.DataSymbol}) - khung {DisplayInterval(snapshot.Interval)}
        Giá gần nhất: {FormatPrice(snapshot.LastPrice)}
        EMA20: {FormatPrice(snapshot.Ema20)}
        EMA50: {FormatPrice(snapshot.Ema50)}
        RSI14: {FormatPrice(snapshot.Rsi14)}
        ATR14: {FormatPrice(snapshot.Atr14)}
        Vùng 20 nến trước: {FormatPrice(snapshot.PreviousRangeLow)} - {FormatPrice(snapshot.PreviousRangeHigh)}
        """;

    public static string FormatNoTrade(IReadOnlyList<MarketSnapshot> snapshots)
        => $"""
        SIGNAL: NO_TRADE
        Lý do: Chưa có setup ngắn đủ rõ hoặc SL/TP không phù hợp giới hạn rủi ro.

        {FormatMarketContext(snapshots)}
        """;

    public static string FormatSignal(TradeSignal signal, IReadOnlyList<MarketSnapshot> snapshots)
        => $"""
        SIGNAL: ENTRY
        HƯỚNG: {signal.Direction}
        ĐIỂM VÀO LỆNH: {FormatPrice(signal.Entry)}
        SL: {FormatPrice(signal.StopLoss)} (rủi ro khoảng {FormatPrice(signal.Risk)} giá)
        TP: {FormatPrice(signal.TakeProfit)} (mục tiêu khoảng {FormatPrice(signal.Reward)} giá)
        RR: 1:{FormatPrice(signal.Reward / signal.Risk)}
        SETUP: {signal.Reason}
        INVALIDATION: Hủy kèo nếu giá phá SL hoặc cấu trúc M5/M15 đảo chiều trước khi khớp.
        GHI CHÚ RỦI RO: Kèo ngắn cho vốn nhỏ, không tự đặt lệnh, luôn kiểm tra spread và tin tức trước khi vào.

        {FormatMarketContext(snapshots)}
        """;

    public static string FormatMarketContext(IReadOnlyList<MarketSnapshot> snapshots)
        => string.Join("\n", snapshots.Select(snapshot =>
            $"- {DisplayInterval(snapshot.Interval)}: giá {FormatPrice(snapshot.LastPrice)}, EMA20 {FormatPrice(snapshot.Ema20)}, EMA50 {FormatPrice(snapshot.Ema50)}, RSI {FormatPrice(snapshot.Rsi14)}, ATR {FormatPrice(snapshot.Atr14)}"));

    public static string FormatPrice(decimal value) => value.ToString("0.##");

    public static string DisplayInterval(string interval)
        => interval switch
        {
            "5" => "M5",
            "15" => "M15",
            "30" => "M30",
            "60" => "H1",
            "240" => "H4",
            "D" => "D1",
            "W" => "W1",
            _ => interval
        };

    private static MarketSnapshot? FindSnapshot(IReadOnlyList<MarketSnapshot> snapshots, string interval)
        => snapshots.FirstOrDefault(snapshot => snapshot.Interval.Equals(interval, StringComparison.OrdinalIgnoreCase));

    private static bool IsBullish(MarketSnapshot snapshot)
        => snapshot.LastPrice > snapshot.Ema20 &&
           snapshot.Ema20 > snapshot.Ema50 &&
           snapshot.Rsi14 is >= 45 and <= 72;

    private static bool IsBearish(MarketSnapshot snapshot)
        => snapshot.LastPrice < snapshot.Ema20 &&
           snapshot.Ema20 < snapshot.Ema50 &&
           snapshot.Rsi14 is >= 28 and <= 55;

    private static bool IsBullishOrNeutral(MarketSnapshot snapshot)
        => snapshot.LastPrice > snapshot.Ema50 && snapshot.Rsi14 >= 42;

    private static bool IsBearishOrNeutral(MarketSnapshot snapshot)
        => snapshot.LastPrice < snapshot.Ema50 && snapshot.Rsi14 <= 58;

    private static bool HasLongTrigger(MarketSnapshot snapshot)
        => (snapshot.Current.Low <= snapshot.Ema20 + snapshot.Atr14 * 0.25m &&
            snapshot.Current.Close > snapshot.Ema20 &&
            snapshot.Current.Close > snapshot.Current.Open) ||
           snapshot.Current.Close > snapshot.PreviousRangeHigh;

    private static bool HasShortTrigger(MarketSnapshot snapshot)
        => (snapshot.Current.High >= snapshot.Ema20 - snapshot.Atr14 * 0.25m &&
            snapshot.Current.Close < snapshot.Ema20 &&
            snapshot.Current.Close < snapshot.Current.Open) ||
           snapshot.Current.Close < snapshot.PreviousRangeLow;

    private static TradeSignal? BuildLongSignal(MarketSnapshot snapshot, BotConfig config)
    {
        var entry = snapshot.LastPrice;
        var stopLoss = snapshot.Candles.TakeLast(10).Min(candle => candle.Low) - 0.3m;
        var risk = entry - stopLoss;

        if (risk <= 0 || risk > config.ChartAnalysisMaxRiskPrice)
        {
            return null;
        }

        var reward = Math.Min(config.ChartAnalysisMaxRewardPrice, Math.Max(risk * 1.2m, 3m));
        return new TradeSignal(
            Direction: "LONG",
            Symbol: snapshot.Symbol,
            EntryInterval: snapshot.Interval,
            Entry: entry,
            StopLoss: stopLoss,
            TakeProfit: entry + reward,
            Risk: risk,
            Reward: reward,
            Reason: $"Bias khung lớn ủng hộ LONG, {DisplayInterval(snapshot.Interval)} có phản ứng quanh EMA20/breakout ngắn.");
    }

    private static TradeSignal? BuildShortSignal(MarketSnapshot snapshot, BotConfig config)
    {
        var entry = snapshot.LastPrice;
        var stopLoss = snapshot.Candles.TakeLast(10).Max(candle => candle.High) + 0.3m;
        var risk = stopLoss - entry;

        if (risk <= 0 || risk > config.ChartAnalysisMaxRiskPrice)
        {
            return null;
        }

        var reward = Math.Min(config.ChartAnalysisMaxRewardPrice, Math.Max(risk * 1.2m, 3m));
        return new TradeSignal(
            Direction: "SHORT",
            Symbol: snapshot.Symbol,
            EntryInterval: snapshot.Interval,
            Entry: entry,
            StopLoss: stopLoss,
            TakeProfit: entry - reward,
            Risk: risk,
            Reward: reward,
            Reason: $"Bias khung lớn ủng hộ SHORT, {DisplayInterval(snapshot.Interval)} có phản ứng quanh EMA20/breakdown ngắn.");
    }
}

internal static class IndicatorMath
{
    public static decimal Ema(IEnumerable<decimal> values, int period)
    {
        var items = values.ToArray();
        var multiplier = 2m / (period + 1);
        var ema = items[0];

        foreach (var value in items.Skip(1))
        {
            ema = (value - ema) * multiplier + ema;
        }

        return ema;
    }

    public static decimal Rsi(IReadOnlyList<decimal> closes, int period)
    {
        var gains = 0m;
        var losses = 0m;
        var start = Math.Max(1, closes.Count - period);

        for (var index = start; index < closes.Count; index++)
        {
            var change = closes[index] - closes[index - 1];
            if (change >= 0)
            {
                gains += change;
            }
            else
            {
                losses -= change;
            }
        }

        if (losses == 0)
        {
            return 100m;
        }

        var rs = gains / losses;
        return 100m - 100m / (1m + rs);
    }

    public static decimal Atr(IReadOnlyList<Candle> candles, int period)
    {
        var trueRanges = new List<decimal>();
        var start = Math.Max(1, candles.Count - period);

        for (var index = start; index < candles.Count; index++)
        {
            var current = candles[index];
            var previous = candles[index - 1];
            var range = Math.Max(
                current.High - current.Low,
                Math.Max(Math.Abs(current.High - previous.Close), Math.Abs(current.Low - previous.Close)));
            trueRanges.Add(range);
        }

        return trueRanges.Count == 0 ? 0 : trueRanges.Average();
    }
}

internal sealed class ChartAnalysisWatcher(
    BotConfig config,
    TelegramClient telegram,
    MarketDataClient marketData,
    OpenAiClient ai)
{
    private string? _lastSignalKey;
    private DateTimeOffset _lastSignalSentAt = DateTimeOffset.MinValue;
    private string? _lastErrorMessage;
    private DateTimeOffset _lastErrorSentAt = DateTimeOffset.MinValue;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (config.ChartAnalysisChatId is not long chatId)
        {
            Console.Error.WriteLine("CHART_ANALYSIS_ENABLED=true but CHART_ANALYSIS_CHAT_ID is missing.");
            return;
        }

        var period = TimeSpan.FromMinutes(config.ChartAnalysisPeriodMinutes);
        var intervalText = string.Join(",", config.ChartAnalysisIntervals);

        Console.WriteLine(
            $"[{DateTimeOffset.UtcNow:u}] Data watcher started for {config.ChartAnalysisSymbol}/{config.ChartAnalysisDataSymbol} [{intervalText}] every {period.TotalMinutes:0} minutes.");

        while (!cancellationToken.IsCancellationRequested)
        {
            await RunOnceAsync(chatId, cancellationToken);
            await Task.Delay(period, cancellationToken);
        }
    }

    private async Task RunOnceAsync(long chatId, CancellationToken cancellationToken)
    {
        try
        {
            var snapshots = new List<MarketSnapshot>();

            foreach (var interval in config.ChartAnalysisIntervals)
            {
                Console.WriteLine($"[{DateTimeOffset.UtcNow:u}] Loading market data {config.ChartAnalysisDataSymbol} {interval}.");
                snapshots.Add(await marketData.GetSnapshotAsync(interval, cancellationToken));
            }

            var signal = MarketSignalEngine.TryBuildSignal(snapshots, config);

            if (signal is null)
            {
                Console.WriteLine($"[{DateTimeOffset.UtcNow:u}] No data entry signal.");

                if (config.ChartAnalysisSendNoTrade)
                {
                    await telegram.SendMessageAsync(chatId, MarketSignalEngine.FormatNoTrade(snapshots), cancellationToken);
                }

                return;
            }

            var signalKey = BuildSignalKey(signal);
            if (_lastSignalKey == signalKey && DateTimeOffset.UtcNow - _lastSignalSentAt < TimeSpan.FromMinutes(30))
            {
                Console.WriteLine($"[{DateTimeOffset.UtcNow:u}] Duplicate entry signal skipped.");
                return;
            }

            await telegram.SendChatActionAsync(chatId, "typing", cancellationToken);
            var message = config.ChartAnalysisUseAi && !string.IsNullOrWhiteSpace(config.OpenAiApiKey)
                ? await ai.ExplainMarketSignalAsync(signal, snapshots, cancellationToken)
                : MarketSignalEngine.FormatSignal(signal, snapshots);

            if (!ChartSignalParser.HasEntrySignal(message))
            {
                message = MarketSignalEngine.FormatSignal(signal, snapshots);
            }

            await telegram.SendMessageAsync(chatId, message, cancellationToken);
            _lastSignalKey = signalKey;
            _lastSignalSentAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{DateTimeOffset.UtcNow:u}] Data watcher error: {ex.Message}");

            if (!ShouldSuppressErrorNotification(ex.Message))
            {
                await telegram.SendMessageAsync(chatId, $"Data watcher lỗi: {ex.Message}", cancellationToken);
                _lastErrorMessage = ex.Message;
                _lastErrorSentAt = DateTimeOffset.UtcNow;
            }
        }
    }

    private static string BuildSignalKey(TradeSignal signal)
        => $"{signal.Direction}:{signal.EntryInterval}:{Math.Round(signal.Entry, 1)}:{Math.Round(signal.StopLoss, 1)}:{Math.Round(signal.TakeProfit, 1)}";

    private bool ShouldSuppressErrorNotification(string errorMessage)
        => _lastErrorMessage == errorMessage &&
           DateTimeOffset.UtcNow - _lastErrorSentAt < TimeSpan.FromMinutes(30);
}

internal static class ChartSignalParser
{
    public static bool HasEntrySignal(string analysis)
        => analysis.Split('\n')
            .Take(6)
            .Any(line => line.Contains("SIGNAL:", StringComparison.OrdinalIgnoreCase) &&
                         line.Contains("ENTRY", StringComparison.OrdinalIgnoreCase));
}

internal sealed class TelegramClient(HttpClient httpClient, BotConfig config)
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, CancellationToken cancellationToken)
    {
        var url = $"{config.TelegramApiBase}/bot{config.TelegramBotToken}/getUpdates";
        var payload = JsonSerializer.Serialize(new
        {
            offset,
            timeout = 30,
            allowed_updates = new[] { "message" }
        });

        using var response = await httpClient.PostAsync(
            url,
            new StringContent(payload, Encoding.UTF8, "application/json"),
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var envelope = await JsonSerializer.DeserializeAsync<TelegramEnvelope<List<TelegramUpdate>>>(stream, _jsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Telegram returned an empty response.");

        if (!envelope.Ok)
        {
            throw new InvalidOperationException("Telegram API returned ok=false.");
        }

        return envelope.Result ?? [];
    }

    public Task SendChatActionAsync(long chatId, string action, CancellationToken cancellationToken)
        => PostWithoutResultAsync("sendChatAction", new
        {
            chat_id = chatId,
            action
        }, cancellationToken);

    public Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken)
        => PostWithoutResultAsync("sendMessage", new
        {
            chat_id = chatId,
            text = text.Length > 4000 ? text[..4000] : text
        }, cancellationToken);

    private async Task PostWithoutResultAsync(string method, object payloadObject, CancellationToken cancellationToken)
    {
        var url = $"{config.TelegramApiBase}/bot{config.TelegramBotToken}/{method}";
        var payload = JsonSerializer.Serialize(payloadObject);

        using var response = await httpClient.PostAsync(
            url,
            new StringContent(payload, Encoding.UTF8, "application/json"),
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }
}

internal sealed class OpenAiClient(HttpClient httpClient, BotConfig config)
{
    public async Task<string> GenerateReplyAsync(ConversationTurn currentTurn, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{config.OpenAiApiBase}/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.OpenAiApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var payload = JsonSerializer.Serialize(new
        {
            model = config.OpenAiModel,
            input = BuildInput(currentTurn)
        });

        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI API error {(int)response.StatusCode}: {raw}");
        }

        return ExtractTextReply(raw) ?? "AI khong tra ve noi dung hop le.";
    }

    public async Task<string> ExplainMarketSignalAsync(
        TradeSignal signal,
        IReadOnlyList<MarketSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.OpenAiApiKey))
        {
            return MarketSignalEngine.FormatSignal(signal, snapshots);
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{config.OpenAiApiBase}/responses");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.OpenAiApiKey);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var payload = JsonSerializer.Serialize(new
        {
            model = config.OpenAiModel,
            input = BuildSignalPrompt(signal, snapshots)
        });

        httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI API error {(int)response.StatusCode}: {raw}");
        }

        return ExtractTextReply(raw) ?? MarketSignalEngine.FormatSignal(signal, snapshots);
    }

    private string BuildInput(ConversationTurn currentTurn)
    {
        var builder = new StringBuilder();
        builder.AppendLine("System instructions:");
        builder.AppendLine(config.SystemPrompt);
        builder.AppendLine();
        builder.AppendLine("Current user message:");
        builder.AppendLine(currentTurn.Content);

        builder.AppendLine();
        builder.AppendLine("Reply naturally without relying on prior chat history.");

        return builder.ToString();
    }

    private static string BuildSignalPrompt(TradeSignal signal, IReadOnlyList<MarketSnapshot> snapshots)
        => $"""
        Hãy viết tín hiệu giao dịch XAUUSD bằng tiếng Việt có dấu, ngắn gọn, rõ ràng.
        Không phóng đại, không bảo đảm lợi nhuận, không nói chắc thắng.
        Giữ nguyên các mức giá đã tính, không tự đổi entry/SL/TP.

        Dữ liệu tín hiệu đã qua bộ lọc:
        - Hướng: {signal.Direction}
        - Điểm vào lệnh: {MarketSignalEngine.FormatPrice(signal.Entry)}
        - SL: {MarketSignalEngine.FormatPrice(signal.StopLoss)}
        - TP: {MarketSignalEngine.FormatPrice(signal.TakeProfit)}
        - Rủi ro: {MarketSignalEngine.FormatPrice(signal.Risk)} giá
        - Mục tiêu: {MarketSignalEngine.FormatPrice(signal.Reward)} giá
        - Lý do: {signal.Reason}

        Bối cảnh đa khung:
        {MarketSignalEngine.FormatMarketContext(snapshots)}

        Format bắt buộc:
        SIGNAL: ENTRY
        HƯỚNG:
        ĐIỂM VÀO LỆNH:
        SL:
        TP:
        RR:
        SETUP:
        INVALIDATION:
        GHI CHÚ RỦI RO:
        """;

    private static string? ExtractTextReply(string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        var root = document.RootElement;

        if (root.TryGetProperty("output_text", out var outputTextElement) &&
            outputTextElement.ValueKind == JsonValueKind.String)
        {
            return outputTextElement.GetString();
        }

        if (root.TryGetProperty("output", out var outputElement) &&
            outputElement.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();

            foreach (var outputItem in outputElement.EnumerateArray())
            {
                if (!outputItem.TryGetProperty("content", out var contentElement) ||
                    contentElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var contentItem in contentElement.EnumerateArray())
                {
                    if (contentItem.TryGetProperty("text", out var textElement) &&
                        textElement.ValueKind == JsonValueKind.String)
                    {
                        parts.Add(textElement.GetString()!);
                    }
                }
            }

            if (parts.Count > 0)
            {
                return string.Join("\n", parts);
            }
        }

        return null;
    }
}

internal sealed class TelegramEnvelope<T>
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("result")]
    public T? Result { get; init; }
}

internal sealed class TelegramUpdate
{
    [JsonPropertyName("update_id")]
    public long UpdateId { get; init; }

    [JsonPropertyName("message")]
    public TelegramMessage? Message { get; init; }
}

internal sealed class TelegramMessage
{
    [JsonPropertyName("message_id")]
    public long MessageId { get; init; }

    [JsonPropertyName("chat")]
    public TelegramChat? Chat { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }
}

internal sealed class TelegramChat
{
    [JsonPropertyName("id")]
    public long Id { get; init; }
}
