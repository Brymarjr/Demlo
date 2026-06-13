using System.ComponentModel.DataAnnotations.Schema;
using Demlo.Domain.Common;

namespace Demlo.Domain.Entities;

// Represents a secure tracking log for long-lived authentication sessions.
// Enforces Section 5.3 Refresh Token Rotation (RTR) invariants.
public class RefreshToken : BaseEntity
{
    // Cryptographically secure token string identifier (CSPRNG generated base64)
    public string Token { get; set; } = string.Empty;

    // The unique jwt token identifier (JTI) of the matching access token issued alongside this record
    public string JwtId { get; set; } = string.Empty;

    // Direct relationship link to the owner account record
    public Guid UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    // Precise expiration timeline calculated at the point of issuance (7 Days)
    public DateTime ExpiryDate { get; set; }

    // Session revocation state trackers to handle immediate compromise invalidation
    public bool IsRevoked { get; set; }

    public bool IsUsed { get; set; }

    // Evaluates if the token session has naturally run out past its statutory window
    public bool IsExpired => DateTime.UtcNow >= ExpiryDate;

    // Combined conditional checkpoint to verify if a token state can actively perform a rotation exchange
    public bool IsActive => !IsRevoked && !IsUsed && !IsExpired;
}