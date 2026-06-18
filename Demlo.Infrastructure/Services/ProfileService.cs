using Microsoft.EntityFrameworkCore;
using Demlo.Application.Common.Interfaces;
using Demlo.Application.DTOs;
using Demlo.Infrastructure.Persistence;

namespace Demlo.Infrastructure.Services;

public class ProfileService : IProfileService
{
    private readonly DemloDbContext _context;

    public ProfileService(DemloDbContext context)
    {
        _context = context;
    }

    public async Task<bool> UpdateBorrowerProfileAsync(Guid userId, UpdateBorrowerProfileDto request, CancellationToken cancellationToken = default)
    {
        // 1. Fetch the existing borrower profile created during registration
        var profile = await _context.BorrowerProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (profile == null)
        {
            throw new InvalidOperationException("Borrower profile record could not be located for the authenticated user.");
        }

        // 2. Map the Tier-1 onboarding payload parameters to the domain entity
        profile.OnboardingAddress = request.OnboardingAddress;
        profile.IncomeKobo = request.MonthlyIncomeKobo;
        profile.EmploymentStatus = request.EmploymentStatus;
        profile.BankName = request.BankName;
        profile.BankAccountNumber = request.BankAccountNumber;

        // 3. Commit the state changes back to PostgreSQL
        _context.BorrowerProfiles.Update(profile);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> UpdateLenderProfileAsync(Guid userId, UpdateLenderProfileDto request, CancellationToken cancellationToken = default)
    {
        // 1. Fetch the existing lender profile
        var profile = await _context.LenderProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (profile == null)
        {
            throw new InvalidOperationException("Lender profile record could not be located for the authenticated user.");
        }

        // 2. Map the Tier-1 payload parameters
        profile.OnboardingAddress = request.OnboardingAddress;
        profile.BankName = request.BankName;
        profile.BankAccountNumber = request.BankAccountNumber;

        // 3. Commit state changes
        _context.LenderProfiles.Update(profile);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}