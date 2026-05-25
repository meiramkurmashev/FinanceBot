using FinanceBot.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FinanceBot.Services;

/// <summary>
/// Groq AI (Llama 3.3-70b + Whisper).
/// Ключевое улучшение: получает список категорий пользователя и матчит их сам.
/// </summary>
public class AIService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger<AIService> _logger;

    private const string ChatUrl   = "https://api.groq.com/openai/v1/chat/completions";
    private const string WhisperUrl = "https://api.groq.com/openai/v1/audio/transcriptions";
    private const string Model     = "llama-3.3-70b-versatile";

    public AIService(IConfiguration config, HttpClient http, ILogger<AIService> logger)
    {
        _logger = logger;
        _http   = http;
        _apiKey = config["BotSettings:GroqApiKey"]
            ?? throw new InvalidOperationException("GroqApiKey не настроен");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    /// <summary>
    /// Парсит сообщение. Передаём список категорий — AI сам матчит.
    /// </summary>
    public async Task<ParsedTransaction?> ParseTextMessageAsync(
        string message,
        IEnumerable<Category> userCategories,
        IEnumerable<Account> userAccounts)
    {
        var today     = DateTime.Today.ToString("yyyy-MM-dd");
        var yesterday = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd");

        // Формируем строки для промпта
        var catList = string.Join(", ",
            userCategories.Select(c => $"{c.Emoji}{c.Name}({(c.Type == CategoryType.Income ? "доход" : "расход")})"));
        var accList = string.Join(", ",
            userAccounts.Select(a => $"{a.Emoji}{a.Name}"));

        var hasCats = !string.IsNullOrEmpty(catList);
        var hasAccs = !string.IsNullOrEmpty(accList);

        var prompt =
            $"Разбери сообщение о финансовой транзакции: \"{message}\"\n\n" +
            $"Сегодня: {today}, Вчера: {yesterday}\n\n" +
            (hasCats ? $"КАТЕГОРИИ ПОЛЬЗОВАТЕЛЯ (используй только их!): {catList}\n\n" : "") +
            (hasAccs ? $"СЧЕТА ПОЛЬЗОВАТЕЛЯ: {accList}\n\n" : "") +
            "Верни ТОЛЬКО JSON (без markdown):\n" +
            "{\n" +
            "  \"amount\": число или null,\n" +
            "  \"type\": \"expense\" или \"income\" или null,\n" +
            "  \"categoryName\": \"точное название из списка или null\",\n" +
            "  \"accountName\": \"точное название из списка или null\",\n" +
            "  \"comment\": \"краткое описание или null\",\n" +
            "  \"date\": \"YYYY-MM-DD или null\"\n" +
            "}\n\n" +
            "ПРАВИЛА (строго):\n" +
            "- amount: только число. Слова: пятьсот=500, тысяча=1000, пять тысяч=5000, млн=1000000\n" +
            "- type: expense если \"потратил/купил/заплатил/плачу\", income если \"получил/заработал/зарплата/пришло\"\n" +
            (hasCats
                ? "- categoryName: найди наиболее подходящую категорию из списка выше. Если подходит — ОБЯЗАТЕЛЬНО укажи. null только если совсем не подходит ни одна.\n"
                : "- categoryName: угадай (кофе→Еда, такси→Транспорт, зарплата→Доход)\n") +
            (hasAccs
                ? "- accountName: если упомянут счёт (наличка/каспий/банк) — укажи из списка. Иначе null.\n"
                : "- accountName: null\n") +
            $"- date: \"вчера\"={yesterday}, \"сегодня\"={today}, иначе null\n" +
            "- comment: что именно купил/за что. НЕ дублируй categoryName.\n" +
            "- Будь уверен! Не оставляй null если можно определить.";

        return await CallLlamaAsync(prompt);
    }

    /// <summary>
    /// Голос → Whisper транскрипция → ParseTextMessageAsync
    /// </summary>
    public async Task<(ParsedTransaction? parsed, string? transcription)> ParseVoiceMessageAsync(
        byte[] audioBytes,
        IEnumerable<Category> userCategories,
        IEnumerable<Account> userAccounts)
    {
        var text = await TranscribeAudioAsync(audioBytes);
        if (string.IsNullOrWhiteSpace(text))
            return (null, null);

        var parsed = await ParseTextMessageAsync(text, userCategories, userAccounts);
        return (parsed, text);
    }

    // --- Приватные методы ---

    private async Task<ParsedTransaction?> CallLlamaAsync(string userPrompt)
    {
        try
        {
            var body = new
            {
                model       = Model,
                messages    = new[]
                {
                    new { role = "system", content = "Ты парсер финансов. Отвечай ТОЛЬКО валидным JSON. Никаких пояснений." },
                    new { role = "user",   content = userPrompt }
                },
                max_tokens  = 300,
                temperature = 0.1
            };

            var content  = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(ChatUrl, content);
            var rawJson  = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Groq error {Code}: {Body}", response.StatusCode, rawJson);
                return null;
            }

            var doc  = JsonNode.Parse(rawJson);
            var text = doc?["choices"]?[0]?["message"]?["content"]?.ToString() ?? "";
            _logger.LogDebug("Groq: {Text}", text);

            return Deserialize(CleanJson(text));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка Groq API");
            return null;
        }
    }

    private async Task<string?> TranscribeAudioAsync(byte[] audioBytes)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            var audio = new ByteArrayContent(audioBytes);
            audio.Headers.ContentType = new MediaTypeHeaderValue("audio/ogg");
            form.Add(audio, "file", "voice.ogg");
            form.Add(new StringContent("whisper-large-v3"), "model");
            form.Add(new StringContent("ru"), "language");

            var response = await _http.PostAsync(WhisperUrl, form);
            var raw      = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) return null;

            return JsonNode.Parse(raw)?["text"]?.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка Whisper");
            return null;
        }
    }

    private static string CleanJson(string text)
    {
        text = text.Replace("```json", "").Replace("```", "").Trim();
        var s = text.IndexOf('{');
        var e = text.LastIndexOf('}');
        return s >= 0 && e > s ? text[s..(e + 1)] : text;
    }

    private ParsedTransaction? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ParsedTransaction>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось разобрать JSON: {Json}", json);
            return null;
        }
    }
}
