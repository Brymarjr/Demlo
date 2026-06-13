namespace Demlo.Application.DTOs;

// Defines the request payload structure for verifying phone ownership.
public record VerifyOtpDto
{
    public string PhoneNumber { get; init; } = string.Empty;
    public string Otp { get; init; } = string.Empty;
}