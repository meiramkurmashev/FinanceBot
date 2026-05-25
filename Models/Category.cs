namespace FinanceBot.Models;

public class Category
{
    public int Id { get; set; }
    public long UserTelegramId { get; set; }        // Принадлежит этому пользователю
    public User User { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string Emoji { get; set; } = "📁";
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
