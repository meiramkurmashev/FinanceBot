using FinanceBot.Data;
using FinanceBot.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceBot.Services;

/// <summary>
/// Регистрация и получение пользователей бота
/// </summary>
public class UserService
{
    private readonly AppDbContext _db;

    public UserService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Получает пользователя из БД или создаёт нового при первом обращении.
    /// Также создаёт счета по умолчанию для нового пользователя.
    /// </summary>
    public async Task<(User user, bool isNew)> GetOrCreateAsync(
        long telegramId,
        string fullName,
        string? username)
    {
        var user = await _db.Users.FindAsync(telegramId);
        if (user != null)
            return (user, false);

        // Новый пользователь — регистрируем
        user = new User
        {
            TelegramId = telegramId,
            FullName = fullName.Trim(),
            Username = username
        };
        _db.Users.Add(user);

        // Создаём счета по умолчанию
        _db.Accounts.AddRange(
            new Account { UserTelegramId = telegramId, Name = "Наличка", Emoji = "💵" },
            new Account { UserTelegramId = telegramId, Name = "Каспий",  Emoji = "💳" },
            new Account { UserTelegramId = telegramId, Name = "Другой банк", Emoji = "🏦" }
        );

        await _db.SaveChangesAsync();
        return (user, true);
    }

    public async Task<bool> IsRegisteredAsync(long telegramId) =>
        await _db.Users.AnyAsync(u => u.TelegramId == telegramId);
}
