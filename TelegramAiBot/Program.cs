using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;

var config = BotConfig.LoadFromEnvironment();
using var httpClient = new HttpClient();
await using var store = new PostgresConversationStore(config);

await store.InitializeAsync(CancellationToken.None);

var telegram = new TelegramClient(httpClient, config);
var ai = new OpenAiClient(httpClient, config);
var accessControl = new AccessControl(config, store);

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

            if (!await accessControl.IsAuthorizedAsync(chatId, CancellationToken.None))
            {
                var accessReply = await accessControl.TryAuthorizeAsync(chatId, messageText, CancellationToken.None);
                await telegram.SendMessageAsync(chatId, accessReply, CancellationToken.None);
                continue;
            }

            await store.AppendMessageAsync(chatId, "user", messageText, CancellationToken.None);

            string reply;

            if (string.IsNullOrWhiteSpace(config.OpenAiApiKey))
            {
                reply = "Bot da nhan tin nhan, nhung chua co OPENAI_API_KEY de goi AI.";
            }
            else
            {
                await telegram.SendChatActionAsync(chatId, "typing", CancellationToken.None);
                var history = await store.GetRecentMessagesAsync(chatId, config.MaxConversationMessages, CancellationToken.None);
                reply = await ai.GenerateReplyAsync(history, CancellationToken.None);
            }

            await store.AppendMessageAsync(chatId, "assistant", reply, CancellationToken.None);
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
    string PostgresConnectionString,
    int MaxConversationMessages,
    int StoredMessageLimit,
    int MaxMessageCharactersPerTurn)
{
    public static BotConfig LoadFromEnvironment()
    {
        var telegramBotToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(telegramBotToken))
        {
            throw new InvalidOperationException("Missing TELEGRAM_BOT_TOKEN environment variable.");
        }

        var postgresConnectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(postgresConnectionString))
        {
            throw new InvalidOperationException("Missing POSTGRES_CONNECTION_STRING environment variable.");
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
            PostgresConnectionString: postgresConnectionString,
            MaxConversationMessages: ReadInt("MAX_CONVERSATION_MESSAGES", 24),
            StoredMessageLimit: ReadInt("STORED_MESSAGE_LIMIT", 30),
            MaxMessageCharactersPerTurn: ReadInt("MAX_MESSAGE_CHARACTERS_PER_TURN", 800));
    }

    private static int ReadInt(string variableName, int fallbackValue)
    {
        var raw = Environment.GetEnvironmentVariable(variableName)?.Trim();
        return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : fallbackValue;
    }
}

internal sealed class AccessControl(BotConfig config, PostgresConversationStore store)
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

internal sealed class PostgresConversationStore(BotConfig config) : IAsyncDisposable
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS telegram_chat_sessions (
            chat_id BIGINT PRIMARY KEY,
            is_authorized BOOLEAN NOT NULL DEFAULT FALSE,
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS telegram_messages (
            id BIGSERIAL PRIMARY KEY,
            chat_id BIGINT NOT NULL REFERENCES telegram_chat_sessions(chat_id) ON DELETE CASCADE,
            role TEXT NOT NULL CHECK (role IN ('user', 'assistant')),
            content TEXT NOT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE INDEX IF NOT EXISTS ix_telegram_messages_chat_id_id_desc
            ON telegram_messages (chat_id, id DESC);
        """;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(config.PostgresConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(SchemaSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> IsAuthorizedAsync(long chatId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT is_authorized FROM telegram_chat_sessions WHERE chat_id = @chat_id;",
            connection);
        command.Parameters.AddWithValue("chat_id", chatId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is bool isAuthorized && isAuthorized;
    }

    public async Task SetAuthorizedAsync(long chatId, bool isAuthorized, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO telegram_chat_sessions (chat_id, is_authorized, updated_at)
            VALUES (@chat_id, @is_authorized, NOW())
            ON CONFLICT (chat_id) DO UPDATE
            SET is_authorized = EXCLUDED.is_authorized,
                updated_at = NOW();
            """,
            connection);
        command.Parameters.AddWithValue("chat_id", chatId);
        command.Parameters.AddWithValue("is_authorized", isAuthorized);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AppendMessageAsync(long chatId, string role, string content, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        await using (var sessionCommand = new NpgsqlCommand(
            """
            INSERT INTO telegram_chat_sessions (chat_id, is_authorized, updated_at)
            VALUES (@chat_id, FALSE, NOW())
            ON CONFLICT (chat_id) DO UPDATE
            SET updated_at = NOW();
            """,
            connection))
        {
            sessionCommand.Parameters.AddWithValue("chat_id", chatId);
            await sessionCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insertMessageCommand = new NpgsqlCommand(
            """
            INSERT INTO telegram_messages (chat_id, role, content)
            VALUES (@chat_id, @role, @content);
            """,
            connection))
        {
            insertMessageCommand.Parameters.AddWithValue("chat_id", chatId);
            insertMessageCommand.Parameters.AddWithValue("role", role);
            insertMessageCommand.Parameters.AddWithValue("content", content);
            await insertMessageCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var trimCommand = new NpgsqlCommand(
            """
            DELETE FROM telegram_messages
            WHERE chat_id = @chat_id
              AND id NOT IN (
                  SELECT id
                  FROM telegram_messages
                  WHERE chat_id = @chat_id
                  ORDER BY id DESC
                  LIMIT @retain
              );
            """,
            connection);
        trimCommand.Parameters.AddWithValue("chat_id", chatId);
        trimCommand.Parameters.AddWithValue("retain", config.StoredMessageLimit);
        await trimCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ConversationTurn>> GetRecentMessagesAsync(long chatId, int limit, CancellationToken cancellationToken)
    {
        var items = new List<ConversationTurn>();

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT role, content
            FROM (
                SELECT role, content, id
                FROM telegram_messages
                WHERE chat_id = @chat_id
                ORDER BY id DESC
                LIMIT @limit
            ) recent
            ORDER BY id ASC;
            """,
            connection);
        command.Parameters.AddWithValue("chat_id", chatId);
        command.Parameters.AddWithValue("limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new ConversationTurn(
                Role: reader.GetString(0),
                Content: LimitCharacters(reader.GetString(1), config.MaxMessageCharactersPerTurn)));
        }

        return items;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(config.PostgresConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static string LimitCharacters(string input, int limit)
        => input.Length <= limit ? input : input[..limit];
}

internal sealed record ConversationTurn(string Role, string Content);

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
    public async Task<string> GenerateReplyAsync(IReadOnlyList<ConversationTurn> history, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{config.OpenAiApiBase}/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.OpenAiApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var payload = JsonSerializer.Serialize(new
        {
            model = config.OpenAiModel,
            input = BuildInput(history)
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

    private string BuildInput(IReadOnlyList<ConversationTurn> history)
    {
        var builder = new StringBuilder();
        builder.AppendLine("System instructions:");
        builder.AppendLine(config.SystemPrompt);
        builder.AppendLine();
        builder.AppendLine("Conversation history:");

        foreach (var turn in history)
        {
            var speaker = turn.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "Assistant" : "User";
            builder.AppendLine($"{speaker}: {turn.Content}");
        }

        builder.AppendLine();
        builder.AppendLine("Reply to the latest user message naturally and keep continuity with the prior context.");

        return builder.ToString();
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
