namespace TransactionsIngest.Models;

public class Transaction
{
    public int TransactionId { get; set; }
    public string CardNumber { get; set; } = string.Empty;
    public string LocationCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime TransactionTime { get; set; }
    public TransactionStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
