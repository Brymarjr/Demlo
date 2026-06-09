using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using PeerLend.Application.Common.Interfaces;
using PeerLend.Domain.Entities;
using PeerLend.Infrastructure.Persistence;

namespace PeerLend.Infrastructure.Services;

// Implements secure JWT token generation and validation mappings.
// Enforces Section 5.3 cryptographic standards of the Engineering Bible.
public class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _configuration;
    private readonly PeerLendDbContext _context;

    public JwtTokenService(IConfiguration configuration, PeerLendDbContext context)
    {
        _configuration = configuration;
        _context = context;
    }

    public string GenerateAccessToken(User user)
    {
        var secretKey = _configuration["JwtSettings:Secret"]
            ?? throw new InvalidOperationException("Cryptographic JWT Token Signing Key is unconfigured.");

        var issuer = _configuration["JwtSettings:Issuer"];
        var audience = _configuration["JwtSettings:Audience"];
        var expiryMinutes = double.Parse(_configuration["JwtSettings:ExpiryMinutes"] ?? "15");

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(expiryMinutes),
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = credentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);

        return tokenHandler.WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomNumber = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);

        return Convert.ToBase64String(randomNumber);
    }

    public async Task SaveRefreshTokenAsync(Guid userId, string token, string jwtId, CancellationToken cancellationToken = default)
    {
        var expiryDays = double.Parse(_configuration["JwtSettings:RefreshExpiryDays"] ?? "7");

        var refreshTokenEntity = new RefreshToken
        {
            Token = token,
            JwtId = jwtId,
            UserId = userId,
            ExpiryDate = DateTime.UtcNow.AddDays(expiryDays),
            IsUsed = false,
            IsRevoked = false
        };

        _context.RefreshTokens.Add(refreshTokenEntity);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public ClaimsPrincipal GetPrincipalFromExpiredToken(string token)
    {
        var secretKey = _configuration["JwtSettings:Secret"]
            ?? throw new InvalidOperationException("Cryptographic JWT Token Signing Key is unconfigured.");

        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudience = _configuration["JwtSettings:Audience"],
            ValidateIssuer = true,
            ValidIssuer = _configuration["JwtSettings:Issuer"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
            ValidateLifetime = false // ◄ CRITICAL: We want to read claims from an EXPIRED token
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out var securityToken);

        if (securityToken is not JwtSecurityToken jwtSecurityToken ||
            !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
        {
            throw new SecurityTokenException("Invalid encryption token algorithm signature detected.");
        }

        return principal;
    }
}