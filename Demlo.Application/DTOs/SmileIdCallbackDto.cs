using System.Text.Json.Serialization;

namespace Demlo.Application.DTOs;

// Defines the data boundary structure for incoming Smile ID webhook callbacks.
// Maps exactly to the official async verification specification.
public record SmileIdCallbackDto
{
    [JsonPropertyName("ResultCode")]
    public string ResultCode { get; init; } = string.Empty;

    [JsonPropertyName("ResultText")]
    public string ResultText { get; init; } = string.Empty;

    [JsonPropertyName("ResultCodeGroup")]
    public int ResultCodeGroup { get; init; }

    [JsonPropertyName("PartnerParams")]
    public CallbackTrackingParams PartnerParams { get; init; } = null!;
}

public record CallbackTrackingParams
{
    [JsonPropertyName("job_id")]
    public string JobId { get; init; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("job_type")]
    public int JobType { get; init; }
}
