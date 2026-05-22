namespace FinanceBot.Models;

/// <summary>
/// Запись о доходе или расходе
/// </summary>
public class Transaction
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }       // Доход / Расход

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public string? Comment { get; set; }            // Комментарий пользователя
    public DateTime Date { get; set; } = DateTime.Today;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum TransactionType
{
    Expense,  // Расход
    Income    // Доход
}
