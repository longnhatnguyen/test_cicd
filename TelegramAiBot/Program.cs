using System.Net.Http.Headers;
using System.Diagnostics;
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
var chartCapture = new TradingViewChartCapture(config);
var chartWatcher = new ChartAnalysisWatcher(config, telegram, chartCapture, ai);

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
                await telegram.SendChatActionAsync(chatId, "upload_photo", CancellationToken.None);
                string? screenshotPath = null;

                try
                {
                    screenshotPath = await chartCapture.CaptureAsync(chartRequest, CancellationToken.None);
                    await using (var screenshot = File.OpenRead(screenshotPath))
                    {
                        await telegram.SendPhotoAsync(
                            chatId,
                            screenshot,
                            Path.GetFileName(screenshotPath),
                            $"Chart {chartRequest.Symbol} {chartRequest.Interval}",
                            CancellationToken.None);
                    }

                    if (chartRequest.Analyze)
                    {
                        await telegram.SendChatActionAsync(chatId, "typing", CancellationToken.None);
                        var analysis = await ai.AnalyzeChartAsync(screenshotPath, chartRequest, CancellationToken.None);
                        await telegram.SendMessageAsync(chatId, analysis, CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    await telegram.SendMessageAsync(
                        chatId,
                        $"Khong chup duoc chart: {ex.Message}",
                        CancellationToken.None);
                }
                finally
                {
                    if (!string.IsNullOrWhiteSpace(screenshotPath) && File.Exists(screenshotPath))
                    {
                        File.Delete(screenshotPath);
                    }
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
    string ChartCaptureScriptPath,
    string ChartCaptureOutputDirectory,
    int ChartCaptureTimeoutSeconds,
    bool ChartAnalysisEnabled,
    long? ChartAnalysisChatId,
    string ChartAnalysisUrl,
    string ChartAnalysisSymbol,
    IReadOnlyList<string> ChartAnalysisIntervals,
    int ChartAnalysisPeriodMinutes,
    bool ChartAnalysisSendNoTrade)
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
            ChartCaptureScriptPath: Environment.GetEnvironmentVariable("CHART_CAPTURE_SCRIPT_PATH")?.Trim()
                ?? Path.Combine(AppContext.BaseDirectory, "scripts", "capture-tradingview-chart.js"),
            ChartCaptureOutputDirectory: Environment.GetEnvironmentVariable("CHART_CAPTURE_OUTPUT_DIRECTORY")?.Trim()
                ?? Path.GetTempPath(),
            ChartCaptureTimeoutSeconds: ReadInt("CHART_CAPTURE_TIMEOUT_SECONDS", 45),
            ChartAnalysisEnabled: ReadBool("CHART_ANALYSIS_ENABLED", false),
            ChartAnalysisChatId: ReadLong("CHART_ANALYSIS_CHAT_ID"),
            ChartAnalysisUrl: Environment.GetEnvironmentVariable("CHART_ANALYSIS_URL")?.Trim() ?? string.Empty,
            ChartAnalysisSymbol: TradingViewSymbolResolver.Resolve(
                Environment.GetEnvironmentVariable("CHART_ANALYSIS_URL")?.Trim(),
                Environment.GetEnvironmentVariable("CHART_ANALYSIS_SYMBOL")?.Trim() ?? "OANDA:XAUUSD"),
            ChartAnalysisIntervals: ReadIntervals(),
            ChartAnalysisPeriodMinutes: ReadInt("CHART_ANALYSIS_PERIOD_MINUTES", 5),
            ChartAnalysisSendNoTrade: ReadBool("CHART_ANALYSIS_SEND_NO_TRADE", false));
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

internal sealed record ChartImage(ChartRequest Request, string Path);

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

internal sealed class TradingViewChartCapture(BotConfig config)
{
    public async Task<string> CaptureAsync(ChartRequest request, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(config.ChartCaptureOutputDirectory);

        var outputPath = Path.Combine(
            config.ChartCaptureOutputDirectory,
            $"tradingview-{SanitizeFileName(request.Symbol)}-{SanitizeFileName(request.Interval)}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.png");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(config.ChartCaptureTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var scriptPath = Path.GetFullPath(config.ChartCaptureScriptPath);
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("Khong tim thay script chup chart.", scriptPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "node",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("--symbol");
        startInfo.ArgumentList.Add(request.Symbol);
        startInfo.ArgumentList.Add("--interval");
        startInfo.ArgumentList.Add(request.Interval);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Khong start duoc Node process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(linked.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(linked.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw new TimeoutException($"Qua {config.ChartCaptureTimeoutSeconds}s van chua chup xong chart.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim());
        }

        if (!File.Exists(outputPath))
        {
            throw new InvalidOperationException("Script chay xong nhung khong tao file anh.");
        }

        return outputPath;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            builder.Append(invalid.Contains(character) ? '-' : character);
        }

        return builder.ToString();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cleanup for timed-out browser captures.
        }
    }
}

internal sealed class ChartAnalysisWatcher(
    BotConfig config,
    TelegramClient telegram,
    TradingViewChartCapture chartCapture,
    OpenAiClient ai)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (config.ChartAnalysisChatId is not long chatId)
        {
            Console.Error.WriteLine("CHART_ANALYSIS_ENABLED=true but CHART_ANALYSIS_CHAT_ID is missing.");
            return;
        }

        var requests = config.ChartAnalysisIntervals
            .Select(interval => new ChartRequest(config.ChartAnalysisSymbol, interval, Analyze: true))
            .ToArray();
        var period = TimeSpan.FromMinutes(config.ChartAnalysisPeriodMinutes);
        var intervalText = string.Join(",", requests.Select(request => request.Interval));

        Console.WriteLine(
            $"[{DateTimeOffset.UtcNow:u}] Chart watcher started for {config.ChartAnalysisSymbol} [{intervalText}] every {period.TotalMinutes:0} minutes.");

        while (!cancellationToken.IsCancellationRequested)
        {
            await RunOnceAsync(chatId, requests, cancellationToken);
            await Task.Delay(period, cancellationToken);
        }
    }

    private async Task RunOnceAsync(long chatId, IReadOnlyList<ChartRequest> requests, CancellationToken cancellationToken)
    {
        var screenshots = new List<ChartImage>();

        try
        {
            await telegram.SendChatActionAsync(chatId, "upload_photo", cancellationToken);

            foreach (var request in requests)
            {
                var screenshotPath = await chartCapture.CaptureAsync(request, cancellationToken);
                screenshots.Add(new ChartImage(request, screenshotPath));
            }

            await telegram.SendChatActionAsync(chatId, "typing", cancellationToken);
            var analysis = await ai.AnalyzeChartsAsync(screenshots, cancellationToken);

            if (!ChartSignalParser.HasEntrySignal(analysis) && !config.ChartAnalysisSendNoTrade)
            {
                Console.WriteLine($"[{DateTimeOffset.UtcNow:u}] No chart entry signal.");
                return;
            }

            foreach (var chartImage in screenshots)
            {
                await using var screenshot = File.OpenRead(chartImage.Path);
                await telegram.SendPhotoAsync(
                    chatId,
                    screenshot,
                    Path.GetFileName(chartImage.Path),
                    $"Chart {chartImage.Request.Symbol} {chartImage.Request.Interval}",
                    cancellationToken);
            }

            await telegram.SendMessageAsync(chatId, analysis, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{DateTimeOffset.UtcNow:u}] Chart watcher error: {ex.Message}");
            await telegram.SendMessageAsync(chatId, $"Chart watcher loi: {ex.Message}", cancellationToken);
        }
        finally
        {
            foreach (var screenshot in screenshots)
            {
                if (File.Exists(screenshot.Path))
                {
                    File.Delete(screenshot.Path);
                }
            }
        }
    }
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

    public async Task SendPhotoAsync(
        long chatId,
        Stream photo,
        string fileName,
        string caption,
        CancellationToken cancellationToken)
    {
        var url = $"{config.TelegramApiBase}/bot{config.TelegramBotToken}/sendPhoto";
        using var form = new MultipartFormDataContent
        {
            { new StringContent(chatId.ToString()), "chat_id" },
            { new StringContent(caption), "caption" }
        };
        using var photoContent = new StreamContent(photo);
        photoContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(photoContent, "photo", fileName);

        using var response = await httpClient.PostAsync(url, form, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

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

    public async Task<string> AnalyzeChartAsync(
        string imagePath,
        ChartRequest request,
        CancellationToken cancellationToken)
        => await AnalyzeChartsAsync([new ChartImage(request, imagePath)], cancellationToken);

    public async Task<string> AnalyzeChartsAsync(
        IReadOnlyList<ChartImage> chartImages,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.OpenAiApiKey))
        {
            return "Da chup duoc chart, nhung chua co OPENAI_API_KEY de phan tich anh.";
        }

        if (chartImages.Count == 0)
        {
            return "Khong co anh chart de phan tich.";
        }

        var contentItems = new List<object>
        {
            new
            {
                type = "input_text",
                text = BuildChartAnalysisPrompt(chartImages.Select(image => image.Request).ToArray())
            }
        };

        foreach (var chartImage in chartImages)
        {
            var imageBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(chartImage.Path, cancellationToken));
            contentItems.Add(new
            {
                type = "input_text",
                text = $"Anh chart timeframe {chartImage.Request.Interval} cho {chartImage.Request.Symbol}."
            });
            contentItems.Add(new
            {
                type = "input_image",
                image_url = $"data:image/png;base64,{imageBase64}",
                detail = "high"
            });
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{config.OpenAiApiBase}/responses");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.OpenAiApiKey);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var payload = JsonSerializer.Serialize(new
        {
            model = config.OpenAiModel,
            input = new object[]
            {
                new
                {
                    role = "user",
                    content = contentItems
                }
            }
        });

        httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI API error {(int)response.StatusCode}: {raw}");
        }

        return ExtractTextReply(raw) ?? "AI khong tra ve phan tich hop le.";
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

    private static string BuildChartAnalysisPrompt(IReadOnlyList<ChartRequest> requests)
    {
        var symbol = requests[0].Symbol;
        var intervals = string.Join(", ", requests.Select(request => request.Interval));

        return $"""
        Bạn là trợ lý phân tích kỹ thuật đa khung thời gian cho chart {symbol}.
        Các ảnh chart được cung cấp theo timeframe: {intervals}.
        Đọc tất cả ảnh chart và trả lời bằng tiếng Việt có dấu, ngắn gọn, có cấu trúc.

        Luật quyết định:
        - Bắt đầu câu trả lời bằng đúng một trong hai dòng:
          SIGNAL: ENTRY
          SIGNAL: NO_TRADE
        - Chỉ dùng SIGNAL: ENTRY khi có setup rõ, có đủ điểm vào lệnh, SL, TP, invalidation và có sự đồng thuận hợp lý giữa khung lớn và khung vào lệnh.
        - Nếu xu hướng khung lớn và khung nhỏ mâu thuẫn, giá đang ở giữa range, SL không rõ, RR kém, hoặc ảnh không rõ thì dùng SIGNAL: NO_TRADE.
        - Không đưa lời khuyên tài chính chắc chắn, không bảo đảm lợi nhuận.
        - Chỉ đưa setup theo kịch bản xác suất.
        - Ưu tiên quản trị rủi ro: mọi lệnh phải có invalidation rõ ràng.
        - Không tự đặt lệnh, không nói "chắc thắng".

        Cách phân tích:
        - Khung lớn: xác định bias/xu hướng, vùng cung cầu, hỗ trợ, kháng cự.
        - Khung trung: xác định cấu trúc, pullback, breakout hoặc retest.
        - Khung nhỏ: chỉ dùng để canh entry nếu bias lớn ủng hộ.
        - Nếu có setup, nêu rõ hướng LONG/SHORT, điểm vào lệnh, stop loss, take profit, RR ước tính, điều kiện xác nhận và điều kiện vô hiệu.

        Format:
        SIGNAL:
        HƯỚNG:
        ĐA KHUNG:
        VÙNG QUAN TRỌNG:
        SETUP:
        ĐIỂM VÀO LỆNH:
        SL:
        TP:
        RR:
        INVALIDATION:
        GHI CHÚ RỦI RO:
        """;
    }

    private static string BuildChartAnalysisPrompt(ChartRequest request)
        => $"""
        Bạn là trợ lý phân tích kỹ thuật cho chart {request.Symbol} timeframe {request.Interval}.
        Đọc ảnh chart được cung cấp và trả lời bằng tiếng Việt có dấu, ngắn gọn, có cấu trúc.

        Yêu cầu:
        - Nếu ảnh không rõ giá/timeframe/nến/indicator thì nói rõ không đủ dữ liệu.
        - Không đưa lời khuyên tài chính chắc chắn, không bảo đảm lợi nhuận.
        - Chỉ đưa setup theo kịch bản xác suất.
        - Nếu có setup, nêu rõ: xu hướng, hỗ trợ/kháng cự, điểm vào lệnh, stop loss, take profit, điều kiện xác nhận, điều kiện vô hiệu.
        - Nếu không có setup đẹp, hãy nói "Đứng ngoài" và nêu lý do.
        - Ưu tiên quản trị rủi ro: mỗi lệnh nên có invalidation rõ ràng.

        Format:
        XU HƯỚNG:
        VÙNG QUAN TRỌNG:
        SETUP:
        ĐIỂM VÀO LỆNH:
        SL:
        TP:
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
