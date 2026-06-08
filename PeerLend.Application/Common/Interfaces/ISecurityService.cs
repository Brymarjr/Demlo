namespace PeerLend.Application.Common.Interfaces;

// Defines core cryptographic and security operations enforced by Section 5 of the Engineering Bible.
public interface ISecurityService
{
    // Hashes a raw user password using BCrypt with a Work Factor of 12.
    string HashPassword(string password);

    // Verifies a raw password against an existing BCrypt hash during authentication.
    bool VerifyPassword(string password, string hashedPassword);

    // Converts regulatory identity numbers (BVN/NIN) into salted, deterministic SHA-256 tokens.
    string HashIdentity(string rawIdentity);
}
