using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace FinanceBot.Handlers;

/// <summary>
/// Обёртка над BotUpdateHandler — создаёт новый DI scope для каждого сообщения.
/// Это нужно потому что Worker — Singleton, а BotUpdateHandler — Scoped.
/// Паттерн: "Scoped service inside Singleton"
/// </summary>
public class ScopedBotUpdateHandler : IUpdateHandler
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ScopedBotUpdateHandler(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        // Создаём новый scope для каждого входящего сообщения
        await using var scope = _scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<BotUpdateHandler>();
        await handler.HandleUpdateAsync(bot, update, ct);
    }

    public async Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, HandleErrorSource source, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<BotUpdateHandler>();
        await handler.HandleErrorAsync(bot, exception, source, ct);
    }
}
