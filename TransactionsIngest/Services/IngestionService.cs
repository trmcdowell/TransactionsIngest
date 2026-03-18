using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TransactionsIngest.Data;
using TransactionsIngest.Models;

namespace TransactionsIngest.Services;

public class IngestionService
{
    private readonly TransactionsDbContext _context;
    private readonly ITransactionFeed _feed;
    private readonly ILogger<IngestionService> _logger;

    public IngestionService(TransactionsDbContext context, ITransactionFeed feed, ILogger<IngestionService> logger)
    {
        _context = context;
        _feed = feed;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        var snapshot = await _feed.FetchSnapshotAsync();
        _logger.LogInformation("Fetched {Count} transactions from feed", snapshot.Count);

        var now = DateTime.UtcNow;
        var cutoff = now.AddHours(-24);

        using var dbTransaction = await _context.Database.BeginTransactionAsync();
        var existing = await _context.Transactions.ToListAsync();

        var existingMap = existing.ToDictionary(t => t.TransactionId);
        var snapshotMap = snapshot.ToDictionary(t => t.TransactionId);

        var inserted = 0;
        foreach (var dto in snapshot)
        {
            if (existingMap.TryGetValue(dto.TransactionId, out var match))
            {
                UpdateIfChanged(match, dto, now);
            }
            else
            {
                _context.Transactions.Add(new Transaction
                {
                    TransactionId = dto.TransactionId,
                    CardNumber = MaskCardNumber(dto.CardNumber),
                    LocationCode = dto.LocationCode,
                    ProductName = dto.ProductName,
                    Amount = dto.Amount,
                    TransactionTime = dto.TransactionTime,
                    Status = TransactionStatus.Active,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                _context.TransactionAudits.Add(new TransactionAudit
                {
                    TransactionId = dto.TransactionId,
                    FieldName = "",
                    OldValue = "",
                    NewValue = "",
                    ChangeType = AuditChangeType.Created,
                    ChangedAt = now
                });
                inserted++;
            }
        }

        var revoked = 0;
        foreach (var transaction in existing)
        {
            if (transaction.Status == TransactionStatus.Active && !snapshotMap.ContainsKey(transaction.TransactionId))
            {
                transaction.Status = TransactionStatus.Revoked;
                transaction.UpdatedAt = now;
                _context.TransactionAudits.Add(new TransactionAudit
                {
                    TransactionId = transaction.TransactionId,
                    FieldName = nameof(Transaction.Status),
                    OldValue = TransactionStatus.Active.ToString(),
                    NewValue = TransactionStatus.Revoked.ToString(),
                    ChangeType = AuditChangeType.Revoked,
                    ChangedAt = now
                });
                revoked++;
            }
        }

        var staleTransactions = existing.Where(t => t.TransactionTime < cutoff && t.Status == TransactionStatus.Active).ToList();
        foreach (var st in staleTransactions)
        {
            st.Status = TransactionStatus.Finalized;
            st.UpdatedAt = now;
            _context.TransactionAudits.Add(new TransactionAudit
            {
                TransactionId = st.TransactionId,
                FieldName = nameof(Transaction.Status),
                OldValue = TransactionStatus.Active.ToString(),
                NewValue = TransactionStatus.Finalized.ToString(),
                ChangeType = AuditChangeType.Finalized,
                ChangedAt = now
            });
        }

        await _context.SaveChangesAsync();
        await dbTransaction.CommitAsync();

        _logger.LogInformation(
            "Ingestion complete: {Inserted} inserted, {Revoked} revoked, {Finalized} finalized",
            inserted, revoked, staleTransactions.Count);
    }

    // Detect transaction changes, then audit the change and update the corresponding record
    private void UpdateIfChanged(Transaction existing, TransactionDto incoming, DateTime now)
    {
        if (existing.Status == TransactionStatus.Finalized)
            return;

        var changed = false;
        var maskedCardNumber = MaskCardNumber(incoming.CardNumber);
        if (existing.CardNumber != maskedCardNumber)
        {
            _context.TransactionAudits.Add(new TransactionAudit
            {
                TransactionId = existing.TransactionId,
                FieldName = nameof(Transaction.CardNumber),
                OldValue = existing.CardNumber,
                NewValue = maskedCardNumber,
                ChangeType = AuditChangeType.Updated,
                ChangedAt = now
            });
            existing.CardNumber = maskedCardNumber;
            changed = true;
        }

        if (existing.LocationCode != incoming.LocationCode)
        {
            _context.TransactionAudits.Add(new TransactionAudit
            {
                TransactionId = existing.TransactionId,
                FieldName = nameof(Transaction.LocationCode),
                OldValue = existing.LocationCode,
                NewValue = incoming.LocationCode,
                ChangeType = AuditChangeType.Updated,
                ChangedAt = now
            });
            existing.LocationCode = incoming.LocationCode;
            changed = true;
        }

        if (existing.ProductName != incoming.ProductName)
        {
            _context.TransactionAudits.Add(new TransactionAudit
            {
                TransactionId = existing.TransactionId,
                FieldName = nameof(Transaction.ProductName),
                OldValue = existing.ProductName,
                NewValue = incoming.ProductName,
                ChangeType = AuditChangeType.Updated,
                ChangedAt = now
            });
            existing.ProductName = incoming.ProductName;
            changed = true;
        }

        if (existing.Amount != incoming.Amount)
        {
            _context.TransactionAudits.Add(new TransactionAudit
            {
                TransactionId = existing.TransactionId,
                FieldName = nameof(Transaction.Amount),
                OldValue = existing.Amount.ToString(),
                NewValue = incoming.Amount.ToString(),
                ChangeType = AuditChangeType.Updated,
                ChangedAt = now
            });
            existing.Amount = incoming.Amount;
            changed = true;
        }

        if (existing.TransactionTime != incoming.TransactionTime)
        {
            _context.TransactionAudits.Add(new TransactionAudit
            {
                TransactionId = existing.TransactionId,
                FieldName = nameof(Transaction.TransactionTime),
                OldValue = existing.TransactionTime.ToString("O"),
                NewValue = incoming.TransactionTime.ToString("O"),
                ChangeType = AuditChangeType.Updated,
                ChangedAt = now
            });
            existing.TransactionTime = incoming.TransactionTime;
            changed = true;
        }

        if (changed)
        {
            existing.UpdatedAt = now;
        }
    }

    private static string MaskCardNumber(string cardNumber)
    {
        if (cardNumber.Length < 4)
            return cardNumber;

        return $"****{cardNumber[^4..]}";
    }
}
