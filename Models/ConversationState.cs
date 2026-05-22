namespace FinanceBot.Models;

/// <summary>
/// Состояние диалога с пользователем (в памяти)
/// Нужно чтобы отслеживать многошаговые диалоги
/// Например: бот спросил "в какую категорию?" — ждём ответа
/// </summary>
public record class ConversationState
{
    public ConversationStep Step { get; set; } = ConversationStep.None;
    public ParsedTransaction? PendingTransaction { get; set; }
}

public enum ConversationStep
{
    None,                   // Обычное состояние
    WaitingForAmount,       // Бот спросил: "Какая сумма?"
    WaitingForType,         // Бот спросил: "Доход или расход?"
    WaitingForCategory,     // Бот спросил: "В какую категорию?"
    WaitingForAccount,      // Бот спросил: "В какой счёт?"
    WaitingForAccountName,  // Создание нового счёта
    WaitingForCategoryName  // Создание новой категории
}
