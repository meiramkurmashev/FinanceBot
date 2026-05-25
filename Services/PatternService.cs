using FinanceBot.Data;
using FinanceBot.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceBot.Services;

/// <summary>
/// Обучение бота на основе выборов пользователя.
///
/// Когда пользователь вручную выбирает категорию/счёт — сохраняем связку.
/// В следующий раз используем её автоматически.
///
/// Пример: "заработал" → категория "Работа", тип income
/// </summary>
public class PatternService
{
    private readonly AppDbContext _db;
    private readonly ILogger<PatternService> _logger;

    public PatternService(AppDbContext db, ILogger<PatternService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Ищет сохранённые паттерны по словам из сообщения.
    /// Возвращает наиболее часто используемый паттерн.
    /// </summary>
    public async Task<UserPattern?> FindPatternAsync(long userId, string message)
    {
        var words = ExtractKeywords(message);
        if (!words.Any()) return null;

        var patterns = await _db.UserPatterns
            .Include(p => p.Category)
            .Include(p => p.Account)
            .Where(p => p.UserTelegramId == userId && words.Contains(p.Keyword))
            .OrderByDescending(p => p.UseCount)
            .ThenByDescending(p => p.LastUsed)
            .ToListAsync();

        return patterns.FirstOrDefault();
    }

    /// <summary>
    /// Сохраняет выбор пользователя как паттерн для будущих сообщений.
    /// Вызывается после того как транзакция успешно создана.
    /// </summary>
    public async Task LearnAsync(
        long userId,
        string originalMessage,
        int? categoryId,
        int? accountId,
        string? transactionType)
    {
        var keywords = ExtractKeywords(originalMessage);

        foreach (var keyword in keywords)
        {
            var existing = await _db.UserPatterns
                .FirstOrDefaultAsync(p => p.UserTelegramId == userId && p.Keyword == keyword);

            if (existing != null)
            {
                // Обновляем существующий паттерн
                if (categoryId.HasValue) existing.CategoryId = categoryId;
                if (accountId.HasValue)  existing.AccountId  = accountId;
                if (transactionType != null) existing.TransactionType = transactionType;
                existing.UseCount++;
                existing.LastUsed = DateTime.UtcNow;
            }
            else
            {
                // Создаём новый паттерн
                _db.UserPatterns.Add(new UserPattern
                {
                    UserTelegramId  = userId,
                    Keyword         = keyword,
                    CategoryId      = categoryId,
                    AccountId       = accountId,
                    TransactionType = transactionType,
                });
            }
        }

        await _db.SaveChangesAsync();
        _logger.LogDebug("Паттерны сохранены для: {Keywords}", string.Join(", ", keywords));
    }

    /// <summary>
    /// Извлекает значимые ключевые слова из сообщения (без стоп-слов).
    /// </summary>
    private static List<string> ExtractKeywords(string message)
    {
        // Стоп-слова которые не несут смысла для паттернов
        var stopWords = new HashSet<string>
        {
            "я", "на", "в", "за", "с", "и", "или", "а", "но", "что",
            "это", "как", "по", "из", "у", "к", "от", "до", "при",
            "мне", "мне", "мой", "мои", "моя", "моё", "себе", "свой",
            "потратил", "потратила", "купил", "купила", "оплатил", "заплатил",
            "получил", "получила", "заработал", "заработала",
            "тысяч", "тысячи", "тысяча", "рублей", "тенге", "сумма"
        };

        return message
            .ToLower()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !stopWords.Contains(w))
            .Select(w => w.Trim('.', ',', '!', '?', ':'))
            .Where(w => w.Length > 2)
            .Distinct()
            .ToList();
    }
}
