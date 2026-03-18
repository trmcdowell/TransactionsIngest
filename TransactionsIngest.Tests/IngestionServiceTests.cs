using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TransactionsIngest.Data;
using TransactionsIngest.Models;
using TransactionsIngest.Services;

namespace TransactionsIngest.Tests;

public class IngestionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TransactionsDbContext _context;

    public IngestionServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TransactionsDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new TransactionsDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private static TransactionDto MakeDto(
        int id = 1001,
        string cardNumber = "4111111111111111",
        string locationCode = "STO-01",
        string productName = "Wireless Mouse",
        decimal amount = 19.99m,
        DateTime? transactionTime = null)
    {
        return new TransactionDto(
            id,
            cardNumber,
            locationCode,
            productName,
            amount,
            transactionTime ?? DateTime.UtcNow.AddHours(-1)
        );
    }

    private IngestionService CreateService(List<TransactionDto> snapshot)
    {
        var feed = new FakeTransactionFeed(snapshot);
        return new IngestionService(_context, feed, NullLogger<IngestionService>.Instance);
    }

    [Fact]
    public async Task NewTransactionsAreInserted()
    {
        var snapshot = new List<TransactionDto> { MakeDto(1001), MakeDto(1002) };
        var service = CreateService(snapshot);

        await service.RunAsync();

        Assert.Equal(2, _context.Transactions.Count());
        Assert.All(_context.Transactions, t => Assert.Equal(TransactionStatus.Active, t.Status));

        var audits = _context.TransactionAudits.Where(a => a.ChangeType == AuditChangeType.Created).ToList();
        Assert.Equal(2, audits.Count);
    }

    [Fact]
    public async Task CardNumberIsMasked()
    {
        var snapshot = new List<TransactionDto> { MakeDto(1001, cardNumber: "4111111111111111") };
        var service = CreateService(snapshot);

        await service.RunAsync();

        var transaction = _context.Transactions.Single();
        Assert.Equal("****1111", transaction.CardNumber);
    }

    [Fact]
    public async Task UpdatesAreDetectedAndAudited()
    {
        // First run — insert
        var service1 = CreateService(new List<TransactionDto> { MakeDto(1001, amount: 19.99m) });
        await service1.RunAsync();

        // Second run — amount changed
        var service2 = CreateService(new List<TransactionDto> { MakeDto(1001, amount: 29.99m) });
        await service2.RunAsync();

        var transaction = _context.Transactions.Single(t => t.TransactionId == 1001);
        Assert.Equal(29.99m, transaction.Amount);

        var audit = _context.TransactionAudits
            .Single(a => a.TransactionId == 1001
                && a.ChangeType == AuditChangeType.Updated
                && a.FieldName == nameof(Transaction.Amount));
        Assert.Equal("19.99", audit.OldValue);
        Assert.Equal("29.99", audit.NewValue);
    }

    [Fact]
    public async Task MissingTransactionsAreRevoked()
    {
        // First run — insert two transactions
        var service1 = CreateService(new List<TransactionDto> { MakeDto(1001), MakeDto(1002) });
        await service1.RunAsync();

        // Second run — only 1001 present, 1002 is missing
        var service2 = CreateService(new List<TransactionDto> { MakeDto(1001) });
        await service2.RunAsync();

        var revoked = _context.Transactions.Single(t => t.TransactionId == 1002);
        Assert.Equal(TransactionStatus.Revoked, revoked.Status);

        var active = _context.Transactions.Single(t => t.TransactionId == 1001);
        Assert.Equal(TransactionStatus.Active, active.Status);

        var audit = _context.TransactionAudits
            .Single(a => a.TransactionId == 1002 && a.ChangeType == AuditChangeType.Revoked);
        Assert.Equal(nameof(Transaction.Status), audit.FieldName);
    }

    [Fact]
    public async Task OldTransactionsAreFinalized()
    {
        // Insert a transaction with a timestamp older than 24 hours
        var oldTime = DateTime.UtcNow.AddHours(-25);
        var service1 = CreateService(new List<TransactionDto> { MakeDto(1001, transactionTime: oldTime) });
        await service1.RunAsync();

        // Second run — finalization should trigger
        var service2 = CreateService(new List<TransactionDto> { MakeDto(1001, transactionTime: oldTime) });
        await service2.RunAsync();

        var transaction = _context.Transactions.Single(t => t.TransactionId == 1001);
        Assert.Equal(TransactionStatus.Finalized, transaction.Status);

        var audit = _context.TransactionAudits
            .Single(a => a.TransactionId == 1001 && a.ChangeType == AuditChangeType.Finalized);
        Assert.Equal(nameof(Transaction.Status), audit.FieldName);
    }

    [Fact]
    public async Task IdempotentRun_NoDuplicatesOrSpuriousAudits()
    {
        var snapshot = new List<TransactionDto> { MakeDto(1001), MakeDto(1002) };

        // Run twice with identical input
        var service1 = CreateService(snapshot);
        await service1.RunAsync();

        var service2 = CreateService(snapshot);
        await service2.RunAsync();

        Assert.Equal(2, _context.Transactions.Count());

        // Only "Created" audits — no "Updated" audits since nothing changed
        var updateAudits = _context.TransactionAudits
            .Where(a => a.ChangeType == AuditChangeType.Updated)
            .ToList();
        Assert.Empty(updateAudits);
    }

    [Fact]
    public async Task FinalizedTransactionsAreNotModified()
    {
        // Insert a transaction older than 24h
        var oldTime = DateTime.UtcNow.AddHours(-25);
        var service1 = CreateService(new List<TransactionDto> { MakeDto(1001, transactionTime: oldTime, amount: 10.00m) });
        await service1.RunAsync();

        // Second run triggers finalization
        var service2 = CreateService(new List<TransactionDto> { MakeDto(1001, transactionTime: oldTime, amount: 10.00m) });
        await service2.RunAsync();

        var transaction = _context.Transactions.Single(t => t.TransactionId == 1001);
        Assert.Equal(TransactionStatus.Finalized, transaction.Status);

        // Third run — try to update the amount on a finalized transaction
        var service3 = CreateService(new List<TransactionDto> { MakeDto(1001, transactionTime: oldTime, amount: 99.99m) });
        await service3.RunAsync();

        // Amount should remain unchanged
        var finalizedTransaction = _context.Transactions.Single(t => t.TransactionId == 1001);
        Assert.Equal(10.00m, finalizedTransaction.Amount);
        Assert.Equal(TransactionStatus.Finalized, finalizedTransaction.Status);
    }

    private class FakeTransactionFeed : ITransactionFeed
    {
        private readonly List<TransactionDto> _snapshot;

        public FakeTransactionFeed(List<TransactionDto> snapshot)
        {
            _snapshot = snapshot;
        }

        public Task<List<TransactionDto>> FetchSnapshotAsync()
        {
            return Task.FromResult(_snapshot);
        }
    }
}
