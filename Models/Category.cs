namespace FinanceBot.Models;

/// <summary>
/// Категория транзакции: Еда, Транспорт, Зарплата и т.д.
/// </summary>
public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;    // "Еда"
    public string Emoji { get; set; } = "📁";           // "🍔"
    public CategoryType Type { get; set; } = CategoryType.Expense;
    public bool IsActive { get; set; } = true;

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}

public enum CategoryType
{
    Expense,  // Расход
    Income,   // Доход
    Both      // Любой
}
