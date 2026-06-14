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
    private readonly IPaystackDisbursementService _disbursementService; // ◄ 1. INJECT DISBURSEMENT SERVICE

    public LoanMatchingJob(
        DemloDbContext context, 
        IGlobalPolicyEngine policyEngine,
        IPaystackDisbursementService disbursementService) // ◄ 2. ADD TO CONSTRUCTOR
    {
        _context = context;
        _policyEngine = policyEngine;
        _disbursementService = disbursementService;
    }

    public async Task RunMatchingCycleAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"[MATCHING ENGINE] Starting allocation sequence at: {DateTime.UtcNow}");

        string unitPolicyStr = await _policyEngine.GetPolicyValueAsync("TARGET_ALLOCATION_UNIT_KOBO", "500000", cancellationToken);
        long targetUnitSizeKobo = long.Parse(unitPolicyStr);

        var approvedLoans = await _context.Loans
            .Where(l => l.Status == LoanStatus.AwaitingMatch)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(cancellationToken);

        if (!approvedLoans.Any())
        {
            Console.WriteLine("[MATCHING ENGINE] Zero assets awaiting capital matching. Exiting cycle.");
            return;
        }

        var lenderPool = await _context.LenderProfiles
            .Where(p => p.AvailableBalanceKobo >= targetUnitSizeKobo)
            .ToListAsync(cancellationToken);

        if (!lenderPool.Any())
        {
            Console.WriteLine("[MATCHING ENGINE] Critical Liquidity Warning: Zero lenders available.");
            return;
        }

        int lenderIndexPointer = 0;

        foreach (var loan in approvedLoans)
        {
            using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            bool dispatchPayout = false;

            try
            {
                long remainingLoanAmountKobo = loan.PrincipalAmountKobo;
                int checksCount = 0;
                int maxLenderChecks = lenderPool.Count * 2;

                while (remainingLoanAmountKobo >= targetUnitSizeKobo && checksCount < maxLenderChecks)
                {
                    checksCount++;
                    var currentLender = lenderPool[lenderIndexPointer];

                    if (currentLender.AvailableBalanceKobo >= targetUnitSizeKobo)
                    {
                        currentLender.AvailableBalanceKobo -= targetUnitSizeKobo;
                        remainingLoanAmountKobo -= targetUnitSizeKobo;

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
                    }

                    lenderIndexPointer = (lenderIndexPointer + 1) % lenderPool.Count;
                }

                if (remainingLoanAmountKobo == 0)
                {
                    loan.TransitionTo(LoanStatus.Matched);
                    await _context.SaveChangesAsync(cancellationToken);
                    await dbTransaction.CommitAsync(cancellationToken);
                    
                    Console.WriteLine($"[MATCH SUCCESS] Asset {loan.Id} committed. Queueing immediate bank transfer.");
                    dispatchPayout = true; // Set flag to trigger payout *after* database transaction closes safely
                }
                else
                {
                    await dbTransaction.RollbackAsync(cancellationToken);
                    Console.WriteLine($"[MATCH INCOMPLETE] Rolling back partial allocations for Asset {loan.Id}.");
                }
            }
            catch (Exception ex)
            {
                await dbTransaction.RollbackAsync(cancellationToken);
                Console.WriteLine($"[CRITICAL ERROR] Failed matching block pass for Asset {loan.Id}: {ex.Message}");
            }

            // 3. EXECUTE THE PHYSICAL PAYSTACK OUTWARD TRANSFER
            if (dispatchPayout)
            {
                // Triggered outside the main database transaction scope to avoid holding table locks during external API latency
                _ = Task.Run(async () => 
                {
                    bool payoutTriggered = await _disbursementService.InitiateLoanDisbursementAsync(loan.Id, CancellationToken.None);
                    Console.WriteLine($"[AUTOMATED DISBURSEMENT EVENT] Loan {loan.Id} payout initialization result: {payoutTriggered}");
                }, cancellationToken);
            }
        }
    }
}