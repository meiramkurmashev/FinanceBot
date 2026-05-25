namespace FinanceBot.Models;

public class Account
{
    public int Id { get; set; }
    public long UserTelegramId { get; set; }        // Принадлежит этому пользователю
    public User User { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string Emoji { get; set; } = "💳";
    public decimal Balance { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
