namespace FinanceBot.Models;

/// <summary>
/// Пользователь бота (идентифицируется по Telegram Chat ID)
/// </summary>
public class User
{
    public long TelegramId { get; set; }            // PK = Telegram Chat ID
    public string FullName { get; set; } = string.Empty;
    public string? Username { get; set; }           // @username
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

    public ICollection<Account> Accounts { get; set; } = new List<Account>();
    public ICollection<Category> Categories { get; set; } = new List<Category>();
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
