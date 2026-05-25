namespace FinanceBot.Models;

public record class ConversationState
{
    public ConversationStep Step { get; set; } = ConversationStep.None;

    // Для добавления транзакции
    public ParsedTransaction? PendingTransaction { get; set; }

    // Для создания/редактирования категории
    public int? EditCategoryId { get; set; }        // ID категории которую редактируем
    public string? NewCategoryName { get; set; }    // Название новой/изменённой категории
    public string? NewCategoryEmoji { get; set; }   // Эмодзи новой категории
}

public enum ConversationStep
{
    None,

    // Добавление транзакции
    WaitingForAmount,
    WaitingForType,
    WaitingForCategory,
    WaitingForAccount,
    WaitingForAccountName,

    // Управление категориями
    WaitingForNewCategoryName,    // Пользователь вводит название новой категории
    WaitingForNewCategoryEmoji,   // Пользователь вводит эмодзи новой категории
    WaitingForNewCategoryType,    // Пользователь выбирает тип (Расход/Доход)
    WaitingForEditCategoryName,   // Пользователь вводит новое название для существующей категории
}
