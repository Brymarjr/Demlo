using System.Text.Json.Serialization;

namespace PeerLend.Application.DTOs;

// Defines the data boundary structure for inbound Mono Open Banking webhooks.
// Maps exactly to Mono's official automated transaction synchronization metadata schemas.
public record MonoWebhookDto
{
    [JsonPropertyName("event")]
    public string Event { get; init; } = string.Empty; // e.g., "account.updated" or "account.connected"

    [JsonPropertyName("data")]
    public MonoWebhookData Data { get; init; } = null!;
}

public record MonoWebhookData
{
    [JsonPropertyName("account")]
    public MonoAccountDetails Account { get; init; } = null!;

    [JsonPropertyName("meta")]
    public MonoWebhookMeta Meta { get; init; } = null!;
}

public record MonoAccountDetails
{
    [JsonPropertyName("_id")]
    public string Id { get; init; } = string.Empty; // Permanent Mono Account Identifier

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty; // Name matching the account

    [JsonPropertyName("balance")]
    public long Balance { get; init; } // Live balance stored in kobo/cents units

    [JsonPropertyName("currency")]
    public string Currency { get; init; } = "NGN";
}

public record MonoWebhookMeta
{
    [JsonPropertyName("data_status")]
    public string DataStatus { get; init; } = string.Empty; // e.g., "AVAILABLE" or "PROCESSING"
}
