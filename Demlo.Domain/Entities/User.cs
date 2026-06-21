using Demlo.Domain.Common;
using Demlo.Domain.Enums;

namespace Demlo.Domain.Entities;

// Represents the authoritative user account record within the platform ecosystem.
// Inherits core tracking and audit capabilities directly from BaseEntity.
public class User : BaseEntity
{
    // The role assigned to the user profile, determining system access permissions.
    // Enforces the strict rule that a user can only hold one role (Borrower, Lender, or Admin).
    public UserRole Role { get; set; }

    // The primary, authenticated mobile number used during registration and OTP verification.
    public string PhoneNumber { get; set; } = string.Empty;

    // The verified email address used for compliance reporting and transaction alerts.
    public string Email { get; set; } = string.Empty;

    // A secure, salted Bcrypt hash of the user's password. Raw passwords are never stored.
    public string PasswordHash { get; set; } = string.Empty;

    // A secure, salted SHA-256 hash of the borrower's Bank Verification Number (BVN).
    // The raw BVN is discarded after validation; this hash prevents duplicate identity sign-ups.
    public string BvnHash { get; set; } = string.Empty;

    // A secure, salted SHA-256 hash of the borrower's National Identification Number (NIN).
    // Used identically to the BVN hash to protect against synthetic identity fraud.
    public string NinHash { get; set; } = string.Empty;

    // The current state of the user's Know Your Customer onboarding pipeline verification.
    public string KycStatus { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    // Flag to enforce mandatory password rotation upon first login for administratively provisioned accounts.
    public bool IsTemporaryPassword { get; set; } = false;

    // Audit tracking for the last time the credential hash was updated.
    public DateTime? LastPasswordChangedAt { get; set; }
}
