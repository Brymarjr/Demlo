using Microsoft.EntityFrameworkCore;
using Demlo.Application.Common.Interfaces;
using Demlo.Domain.Enums;
using Demlo.Domain.Entities;
using Demlo.Infrastructure.Persistence;

namespace Demlo.Workers.Jobs;

public class LoanMatchingJob
{
    private readonly DemloDbContext _context;
    private readonly IGlobalPolicyEngine _policyEngine;

    public LoanMatchingJob(DemloDbContext context, IGlobalPolicyEngine policyEngine)
    {
        _context = context;
        _policyEngine = policyEngine;
    }

    public async Task RunMatchingCycleAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"[MATCHING ENGINE] Starting allocation sequence at: {DateTime.UtcNow}");

        // 1. Fetch our base target allocation unit rule (NGN 5,000 / 500,000 Kobo)
        string unitPolicyStr = await _policyEngine.GetPolicyValueAsync("TARGET_ALLOCATION_UNIT_KOBO", "500000", cancellationToken);
        long targetUnitSizeKobo = long.Parse(unitPolicyStr);

        // 2. Fetch the approved loan queue ordered by oldest first to prevent asset starvation
        var approvedLoans = await _context.Loans
            .Where(l => l.Status == LoanStatus.AwaitingMatch)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(cancellationToken);

        if (!approvedLoans.Any())
        {
            Console.WriteLine("[MATCHING ENGINE] Zero assets awaiting capital matching. Exiting cycle.");
            return;
        }

        // 3. Fetch all active lender profiles who have at least enough capital for 1 allocation block
        // We order by Id (or any tracking timestamp) to create a baseline round-robin sequence pool
        var lenderPool = await _context.LenderProfiles
            .Where(p => p.AvailableBalanceKobo >= targetUnitSizeKobo)
            .ToListAsync(cancellationToken);

        if (!lenderPool.Any())
        {
            Console.WriteLine("[MATCHING ENGINE] Critical Liquidity Warning: Zero lenders meet the minimum unit threshold.");
            return;
        }

        int lenderIndexPointer = 0;

        // 4. Begin matching orchestration loop
        foreach (var loan in approvedLoans)
        {
            using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                long remainingLoanAmountKobo = loan.PrincipalAmountKobo;
                Console.WriteLine($"[MATCHING] Processing Asset {loan.Id}. Total capital needed: {remainingLoanAmountKobo} Kobo.");

                // Track total iterations to prevent an infinite loop if the collective liquidity pool runs dry mid-pass
                int checksCount = 0;
                int maxLenderChecks = lenderPool.Count * 2;

                while (remainingLoanAmountKobo >= targetUnitSizeKobo && checksCount < maxLenderChecks)
                {
                    checksCount++;
                    var currentLender = lenderPool[lenderIndexPointer];

                    // Verify this specific lender has enough money left in their rolling wallet block
                    if (currentLender.AvailableBalanceKobo >= targetUnitSizeKobo)
                    {
                        // Deduct the unit size from lender's local available tracking memory
                        currentLender.AvailableBalanceKobo -= targetUnitSizeKobo;
                        remainingLoanAmountKobo -= targetUnitSizeKobo;

                        // Create the immutable fractional contract assignment block (Enforces your exact field setup)
                        var allocationRecord = new LoanAllocation
                        {
                            Id = Guid.NewGuid(),
                            LoanId = loan.Id,
                            LenderId = currentLender.UserId,
                            AllocatedAmountKobo = targetUnitSizeKobo,
                            InterestEarnedKobo = 0,
                            Status = "YIELDING",
                            CreatedAt = DateTime.UtcNow
                        };

                        await _context.LoanAllocations.AddAsync(allocationRecord, cancellationToken);
                        Console.WriteLine($"[ALLOCATED] Lender {currentLender.UserId} matched to Loan {loan.Id} for {targetUnitSizeKobo} Kobo.");
                    }

                    // Move the round-robin index pointer to the next lender in sequence, looping back if at the end
                    lenderIndexPointer = (lenderIndexPointer + 1) % lenderPool.Count;
                }

                // 5. Check if the asset was fully covered during this pass
                if (remainingLoanAmountKobo == 0)
                {
                    // Update state inside our domain boundaries
                    loan.TransitionTo(LoanStatus.Matched);
                    Console.WriteLine($"[MATCH SUCCESS] Asset {loan.Id} is 100% matched and transitioning to MATCHED state.");
                    
                    await _context.SaveChangesAsync(cancellationToken);
                    await dbTransaction.CommitAsync(cancellationToken);
                }
                else
                {
                    // Roll back this individual asset match if available market liquidity couldn't fully clear it
                    await dbTransaction.RollbackAsync(cancellationToken);
                    Console.WriteLine($"[MATCH INCOMPLETE] Insufficient liquid capital to fully fund Asset {loan.Id}. Rolling back partial allocations.");
                }
            }
            catch (Exception ex)
            {
                await dbTransaction.RollbackAsync(cancellationToken);
                Console.WriteLine($"[CRITICAL ERROR] Failed matching block pass for Asset {loan.Id}: {ex.Message}");
            }
        }
    }
}