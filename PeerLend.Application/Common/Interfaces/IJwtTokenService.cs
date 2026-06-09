using PeerLend.Domain.Entities;

namespace PeerLend.Application.Common.Interfaces;

// Defines core identity token generation and extraction behaviors.
// Enforces Section 5.3 security parameters of the Engineering Bible.
public interface IJwtTokenService
{
    // Generates a short-lived (15 min) secure access token carrying user identity and role claims.
    string GenerateAccessToken(User user);

    // Generates a cryptographically strong, unique refresh token string.
    string GenerateRefreshToken();
}
