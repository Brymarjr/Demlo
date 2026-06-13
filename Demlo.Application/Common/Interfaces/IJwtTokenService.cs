using System.Security.Claims;
using Demlo.Domain.Entities;

namespace Demlo.Application.Common.Interfaces;

// Defines core identity token generation and extraction behaviors.
// Enforces Section 5.3 security parameters of the Engineering Bible.
public interface IJwtTokenService
{
    // Generates a short-lived (15 min) secure access token carrying user identity and role claims.
    string GenerateAccessToken(User user);

    // Generates a cryptographically strong, unique refresh token string.
    string GenerateRefreshToken();

    // Persists an active refresh token tracking record to the database (PL-22)
    Task SaveRefreshTokenAsync(Guid userId, string token, string jwtId, CancellationToken cancellationToken = default);

    // Extracts claims principal configurations from an expired token payload to verify session identity (PL-22)
    ClaimsPrincipal GetPrincipalFromExpiredToken(string token);
}