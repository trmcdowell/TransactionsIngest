namespace TransactionsIngest.Models;

public class TransactionAudit
{
    public int TransactionAuditId { get; set; }
    public int TransactionId { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public AuditChangeType ChangeType { get; set; }
    public DateTime ChangedAt { get; set; }
}
