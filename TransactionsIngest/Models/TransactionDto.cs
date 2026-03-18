using System.Text.Json.Serialization;

namespace TransactionsIngest.Models;

public record TransactionDto(
    int TransactionId,
    string CardNumber,
    string LocationCode,
    string ProductName,
    decimal Amount,
    [property: JsonPropertyName("timestamp")]
    DateTime TransactionTime
);
