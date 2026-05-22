using FinanceBot;
using FinanceBot.Data;
using FinanceBot.Handlers;
using FinanceBot.Services;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;

var builder = Host.CreateApplicationBuilder(args);

// ===== База данных (SQLite) =====
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=finance.db"));

// ===== Telegram Bot =====
builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var token = config["BotSettings:TelegramToken"]
        ?? throw new InvalidOperationException("TelegramToken не настроен в appsettings.json");
    return new TelegramBotClient(token);
});

// ===== Наши сервисы =====
builder.Services.AddHttpClient<AIService>();            // HttpClient для Gemini REST API
builder.Services.AddSingleton<ConversationService>();   // Singleton — хранит состояния в памяти
builder.Services.AddScoped<FinanceService>();           // Scoped — создаётся на каждый запрос
builder.Services.AddScoped<AIService>();                // Scoped — работает с Gemini API
builder.Services.AddScoped<BotUpdateHandler>();         // Scoped — обработчик сообщений
builder.Services.AddSingleton<ScopedBotUpdateHandler>(); // Singleton wrapper для Worker

// ===== Фоновый сервис (Worker) =====
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

// ===== Применяем миграции при старте =====
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

host.Run();
