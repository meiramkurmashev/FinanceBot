using FinanceBot;
using FinanceBot.Data;
using FinanceBot.Handlers;
using FinanceBot.Services;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;

var builder = Host.CreateApplicationBuilder(args);

// База данных (SQLite)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=finance.db"));

// Telegram Bot
builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var token = sp.GetRequiredService<IConfiguration>()["BotSettings:TelegramToken"]
        ?? throw new InvalidOperationException("TelegramToken не настроен");
    return new TelegramBotClient(token);
});

// Сервисы
builder.Services.AddHttpClient<AIService>();
builder.Services.AddSingleton<ConversationService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<FinanceService>();
builder.Services.AddScoped<AIService>();
builder.Services.AddScoped<PatternService>();
builder.Services.AddScoped<BotUpdateHandler>();
builder.Services.AddSingleton<ScopedBotUpdateHandler>();

// Фоновый сервис
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// Применяем миграции при старте
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

host.Run();
