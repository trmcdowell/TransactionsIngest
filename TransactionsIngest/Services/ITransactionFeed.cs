using TransactionsIngest.Models;

namespace TransactionsIngest.Services;

public interface ITransactionFeed
{
    Task<List<TransactionDto>> FetchSnapshotAsync();
}
