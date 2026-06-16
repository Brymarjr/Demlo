namespace Demlo.Application.DTOs;
using System.Text.Json.Serialization;

// Defines the strict data contract for incoming registration payloads.
// Enforces Section 5.2 and Section 6.1 compliance parameters.
public record RegisterUserDto
{
    public string Email { get; init; } = string.Empty;

    [JsonPropertyName("phone")]
    public string PhoneNumber { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    // Captured temporarily for validation and anonymization processing.
    // Must never be stored in plaintext.
    public string Bvn { get; init; } = string.Empty;

    // Captured temporarily for validation and anonymization processing.
    // Must never be stored in plaintext.
    public string Nin { get; init; } = string.Empty;

    // Determines if the user account should initialize a Borrower or Lender track.
    public string Role { get; init; } = string.Empty;
}
