namespace FinanceBot.Models;

public class Transaction
{
    public int Id { get; set; }
    public long UserTelegramId { get; set; }        // Принадлежит этому пользователю
    public User User { get; set; } = null!;

    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public string? Comment { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum TransactionType
{
    Expense,
    Income
}
