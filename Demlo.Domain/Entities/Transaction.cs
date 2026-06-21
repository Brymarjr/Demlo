namespace Demlo.Domain.Entities;

public class Transaction
{
    public Guid Id { get; set; }
    public Guid WalletId { get; set; }
    public long AmountKobo { get; set; } // Storing in Kobo avoids floating-point errors
    public string Type { get; set; } = string.Empty; // "CREDIT" or "DEBIT"
    public string Description { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
}