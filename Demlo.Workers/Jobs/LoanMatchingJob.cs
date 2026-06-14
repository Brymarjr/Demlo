using Microsoft.EntityFrameworkCore;
using Hangfire;
using Demlo.Application.Common.Interfaces;
using Demlo.Domain.Enums;
using Demlo.Domain.Entities;
using Demlo.Infrastructure.Persistence;

namespace Demlo.Workers.Jobs;

// Automated capital allocation architecture running on a 5-minute persistent execution cycle.
// Enforces Section 6.2 fractional distribution and weighted round-robin diversification logic.
public class LoanMatchingJob
{
    private readonly DemloDbContext _context;
    private readonly IGlobalPolicyEngine _policyEngine;

    public LoanMatchingJob(DemloDbContext context, IGlobalPolicyEngine policyEngine)
    {
        _context = context;
        _policyEngine = policyEngine;
    }

    // Invoked automatically every 5 minutes by the Hangfire server wrapper
    public async Task RunMatchingCycleAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"[MATCHING ENGINE] Starting automated allocation cycle at: {DateTime.UtcNow}");

        // 1. Establish an atomic transaction block to enforce row-level safety rules
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // 2. Fetch the approved loan queue ordered by oldest first to prevent starvation
            var approvedLoans = await _context.Loans
                .Where(l => l.Status == LoanStatus.AwaitingMatch)
                .OrderBy(l => l.CreatedAt)
                .ToListAsync(cancellationToken);

            if (!approvedLoans.Any())
            {
                Console.WriteLine("[MATCHING ENGINE] Zero asset applications in AWAITING_MATCH status. Ending cycle.");
                return;
            }

            // 3. Fetch our base target allocation unit rule (Defaulting to NGN 5,000 / 500,000 Kobo)
            string unitPolicyStr = await _policyEngine.GetPolicyValueAsync("TARGET_ALLOCATION_UNIT_KOBO", "500000", cancellationToken);
            long targetUnitSizeKobo = long.Parse(unitPolicyStr);

            foreach (var loan in approvedLoans)
            {
                // Calculate the fractional allocation segments needed to fund this asset complete
                long loanPrincipalKobo = loan.PrincipalAmountKobo;
                long requiredUnitsCount = loanPrincipalKobo / targetUnitSizeKobo;

                Console.WriteLine($"[PROCESSING] Asset {loan.Id} requires {requiredUnitsCount} fractional distribution lines.");

                // Next, we will incorporate our weighted round-robin selector to bind lenders to these units
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            Console.WriteLine($"[MATCHING CRITICAL ERROR] Capital matching pass failed: {ex.Message}");
        }
    }
}