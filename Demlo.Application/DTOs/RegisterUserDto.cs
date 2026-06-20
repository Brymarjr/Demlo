using System.ComponentModel.DataAnnotations; // REQUIRED FOR DATA VALIDATION
using System.Text.Json.Serialization;

namespace Demlo.Application.DTOs;

// Defines the strict data contract for incoming registration payloads.
// Enforces Section 5.2 and Section 6.1 compliance parameters.
public record RegisterUserDto
{
    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Invalid email address format.")] // ──► Enforces TC-A-005
    public string Email { get; init; } = string.Empty;

    [Required(ErrorMessage = "Phone number is required.")]
    [JsonPropertyName("phone")]
    public string PhoneNumber { get; init; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters long.")] // Enforces TC-A-006
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,}$", 
        ErrorMessage = "Password must contain at least one uppercase letter, one lowercase letter, one number, and one special character.")]
    public string Password { get; init; } = string.Empty;

    [Required(ErrorMessage = "BVN is required.")]
    [StringLength(11, MinimumLength = 11, ErrorMessage = "BVN must be exactly 11 digits.")]
    public string Bvn { get; init; } = string.Empty;

    [Required(ErrorMessage = "NIN is required.")]
    [StringLength(11, MinimumLength = 11, ErrorMessage = "NIN must be exactly 11 digits.")]
    public string Nin { get; init; } = string.Empty;

    [Required(ErrorMessage = "User role track is required.")]
    public string Role { get; init; } = string.Empty;

    [Required(ErrorMessage = "First name is required.")]
    [StringLength(50)]
    public string FirstName { get; init; } = string.Empty;

    [Required(ErrorMessage = "Last name is required.")]
    [StringLength(50)]
    public string LastName { get; init; } = string.Empty;
}