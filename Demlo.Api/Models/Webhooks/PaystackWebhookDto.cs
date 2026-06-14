using System.Text.Json.Serialization;

namespace Demlo.Api.Models.Webhooks;

public class PaystackWebhookDto
{
    [JsonPropertyName("event")]
    public string Event { get; set; } = null!;

    [JsonPropertyName("data")]
    public PaystackWebhookData Data { get; set; } = null!;
}

public class PaystackWebhookData
{
    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("reference")]
    public string Reference { get; set; } = null!;

    [JsonPropertyName("transfer_code")]
    public string TransferCode { get; set; } = null!;

    [JsonPropertyName("recipient")]
    public PaystackRecipientData Recipient { get; set; } = null!;
}

public class PaystackRecipientData
{
    [JsonPropertyName("recipient_code")]
    public string RecipientCode { get; set; } = null!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;
}