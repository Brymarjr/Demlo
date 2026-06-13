using System.Security.Cryptography;
using System.Text;
using Demlo.Application.Common.Interfaces;

namespace Demlo.Infrastructure.Services;

// Implements bank-grade cryptographic hashing parameters matching Section 5.1 and 5.2.
public class SecurityService : ISecurityService
{
    // Explicit Work Factor of 12 mandated by Section 5 of the Engineering Bible for BCrypt operations.
    private const int BcryptWorkFactor = 12;

    // Fixed, secure application-level salt value used to ensure identity hashes (BVN/NIN) 
    // are deterministic across the ecosystem for duplicate detection.
    private const string IdentitySalt = "Demlo_Secret_Identity_Salt_2026_Secure_Token";

    public string HashPassword(string password)
    {
        return BCrypt.Net.BCrypt.EnhancedHashPassword(password, BcryptWorkFactor);
    }

    public bool VerifyPassword(string password, string hashedPassword)
    {
        return BCrypt.Net.BCrypt.EnhancedVerify(password, hashedPassword);
    }

    public string HashIdentity(string rawIdentity)
    {
        if (string.IsNullOrWhiteSpace(rawIdentity))
            return string.Empty;

        // Combine raw entry with our application salt to neutralize rainbow table attacks
        var saltedInput = $"{rawIdentity}{IdentitySalt}";

        var inputBytes = Encoding.UTF8.GetBytes(saltedInput);
        var hashBytes = SHA256.HashData(inputBytes);

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
