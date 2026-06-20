using Microsoft.EntityFrameworkCore;
using Demlo.Application.Common.Interfaces;
using Demlo.Domain.Enums;
using Demlo.Infrastructure.Persistence;

namespace Demlo.Infrastructure.Services;

// Implements the settlement engine tracking debt collections, ledger balance closures, and yield distributions.
// Enforces Section 11.2 atomic transaction guardrails and dispatches receipt milestones.
public class RepaymentService : IRepaymentService
{
    private readonly DemloDbContext _context;
    private readonly ILoanService _loanService;
    private readonly INotificationService _notificationService;

    public RepaymentService(
        DemloDbContext context,
        ILoanService loanService,
        INotificationService notificationService)
    {
        _context = context;
        _loanService = loanService;
        _notificationService = notificationService;
    }

    public async Task<long> CalculateOutstandingBalanceAsync(Guid loanId, CancellationToken cancellationToken = default)
    {
        var loan = await _context.Loans.FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken);
        if (loan == null) return 0;

        double interestMultiplier = loan.InterestRateBps / 10000.0;
        long totalInterestKobo = (long)(loan.PrincipalAmountKobo * interestMultiplier);
        long totalPayableKobo = loan.PrincipalAmountKobo + totalInterestKobo;

        var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == loan.BorrowerId, cancellationToken);
        if (wallet == null) return totalPayableKobo;

        long totalRepaidKobo = await _context.Transactions
            .Where(t => t.WalletId == wallet.Id && t.Type == "DEBIT" && t.Description.Contains(loanId.ToString()))
            .SumAsync(t => t.AmountKobo, cancellationToken);

        long balanceRemaining = totalPayableKobo - totalRepaidKobo;
        return balanceRemaining < 0 ? 0 : balanceRemaining;
    }

    public async Task<bool> ProcessRepaymentAsync(Guid loanId, long amountKobo, CancellationToken cancellationToken = default)
    {
        if (amountKobo <= 0) throw new ArgumentException("Repayment processing metric must be greater than zero kobo.");

        using var dbTransaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var loan = await _context.Loans.FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken);
            if (loan == null || loan.Status != LoanStatus.Disbursed)
            {
                Console.WriteLine($"[REPAYMENT BLOCK] Asset {loanId} is missing or not currently in an active repayable state.");
                return false;
            }

            var borrowerWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == loan.BorrowerId, cancellationToken);
            if (borrowerWallet == null) return false;

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == loan.BorrowerId, cancellationToken);
            if (user == null) return false;

            // 1. Calculate Exact Debt and Prevent Overpayment
            long exactRemainingDebt = await CalculateOutstandingBalanceAsync(loanId, cancellationToken);
            if (exactRemainingDebt <= 0) return false;

            long actualDeductionKobo = amountKobo > exactRemainingDebt ? exactRemainingDebt : amountKobo;

            long currentWalletBalance = await _context.Transactions
                .Where(t => t.WalletId == borrowerWallet.Id)
                .SumAsync(t => t.Type == "CREDIT" ? t.AmountKobo : -t.AmountKobo, cancellationToken);

            if (currentWalletBalance < actualDeductionKobo)
            {
                Console.WriteLine($"[REPAYMENT INSUFFICIENT] Borrower wallet lacks clear capital. Needed: {actualDeductionKobo}");
                return false;
            }

            // 2. Debit the Borrower
            var paymentDebitRecord = new Domain.Entities.Transaction
            {
                Id = Guid.NewGuid(),
                WalletId = borrowerWallet.Id,
                AmountKobo = actualDeductionKobo,
                Type = "DEBIT",
                Description = $"Loan Repayment Settlement - Asset Reference ID: {loan.Id}",
                Timestamp = DateTime.UtcNow
            };
            await _context.Transactions.AddAsync(paymentDebitRecord, cancellationToken);

            // 3. ──► NEW: Yield Distribution to Fractional Lenders
            var allocations = await _context.LoanAllocations.Where(a => a.LoanId == loanId).ToListAsync(cancellationToken);
            
            foreach (var allocation in allocations)
            {
                // Calculate proportional share
                double sharePercentage = (double)allocation.AllocatedAmountKobo / loan.PrincipalAmountKobo;
                long grossLenderShareKobo = (long)(actualDeductionKobo * sharePercentage);
                
                // Demlo Revenue: 5% flat platform fee on all capital returned
                long platformFeeKobo = (long)(grossLenderShareKobo * 0.05);
                long netLenderShareKobo = grossLenderShareKobo - platformFeeKobo;

                var lenderWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == allocation.LenderId, cancellationToken);
                if (lenderWallet != null && netLenderShareKobo > 0)
                {
                    // Credit the Lender's internal ledger
                    var lenderCredit = new Domain.Entities.Transaction
                    {
                        Id = Guid.NewGuid(),
                        WalletId = lenderWallet.Id,
                        AmountKobo = netLenderShareKobo,
                        Type = "CREDIT",
                        Description = $"Yield Distribution - Loan Ref: {loan.Id}",
                        Timestamp = DateTime.UtcNow
                    };
                    await _context.Transactions.AddAsync(lenderCredit, cancellationToken);

                    // Update the Lender's live portfolio metrics
                    var lenderProfile = await _context.LenderProfiles.FirstOrDefaultAsync(p => p.UserId == allocation.LenderId, cancellationToken);
                    if (lenderProfile != null)
                    {
                        lenderProfile.AvailableBalanceKobo += netLenderShareKobo;
                        lenderProfile.TotalEarnedKobo += netLenderShareKobo; 
                        _context.LenderProfiles.Update(lenderProfile);
                    }
                }
            }

            // 4. Resolve State Machine Transitions
            bool isFullySettled = (actualDeductionKobo == exactRemainingDebt);
            if (isFullySettled)
            {
                Console.WriteLine($"[REPAYMENT MATCH] Debt completely zeroed out. Closing down contract pipeline context.");
                await _loanService.UpdateLoanStatusAsync(loanId, LoanStatus.ClosedRepaid, cancellationToken);
                
                // Update allocation statuses
                foreach(var allocation in allocations)
                {
                    allocation.Status = "SETTLED";
                    _context.LoanAllocations.Update(allocation);
                }
            }

            // 5. Commit all moving parts to the database atomically
            await _context.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
            Console.WriteLine($"[REPAYMENT SUCCESS] Liquidated {actualDeductionKobo} Kobo against Asset {loanId}. Yield distributed.");

            // 6. Automated Receipt Milestone Alerts
            double repaidInNaira = actualDeductionKobo / 100.0;
            long postPaymentDebt = isFullySettled ? 0 : (exactRemainingDebt - actualDeductionKobo);
            double remainingInNaira = postPaymentDebt / 100.0;

            string smsPayloadText = isFullySettled
                ? $"Demlo Alert: Repayment of NGN {repaidInNaira:N2} received! Your loan (ID: {loanId.ToString()[..8]}) is now FULLY SETTLED. Thank you!"
                : $"Demlo Alert: Repayment of NGN {repaidInNaira:N2} received. Outstanding balance: NGN {remainingInNaira:N2}.";

            _ = _notificationService.SendSmsAsync(user.PhoneNumber ?? "+2348000000000", smsPayloadText, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            Console.WriteLine($"[REPAYMENT FAILURE] Settle handler tracking encountered internal faults. Rolled back: {ex.Message}");
            return false;
        }
    }
}