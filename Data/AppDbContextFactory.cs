using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FinanceBot.Data;

/// <summary>
/// Нужен только для команд dotnet-ef (migrations, database update)
/// В рантайме не используется
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=finance.db")
            .Options;
        return new AppDbContext(options);
    }
}
