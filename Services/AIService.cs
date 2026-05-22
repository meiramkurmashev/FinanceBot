using FinanceBot.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FinanceBot.Services;

/// <summary>
/// Сервис для работы с Groq AI (Llama 3.3 + Whisper)
/// - Текст парсит через Llama 3.3-70b
/// - Голос транскрибирует через Whisper, затем парсит Llama
/// </summary>
public class AIService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly ILogger<AIService> _logger;

    private const string ChatUrl = "https://api.groq.com/openai/v1/chat/completions";
    private const string WhisperUrl = "https://api.groq.com/openai/v1/audio/transcriptions";
    private const string Model = "llama-3.3-70b-versatile";

    private const string SystemPrompt =
        "Ты — парсер финансовых транзакций для личного учёта. " +
        "Всегда отвечай ТОЛЬКО валидным JSON без markdown, без пояснений. " +
        "Язык сообщений: русский.";

    public AIService(IConfiguration config, HttpClient http, ILogger<AIService> logger)
    {
        _logger = logger;
        _http = http;
        _apiKey = config["BotSettings:GroqApiKey"]
            ?? throw new InvalidOperationException("GroqApiKey не настроен в appsettings.json");

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    /// <summary>
    /// Парсит текстовое сообщение → структурированная транзакция
    /// </summary>
    public async Task<ParsedTransaction?> ParseTextMessageAsync(string message)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var yesterday = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd");

        var userPrompt =
            $"Разбери это сообщение о финансовой транзакции: \"{message}\"\n\n" +
            $"Сегодня: {today}, Вчера: {yesterday}\n\n" +
            "Верни JSON строго в таком формате:\n" +
            "{\n" +
            "  \"amount\": 500,\n" +
            "  \"type\": \"expense\",\n" +
            "  \"categoryName\": \"Еда\",\n" +
            "  \"accountName\": null,\n" +
            "  \"comment\": \"кофе\",\n" +
            "  \"date\": null\n" +
            "}\n\n" +
            "Правила:\n" +
            "- type: \"expense\" если потратил/купил/заплатил, \"income\" если получил/зарплата/заработал\n" +
            "- amount: только число (пятьсот=500, тысяча=1000, пять тысяч=5000)\n" +
            "- categoryName: угадай (кофе/ресторан→\"Еда\", такси/автобус→\"Транспорт\", аптека→\"Здоровье\", зарплата→\"Зарплата\")\n" +
            "- accountName: только если явно сказано (наличкой→\"Наличка\", со сбера→\"Сбер\", с каспия→\"Каспий\"), иначе null\n" +
            "- comment: краткое описание что купил\n" +
            $"- date: если \"вчера\"→\"{yesterday}\", \"сегодня\"→\"{today}\", иначе null\n" +
            "- Если что-то неизвестно — ставь null";

        return await CallLlamaAsync(userPrompt);
    }

    /// <summary>
    /// Голосовое сообщение → транскрипция (Whisper) → парсинг (Llama)
    /// </summary>
    public async Task<ParsedTransaction?> ParseVoiceMessageAsync(byte[] audioBytes)
    {
        // Шаг 1: транскрибируем голос через Groq Whisper
        var transcribedText = await TranscribeAudioAsync(audioBytes);
        if (string.IsNullOrWhiteSpace(transcribedText))
        {
            _logger.LogWarning("Whisper не смог транскрибировать аудио");
            return null;
        }

        _logger.LogInformation("Голос распознан: {Text}", transcribedText);

        // Шаг 2: парсим распознанный текст как обычное сообщение
        return await ParseTextMessageAsync(transcribedText);
    }

    // --- Приватные методы ---

    private async Task<ParsedTransaction?> CallLlamaAsync(string userPrompt)
    {
        try
        {
            var body = new
            {
                model = Model,
                messages = new[]
                {
                    new { role = "system", content = SystemPrompt },
                    new { role = "user",   content = userPrompt }
                },
                max_tokens = 300,
                temperature = 0.1  // Низкая температура = точнее следует инструкции
            };

            var content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");

            var response = await _http.PostAsync(ChatUrl, content);
            var rawJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Groq API ошибка {Code}: {Body}", response.StatusCode, rawJson);
                return null;
            }

            // Извлекаем текст из ответа Groq
            var doc = JsonNode.Parse(rawJson);
            var text = doc?["choices"]?[0]?["message"]?["content"]?.ToString() ?? string.Empty;

            _logger.LogDebug("Groq ответил: {Text}", text);

            return DeserializeTransaction(CleanJson(text));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при обращении к Groq API");
            return null;
        }
    }

    private async Task<string?> TranscribeAudioAsync(byte[] audioBytes)
    {
        try
        {
            // Groq Whisper принимает multipart/form-data
            using var formData = new MultipartFormDataContent();

            var audioContent = new ByteArrayContent(audioBytes);
            audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/ogg");
            formData.Add(audioContent, "file", "voice.ogg");
            formData.Add(new StringContent("whisper-large-v3"), "model");
            formData.Add(new StringContent("ru"), "language");  // Подсказываем язык

            var response = await _http.PostAsync(WhisperUrl, formData);
            var rawJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Whisper ошибка {Code}: {Body}", response.StatusCode, rawJson);
                return null;
            }

            var doc = JsonNode.Parse(rawJson);
            return doc?["text"]?.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при транскрипции аудио");
            return null;
        }
    }

    private static string CleanJson(string text)
    {
        // Убираем markdown если модель добавила ```json ... ```
        text = text.Replace("```json", "").Replace("```", "").Trim();
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start)
            return text[start..(end + 1)];
        return text;
    }

    private ParsedTransaction? DeserializeTransaction(string json)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<ParsedTransaction>(json, options);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось разобрать JSON: {Json}", json);
            return null;
        }
    }
}
