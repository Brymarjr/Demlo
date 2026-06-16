namespace Demlo.Application.DTOs;
using System.Text.Json.Serialization;

// Defines the request payload structure for verifying phone ownership.
public record VerifyOtpDto
{
    [JsonPropertyName("phone")]
    public string PhoneNumber { get; init; } = string.Empty;
    public string Otp { get; init; } = string.Empty;
}