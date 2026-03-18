using Microsoft.EntityFrameworkCore;
using TransactionsIngest.Models;

namespace TransactionsIngest.Data;

public class TransactionsDbContext : DbContext
{
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<TransactionAudit> TransactionAudits => Set<TransactionAudit>();

    public TransactionsDbContext(DbContextOptions<TransactionsDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Transaction>()
            .HasIndex(t => t.TransactionId)
            .IsUnique();
    }
}

