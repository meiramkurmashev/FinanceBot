using FinanceBot.Models;
using FinanceBot.Services;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace FinanceBot.Handlers;

public class BotUpdateHandler : IUpdateHandler
{
    private readonly AIService _ai;
    private readonly FinanceService _finance;
    private readonly UserService _userService;
    private readonly ConversationService _conversation;
    private readonly ILogger<BotUpdateHandler> _logger;

    public BotUpdateHandler(
        AIService ai,
        FinanceService finance,
        UserService userService,
        ConversationService conversation,
        ILogger<BotUpdateHandler> logger)
    {
        _ai = ai;
        _finance = finance;
        _userService = userService;
        _conversation = conversation;
        _logger = logger;
    }

    // ===== ВХОДЯЩИЕ ОБНОВЛЕНИЯ =====

    public async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        try
        {
            if (update.Type == UpdateType.Message)
                await HandleMessageAsync(bot, update.Message!, ct);
            else if (update.Type == UpdateType.CallbackQuery)
                await HandleCallbackAsync(bot, update.CallbackQuery!, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при обработке обновления");
        }
    }

    public Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, HandleErrorSource source, CancellationToken ct)
    {
        if (exception is ApiRequestException api)
            _logger.LogError("Telegram API Error {Code}: {Msg}", api.ErrorCode, api.Message);
        else
            _logger.LogError(exception, "Ошибка бота");
        return Task.CompletedTask;
    }

    // ===== СООБЩЕНИЯ =====

    private async Task HandleMessageAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var fromUser = message.From;

        // Авторегистрация при любом сообщении
        var fullName = $"{fromUser?.FirstName} {fromUser?.LastName}".Trim();
        var (user, isNew) = await _userService.GetOrCreateAsync(chatId, fullName, fromUser?.Username);

        // Команды
        if (message.Text?.StartsWith('/') == true)
        {
            await HandleCommandAsync(bot, message, user.TelegramId, ct);
            return;
        }

        // Многошаговый диалог (бот ждёт ответа)
        var state = _conversation.Get(chatId);
        if (state.Step != ConversationStep.None)
        {
            await HandleConversationReplyAsync(bot, message, state, user.TelegramId, ct);
            return;
        }

        // Голосовое сообщение
        if (message.Voice != null)
        {
            await HandleVoiceMessageAsync(bot, message, user.TelegramId, ct);
            return;
        }

        // Обычный текст → парсим как транзакцию
        if (!string.IsNullOrWhiteSpace(message.Text))
        {
            await HandleTransactionTextAsync(bot, message, message.Text, user.TelegramId, ct);
            return;
        }

        await bot.SendMessage(chatId, "Напиши что потратил или получил 😊", cancellationToken: ct);
    }

    // ===== КОМАНДЫ =====

    private async Task HandleCommandAsync(ITelegramBotClient bot, Message message, long userId, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var cmd = message.Text!.Split(' ')[0].ToLower().Split('@')[0];

        switch (cmd)
        {
            case "/start":   await SendWelcomeAsync(bot, chatId, userId, ct); break;
            case "/balance": await SendBalanceAsync(bot, chatId, userId, ct); break;
            case "/stats":   await SendStatsAsync(bot, chatId, userId, DateTime.Today.Year, DateTime.Today.Month, ct); break;
            case "/categories": await SendCategoriesAsync(bot, chatId, userId, ct); break;
            case "/accounts":   await SendAccountsAsync(bot, chatId, userId, ct); break;
            case "/help":    await SendHelpAsync(bot, chatId, ct); break;
            default:         await bot.SendMessage(chatId, "Неизвестная команда. /help", cancellationToken: ct); break;
        }
    }

    private async Task SendWelcomeAsync(ITelegramBotClient bot, long chatId, long userId, CancellationToken ct)
    {
        var isNew = !await _userService.IsRegisteredAsync(userId);
        var text = isNew
            ? "👋 Добро пожаловать! Я твой личный финансовый помощник.\n\n" +
              "Для тебя уже созданы счета: 💵 Наличка, 💳 Каспий, 🏦 Другой банк\n\n" +
              "Добавь свои категории через /categories и начни вести учёт!\n\n" +
              "Просто напиши или скажи что потратил:\n" +
              "• \"потратил 5000 на продукты\"\n• \"получил зарплату 200000\""
            : "👋 Привет! Чем могу помочь?\n\n" +
              "/balance — баланс\n/stats — статистика\n/categories — категории\n/accounts — счета";

        await bot.SendMessage(chatId, text, cancellationToken: ct);
    }

    private async Task SendHelpAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        var text =
            "💡 *Как пользоваться:*\n\n" +
            "Просто пиши естественным языком:\n" +
            "✅ \"потратил 1500 в магазине\"\n" +
            "✅ \"заплатил пять тысяч за такси наличкой\"\n" +
            "✅ \"вчера получил зарплату 300000 на каспий\"\n" +
            "✅ Или отправь голосовое 🎤\n\n" +
            "*Команды:*\n" +
            "/balance — баланс счетов\n" +
            "/stats — статистика за месяц\n" +
            "/categories — управление категориями\n" +
            "/accounts — управление счетами";

        await bot.SendMessage(chatId, text, parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    private async Task SendBalanceAsync(ITelegramBotClient bot, long chatId, long userId, CancellationToken ct)
    {
        var summary = await _finance.GetBalanceSummaryAsync(userId);
        await bot.SendMessage(chatId, $"💰 *Текущий баланс*\n\n{summary}",
            parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    private async Task SendStatsAsync(ITelegramBotClient bot, long chatId, long userId, int year, int month, CancellationToken ct)
    {
        var monthName = new DateTime(year, month, 1).ToString("MMMM yyyy",
            new System.Globalization.CultureInfo("ru-RU"));
        var stats = await _finance.GetMonthlyStatsAsync(userId, year, month);
        await bot.SendMessage(chatId, $"📊 *Статистика за {monthName}*\n\n{stats}",
            parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    // ===== КАТЕГОРИИ (CRUD) =====

    private async Task SendCategoriesAsync(ITelegramBotClient bot, long chatId, long userId, CancellationToken ct)
    {
        var categories = await _finance.GetCategoriesAsync(userId);

        if (!categories.Any())
        {
            var emptyKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("➕ Добавить категорию", "cat_add") }
            });
            await bot.SendMessage(chatId,
                "📁 У тебя пока нет категорий.\nДобавь первую!",
                replyMarkup: emptyKeyboard, cancellationToken: ct);
            return;
        }

        var expenses = categories.Where(c => c.Type == CategoryType.Expense || c.Type == CategoryType.Both).ToList();
        var incomes  = categories.Where(c => c.Type == CategoryType.Income  || c.Type == CategoryType.Both).ToList();

        var sb = new System.Text.StringBuilder("📁 *Твои категории:*\n");
        if (expenses.Any())
        {
            sb.AppendLine("\n💸 Расходы:");
            foreach (var c in expenses) sb.AppendLine($"  {c.Emoji} {c.Name}");
        }
        if (incomes.Any())
        {
            sb.AppendLine("\n💰 Доходы:");
            foreach (var c in incomes) sb.AppendLine($"  {c.Emoji} {c.Name}");
        }

        // Кнопки: [✏️ Изменить] [🗑️ Удалить] для каждой категории + Добавить
        var rows = new List<InlineKeyboardButton[]>();
        foreach (var c in categories)
        {
            rows.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData($"{c.Emoji} {c.Name}", $"cat_noop"),
                InlineKeyboardButton.WithCallbackData("✏️", $"cat_edit_{c.Id}"),
                InlineKeyboardButton.WithCallbackData("🗑️", $"cat_del_{c.Id}")
            });
        }
        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("➕ Добавить категорию", "cat_add") });

        await bot.SendMessage(chatId, sb.ToString(),
            parseMode: ParseMode.Markdown,
            replyMarkup: new InlineKeyboardMarkup(rows),
            cancellationToken: ct);
    }

    // ===== СЧЕТА =====

    private async Task SendAccountsAsync(ITelegramBotClient bot, long chatId, long userId, CancellationToken ct)
    {
        var accounts = await _finance.GetAccountsAsync(userId);
        var sb = new System.Text.StringBuilder("🏦 *Твои счета:*\n\n");
        foreach (var a in accounts)
            sb.AppendLine($"{a.Emoji} *{a.Name}*: {FinanceService.FormatMoney(a.Balance)}");

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("➕ Добавить счёт", "acc_add") }
        });
        await bot.SendMessage(chatId, sb.ToString(),
            parseMode: ParseMode.Markdown,
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    // ===== ТРАНЗАКЦИИ =====

    private async Task HandleVoiceMessageAsync(ITelegramBotClient bot, Message message, long userId, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var waitMsg = await bot.SendMessage(chatId, "🎤 Распознаю голосовое...", cancellationToken: ct);
        try
        {
            var fileInfo = await bot.GetFile(message.Voice!.FileId, ct);
            using var stream = new MemoryStream();
            await bot.DownloadFile(fileInfo.FilePath!, stream, ct);

            await bot.DeleteMessage(chatId, waitMsg.MessageId, ct);
            var parsed = await _ai.ParseVoiceMessageAsync(stream.ToArray());
            if (parsed == null)
            {
                await bot.SendMessage(chatId, "❌ Не удалось распознать. Попробуй написать текстом.", cancellationToken: ct);
                return;
            }
            await ProcessParsedTransactionAsync(bot, chatId, userId, parsed, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обработки голосового");
            await bot.DeleteMessage(chatId, waitMsg.MessageId, ct);
            await bot.SendMessage(chatId, "❌ Ошибка при обработке голосового.", cancellationToken: ct);
        }
    }

    private async Task HandleTransactionTextAsync(ITelegramBotClient bot, Message message, string text, long userId, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var parsed = await _ai.ParseTextMessageAsync(text);
        if (parsed == null)
        {
            await bot.SendMessage(chatId,
                "🤔 Не понял. Попробуй: \"потратил 5000 на продукты\"",
                cancellationToken: ct);
            return;
        }
        await ProcessParsedTransactionAsync(bot, chatId, userId, parsed, ct);
    }

    private async Task ProcessParsedTransactionAsync(
        ITelegramBotClient bot, long chatId, long userId,
        ParsedTransaction parsed, CancellationToken ct)
    {
        var state = new ConversationState { PendingTransaction = parsed };

        if (!parsed.Amount.HasValue || parsed.Amount <= 0)
        {
            _conversation.Set(chatId, state with { Step = ConversationStep.WaitingForAmount });
            await bot.SendMessage(chatId, "💬 Какая сумма?", cancellationToken: ct);
            return;
        }

        if (string.IsNullOrEmpty(parsed.Type))
        {
            _conversation.Set(chatId, state with { Step = ConversationStep.WaitingForType });
            var kb = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("💸 Расход", "type_expense"),
                    InlineKeyboardButton.WithCallbackData("💰 Доход",  "type_income")
                }
            });
            await bot.SendMessage(chatId, "💬 Это доход или расход?", replyMarkup: kb, cancellationToken: ct);
            return;
        }

        var transType = parsed.Type == "income" ? TransactionType.Income : TransactionType.Expense;
        Category? category = null;
        if (!string.IsNullOrEmpty(parsed.CategoryName))
            category = await _finance.FindCategoryByNameAsync(userId, parsed.CategoryName);

        if (category == null)
        {
            var categories = await _finance.GetCategoriesAsync(userId);
            if (!categories.Any())
            {
                _conversation.Set(chatId, state with { Step = ConversationStep.WaitingForCategory });
                await bot.SendMessage(chatId,
                    "📁 У тебя нет категорий. Сначала добавь через /categories",
                    cancellationToken: ct);
                return;
            }
            _conversation.Set(chatId, state with { Step = ConversationStep.WaitingForCategory });
            await AskForCategoryAsync(bot, chatId, userId, transType, ct);
            return;
        }

        Account? account = null;
        if (!string.IsNullOrEmpty(parsed.AccountName))
            account = await _finance.FindAccountByNameAsync(userId, parsed.AccountName);

        if (account == null)
        {
            _conversation.Set(chatId, state with
            {
                Step = ConversationStep.WaitingForAccount,
                PendingTransaction = parsed with { CategoryName = category.Name }
            });
            await AskForAccountAsync(bot, chatId, userId, ct);
            return;
        }

        await SaveTransactionAsync(bot, chatId, userId, parsed, category, account, ct);
    }

    private async Task AskForCategoryAsync(ITelegramBotClient bot, long chatId, long userId, TransactionType type, CancellationToken ct)
    {
        var catType = type == TransactionType.Expense ? CategoryType.Expense : CategoryType.Income;
        var categories = await _finance.GetCategoriesAsync(userId, catType);

        if (!categories.Any())
            categories = await _finance.GetCategoriesAsync(userId); // Все если нет нужного типа

        var buttons = categories
            .Select(c => InlineKeyboardButton.WithCallbackData($"{c.Emoji} {c.Name}", $"cat_{c.Id}"))
            .Chunk(2).Select(r => r.ToArray()).ToArray();

        await bot.SendMessage(chatId, "📁 В какую категорию?",
            replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
    }

    private async Task AskForAccountAsync(ITelegramBotClient bot, long chatId, long userId, CancellationToken ct)
    {
        var accounts = await _finance.GetAccountsAsync(userId);
        var buttons = accounts
            .Select(a => InlineKeyboardButton.WithCallbackData($"{a.Emoji} {a.Name}", $"acc_{a.Id}"))
            .Chunk(2).Select(r => r.ToArray()).ToArray();

        await bot.SendMessage(chatId, "🏦 В какой счёт?",
            replyMarkup: new InlineKeyboardMarkup(buttons), cancellationToken: ct);
    }

    private async Task SaveTransactionAsync(
        ITelegramBotClient bot, long chatId, long userId,
        ParsedTransaction parsed, Category category, Account account, CancellationToken ct)
    {
        var transType = parsed.Type == "income" ? TransactionType.Income : TransactionType.Expense;
        DateTime? date = null;
        if (!string.IsNullOrEmpty(parsed.Date) && DateTime.TryParse(parsed.Date, out var d))
            date = d;

        await _finance.AddTransactionAsync(userId, parsed.Amount!.Value, transType,
            category.Id, account.Id, parsed.Comment, date);

        _conversation.Reset(chatId);

        var sign    = transType == TransactionType.Expense ? "-" : "+";
        var typeEmoji = transType == TransactionType.Expense ? "💸" : "💰";
        var comment = string.IsNullOrEmpty(parsed.Comment) ? "" : $" · _{parsed.Comment}_";

        await bot.SendMessage(chatId,
            $"✅ Внесено!\n\n{typeEmoji} {sign}{FinanceService.FormatMoney(parsed.Amount!.Value)}{comment}\n" +
            $"📁 {category.Emoji} {category.Name} · {account.Emoji} {account.Name}",
            parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    // ===== МНОГОШАГОВЫЙ ДИАЛОГ =====

    private async Task HandleConversationReplyAsync(
        ITelegramBotClient bot, Message message,
        ConversationState state, long userId, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var text = message.Text?.Trim() ?? string.Empty;

        switch (state.Step)
        {
            // --- Транзакции ---
            case ConversationStep.WaitingForAmount:
                if (decimal.TryParse(text.Replace(",", "."),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var amount))
                {
                    var updated = state with { Step = ConversationStep.None };
                    updated.PendingTransaction!.Amount = amount;
                    _conversation.Set(chatId, updated);
                    await ProcessParsedTransactionAsync(bot, chatId, userId, updated.PendingTransaction!, ct);
                }
                else
                    await bot.SendMessage(chatId, "Введи число, например: 5000", cancellationToken: ct);
                break;

            case ConversationStep.WaitingForAccountName:
                var newAcc = await _finance.CreateAccountAsync(userId, text);
                _conversation.Reset(chatId);
                await bot.SendMessage(chatId,
                    $"✅ Счёт *{newAcc.Emoji} {newAcc.Name}* добавлен!",
                    parseMode: ParseMode.Markdown, cancellationToken: ct);
                break;

            // --- Категории ---
            case ConversationStep.WaitingForNewCategoryName:
                _conversation.Set(chatId, state with
                {
                    Step = ConversationStep.WaitingForNewCategoryEmoji,
                    NewCategoryName = text
                });
                await bot.SendMessage(chatId,
                    "Введи эмодзи для категории (например: 🍔 или 🚗):",
                    cancellationToken: ct);
                break;

            case ConversationStep.WaitingForNewCategoryEmoji:
                _conversation.Set(chatId, state with
                {
                    Step = ConversationStep.WaitingForNewCategoryType,
                    NewCategoryEmoji = text
                });
                var typeKb = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData("💸 Расход", "newcat_type_expense"),
                        InlineKeyboardButton.WithCallbackData("💰 Доход",  "newcat_type_income"),
                        InlineKeyboardButton.WithCallbackData("🔄 Оба",    "newcat_type_both")
                    }
                });
                await bot.SendMessage(chatId, "Тип категории?",
                    replyMarkup: typeKb, cancellationToken: ct);
                break;

            case ConversationStep.WaitingForEditCategoryName:
                if (state.EditCategoryId.HasValue)
                {
                    await _finance.RenameCategoryAsync(userId, state.EditCategoryId.Value, text);
                    _conversation.Reset(chatId);
                    await bot.SendMessage(chatId, $"✅ Категория переименована в *{text}*",
                        parseMode: ParseMode.Markdown, cancellationToken: ct);
                    await SendCategoriesAsync(bot, chatId, userId, ct);
                }
                break;
        }
    }

    // ===== CALLBACK QUERIES =====

    private async Task HandleCallbackAsync(ITelegramBotClient bot, CallbackQuery callback, CancellationToken ct)
    {
        var chatId = callback.Message!.Chat.Id;
        var data = callback.Data ?? string.Empty;
        await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        var (user, _) = await _userService.GetOrCreateAsync(chatId, "", null);
        var userId = user.TelegramId;
        var state = _conversation.Get(chatId);

        // --- Выбор категории при добавлении транзакции ---
        if (data.StartsWith("cat_") && int.TryParse(data[4..], out var catId) && data.Length > 4 && char.IsDigit(data[4]))
        {
            var category = (await _finance.GetCategoriesAsync(userId))
                .FirstOrDefault(c => c.Id == catId);
            if (category == null) return;

            await bot.DeleteMessage(chatId, callback.Message.MessageId, ct);
            var pending = state.PendingTransaction!;
            pending.CategoryName = category.Name;

            Account? account = null;
            if (!string.IsNullOrEmpty(pending.AccountName))
                account = await _finance.FindAccountByNameAsync(userId, pending.AccountName);

            if (account == null)
            {
                _conversation.Set(chatId, state with { Step = ConversationStep.WaitingForAccount });
                await AskForAccountAsync(bot, chatId, userId, ct);
            }
            else
            {
                await SaveTransactionAsync(bot, chatId, userId, pending, category, account, ct);
            }
            return;
        }

        // --- Выбор счёта при добавлении транзакции ---
        if (data.StartsWith("acc_") && int.TryParse(data[4..], out var accId) && data.Length > 4 && char.IsDigit(data[4]))
        {
            var account = (await _finance.GetAccountsAsync(userId))
                .FirstOrDefault(a => a.Id == accId);
            if (account == null) return;

            await bot.DeleteMessage(chatId, callback.Message.MessageId, ct);
            var pending = state.PendingTransaction!;
            var category = await _finance.FindCategoryByNameAsync(userId, pending.CategoryName ?? "");
            if (category == null)
            {
                var all = await _finance.GetCategoriesAsync(userId);
                category = all.FirstOrDefault();
                if (category == null)
                {
                    await bot.SendMessage(chatId, "Сначала добавь категории через /categories", cancellationToken: ct);
                    _conversation.Reset(chatId);
                    return;
                }
            }
            await SaveTransactionAsync(bot, chatId, userId, pending, category, account, ct);
            return;
        }

        // --- Тип транзакции (доход/расход) ---
        if (data is "type_expense" or "type_income")
        {
            await bot.DeleteMessage(chatId, callback.Message.MessageId, ct);
            var pending = state.PendingTransaction!;
            pending.Type = data == "type_expense" ? "expense" : "income";
            _conversation.Set(chatId, state with { Step = ConversationStep.None });
            await ProcessParsedTransactionAsync(bot, chatId, userId, pending, ct);
            return;
        }

        // --- Категории CRUD ---
        if (data == "cat_add")
        {
            _conversation.Set(chatId, new ConversationState { Step = ConversationStep.WaitingForNewCategoryName });
            await bot.SendMessage(chatId, "✏️ Введи название новой категории:", cancellationToken: ct);
            return;
        }

        if (data.StartsWith("cat_edit_") && int.TryParse(data[9..], out var editId))
        {
            var cat = (await _finance.GetCategoriesAsync(userId)).FirstOrDefault(c => c.Id == editId);
            if (cat == null) return;
            _conversation.Set(chatId, new ConversationState
            {
                Step = ConversationStep.WaitingForEditCategoryName,
                EditCategoryId = editId
            });
            await bot.SendMessage(chatId,
                $"✏️ Введи новое название для *{cat.Emoji} {cat.Name}*:",
                parseMode: ParseMode.Markdown, cancellationToken: ct);
            return;
        }

        if (data.StartsWith("cat_del_") && int.TryParse(data[8..], out var delId))
        {
            var cat = (await _finance.GetCategoriesAsync(userId)).FirstOrDefault(c => c.Id == delId);
            if (cat == null) return;
            var kb = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("✅ Да, удалить", $"cat_del_confirm_{delId}"),
                    InlineKeyboardButton.WithCallbackData("❌ Отмена",      "cat_del_cancel")
                }
            });
            await bot.SendMessage(chatId,
                $"Удалить категорию *{cat.Emoji} {cat.Name}*?",
                parseMode: ParseMode.Markdown,
                replyMarkup: kb, cancellationToken: ct);
            return;
        }

        if (data.StartsWith("cat_del_confirm_") && int.TryParse(data[16..], out var confirmId))
        {
            await bot.DeleteMessage(chatId, callback.Message.MessageId, ct);
            await _finance.DeleteCategoryAsync(userId, confirmId);
            await bot.SendMessage(chatId, "✅ Категория удалена", cancellationToken: ct);
            await SendCategoriesAsync(bot, chatId, userId, ct);
            return;
        }

        if (data == "cat_del_cancel")
        {
            await bot.DeleteMessage(chatId, callback.Message.MessageId, ct);
            return;
        }

        // --- Тип новой категории ---
        if (data is "newcat_type_expense" or "newcat_type_income" or "newcat_type_both")
        {
            await bot.DeleteMessage(chatId, callback.Message.MessageId, ct);
            var type = data switch
            {
                "newcat_type_income"  => CategoryType.Income,
                "newcat_type_both"    => CategoryType.Both,
                _                     => CategoryType.Expense
            };
            var name  = state.NewCategoryName ?? "Без названия";
            var emoji = state.NewCategoryEmoji ?? "📁";
            var cat = await _finance.CreateCategoryAsync(userId, name, emoji, type);
            _conversation.Reset(chatId);
            await bot.SendMessage(chatId,
                $"✅ Категория *{cat.Emoji} {cat.Name}* добавлена!",
                parseMode: ParseMode.Markdown, cancellationToken: ct);
            await SendCategoriesAsync(bot, chatId, userId, ct);
            return;
        }

        // --- Добавить счёт ---
        if (data == "acc_add")
        {
            _conversation.Set(chatId, new ConversationState { Step = ConversationStep.WaitingForAccountName });
            await bot.SendMessage(chatId, "✏️ Введи название нового счёта:", cancellationToken: ct);
            return;
        }

        // Заглушка для кнопок без действия
        if (data == "cat_noop") return;
    }
}
