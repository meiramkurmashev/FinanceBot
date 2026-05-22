namespace FinanceBot.Models;

/// <summary>
/// Счёт: Наличка, Сбер, Каспий и т.д.
/// </summary>
public class Account
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;   // "Наличка"
    public string Emoji { get; set; } = "💳";           // "💵"
    public decimal Balance { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
