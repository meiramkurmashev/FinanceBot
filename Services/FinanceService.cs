using FinanceBot.Data;
using FinanceBot.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceBot.Services;

/// <summary>
/// Сервис для работы с базой данных:
/// счета, категории, транзакции, статистика
/// </summary>
public class FinanceService
{
    private readonly AppDbContext _db;
    private readonly ILogger<FinanceService> _logger;

    public FinanceService(AppDbContext db, ILogger<FinanceService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ===== ИНИЦИАЛИЗАЦИЯ =====

    /// <summary>
    /// Создаёт счета и категории по умолчанию при первом запуске
    /// </summary>
    public async Task SeedDefaultDataAsync()
    {
        if (!await _db.Accounts.AnyAsync())
        {
            _db.Accounts.AddRange(
                new Account { Name = "Наличка", Emoji = "💵" },
                new Account { Name = "Сбер", Emoji = "🏦" },
                new Account { Name = "Каспий", Emoji = "💳" }
            );
        }

        if (!await _db.Categories.AnyAsync())
        {
            _db.Categories.AddRange(
                // Расходы
                new Category { Name = "Еда", Emoji = "🍔", Type = CategoryType.Expense },
                new Category { Name = "Транспорт", Emoji = "🚗", Type = CategoryType.Expense },
                new Category { Name = "Покупки", Emoji = "🛒", Type = CategoryType.Expense },
                new Category { Name = "Здоровье", Emoji = "💊", Type = CategoryType.Expense },
                new Category { Name = "Развлечения", Emoji = "🎬", Type = CategoryType.Expense },
                new Category { Name = "Коммунальные", Emoji = "🏠", Type = CategoryType.Expense },
                new Category { Name = "Связь", Emoji = "📱", Type = CategoryType.Expense },
                new Category { Name = "Другое", Emoji = "📦", Type = CategoryType.Expense },
                // Доходы
                new Category { Name = "Зарплата", Emoji = "💼", Type = CategoryType.Income },
                new Category { Name = "Фриланс", Emoji = "💻", Type = CategoryType.Income },
                new Category { Name = "Подарок", Emoji = "🎁", Type = CategoryType.Income },
                new Category { Name = "Прочий доход", Emoji = "💰", Type = CategoryType.Income }
            );
        }

        await _db.SaveChangesAsync();
    }

    // ===== СЧЕТА =====

    public async Task<List<Account>> GetAccountsAsync() =>
        await _db.Accounts.Where(a => a.IsActive).OrderBy(a => a.Name).ToListAsync();

    public async Task<Account?> FindAccountByNameAsync(string name)
    {
        name = name.ToLower().Trim();
        return await _db.Accounts
            .Where(a => a.IsActive)
            .FirstOrDefaultAsync(a => a.Name.ToLower().Contains(name));
    }

    public async Task<Account> CreateAccountAsync(string name, string emoji = "💳")
    {
        var account = new Account { Name = name, Emoji = emoji };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        return account;
    }

    // ===== КАТЕГОРИИ =====

    public async Task<List<Category>> GetCategoriesAsync(CategoryType? type = null)
    {
        var query = _db.Categories.Where(c => c.IsActive);
        if (type.HasValue)
            query = query.Where(c => c.Type == type.Value || c.Type == CategoryType.Both);
        return await query.OrderBy(c => c.Name).ToListAsync();
    }

    public async Task<Category?> FindCategoryByNameAsync(string name, CategoryType? preferredType = null)
    {
        name = name.ToLower().Trim();
        var categories = await _db.Categories
            .Where(c => c.IsActive && c.Name.ToLower().Contains(name))
            .ToListAsync();

        // Сначала ищем с подходящим типом
        if (preferredType.HasValue)
        {
            var match = categories.FirstOrDefault(c => c.Type == preferredType.Value || c.Type == CategoryType.Both);
            if (match != null) return match;
        }

        return categories.FirstOrDefault();
    }

    public async Task<Category> CreateCategoryAsync(string name, string emoji, CategoryType type)
    {
        var category = new Category { Name = name, Emoji = emoji, Type = type };
        _db.Categories.Add(category);
        await _db.SaveChangesAsync();
        return category;
    }

    // ===== ТРАНЗАКЦИИ =====

    public async Task<Transaction> AddTransactionAsync(
        decimal amount,
        TransactionType type,
        int categoryId,
        int accountId,
        string? comment,
        DateTime? date = null)
    {
        var transaction = new Transaction
        {
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
        {
            account.Balance += type == TransactionType.Income ? amount : -amount;
        }

        await _db.SaveChangesAsync();

        // Загружаем связанные данные для ответа
        await _db.Entry(transaction).Reference(t => t.Category).LoadAsync();
        await _db.Entry(transaction).Reference(t => t.Account).LoadAsync();

        return transaction;
    }

    // ===== СТАТИСТИКА =====

    public async Task<string> GetBalanceSummaryAsync()
    {
        var accounts = await _db.Accounts.Where(a => a.IsActive).ToListAsync();
        var total = accounts.Sum(a => a.Balance);

        var lines = accounts.Select(a =>
            $"{a.Emoji} {a.Name}: {FormatMoney(a.Balance)}");

        return string.Join("\n", lines) + $"\n\n📊 Итого: {FormatMoney(total)}";
    }

    public async Task<string> GetMonthlyStatsAsync(int year, int month)
    {
        var transactions = await _db.Transactions
            .Include(t => t.Category)
            .Where(t => t.Date.Year == year && t.Date.Month == month)
            .ToListAsync();

        var expenses = transactions.Where(t => t.Type == TransactionType.Expense).ToList();
        var incomes = transactions.Where(t => t.Type == TransactionType.Income).ToList();

        var totalExpense = expenses.Sum(t => t.Amount);
        var totalIncome = incomes.Sum(t => t.Amount);
        var profit = totalIncome - totalExpense;

        var sb = new System.Text.StringBuilder();

        // Расходы по категориям
        if (expenses.Any())
        {
            sb.AppendLine("💸 *Расходы:*");
            foreach (var group in expenses.GroupBy(t => t.Category.Name).OrderByDescending(g => g.Sum(t => t.Amount)))
            {
                var cat = expenses.First(t => t.Category.Name == group.Key).Category;
                sb.AppendLine($"  {cat.Emoji} {group.Key}: {FormatMoney(group.Sum(t => t.Amount))}");
            }
            sb.AppendLine($"  ▶ Итого: {FormatMoney(totalExpense)}");
            sb.AppendLine();
        }

        // Доходы по категориям
        if (incomes.Any())
        {
            sb.AppendLine("💰 *Доходы:*");
            foreach (var group in incomes.GroupBy(t => t.Category.Name).OrderByDescending(g => g.Sum(t => t.Amount)))
            {
                var cat = incomes.First(t => t.Category.Name == group.Key).Category;
                sb.AppendLine($"  {cat.Emoji} {group.Key}: {FormatMoney(group.Sum(t => t.Amount))}");
            }
            sb.AppendLine($"  ▶ Итого: {FormatMoney(totalIncome)}");
            sb.AppendLine();
        }

        if (!expenses.Any() && !incomes.Any())
        {
            sb.AppendLine("Записей за этот месяц нет.");
        }
        else
        {
            var profitEmoji = profit >= 0 ? "📈" : "📉";
            sb.AppendLine($"{profitEmoji} Профит: {(profit >= 0 ? "+" : "")}{FormatMoney(profit)}");
        }

        return sb.ToString();
    }

    // ===== УТИЛИТЫ =====

    public static string FormatMoney(decimal amount) =>
        $"{amount:N0} ₽";
}
