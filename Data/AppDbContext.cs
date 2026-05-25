using FinanceBot.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceBot.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // User — PK = TelegramId
        modelBuilder.Entity<User>()
            .HasKey(u => u.TelegramId);

        // Decimal precision
        modelBuilder.Entity<Account>()
            .Property(a => a.Balance)
            .HasColumnType("decimal(18,2)");

        modelBuilder.Entity<Transaction>()
            .Property(t => t.Amount)
            .HasColumnType("decimal(18,2)");

        // User → Accounts
        modelBuilder.Entity<Account>()
            .HasOne(a => a.User)
            .WithMany(u => u.Accounts)
            .HasForeignKey(a => a.UserTelegramId)
            .OnDelete(DeleteBehavior.Cascade);

        // User → Categories
        modelBuilder.Entity<Category>()
            .HasOne(c => c.User)
            .WithMany(u => u.Categories)
            .HasForeignKey(c => c.UserTelegramId)
            .OnDelete(DeleteBehavior.Cascade);

        // User → Transactions
        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.User)
            .WithMany(u => u.Transactions)
            .HasForeignKey(t => t.UserTelegramId)
            .OnDelete(DeleteBehavior.Cascade);

        // Transaction → Category
        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.Category)
            .WithMany(c => c.Transactions)
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Transaction → Account
        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.Account)
            .WithMany(a => a.Transactions)
            .HasForeignKey(t => t.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
