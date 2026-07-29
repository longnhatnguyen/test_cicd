using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var config = BotConfig.LoadFromEnvironment();
using var httpClient = new HttpClient();

var telegram = new TelegramClient(httpClient, config);
var ai = new OpenAiClient(httpClient, config);

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
                    "Xin chào tôi là Cáp Tần.\n\n" +
                    "Cứ hỏi thoải mái nhé, tôi sẽ trả lời.\n",
                    CancellationToken.None);
                continue;
            }

            await telegram.SendChatActionAsync(chatId, "typing", CancellationToken.None);

            string reply;

            if (string.IsNullOrWhiteSpace(config.OpenAiApiKey))
            {
                reply = "Bot da nhan tin nhan, nhung chua co OPENAI_API_KEY de goi AI.";
            }
            else
            {
                reply = await ai.GenerateReplyAsync(messageText, CancellationToken.None);
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
    string SystemPrompt)
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
                ?? "Ban la tro ly Telegram gon gang, lich su va huu ich. Tra loi bang tieng Viet neu nguoi dung viet tieng Viet.");
    }
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
    public async Task<string> GenerateReplyAsync(string userMessage, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{config.OpenAiApiBase}/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.OpenAiApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var input = $"System instructions:\n{config.SystemPrompt}\n\nUser message:\n{userMessage}";
        var payload = JsonSerializer.Serialize(new
        {
            model = config.OpenAiModel,
            input
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
