using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using PeerLend.Application.Common.Interfaces;
using PeerLend.Application.DTOs;
using PeerLend.Domain.Entities;
using PeerLend.Domain.Enums;
using PeerLend.Infrastructure.Persistence;

namespace PeerLend.Infrastructure.Services;

// Coordinates customer authentication registration, anonymization, and profile binding.
// Enforces Section 5 and Section 6 onboarding rules.
public class UserService : IUserService
{
    private readonly PeerLendDbContext _context;
    private readonly ISecurityService _securityService;
    private readonly ISmsService _smsService;
    private readonly IDistributedCache _cache;

    // Inject our database context, security services, messaging clients, and Redis caching infrastructure
    public UserService(
        PeerLendDbContext context,
        ISecurityService securityService,
        ISmsService smsService,
        IDistributedCache cache)
    {
        _context = context;
        _securityService = securityService;
        _smsService = smsService;
        _cache = cache;
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
                var user = new User
                {
                    Email = request.Email.ToLowerInvariant(),
                    PhoneNumber = request.PhoneNumber,
                    PasswordHash = passwordHash,
                    BvnHash = bvnHash,
                    NinHash = ninHash,
                    Role = mappedRole
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
        // 1. Reconstruct the standardized Redis lookup key (Section 6.2)
        var cacheKey = $"otp:{request.PhoneNumber}";

        // 2. Query our distributed Redis cache cluster for the stored token
        var cachedOtp = await _cache.GetStringAsync(cacheKey, cancellationToken);

        // 3. Fallback safely if token has naturally expired past its 5-minute lifespan
        if (string.IsNullOrWhiteSpace(cachedOtp))
        {
            throw new InvalidOperationException("The verification code has expired or was never generated.");
        }

        // 4. Cryptographically cross-validate the strings
        if (cachedOtp != request.Otp)
        {
            throw new ArgumentException("The submitted verification code is incorrect.");
        }

        // 5. Success: Purge the token from our cache cluster immediately to prevent replay attempts
        await _cache.RemoveAsync(cacheKey, cancellationToken);

        // NOTE: In the upcoming user status milestones, we will toggle the user's account status
        // database property to IsPhoneVerified = true.

        return true;
    }

    // Generates an unguessable 6-digit numeric string using a Cryptographically Secure Pseudo-Random Number Generator (CSPRNG)
    private static string GenerateSecureOtp()
    {
        // Enforces cryptographically strong randomization to eliminate predictability vectors
        return RandomNumberGenerator.GetInt32(100000, 999999).ToString();
    }
}