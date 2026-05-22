using FinanceBot.Models;
using FinanceBot.Services;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace FinanceBot.Handlers;

/// <summary>
/// Главный обработчик всех входящих сообщений от Telegram
/// </summary>
public class BotUpdateHandler : IUpdateHandler
{
    private readonly AIService _ai;
    private readonly FinanceService _finance;
    private readonly ConversationService _conversation;
    private readonly ILogger<BotUpdateHandler> _logger;

    public BotUpdateHandler(
        AIService ai,
        FinanceService finance,
        ConversationService conversation,
        ILogger<BotUpdateHandler> logger)
    {
        _ai = ai;
        _finance = finance;
        _conversation = conversation;
        _logger = logger;
    }

    // ===== ВХОДЯЩИЕ ОБНОВЛЕНИЯ =====

    public async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        try
        {
            var handler = update.Type switch
            {
                UpdateType.Message      => HandleMessageAsync(bot, update.Message!, ct),
                UpdateType.CallbackQuery => HandleCallbackAsync(bot, update.CallbackQuery!, ct),
                _                       => Task.CompletedTask
            };
            await handler;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при обработке обновления");
        }
    }

    public Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, HandleErrorSource source, CancellationToken ct)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiEx => $"Telegram API Error: {apiEx.ErrorCode} - {apiEx.Message}",
            _ => exception.ToString()
        };
        _logger.LogError(errorMessage);
        return Task.CompletedTask;
    }

    // ===== ОБРАБОТКА СООБЩЕНИЙ =====

    private async Task HandleMessageAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;

        // Команды
        if (message.Text?.StartsWith('/') == true)
        {
            await HandleCommandAsync(bot, message, ct);
            return;
        }

        // Если бот ждёт ответа от пользователя (многошаговый диалог)
        var state = _conversation.Get(chatId);
        if (state.Step != ConversationStep.None)
        {
            await HandleConversationReplyAsync(bot, message, state, ct);
            return;
        }

        // Голосовое сообщение
        if (message.Voice != null)
        {
            await HandleVoiceMessageAsync(bot, message, ct);
            return;
        }

        // Обычный текст → пытаемся распарсить как транзакцию
        if (!string.IsNullOrWhiteSpace(message.Text))
        {
            await HandleTransactionTextAsync(bot, message, message.Text, ct);
            return;
        }

        await bot.SendMessage(chatId, "Напиши что-нибудь, например: \"потратил 500 на кофе\" 😊", cancellationToken: ct);
    }

    // ===== КОМАНДЫ =====

    private async Task HandleCommandAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var command = message.Text!.Split(' ')[0].ToLower().Replace($"@{(await bot.GetMe(ct)).Username}", "");

        switch (command)
        {
            case "/start":
                await SendWelcomeAsync(bot, chatId, ct);
                break;

            case "/balance":
                await SendBalanceAsync(bot, chatId, ct);
                break;

            case "/stats":
                await SendStatsAsync(bot, chatId, DateTime.Today.Year, DateTime.Today.Month, ct);
                break;

            case "/categories":
                await SendCategoriesAsync(bot, chatId, ct);
                break;

            case "/accounts":
                await SendAccountsAsync(bot, chatId, ct);
                break;

            case "/help":
                await SendHelpAsync(bot, chatId, ct);
                break;

            default:
                await bot.SendMessage(chatId, "Неизвестная команда. Напиши /help", cancellationToken: ct);
                break;
        }
    }

    private async Task SendWelcomeAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        var text = """
            👋 Привет! Я твой личный финансовый помощник.

            Просто напиши или скажи что потратил или получил:
            • "потратил 500 на кофе"
            • "получил зарплату 80000"
            • "заплатил 2000 за такси вчера"

            📌 Команды:
            /balance — текущий баланс по счетам
            /stats — статистика за текущий месяц
            /categories — управление категориями
            /accounts — управление счетами
            /help — помощь
            """;

        await bot.SendMessage(chatId, text, cancellationToken: ct);
    }

    private async Task SendHelpAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        var text = """
            💡 *Как пользоваться:*

            Просто пиши естественным языком:
            ✅ "потратил 1500 в магазине"
            ✅ "заплатил пятьсот за обед наличкой"
            ✅ "вчера получил зарплату 90000 на сбер"
            ✅ Или отправь голосовое сообщение 🎤

            Если я не пойму что-то — спрошу уточнение.

            *Команды:*
            /balance — баланс счетов
            /stats — статистика за месяц
            /categories — категории расходов/доходов
            /accounts — твои счета
            """;

        await bot.SendMessage(chatId, text, parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    private async Task SendBalanceAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        var summary = await _finance.GetBalanceSummaryAsync();
        await bot.SendMessage(chatId, $"💰 *Текущий баланс*\n\n{summary}", parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    private async Task SendStatsAsync(ITelegramBotClient bot, long chatId, int year, int month, CancellationToken ct)
    {
        var monthName = new DateTime(year, month, 1).ToString("MMMM yyyy", new System.Globalization.CultureInfo("ru-RU"));
        var stats = await _finance.GetMonthlyStatsAsync(year, month);
        await bot.SendMessage(chatId, $"📊 *Статистика за {monthName}*\n\n{stats}", parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    private async Task SendCategoriesAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        var categories = await _finance.GetCategoriesAsync();
        var expenses = categories.Where(c => c.Type == CategoryType.Expense || c.Type == CategoryType.Both).ToList();
        var incomes = categories.Where(c => c.Type == CategoryType.Income || c.Type == CategoryType.Both).ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("📁 *Категории расходов:*");
        foreach (var c in expenses) sb.AppendLine($"  {c.Emoji} {c.Name}");
        sb.AppendLine();
        sb.AppendLine("📁 *Категории доходов:*");
        foreach (var c in incomes) sb.AppendLine($"  {c.Emoji} {c.Name}");

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("➕ Добавить категорию", "add_category") }
        });

        await bot.SendMessage(chatId, sb.ToString(), parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
    }

    private async Task SendAccountsAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        var accounts = await _finance.GetAccountsAsync();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("🏦 *Твои счета:*\n");
        foreach (var a in accounts)
            sb.AppendLine($"{a.Emoji} *{a.Name}*: {FinanceService.FormatMoney(a.Balance)}");

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("➕ Добавить счёт", "add_account") }
        });

        await bot.SendMessage(chatId, sb.ToString(), parseMode: ParseMode.Markdown, replyMarkup: keyboard, cancellationToken: ct);
    }

    // ===== ОБРАБОТКА ТРАНЗАКЦИЙ =====

    private async Task HandleVoiceMessageAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var waitMsg = await bot.SendMessage(chatId, "🎤 Распознаю голосовое...", cancellationToken: ct);

        try
        {
            // Скачиваем голосовое сообщение
            var fileInfo = await bot.GetFile(message.Voice!.FileId, ct);
            using var stream = new MemoryStream();
            await bot.DownloadFile(fileInfo.FilePath!, stream, ct);
            var audioBytes = stream.ToArray();

            // Удаляем сообщение "распознаю..."
            await bot.DeleteMessage(chatId, waitMsg.MessageId, ct);

            // Парсим через Gemini
            var parsed = await _ai.ParseVoiceMessageAsync(audioBytes);
            if (parsed == null)
            {
                await bot.SendMessage(chatId, "❌ Не удалось распознать голосовое. Попробуй написать текстом.", cancellationToken: ct);
                return;
            }

            await ProcessParsedTransactionAsync(bot, chatId, parsed, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обработки голосового");
            await bot.DeleteMessage(chatId, waitMsg.MessageId, ct);
            await bot.SendMessage(chatId, "❌ Ошибка при обработке голосового.", cancellationToken: ct);
        }
    }

    private async Task HandleTransactionTextAsync(ITelegramBotClient bot, Message message, string text, CancellationToken ct)
    {
        var chatId = message.Chat.Id;

        // Парсим через Gemini
        var parsed = await _ai.ParseTextMessageAsync(text);
        if (parsed == null)
        {
            await bot.SendMessage(chatId, "🤔 Не понял. Попробуй написать например: \"потратил 500 на кофе\"", cancellationToken: ct);
            return;
        }

        await ProcessParsedTransactionAsync(bot, chatId, parsed, ct);
    }

    /// <summary>
    /// Обрабатывает распарсенную транзакцию:
    /// если чего-то не хватает — спрашивает пользователя
    /// </summary>
    private async Task ProcessParsedTransactionAsync(ITelegramBotClient bot, long chatId, ParsedTransaction parsed, CancellationToken ct)
    {
        // Сохраняем транзакцию в состояние (она может быть неполной)
        var state = new ConversationState { PendingTransaction = parsed };

        // Проверяем сумму
        if (!parsed.Amount.HasValue || parsed.Amount <= 0)
        {
            _conversation.Set(chatId, state with { Step = ConversationStep.WaitingForAmount });
            await bot.SendMessage(chatId, "💬 Какая сумма?", cancellationToken: ct);
            return;
        }

        // Проверяем тип (доход/расход)
        if (string.IsNullOrEmpty(parsed.Type))
        {
            _conversation.Set(chatId, state with { Step = ConversationStep.WaitingForType });
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("💸 Расход", "type_expense"),
                    InlineKeyboardButton.WithCallbackData("💰 Доход", "type_income")
                }
            });
            await bot.SendMessage(chatId, "💬 Это доход или расход?", replyMarkup: keyboard, cancellationToken: ct);
            return;
        }

        // Проверяем категорию
        var transType = parsed.Type == "income" ? TransactionType.Income : TransactionType.Expense;
        Category? category = null;

        if (!string.IsNullOrEmpty(parsed.CategoryName))
            category = await _finance.FindCategoryByNameAsync(parsed.CategoryName, CategoryType.Expense);

        if (category == null)
        {
            _conversation.Set(chatId, state with { Step = ConversationStep.WaitingForCategory });
            await AskForCategoryAsync(bot, chatId, transType, ct);
            return;
        }

        // Проверяем счёт
        Account? account = null;

        if (!string.IsNullOrEmpty(parsed.AccountName))
            account = await _finance.FindAccountByNameAsync(parsed.AccountName);

        if (account == null)
        {
            _conversation.Set(chatId, state with { Step = ConversationStep.WaitingForAccount, PendingTransaction = parsed with { CategoryName = category.Name } });
            await AskForAccountAsync(bot, chatId, ct);
            return;
        }

        // Всё есть — сохраняем!
        await SaveTransactionAsync(bot, chatId, parsed, category, account, ct);
    }

    private async Task AskForCategoryAsync(ITelegramBotClient bot, long chatId, TransactionType type, CancellationToken ct)
    {
        var categoryType = type == TransactionType.Expense ? CategoryType.Expense : CategoryType.Income;
        var categories = await _finance.GetCategoriesAsync(categoryType);

        // Создаём кнопки по 2 в ряд
        var buttons = categories
            .Select(c => InlineKeyboardButton.WithCallbackData($"{c.Emoji} {c.Name}", $"cat_{c.Id}"))
            .Chunk(2)
            .Select(row => row.ToArray())
            .ToArray();

        var keyboard = new InlineKeyboardMarkup(buttons);
        await bot.SendMessage(chatId, "📁 В какую категорию внести?", replyMarkup: keyboard, cancellationToken: ct);
    }

    private async Task AskForAccountAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        var accounts = await _finance.GetAccountsAsync();

        var buttons = accounts
            .Select(a => InlineKeyboardButton.WithCallbackData($"{a.Emoji} {a.Name}", $"acc_{a.Id}"))
            .Chunk(2)
            .Select(row => row.ToArray())
            .ToArray();

        var keyboard = new InlineKeyboardMarkup(buttons);
        await bot.SendMessage(chatId, "🏦 В какой счёт?", replyMarkup: keyboard, cancellationToken: ct);
    }

    private async Task SaveTransactionAsync(
        ITelegramBotClient bot,
        long chatId,
        ParsedTransaction parsed,
        Category category,
        Account account,
        CancellationToken ct)
    {
        var transType = parsed.Type == "income" ? TransactionType.Income : TransactionType.Expense;
        DateTime? date = null;
        if (!string.IsNullOrEmpty(parsed.Date) && DateTime.TryParse(parsed.Date, out var d))
            date = d;

        var transaction = await _finance.AddTransactionAsync(
            amount: parsed.Amount!.Value,
            type: transType,
            categoryId: category.Id,
            accountId: account.Id,
            comment: parsed.Comment,
            date: date
        );

        _conversation.Reset(chatId);

        // Формируем красивое подтверждение
        var typeSign = transType == TransactionType.Expense ? "-" : "+";
        var typeEmoji = transType == TransactionType.Expense ? "💸" : "💰";
        var commentText = string.IsNullOrEmpty(parsed.Comment) ? "" : $" · _{parsed.Comment}_";
        var dateText = transaction.Date.Date == DateTime.Today ? "" : $" · {transaction.Date:dd.MM.yyyy}";

        var confirmText = $"✅ Внесено!\n\n{typeEmoji} {typeSign}{FinanceService.FormatMoney(parsed.Amount!.Value)}{commentText}\n" +
                          $"📁 {category.Emoji} {category.Name} · {account.Emoji} {account.Name}{dateText}";

        await bot.SendMessage(chatId, confirmText, parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    // ===== МНОГОШАГОВЫЙ ДИАЛОГ =====

    private async Task HandleConversationReplyAsync(
        ITelegramBotClient bot,
        Message message,
        ConversationState state,
        CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var text = message.Text ?? string.Empty;
        var pending = state.PendingTransaction!;

        switch (state.Step)
        {
            case ConversationStep.WaitingForAmount:
                if (decimal.TryParse(text.Replace(",", "."), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var amount))
                {
                    pending.Amount = amount;
                    _conversation.Set(chatId, state with { Step = ConversationStep.None });
                    await ProcessParsedTransactionAsync(bot, chatId, pending, ct);
                }
                else
                {
                    await bot.SendMessage(chatId, "Введи число, например: 500", cancellationToken: ct);
                }
                break;

            case ConversationStep.WaitingForAccountName:
                var newAccount = await _finance.CreateAccountAsync(text);
                await bot.SendMessage(chatId, $"✅ Счёт *{newAccount.Emoji} {newAccount.Name}* создан!", parseMode: ParseMode.Markdown, cancellationToken: ct);
                _conversation.Reset(chatId);
                break;

            case ConversationStep.WaitingForCategoryName:
                // Будет реализовано позже
                _conversation.Reset(chatId);
                break;
        }
    }

    // ===== CALLBACK QUERIES (нажатие на инлайн-кнопки) =====

    private async Task HandleCallbackAsync(ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        var chatId = callback.Message!.Chat.Id;
        var data = callback.Data ?? string.Empty;

        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var state = _conversation.Get(chatId);

        // Выбор категории
        if (data.StartsWith("cat_") && int.TryParse(data[4..], out var catId))
        {
            var categories = await _finance.GetCategoriesAsync();
            var category = categories.FirstOrDefault(c => c.Id == catId);
            if (category == null) return;

            await bot.DeleteMessage(chatId, callback.Message.MessageId, ct);

            var pending = state.PendingTransaction!;
            pending.CategoryName = category.Name;

            // Теперь проверяем счёт
            Account? account = null;
            if (!string.IsNullOrEmpty(pending.AccountName))
                account = await _finance.FindAccountByNameAsync(pending.AccountName);

            if (account == null)
            {
                _conversation.Set(chatId, state with { Step = ConversationStep.WaitingForAccount });
                await AskForAccountAsync(bot, chatId, ct);
            }
            else
            {
                await SaveTransactionAsync(bot, chatId, pending, category, account, ct);
            }
            return;
        }

        // Выбор счёта
        if (data.StartsWith("acc_") && int.TryParse(data[4..], out var accId))
        {
            var accounts = await _finance.GetAccountsAsync();
            var account = accounts.FirstOrDefault(a => a.Id == accId);
            if (account == null) return;

            await bot.DeleteMessage(chatId, callback.Message.MessageId, ct);

            var pending = state.PendingTransaction!;
            var category = await _finance.FindCategoryByNameAsync(pending.CategoryName ?? "Другое");
            if (category == null)
            {
                var allCats = await _finance.GetCategoriesAsync();
                category = allCats.First(); // fallback
            }

            await SaveTransactionAsync(bot, chatId, pending, category, account, ct);
            return;
        }

        // Выбор типа (доход/расход)
        if (data == "type_expense" || data == "type_income")
        {
            await bot.DeleteMessage(chatId, callback.Message.MessageId, ct);
            var pending = state.PendingTransaction!;
            pending.Type = data == "type_expense" ? "expense" : "income";
            _conversation.Set(chatId, state with { Step = ConversationStep.None });
            await ProcessParsedTransactionAsync(bot, chatId, pending, ct);
            return;
        }

        // Добавить счёт
        if (data == "add_account")
        {
            _conversation.Set(chatId, new ConversationState
            {
                Step = ConversationStep.WaitingForAccountName,
                PendingTransaction = new ParsedTransaction()
            });
            await bot.SendMessage(chatId, "✏️ Введи название нового счёта:", cancellationToken: ct);
            return;
        }

        // Добавить категорию
        if (data == "add_category")
        {
            await bot.SendMessage(chatId, "🚧 Добавление категорий будет в следующей версии.", cancellationToken: ct);
        }
    }
}
