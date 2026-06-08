using Microsoft.EntityFrameworkCore;
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

    public UserService(PeerLendDbContext context, ISecurityService securityService)
    {
        _context = context;
        _securityService = securityService;
    }

    public async Task<Guid> RegisterUserAsync(RegisterUserDto request, CancellationToken cancellationToken = default)
    {
        // Enforce EF Core's native execution strategy to handle transient database connection retries safely
        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // Open an atomic database transaction block to guarantee cross-profile consistency
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

                // 3. Transform data parameters using our security engine
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
                        CreditScore = 300, // Baseline statutory score
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

                // Commit the physical database transaction safely
                await transaction.CommitAsync(cancellationToken);

                return user.Id;
            }
            catch
            {
                // Roll back the entire transaction if any part fails to prevent data corruption
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }
}