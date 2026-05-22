namespace FinanceBot.Models;

/// <summary>
/// Результат парсинга сообщения через Gemini AI
/// Поля могут быть null если AI не смог определить значение
/// </summary>
public record class ParsedTransaction
{
    public decimal? Amount { get; set; }           // 500
    public string? Type { get; set; }              // "expense" или "income"
    public string? CategoryName { get; set; }      // "Еда"
    public string? AccountName { get; set; }       // "Наличка"
    public string? Comment { get; set; }           // "кофе с коллегами"
    public string? Date { get; set; }              // "2026-05-21" (ISO формат)
}
