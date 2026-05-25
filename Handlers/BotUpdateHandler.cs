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
    private readonly PatternService _patterns;
    private readonly ConversationService _conversation;
    private readonly ILogger<BotUpdateHandler> _logger;

    public BotUpdateHandler(
        AIService ai, FinanceService finance, UserService userService,
        PatternService patterns, ConversationService conversation,
        ILogger<BotUpdateHandler> logger)
    {
        _ai = ai; _finance = finance; _userService = userService;
        _patterns = patterns; _conversation = conversation; _logger = logger;
    }

    public async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        try
        {
            if (update.Type == UpdateType.Message)
                await HandleMessageAsync(bot, update.Message!, ct);
            else if (update.Type == UpdateType.CallbackQuery)
                await HandleCallbackAsync(bot, update.CallbackQuery!, ct);
        }
        catch (Exception ex) { _logger.LogError(ex, "Ошибка обновления"); }
    }

    public Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, HandleErrorSource src, CancellationToken ct)
    {
        if (ex is ApiRequestException api) _logger.LogError("TG {Code}: {Msg}", api.ErrorCode, api.Message);
        else _logger.LogError(ex, "Ошибка бота");
        return Task.CompletedTask;
    }

    // ─────────────── СООБЩЕНИЯ ───────────────

    private async Task HandleMessageAsync(ITelegramBotClient bot, Message msg, CancellationToken ct)
    {
        var chatId   = msg.Chat.Id;
        var fullName = $"{msg.From?.FirstName} {msg.From?.LastName}".Trim();
        var (user, _) = await _userService.GetOrCreateAsync(chatId, fullName, msg.From?.Username);
        var userId   = user.TelegramId;

        if (msg.Text?.StartsWith('/') == true)
        {
            await HandleCommandAsync(bot, msg, userId, ct);
            return;
        }

        var state = _conversation.Get(chatId);
        if (state.Step != ConversationStep.None)
        {
            await HandleConversationReplyAsync(bot, msg, state, userId, ct);
            return;
        }

        if (msg.Voice != null)
        {
            await HandleVoiceAsync(bot, msg, userId, ct);
            return;
        }

        if (!string.IsNullOrWhiteSpace(msg.Text))
        {
            await HandleTransactionTextAsync(bot, msg, msg.Text, userId, ct);
            return;
        }

        await bot.SendMessage(chatId, "Напиши что потратил или получил 😊", cancellationToken: ct);
    }

    // ─────────────── КОМАНДЫ ───────────────

    private async Task HandleCommandAsync(ITelegramBotClient bot, Message msg, long userId, CancellationToken ct)
    {
        var chatId = msg.Chat.Id;
        var cmd    = msg.Text!.Split(' ')[0].ToLower().Split('@')[0];
        switch (cmd)
        {
            case "/start":      await SendWelcomeAsync(bot, chatId, userId, ct); break;
            case "/balance":    await SendBalanceAsync(bot, chatId, userId, ct); break;
            case "/stats":      await SendStatsAsync(bot, chatId, userId, DateTime.Today.Year, DateTime.Today.Month, ct); break;
            case "/categories": await SendCategoriesAsync(bot, chatId, userId, ct); break;
            case "/accounts":   await SendAccountsAsync(bot, chatId, userId, ct); break;
            case "/help":       await SendHelpAsync(bot, chatId, ct); break;
            default:            await bot.SendMessage(chatId, "Неизвестная команда. /help", cancellationToken: ct); break;
        }
    }

    private async Task SendWelcomeAsync(ITelegramBotClient bot, long chatId, long userId, CancellationToken ct)
    {
        var isRegistered = await _userService.IsRegisteredAsync(userId);
        var text = isRegistered
            ? "👋 Привет! Пиши что потратил или получил.\n\n/balance /stats /categories /accounts"
            : "👋 Добро пожаловать!\n\nУже созданы счета: 💵 Наличка, 💳 Каспий, 🏦 Другой банк\n\n" +
              "Добавь категории через /categories и начни вести учёт!\n\n" +
              "Просто пиши или говори:\n• \"потратил 5000 на продукты\"\n• \"получил зарплату 200000\"";
        await bot.SendMessage(chatId, text, cancellationToken: ct);
    }

    private async Task SendHelpAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        await bot.SendMessage(chatId,
            "💡 *Как пользоваться:*\n\n" +
            "Пиши свободным текстом или отправляй голосовые:\n" +
            "✅ \"потратил 1500 на кофе\"\n" +
            "✅ \"заплатил пять тысяч за такси наличкой\"\n" +
            "✅ \"вчера получил зарплату 300000 на каспий\"\n\n" +
            "Бот запоминает твои привычки и со временем задаёт меньше вопросов 🧠\n\n" +
            "/balance — баланс\n/stats — статистика\n/categories — категории\n/accounts — счета",
            parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    private async Task SendBalanceAsync(ITelegramBotClient bot, long chatId, long userId, CancellationToken ct)
    {
        var s = await _finance.GetBalanceSummaryAsync(userId);
        await bot.SendMessage(chatId, $"💰 *Баланс*\n\n{s}", parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    private async Task SendStatsAsync(ITelegramBotClient bot, long chatId, long userId, int y, int m, CancellationToken ct)
    {
        var name = new DateTime(y, m, 1).ToString("MMMM yyyy", new System.Globalization.CultureInfo("ru-RU"));
        var s    = await _finance.GetMonthlyStatsAsync(userId, y, m);
        await bot.SendMessage(chatId, $"📊 *Статистика за {name}*\n\n{s}", parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    // ─────────────── КАТЕГОРИИ CRUD ───────────────

    private async Task SendCategoriesAsync(ITelegramBotClient bot, long chatId, long userId, CancellationToken ct)
    {
        var cats = await _finance.GetCategoriesAsync(userId);
        var rows = new List<InlineKeyboardButton[]>();

        if (!cats.Any())
        {
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("➕ Добавить категорию", "cat_add") });
            await bot.SendMessage(chatId, "📁 Категорий пока нет. Добавь первую!",
                replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
            return;
        }

        var sb = new System.Text.StringBuilder("📁 *Категории:*\n");
        var expenses = cats.Where(c => c.Type != CategoryType.Income).ToList();
        var incomes  = cats.Where(c => c.Type != CategoryType.Expense).ToList();

        if (expenses.Any()) { sb.AppendLine("\n💸 Расходы:"); foreach (var c in expenses) sb.AppendLine($"  {c.Emoji} {c.Name}"); }
        if (incomes.Any())  { sb.AppendLine("\n💰 Доходы:");  foreach (var c in incomes)  sb.AppendLine($"  {c.Emoji} {c.Name}"); }

        foreach (var c in cats)
            rows.Add(new[]
            {
                InlineKeyboardButton.WithCallbackData($"{c.Emoji} {c.Name}", "cat_noop"),
                InlineKeyboardButton.WithCallbackData("✏️", $"cat_edit_{c.Id}"),
                InlineKeyboardButton.WithCallbackData("🗑️", $"cat_del_{c.Id}")
            });
        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("➕ Добавить категорию", "cat_add") });

        await bot.SendMessage(chatId, sb.ToString(), parseMode: ParseMode.Markdown,
            replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
    }

    private async Task SendAccountsAsync(ITelegramBotClient bot, long chatId, long userId, CancellationToken ct)
    {
        var accs = await _finance.GetAccountsAsync(userId);
        var sb   = new System.Text.StringBuilder("🏦 *Счета:*\n\n");
        foreach (var a in accs) sb.AppendLine($"{a.Emoji} *{a.Name}*: {FinanceService.FormatMoney(a.Balance)}");
        var kb = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("➕ Добавить счёт", "acc_add") } });
        await bot.SendMessage(chatId, sb.ToString(), parseMode: ParseMode.Markdown, replyMarkup: kb, cancellationToken: ct);
    }

    // ─────────────── ТРАНЗАКЦИИ ───────────────

    private async Task HandleVoiceAsync(ITelegramBotClient bot, Message msg, long userId, CancellationToken ct)
    {
        var chatId  = msg.Chat.Id;
        var waitMsg = await bot.SendMessage(chatId, "🎤 Распознаю...", cancellationToken: ct);
        try
        {
            var fi = await bot.GetFile(msg.Voice!.FileId, ct);
            using var stream = new MemoryStream();
            await bot.DownloadFile(fi.FilePath!, stream, ct);
            await bot.DeleteMessage(chatId, waitMsg.MessageId, ct);

            var cats = await _finance.GetCategoriesAsync(userId);
            var accs = await _finance.GetAccountsAsync(userId);
            var (parsed, transcription) = await _ai.ParseVoiceMessageAsync(stream.ToArray(), cats, accs);

            if (parsed == null)
            {
                await bot.SendMessage(chatId, "❌ Не удалось распознать. Попробуй написать текстом.", cancellationToken: ct);
                return;
            }

            if (!string.IsNullOrEmpty(transcription))
                await bot.SendMessage(chatId, $"🎤 _{transcription}_", parseMode: ParseMode.Markdown, cancellationToken: ct);

            await ProcessParsedAsync(bot, chatId, userId, parsed, msg.Text ?? transcription ?? "", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка голосового");
            await bot.DeleteMessage(chatId, waitMsg.MessageId, ct);
            await bot.SendMessage(chatId, "❌ Ошибка при обработке голосового.", cancellationToken: ct);
        }
    }

    private async Task HandleTransactionTextAsync(ITelegramBotClient bot, Message msg, string text, long userId, CancellationToken ct)
    {
        var chatId = msg.Chat.Id;
        var cats   = await _finance.GetCategoriesAsync(userId);
        var accs   = await _finance.GetAccountsAsync(userId);
        var parsed = await _ai.ParseTextMessageAsync(text, cats, accs);

        if (parsed == null)
        {
            await bot.SendMessage(chatId, "🤔 Не понял. Попробуй: \"потратил 5000 на продукты\"", cancellationToken: ct);
            return;
        }

        await ProcessParsedAsync(bot, chatId, userId, parsed, text, ct);
    }

    /// <summary>
    /// Главная логика: умно решает что спросить, а что сделать автоматически.
    /// </summary>
    private async Task ProcessParsedAsync(
        ITelegramBotClient bot, long chatId, long userId,
        ParsedTransaction parsed, string originalMessage, CancellationToken ct)
    {
        var state = new ConversationState
        {
            PendingTransaction = parsed with { },
            NewCategoryName    = originalMessage  // сохраняем оригинал для обучения
        };

        // ── Сумма ──
        if (!parsed.Amount.HasValue || parsed.Amount <= 0)
        {
            _conversation.Set(chatId, state with { Step = ConversationStep.WaitingForAmount });
            await bot.SendMessage(chatId, "💬 Какая сумма?", cancellationToken: ct);
            return;
        }

        // ── Тип (доход/расход) ──
        if (string.IsNullOrEmpty(parsed.Type))
        {
            // Проверяем паттерны
            var pattern = await _patterns.FindPatternAsync(userId, originalMessage);
            if (pattern?.TransactionType != null)
            {
                parsed.Type = pattern.TransactionType;
            }
            else
            {
                _conversation.Set(chatId, state with { Step = ConversationStep.WaitingForType });
                var kb = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("💸 Расход", "type_expense"),
                            InlineKeyboardButton.WithCallbackData("💰 Доход",  "type_income") }
                });
                await bot.SendMessage(chatId, "💬 Это доход или расход?", replyMarkup: kb, cancellationToken: ct);
                return;
            }
        }

        var transType = parsed.Type == "income" ? TransactionType.Income : TransactionType.Expense;

        // ── Категория ──
        Category? category = null;

        // 1. AI уже нашёл категорию?
        if (!string.IsNullOrEmpty(parsed.CategoryName))
            category = await _finance.FindCategoryByNameAsync(userId, parsed.CategoryName);

        // 2. Паттерны?
        if (category == null)
        {
            var pattern = await _patterns.FindPatternAsync(userId, originalMessage);
            if (pattern?.CategoryId != null)
            {
                var cats = await _finance.GetCategoriesAsync(userId);
                category = cats.FirstOrDefault(c => c.Id == pattern.CategoryId);
            }
        }

        // 3. Нужно спросить?
        if (category == null)
        {
            var cats = await _finance.GetCategoriesAsync(userId);
            if (!cats.Any())
            {
                await bot.SendMessage(chatId, "📁 Сначала добавь категории через /categories", cancellationToken: ct);
                _conversation.Reset(chatId);
                return;
            }
            _conversation.Set(chatId, state with { Step = ConversationStep.WaitingForCategory });
            await AskCategoryAsync(bot, chatId, userId, transType, ct);
            return;
        }

        // ── Счёт ──
        Account? account = null;

        // 1. AI указал счёт?
        if (!string.IsNullOrEmpty(parsed.AccountName))
            account = await _finance.FindAccountByNameAsync(userId, parsed.AccountName);

        // 2. Паттерны?
        if (account == null)
        {
            var pattern = await _patterns.FindPatternAsync(userId, originalMessage);
            if (pattern?.AccountId != null)
            {
                var accs = await _finance.GetAccountsAsync(userId);
                account = accs.FirstOrDefault(a => a.Id == pattern.AccountId);
            }
        }

        // 3. Только один счёт — берём автоматически
        if (account == null)
        {
            var accs = await _finance.GetAccountsAsync(userId);
            if (accs.Count == 1)
                account = accs[0];
        }

        // 4. Нужно спросить?
        if (account == null)
        {
            _conversation.Set(chatId, state with
            {
                Step = ConversationStep.WaitingForAccount,
                PendingTransaction = parsed with { CategoryName = category.Name }
            });
            await AskAccountAsync(bot, chatId, userId, ct);
            return;
        }

        // ── Всё есть — сохраняем! ──
        await SaveTransactionAsync(bot, chatId, userId, parsed, originalMessage, category, account, ct);
    }

    private async Task AskCategoryAsync(ITelegramBotClient bot, long chatId, long userId, TransactionType type, CancellationToken ct)
    {
        var catType = type == TransactionType.Expense ? CategoryType.Expense : CategoryType.Income;
        var cats    = await _finance.GetCategoriesAsync(userId, catType);
        if (!cats.Any()) cats = await _finance.GetCategoriesAsync(userId);

        var btns = cats.Select(c => InlineKeyboardButton.WithCallbackData($"{c.Emoji} {c.Name}", $"cat_{c.Id}"))
                       .Chunk(2).Select(r => r.ToArray()).ToArray();
        await bot.SendMessage(chatId, "📁 В какую категорию?",
            replyMarkup: new InlineKeyboardMarkup(btns), cancellationToken: ct);
    }

    private async Task AskAccountAsync(ITelegramBotClient bot, long chatId, long userId, CancellationToken ct)
    {
        var accs = await _finance.GetAccountsAsync(userId);
        var btns = accs.Select(a => InlineKeyboardButton.WithCallbackData($"{a.Emoji} {a.Name}", $"acc_{a.Id}"))
                       .Chunk(2).Select(r => r.ToArray()).ToArray();
        await bot.SendMessage(chatId, "🏦 В какой счёт?",
            replyMarkup: new InlineKeyboardMarkup(btns), cancellationToken: ct);
    }

    private async Task SaveTransactionAsync(
        ITelegramBotClient bot, long chatId, long userId,
        ParsedTransaction parsed, string originalMessage,
        Category category, Account account, CancellationToken ct)
    {
        var transType = parsed.Type == "income" ? TransactionType.Income : TransactionType.Expense;
        DateTime? date = null;
        if (!string.IsNullOrEmpty(parsed.Date) && DateTime.TryParse(parsed.Date, out var d)) date = d;

        await _finance.AddTransactionAsync(userId, parsed.Amount!.Value, transType,
            category.Id, account.Id, parsed.Comment, date);

        // Обучаем бота на этом выборе
        await _patterns.LearnAsync(userId, originalMessage, category.Id, account.Id, parsed.Type);

        _conversation.Reset(chatId);

        var sign    = transType == TransactionType.Expense ? "−" : "+";
        var typeEmoji = transType == TransactionType.Expense ? "💸" : "💰";
        var comment = string.IsNullOrEmpty(parsed.Comment) ? "" : $"\n💬 _{parsed.Comment}_";

        await bot.SendMessage(chatId,
            $"✅ Готово!\n\n{typeEmoji} {sign}{FinanceService.FormatMoney(parsed.Amount!.Value)}{comment}\n" +
            $"📁 {category.Emoji} {category.Name}  ·  {account.Emoji} {account.Name}",
            parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    // ─────────────── ДИАЛОГ (ответы на вопросы) ───────────────

    private async Task HandleConversationReplyAsync(
        ITelegramBotClient bot, Message msg, ConversationState state, long userId, CancellationToken ct)
    {
        var chatId = msg.Chat.Id;
        var text   = msg.Text?.Trim() ?? "";

        switch (state.Step)
        {
            case ConversationStep.WaitingForAmount:
                if (decimal.TryParse(text.Replace(",", "."),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var amount))
                {
                    var pending = state.PendingTransaction! with { Amount = amount };
                    _conversation.Set(chatId, state with { Step = ConversationStep.None, PendingTransaction = pending });
                    await ProcessParsedAsync(bot, chatId, userId, pending, state.NewCategoryName ?? text, ct);
                }
                else
                    await bot.SendMessage(chatId, "Введи число, например: 5000", cancellationToken: ct);
                break;

            case ConversationStep.WaitingForAccountName:
                var newAcc = await _finance.CreateAccountAsync(userId, text);
                _conversation.Reset(chatId);
                await bot.SendMessage(chatId, $"✅ Счёт *{newAcc.Emoji} {newAcc.Name}* добавлен!",
                    parseMode: ParseMode.Markdown, cancellationToken: ct);
                break;

            case ConversationStep.WaitingForNewCategoryName:
                _conversation.Set(chatId, state with { Step = ConversationStep.WaitingForNewCategoryEmoji, NewCategoryName = text });
                await bot.SendMessage(chatId, "Введи эмодзи (например: 🍔 или 🚗):", cancellationToken: ct);
                break;

            case ConversationStep.WaitingForNewCategoryEmoji:
                _conversation.Set(chatId, state with { Step = ConversationStep.WaitingForNewCategoryType, NewCategoryEmoji = text });
                var typeKb = new InlineKeyboardMarkup(new[]
                {
                    new[] { InlineKeyboardButton.WithCallbackData("💸 Расход", "newcat_expense"),
                            InlineKeyboardButton.WithCallbackData("💰 Доход",  "newcat_income"),
                            InlineKeyboardButton.WithCallbackData("🔄 Оба",    "newcat_both") }
                });
                await bot.SendMessage(chatId, "Тип категории?", replyMarkup: typeKb, cancellationToken: ct);
                break;

            case ConversationStep.WaitingForEditCategoryName:
                if (state.EditCategoryId.HasValue)
                {
                    await _finance.RenameCategoryAsync(userId, state.EditCategoryId.Value, text);
                    _conversation.Reset(chatId);
                    await bot.SendMessage(chatId, $"✅ Переименовано в *{text}*",
                        parseMode: ParseMode.Markdown, cancellationToken: ct);
                    await SendCategoriesAsync(bot, chatId, userId, ct);
                }
                break;
        }
    }

    // ─────────────── CALLBACK QUERIES ───────────────

    private async Task HandleCallbackAsync(ITelegramBotClient bot, CallbackQuery cb, CancellationToken ct)
    {
        var chatId = cb.Message!.Chat.Id;
        var data   = cb.Data ?? "";
        await bot.AnswerCallbackQuery(cb.Id, cancellationToken: ct);

        var (user, _) = await _userService.GetOrCreateAsync(chatId, "", null);
        var userId    = user.TelegramId;
        var state     = _conversation.Get(chatId);

        // Выбор категории (транзакция)
        if (data.StartsWith("cat_") && data.Length > 4 && char.IsDigit(data[4])
            && int.TryParse(data[4..], out var catId))
        {
            var cats     = await _finance.GetCategoriesAsync(userId);
            var category = cats.FirstOrDefault(c => c.Id == catId);
            if (category == null) return;
            await bot.DeleteMessage(chatId, cb.Message.MessageId, ct);

            var pending = state.PendingTransaction!;
            pending.CategoryName = category.Name;

            Account? account = null;
            if (!string.IsNullOrEmpty(pending.AccountName))
                account = await _finance.FindAccountByNameAsync(userId, pending.AccountName);
            if (account == null)
            {
                var accs = await _finance.GetAccountsAsync(userId);
                if (accs.Count == 1) account = accs[0];
            }

            if (account == null)
            {
                _conversation.Set(chatId, state with { Step = ConversationStep.WaitingForAccount });
                await AskAccountAsync(bot, chatId, userId, ct);
            }
            else
            {
                await SaveTransactionAsync(bot, chatId, userId, pending,
                    state.NewCategoryName ?? "", category, account, ct);
            }
            return;
        }

        // Выбор счёта (транзакция)
        if (data.StartsWith("acc_") && data.Length > 4 && char.IsDigit(data[4])
            && int.TryParse(data[4..], out var accId))
        {
            var accs    = await _finance.GetAccountsAsync(userId);
            var account = accs.FirstOrDefault(a => a.Id == accId);
            if (account == null) return;
            await bot.DeleteMessage(chatId, cb.Message.MessageId, ct);

            var pending  = state.PendingTransaction!;
            var category = await _finance.FindCategoryByNameAsync(userId, pending.CategoryName ?? "");
            if (category == null) { var all = await _finance.GetCategoriesAsync(userId); category = all.FirstOrDefault(); }
            if (category == null)
            {
                await bot.SendMessage(chatId, "Добавь категории через /categories", cancellationToken: ct);
                _conversation.Reset(chatId);
                return;
            }
            await SaveTransactionAsync(bot, chatId, userId, pending,
                state.NewCategoryName ?? "", category, account, ct);
            return;
        }

        // Тип транзакции
        if (data is "type_expense" or "type_income")
        {
            await bot.DeleteMessage(chatId, cb.Message.MessageId, ct);
            var pending = state.PendingTransaction!;
            pending.Type = data == "type_expense" ? "expense" : "income";
            _conversation.Set(chatId, state with { Step = ConversationStep.None });
            await ProcessParsedAsync(bot, chatId, userId, pending, state.NewCategoryName ?? "", ct);
            return;
        }

        // ── Категории CRUD ──
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
            _conversation.Set(chatId, new ConversationState { Step = ConversationStep.WaitingForEditCategoryName, EditCategoryId = editId });
            await bot.SendMessage(chatId, $"✏️ Новое название для *{cat.Emoji} {cat.Name}*:",
                parseMode: ParseMode.Markdown, cancellationToken: ct);
            return;
        }

        if (data.StartsWith("cat_del_confirm_") && int.TryParse(data[16..], out var confirmId))
        {
            await bot.DeleteMessage(chatId, cb.Message.MessageId, ct);
            await _finance.DeleteCategoryAsync(userId, confirmId);
            await bot.SendMessage(chatId, "✅ Удалено", cancellationToken: ct);
            await SendCategoriesAsync(bot, chatId, userId, ct);
            return;
        }

        if (data.StartsWith("cat_del_") && int.TryParse(data[8..], out var delId))
        {
            var cat = (await _finance.GetCategoriesAsync(userId)).FirstOrDefault(c => c.Id == delId);
            if (cat == null) return;
            var kb = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("✅ Удалить", $"cat_del_confirm_{delId}"),
                        InlineKeyboardButton.WithCallbackData("❌ Отмена",  "cat_del_cancel") }
            });
            await bot.SendMessage(chatId, $"Удалить *{cat.Emoji} {cat.Name}*?",
                parseMode: ParseMode.Markdown, replyMarkup: kb, cancellationToken: ct);
            return;
        }

        if (data == "cat_del_cancel") { await bot.DeleteMessage(chatId, cb.Message.MessageId, ct); return; }
        if (data == "cat_noop") return;

        // Тип новой категории
        if (data is "newcat_expense" or "newcat_income" or "newcat_both")
        {
            await bot.DeleteMessage(chatId, cb.Message.MessageId, ct);
            var type = data switch { "newcat_income" => CategoryType.Income, "newcat_both" => CategoryType.Both, _ => CategoryType.Expense };
            var cat  = await _finance.CreateCategoryAsync(userId, state.NewCategoryName ?? "Без названия", state.NewCategoryEmoji ?? "📁", type);
            _conversation.Reset(chatId);
            await bot.SendMessage(chatId, $"✅ Категория *{cat.Emoji} {cat.Name}* добавлена!",
                parseMode: ParseMode.Markdown, cancellationToken: ct);
            await SendCategoriesAsync(bot, chatId, userId, ct);
            return;
        }

        // Добавить счёт
        if (data == "acc_add")
        {
            _conversation.Set(chatId, new ConversationState { Step = ConversationStep.WaitingForAccountName });
            await bot.SendMessage(chatId, "✏️ Название нового счёта:", cancellationToken: ct);
        }
    }
}
