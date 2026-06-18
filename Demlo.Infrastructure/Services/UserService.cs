using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Demlo.Application.Common.Interfaces;
using Demlo.Application.DTOs;
using Demlo.Domain.Entities;
using Demlo.Domain.Enums;
using Demlo.Infrastructure.Persistence;
using System.Security.Cryptography;

namespace Demlo.Infrastructure.Services;

// Coordinates customer authentication registration, anonymization, and profile binding.
// Enforces Section 5 and Section 6 onboarding rules.
public class UserService : IUserService
{
    private readonly DemloDbContext _context;
    private readonly ISecurityService _securityService;
    private readonly ISmsService _smsService;
    private readonly IDistributedCache _cache;
    private readonly IJwtTokenService _tokenService;
    private readonly IKycService _kycService;

    // Inject our database context, security services, messaging clients, Redis caching token engine, and KYC service
    public UserService(
        DemloDbContext context,
        ISecurityService securityService,
        ISmsService smsService,
        IDistributedCache cache,
        IJwtTokenService tokenService,
        IKycService kycService)
    {
        _context = context;
        _securityService = securityService;
        _smsService = smsService;
        _cache = cache;
        _tokenService = tokenService;
        _kycService = kycService;
    }

    public async Task<Guid> RegisterUserAsync(RegisterUserDto request, CancellationToken cancellationToken = default)
    {
        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                // 1. Validate and map structural user roles from payload
                if (!Enum.TryParse<UserRole>(request.Role, true, out var mappedRole))
                {
                    throw new ArgumentException($"Invalid registration role target: {request.Role}");
                }

                // 2. Enforce absolute email and phone uniqueness constraints before processing
                var identityConflict = await _context.Users.AnyAsync(u =>
                    u.Email.ToLower() == request.Email.ToLower() || u.PhoneNumber == request.PhoneNumber,
                    cancellationToken);

                if (identityConflict)
                {
                    throw new InvalidOperationException("A user account with this email or phone number is already registered.");
                }

                // 3. Transform sensitive identifiers using our security engine
                var passwordHash = _securityService.HashPassword(request.Password);
                var bvnHash = _securityService.HashIdentity(request.Bvn);
                var ninHash = _securityService.HashIdentity(request.Nin);

                // 4. Construct the core user account matrix record
                var currentEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
                bool isDevelopment = string.Equals(currentEnv, "Development", StringComparison.OrdinalIgnoreCase);

                var user = new User
                {
                    Email = request.Email.ToLowerInvariant(),
                    PhoneNumber = request.PhoneNumber,
                    PasswordHash = passwordHash,
                    BvnHash = bvnHash,
                    NinHash = ninHash,
                    Role = mappedRole,
                    KycStatus = "PENDING"
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync(cancellationToken);

                // 5. Enforce Section 6 Trigger Rule: Generate corresponding profile records using your precise domain fields
                if (mappedRole == UserRole.Borrower)
                {
                    var borrowerProfile = new BorrowerProfile
                    {
                        UserId = user.Id,
                        CreditScore = 300,
                        MaxLoanLimitKobo = 0,
                        IncomeKobo = 0,
                        BankAccountId = string.Empty
                    };
                    _context.BorrowerProfiles.Add(borrowerProfile);
                }
                else if (mappedRole == UserRole.Lender)
                {
                    var lenderProfile = new LenderProfile
                    {
                        UserId = user.Id,
                        TotalDepositedKobo = 0,
                        AvailableBalanceKobo = 0,
                        TotalEarnedKobo = 0
                    };
                    _context.LenderProfiles.Add(lenderProfile);
                }

                await _context.SaveChangesAsync(cancellationToken);

                // 6. Cryptographically Generate Onboarding OTP Token (PL-16)
                var otpCode = GenerateSecureOtp();

                // 7. Commit Token to Redis Cache with a strict 5-Minute Lifespan (Section 6.2)
                var cacheKey = $"otp:{user.PhoneNumber}";
                var cacheOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                };
                await _cache.SetStringAsync(cacheKey, otpCode, cacheOptions, cancellationToken);

                // 8. Fire OTP Outbound via Termii Communication Gateway
                // Executed asynchronously right before transaction completion
                var smsDispatched = await _smsService.SendVerificationOtpAsync(user.PhoneNumber, otpCode, cancellationToken);
                if (!smsDispatched)
                {
                    Console.WriteLine($"[WARNING] Automated OTP notification delivery failed for user: {user.Id}");
                }

                // Commit the physical database transaction safely
                await transaction.CommitAsync(cancellationToken);

                return user.Id;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    public async Task<bool> VerifyOtpAsync(VerifyOtpDto request, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"otp:{request.PhoneNumber}";

        // 1. Fetch the active OTP sequence currently retained inside our Redis cache store
        var cachedOtp = await _cache.GetStringAsync(cacheKey, cancellationToken);

        // ──► FIXED: Read the system environment variable directly (No dependencies required)
        var currentEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        bool isDevelopment = string.Equals(currentEnv, "Development", StringComparison.OrdinalIgnoreCase);

        // DEFENSIVE GUARD: Bypass code is strictly locked to local Development instances
        bool isTestBypass = isDevelopment && request.Otp == "123456";

        if (!isTestBypass && (string.IsNullOrEmpty(cachedOtp) || cachedOtp != request.Otp))
        {
            throw new ArgumentException("The submitted verification token is invalid or has expired.");
        }

        // 2. Fetch the target user profile configuration out of PostgreSQL to acquire compliance identities
        var user = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == request.PhoneNumber, cancellationToken);
        if (user == null)
        {
            throw new InvalidOperationException("No user account matching the validated phone metadata could be discovered.");
        }

        // UPDATE AND SAVE THE VERIFIED STATE CHANGE
        user.KycStatus = "VERIFIED";
        _context.Users.Update(user);
        await _context.SaveChangesAsync(cancellationToken);

        // 3. Purge the consumed OTP sequence from our Redis cluster to prevent token replay leaks
        if (!isTestBypass)
        {    
             await _cache.RemoveAsync(cacheKey, cancellationToken);
        }
        // 4. AUTOMATED KYC KICKOFF LOOP (Section 6.3): Dispatch async requests directly to Smile ID
        Console.WriteLine($"[KYC TRIGGER] Phone verification successful for User {user.Id}. Initializing asynchronous compliance validation paths.");

        // Dispatch BVN registration matching asynchronously
        _ = Task.Run(async () =>
        {
            try
            {
                // ◄ CHANGED: user.Bvn modified to user.BvnHash to match your Domain Entity
                var bvnResult = await _kycService.VerifyBvnAsync(user, user.BvnHash, CancellationToken.None);
                Console.WriteLine($"[KYC BACKGROUND DISPATCH] BVN job submission state for user {user.Id}: {bvnResult}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRITICAL KYC BACKGROUND FAULT] Failed to forward BVN task context: {ex.Message}");
            }
        }, CancellationToken.None);

        // Dispatch NIN identity verification records asynchronously
        _ = Task.Run(async () =>
        {
            try
            {
                // ◄ CHANGED: user.Nin modified to user.NinHash to match your Domain Entity
                var ninResult = await _kycService.VerifyNinAsync(user, user.NinHash, CancellationToken.None);
                Console.WriteLine($"[KYC BACKGROUND DISPATCH] NIN job submission state for user {user.Id}: {ninResult}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRITICAL KYC BACKGROUND FAULT] Failed to forward NIN task context: {ex.Message}");
            }
        }, CancellationToken.None);

        return true;
    }

    public async Task<TokenResponseDto> RefreshTokenAsync(TokenRequestDto request, CancellationToken cancellationToken = default)
    {
        // 1. Extract user claims principal out of the expired incoming token safely
        var principal = _tokenService.GetPrincipalFromExpiredToken(request.AccessToken);

        // Search across both standard token layout namespaces to capture the user ID string
        var userIdClaim = principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                          ?? principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                          
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new Microsoft.IdentityModel.Tokens.SecurityTokenException("Invalid token payload claims principal formatting.");
        }

        // 2. Fetch the refresh token record from PostgreSQL
        var storedRefreshToken = await _context.RefreshTokens
            .FirstOrDefaultAsync(t => t.Token == request.RefreshToken, cancellationToken);

        if (storedRefreshToken == null)
        {
            throw new Microsoft.IdentityModel.Tokens.SecurityTokenException("The submitted refresh token does not exist.");
        }

        // 3. REPLAY ATTACK DETECTION (Section 5.3)
        if (storedRefreshToken.IsUsed)
        {
            var activeUserTokens = await _context.RefreshTokens
                .Where(t => t.UserId == userId && !t.IsRevoked)
                .ToListAsync(cancellationToken);

            foreach (var token in activeUserTokens)
            {
                token.IsRevoked = true;
            }

            await _context.SaveChangesAsync(cancellationToken);
            throw new Microsoft.IdentityModel.Tokens.SecurityTokenException("Breach Warning: Refresh token reuse detected! All active sessions revoked.");
        }

        // 4. Validate expiration and revocation state invariants
        if (storedRefreshToken.IsRevoked || storedRefreshToken.ExpiryDate <= DateTime.UtcNow)
        {
            throw new Microsoft.IdentityModel.Tokens.SecurityTokenException("The submitted refresh token has expired or has been revoked.");
        }

        // 5. Cross-reference JTI mapping constraints to ensure the token pairs match
        var incomingJti = principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;
        if (storedRefreshToken.JwtId != incomingJti)
        {
            throw new Microsoft.IdentityModel.Tokens.SecurityTokenException("Token binding configuration mismatch detected.");
        }

        var user = await _context.Users.FindAsync(new object[] { userId }, cancellationToken);
        if (user == null)
        {
            throw new Microsoft.IdentityModel.Tokens.SecurityTokenException("User profile linked to token context could not be found.");
        }

        // 6. Enforce rotation sequence: Mark the old token as used
        storedRefreshToken.IsUsed = true;

        // 7. Spin up a brand new access token and high-entropy refresh token pair
        var newAccessToken = _tokenService.GenerateAccessToken(user);
        var newRefreshToken = _tokenService.GenerateRefreshToken();

        // 8. Extract the new JTI tracking ID and save the new refresh token to PostgreSQL
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var decodedToken = handler.ReadJwtToken(newAccessToken);
        var newJti = decodedToken.Claims.FirstOrDefault(c => c.Type == System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value
            ?? Guid.NewGuid().ToString();

        await _tokenService.SaveRefreshTokenAsync(user.Id, newRefreshToken, newJti, cancellationToken);

        return new TokenResponseDto
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken,
            Message = "Token session rotated successfully."
        };
    }

    // Generates an unguessable 6-digit numeric string using a Cryptographically Secure Pseudo-Random Number Generator (CSPRNG)
    private static string GenerateSecureOtp()
    {
        // Enforces cryptographically strong randomization to eliminate predictability vectors
        return RandomNumberGenerator.GetInt32(100000, 999999).ToString();
    }
}