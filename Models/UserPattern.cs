namespace FinanceBot.Models;

/// <summary>
/// Выученная связка: "заработал" → категория "Работа", тип income
/// Сохраняется после того как пользователь выбрал категорию/счёт вручную.
/// В следующий раз бот использует эту связку автоматически.
/// </summary>
public class UserPattern
{
    public int Id { get; set; }
    public long UserTelegramId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>Ключевое слово из сообщения (нижний регистр), например "кофе", "заработал", "такси"</summary>
    public string Keyword { get; set; } = string.Empty;

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    public int? AccountId { get; set; }
    public Account? Account { get; set; }

    /// <summary>expense или income — если слово однозначно указывает на тип</summary>
    public string? TransactionType { get; set; }

    /// <summary>Сколько раз этот паттерн использовался (чем больше — тем надёжнее)</summary>
    public int UseCount { get; set; } = 1;

    public DateTime LastUsed { get; set; } = DateTime.UtcNow;
}
