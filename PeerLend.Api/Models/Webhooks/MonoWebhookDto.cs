namespace PeerLend.Api.Models.Webhooks;

public class MonoWebhookDto
{
    public string Event { get; set; } = string.Empty; // e.g., "direct-debit.successful" or "direct-debit.failed"
    public MonoWebhookData Data { get; set; } = new();
}

public class MonoWebhookData
{
    public string Reference { get; set; } = string.Empty; // Unique payment tracking ID
    public string MandateId { get; set; } = string.Empty; // Linked bank authority token
    public long Amount { get; set; } // Amount passed in raw Kobo
    public string FailureReason { get; set; } = string.Empty; // Gateway explanation string
}