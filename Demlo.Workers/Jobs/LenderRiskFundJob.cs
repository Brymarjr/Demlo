using Microsoft.EntityFrameworkCore;
using Demlo.Infrastructure.Persistence;
using Demlo.Domain.Entities;
using Demlo.Domain.Enums;

namespace Demlo.Workers.Jobs;

public class LenderRiskFundJob
{
    private readonly DemloDbContext _context;

    public LenderRiskFundJob(DemloDbContext context)
    {
        _context = context;
    }

    public async Task LiquidateDefaultedClaimsAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"[INSURANCE ENGINE] Initiating defaulted loan insurance coverage sweep at: {DateTime.UtcNow}");

        var lrfPool = await _context.LenderRiskFundPools
            .FirstOrDefaultAsync(p => p.PoolCode == "LRF_MASTER_NGN", cancellationToken);

        if (lrfPool == null)
        {
            Console.WriteLine("[INSURANCE ENGINE ERROR] Global master Lender Risk Fund pool row not initialized in database. Aborting.");
            return;
        }

        var defaultedLoans = await _context.Loans
            .Where(l => l.Status == LoanStatus.Defaulted)
            .ToListAsync(cancellationToken);

        if (!defaultedLoans.Any()) return;

        foreach (var loan in defaultedLoans)
        {
            var allocations = await _context.LoanAllocations
                .Where(a => a.LoanId == loan.Id)
                .ToListAsync(cancellationToken);

            if (!allocations.Any()) continue;

            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                long totalClaimPayoutRequiredKobo = 0;

                foreach (var allocation in allocations)
                {
                    // ──► FIXED: Using your exact property 'LenderId' instead of LenderProfileId
                    var lender = await _context.LenderProfiles
                        .FirstOrDefaultAsync(l => l.Id == allocation.LenderId, cancellationToken);

                    if (lender == null) continue;

                    // ──► FIXED: Using your exact property 'AllocatedAmountKobo' instead of AmountKobo
                    long lenderLossClaimKobo = allocation.AllocatedAmountKobo; 
                    totalClaimPayoutRequiredKobo += lenderLossClaimKobo;

                    lender.AvailableBalanceKobo += lenderLossClaimKobo;
                }

                if (lrfPool.TotalReservesKobo < totalClaimPayoutRequiredKobo)
                {
                    Console.WriteLine($"[CRITICAL LRF INSOLVENCY] LRF Pool balance ({lrfPool.TotalReservesKobo} Kobo) is insufficient to clear claim of {totalClaimPayoutRequiredKobo} Kobo for Loan {loan.Id}. Halting execution.");
                    await transaction.RollbackAsync(cancellationToken);
                    continue;
                }

                long balanceBefore = lrfPool.TotalReservesKobo;
                lrfPool.TotalReservesKobo -= totalClaimPayoutRequiredKobo;
                lrfPool.LastUpdatedAt = DateTime.UtcNow;

                var lrfLedgerLog = new LenderRiskFundLedger
                {
                    Id = Guid.NewGuid(),
                    TransactionReference = $"lrf_claim_liquidation_{loan.Id}",
                    EntryType = "DEFAULT_CLAIM_OUTFLUX",
                    AssociatedLoanId = loan.Id,
                    AmountKobo = totalClaimPayoutRequiredKobo,
                    BalanceBeforeKobo = balanceBefore,
                    BalanceAfterKobo = lrfPool.TotalReservesKobo,
                    Narrative = $"Automated insurance liquidation payout covering {allocations.Count} fractional lenders for defaulted asset reference.",
                    CreatedAt = DateTime.UtcNow
                };
                await _context.LenderRiskFundLedgers.AddAsync(lrfLedgerLog, cancellationToken);

                // ──► FIXED: Using your explicit Enum state 'ClosedWrittenOff' instead of 'Closed'
                loan.TransitionTo(LoanStatus.ClosedWrittenOff);

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                Console.WriteLine($"[INSURANCE CLEAR] Loan {loan.Id} successfully liquidated via LRF. {totalClaimPayoutRequiredKobo} Kobo distributed back to lenders.");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                Console.WriteLine($"[INSURANCE PIPELINE CRASH] Failed executing automated LRF liquidation for Loan {loan.Id}: {ex.Message}");
            }
        }
    }
}