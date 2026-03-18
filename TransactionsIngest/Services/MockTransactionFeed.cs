using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TransactionsIngest.Models;

namespace TransactionsIngest.Services;

public class MockTransactionFeed : ITransactionFeed
{
    private readonly string _mockDataPath;

    public MockTransactionFeed(IConfiguration config)
    {
        var basePath = AppContext.BaseDirectory;
        _mockDataPath = Path.Combine(basePath, config["MockDataPath"] ?? "mock-data.json");
    }

    public async Task<List<TransactionDto>> FetchSnapshotAsync()
    {
        var json = await File.ReadAllTextAsync(_mockDataPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<List<TransactionDto>>(json, options) ?? [];
    }
}
