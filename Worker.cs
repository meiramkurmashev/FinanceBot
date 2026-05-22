using FinanceBot.Data;
using FinanceBot.Handlers;
using FinanceBot.Services;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace FinanceBot;

/// <summary>
/// Фоновый сервис — запускает Telegram бота и держит его живым
/// </summary>
public class Worker : BackgroundService
{
    private readonly ITelegramBotClient _bot;
    private readonly ScopedBotUpdateHandler _handler;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<Worker> _logger;

    public Worker(
        ITelegramBotClient bot,
        ScopedBotUpdateHandler handler,
        IServiceScopeFactory scopeFactory,
        ILogger<Worker> logger)
    {
        _bot = bot;
        _handler = handler;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Создаём данные по умолчанию (счета, категории) при первом запуске
        await using var scope = _scopeFactory.CreateAsyncScope();
        var finance = scope.ServiceProvider.GetRequiredService<FinanceService>();
        await finance.SeedDefaultDataAsync();

        // Проверяем что бот успешно подключён
        var me = await _bot.GetMe(stoppingToken);
        _logger.LogInformation("🤖 Бот запущен: @{Username}", me.Username);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery },
            DropPendingUpdates = true // Игнорируем сообщения пока бот был выключен
        };

        // Запускаем получение обновлений (long polling)
        await _bot.ReceiveAsync(
            updateHandler: _handler,
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken
        );
    }
}
