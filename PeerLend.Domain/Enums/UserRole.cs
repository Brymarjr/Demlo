namespace PeerLend.Domain.Enums;

/// Defines the strict, mutually exclusive system access levels.
/// Enforces Section 5.3 of the Engineering Bible.

public enum UserRole
{
    Borrower = 1,
    Lender = 2,
    Admin = 3
}
