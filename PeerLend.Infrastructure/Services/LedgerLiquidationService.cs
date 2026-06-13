using Microsoft.EntityFrameworkCore;
using PeerLend.Application.Common.Interfaces;
using PeerLend.Domain.Entities;
using PeerLend.Infrastructure.Persistence;

namespace PeerLend.Infrastructure.Services;

public class LedgerLiquidationService : ILedgerLiquidationService
{
    private readonly PeerLendDbContext _context;

    public LedgerLiquidationService(PeerLendDbContext context)
    {
        _context = context;
    }

    public async Task<bool> DistributeRepaymentAsync(Guid loanId, long totalReceivedKobo, CancellationToken cancellationToken)
    {
        using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var loan = await _context.Loans.AsNoTracking().FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken);
            if (loan == null) return false;

            var allocations = await _context.LoanAllocations
                .Where(a => a.LoanId == loanId)
                .ToListAsync(cancellationToken);

            if (!allocations.Any()) return false;

            // Aligned to use your exact entity property name: AllocatedAmountKobo
            long totalAllocatedPrincipalKobo = allocations.Sum(a => a.AllocatedAmountKobo);
            long runningDistributedKobo = 0;

            foreach (var allocation in allocations)
            {
                // Aligned to use your exact entity property name: AllocatedAmountKobo
                double lenderExposureRatio = (double)allocation.AllocatedAmountKobo / totalAllocatedPrincipalKobo;
                
                long lenderShareKobo = (long)Math.Floor(totalReceivedKobo * lenderExposureRatio);
                runningDistributedKobo += lenderShareKobo;

                var lenderProfile = await _context.LenderProfiles
                    .FirstOrDefaultAsync(p => p.UserId == allocation.LenderId, cancellationToken);

                if (lenderProfile != null)
                {
                    lenderProfile.AvailableBalanceKobo += lenderShareKobo;
                    
                    // Aligned to use your exact entity property name: AllocatedAmountKobo
                    lenderProfile.TotalEarnedKobo += (lenderShareKobo - allocation.AllocatedAmountKobo > 0) 
                        ? (lenderShareKobo - allocation.AllocatedAmountKobo) : 0;
                }

                var ledgerDebit = new LedgerEntry
                {
                    Id = Guid.NewGuid(),
                    DebitAccountId = loan.BorrowerId, 
                    CreditAccountId = allocation.LenderId, 
                    AmountKobo = lenderShareKobo,
                    Type = "REPAYMENT_DISTRIBUTION",
                    ReferenceId = loanId,
                    CreatedAt = DateTime.UtcNow
                };

                await _context.LedgerEntries.AddAsync(ledgerDebit, cancellationToken);
            }

            long platformFeeRemainderKobo = totalReceivedKobo - runningDistributedKobo;
            if (platformFeeRemainderKobo > 0)
            {
                var platformFeeDebit = new LedgerEntry
                {
                    Id = Guid.NewGuid(),
                    DebitAccountId = loan.BorrowerId,
                    CreditAccountId = Guid.Empty, 
                    AmountKobo = platformFeeRemainderKobo,
                    Type = "PLATFORM_SERVICING_FEE",
                    ReferenceId = loanId,
                    CreatedAt = DateTime.UtcNow
                };
                await _context.LedgerEntries.AddAsync(platformFeeDebit, cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            Console.WriteLine($"[LEDGER CRITICAL FAILURE] Aborted financial allocation routine: {ex.Message}");
            return false;
        }
    }
}