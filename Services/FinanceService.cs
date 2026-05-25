using FinanceBot.Data;
using FinanceBot.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceBot.Services;

/// <summary>
/// Вся работа с деньгами: счета, категории, транзакции, статистика.
/// Все методы принимают userId — данные строго изолированы по пользователям.
/// </summary>
public class FinanceService
{
    private readonly AppDbContext _db;

    public FinanceService(AppDbContext db)
    {
        _db = db;
    }

    // ===== СЧЕТА =====

    public async Task<List<Account>> GetAccountsAsync(long userId) =>
        await _db.Accounts
            .Where(a => a.UserTelegramId == userId && a.IsActive)
            .OrderBy(a => a.Name)
            .ToListAsync();

    public async Task<Account?> FindAccountByNameAsync(long userId, string name)
    {
        name = name.ToLower().Trim();
        return await _db.Accounts
            .Where(a => a.UserTelegramId == userId && a.IsActive)
            .FirstOrDefaultAsync(a => a.Name.ToLower().Contains(name));
    }

    public async Task<Account> CreateAccountAsync(long userId, string name, string emoji = "💳")
    {
        var account = new Account { UserTelegramId = userId, Name = name, Emoji = emoji };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        return account;
    }

    // ===== КАТЕГОРИИ =====

    public async Task<List<Category>> GetCategoriesAsync(long userId, CategoryType? type = null)
    {
        var query = _db.Categories
            .Where(c => c.UserTelegramId == userId && c.IsActive);
        if (type.HasValue)
            query = query.Where(c => c.Type == type.Value || c.Type == CategoryType.Both);
        return await query.OrderBy(c => c.Name).ToListAsync();
    }

    public async Task<Category?> FindCategoryByNameAsync(long userId, string name, CategoryType? preferredType = null)
    {
        name = name.ToLower().Trim();
        var list = await _db.Categories
            .Where(c => c.UserTelegramId == userId && c.IsActive && c.Name.ToLower().Contains(name))
            .ToListAsync();

        if (preferredType.HasValue)
        {
            var match = list.FirstOrDefault(c => c.Type == preferredType.Value || c.Type == CategoryType.Both);
            if (match != null) return match;
        }
        return list.FirstOrDefault();
    }

    public async Task<Category> CreateCategoryAsync(long userId, string name, string emoji, CategoryType type)
    {
        var category = new Category
        {
            UserTelegramId = userId,
            Name = name,
            Emoji = emoji,
            Type = type
        };
        _db.Categories.Add(category);
        await _db.SaveChangesAsync();
        return category;
    }

    public async Task<bool> RenameCategoryAsync(long userId, int categoryId, string newName)
    {
        var category = await _db.Categories
            .FirstOrDefaultAsync(c => c.Id == categoryId && c.UserTelegramId == userId);
        if (category == null) return false;

        category.Name = newName;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteCategoryAsync(long userId, int categoryId)
    {
        var category = await _db.Categories
            .FirstOrDefaultAsync(c => c.Id == categoryId && c.UserTelegramId == userId);
        if (category == null) return false;

        // Мягкое удаление — помечаем неактивной
        category.IsActive = false;
        await _db.SaveChangesAsync();
        return true;
    }

    // ===== ТРАНЗАКЦИИ =====

    public async Task<Transaction> AddTransactionAsync(
        long userId,
        decimal amount,
        TransactionType type,
        int categoryId,
        int accountId,
        string? comment,
        DateTime? date = null)
    {
        var transaction = new Transaction
        {
            UserTelegramId = userId,
            Amount = amount,
            Type = type,
            CategoryId = categoryId,
            AccountId = accountId,
            Comment = comment,
            Date = date ?? DateTime.Today
        };
        _db.Transactions.Add(transaction);

        // Обновляем баланс счёта
        var account = await _db.Accounts.FindAsync(accountId);
        if (account != null)
            account.Balance += type == TransactionType.Income ? amount : -amount;

        await _db.SaveChangesAsync();

        await _db.Entry(transaction).Reference(t => t.Category).LoadAsync();
        await _db.Entry(transaction).Reference(t => t.Account).LoadAsync();
        return transaction;
    }

    // ===== СТАТИСТИКА =====

    public async Task<string> GetBalanceSummaryAsync(long userId)
    {
        var accounts = await GetAccountsAsync(userId);
        if (!accounts.Any())
            return "У тебя пока нет счетов. Добавь через /accounts";

        var total = accounts.Sum(a => a.Balance);
        var lines = accounts.Select(a => $"{a.Emoji} {a.Name}: {FormatMoney(a.Balance)}");
        return string.Join("\n", lines) + $"\n\n📊 Итого: {FormatMoney(total)}";
    }

    public async Task<string> GetMonthlyStatsAsync(long userId, int year, int month)
    {
        var transactions = await _db.Transactions
            .Include(t => t.Category)
            .Where(t => t.UserTelegramId == userId
                     && t.Date.Year == year
                     && t.Date.Month == month)
            .ToListAsync();

        var expenses = transactions.Where(t => t.Type == TransactionType.Expense).ToList();
        var incomes  = transactions.Where(t => t.Type == TransactionType.Income).ToList();

        var totalExpense = expenses.Sum(t => t.Amount);
        var totalIncome  = incomes.Sum(t => t.Amount);
        var profit = totalIncome - totalExpense;

        var sb = new System.Text.StringBuilder();

        if (expenses.Any())
        {
            sb.AppendLine("💸 *Расходы:*");
            foreach (var g in expenses.GroupBy(t => t.Category.Name).OrderByDescending(g => g.Sum(t => t.Amount)))
            {
                var emoji = expenses.First(t => t.Category.Name == g.Key).Category.Emoji;
                sb.AppendLine($"  {emoji} {g.Key}: {FormatMoney(g.Sum(t => t.Amount))}");
            }
            sb.AppendLine($"  ▶ Итого: {FormatMoney(totalExpense)}\n");
        }

        if (incomes.Any())
        {
            sb.AppendLine("💰 *Доходы:*");
            foreach (var g in incomes.GroupBy(t => t.Category.Name).OrderByDescending(g => g.Sum(t => t.Amount)))
            {
                var emoji = incomes.First(t => t.Category.Name == g.Key).Category.Emoji;
                sb.AppendLine($"  {emoji} {g.Key}: {FormatMoney(g.Sum(t => t.Amount))}");
            }
            sb.AppendLine($"  ▶ Итого: {FormatMoney(totalIncome)}\n");
        }

        if (!expenses.Any() && !incomes.Any())
        {
            sb.AppendLine("Записей за этот месяц пока нет.");
        }
        else
        {
            var profitEmoji = profit >= 0 ? "📈" : "📉";
            sb.AppendLine($"{profitEmoji} Профит: {(profit >= 0 ? "+" : "")}{FormatMoney(profit)}");
        }

        return sb.ToString();
    }

    // ===== УТИЛИТЫ =====

    /// <summary>Форматирует сумму в тенге</summary>
    public static string FormatMoney(decimal amount) =>
        $"{amount:N0} ₸";
}
